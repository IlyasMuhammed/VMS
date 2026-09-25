using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>The currency master (FSD §13A). PKR is seeded as the tenant's base currency the first time any of
/// this is touched (<see cref="Services.CurrencySeeder"/>), the same lazy-per-tenant idiom every other master
/// list in this repo uses.</summary>
internal sealed class Currency : ITenantScopedEntity, IAuditRooted
{
    public int CurrencyId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("Currency", CurrencyId.ToString());

    /// <summary>ISO 4217, e.g. PKR, USD, AED. Immutable once created (BR: a currency's code never changes).</summary>
    public string CurrencyCode { get; set; } = string.Empty;
    public string CurrencyName { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public byte DecimalPlaces { get; set; } = 2;

    /// <summary>Mirrors <see cref="CurrencySettings.BaseCurrencyCode"/> — kept in step whenever the tenant setting
    /// changes (exactly one row per tenant may hold this, enforced by a filtered unique index), so a currency list
    /// can show which one is the base without a join to the settings row.</summary>
    public bool IsBase { get; set; }

    public string Status { get; set; } = CurrencyStatuses.Active;
}

public static class CurrencyStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public static readonly IReadOnlyList<string> All = [Active, Inactive];
}

/// <summary>One tenant's currency policy (FSD §13A): PKR-only by default, or multi-currency with a chosen base.
/// Exactly one row per tenant, lazily created the same way (<see cref="Services.CurrencySeeder"/>).</summary>
internal sealed class CurrencySettings : ITenantScopedEntity, IAuditRooted
{
    public int CurrencySettingsId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("CurrencySettings", CurrencySettingsId.ToString());

    public string BaseCurrencyCode { get; set; } = "PKR";
    public bool MultiCurrencyEnabled { get; set; }
}

/// <summary>An effective-dated rate from one currency to the tenant's base currency (FSD §13A, Recommended Design
/// — needed only when multi-currency is on). Every foreign-currency transaction later in this module stores its
/// own <c>CurrencyCode</c>/<c>ExchangeRate</c>/<c>BaseAmount</c> at entry, so this table is read at save time, never
/// re-joined to reprice history.</summary>
internal sealed class ExchangeRate : ITenantScopedEntity, IAuditRooted
{
    public int ExchangeRateId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("ExchangeRate", ExchangeRateId.ToString());

    public string FromCurrencyCode { get; set; } = string.Empty;
    public string ToCurrencyCode { get; set; } = string.Empty;
    public DateOnly RateDate { get; set; }
    public decimal Rate { get; set; }
}
