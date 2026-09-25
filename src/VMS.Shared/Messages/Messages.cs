using System.Globalization;
using System.Text.RegularExpressions;
using VMS.Shared.Exceptions;

namespace VMS.Shared.Messages;

/// <summary>
/// The message IDs of the FSD's catalogues (§12.2, §22.2), named by what they say. The text lives in
/// <c>messages.en.json</c>, not here, so it can be reworded or translated without a code change; code refers
/// only to the ID. <c>VAL-GEN-*</c> are the platform's own wording for the checks every form has.
/// </summary>
public static class Msg
{
    // Business Partner (§12.2)
    public const string BpRoleRequired = "VAL-BP-001";
    public const string BpCnicFormat = "VAL-BP-002";
    public const string BpCnicUsed = "VAL-BP-003";
    public const string BpNtnUsed = "VAL-BP-004";
    public const string BpMobileFormat = "VAL-BP-005";
    public const string BpSimilarName = "VAL-BP-006";
    public const string BpLicenceExpired = "VAL-BP-007";
    public const string BpLicenceNumberRequired = "VAL-BP-008";
    public const string BpLastRole = "VAL-BP-009";
    public const string BpRoleInUse = "VAL-BP-010";
    public const string BpOpeningBalanceDateRequired = "VAL-BP-011";
    public const string BpOpeningBalanceDateFuture = "VAL-BP-012";
    public const string BpEmailRequiredForCustomer = "VAL-BP-013";
    public const string BpDocumentExpiryRequired = "VAL-BP-014";
    public const string BpFileTooLarge = "VAL-BP-015";
    public const string BpDeactivationBlocked = "VAL-BP-016";

    // Vehicle (§22.2). VH-013, 017 and 018 end in a question: show them with a confirmation, not as an error.
    public const string VhRegNoRequired = "VAL-VH-001";
    public const string VhRegNoExists = "VAL-VH-002";
    public const string VhChassisOrEngineDuplicate = "VAL-VH-003";
    public const string VhCapacityRequired = "VAL-VH-004";
    public const string VhAcquisitionDateFuture = "VAL-VH-005";
    public const string VhCounterpartyRequired = "VAL-VH-006";
    public const string VhCounterpartyLacksRole = "VAL-VH-007";
    public const string VhPaidExceedsPrice = "VAL-VH-008";
    public const string VhShareRange = "VAL-VH-009";
    public const string VhEndBeforeStart = "VAL-VH-010";
    public const string VhInstallmentRequired = "VAL-VH-011";
    public const string VhDownPaymentMismatch = "VAL-VH-012";
    public const string VhFinanceNotReconciled = "VAL-VH-013";
    public const string VhRegistrationBookRequired = "VAL-VH-014";
    public const string VhItemBeforeAcquisition = "VAL-VH-015";
    public const string VhDocumentExpired = "VAL-VH-016";
    public const string VhDriverAlreadyAssigned = "VAL-VH-017";
    public const string VhFuelCardInUse = "VAL-VH-018";
    public const string VhCategoryLockedByFinance = "VAL-VH-019";
    public const string VhOdometerBelowOpening = "VAL-VH-020";
    public const string VhOdometerOutOfSequence = "VAL-VH-021";
    public const string VhWrongStatus = "VAL-VH-022";
    public const string VhFinanceNeedsBankLeased = "VAL-VH-023";
    public const string VhBankLeasedNeedsFinance = "VAL-VH-024";
    public const string VhBankIsAgreementBank = "VAL-VH-025";
    public const string VhInstallmentOrder = "VAL-VH-026";
    public const string VhAlreadyReversed = "VAL-VH-027";
    public const string VhReversalNotReversible = "VAL-VH-028";
    public const string VhReceiptAlreadyAttached = "VAL-VH-029";
    // §19A: recurring charges. The FSD's own text names VAL-VH-019 for BR-VH-029 (line 785), but that ID was already
    // given to VhCategoryLockedByFinance in Stage 2; VAL-VH-030 onward are new, not a clash with anything in §22.2.
    public const string VhBankInstallmentAlreadyFinanced = "VAL-VH-030";
    public const string VhEndDateOrOccurrences = "VAL-VH-031";
    public const string VhAutoPostNeedsFixedAmount = "VAL-VH-032";
    public const string VhEntryWrongStatus = "VAL-VH-033";

    // Documents (§23A). No VAL-DOC-* ids are given in the FSD's own §12.2/§22.2 catalogue; these are new.
    public const string DocAlreadyCurrent = "VAL-DOC-001";
    public const string DocNothingToRenew = "VAL-DOC-002";
    public const string DocTypeCodeInUse = "VAL-DOC-003";

