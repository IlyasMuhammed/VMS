using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace VMS.Modules.Trips.Services;

/// <summary>§34's own Phase 1 standard layout: company header, invoice no./date/due date/period, bill-to,
/// customer NTN/STRN, line table grouped by vehicle, subtotal, adjustments, gross, each deduction, net, amount in
/// words (PKR), bank details, signature block. Pure rendering — everything it draws comes from
/// <see cref="InvoicePdfData"/>, never a live database read, so "uses snapshots only" holds by construction: the
/// caller (<see cref="InvoiceDocumentService"/>) is the only place that ever touches the database.</summary>
internal interface IInvoicePdfRenderer
{
    byte[] Render(InvoicePdfData data);
}

internal sealed record InvoicePdfLine(string? VehicleRegNo, string? TripNumber, DateOnly? TripDate, string Description, decimal Quantity, decimal Rate, decimal Amount);
internal sealed record InvoicePdfAdjustment(string AdjustmentMonth, decimal Amount, string Note);
internal sealed record InvoicePdfTaxLine(string TaxName, string TaxCode, bool Applicable, decimal Amount);

internal sealed record InvoicePdfData(
    string CompanyName, string? CompanyAddress, string? CompanyEmail, string? CompanyPhone,
    string InvoiceNumber, int Version, DateOnly InvoiceDate, DateOnly? DueDate, DateOnly PeriodFrom, DateOnly PeriodTo,
    string BillToName, string? BillToLine1, string? BillToLine2, string? BillToProvinceState, string? BillToPostalCode, string? BillToNtn, string? BillToStrn,
    string CustomerCode, string CustomerName, string? CustomerNtn, string? CustomerStrn, string CurrencyCode,
    IReadOnlyList<InvoicePdfLine> Lines, IReadOnlyList<InvoicePdfAdjustment> Adjustments, IReadOnlyList<InvoicePdfTaxLine> TaxLines,
    decimal TotalTripAmount, decimal TotalAdjustment, decimal GrossAmount, decimal TotalDeduction, decimal NetAmount);

