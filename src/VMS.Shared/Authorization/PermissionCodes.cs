namespace VMS.Shared.Authorization;

/// <summary>
/// How a permission is enforced (FSD §23B). Interface: whether a menu, screen or tab is reachable.
/// Operation: whether an action can be performed. Field: whether a value is returned at all.
/// </summary>
public static class PermissionLevels
{
    public const string Interface = "Interface";
    public const string Operation = "Operation";
    public const string Field = "Field";
}

/// <summary>
/// What records a user-role assignment reaches (FSD §23B.4). Public (not owned by the Auth module) so every
/// module that filters a list by scope — Partners, Vehicles, Finance — can read a caller's scope claims and
/// compare against it without depending on the Auth module itself.
/// </summary>
public static class ScopeTypes
{
    public const string AllBranches = "AllBranches";
    public const string OwnBranch = "OwnBranch";
    public const string OwnVehicles = "OwnVehicles";
    public const string OwnRecords = "OwnRecords";

    public static readonly IReadOnlyList<string> All = [AllBranches, OwnBranch, OwnVehicles, OwnRecords];

    /// <summary>Broadest to narrowest (the FSD's own table order): when a user holds several roles, the widest scope across them wins.</summary>
    private static readonly IReadOnlyDictionary<string, int> Breadth = new Dictionary<string, int> { [AllBranches] = 0, [OwnBranch] = 1, [OwnVehicles] = 2, [OwnRecords] = 3 };

    public static string Widest(IEnumerable<string> scopeTypes)
    {
        var widest = AllBranches;
        var best = int.MaxValue;
        foreach (var s in scopeTypes)
        {
            var rank = Breadth.GetValueOrDefault(s, int.MaxValue - 1);
            if (rank < best) { best = rank; widest = s; }
        }
        return widest;
    }
}

/// <summary>
/// Every permission a role can hold. Platform-level (cross-tenant) access is deliberately NOT a
/// permission: it is the <c>is_super_admin</c> claim, which comes from the SuperAdminUsers table
/// and cannot be granted by editing a role — otherwise a tenant admin could mint themselves a
/// platform-wide role.
///
/// The user and role permissions predate the FSD's catalogue and keep their names. They are the FSD's
/// <c>ADM.USER.MANAGE</c> and <c>ADM.ROLE.MANAGE</c>: <see cref="USER_MANAGE"/> and
/// <see cref="ROLE_MANAGE"/> are those permissions, so the catalogue has one way to do each thing.
/// </summary>
public static class PermissionCodes
{
    // ── User and role administration (FSD ADM.USER.MANAGE / ADM.ROLE.MANAGE) ──────────────────
    public const string USER_VIEW   = "USER_VIEW";
    public const string USER_MANAGE = "USER_MANAGE";
    public const string ROLE_VIEW   = "ROLE_VIEW";
    public const string ROLE_MANAGE = "ROLE_MANAGE";

    // ── Business Partner ─────────────────────────────────────────────────────────────────────
    public const string BP_VIEW                = "BP.VIEW";
    public const string BP_CREATE              = "BP.CREATE";
    public const string BP_EDIT                = "BP.EDIT";
    public const string BP_ROLE_MANAGE         = "BP.ROLE.MANAGE";
    public const string BP_STATUS_CHANGE       = "BP.STATUS.CHANGE";
    public const string BP_MERGE               = "BP.MERGE";
    public const string BP_DELETE              = "BP.DELETE";
    public const string BP_FIELD_SALARY_VIEW   = "BP.FIELD.SALARY.VIEW";
    public const string BP_FIELD_CREDIT_VIEW   = "BP.FIELD.CREDIT.VIEW";
    public const string BP_FIELD_OPENING_VIEW  = "BP.FIELD.OPENING.VIEW";
    public const string BP_EXPORT              = "BP.EXPORT";

