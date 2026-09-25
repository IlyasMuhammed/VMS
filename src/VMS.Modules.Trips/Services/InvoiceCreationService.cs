using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Partners;
using VMS.Shared.Time;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Trips.Services;

/// <summary>Invoice creation (§32.2–32.4, §33, §35, §52). First-time generation only — regeneration and overlap
/// replacement are CC-35's own job; an overlapping active invoice here is a hard refusal, never a replacement.</summary>
public interface IInvoiceCreationService
{
    Task<InvoiceModel> CreateAsync(CreateInvoiceRequest request, int createdByUserId, CancellationToken ct = default);
    Task<InvoiceModel> GetAsync(long invoiceId, CancellationToken ct = default);
}

internal sealed class InvoiceCreationService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IOperatingClock clock, IVehicleDirectory vehicles, IPartnerDirectory partners,
    ICustomerBillingConfigurationService billingConfigurations, ICustomerInvoiceTemplateService templates, ICustomerBillingAddressService billingAddresses,
    ICurrencyService currency, IInvoiceNumberAllocator numbers) : IInvoiceCreationService
{
    public async Task<InvoiceModel> CreateAsync(CreateInvoiceRequest request, int createdByUserId, CancellationToken ct = default)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == request.CustomerId, ct);
        if (customer is null) throw new ValidationException(messages.Error("customerId", Msg.Invalid, ("Field", "Customer")));
        if (customer.Status != CustomerStatuses.Active) throw new ValidationException(messages.Error("customerId", Msg.Invalid, ("Field", "Customer (not Active)")));
        if (request.PeriodFrom > request.PeriodTo) throw new ValidationException(messages.Error("periodTo", Msg.Invalid, ("Field", "Period to (must be on or after Period from)")));
        if (request.PeriodTo.DayNumber - request.PeriodFrom.DayNumber > 366)
            throw new ValidationException(messages.Error("periodTo", Msg.Invalid, ("Field", "Period (spans more than 366 days)")));
        // §32: "An invoice must contain at least one trip; adjustment-only invoices are not allowed."
        if (request.TripIds.Count == 0)
            throw new ValidationException(messages.Error("tripIds", Msg.Required, ("Field", "At least one trip (adjustment-only invoices are not allowed)")));
        foreach (var adj in request.Adjustments)
        {
            if (adj.Amount == 0) throw new ValidationException(messages.Error("adjustments", Msg.Invalid, ("Field", "Adjustment amount (must be non-zero)")));
            if (string.IsNullOrWhiteSpace(adj.AdjustmentMonth)) throw new ValidationException(messages.Error("adjustments", Msg.Required, ("Field", "Adjustment month")));
            if (string.IsNullOrWhiteSpace(adj.Note)) throw new ValidationException(messages.Error("adjustments", Msg.Required, ("Field", "Adjustment note")));
        }

        var invoiceDate = request.InvoiceDate ?? await clock.TodayAsync(tenant.TenantId);

        var applicableTemplates = await templates.ResolveApplicableAsync(request.CustomerId, invoiceDate, ct);
        long templateId; int templateVersion;
        if (request.CustomerInvoiceTemplateId is { } reqTemplateId)
        {
            var chosen = applicableTemplates.Templates.FirstOrDefault(t => t.CustomerInvoiceTemplateId == reqTemplateId)
                ?? throw new BusinessRuleException("TEMPLATE_REQUIRED", "The selected invoice template is not applicable for this customer and date.", []);
            templateId = chosen.CustomerInvoiceTemplateId; templateVersion = chosen.Version;
        }
        else if (applicableTemplates.Templates.Count == 1)
        {
            templateId = applicableTemplates.Templates[0].CustomerInvoiceTemplateId; templateVersion = applicableTemplates.Templates[0].Version;
        }
        else
        {
            throw new BusinessRuleException("TEMPLATE_REQUIRED",
                applicableTemplates.Templates.Count == 0 ? "No active invoice format is configured for this customer." : "Several invoice formats are applicable; choose one.", []);
        }

        var activeAddresses = await billingAddresses.ListAsync(request.CustomerId, includeInactive: false, ct);
        CustomerBillingAddressModel address;
        if (request.CustomerBillingAddressId is { } reqAddressId)
            address = activeAddresses.FirstOrDefault(a => a.CustomerBillingAddressId == reqAddressId)
                ?? throw new BusinessRuleException("BILLING_ADDRESS_REQUIRED", "The selected billing address is not Active for this customer.", []);
        else
            address = activeAddresses.FirstOrDefault(a => a.IsDefault) ?? activeAddresses.FirstOrDefault()
                ?? throw new BusinessRuleException("BILLING_ADDRESS_REQUIRED", "This customer has no Active billing address.", []);

        var billing = await billingConfigurations.GetCurrentAsync(request.CustomerId, ct);
        var settings = await currency.GetSettingsAsync(ct);
        // §35: "non-applicable rules are still snapshotted with DeductionAmount 0" — needs every effective-dated
        // rule, not just the applicable ones ICustomerTaxRuleService.ResolveApplicableAsync returns (that method's
        // own contract, established in CC-06, is "the rules that actually apply" — reading the table directly
        // here rather than changing that method's meaning for every other caller).
        var effectiveTaxRules = await db.CustomerTaxRules.AsNoTracking()
            .Where(r => r.TenantId == tenant.TenantId && r.CustomerId == request.CustomerId
                && r.EffectiveFrom <= invoiceDate && (r.EffectiveTo == null || r.EffectiveTo >= invoiceDate))
            .OrderBy(r => r.Sequence).ToListAsync(ct);

        return await db.InTransactionAsync(async ct2 =>
        {
            // §52 step 3: serialise generation per customer — the filtered unique index on InvoiceTripLink
            // (CC-23) is still the final, DB-enforced guarantee (AC-32); this lock only avoids two concurrent
            // requests for the *same customer* wastefully doing all of steps 4-9 before one of them loses.
            await AcquireCustomerLockAsync(request.CustomerId, ct2);

            var distinctTripIds = request.TripIds.Distinct().ToList();
            var trips = await db.Trips.Where(t => t.TenantId == tenant.TenantId && distinctTripIds.Contains(t.TripId)).ToListAsync(ct2);
            if (trips.Count != distinctTripIds.Count)
                throw new ValidationException(messages.Error("tripIds", Msg.Invalid, ("Field", "One or more selected trips were not found")));
            if (trips.Any(t => t.CustomerId != request.CustomerId))
                throw new ValidationException(messages.Error("tripIds", Msg.Invalid, ("Field", "One or more selected trips belong to a different customer")));

            // §52 step 5, re-checked fresh inside the lock (not trusting the earlier search read).
            var alreadyLinked = await db.InvoiceTripLinks.AnyAsync(l => l.TenantId == tenant.TenantId && l.IsActive && distinctTripIds.Contains(l.TripId), ct2);
            if (alreadyLinked) throw new ConflictException("One or more selected trips have already been invoiced.");

            var podStatusByTrip = billing.PodRequired ? await LatestPodStatusByTripAsync(distinctTripIds, ct2) : new Dictionary<long, string?>();
            var blockedTrips = new List<(Trip Trip, IReadOnlyList<string> Reasons)>();
            foreach (var trip in trips)
            {
                var reasons = TripInvoiceEligibility.BlockingReasons(trip, billing.PodRequired, podStatusByTrip.GetValueOrDefault(trip.TripId), settings.MultiCurrencyEnabled, settings.BaseCurrencyCode);
                if (reasons.Count > 0) blockedTrips.Add((trip, reasons));
            }
            if (blockedTrips.Count > 0)
                // §32.2: hard stop — "the user cannot bypass this by deselecting the unpriced trip." The whole
                // invoice is refused, not just the offending trips.
                throw new BusinessRuleException("RATE_MISSING", "Invoice cannot be generated. One or more selected trips are not eligible.",
                    blockedTrips.Select(b => new BusinessRuleDetail("tripId", b.Trip.TripId, string.Join(", ", b.Reasons))).ToArray());

            // §39/§52 step 6: overlap re-checked fresh. Replacement is CC-35's own job — this refuses outright.
            var overlap = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.CustomerId == request.CustomerId && i.IsActive
                && i.PeriodFrom <= request.PeriodTo && i.PeriodTo >= request.PeriodFrom, ct2);
            if (overlap is not null)
                throw new BusinessRuleException("OVERLAP_DETECTED", $"An active invoice ({overlap.InvoiceNumber}) already exists for part of the selected billing period.",
                    [new BusinessRuleDetail("invoiceId", overlap.InvoiceId, overlap.InvoiceNumber)]);

            var vehicleIds = trips.Select(t => t.VehicleId).Distinct().ToList();
            var vehicleInfos = await vehicles.FindManyAsync(vehicleIds, ct2);
            var driverIds = trips.Where(t => t.DriverId is not null).Select(t => t.DriverId!.Value).Distinct().ToList();
            var driverInfos = driverIds.Count == 0 ? new Dictionary<int, PartnerInfo>() : await partners.FindManyAsync(driverIds, ct2);
            var configIds = trips.Where(t => t.TripConfigurationId is not null).Select(t => t.TripConfigurationId!.Value).Distinct().ToList();
            var configs = configIds.Count == 0 ? new Dictionary<long, TripConfiguration>() : await db.TripConfigurations
                .Where(c => c.TenantId == tenant.TenantId && configIds.Contains(c.TripConfigurationId)).ToDictionaryAsync(c => c.TripConfigurationId, ct2);

            var invoice = new Invoice
            {
                CustomerId = request.CustomerId, PeriodFrom = request.PeriodFrom, PeriodTo = request.PeriodTo, InvoiceDate = invoiceDate,
                // §36's own header field, CC-23 built the column ahead of any caller — CC-38's own "overdue
                // amount" is the first thing that actually needs it, so it is finally stamped here.
                DueDate = invoiceDate.AddDays(billing.PaymentTermsDays),
                CustomerInvoiceTemplateId = templateId, TemplateVersion = templateVersion,
                CustomerBillingAddressId = address.CustomerBillingAddressId, BillToAddressName = address.AddressName, BillToAddressLine1 = address.AddressLine1,
                BillToAddressLine2 = address.AddressLine2, BillToCityId = address.CityId, BillToProvinceState = address.ProvinceState, BillToCountryId = address.CountryId,
                BillToPostalCode = address.PostalCode, BillToNtn = address.Ntn, BillToStrn = address.Strn,
                CustomerCode = customer.CustomerCode, CustomerName = customer.CustomerName, Ntn = customer.Ntn, Strn = customer.Strn,
                CurrencyCode = customer.CurrencyCode, PaymentTermsDays = billing.PaymentTermsDays,
                Status = request.SaveAsDraft ? InvoiceStatuses.Draft : InvoiceStatuses.Generated, PaymentStatus = InvoicePaymentStatuses.Unpaid, IsActive = true,
                GeneratedBy = createdByUserId, GeneratedOn = DateTime.UtcNow, Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
                // §41: "PageSize | Snapshot of customer config (default 50)" — CC-23 already added this column
                // to Invoice ahead of this task (CC-28); populated for real for the first time here.
                EvidencePageSize = billing.EvidencePageSize
            };

            var lines = new List<InvoiceLine>();
            var incomeByLine = new List<(TripIncome Income, InvoiceLine Line)>();
            var lineNo = 1;
            var warnings = new List<string>();

            foreach (var trip in trips.OrderBy(t => vehicleInfos.GetValueOrDefault(t.VehicleId)?.RegistrationNo).ThenBy(t => t.TripDate).ThenBy(t => t.TripNumber))
            {
                var vehicle = vehicleInfos.GetValueOrDefault(trip.VehicleId);
                var driver = trip.DriverId is { } d ? driverInfos.GetValueOrDefault(d) : null;
                var config = trip.TripConfigurationId is { } cid ? configs.GetValueOrDefault(cid) : null;
                var routeLabel = await RouteLabels.BuildAsync(db, tenant.TenantId, trip, ct2);

                lines.Add(new InvoiceLine
                {
                    LineNo = lineNo++, TripId = trip.TripId, LineType = InvoiceLineTypes.Trip, TripNumber = trip.TripNumber, TripDate = trip.TripDate,
                    TripType = trip.TripType, CustomerTripReference = trip.CustomerTripReference, RouteId = trip.RouteId, RouteLabel = routeLabel,
                    TripConfigurationId = trip.TripConfigurationId, TripCode = config?.TripCode, VehicleId = trip.VehicleId, VehicleRegNo = vehicle?.RegistrationNo,
                    DriverId = trip.DriverId, DriverName = driver?.DisplayName,
                    Description = config is not null ? $"{config.TripCode} {routeLabel}".Trim() : $"Open trip {routeLabel}",
                    Quantity = 1, Rate = trip.TripAmount!.Value, Amount = trip.TripAmount!.Value,
                    TripRateId = trip.TripRateId, RateEffectiveFrom = trip.RateEffectiveFrom, RateEffectiveTo = trip.RateEffectiveTo, RateSource = trip.RateSource,
                    CustomerId = trip.CustomerId, CustomerCode = customer.CustomerCode, CustomerName = customer.CustomerName
                });

                // §32.1/§33: "Billable trip income ... is added automatically as extra lines under that trip."
                var billableIncome = await db.TripIncomes
                    .Where(i => i.TenantId == tenant.TenantId && i.TripId == trip.TripId && i.IsBillable && !i.IsVoided && i.InvoiceLineId == null).ToListAsync(ct2);
                foreach (var income in billableIncome)
                {
                    var incomeLine = new InvoiceLine
                    {
                        LineNo = lineNo++, TripId = trip.TripId, LineType = InvoiceLineTypes.Income, TripNumber = trip.TripNumber, TripDate = trip.TripDate,
                        Description = $"{trip.TripNumber} — additional income", Quantity = 1, Rate = income.Amount, Amount = income.Amount,
                        CustomerId = trip.CustomerId, CustomerCode = customer.CustomerCode, CustomerName = customer.CustomerName
                    };
                    lines.Add(incomeLine);
                    incomeByLine.Add((income, incomeLine));
                }
            }

            // §35: "Gross = Sum Line Amounts + Sum Adjustments" — both Trip and Income lines count as line amounts;
            // "TotalTripAmount" is this module's own name for that sum (the schema has no separate income total).
            var totalTripAmount = lines.Sum(l => l.Amount);
            var totalAdjustment = request.Adjustments.Sum(a => a.Amount);
            var grossAmount = totalTripAmount + totalAdjustment;

            var taxLines = new List<InvoiceTaxLine>();
            decimal totalDeduction = 0;
            foreach (var rule in effectiveTaxRules)
            {
                decimal amount = 0;
                if (rule.Applicable)
                {
                    var basis = rule.CalculationBasis == CalculationBases.InvoiceSubtotal ? grossAmount : totalTripAmount;
                    // §35: "Negative basis ... deduction computed as 0 and a warning shown."
                    if (basis < 0) warnings.Add($"{rule.TaxName}: basis is negative, deduction computed as 0.");
                    else amount = rule.TaxType == TaxTypes.Percentage ? Math.Round(basis * (rule.TaxPercentage ?? 0) / 100, 2) : rule.FixedAmount ?? 0;
                }
                // §35: "non-applicable rules are still snapshotted with DeductionAmount 0 for traceability."
                taxLines.Add(new InvoiceTaxLine
                {
                    CustomerTaxRuleId = rule.CustomerTaxRuleId, TaxName = rule.TaxName, TaxCode = rule.TaxCode, TaxType = rule.TaxType,
                    TaxPercentage = rule.TaxPercentage, FixedAmount = rule.FixedAmount, CalculationBasis = rule.CalculationBasis, Sequence = rule.Sequence,
                    Applicable = rule.Applicable, Amount = amount
                });
                totalDeduction += amount;
            }

            var netAmount = grossAmount - totalDeduction;
            if (netAmount < 0)
                // §35: "Net may not be negative; if Gross − Deductions < 0 the invoice is blocked" (Recommended Design).
                throw new BusinessRuleException("NET_NEGATIVE", "The invoice's net amount cannot be negative.",
                    [new BusinessRuleDetail("netAmount", netAmount, "Gross minus deductions is negative.")]);

            invoice.TotalTripAmount = totalTripAmount; invoice.TotalAdjustment = totalAdjustment; invoice.GrossAmount = grossAmount;
            invoice.TotalDeduction = totalDeduction; invoice.NetAmount = netAmount; invoice.BalanceAmount = netAmount;
            invoice.InvoiceNumber = await numbers.NextAsync(ct2);
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync(ct2);   // the id is needed for every child row and for RootInvoiceId

            invoice.RootInvoiceId = invoice.InvoiceId;
            foreach (var line in lines) line.InvoiceId = invoice.InvoiceId;
            var adjustments = request.Adjustments.Select((a, i) => new InvoiceAdjustment
            {
                InvoiceId = invoice.InvoiceId, AdjustmentMonth = a.AdjustmentMonth.Trim(), AdjustmentAmount = a.Amount, AdjustmentNote = a.Note.Trim(),
                ReferenceInvoiceNo = string.IsNullOrWhiteSpace(a.ReferenceInvoiceNo) ? null : a.ReferenceInvoiceNo.Trim(), Sequence = i + 1
            }).ToList();
            foreach (var taxLine in taxLines) taxLine.InvoiceId = invoice.InvoiceId;

            db.InvoiceAdjustments.AddRange(adjustments);
            db.InvoiceLines.AddRange(lines);
            db.InvoiceTaxLines.AddRange(taxLines);
            await db.SaveChangesAsync(ct2);   // line ids are needed for trip links and for locking billed income

            var tripLines = lines.Where(l => l.LineType == InvoiceLineTypes.Trip).ToList();
            db.InvoiceTripLinks.AddRange(tripLines.Select(l =>
                new InvoiceTripLink { InvoiceId = invoice.InvoiceId, TripId = l.TripId!.Value, InvoiceLineId = l.InvoiceLineId, IsActive = true, LinkedOn = DateTime.UtcNow }));

            // §46.4: "Trip.CurrentInvoiceId ... Denormalised from active InvoiceTripLink" — Trip.InvoiceId (built
            // in CC-15, always null until now) is that column; this is the first task that ever sets it, which is
            // also what makes CC-15's own Reopen/Inactivate/Reactivate InvoiceId guards genuinely reachable at last.
            foreach (var trip in trips) trip.InvoiceId = invoice.InvoiceId;
            // §30: "InvoiceLineId | Set when billed; a billed income row cannot be edited" — locks it against
            // CC-21's own TripIncomeService.VoidAsync, which already refuses once this is set.
            foreach (var (income, line) in incomeByLine) income.InvoiceLineId = line.InvoiceLineId;

            db.InvoiceHistories.Add(new InvoiceHistory { InvoiceId = invoice.InvoiceId, FromStatus = string.Empty, ToStatus = invoice.Status, ChangedBy = createdByUserId, ChangedAtUtc = DateTime.UtcNow });

            // §41: "Queued in the invoice-creation transaction and rendered by a background job." The job
            // (IInvoiceEvidenceGenerationService) does the actual rendering later; this task only queues it.
            db.InvoiceEvidences.Add(new InvoiceEvidence
            {
                InvoiceId = invoice.InvoiceId, EvidenceVersion = 1, GroupBy = InvoiceEvidenceGroupBy.Vehicle,
                PageSize = invoice.EvidencePageSize ?? 50, Status = InvoiceEvidenceStatuses.Queued
            });

            try { await db.SaveChangesAsync(ct2); }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // §52's own sequence diagram: the filtered unique index is the final guard even if two requests
                // somehow got this far together (AC-32).
                throw new ConflictException("One or more selected trips have already been invoiced.");
            }

            var model = ToModel(invoice, lines, adjustments, taxLines);
            model.Warnings = warnings;
            return model;
        }, ct);
    }

    public async Task<InvoiceModel> GetAsync(long invoiceId, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == invoiceId, ct)
            ?? throw new NotFoundException($"Invoice {invoiceId} was not found.");
        var lines = await db.InvoiceLines.AsNoTracking().Where(l => l.TenantId == tenant.TenantId && l.InvoiceId == invoiceId).OrderBy(l => l.LineNo).ToListAsync(ct);
        var adjustments = await db.InvoiceAdjustments.AsNoTracking().Where(a => a.TenantId == tenant.TenantId && a.InvoiceId == invoiceId).OrderBy(a => a.Sequence).ToListAsync(ct);
        var taxLines = await db.InvoiceTaxLines.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && t.InvoiceId == invoiceId).OrderBy(t => t.Sequence).ToListAsync(ct);
        var model = ToModel(invoice, lines, adjustments, taxLines);

        // CC-28 built the real evidence pipeline this task's own placeholder ("always Queued, never advances")
        // named as its future caller — reading the latest version's real status for the first time here.
        var latestEvidence = await db.InvoiceEvidences.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && e.InvoiceId == invoiceId)
            .OrderByDescending(e => e.EvidenceVersion).FirstOrDefaultAsync(ct);
        if (latestEvidence is not null) model.EvidenceStatus = latestEvidence.Status;
        return model;
    }

    /// <summary>§52 step 3: <c>sp_getapplock('INV-CUST-{CustomerId}', Exclusive)</c>. <c>@LockOwner = 'Transaction'</c>
    /// releases it automatically on commit or rollback — no explicit release call needed.</summary>
    private async Task AcquireCustomerLockAsync(int customerId, CancellationToken ct)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        var dbTransaction = (SqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = dbTransaction;
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add(new SqlParameter("@Resource", $"INV-CUST-{customerId}"));
        command.Parameters.Add(new SqlParameter("@LockMode", "Exclusive"));
        command.Parameters.Add(new SqlParameter("@LockOwner", "Transaction"));
        command.Parameters.Add(new SqlParameter("@LockTimeout", 10000));
        var returnValue = new SqlParameter { Direction = ParameterDirection.ReturnValue, DbType = DbType.Int32 };
        command.Parameters.Add(returnValue);
        await command.ExecuteNonQueryAsync(ct);
        if ((int)returnValue.Value < 0)
            throw new ConcurrencyConflictException("Another request is generating an invoice for this customer at the same time. Please try again.");
    }

    private async Task<Dictionary<long, string?>> LatestPodStatusByTripAsync(IEnumerable<long> tripIds, CancellationToken ct)
    {
        var ids = tripIds.ToList();
        if (ids.Count == 0) return new Dictionary<long, string?>();
        var pods = await db.TripPODs.Where(p => p.TenantId == tenant.TenantId && ids.Contains(p.TripId)).ToListAsync(ct);
        return pods.GroupBy(p => p.TripId).ToDictionary(g => g.Key, g => (string?)g.OrderByDescending(p => p.UploadedAtUtc).First().Status);
    }

    private static InvoiceModel ToModel(Invoice i, List<InvoiceLine> lines, List<InvoiceAdjustment> adjustments, List<InvoiceTaxLine> taxLines) => new()
    {
        InvoiceId = i.InvoiceId, InvoiceNumber = i.InvoiceNumber, Version = i.Version, CustomerId = i.CustomerId, CustomerCode = i.CustomerCode,
        CustomerName = i.CustomerName, CurrencyCode = i.CurrencyCode, PeriodFrom = i.PeriodFrom, PeriodTo = i.PeriodTo, InvoiceDate = i.InvoiceDate, DueDate = i.DueDate,
        CustomerInvoiceTemplateId = i.CustomerInvoiceTemplateId, TemplateVersion = i.TemplateVersion, CustomerBillingAddressId = i.CustomerBillingAddressId,
        TotalTripAmount = i.TotalTripAmount, TotalAdjustment = i.TotalAdjustment, GrossAmount = i.GrossAmount, TotalDeduction = i.TotalDeduction,
        NetAmount = i.NetAmount, PaidAmount = i.PaidAmount, AdvanceAppliedAmount = i.AdvanceAppliedAmount, WriteOffAmount = i.WriteOffAmount,
        DiscountAmount = i.DiscountAmount, TransferredInAmount = i.TransferredInAmount, TransferredOutAmount = i.TransferredOutAmount,
        CarryForwardInAmount = i.CarryForwardInAmount, CarryForwardOutAmount = i.CarryForwardOutAmount, RefundedAmount = i.RefundedAmount,
        BalanceAmount = i.BalanceAmount, Status = i.Status, PaymentStatus = i.PaymentStatus, IsActive = i.IsActive,
        SubmittedOn = i.SubmittedOn, SubmissionChannel = i.SubmissionChannel, CancelledOn = i.CancelledOn, CancelReason = i.CancelReason, Remarks = i.Remarks,
        RowVersion = Convert.ToBase64String(i.RowVersion),
        Lines = lines.Select(l => new InvoiceLineModel
        {
            InvoiceLineId = l.InvoiceLineId, LineNo = l.LineNo, TripId = l.TripId, LineType = l.LineType, TripNumber = l.TripNumber, TripDate = l.TripDate,
            RouteLabel = l.RouteLabel, VehicleRegNo = l.VehicleRegNo, DriverName = l.DriverName, Description = l.Description, Quantity = l.Quantity, Rate = l.Rate, Amount = l.Amount
        }).ToList(),
        Adjustments = adjustments.Select(a => new InvoiceAdjustmentModel
        {
            InvoiceAdjustmentId = a.InvoiceAdjustmentId, AdjustmentMonth = a.AdjustmentMonth, AdjustmentAmount = a.AdjustmentAmount,
            AdjustmentNote = a.AdjustmentNote, ReferenceInvoiceNo = a.ReferenceInvoiceNo, Sequence = a.Sequence
        }).ToList(),
        TaxLines = taxLines.Select(t => new InvoiceTaxLineModel
        {
            InvoiceTaxLineId = t.InvoiceTaxLineId, TaxName = t.TaxName, TaxCode = t.TaxCode, Applicable = t.Applicable, Amount = t.Amount
        }).ToList()
    };
}
