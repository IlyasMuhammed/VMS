using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Time;

namespace VMS.Modules.Trips.Services;

public interface ICustomerInvoiceTemplateService
{
    Task<IReadOnlyList<CustomerInvoiceTemplateModel>> ListAsync(int customerId, bool includeInactive, CancellationToken ct = default);

    /// <summary>
    /// FSD AC-07/AC-08's own selection rule ("Applicable = Active and effective on the Invoice Date"), exposed as
    /// its own read so the invoice-generation task (not built yet) can call it directly. Deliberately different
    /// from <see cref="ICustomerTaxRuleService.ResolveApplicableAsync"/>: a tax rule the FSD explicitly says "still
    /// applies to invoices dated inside its old range" even after being superseded, but §15 gives templates no such
    /// exception — a superseded template is never applicable again, even to a date inside its old effective range.
    /// "Re-printing an old invoice uses that version" (§15) is a plain lookup of the invoice's own stored
    /// <c>CustomerInvoiceTemplateId</c>/<c>TemplateVersion</c> once Invoice exists (a later CC task), not a
    /// re-resolution through this method.
    /// </summary>
    Task<ApplicableInvoiceTemplatesModel> ResolveApplicableAsync(int customerId, DateOnly invoiceDate, CancellationToken ct = default);

    Task<CustomerInvoiceTemplateModel> CreateAsync(int customerId, CreateCustomerInvoiceTemplateRequest request, CancellationToken ct = default);
    Task<CustomerInvoiceTemplateModel> NewVersionAsync(long templateId, NewInvoiceTemplateVersionRequest request, CancellationToken ct = default);
    Task<CustomerInvoiceTemplateModel> ActivateAsync(long templateId, ActivateCustomerInvoiceTemplateRequest request, CancellationToken ct = default);
    Task<CustomerInvoiceTemplateModel> SetDefaultAsync(long templateId, CancellationToken ct = default);
    Task<CustomerInvoiceTemplateModel> DeactivateAsync(long templateId, CancellationToken ct = default);
}

