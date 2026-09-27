/**
 * Payments, receipts, settlements (write-off/discount) and credit handling (carry forward/refund) — FSD §37,
 * §37.5, §40, §48.5-§48.8. Advances (§37.4) are a later cluster's own scope.
 */

export type PaymentMethod = 'DirectToAccount' | 'BankCheque';

export interface BankCashAccountModel {
  bankCashAccountId: number;
  accountTitle: string;
  bankName: string;
  branchName: string;
  accountNumberLast4: string;
  currencyCode: string;
  isActive: boolean;
  rowVersion: string;
}

export interface SaveBankCashAccountRequest {
  accountTitle: string;
  bankName: string;
  branchName: string;
  accountNumberLast4: string;
  currencyCode?: string | null;
  isActive: boolean;
  /** Required to update; omitted when creating. */
  rowVersion?: string | null;
}

// ── Receipts / payments (§37) ───────────────────────────────────────────────────────

export type SettlementType = 'WriteOff' | 'Discount';

/** §37.5: "Settle remaining balance" — only valid with exactly one allocation. */
export interface SettleRemainingRequest {
  settlementType: SettlementType;
  amount?: number | null;
  settlementDate?: string | null;
  reason: string;
}

export interface PaymentAllocationRequest {
  invoiceId: number;
  amount: number;
}

export interface CreatePaymentReceiptRequest {
  customerId: number;
  receiptDate: string;
  receiptAmount: number;
  paymentMethod: PaymentMethod;
  bankCashAccountId: number;
  instrumentNo: string;
  instrumentDate?: string | null;
  drawnOnBank?: string | null;
  paymentReference?: string | null;
  attachmentDocumentId?: number | null;
  remarks?: string | null;
  confirmOverpayment?: boolean;
  confirmDuplicate?: boolean;
  allocations: PaymentAllocationRequest[];
  settleRemaining?: SettleRemainingRequest | null;
}

/** §47.3's own literal body for `POST /api/invoices/{id}/payments` — no `customerId` (derived from the invoice)
 * and no `allocations` (always exactly this one). */
export interface RecordInvoicePaymentRequest {
  receiptDate: string;
  amount: number;
  paymentMethod: PaymentMethod;
  bankCashAccountId: number;
  instrumentNo: string;
  instrumentDate?: string | null;
  drawnOnBank?: string | null;
  paymentReference?: string | null;
  attachmentId?: number | null;
  remarks?: string | null;
  confirmOverpayment?: boolean;
  confirmDuplicate?: boolean;
  settleRemaining?: SettleRemainingRequest | null;
}

export interface InvoicePaymentAllocationModel {
  invoicePaymentId: number;
  invoiceId: number;
  invoiceNumber: string;
  amount: number;
  ledgerEntryId?: number | null;
  invoiceBalance: number;
  invoicePaymentStatus: string;
}

export interface InvoiceSettlementModel {
  invoiceSettlementId: number;
  invoiceId: number;
  invoiceNumber: string;
  settlementNumber: string;
  settlementType: SettlementType;
  amount: number;
  settlementDate: string;
  reason: string;
  status: string;
  ledgerEntryId?: number | null;
  invoiceBalance: number;
  invoicePaymentStatus: string;
}

export interface CustomerReceiptModel {
  customerReceiptId: number;
  receiptNumber: string;
  customerId: number;
  receiptDate: string;
  receiptAmount: number;
  currencyCode: string;
  paymentMethod: PaymentMethod;
  bankCashAccountId: number;
  instrumentNo: string;
  status: string;
  remarks?: string | null;
  rowVersion: string;
  allocations: InvoicePaymentAllocationModel[];
  settlement?: InvoiceSettlementModel | null;
}

export interface ReversePaymentRequest {
  reason: string;
  reversalDate?: string | null;
}

export interface InvoicePaymentReversalModel {
  invoicePaymentId: number;
  invoiceId: number;
  invoiceNumber: string;
  amount: number;
  status: string;
  reversalLedgerEntryId: number;
  invoiceBalance: number;
  invoicePaymentStatus: string;
}

// ── Standalone settlement (write-off / discount) modal ──────────────────────────────

export interface CreateSettlementRequest {
  settlementType: SettlementType;
  amount: number;
  settlementDate?: string | null;
  reason: string;
}

export interface ReverseSettlementRequest {
  reason: string;
  reversalDate?: string | null;
}

// ── Carry forward / refund (§40) ────────────────────────────────────────────────────

export interface CarryForwardRequest {
  targetInvoiceId: number;
  amount: number;
  reason: string;
}

export interface CarryForwardModel {
  invoiceCreditCarryForwardId: number;
  sourceInvoiceId: number;
  sourceInvoiceNumber: string;
  sourceInvoiceBalance: number;
  targetInvoiceId: number;
  targetInvoiceNumber: string;
  targetInvoiceBalance: number;
  amount: number;
  reason: string;
}

export interface RefundCreditRequest {
  refundDate?: string | null;
  amount: number;
  paymentMethod: PaymentMethod;
  bankCashAccountId: number;
  reference?: string | null;
  reason: string;
}

export interface CustomerRefundModel {
  customerRefundId: number;
  invoiceId: number;
  invoiceNumber: string;
  amount: number;
  refundDate: string;
  paymentMethod: PaymentMethod;
  reference?: string | null;
  invoiceBalance: number;
  reason: string;
}

// ── Receipts List (§48.5 — see ReceiptListItem's own backend doc comment) ───────────

export interface ReceiptListItem {
  customerReceiptId: number;
  receiptNumber: string;
  customerId: number;
  customerName: string;
  receiptDate: string;
  receiptAmount: number;
  currencyCode: string;
  paymentMethod: PaymentMethod;
  bankCashAccountId: number;
  instrumentNo: string;
  status: string;
  invoiceNumbers: string[];
}

export interface ReceiptSearchFilter {
  customerId?: number | null;
  paymentMethod?: string | null;
  bankCashAccountId?: number | null;
  status?: string | null;
  fromDate?: string | null;
  toDate?: string | null;
}