internal sealed class InvoicePdfRenderer : IInvoicePdfRenderer
{
    public byte[] Render(InvoicePdfData d)
    {
        var groups = d.Lines.GroupBy(l => l.VehicleRegNo ?? "—").OrderBy(g => g.Key).ToList();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text(d.CompanyName).FontSize(16).Bold();
                    if (!string.IsNullOrWhiteSpace(d.CompanyAddress)) col.Item().Text(d.CompanyAddress);
                    var contact = string.Join("  ·  ", new[] { d.CompanyEmail, d.CompanyPhone }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (contact.Length > 0) col.Item().Text(contact);
                    col.Item().PaddingTop(10).Text("TAX INVOICE").FontSize(13).Bold();

                    col.Item().PaddingTop(5).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(t => { t.Span("Invoice No.: ").SemiBold(); t.Span($"{d.InvoiceNumber} (v{d.Version})"); });
                            c.Item().Text(t => { t.Span("Invoice Date: ").SemiBold(); t.Span(d.InvoiceDate.ToString("dd-MMM-yyyy")); });
                            if (d.DueDate is { } due) c.Item().Text(t => { t.Span("Due Date: ").SemiBold(); t.Span(due.ToString("dd-MMM-yyyy")); });
                            c.Item().Text(t => { t.Span("Period: ").SemiBold(); t.Span($"{d.PeriodFrom:dd-MMM-yyyy} to {d.PeriodTo:dd-MMM-yyyy}"); });
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Bill To:").SemiBold();
                            c.Item().Text(d.BillToName);
                            if (!string.IsNullOrWhiteSpace(d.BillToLine1)) c.Item().Text(d.BillToLine1);
                            if (!string.IsNullOrWhiteSpace(d.BillToLine2)) c.Item().Text(d.BillToLine2);
                            var cityLine = string.Join(", ", new[] { d.BillToProvinceState, d.BillToPostalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
                            if (cityLine.Length > 0) c.Item().Text(cityLine);
                            c.Item().Text($"Customer Code: {d.CustomerCode}");
                            var ntn = d.BillToNtn ?? d.CustomerNtn;
                            var strn = d.BillToStrn ?? d.CustomerStrn;
                            if (!string.IsNullOrWhiteSpace(ntn)) c.Item().Text($"NTN: {ntn}");
                            if (!string.IsNullOrWhiteSpace(strn)) c.Item().Text($"STRN: {strn}");
                        });
                    });
                });

                page.Content().PaddingTop(15).Column(col =>
                {
                    foreach (var group in groups)
                    {
                        col.Item().PaddingTop(8).Text(group.Key).Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(60); c.ConstantColumn(70); c.RelativeColumn(); c.ConstantColumn(40); c.ConstantColumn(70); c.ConstantColumn(70);
                            });
                            table.Header(header =>
                            {
                                foreach (var text in new[] { "Trip Date", "Trip No.", "Description", "Qty", "Rate", "Amount" })
                                    header.Cell().Border(1).Padding(2).Text(text).SemiBold();
                            });
                            foreach (var line in group.OrderBy(l => l.TripDate).ThenBy(l => l.TripNumber))
                            {
                                table.Cell().Border(1).Padding(2).Text(line.TripDate?.ToString("dd-MMM-yy") ?? "");
                                table.Cell().Border(1).Padding(2).Text(line.TripNumber ?? "");
                                table.Cell().Border(1).Padding(2).Text(line.Description);
                                table.Cell().Border(1).Padding(2).Text(line.Quantity.ToString("0.##"));
                                table.Cell().Border(1).Padding(2).AlignRight().Text(AmountInWords.FormatAmount(line.Rate));
                                table.Cell().Border(1).Padding(2).AlignRight().Text(AmountInWords.FormatAmount(line.Amount));
                            }
                        });
                        col.Item().AlignRight().Text($"Vehicle subtotal: {AmountInWords.FormatAmount(group.Sum(l => l.Amount))}").SemiBold();
                    }

                    col.Item().PaddingTop(10).AlignRight().Text($"Trip Amount: {AmountInWords.FormatAmount(d.TotalTripAmount)}");

                    if (d.Adjustments.Count > 0)
                    {
                        col.Item().PaddingTop(6).Text("Adjustments").SemiBold();
                        foreach (var adj in d.Adjustments)
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"{adj.AdjustmentMonth} — {adj.Note}");
                                row.ConstantItem(90).AlignRight().Text(AmountInWords.FormatAmount(adj.Amount));
                            });
                    }
                    col.Item().AlignRight().Text($"Gross Amount: {AmountInWords.FormatAmount(d.GrossAmount)}").SemiBold();

                    if (d.TaxLines.Count > 0)
                    {
                        col.Item().PaddingTop(6).Text("Deductions").SemiBold();
                        foreach (var tax in d.TaxLines)
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"{tax.TaxName} ({tax.TaxCode}){(tax.Applicable ? "" : " — not applicable")}");
                                row.ConstantItem(90).AlignRight().Text(AmountInWords.FormatAmount(tax.Amount));
                            });
                    }
                    col.Item().PaddingTop(4).AlignRight().Text($"Net Amount: {AmountInWords.FormatAmount(d.NetAmount)}").FontSize(11).Bold();
                    col.Item().PaddingTop(6).Text($"Amount in words: {AmountInWords.Convert(d.NetAmount, d.CurrencyCode)}").Italic();

                    col.Item().PaddingTop(20).Text("Bank Details").SemiBold();
                    col.Item().Text("Please refer to the company's bank details on file for payment.");

                    col.Item().PaddingTop(30).Row(row =>
                    {
                        row.RelativeItem().Text("_____________________\nPrepared By");
                        row.RelativeItem().AlignRight().Text("_____________________\nAuthorised Signatory");
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }
}
