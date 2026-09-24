using System.Security.Claims;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.BusinessPartners.Data;
using VMS.Modules.BusinessPartners.Domain;
using VMS.Modules.BusinessPartners.Models;
using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Branches;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;
using VMS.Shared.Messages;
using VMS.Shared.Pagination;
using VMS.Shared.Time;

namespace VMS.Modules.BusinessPartners.Services;

public interface IPartnerQueryService
{
    Task<PaginatedResponse<PartnerListItem>> ListAsync(PartnerListQuery query);
    Task<List<PartnerPickerItem>> PickerAsync(string? role, string? search, int take);
    Task<(byte[] Content, string FileName)> ExportAsync(PartnerListQuery query, ClaimsPrincipal user);
    Task<PartnerHistory> HistoryAsync(int id, int page, int pageSize, ClaimsPrincipal user);
}

/// <summary>The partner list, the picker other screens use to choose a partner, and the Excel export (FSD §13.2, §13.3, §14).</summary>
internal sealed class PartnerQueryService(
    PartnerDbContext db,
    ITenantContext tenantContext,
    ILookupReader lookups,
    IBranchDirectory branches,
    ICallerScope scope,
    IMessageCatalogue messages,
    IClientTimeZone clientZone,
    TimeProvider time) : IPartnerQueryService
{
    public const int MinSearchLength = 3;
    public const int MaxPageSize = 100;
    public const int MaxExportRows = 50_000;

    private Guid Tenant => tenantContext.TenantId;

    // ── List ────────────────────────────────────────────────────────────────────────

    public async Task<PaginatedResponse<PartnerListItem>> ListAsync(PartnerListQuery query)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 1, MaxPageSize);

        var filtered = Filter(query);
        var total = await filtered.CountAsync();
        var rows = await Sort(filtered, query.Sort).Skip((page - 1) * size).Take(size)
            .Select(p => new PartnerListItem
            {
                Id = p.BusinessPartnerId, BpCode = p.BpCode, LegalName = p.LegalName, DisplayName = p.DisplayName ?? p.LegalName, PartyType = p.PartyType,
                CityId = p.CityId, PrimaryMobile = p.PrimaryMobile, Status = p.Status, BranchId = p.BranchId, ModifiedOn = p.ModifiedOn
            }).ToListAsync();

        await FillAsync(rows);
        return new PaginatedResponse<PartnerListItem> { Items = rows, TotalCount = total, Page = page, PageSize = size };
    }

    /// <summary>Roles, city and branch names, looked up for the page's rows only.</summary>
    private async Task FillAsync(List<PartnerListItem> rows)
    {
        if (rows.Count == 0) return;
        var ids = rows.Select(r => r.Id).ToList();
        var roles = (await db.Roles.AsNoTracking().Where(r => r.TenantId == Tenant && ids.Contains(r.BusinessPartnerId) && r.IsActive)
            .Select(r => new { r.BusinessPartnerId, r.RoleCode }).ToListAsync()).ToLookup(r => r.BusinessPartnerId, r => r.RoleCode);
        var cities = await lookups.FindManyAsync(PlatformLookups.City, rows.Select(r => r.CityId));
        var branchInfo = await branches.FindManyAsync(rows.Where(r => r.BranchId is not null).Select(r => r.BranchId!.Value));
        foreach (var row in rows)
        {
            row.Roles = roles[row.Id].Order().ToList();
            row.City = cities.GetValueOrDefault(row.CityId)?.Description;
            row.Branch = row.BranchId is { } b ? branchInfo.GetValueOrDefault(b)?.Name : null;
        }
    }

    private IQueryable<BusinessPartner> Filter(PartnerListQuery q)
    {
        // The tenant is named outright: a Super Admin's requests bypass the query filter, and must still see one tenant's data.
        var query = db.Partners.AsNoTracking().Where(p => p.TenantId == Tenant && !p.IsDeleted);
        query = ApplyScope(query);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            if (term.Length < MinSearchLength)
                throw new ValidationException(messages.Error("search", Msg.MinLength, ("Field", "Search"), ("Min", MinSearchLength)));
            query = query.Where(SearchPredicate(term));
        }

        if (q.Roles is { Count: > 0 })
        {
            var roles = q.Roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList();
            query = query.Where(p => p.Roles.Any(r => r.IsActive && roles.Contains(r.RoleCode)));
        }
        if (!string.IsNullOrWhiteSpace(q.Status)) { var status = q.Status.Trim(); query = query.Where(p => p.Status == status); }
        if (q.CityId is { } city) query = query.Where(p => p.CityId == city);
        if (q.BranchId is { } branch) query = query.Where(p => p.BranchId == branch);
        if (!string.IsNullOrWhiteSpace(q.PartyType)) { var type = q.PartyType.Trim(); query = query.Where(p => p.PartyType == type); }
        return query;
    }

    /// <summary>
    /// §23B.4: Own branch filters to the caller's branch; Own records, and Own vehicles (a partner list has nothing to
    /// filter by "vehicle", so it falls back to the same rule) filter to what the caller themselves created.
    /// </summary>
    private IQueryable<BusinessPartner> ApplyScope(IQueryable<BusinessPartner> query) => scope.ScopeType switch
    {
        ScopeTypes.OwnBranch => query.Where(p => p.BranchId == scope.BranchId),
        ScopeTypes.OwnRecords or ScopeTypes.OwnVehicles => query.Where(p => p.CreatedBy == scope.UserId),
        _ => query,
    };

    /// <summary>
    /// Partial match on code, names, CNIC, NTN and mobile (dashes ignored), and on a legal name the partner used to have
    /// (BR-BP-019): a customer who changed their name is still found by the old one.
    /// </summary>
    private System.Linq.Expressions.Expression<Func<BusinessPartner, bool>> SearchPredicate(string term)
    {
        var digits = new string(term.Where(char.IsDigit).ToArray());
        var digitsOnly = digits.Length >= MinSearchLength && digits.Length == term.Replace("-", "").Replace(" ", "").Length;
        var tenant = Tenant;

        return p =>
            p.BpCode.Contains(term) || p.LegalName.Contains(term) || (p.DisplayName != null && p.DisplayName.Contains(term))
            || (p.Cnic != null && p.Cnic.Contains(term)) || (p.Ntn != null && p.Ntn.Contains(term))
            || p.PrimaryMobile.Contains(term)
            || (digitsOnly && ((p.Cnic != null && p.Cnic.Replace("-", "").Contains(digits))
                               || (p.Ntn != null && p.Ntn.Replace("-", "").Contains(digits))
                               || p.PrimaryMobile.Replace("-", "").Contains(digits)))
            || db.Set<AuditEntry>().Any(a => a.TenantId == tenant && a.Entity == nameof(BusinessPartner) && a.Field == nameof(BusinessPartner.LegalName)
                                             && a.RecordId == p.BusinessPartnerId.ToString() && a.OldValue != null && a.OldValue.Contains(term));
    }

    private static IQueryable<BusinessPartner> Sort(IQueryable<BusinessPartner> q, string? sort)
    {
        var parts = (sort ?? "modifiedOn,desc").Split(',', 2, StringSplitOptions.TrimEntries);
        var descending = parts.Length < 2 ? parts[0].Equals("modifiedOn", StringComparison.OrdinalIgnoreCase) : !parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase);

        // Only these: a client cannot sort on a column that is not indexed, nor probe one it may not see.
        return parts[0].ToLowerInvariant() switch
        {
            "bpcode" => descending ? q.OrderByDescending(p => p.BpCode) : q.OrderBy(p => p.BpCode),
            "legalname" => descending ? q.OrderByDescending(p => p.LegalName).ThenBy(p => p.BpCode) : q.OrderBy(p => p.LegalName).ThenBy(p => p.BpCode),
            "status" => descending ? q.OrderByDescending(p => p.Status).ThenBy(p => p.BpCode) : q.OrderBy(p => p.Status).ThenBy(p => p.BpCode),
            "city" => descending ? q.OrderByDescending(p => p.CityId).ThenBy(p => p.BpCode) : q.OrderBy(p => p.CityId).ThenBy(p => p.BpCode),
            _ => descending ? q.OrderByDescending(p => p.ModifiedOn).ThenByDescending(p => p.BusinessPartnerId) : q.OrderBy(p => p.ModifiedOn).ThenBy(p => p.BusinessPartnerId)
        };
    }

    // ── Picker ──────────────────────────────────────────────────────────────────────

    /// <summary>Partners another screen can pick (a driver for a vehicle, a bank for a lease): Active only, in the role asked for (BR-BP-020).</summary>
    public async Task<List<PartnerPickerItem>> PickerAsync(string? role, string? search, int take)
    {
        var query = db.Partners.AsNoTracking().Where(p => p.TenantId == Tenant && !p.IsDeleted && p.Status == PartnerStatuses.Active);
        query = ApplyScope(query);
        if (!string.IsNullOrWhiteSpace(role)) { var r = role.Trim(); query = query.Where(p => p.Roles.Any(x => x.IsActive && x.RoleCode == r)); }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.BpCode.Contains(term) || p.LegalName.Contains(term) || (p.DisplayName != null && p.DisplayName.Contains(term)) || p.PrimaryMobile.Contains(term));
        }

        var rows = await query.OrderBy(p => p.DisplayName ?? p.LegalName).ThenBy(p => p.BpCode).Take(Math.Clamp(take <= 0 ? 50 : take, 1, 200))
            .Select(p => new PartnerPickerItem { Id = p.BusinessPartnerId, BpCode = p.BpCode, DisplayName = p.DisplayName ?? p.LegalName, LegalName = p.LegalName, CityId = p.CityId })
            .ToListAsync();
        var cities = await lookups.FindManyAsync(PlatformLookups.City, rows.Select(r => r.CityId));
        foreach (var row in rows) row.City = cities.GetValueOrDefault(row.CityId)?.Description;
        return rows;
    }

    // ── History ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every change to the partner and to what belongs to it, newest first, and the role and status logs. A value the caller
    /// may not see (a salary the audit row is tagged with a permission for) is left out and the row marked <c>restricted</c> (BR-SEC-003).
    /// </summary>
    public async Task<PartnerHistory> HistoryAsync(int id, int page, int pageSize, ClaimsPrincipal user)
    {
        if (!await db.Partners.AsNoTracking().AnyAsync(p => p.TenantId == Tenant && !p.IsDeleted && p.BusinessPartnerId == id))
            throw new NotFoundException("Partner not found.");

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 50 : pageSize, 1, MaxPageSize);
        var root = id.ToString();

        var changes = db.Set<AuditEntry>().AsNoTracking()
            .Where(a => a.TenantId == Tenant && a.RootEntity == nameof(BusinessPartner) && a.RootRecordId == root
                        // The role log below says who added or removed a role and why, so its own audit rows would only repeat it.
                        && a.Entity != nameof(BusinessPartnerRole)
                        // A value that starts at nothing (0, None, off) says nothing when the record is created.
                        && !(a.Action == AuditActions.Created && a.Field != null && (a.NewValue == null || a.NewValue == "" || a.NewValue == "0" || a.NewValue == "None" || a.NewValue == "false")));
        var total = await changes.CountAsync();
        var rows = await changes.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.AuditEntryID)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var items = rows.Select(a =>
        {
            var hidden = a.RequiredPermission is { } permission && !user.HasPermission(permission);
            return new HistoryChange
            {
                Id = a.AuditEntryID, OccurredAt = a.OccurredAt, UserName = a.UserName, Entity = a.Entity, RecordId = a.RecordId,
                Action = a.Action, Field = a.Field, Reason = a.Reason, Restricted = hidden,
                OldValue = hidden ? null : a.OldValue, NewValue = hidden ? null : a.NewValue
            };
        }).ToList();

        var roles = await db.RoleLog.AsNoTracking().Where(r => r.TenantId == Tenant && r.BusinessPartnerId == id)
            .OrderByDescending(r => r.OccurredOn).ThenByDescending(r => r.BpRoleLogId)
            .Select(r => new RoleLogItem { RoleCode = r.RoleCode, Action = r.Action, EffectiveDate = r.EffectiveDate, Reason = r.Reason, UserName = r.UserName, OccurredOn = r.OccurredOn })
            .ToListAsync();
        var statuses = await db.StatusLog.AsNoTracking().Where(s => s.TenantId == Tenant && s.BusinessPartnerId == id)
            .OrderByDescending(s => s.OccurredOn).ThenByDescending(s => s.BpStatusLogId)
            .Select(s => new StatusLogItem { FromStatus = s.FromStatus, ToStatus = s.ToStatus, Reason = s.Reason, EffectiveDate = s.EffectiveDate, UserName = s.UserName, OccurredOn = s.OccurredOn })
            .ToListAsync();

        return new PartnerHistory
        {
            Changes = new PaginatedResponse<HistoryChange> { Items = items, TotalCount = total, Page = page, PageSize = pageSize },
            Roles = roles,
            Statuses = statuses
        };
    }

    // ── Export ──────────────────────────────────────────────────────────────────────

    /// <summary>What a row of the export holds. Restricted columns follow the same field permissions as the screen (BR-SEC-003).</summary>
    internal sealed class ExportRow
    {
        public string BpCode { get; set; } = string.Empty;
        public string LegalName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string PartyType { get; set; } = string.Empty;
        public string Roles { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string PrimaryMobile { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Cnic { get; set; } = string.Empty;
        public string Ntn { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        [FieldPermission(PermissionCodes.BP_FIELD_OPENING_VIEW)] public decimal? OpeningBalance { get; set; }
        public DateTime ModifiedOn { get; set; }
    }

    private static readonly (string Header, string Property)[] Columns =
    [
        ("BP code", nameof(ExportRow.BpCode)), ("Legal name", nameof(ExportRow.LegalName)), ("Display name", nameof(ExportRow.DisplayName)),
        ("Party type", nameof(ExportRow.PartyType)), ("Roles", nameof(ExportRow.Roles)), ("City", nameof(ExportRow.City)), ("Branch", nameof(ExportRow.Branch)),
        ("Mobile", nameof(ExportRow.PrimaryMobile)), ("Email", nameof(ExportRow.Email)), ("CNIC", nameof(ExportRow.Cnic)), ("NTN", nameof(ExportRow.Ntn)),
        ("Status", nameof(ExportRow.Status)), ("Opening balance", nameof(ExportRow.OpeningBalance)), ("Last modified", nameof(ExportRow.ModifiedOn))
    ];

    /// <summary>The filtered list as an Excel file: everything the filter matches, not just the page on screen.</summary>
    public async Task<(byte[] Content, string FileName)> ExportAsync(PartnerListQuery query, ClaimsPrincipal user)
    {
        var filtered = Filter(query);
        var total = await filtered.CountAsync();
        if (total > MaxExportRows) throw new ValidationException(messages.Error("", Msg.ExportTooLarge, ("n", total), ("Max", MaxExportRows)));

        var partners = await Sort(filtered, query.Sort).Select(p => new
        {
            p.BusinessPartnerId, p.BpCode, p.LegalName, p.DisplayName, p.PartyType, p.CityId, p.BranchId, p.PrimaryMobile, p.Email, p.Cnic, p.Ntn,
            p.Status, p.OpeningBalance, p.ModifiedOn
        }).ToListAsync();

        var ids = partners.Select(p => p.BusinessPartnerId).ToList();
        var roles = ids.Count == 0 ? Enumerable.Empty<(int, string)>().ToLookup(x => x.Item1, x => x.Item2)
            : (await db.Roles.AsNoTracking().Where(r => r.TenantId == Tenant && r.IsActive).Select(r => new { r.BusinessPartnerId, r.RoleCode }).ToListAsync())
                .ToLookup(r => r.BusinessPartnerId, r => r.RoleCode);
        var cities = await lookups.FindManyAsync(PlatformLookups.City, partners.Select(p => p.CityId).Distinct());
        var branchInfo = await branches.FindManyAsync(partners.Where(p => p.BranchId is not null).Select(p => p.BranchId!.Value).Distinct());

        var rows = partners.Select(p => new ExportRow
        {
            BpCode = p.BpCode, LegalName = p.LegalName, DisplayName = p.DisplayName ?? p.LegalName, PartyType = p.PartyType,
            Roles = string.Join(", ", roles[p.BusinessPartnerId].Order()), City = cities.GetValueOrDefault(p.CityId)?.Description ?? string.Empty,
            Branch = (p.BranchId is { } b ? branchInfo.GetValueOrDefault(b)?.Name : null) ?? string.Empty,
            PrimaryMobile = p.PrimaryMobile, Email = p.Email ?? string.Empty, Cnic = p.Cnic ?? string.Empty, Ntn = p.Ntn ?? string.Empty,
            Status = p.Status, OpeningBalance = p.OpeningBalance, ModifiedOn = p.ModifiedOn
        }).ToList();

        var hidden = FieldPermissionRules.HiddenProperties(typeof(ExportRow), user).Select(h => h.Name).ToHashSet();
        var columns = Columns.Where(c => !hidden.Contains(c.Property)).ToList();
        var zone = clientZone.Current ?? TimeZoneInfo.Utc;

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Business partners");
        for (var c = 0; c < columns.Count; c++)
        {
            var header = columns[c].Property == nameof(ExportRow.ModifiedOn) ? $"{columns[c].Header} ({zone.Id})" : columns[c].Header;
            sheet.Cell(1, c + 1).Value = header;
        }
        sheet.Row(1).Style.Font.Bold = true;

        var properties = typeof(ExportRow).GetProperties().ToDictionary(p => p.Name);
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < columns.Count; c++)
            {
                var value = properties[columns[c].Property].GetValue(rows[r]);
                var cell = sheet.Cell(r + 2, c + 1);
                switch (value)
                {
                    case DateTime utc:
                        cell.Value = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
                        cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                        break;
                    case decimal amount:
                        cell.Value = amount;
                        cell.Style.NumberFormat.Format = "#,##0.00";
                        break;
                    case string text:
                        // A cell that starts with = + - or @ is a formula to a spreadsheet: keep names typed by users as plain text.
                        cell.SetValue(text);
                        cell.Style.NumberFormat.Format = "@";
                        break;
                }
            }
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var stamp = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).ToString("yyyyMMdd-HHmm");
        return (stream.ToArray(), $"business-partners-{stamp}.xlsx");
    }
}

