/**
 * The Customer Ledger — accounts-receivable sub-ledger (FSD §40A, §48.5's own unnumbered "Customer Ledger" /
 * "Customer Balances" rows, and the "Ledger" tab on Invoice Detail, §48.5 screen 21). Every screen here reads
 * already-posted entries; nothing here posts one directly except the go-live opening balance (L16) — every
 * other entry (L1-L15) is a side effect of some other action (Submit, Record Payment, Regenerate, …) that
 * already has its own screen. `CustomerBalanceModel` (one customer/currency's own running total) already lives
 * in `customer.models.ts` — reused here, not redefined.
 */

export interface LedgerEntryModel {
  customerLedgerEntryId: number;
  entryNumber: string;
  customerId: number;
  invoiceId?: number | null;
  tripId?: number | null;
  entryType: string;
  entryDate: string;
  debitAmount: number;
  creditAmount: number;
  currencyCode: string;
  sourceType: string;
  sourceId: number;
  reversesEntryId?: number | null;
  documentNo: string;
  narration: string;
  customerSeq: number;
}

export interface CustomerLedgerStatementRowModel {
  customerLedgerEntryId: number;
  entryNumber: string;
  entryDate: string;
  entryType: string;
  invoiceId?: number | null;
  invoiceNumber?: string | null;
  documentNo: string;
  narration: string;
  debitAmount: number;
  creditAmount: number;
  runningBalance: number;
}

/** §40A.4's own "Customer Ledger (statement)" screen — LR-9: kept per customer per currency, so one currency at
 * a time, never blended into one number. */
export interface CustomerLedgerStatementModel {
  customerId: number;
  customerCode: string;
  customerName: string;
  currencyCode: string;
  from?: string | null;
  to?: string | null;
  openingBalance: number;
  periodDebits: number;
  periodCredits: number;
  closingBalance: number;
  overdueAmount: number;
  rows: CustomerLedgerStatementRowModel[];
}

export interface CustomerLedgerStatementFilter {
  from?: string | null;
  to?: string | null;
  invoiceId?: number | null;
  currencyCode?: string | null;
  /** Default on (§40A.4's own filter default). */
  includeReversedPairs?: boolean;
}

/** §40A.4's own "Invoice Ledger tab": every entry for one invoice, plus the reconciliation line. */
export interface InvoiceLedgerModel {
  invoiceId: number;
  invoiceNumber: string;
  invoiceBalance: number;
  ledgerBalance: number;
  reconciled: boolean;
  entries: LedgerEntryModel[];
}

/** §40A.4's own "Customer Balances" screen row. */
export interface CustomerBalanceSummaryModel {
  customerId: number;
  customerCode: string;
  customerName: string;
  currencyCode: string;
  balanceAmount: number;
  overdueAmount: number;
  creditAmount: number;
  lastPaymentDate?: string | null;
  lastInvoiceDate?: string | null;
}

/** §40A L16, §47.2: go-live opening balance — signed (positive = Dr/receivable, negative = Cr/credit). */
export interface PostOpeningBalanceRequest {
  amount: number;
  currencyCode?: string | null;
  asOfDate: string;
  reason?: string | null;
}

export interface OpeningBalanceModel {
  customerId: number;
  customerCode: string;
  currencyCode: string;
  amount: number;
  asOfDate: string;
  pseudoInvoiceNumber: string;
}