internal sealed class CustomerInvoiceTemplateService(TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IOperatingClock clock, ICustomerLookup customerLookup)
    : ICustomerInvoiceTemplateService
{
    public async Task<IReadOnlyList<CustomerInvoiceTemplateModel>> ListAsync(int customerId, bool includeInactive, CancellationToken ct = default)
    {
        await customerLookup.RequireAsync(customerId, ct);
        var query = db.CustomerInvoiceTemplates.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && t.CustomerId == customerId);
        if (!includeInactive) query = query.Where(t => t.Status != InvoiceTemplateStatuses.Inactive);
        return await query.OrderBy(t => t.TemplateName).ThenByDescending(t => t.Version).Select(t => ToModel(t)).ToListAsync(ct);
    }

    public async Task<ApplicableInvoiceTemplatesModel> ResolveApplicableAsync(int customerId, DateOnly invoiceDate, CancellationToken ct = default)
    {
        await customerLookup.RequireAsync(customerId, ct);
        var applicable = await db.CustomerInvoiceTemplates.AsNoTracking()
            .Where(t => t.TenantId == tenant.TenantId && t.CustomerId == customerId && t.Status == InvoiceTemplateStatuses.Active
                && t.EffectiveFrom <= invoiceDate && (t.EffectiveTo == null || t.EffectiveTo >= invoiceDate))
            .OrderByDescending(t => t.IsDefault).ThenBy(t => t.TemplateName).Select(t => ToModel(t)).ToListAsync(ct);
        return new ApplicableInvoiceTemplatesModel { Templates = applicable, RequiresSelection = applicable.Count > 1 };
    }

    public async Task<CustomerInvoiceTemplateModel> CreateAsync(int customerId, CreateCustomerInvoiceTemplateRequest request, CancellationToken ct = default)
    {
        await customerLookup.RequireAsync(customerId, ct);
        var (errors, type, reference) = ValidateTypeAndReference(request.TemplateType, request.TemplateReference);
        if (string.IsNullOrWhiteSpace(request.TemplateName)) errors.Add(messages.Error("templateName", Msg.Required, ("Field", "Template name")));
        if (errors.Count > 0) throw new ValidationException(errors);

        var name = request.TemplateName.Trim();
        var nextVersion = 1 + await db.CustomerInvoiceTemplates
            .Where(t => t.TenantId == tenant.TenantId && t.CustomerId == customerId && t.TemplateName == name)
            .Select(t => (int?)t.Version).MaxAsync(ct) ?? 1;

        var template = new CustomerInvoiceTemplate
        {
            CustomerId = customerId, TemplateName = name, Version = nextVersion, TemplateType = type, TemplateReference = reference,
            EffectiveFrom = await clock.TodayAsync(tenant.TenantId), Status = InvoiceTemplateStatuses.Draft
        };
        db.CustomerInvoiceTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return ToModel(template);
    }

    public async Task<CustomerInvoiceTemplateModel> NewVersionAsync(long templateId, NewInvoiceTemplateVersionRequest request, CancellationToken ct = default)
    {
        var existing = await Find(templateId, ct);
        var (errors, type, reference) = ValidateTypeAndReference(request.TemplateType ?? existing.TemplateType, request.TemplateReference);
        if (errors.Count > 0) throw new ValidationException(errors);

        var nextVersion = 1 + await db.CustomerInvoiceTemplates
            .Where(t => t.TenantId == tenant.TenantId && t.CustomerId == existing.CustomerId && t.TemplateName == existing.TemplateName)
            .Select(t => t.Version).MaxAsync(ct);

        var next = new CustomerInvoiceTemplate
        {
            CustomerId = existing.CustomerId, TemplateName = existing.TemplateName, Version = nextVersion, TemplateType = type,
            TemplateReference = reference, EffectiveFrom = await clock.TodayAsync(tenant.TenantId), Status = InvoiceTemplateStatuses.Draft
        };
        db.CustomerInvoiceTemplates.Add(next);
        await db.SaveChangesAsync(ct);
        return ToModel(next);
    }

    public async Task<CustomerInvoiceTemplateModel> ActivateAsync(long templateId, ActivateCustomerInvoiceTemplateRequest request, CancellationToken ct = default)
    {
        var template = await Find(templateId, ct);
        if (template.Status != InvoiceTemplateStatuses.Draft) throw new ConflictException($"A {template.Status} template cannot be activated; only a Draft can.");

        var effectiveFrom = request.EffectiveFrom ?? await clock.TodayAsync(tenant.TenantId);
        var current = await db.CustomerInvoiceTemplates.FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.CustomerId == template.CustomerId
            && t.TemplateName == template.TemplateName && t.Status == InvoiceTemplateStatuses.Active && t.EffectiveTo == null, ct);

        if (current is not null && effectiveFrom <= current.EffectiveFrom)
            throw new ValidationException(messages.Error("effectiveFrom", Msg.Min, ("Field", "Effective from"), ("Min", current.EffectiveFrom.AddDays(1).ToString("yyyy-MM-dd"))));

        await db.InTransactionAsync(async ct2 =>
        {
            if (current is not null)
            {
                current.EffectiveTo = effectiveFrom.AddDays(-1);
                current.Status = InvoiceTemplateStatuses.Inactive;
            }
            if (request.IsDefault)
                await db.CustomerInvoiceTemplates.Where(t => t.TenantId == tenant.TenantId && t.CustomerId == template.CustomerId && t.IsDefault)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDefault, false), ct2);

            template.Status = InvoiceTemplateStatuses.Active;
            template.EffectiveFrom = effectiveFrom;
            template.IsDefault = request.IsDefault;
            await db.SaveChangesAsync(ct2);
        }, ct);

        return ToModel(template);
    }

    public async Task<CustomerInvoiceTemplateModel> SetDefaultAsync(long templateId, CancellationToken ct = default)
    {
        var template = await Find(templateId, ct);
        if (template.Status != InvoiceTemplateStatuses.Active) throw new ConflictException("Only an Active template can be made the default.");
        await db.CustomerInvoiceTemplates.Where(t => t.TenantId == tenant.TenantId && t.CustomerId == template.CustomerId && t.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDefault, false), ct);
        template.IsDefault = true;
        await db.SaveChangesAsync(ct);
        return ToModel(template);
    }

    public async Task<CustomerInvoiceTemplateModel> DeactivateAsync(long templateId, CancellationToken ct = default)
    {
        var template = await Find(templateId, ct);
        if (template.Status == InvoiceTemplateStatuses.Inactive) throw new ConflictException("This template is already Inactive.");
        template.Status = InvoiceTemplateStatuses.Inactive;
        template.IsDefault = false;
        template.EffectiveTo ??= await clock.TodayAsync(tenant.TenantId);
        await db.SaveChangesAsync(ct);
        return ToModel(template);
    }

    private async Task<CustomerInvoiceTemplate> Find(long templateId, CancellationToken ct) =>
        await db.CustomerInvoiceTemplates.FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.CustomerInvoiceTemplateId == templateId, ct)
        ?? throw new NotFoundException($"Invoice template {templateId} was not found.");

    private (List<ValidationError> Errors, string Type, string Reference) ValidateTypeAndReference(string? requestedType, string? requestedReference)
    {
        var errors = new List<ValidationError>();
        var type = string.IsNullOrWhiteSpace(requestedType) ? InvoiceTemplateTypes.SystemStandard : requestedType;
        if (!InvoiceTemplateTypes.All.Contains(type))
            errors.Add(messages.Error("templateType", Msg.OneOf, ("Field", "Template type"), ("Allowed", string.Join(", ", InvoiceTemplateTypes.All))));

        // SystemStandard needs no per-customer file — CC-27 renders it the same way for everyone. Every other
        // type is "not fixed now" (§15) and needs a real reference once one is chosen for a customer (pending Q2).
        var reference = type == InvoiceTemplateTypes.SystemStandard && string.IsNullOrWhiteSpace(requestedReference)
            ? InvoiceTemplateTypes.SystemStandard : requestedReference ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reference)) errors.Add(messages.Error("templateReference", Msg.Required, ("Field", "Template reference")));

        return (errors, type, reference);
    }

    private static CustomerInvoiceTemplateModel ToModel(CustomerInvoiceTemplate t) => new()
    {
        CustomerInvoiceTemplateId = t.CustomerInvoiceTemplateId, CustomerId = t.CustomerId, TemplateName = t.TemplateName,
        Version = t.Version, TemplateType = t.TemplateType, TemplateReference = t.TemplateReference, EffectiveFrom = t.EffectiveFrom,
        EffectiveTo = t.EffectiveTo, IsDefault = t.IsDefault, Status = t.Status
    };
}
