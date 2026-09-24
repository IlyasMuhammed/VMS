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

    public sealed record Definition(string Code, string Name, string Module, string Description, string Level = PermissionLevels.Operation);

    private const string Users = "User Management";
    private const string Roles = "Role Management";
    private const string Partners = "Business Partner";
    private const string Vehicles = "Vehicle";
    private const string Finance = "Financial";
    private const string Documents = "Documents";
    private const string Admin = "Administration";

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
    ];
}