    // ── Vehicle ──────────────────────────────────────────────────────────────────────────────
    public const string VEH_VIEW                = "VEH.VIEW";
    public const string VEH_CREATE              = "VEH.CREATE";
    public const string VEH_EDIT                = "VEH.EDIT";
    public const string VEH_ACQUISITION_EDIT    = "VEH.ACQUISITION.EDIT";
    public const string VEH_ACTIVATE            = "VEH.ACTIVATE";
    public const string VEH_CATEGORY_CHANGE     = "VEH.CATEGORY.CHANGE";
    public const string VEH_STATUS_CHANGE       = "VEH.STATUS.CHANGE";
    public const string VEH_DISPOSE             = "VEH.DISPOSE";
    public const string VEH_ITEM_MANAGE         = "VEH.ITEM.MANAGE";
    public const string VEH_DRIVER_ASSIGN       = "VEH.DRIVER.ASSIGN";
    public const string VEH_FIELD_COST_VIEW     = "VEH.FIELD.COST.VIEW";
    public const string VEH_FIELD_FINANCE_VIEW  = "VEH.FIELD.FINANCE.VIEW";
    public const string VEH_FIELD_PROFIT_VIEW   = "VEH.FIELD.PROFIT.VIEW";
    public const string VEH_EXPORT              = "VEH.EXPORT";

    // ── Financial operations ─────────────────────────────────────────────────────────────────
    public const string FIN_EXPENSE_CREATE      = "FIN.EXPENSE.CREATE";
    public const string FIN_EXPENSE_EDIT        = "FIN.EXPENSE.EDIT";
    public const string FIN_EXPENSE_APPROVE     = "FIN.EXPENSE.APPROVE";
    public const string FIN_INCOME_CREATE       = "FIN.INCOME.CREATE";
    public const string FIN_INCOME_EDIT         = "FIN.INCOME.EDIT";
    public const string FIN_INSTALLMENT_PAY     = "FIN.INSTALLMENT.PAY";
    public const string FIN_AGREEMENT_MANAGE    = "FIN.AGREEMENT.MANAGE";
    public const string FIN_RECURRING_MANAGE    = "FIN.RECURRING.MANAGE";
    public const string FIN_RECURRING_AUTOPOST  = "FIN.RECURRING.AUTOPOST";
    public const string FIN_DUE_CONFIRM         = "FIN.DUE.CONFIRM";
    public const string FIN_DUE_WAIVE           = "FIN.DUE.WAIVE";
    public const string FIN_ADJUSTMENT_POST     = "FIN.ADJUSTMENT.POST";
    public const string FIN_PNL_VIEW            = "FIN.PNL.VIEW";
    public const string FIN_REPORT_VIEW         = "FIN.REPORT.VIEW";

    // ── Documents ────────────────────────────────────────────────────────────────────────────
    public const string DOC_VIEW                = "DOC.VIEW";
    public const string DOC_UPLOAD              = "DOC.UPLOAD";
    public const string DOC_RENEW               = "DOC.RENEW";
    public const string DOC_DOWNLOAD            = "DOC.DOWNLOAD";
    public const string DOC_DOWNLOAD_SENSITIVE  = "DOC.DOWNLOAD.SENSITIVE";
    public const string DOC_REJECT              = "DOC.REJECT";
    public const string DOC_REGISTER_VIEW       = "DOC.REGISTER.VIEW";

    // ── Administration (ADM.USER.MANAGE and ADM.ROLE.MANAGE are USER_MANAGE and ROLE_MANAGE above) ──
    public const string ADM_MASTER_MANAGE       = "ADM.MASTER.MANAGE";
    public const string ADM_NOTIFICATION_MANAGE = "ADM.NOTIFICATION.MANAGE";
    public const string ADM_SERIES_MANAGE       = "ADM.SERIES.MANAGE";
    public const string ADM_AUDIT_VIEW          = "ADM.AUDIT.VIEW";
    public const string ADM_CONFIG_MANAGE       = "ADM.CONFIG.MANAGE";

