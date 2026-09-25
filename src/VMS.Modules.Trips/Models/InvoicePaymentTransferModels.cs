namespace VMS.Modules.Trips.Models;

/// <summary>§40's own <c>InvoicePaymentTransfer</c> row, read back — <see cref="SourceKind"/>/<see cref="SourceId"/>
/// is this module's own discriminated generalisation of the FSD's literal <c>SourceInvoicePaymentId</c> field
/// (see <c>PaymentTransferSourceKinds</c>' own doc comment).</summary>
public sealed class PaymentTransferModel
{
    public long InvoicePaymentTransferId { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public long OldInvoiceId { get; set; }
    public string OldInvoiceNumber { get; set; } = string.Empty;
    public long NewInvoiceId { get; set; }
    public string NewInvoiceNumber { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public long SourceId { get; set; }
    public decimal AmountTransferred { get; set; }
    public DateOnly TransferDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}
