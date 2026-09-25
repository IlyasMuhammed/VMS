using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace VMS.Modules.Trips.Services;

/// <summary>§40A.4: "Export PDF (customer statement format with company header)" — pure rendering, the same
/// "everything comes from a snapshot record, never a live read" discipline as <see cref="IInvoicePdfRenderer"/>.</summary>
internal interface ICustomerStatementPdfRenderer
{
    byte[] Render(CustomerStatementPdfData data);
}

internal sealed record CustomerStatementPdfRow(DateOnly EntryDate, string EntryNumber, string DocumentNo, string EntryType,
    string? InvoiceNumber, string Narration, decimal DebitAmount, decimal CreditAmount, decimal RunningBalance);

internal sealed record CustomerStatementPdfData(
    string CompanyName, string? CompanyAddress, string? CompanyEmail, string? CompanyPhone,
    string CustomerCode, string CustomerName, string CurrencyCode, DateOnly? From, DateOnly? To,
    decimal OpeningBalance, decimal PeriodDebits, decimal PeriodCredits, decimal ClosingBalance, decimal OverdueAmount,
    IReadOnlyList<CustomerStatementPdfRow> Rows);

internal sealed class CustomerStatementPdfRenderer : ICustomerStatementPdfRenderer
{
    public byte[] Render(CustomerStatementPdfData d)
    {
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
                    col.Item().PaddingTop(10).Text("CUSTOMER STATEMENT").FontSize(13).Bold();

                    col.Item().PaddingTop(5).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(t => { t.Span("Customer: ").SemiBold(); t.Span($"{d.CustomerName} ({d.CustomerCode})"); });
                            c.Item().Text(t => { t.Span("Currency: ").SemiBold(); t.Span(d.CurrencyCode); });
                            var period = (d.From is { } f ? f.ToString("dd-MMM-yyyy") : "beginning") + " to " + (d.To is { } to ? to.ToString("dd-MMM-yyyy") : "date");
                            c.Item().Text(t => { t.Span("Period: ").SemiBold(); t.Span(period); });
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().AlignRight().Text(t => { t.Span("Opening Balance: ").SemiBold(); t.Span(AmountInWords.FormatAmount(d.OpeningBalance)); });
                            c.Item().AlignRight().Text(t => { t.Span("Closing Balance: ").SemiBold(); t.Span(AmountInWords.FormatAmount(d.ClosingBalance)); });
                            c.Item().AlignRight().Text(t => { t.Span("Overdue: ").SemiBold(); t.Span(AmountInWords.FormatAmount(d.OverdueAmount)); });
                        });
                    });
                });

                page.Content().PaddingTop(15).Column(col =>
                {
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(55); c.ConstantColumn(85); c.RelativeColumn(); c.ConstantColumn(65); c.ConstantColumn(65); c.ConstantColumn(70);
                        });
                        table.Header(header =>
                        {
                            foreach (var text in new[] { "Date", "Doc No.", "Narration", "Debit", "Credit", "Balance" })
                                header.Cell().Border(1).Padding(2).Text(text).SemiBold();
                        });
                        foreach (var row in d.Rows)
                        {
                            table.Cell().Border(1).Padding(2).Text(row.EntryDate.ToString("dd-MMM-yy"));
                            table.Cell().Border(1).Padding(2).Text(row.DocumentNo);
                            table.Cell().Border(1).Padding(2).Text(row.Narration);
                            table.Cell().Border(1).Padding(2).AlignRight().Text(row.DebitAmount == 0 ? "" : AmountInWords.FormatAmount(row.DebitAmount));
                            table.Cell().Border(1).Padding(2).AlignRight().Text(row.CreditAmount == 0 ? "" : AmountInWords.FormatAmount(row.CreditAmount));
                            table.Cell().Border(1).Padding(2).AlignRight().Text(AmountInWords.FormatAmount(row.RunningBalance));
                        }
                    });

                    col.Item().PaddingTop(10).AlignRight().Text($"Period Debits: {AmountInWords.FormatAmount(d.PeriodDebits)}   Period Credits: {AmountInWords.FormatAmount(d.PeriodCredits)}");
                    col.Item().PaddingTop(4).AlignRight().Text($"Closing Balance: {AmountInWords.FormatAmount(d.ClosingBalance)}").FontSize(11).Bold();
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