    // ── Trips, Billing, Invoicing & Customer Ledger (second FSD; §44's dotted names mapped onto this catalogue's
    // own MODULE_ACTION convention, the same way the first FSD's ADM.USER.MANAGE/ADM.ROLE.MANAGE were) ──────────
    public const string TRP_CURRENCY_MANAGE     = "TRP.CURRENCY.MANAGE";
    public const string TRP_EXCHANGERATE_MANAGE = "TRP.EXCHANGERATE.MANAGE";
    public const string TRP_CUSTOMER_VIEW       = "TRP.CUSTOMER.VIEW";
    public const string TRP_CUSTOMER_EDIT       = "TRP.CUSTOMER.EDIT";
    public const string TRP_TAXRULE_EDIT        = "TRP.TAXRULE.EDIT";
    public const string TRP_TEMPLATE_EDIT       = "TRP.TEMPLATE.EDIT";
    public const string TRP_CITY_EDIT           = "TRP.CITY.EDIT";
    public const string TRP_ROUTE_EDIT          = "TRP.ROUTE.EDIT";
    public const string TRP_TRIPCONFIG_VIEW     = "TRP.TRIPCONFIG.VIEW";
    public const string TRP_TRIPCONFIG_EDIT     = "TRP.TRIPCONFIG.EDIT";
    public const string TRP_RATE_VIEW           = "TRP.RATE.VIEW";
    public const string TRP_RATE_CONFIGURE      = "TRP.RATE.CONFIGURE";
    public const string TRP_RATE_REPRICE        = "TRP.RATE.REPRICE";
    public const string TRP_TRIP_VIEW           = "TRP.TRIP.VIEW";
    public const string TRP_TRIP_CREATE         = "TRP.TRIP.CREATE";
    public const string TRP_TRIP_EDIT           = "TRP.TRIP.EDIT";
    public const string TRP_TRIP_STATUS         = "TRP.TRIP.STATUS";
    public const string TRP_TRIP_INACTIVATE     = "TRP.TRIP.INACTIVATE";
    public const string TRP_TRIP_SKIPSTATUS     = "TRP.TRIP.SKIPSTATUS";
    public const string TRP_TRIP_REOPEN         = "TRP.TRIP.REOPEN";
    public const string TRP_TRIP_DOCUMENTS      = "TRP.TRIP.DOCUMENTS";
    public const string TRP_TRIP_REVIEW         = "TRP.TRIP.REVIEW";
    public const string TRP_TRIP_OVERRIDE_DRIVER = "TRP.TRIP.OVERRIDEDRIVER";
    public const string TRP_POD_APPROVE         = "TRP.POD.APPROVE";
    public const string TRP_EXPENSE_EDIT        = "TRP.EXPENSE.EDIT";
    public const string TRP_EXPENSE_APPROVE     = "TRP.EXPENSE.APPROVE";
    public const string TRP_FUEL_EDIT           = "TRP.FUEL.EDIT";
    public const string TRP_INCOME_EDIT         = "TRP.INCOME.EDIT";
    public const string TRP_PNL_VIEW            = "TRP.PNL.VIEW";
    public const string TRP_FUELCARD_EDIT       = "TRP.FUELCARD.EDIT";
    public const string TRP_INVOICE_GENERATE    = "TRP.INVOICE.GENERATE";
    public const string TRP_INVOICE_VIEW        = "TRP.INVOICE.VIEW";
    public const string TRP_INVOICE_SUBMIT      = "TRP.INVOICE.SUBMIT";
    public const string TRP_INVOICE_CANCEL      = "TRP.INVOICE.CANCEL";
    public const string TRP_INVOICE_REGENERATE  = "TRP.INVOICE.REGENERATE";
    public const string TRP_INVOICE_RERENDER    = "TRP.INVOICE.RERENDER";
    public const string TRP_INVOICE_EVIDENCE_RETRY    = "TRP.INVOICE.EVIDENCE.RETRY";
    public const string TRP_INVOICE_EVIDENCE_RERENDER = "TRP.INVOICE.EVIDENCE.RERENDER";
    public const string TRP_BANKACCOUNT_MANAGE  = "TRP.BANKACCOUNT.MANAGE";
    public const string TRP_PAYMENT_CREATE      = "TRP.PAYMENT.CREATE";
    public const string TRP_PAYMENT_REVERSE     = "TRP.PAYMENT.REVERSE";
    public const string TRP_PAYMENT_CARRYFORWARD = "TRP.PAYMENT.CARRYFORWARD";
    public const string TRP_PAYMENT_REFUND      = "TRP.PAYMENT.REFUND";
    public const string TRP_PAYMENT_WRITEOFF    = "TRP.PAYMENT.WRITEOFF";
    public const string TRP_PAYMENT_DISCOUNT    = "TRP.PAYMENT.DISCOUNT";
    public const string TRP_PAYMENT_ADVANCE     = "TRP.PAYMENT.ADVANCE";
    public const string TRP_LEDGER_VIEW         = "TRP.LEDGER.VIEW";
    public const string TRP_LEDGER_OPENINGBALANCE = "TRP.LEDGER.OPENINGBALANCE";
    public const string TRP_LEDGER_PERIODLOCK   = "TRP.LEDGER.PERIODLOCK";
    public const string TRP_REPORT_VIEW         = "TRP.REPORT.VIEW";

