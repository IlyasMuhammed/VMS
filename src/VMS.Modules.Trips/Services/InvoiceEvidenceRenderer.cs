using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace VMS.Modules.Trips.Services;

/// <summary>§41's own standard evidence layout — Sr. No., Trip Date, Route, Customer Reference, Bill Amount,
/// grouped and paginated by vehicle. Pure rendering: everything drawn comes from <see cref="InvoiceEvidenceData"/>
/// and the already-computed <see cref="InvoiceEvidencePagination.Result"/>, never a live database read — the same
/// "uses snapshots only" guarantee <see cref="InvoicePdfRenderer"/> already established for the invoice PDF
/// itself. "No template (Confirmed)": one layout for every customer, so this renderer takes no customer-specific
/// input at all.</summary>
internal interface IInvoiceEvidenceRenderer
{
    byte[] Render(InvoiceEvidenceData data, InvoiceEvidencePagination.Result pagination);
}

internal sealed record InvoiceEvidenceData(
    string CompanyName, string CustomerName, string CustomerCode, string InvoiceNumber, int InvoiceVersion,
    DateOnly InvoiceDate, DateOnly PeriodFrom, DateOnly PeriodTo, int EvidenceVersion, string CurrencyCode,
    decimal TotalTripAmount, decimal TotalAdjustment, decimal GrossAmount, decimal TotalDeduction, decimal NetAmount);

internal sealed class InvoiceEvidenceRenderer : IInvoiceEvidenceRenderer
{
    public byte[] Render(InvoiceEvidenceData d, InvoiceEvidencePagination.Result pagination)
    {
        var vehicleByReg = pagination.VehiclePages.ToDictionary(v => v.VehicleRegNo);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9));

                // §41: "Header (repeated on every page)" — the parts that never vary page to page. The
                // per-page-varying parts (current vehicle, "vehicle page n of m", the group subtotal) are
                // rendered inline in the content flow instead, right after each explicit page break, since
                // QuestPDF's own Header()/Footer() are one fixed template repeated verbatim on every page.
                page.Header().Column(col =>
                {
                    col.Item().Text($"{d.CompanyName} — Invoice Evidence").FontSize(13).Bold();
                    col.Item().Text($"{d.CustomerName} ({d.CustomerCode})");
                    col.Item().Text($"Invoice No.: {d.InvoiceNumber} (v{d.InvoiceVersion})   Invoice Date: {d.InvoiceDate:dd-MMM-yyyy}   Evidence version: {d.EvidenceVersion}");
                    col.Item().Text($"Billing Period: {d.PeriodFrom:dd-MMM-yyyy} to {d.PeriodTo:dd-MMM-yyyy}");
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    for (var i = 0; i < pagination.Pages.Count; i++)
                    {
                        var chunk = pagination.Pages[i];
                        if (i > 0) col.Item().PageBreak();

                        var vehiclePage = vehicleByReg[chunk.VehicleRegNo];
                        var pageOfVehicle = chunk.PageNumber - vehiclePage.FirstPage + 1;
                        var vehiclePageCount = vehiclePage.LastPage - vehiclePage.FirstPage + 1;
                        var routes = string.Join(", ", chunk.Lines.Select(l => l.RouteLabel).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct());

                        col.Item().Text($"Vehicle: {chunk.VehicleRegNo}   Route(s): {routes}   Vehicle page {pageOfVehicle} of {vehiclePageCount}").SemiBold();

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.ConstantColumn(30); c.ConstantColumn(70); c.RelativeColumn(); c.ConstantColumn(90); c.ConstantColumn(80); });
                            table.Header(header =>
                            {
                                foreach (var text in new[] { "Sr.", "Trip Date", "Route", "Customer Reference", "Bill Amount" })
                                    header.Cell().Border(1).Padding(2).Text(text).SemiBold();
                            });
                            var srBase = pagination.Pages.Where(p => p.VehicleRegNo == chunk.VehicleRegNo && p.PageNumber < chunk.PageNumber).Sum(p => p.Lines.Count);
                            for (var li = 0; li < chunk.Lines.Count; li++)
                            {
                                var line = chunk.Lines[li];
                                table.Cell().Border(1).Padding(2).Text((srBase + li + 1).ToString());
                                table.Cell().Border(1).Padding(2).Text(line.TripDate?.ToString("dd-MMM-yy") ?? "");
                                table.Cell().Border(1).Padding(2).Text(line.RouteLabel);
                                table.Cell().Border(1).Padding(2).Text(line.CustomerTripReference ?? "");
                                table.Cell().Border(1).Padding(2).AlignRight().Text(AmountInWords.FormatAmount(line.Amount));
                            }
                        });

                        if (chunk.PageNumber == vehiclePage.LastPage)
                            col.Item().AlignRight().Text($"{chunk.VehicleRegNo} subtotal: {AmountInWords.FormatAmount(vehiclePage.Subtotal)}").SemiBold();

                        if (i == pagination.Pages.Count - 1)
                        {
                            col.Item().PaddingTop(10).AlignRight().Text($"Trip Amount: {AmountInWords.FormatAmount(d.TotalTripAmount)}");
                            col.Item().AlignRight().Text($"Adjustments: {AmountInWords.FormatAmount(d.TotalAdjustment)}");
                            col.Item().AlignRight().Text($"Gross Amount: {AmountInWords.FormatAmount(d.GrossAmount)}");
                            col.Item().AlignRight().Text($"Deductions: {AmountInWords.FormatAmount(d.TotalDeduction)}");
                            col.Item().PaddingTop(4).AlignRight().Text($"Net Amount: {AmountInWords.FormatAmount(d.NetAmount)}").FontSize(11).Bold();
                        }
                    }

                    if (pagination.Pages.Count == 0) col.Item().Text("No trip lines on this invoice.");
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page "); t.CurrentPageNumber(); t.Span(" of "); t.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }
}
