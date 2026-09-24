using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Notifications;
using VMS.Shared.Numbering;
using VMS.Shared.Partners;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Vehicles.Services;

public interface IActivationService
{
    /// <summary>The schedule a Draft's saved agreement would generate. Empty when it has none.</summary>
    Task<List<ScheduleRowModel>> SchedulePreviewAsync(int vehicleId);
    /// <summary>What is missing before the Draft can be activated, and what activating would write. Writes nothing.</summary>
    Task<ActivationCheck> CheckAsync(int vehicleId, ActivateVehicleRequest request, VehicleCaller caller);
    Task<VehicleModel> ActivateAsync(int vehicleId, ActivateVehicleRequest request, VehicleCaller caller);
}

/// <summary>
/// Activating a Draft (FSD §21 step 5, FR-VH-012, BR-VH-012, BR-VH-019): the vehicle goes into the fleet, its ownership relation opens, its
/// lifecycle is logged, its ledger opens with the postings of §19, its bank agreement goes live with its installment schedule, and its default
/// driver is assigned. All of it is written in one transaction: if any part fails, nothing is (FSD §25).
/// </summary>
internal sealed class ActivationService(
    VehicleContext ctx, ICategoryService categories, IVehicleService vehicles, IAuditContext audit, IVehicleDocumentCheck documents, ItemService itemService,
    INotificationTrigger notifications) : IActivationService
{
    // ── Reading ─────────────────────────────────────────────────────────────────────

    public async Task<List<ScheduleRowModel>> SchedulePreviewAsync(int vehicleId)
    {
        await ctx.LoadReadOnlyAsync(vehicleId);
        var agreement = await ctx.Db.Agreements.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == ctx.Tenant && a.VehicleId == vehicleId && a.Status == AgreementStatuses.Draft);
        return agreement is null ? [] : Generate(agreement).Select(ToModel).ToList();
    }

    private static IReadOnlyList<ScheduleRow> Generate(VehicleFinanceAgreement a) =>
        ScheduleGenerator.Generate(a.FirstDueDate, a.Frequency, a.Tenure, a.InstallmentAmount, a.ResidualAmount);

    private static ScheduleRowModel ToModel(ScheduleRow r) => new() { InstallmentNo = r.InstallmentNo, DueDate = r.DueDate, Amount = r.Amount, IsResidual = r.IsResidual };

    // ── What activating needs, and would write ──────────────────────────────────────

    private sealed class Evaluation
    {
        public List<ChecklistItem> Items { get; } = [];
        public List<ValidationError> Errors { get; } = [];
        public VehicleAcquisition? Acquisition { get; set; }
        public VehicleFinanceAgreement? Agreement { get; set; }
        public VehicleRelation? Relation { get; set; }
        public List<VehicleAttachedItem> AttachedItems { get; } = [];
        public IReadOnlyList<PostingPlan> Postings { get; set; } = [];
        public IReadOnlyList<ScheduleRow> Schedule { get; set; } = [];
        public DateOnly? Acquired { get; set; }
    }

    private async Task<Evaluation> EvaluateAsync(Vehicle vehicle, ActivateVehicleRequest request, VehicleCaller caller)
    {
        var ev = new Evaluation();
        var today = await ctx.TodayAsync();
        var mayEnterFinance = caller.Has(PermissionCodes.VEH_FIELD_FINANCE_VIEW);
        ev.Acquisition = await ctx.Db.Acquisitions.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == ctx.Tenant && a.VehicleId == vehicle.VehicleId);
        ev.Agreement = await ctx.Db.Agreements.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == ctx.Tenant && a.VehicleId == vehicle.VehicleId && a.Status == AgreementStatuses.Draft);
        ev.Acquired = vehicle.AcquisitionDate;
        var category = request.Category ?? string.Empty;

        void Item(string code, string label, List<ValidationError> found, bool blocking = true, string? message = null)
        {
            ev.Items.Add(new ChecklistItem { Code = code, Label = label, Ok = found.Count == 0, Blocking = blocking, Message = found.Count == 0 ? message : string.Join(" ", found.Select(e => e.Message)) });
            if (blocking) ev.Errors.AddRange(found);
        }
        List<ValidationError> Errors() => [];
        ValidationError Error(string field, string code, params (string Name, object? Value)[] values) => ctx.Messages.Error(field, code, values);

        // 1. Category and counterparty (BR-VH-019). A bank lease takes its bank from the agreement when none was chosen.
        var details = request.Details ?? new CategoryDetails();
        if (category == OwnershipCategories.BankLeased && details.CounterpartyId is not > 0 && ev.Agreement is not null) details.CounterpartyId = ev.Agreement.BankId;
        details.StartDate = vehicle.AcquisitionDate;
        var found = Errors();
        if (category.Length == 0) found.Add(Error("category", Msg.Required, ("Field", "Category")));
        else found.AddRange(await categories.ValidateAsync(category, details, today, vehicle.AcquisitionDate, requireAmounts: mayEnterFinance));
        // The start date is the acquisition date and is judged under the acquisition item below, not as a field of the category.
        found.RemoveAll(e => e.Field == "details.startDate");
        Item("category", "Ownership category and counterparty", found);

        // 2. Acquisition (BR-VH-019), and the price that Self Owned and Bank Leased vehicles must have.
        found = Errors();
        if (vehicle.AcquisitionDate is null) found.Add(Error("acquisitionDate", Msg.Required, ("Field", "Acquisition date")));
        if (string.IsNullOrWhiteSpace(vehicle.AcquisitionType)) found.Add(Error("acquisitionType", Msg.Required, ("Field", "Acquisition type")));
        if ((category is OwnershipCategories.SelfOwned or OwnershipCategories.BankLeased) && ev.Acquisition?.PurchasePrice is not > 0)
            found.Add(Error("purchasePrice", Msg.Required, ("Field", "Purchase price")));
        Item("acquisition", "Acquisition date, type and price", found);

        // 3. The bank agreement belongs to a Bank Leased vehicle, and only to one.
        var hasAgreement = ev.Agreement is not null;
        if (category == OwnershipCategories.BankLeased || hasAgreement)
        {
            found = Errors();
            if (category == OwnershipCategories.BankLeased && !hasAgreement) found.Add(Error("finance", Msg.VhBankLeasedNeedsFinance));
            else if (category != OwnershipCategories.BankLeased && category.Length > 0 && hasAgreement) found.Add(Error("category", Msg.VhFinanceNeedsBankLeased));
            else if (ev.Agreement is { } agreement)
            {
                if (details.CounterpartyId is > 0 && details.CounterpartyId != agreement.BankId)
                {
                    var bank = (await ctx.RefsAsync([agreement.BankId])).GetValueOrDefault(agreement.BankId);
                    found.Add(Error("details.counterpartyId", Msg.VhBankIsAgreementBank, ("Bank", bank?.Name ?? "the agreement's bank")));
                }
                // The acquisition block may have changed since the agreement was saved: the two must still agree (BR-VH-009).
                found.AddRange(FinanceService.CrossCheck(ctx, agreement.DownPayment, ev.Acquisition?.AmountPaid));
                if (vehicle.AcquisitionDate is { } acquired && agreement.AgreementDate > acquired.AddDays(90))
                    found.Add(Error("agreementDate", Msg.Max, ("Field", "Agreement date"), ("Max", acquired.AddDays(90).ToString("yyyy-MM-dd"))));
            }
            Item("finance", "Bank finance agreement", found);
        }

        // 4. The registration book (BR-VH-015): required for a vehicle we own or lease from a bank, a warning for one whose papers sit with its owner.
        if (category.Length > 0)
        {
            var needed = category is OwnershipCategories.SelfOwned or OwnershipCategories.BankLeased;
            var has = await documents.HasRegistrationBookAsync(vehicle.VehicleId);
            found = Errors();
            if (!has) found.Add(Error("documents", Msg.VhRegistrationBookRequired));
            var note = !documents.CanCheck ? "The documents module is not installed yet, so the registration book is not being checked." : null;
            Item("registrationBook", "Registration book", found, blocking: needed, message: note);
        }

        // The postings and the schedule, when there is enough to work them out.
        var relation = category.Length > 0 && category != OwnershipCategories.SelfOwned && ev.Errors.All(e => e.Field is null || !e.Field.StartsWith("details."))
            ? CategoryService.NewRelation(vehicle.VehicleId, category, details, vehicle.AcquisitionDate ?? today, mayEnterFinance)
            : null;
        ev.Relation = relation;

        if (ev.Agreement is { } a)
        {
            var rows = Generate(a);
            var changed = new Dictionary<int, DateOnly>();
            found = Errors();
            foreach (var due in request.DueDates ?? [])
            {
                if (rows.All(r => r.InstallmentNo != due.InstallmentNo)) found.Add(Error("dueDates", Msg.Invalid, ("Field", "Due dates")));
                else changed[due.InstallmentNo] = due.DueDate;
            }
            rows = ScheduleGenerator.WithDueDates(rows, changed);
            if (found.Count == 0 && ScheduleGenerator.DateProblem(rows, a.AgreementDate, out var no) is { } problem)
            {
                var row = rows.First(r => r.InstallmentNo == no);
                var previous = rows.Where(r => r.InstallmentNo < no).Select(r => r.DueDate).DefaultIfEmpty(a.AgreementDate).Max();
                found.Add(problem == "before"
                    ? Error("dueDates", Msg.VhInstallmentOrder, ("No", no), ("Before", a.AgreementDate.ToString("yyyy-MM-dd")))
                    : Error("dueDates", Msg.VhInstallmentOrder, ("No", no), ("Before", previous.ToString("yyyy-MM-dd"))));
            }
            Item("schedule", "Installment due dates", found);
            ev.Schedule = rows;
        }

        // 5. The items entered on the Draft (step 4): each is judged by the rules of an item attached to a vehicle in the fleet (§20.1), and a cost is a major expense (§19).
        var itemPostings = new List<PostingPlan>();
        if (request.Items is { Count: > 0 })
        {
            found = Errors();
            var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var n = 0; n < request.Items.Count; n++)
            {
                var prepared = await itemService.PrepareAsync(vehicle.VehicleId, request.Items[n], vehicle.AcquisitionDate, today, vehicle.AcquisitionDate ?? today, caller, $"items[{n}].");
                found.AddRange(prepared.Errors);
                if (prepared.Item.SerialNo is { } serial && !serials.Add(serial)) found.Add(Error($"items[{n}].serialNo", Msg.ListedTwice, ("Field", "serial number")));
                ev.AttachedItems.Add(prepared.Item);
                if (OpeningPostings.ForItem(prepared.TypeName, prepared.Item.Description, prepared.Item.Cost, prepared.Item.InstallationDate, prepared.Item.SupplierId) is { } posting) itemPostings.Add(posting);
            }
            Item("items", "Attached items", found);
        }

        var opening = vehicle.AcquisitionDate is { } day && category.Length > 0
            ? OpeningPostings.ForVehicle(vehicle.RegistrationNo, day, ev.Acquisition, ev.Agreement, category, relation?.SecurityDeposit, relation?.CounterpartyId)
            : [];
        ev.Postings = [.. opening, .. itemPostings];
        return ev;
    }

    // ── Checking ────────────────────────────────────────────────────────────────────

    private void EnsureDraft(Vehicle vehicle)
    {
        if (vehicle.Status != VehicleStatuses.Draft)
            throw new ValidationException(ctx.Messages.Error("status", Msg.VhWrongStatus, ("Status", vehicle.Status), ("Action", "activated")));
    }

    public async Task<ActivationCheck> CheckAsync(int vehicleId, ActivateVehicleRequest request, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadReadOnlyAsync(vehicleId);
        EnsureDraft(vehicle);
        var ev = await EvaluateAsync(vehicle, request, caller);
        var refs = await ctx.RefsAsync(ev.Postings.Select(p => p.PartnerId));
        return new ActivationCheck
        {
            CanActivate = ev.Items.All(i => i.Ok || !i.Blocking), Items = ev.Items,
            Postings = ev.Postings.Select(p => new PostingModel { Type = p.Type, SubType = p.SubType, Amount = p.Amount, Date = p.Date, Partner = VehicleContext.Ref(refs, p.PartnerId), Reference = p.Reference }).ToList(),
            Schedule = ev.Schedule.Select(ToModel).ToList()
        };
    }

    // ── Activating ──────────────────────────────────────────────────────────────────

    public async Task<VehicleModel> ActivateAsync(int vehicleId, ActivateVehicleRequest request, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadAsync(vehicleId);
        EnsureDraft(vehicle);
        var ev = await EvaluateAsync(vehicle, request, caller);
        var errors = new List<ValidationError>(ev.Errors);
        if (string.IsNullOrWhiteSpace(request.RowVersion)) errors.Add(ctx.Messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));
        if (errors.Count > 0) throw new ValidationException(errors);
        try { ctx.Db.Entry(vehicle).Property(v => v.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion!); }
        catch (FormatException) { throw new ValidationException(ctx.Messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        // Two questions the person may have to answer: the fuel card is on another vehicle, and the driver is on another vehicle.
        var today = await ctx.TodayAsync();
        var questions = new List<ValidationError>();
        Vehicle? cardHolder = null;
        if (vehicle.FuelCardNumber is { } card)
        {
            cardHolder = await ctx.Db.Vehicles.Where(v => v.TenantId == ctx.Tenant && v.IsInFleet && v.FuelCardNumber == card && v.VehicleId != vehicle.VehicleId).FirstOrDefaultAsync();
            if (cardHolder is not null && !request.ReassignFuelCard) questions.Add(ctx.Messages.Error("fuelCardNumber", Msg.VhFuelCardInUse, ("RegNo", cardHolder.RegistrationNo)));
        }
        DriverAssignment? driverElsewhere = null;
        Vehicle? driverOther = null;
        if (vehicle.DefaultDriverId is { } driverId)
        {
            var driver = await ctx.Partners.FindAsync(driverId);
            if (driver is null || !driver.IsAvailable) errors.Add(ctx.Messages.Error("defaultDriverId", Msg.Invalid, ("Field", "Default driver")));
            else if (!driver.HasRole(PartnerRoleCodes.Driver)) errors.Add(ctx.Messages.Error("defaultDriverId", Msg.VhCounterpartyLacksRole, ("PartnerName", driver.DisplayName), ("Role", "Driver")));
            else
            {
                driverElsewhere = await ctx.Db.Assignments.FirstOrDefaultAsync(a => a.TenantId == ctx.Tenant && a.DriverId == driverId && a.EffectiveTo == null);
                if (driverElsewhere is not null)
                {
                    driverOther = await ctx.Db.Vehicles.FirstAsync(v => v.TenantId == ctx.Tenant && v.VehicleId == driverElsewhere.VehicleId);
                    if (!request.ReleaseFromOther) questions.Add(ctx.Messages.Error("defaultDriverId", Msg.VhDriverAlreadyAssigned, ("DriverName", driver.DisplayName), ("RegNo", driverOther.RegistrationNo)));
                }
            }
        }
        if (errors.Count > 0) throw new ValidationException(errors);
        if (questions.Count > 0) throw new ValidationException(questions);

        var mayEnterFinance = caller.Has(PermissionCodes.VEH_FIELD_FINANCE_VIEW);
        var category = request.Category;
        var details = request.Details;
        var acquired = ev.Acquired!.Value;
        VehicleInstallment? firstInstallment = null;

        try
        {
            await ctx.Db.InTransactionAsync(async ct =>
            {
                var now = DateTime.UtcNow;

                // 1. What the vehicle takes over from others goes first: the database allows one holder of a card and one vehicle per driver.
                if (cardHolder is not null)
                {
                    audit.Note(new AuditNote("Vehicle", cardHolder.VehicleId.ToString(), "FuelCardReassigned", Field: "FuelCardNumber", OldValue: cardHolder.FuelCardNumber, NewValue: null,
                        Reason: $"Reassigned to {vehicle.RegistrationNo}", RootEntity: "Vehicle", RootRecordId: cardHolder.VehicleId.ToString()));
                    cardHolder.FuelCardCompanyId = null;
                    cardHolder.FuelCardNumber = null;
                    cardHolder.ModifiedBy = caller.UserId;
                    cardHolder.ModifiedOn = now;
                }
                if (driverElsewhere is not null && driverOther is not null)
                {
                    driverElsewhere.EffectiveTo = today < driverElsewhere.EffectiveFrom ? driverElsewhere.EffectiveFrom : today;
                    driverElsewhere.EndReason = $"Reassigned to {vehicle.RegistrationNo}";
                    driverOther.DefaultDriverId = null;
                    driverOther.ModifiedBy = caller.UserId;
                    driverOther.ModifiedOn = now;
                }
                if (cardHolder is not null || driverElsewhere is not null) await ctx.Db.SaveChangesAsync(ct);

                // 2. The vehicle, its lifecycle, its ledger and its agreement.
                var from = vehicle.Status;
                vehicle.Status = VehicleStatuses.Active;
                vehicle.CurrentCategory = category;
                vehicle.CurrentCounterpartyId = category == OwnershipCategories.SelfOwned ? null : details.CounterpartyId;
                vehicle.DraftData = null;
                vehicle.ModifiedBy = caller.UserId;
                vehicle.ModifiedOn = now;
                ctx.LogEvent(vehicle, LifecycleEvents.StatusChange, today, caller, fromStatus: from, toStatus: VehicleStatuses.Active, toCategory: category,
                    reason: "Activated", counterpartyId: vehicle.CurrentCounterpartyId);

                foreach (var p in ev.Postings)
                    ctx.Db.Transactions.Add(new VehicleTransaction
                    {
                        VehicleId = vehicle.VehicleId, Type = p.Type, SubType = p.SubType, Amount = p.Amount, TransactionDate = p.Date, PartnerId = p.PartnerId,
                        Reference = p.Reference.Length <= 120 ? p.Reference : p.Reference[..120],
                        Source = TransactionSources.VehicleCreation, IsSystemGenerated = true, CreatedBy = caller.UserId, CreatedOn = now
                    });

                foreach (var item in ev.AttachedItems) ctx.Db.Items.Add(item);

                if (ev.Agreement is { } draft)
                {
                    var agreement = await ctx.Db.Agreements.FirstAsync(a => a.VehicleFinanceAgreementId == draft.VehicleFinanceAgreementId, ct);
                    agreement.Status = AgreementStatuses.Active;
                    agreement.ModifiedBy = caller.UserId;
                    agreement.ModifiedOn = now;
                    await ctx.Db.SaveChangesAsync(ct);   // the agreement's id is needed for its rows, and the vehicle goes with it
                    foreach (var row in ev.Schedule)
                    {
                        var installment = new VehicleInstallment
                        {
                            VehicleId = vehicle.VehicleId, VehicleFinanceAgreementId = agreement.VehicleFinanceAgreementId, InstallmentNo = row.InstallmentNo, DueDate = row.DueDate,
                            ExpectedAmount = row.Amount, PaidAmount = 0, IsResidual = row.IsResidual, Status = InstallmentStatuses.Pending
                        };
                        ctx.Db.Installments.Add(installment);
                        if (row.InstallmentNo == 1) firstInstallment = installment;   // FSD §23.4: "First installment due date" is the one a save-time hook needs to pick up immediately
                    }
                }
                await ctx.Db.SaveChangesAsync(ct);

                // 3. The relation and the driver's assignment, last: they are what a second activation of the same vehicle would collide on.
                if (category != OwnershipCategories.SelfOwned)
                    ctx.Db.Relations.Add(CategoryService.NewRelation(vehicle.VehicleId, category, details, acquired, mayEnterFinance));
                if (vehicle.DefaultDriverId is { } assigned)
                    ctx.Db.Assignments.Add(new DriverAssignment { VehicleId = vehicle.VehicleId, DriverId = assigned, EffectiveFrom = today, CreatedBy = caller.UserId, CreatedOn = now });
                await ctx.Db.SaveChangesAsync(ct);
            });
        }
        catch (DbUpdateConcurrencyException) { throw await ctx.StaleAsync(vehicleId); }
        catch (Exception ex) when (ex is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } })
        {
            // Someone took the fuel card or the driver, or activated the same vehicle, at the same moment. The transaction has rolled back: nothing was written.
            throw new ConflictException("The vehicle could not be activated because something changed at the same moment. Nothing was saved. Reload and try again.");
        }

        // FR-VH-008's "immediately": the first installment's due date is picked up now, not at the next hourly run. Called only after
        // the transaction above has committed — a notification for a schedule that then rolled back would be a false alarm.
        if (firstInstallment is not null) await notifications.NotifyAsync(nameof(VehicleInstallment), firstInstallment.VehicleInstallmentId.ToString());

        return await vehicles.GetAsync(vehicleId);
    }
}