    public sealed record Definition(string Code, string Name, string Module, string Description, string Level = PermissionLevels.Operation);

    private const string Users = "User Management";
    private const string Roles = "Role Management";
    private const string Partners = "Business Partner";
    private const string Vehicles = "Vehicle";
    private const string Finance = "Financial";
    private const string Documents = "Documents";
    private const string Admin = "Administration";
    private const string Trips = "Trips";
    private const string Billing = "Billing and Invoicing";
    private const string Ledger = "Customer Ledger";

    private const string I = PermissionLevels.Interface;
    private const string O = PermissionLevels.Operation;
    private const string F = PermissionLevels.Field;

    public static readonly IReadOnlyList<Definition> Catalog =
    [
        new(USER_VIEW,   "View users",   Users, "List and view users of the tenant.", I),
        new(USER_MANAGE, "Manage users", Users, "Create, edit, deactivate and delete users; assign roles and scope; reset passwords.", O),
        new(ROLE_VIEW,   "View roles",   Roles, "List roles and view their permissions.", I),
        new(ROLE_MANAGE, "Manage roles", Roles, "Create and edit roles and change their permissions.", O),

        new(BP_VIEW,               "View partners",                  Partners, "Partner list and partner screen.", I),
        new(BP_CREATE,             "Create partners",                Partners, "Create a business partner.", O),
        new(BP_EDIT,               "Edit partners",                  Partners, "Edit a partner's general and role details.", O),
        new(BP_ROLE_MANAGE,        "Add or remove partner roles",    Partners, "Add or remove roles on a partner.", O),
        new(BP_STATUS_CHANGE,      "Change partner status",          Partners, "Deactivate, reactivate or blacklist a partner.", O),
        new(BP_MERGE,              "Merge partners",                 Partners, "Merge duplicate partners into one.", O),
        new(BP_DELETE,             "Delete partners",                Partners, "Soft-delete an unused partner.", O),
        new(BP_FIELD_SALARY_VIEW,  "See driver salary",              Partners, "Driver salary, commission basis and commission value.", F),
        new(BP_FIELD_CREDIT_VIEW,  "See credit terms",               Partners, "Credit limit, credit days and payment terms.", F),
        new(BP_FIELD_OPENING_VIEW, "See opening balance",            Partners, "A partner's opening balance and its date.", F),
        new(BP_EXPORT,             "Export partners",                Partners, "Export the partner list.", O),

        new(VEH_VIEW,               "View vehicles",                 Vehicles, "Vehicle list and vehicle screen.", I),
        new(VEH_CREATE,             "Create vehicles",               Vehicles, "Create and save a Draft vehicle.", O),
        new(VEH_EDIT,               "Edit vehicles",                 Vehicles, "Edit identity, technical and operational fields.", O),
        new(VEH_ACQUISITION_EDIT,   "Enter acquisition and finance", Vehicles, "Purchase price, amount paid and the finance block.", O),
        new(VEH_ACTIVATE,           "Activate vehicles",             Vehicles, "Activate a Draft vehicle, writing its opening postings.", O),
        new(VEH_CATEGORY_CHANGE,    "Change ownership category",     Vehicles, "Close a relation and open a new one.", O),
        new(VEH_STATUS_CHANGE,      "Change vehicle status",         Vehicles, "Maintenance and temporarily unavailable.", O),
        new(VEH_DISPOSE,            "Retire, sell or transfer",      Vehicles, "Dispose of a vehicle.", O),
        new(VEH_ITEM_MANAGE,        "Manage attached items",         Vehicles, "Attach, detach and transfer attached items.", O),
        new(VEH_DRIVER_ASSIGN,      "Assign drivers",                Vehicles, "Set or release the default driver.", O),
        new(VEH_FIELD_COST_VIEW,    "See vehicle cost",              Vehicles, "Purchase price, amount paid, acquisition cost and item cost.", F),
        new(VEH_FIELD_FINANCE_VIEW, "See vehicle finance",           Vehicles, "Finance amount, installment schedule and outstanding.", F),
        new(VEH_FIELD_PROFIT_VIEW,  "See vehicle profit",            Vehicles, "Vehicle profit and loss, margin and income totals.", F),
        new(VEH_EXPORT,             "Export vehicles",               Vehicles, "Export the vehicle list.", O),

        new(FIN_EXPENSE_CREATE,     "Add expenses",                  Finance, "Add a major or operating expense.", O),
        new(FIN_EXPENSE_EDIT,       "Edit expenses",                 Finance, "Edit an unapproved expense.", O),
        new(FIN_EXPENSE_APPROVE,    "Approve expenses",              Finance, "Approve expenses above the configured threshold.", O),
        new(FIN_INCOME_CREATE,      "Add income",                    Finance, "Add vehicle income.", O),
        new(FIN_INCOME_EDIT,        "Edit income",                   Finance, "Edit unposted income.", O),
        new(FIN_INSTALLMENT_PAY,    "Record installment payments",   Finance, "Record a lease installment payment.", O),
        new(FIN_AGREEMENT_MANAGE,   "Manage finance agreements",     Finance, "Create, amend, close or settle a finance agreement.", O),
        new(FIN_RECURRING_MANAGE,   "Manage recurring charges",      Finance, "Configure recurring charges.", O),
        new(FIN_RECURRING_AUTOPOST, "Allow auto-post charges",       Finance, "Set a recurring charge to Auto-post. Admin only by default.", O),
        new(FIN_DUE_CONFIRM,        "Confirm due payments",          Finance, "Confirm a due entry, posting its transaction.", O),
        new(FIN_DUE_WAIVE,          "Waive due entries",             Finance, "Waive a due entry with a reason.", O),
        new(FIN_ADJUSTMENT_POST,    "Post adjustments",              Finance, "Post a correction or a reversal.", O),
        new(FIN_PNL_VIEW,           "View profit and loss",          Finance, "Trip, monthly and vehicle-to-date profit and loss.", I),
        new(FIN_REPORT_VIEW,        "View financial reports",        Finance, "Financial reports and exports.", I),

        new(DOC_VIEW,               "View documents",                Documents, "See document lists and metadata.", I),
        new(DOC_UPLOAD,             "Upload documents",              Documents, "Upload a new document.", O),
        new(DOC_RENEW,              "Renew documents",               Documents, "Upload a renewal, superseding the current version.", O),
        new(DOC_DOWNLOAD,           "Download documents",            Documents, "Download or preview a file.", O),
        new(DOC_DOWNLOAD_SENSITIVE, "Download identity documents",   Documents, "Download CNIC and licence scans.", O),
        new(DOC_REJECT,             "Reject documents",              Documents, "Mark a version rejected.", O),
        new(DOC_REGISTER_VIEW,      "View the document register",    Documents, "The fleet-wide register and the missing-documents report.", I),

        new(ADM_MASTER_MANAGE,       "Manage master data",           Admin, "Vehicle types, expense types, document types and other lookups.", O),
        new(ADM_NOTIFICATION_MANAGE, "Manage notification rules",    Admin, "Notification rules and lead days.", O),
        new(ADM_SERIES_MANAGE,       "Manage numbering series",      Admin, "Numbering series.", O),
        new(ADM_AUDIT_VIEW,          "View the audit log",           Admin, "The audit log viewer.", I),
        new(ADM_CONFIG_MANAGE,       "Manage system configuration",  Admin, "System configuration switches.", O),

        new(TRP_CURRENCY_MANAGE,     "Manage currency setup",         Trips, "The currency master and the tenant's multi-currency setting (§44: Admin only).", O),
        new(TRP_EXCHANGERATE_MANAGE, "Manage exchange rates",         Trips, "Exchange rates (§44: Admin, or Finance if granted).", O),
        new(TRP_CUSTOMER_VIEW,       "View customers",                Trips, "Customer list and customer screen.", I),
        new(TRP_CUSTOMER_EDIT,       "Edit customers",                Trips, "Create and edit a customer, its contacts, addresses and billing configuration.", O),
        new(TRP_TAXRULE_EDIT,        "Edit tax/deduction rules",      Trips, "A customer's tax and deduction rules.", O),
        new(TRP_TEMPLATE_EDIT,       "Edit invoice templates",        Trips, "A customer's invoice template registry.", O),
        new(TRP_CITY_EDIT,           "Edit cities",                   Trips, "The city master.", O),
        new(TRP_ROUTE_EDIT,          "Edit routes",                   Trips, "Routes and route stops.", O),
        new(TRP_TRIPCONFIG_VIEW,     "View trip configurations",      Trips, "§44: Admin/Fleet Manager edit, Operations/Finance view.", I),
        new(TRP_TRIPCONFIG_EDIT,     "Edit trip configurations",      Trips, "A customer's trip configurations, stops and allowed vehicles.", O),
        new(TRP_RATE_VIEW,           "View trip rates",               Trips, "§44: Admin, Finance, Fleet Manager and Read Only view; Operations sees only the amount on a trip.", I),
        new(TRP_RATE_CONFIGURE,      "Configure trip rates",          Trips, "Effective-dated trip rates.", O),
        new(TRP_RATE_REPRICE,        "Re-price trips",                Trips, "Resolve missing rates and re-price already-rated trips.", O),
        new(TRP_TRIP_VIEW,           "View trips",                    Trips, "Trip list and trip screen.", I),
        new(TRP_TRIP_CREATE,         "Create trips",                  Trips, "Create a fixed or open trip.", O),
        new(TRP_TRIP_EDIT,           "Edit trips",                    Trips, "Edit a trip before it is invoiced.", O),
        new(TRP_TRIP_STATUS,         "Change trip status",            Trips, "Trip lifecycle transitions, events and issues.", O),
        new(TRP_TRIP_INACTIVATE,     "Inactivate trips",              Trips, "Mark a trip inactive or reactivate it, with a reason.", O),
        new(TRP_TRIP_SKIPSTATUS,     "Skip lifecycle steps",          Trips, "Jump a trip's status past steps the transition table does not directly allow (§24, back-dated entry).", O),
        new(TRP_TRIP_REOPEN,         "Reopen completed trips",        Trips, "Move a Completed trip back to Delivered, if it is not on an active invoice.", O),
        new(TRP_TRIP_DOCUMENTS,      "Manage trip documents",         Trips, "Upload trip documents and proof of delivery.", O),
        new(TRP_TRIP_REVIEW,         "Review driver-created trips",   Trips, "Release or reject a trip a driver created in the app.", O),
        new(TRP_TRIP_OVERRIDE_DRIVER,"Override the assigned driver",  Trips, "Change a trip's driver away from the vehicle's own default (§20).", O),
        new(TRP_POD_APPROVE,         "Approve proof of delivery",     Trips, "Approve a trip's proof of delivery.", O),
        new(TRP_EXPENSE_EDIT,        "Edit trip expenses",            Trips, "Add or void a trip expense.", O),
        new(TRP_EXPENSE_APPROVE,     "Approve trip expenses",         Trips, "Approve a driver-app expense.", O),
        new(TRP_FUEL_EDIT,           "Edit trip fuel",                Trips, "Add or void trip fuel.", O),
        new(TRP_INCOME_EDIT,         "Edit trip income",              Trips, "Add trip income, billable or not.", O),
        new(TRP_PNL_VIEW,            "View trip P&L",                 Trips, "A trip's operational profit and loss.", I),
        new(TRP_FUELCARD_EDIT,       "Edit fuel cards",                Trips, "Fuel cards and their vehicle assignments.", O),

        new(TRP_INVOICE_GENERATE,    "Generate invoices",             Billing, "Search eligible trips and create an invoice.", O),
        new(TRP_INVOICE_VIEW,        "View invoices",                 Billing, "Invoice list, detail, lines and evidence.", I),
        new(TRP_INVOICE_SUBMIT,      "Submit invoices",               Billing, "Submit a Generated invoice.", O),
        new(TRP_INVOICE_CANCEL,      "Cancel invoices",               Billing, "Cancel a submitted invoice, with a reason.", O),
        new(TRP_INVOICE_REGENERATE,  "Regenerate invoices",           Billing, "Regenerate an invoice, transferring its payments.", O),
        new(TRP_INVOICE_RERENDER,    "Re-render invoice PDF",         Billing, "§34: re-render an invoice's PDF as a new version, without altering its data. Admin-only.", O),
        new(TRP_INVOICE_EVIDENCE_RETRY,    "Retry invoice evidence",  Billing, "§41: retry a failed invoice evidence generation. Finance.", O),
        new(TRP_INVOICE_EVIDENCE_RERENDER, "Re-render invoice evidence", Billing, "§41: re-render invoice evidence as a new version. Admin-only.", O),
        new(TRP_BANKACCOUNT_MANAGE,  "Manage bank accounts",          Billing, "§37: create and update the company's own bank accounts that receive customer payments.", O),
        new(TRP_PAYMENT_CREATE,      "Record payments",               Billing, "Record a customer receipt against one or more invoices.", O),
        new(TRP_PAYMENT_REVERSE,     "Reverse payments",               Billing, "Reverse a recorded payment, with a reason.", O),
        new(TRP_PAYMENT_CARRYFORWARD,"Carry forward credit",          Billing, "Carry a negative invoice balance forward to another invoice.", O),
        new(TRP_PAYMENT_REFUND,      "Refund credit",                 Billing, "Refund a customer credit or advance.", O),
        new(TRP_PAYMENT_WRITEOFF,    "Write off balances",            Billing, "Write off an invoice's remaining balance.", O),
        new(TRP_PAYMENT_DISCOUNT,    "Discount balances",             Billing, "Discount an invoice's remaining balance.", O),
        new(TRP_PAYMENT_ADVANCE,     "Record advances",               Billing, "Record, move or refund an advance against an Open trip.", O),

        new(TRP_LEDGER_VIEW,             "View the customer ledger",  Ledger, "Ledger statement, invoice ledger and balances.", I),
        new(TRP_LEDGER_OPENINGBALANCE,   "Post opening balances",     Ledger, "A customer's go-live opening balance.", O),
        new(TRP_LEDGER_PERIODLOCK,       "Lock ledger periods",       Ledger, "Close or reopen a ledger period.", O),
        new(TRP_REPORT_VIEW,             "View trip/billing reports", Ledger, "Every report and dashboard in this module (one permission covers all report codes, matching FIN.REPORT.VIEW/DOC.REGISTER.VIEW's existing generic pattern).", I),
    ];
}