    // Trip/Billing/Invoicing/Customer Ledger (second FSD). No VAL-TRP-* ids are given in that FSD's own catalogue
    // (it uses plain error codes like RATE_MISSING instead — see BusinessRuleException); these are new, for the
    // form-validation problems this module raises the same way every other module raises theirs.
    public const string TrpCurrencyCodeUsed = "VAL-TRP-001";
    public const string TrpCurrencyCodeInvalid = "VAL-TRP-002";
    public const string TrpBaseCurrencyUnknown = "VAL-TRP-003";
    public const string TrpBaseCurrencyInactive = "VAL-TRP-004";
    public const string TrpBaseCurrencyChangeBlocked = "VAL-TRP-005";
    public const string TrpMultiCurrencyDisableBlocked = "VAL-TRP-006";
    public const string TrpExchangeRateCurrencyUnknown = "VAL-TRP-007";
    public const string TrpExchangeRateToMustBeBase = "VAL-TRP-008";
    public const string TrpExchangeRateDuplicate = "VAL-TRP-009";
    public const string TrpTaxRuleOverlap = "VAL-TRP-010";
    public const string TrpRateOverlap = "VAL-TRP-011";

    // Platform: the checks every form has
    public const string Required = "VAL-GEN-001";
    public const string Email = "VAL-GEN-002";
    public const string MaxLength = "VAL-GEN-003";
    public const string MinLength = "VAL-GEN-004";
    public const string Format = "VAL-GEN-005";
    public const string Min = "VAL-GEN-006";
    public const string Max = "VAL-GEN-007";
    public const string NotFuture = "VAL-GEN-008";
    public const string Mismatch = "VAL-GEN-009";
    public const string Invalid = "VAL-GEN-010";
    public const string CorrectFields = "VAL-GEN-011";
    public const string OnlyOnePrimary = "VAL-GEN-012";
    public const string AlreadyUsed = "VAL-GEN-013";
    public const string ChangedByAnother = "VAL-GEN-014";
    public const string OneOf = "VAL-GEN-015";
    public const string FutureDate = "VAL-GEN-016";
    public const string ReadOnlyOnceSaved = "VAL-GEN-017";
    public const string OthersHaveThis = "VAL-GEN-018";
    public const string ListedTwice = "VAL-GEN-019";
    public const string ExportTooLarge = "VAL-GEN-020";
    public const string IdempotencyKeyRequired = "VAL-GEN-021";
}

/// <summary>Fills <c>{Name}</c> placeholders. Pure, so it is testable without the catalogue.</summary>
public static partial class MessageFormat
{
    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex Placeholder();

    /// <summary>The placeholder names in a template, in order, without repeats.</summary>
    public static IReadOnlyList<string> Placeholders(string template) =>
        Placeholder().Matches(template).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>
    /// Replaces each <c>{Name}</c> with its value. A placeholder with no value is a programming error and
    /// throws, because a message that shows "{BPCode}" to a user is worse than a failed test. Extra values are ignored.
    /// </summary>
    public static string Format(string template, IReadOnlyDictionary<string, string?> values)
    {
        var missing = Placeholders(template).Where(p => !values.ContainsKey(p)).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"No value for {string.Join(", ", missing.Select(m => "{" + m + "}"))} in \"{template}\".");

        return Placeholder().Replace(template, m => values[m.Groups[1].Value] ?? string.Empty);
    }

    public static Dictionary<string, string?> Values(IEnumerable<(string Name, object? Value)> values) =>
        values.ToDictionary(v => v.Name, v => Convert.ToString(v.Value, CultureInfo.InvariantCulture));
}

/// <summary>
/// Every message the API can show, by ID, in one or more languages. English is built in; a folder named by
/// <c>Messages:Directory</c> may hold <c>messages.en.json</c> (reword any message) and <c>messages.ur.json</c>
/// (a translation; anything it leaves out falls back to English). Edits are picked up without a restart.
/// </summary>
public interface IMessageCatalogue
{
    bool Has(string code);

    /// <summary>The message with its placeholders filled. Throws for an unknown ID or a missing value.</summary>
    string Text(string code, params (string Name, object? Value)[] values);

    string Text(string locale, string code, params (string Name, object? Value)[] values);

    /// <summary>Every message in a language (falling back to English for what it lacks), for the client to format itself.</summary>
    IReadOnlyDictionary<string, string> All(string locale = "en");

    /// <summary>Builds one field error from a catalogue message: the ID, the text, and the values it was filled with.</summary>
    ValidationError Error(string? field, string code, params (string Name, object? Value)[] values);
}
