using Microsoft.EntityFrameworkCore;
using VMS.Modules.Documents.Data;
using VMS.Modules.Documents.Domain;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Files;
using VMS.Shared.Messages;
using VMS.Shared.Partners;
using VMS.Shared.Time;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Documents.Services;

/// <summary>Who is acting, and which restricted values they may see or set.</summary>
public sealed record DocumentCaller(int UserId, string? UserName, bool IsSuperAdmin, IReadOnlySet<string> Permissions)
{
    public bool Has(string permission) => IsSuperAdmin || Permissions.Contains(permission);
}

/// <summary>What every document service shares: the database, the day it is for the company, and the owners a document points at.</summary>
internal sealed class DocumentContext(DocumentDbContext db, ITenantContext tenantContext, IOperatingClock clock, IPartnerDirectory partners, IVehicleDirectory vehicles, IMessageCatalogue messages)
{
    public DocumentDbContext Db => db;
    public IMessageCatalogue Messages => messages;
    public Guid Tenant => tenantContext.TenantId;

    public Task<DateOnly> TodayAsync() => clock.TodayAsync(Tenant);

    /// <summary>True if the owner exists in this tenant (a partner not deleted, a vehicle not deleted). What a document row may be filed against.</summary>
    public async Task<bool> OwnerExistsAsync(string ownerType, int ownerId) => ownerType switch
    {
        DocumentOwnerTypes.BusinessPartner => await partners.FindAsync(ownerId) is not null,
        DocumentOwnerTypes.Vehicle => await vehicles.FindAsync(ownerId) is not null,
        _ => false,
    };

    /// <summary>The active roles a Business Partner owner holds, for narrowing which types apply to it. Empty for a Vehicle owner.</summary>
    public async Task<IReadOnlySet<string>> OwnerRolesAsync(string ownerType, int ownerId)
    {
        if (ownerType != DocumentOwnerTypes.BusinessPartner) return new HashSet<string>();
        var partner = await partners.FindAsync(ownerId);
        return partner?.Roles.ToHashSet() ?? new HashSet<string>();
    }

    public async Task<string> OwnerDisplayNameAsync(string ownerType, int ownerId) => ownerType switch
    {
        DocumentOwnerTypes.BusinessPartner => (await partners.FindAsync(ownerId))?.DisplayName ?? $"Partner #{ownerId}",
        DocumentOwnerTypes.Vehicle => (await vehicles.FindAsync(ownerId))?.RegistrationNo ?? $"Vehicle #{ownerId}",
        _ => $"#{ownerId}",
    };

    public async Task<DocumentType> LoadTypeAsync(int documentTypeId) =>
        await db.Types.FirstOrDefaultAsync(t => t.TenantId == Tenant && t.DocumentTypeId == documentTypeId) ?? throw new NotFoundException("Document type not found.");

    public FileRules RulesFor(DocumentType type) => new((FileKinds)type.AllowedFormats, (long)type.MaxFileSizeMb * 1024 * 1024);

    public ValidationException Error(string field, string code, params (string Name, object? Value)[] values) => new(messages.Error(field, code, values));
}
