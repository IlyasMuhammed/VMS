using Microsoft.Data.SqlClient;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-23: Invoice schema, versioning fields, trip link lock (§33, §36, §46.4, §46.5) — schema-level
/// guarantees only; there is no service/controller yet (generation itself is CC-24/25's job).</summary>
[Collection(ApiCollection.Name)]
public sealed class InvoiceSchemaTests(ApiFactory factory)
{
    private static long InsertInvoice(ApiFactory factory, Guid tenantId, string? invoiceNumber = null, int customerId = 1) =>
        factory.Scalar<long>(@"INSERT INTO trp.Invoices
            (TenantId, InvoiceNumber, Version, RootInvoiceId, CustomerId, PeriodFrom, PeriodTo, InvoiceDate,
             CustomerCode, CustomerName, CurrencyCode, PaymentTermsDays, Status, PaymentStatus, IsActive,
             TotalTripAmount, TotalAdjustment, GrossAmount, TotalDeduction, NetAmount,
             PaidAmount, AdvanceAppliedAmount, WriteOffAmount, DiscountAmount,
             TransferredInAmount, TransferredOutAmount, CarryForwardInAmount, CarryForwardOutAmount, RefundedAmount, BalanceAmount)
            OUTPUT INSERTED.InvoiceId
            VALUES (@t, @n, 1, 0, @c, '2026-07-01', '2026-07-31', '2026-08-01',
                    'CUS-00001', 'Acme', 'PKR', 30, 'Draft', 'Unpaid', 1,
                    0, 0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 0, 0, 0)",
            ("@t", tenantId), ("@n", invoiceNumber ?? $"INV-T-{Guid.NewGuid():N}"[..20]), ("@c", customerId));

    // LineNo must be bracketed ([LineNo]) in raw SQL — empirically, SQL Server's parser refuses it unquoted in
    // this position (a genuine reserved-word-like quirk, not a typo); EF Core's own generated SQL always quotes
    // identifiers so production code never hits this, but hand-written test SQL against this table must.
    private static long InsertInvoiceLine(ApiFactory factory, Guid tenantId, long invoiceId, long? tripId = null) =>
        factory.Scalar<long>(@"INSERT INTO trp.InvoiceLines
            (TenantId, InvoiceId, [LineNo], TripId, LineType, Description, Quantity, Rate, Amount, CustomerId, CustomerCode, CustomerName)
            OUTPUT INSERTED.InvoiceLineId
            VALUES (@t, @inv, 1, @trip, 'Trip', 'Test line', 1, 1000, 1000, 1, 'CUS-00001', 'Acme')",
            ("@t", tenantId), ("@inv", invoiceId), ("@trip", (object?)tripId ?? DBNull.Value));

    [Fact]
    public void The_database_rejects_a_second_active_link_for_the_same_trip()
    {
        var tenantId = factory.CreateTenant();
        var invoice1 = InsertInvoice(factory, tenantId);
        var invoice2 = InsertInvoice(factory, tenantId);
        var tripId = new Random().NextInt64(1, long.MaxValue / 2);
        var line1 = InsertInvoiceLine(factory, tenantId, invoice1, tripId);
        var line2 = InsertInvoiceLine(factory, tenantId, invoice2, tripId);

        factory.Execute(@"INSERT INTO trp.InvoiceTripLinks (TenantId, InvoiceId, TripId, InvoiceLineId, IsActive, LinkedOn)
            VALUES (@t, @inv, @trip, @line, 1, SYSUTCDATETIME())",
            ("@t", tenantId), ("@inv", invoice1), ("@trip", tripId), ("@line", line1));

        var ex = Assert.Throws<SqlException>(() => factory.Execute(
            @"INSERT INTO trp.InvoiceTripLinks (TenantId, InvoiceId, TripId, InvoiceLineId, IsActive, LinkedOn)
              VALUES (@t, @inv, @trip, @line, 1, SYSUTCDATETIME())",
            ("@t", tenantId), ("@inv", invoice2), ("@trip", tripId), ("@line", line2)));
        Assert.Contains("IX_InvoiceTripLinks_TripId", ex.Message);

        // A second, INACTIVE link for the same trip is not blocked — the constraint is scoped to IsActive = 1 only.
        factory.Execute(@"INSERT INTO trp.InvoiceTripLinks (TenantId, InvoiceId, TripId, InvoiceLineId, IsActive, LinkedOn, UnlinkedOn, UnlinkReason)
            VALUES (@t, @inv, @trip, @line, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), 'Regenerated')",
            ("@t", tenantId), ("@inv", invoice2), ("@trip", tripId), ("@line", line2));
        Assert.Equal(2, factory.Scalar<int>("SELECT COUNT(*) FROM trp.InvoiceTripLinks WHERE TripId = @trip", ("@trip", tripId)));
    }

    [Fact]
    public void The_app_cannot_update_an_invoice_line()
    {
        var tenantId = factory.CreateTenant();
        var invoiceId = InsertInvoice(factory, tenantId);
        var lineId = InsertInvoiceLine(factory, tenantId, invoiceId);

        var ex = Assert.Throws<SqlException>(() => factory.Execute("UPDATE trp.InvoiceLines SET Amount = 999999 WHERE InvoiceLineId = @id", ("@id", lineId)));
        Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1000, factory.Scalar<decimal>("SELECT Amount FROM trp.InvoiceLines WHERE InvoiceLineId = @id", ("@id", lineId)));
    }

    [Fact]
    public void The_app_cannot_delete_an_invoice_line()
    {
        var tenantId = factory.CreateTenant();
        var invoiceId = InsertInvoice(factory, tenantId);
        var lineId = InsertInvoiceLine(factory, tenantId, invoiceId);

        var ex = Assert.Throws<SqlException>(() => factory.Execute("DELETE FROM trp.InvoiceLines WHERE InvoiceLineId = @id", ("@id", lineId)));
        Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM trp.InvoiceLines WHERE InvoiceLineId = @id", ("@id", lineId)));
    }

    [Fact]
    public void The_invoice_number_is_unique_per_tenant()
    {
        var tenantId = factory.CreateTenant();
        InsertInvoice(factory, tenantId, invoiceNumber: "INV-DUPLICATE");
        var ex = Assert.Throws<SqlException>(() => InsertInvoice(factory, tenantId, invoiceNumber: "INV-DUPLICATE"));
        Assert.Contains("IX_Invoices_TenantId_InvoiceNumber", ex.Message);
    }
}
