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

public interface ICustomerTaxRuleService
{
    Task<IReadOnlyList<CustomerTaxRuleModel>> ListAsync(int customerId, bool includeInactive, CancellationToken ct = default);

    /// <summary>Every rule active on a given invoice date (§14: "a rule is applied if it is active" — resolved by
    /// date, not by current status, so a superseded rule still applies to invoices dated in its old range).</summary>
    Task<IReadOnlyList<CustomerTaxRuleModel>> ResolveApplicableAsync(int customerId, DateOnly invoiceDate, CancellationToken ct = default);

    Task<CustomerTaxRuleModel> CreateAsync(int customerId, SaveCustomerTaxRuleRequest request, CancellationToken ct = default);

    /// <summary>Same as <see cref="CreateAsync"/>, entered from an existing rule's own row ("New Rate") rather than
    /// a blank form — <paramref name="ruleId"/> only confirms which rule the caller expects to replace.</summary>
    Task<CustomerTaxRuleModel> ReplaceAsync(long ruleId, SaveCustomerTaxRuleRequest request, CancellationToken ct = default);

    Task<CustomerTaxRuleModel> UpdateAsync(long ruleId, UpdateCustomerTaxRuleRequest request, CancellationToken ct = default);
}

internal sealed class CustomerTaxRuleService(TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IOperatingClock clock, ICustomerLookup customerLookup)
    : ICustomerTaxRuleService
{
    public async Task<IReadOnlyList<CustomerTaxRuleModel>> ListAsync(int customerId, bool includeInactive, CancellationToken ct = default)
    {
        await customerLookup.RequireAsync(customerId, ct);
        var query = db.CustomerTaxRules.AsNoTracking().Where(r => r.TenantId == tenant.TenantId && r.CustomerId == customerId);
        if (!includeInactive) query = query.Where(r => r.Status == ActiveInactiveStatuses.Active);
        return await query.OrderBy(r => r.TaxName).ThenBy(r => r.Sequence).Select(r => ToModel(r)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CustomerTaxRuleModel>> ResolveApplicableAsync(int customerId, DateOnly invoiceDate, CancellationToken ct = default)
    {
        await customerLookup.RequireAsync(customerId, ct);
        return await db.CustomerTaxRules.AsNoTracking()
            .Where(r => r.TenantId == tenant.TenantId && r.CustomerId == customerId && r.Applicable
                && r.EffectiveFrom <= invoiceDate && (r.EffectiveTo == null || r.EffectiveTo >= invoiceDate))
            .OrderBy(r => r.Sequence).Select(r => ToModel(r)).ToListAsync(ct);
    }

    public Task<CustomerTaxRuleModel> CreateAsync(int customerId, SaveCustomerTaxRuleRequest request, CancellationToken ct = default) =>
        SaveAsync(customerId, expectedOpenRuleId: null, request, ct);

    public async Task<CustomerTaxRuleModel> ReplaceAsync(long ruleId, SaveCustomerTaxRuleRequest request, CancellationToken ct = default)
    {
        var existing = await db.CustomerTaxRules.AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.CustomerTaxRuleId == ruleId, ct)
            ?? throw new NotFoundException($"Tax rule {ruleId} was not found.");
        return await SaveAsync(existing.CustomerId, expectedOpenRuleId: ruleId, request, ct);
    }

    public async Task<CustomerTaxRuleModel> UpdateAsync(long ruleId, UpdateCustomerTaxRuleRequest request, CancellationToken ct = default)
    {
        var rule = await db.CustomerTaxRules.FirstOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.CustomerTaxRuleId == ruleId, ct)
            ?? throw new NotFoundException($"Tax rule {ruleId} was not found.");

        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));
        var basis = string.IsNullOrWhiteSpace(request.CalculationBasis) ? rule.CalculationBasis : request.CalculationBasis;
        if (!CalculationBases.All.Contains(basis)) Add("calculationBasis", Msg.OneOf, ("Field", "Calculation basis"), ("Allowed", string.Join(", ", CalculationBases.All)));
        ValidateRate(rule.TaxType, request.TaxPercentage ?? rule.TaxPercentage, request.FixedAmount ?? rule.FixedAmount, Add);
        if (errors.Count > 0) throw new ValidationException(errors);

        // Identity fields (name/code/type/effective range) are not editable here — a rate change goes through
        // replace-with-confirmation instead, so the history it creates always has a reason (a new rule, not a
        // silently rewritten old one).
        rule.TaxPercentage = request.TaxPercentage ?? rule.TaxPercentage;
        rule.FixedAmount = request.FixedAmount ?? rule.FixedAmount;
        rule.Applicable = request.Applicable;
        rule.CalculationBasis = basis;
        rule.Sequence = request.Sequence;
        rule.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        await db.SaveChangesAsync(ct);
        return ToModel(rule);
    }

    private async Task<CustomerTaxRuleModel> SaveAsync(int customerId, long? expectedOpenRuleId, SaveCustomerTaxRuleRequest request, CancellationToken ct)
    {
        await customerLookup.RequireAsync(customerId, ct);
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (string.IsNullOrWhiteSpace(request.TaxName)) Add("taxName", Msg.Required, ("Field", "Tax name"));
        if (string.IsNullOrWhiteSpace(request.TaxCode)) Add("taxCode", Msg.Required, ("Field", "Tax code"));
        if (!TaxTypes.All.Contains(request.TaxType)) Add("taxType", Msg.OneOf, ("Field", "Tax type"), ("Allowed", string.Join(", ", TaxTypes.All)));
        if (!CalculationBases.All.Contains(request.CalculationBasis))
            Add("calculationBasis", Msg.OneOf, ("Field", "Calculation basis"), ("Allowed", string.Join(", ", CalculationBases.All)));
        ValidateRate(request.TaxType, request.TaxPercentage, request.FixedAmount, Add);
        if (errors.Count > 0) throw new ValidationException(errors);

        var name = request.TaxName.Trim();
        var nameKey = name.ToUpperInvariant();
        var code = request.TaxCode.Trim();
        var effectiveFrom = request.EffectiveFrom ?? await clock.TodayAsync(tenant.TenantId);

        var open = await db.CustomerTaxRules.FirstOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.CustomerId == customerId
            && r.TaxNameKey == nameKey && r.TaxCode == code && r.EffectiveTo == null, ct);

        if (expectedOpenRuleId is { } expected && open?.CustomerTaxRuleId != expected)
            throw new ConflictException("This rule is no longer the current version for this tax name; reload and try again.");

        if (open is not null)
        {
            if (effectiveFrom <= open.EffectiveFrom)
                throw new ValidationException(messages.Error("effectiveFrom", Msg.TrpTaxRuleOverlap));

            if (!request.ConfirmReplace)
                throw new BusinessRuleException("TAX_RULE_REPLACEMENT_CONFIRMATION",
                    $"{open.TaxName} {DescribeRate(open)} (effective {open.EffectiveFrom:dd-MMM-yyyy}) will be set Inactive from {effectiveFrom.AddDays(-1):dd-MMM-yyyy}. Continue?",
                    [new BusinessRuleDetail("effectiveFrom", open.CustomerTaxRuleId, "confirmReplace")]);
        }
        else
        {
            // No open rule of this name/code — still reject a range that falls inside a closed historical one.
            var overlapsHistory = await db.CustomerTaxRules.AnyAsync(r => r.TenantId == tenant.TenantId && r.CustomerId == customerId
                && r.TaxNameKey == nameKey && r.TaxCode == code && r.EffectiveFrom <= effectiveFrom && r.EffectiveTo != null && r.EffectiveTo >= effectiveFrom, ct);
            if (overlapsHistory) throw new ValidationException(messages.Error("effectiveFrom", Msg.TrpTaxRuleOverlap));
        }

        var rule = new CustomerTaxRule
        {
            CustomerId = customerId, TaxName = name, TaxNameKey = nameKey, TaxCode = code, TaxType = request.TaxType,
            TaxPercentage = request.TaxType == TaxTypes.Percentage ? request.TaxPercentage : null,
            FixedAmount = request.TaxType == TaxTypes.Fixed ? request.FixedAmount : null,
            Applicable = request.Applicable, CalculationBasis = request.CalculationBasis, Sequence = request.Sequence,
            EffectiveFrom = effectiveFrom, Status = ActiveInactiveStatuses.Active,
            SupersedesRuleId = open?.CustomerTaxRuleId, Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim()
        };

        await db.InTransactionAsync(async ct2 =>
        {
            if (open is not null)
            {
                // The old rule's CreatedOn never changes (there is no such column to touch); its Status/EffectiveTo
                // change is exactly the kind of update the ordinary audit trail already records.
                open.EffectiveTo = effectiveFrom.AddDays(-1);
                open.Status = ActiveInactiveStatuses.Inactive;
                db.CustomerTaxRules.Update(open);
            }
            db.CustomerTaxRules.Add(rule);
            await db.SaveChangesAsync(ct2);
        }, ct);

        return ToModel(rule);
    }

    private static void ValidateRate(string taxType, decimal? percentage, decimal? fixedAmount, Action<string, string, (string, object?)[]> add)
    {
        if (taxType == TaxTypes.Percentage)
        {
            if (percentage is null) add("taxPercentage", Msg.Required, [("Field", "Tax percentage")]);
            else if (percentage is <= 0 or > 100) add("taxPercentage", Msg.OneOf, [("Field", "Tax percentage"), ("Allowed", "0 < x ≤ 100")]);
        }
        else if (taxType == TaxTypes.Fixed)
        {
            if (fixedAmount is null) add("fixedAmount", Msg.Required, [("Field", "Fixed amount")]);
            else if (fixedAmount <= 0) add("fixedAmount", Msg.Min, [("Field", "Fixed amount"), ("Min", "0.01")]);
        }
    }

    private static string DescribeRate(CustomerTaxRule r) => r.TaxType == TaxTypes.Percentage ? $"{r.TaxPercentage:0.####}%" : $"Rs.{r.FixedAmount:N2}";

    private static CustomerTaxRuleModel ToModel(CustomerTaxRule r) => new()
    {
        CustomerTaxRuleId = r.CustomerTaxRuleId, CustomerId = r.CustomerId, TaxName = r.TaxName, TaxCode = r.TaxCode,
        TaxType = r.TaxType, TaxPercentage = r.TaxPercentage, FixedAmount = r.FixedAmount, Applicable = r.Applicable,
        CalculationBasis = r.CalculationBasis, Sequence = r.Sequence, EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo,
        Status = r.Status, SupersedesRuleId = r.SupersedesRuleId, Remarks = r.Remarks
    };
}
