/**
 * Invoice generation, detail, submit/cancel, list and history (FSD §32-§36, §46.4-46.5, §48.5 screens 19-21).
 * Payments, transfers, regeneration, evidence, documents and versions are a later cluster's own scope — see each
 * tab's own doc comment in `invoice-detail.component.ts` for exactly what is and is not built yet.
 */

export type InvoiceStatus = 'Draft' | 'Generated' | 'Submitted' | 'Inactive' | 'Cancelled';
export type InvoicePaymentStatus = 'Unpaid' | 'PartiallyPaid' | 'Paid';
export type SubmissionChannel = 'Hand' | 'Email' | 'Portal';

// ── Eligible-trip search (§32.1, §39) ───────────────────────────────────────────────

export interface EligibleTripsQuery {
  customerId: number;
  periodFrom: string;
  periodTo: string;
  /** Defaults to today. */
  invoiceDate?: string | null;
  /** Set when searching to regenerate an existing invoice — its own trip links count as available, not "Already Invoiced." */
  regeneratingInvoiceId?: number | null;
}

export type EligibleTripCategory = 'Available' | 'AlreadyInvoiced' | 'Blocked';
export type TripBlockingCode = 'RATE_MISSING' | 'POD_MISSING' | 'INACTIVE' | 'NOT_COMPLETED' | 'CURRENCY_MISMATCH';

export interface EligibleTripsSummary {
  completedTrips: number;
  alreadyInvoiced: number;
  blocked: number;
  available: number;
  availableAmount: number;
}

export interface EligibleTripRow {
  tripId: number;
  tripNumber: string;
  tripDate: string;
  category: EligibleTripCategory;
  amount?: number | null;
  vehicle?: string | null;
  route?: string | null;
  customerTripReference?: string | null;
  rowVersion: string;
}

export interface TripBlockingError {
  code: TripBlockingCode;
  tripId: number;
  tripDate: string;
  configuration?: string | null;
}

export interface InvoiceOverlapRow {
  invoiceId: number;
  invoiceNumber: string;
  periodFrom: string;
  periodTo: string;
  paymentStatus: string;
  fullyPaid: boolean;
}

export interface InvoiceTemplateOption {
  id: number;
  name: string;
  version: number;
  isDefault: boolean;
}

export interface InvoiceAddressOption {
  id: number;
  name: string;
  isDefault: boolean;
}

export interface TaxRulePreview {
  taxCode: string;
  rate?: number | null;
  calculationBasis: string;
}

export interface ResolvedInvoiceOptions {
  templateOptions: InvoiceTemplateOption[];
  billingAddressOptions: InvoiceAddressOption[];
  taxRulesPreview: TaxRulePreview[];
}

export interface EligibleTripsResult {
  summary: EligibleTripsSummary;
  trips: EligibleTripRow[];
  blockingErrors: TripBlockingError[];
  overlaps: InvoiceOverlapRow[];
  resolved: ResolvedInvoiceOptions;
}

// ── Creation (§32.2-32.4, §33, §35, §52) ────────────────────────────────────────────

export interface CreateInvoiceAdjustmentRequest {
  adjustmentMonth: string;
  amount: number;
  note: string;
  referenceInvoiceNo?: string | null;
}

export interface CreateInvoiceRequest {
  customerId: number;
  periodFrom: string;
  periodTo: string;
  invoiceDate?: string | null;
  /** Required only when more than one template is applicable. */
  customerInvoiceTemplateId?: number | null;
  /** Defaults to the customer's own default Active address. */
  customerBillingAddressId?: number | null;
  tripIds: number[];
  adjustments: CreateInvoiceAdjustmentRequest[];
  /** true saves as Draft; false (default) Generates directly (§36: no approval step). */
  saveAsDraft?: boolean;
  remarks?: string | null;
}

export interface InvoiceLineModel {
  invoiceLineId: number;
  lineNo: number;
  tripId?: number | null;
  lineType: string;
  tripNumber?: string | null;
  tripDate?: string | null;
  routeLabel?: string | null;
  vehicleRegNo?: string | null;
  driverName?: string | null;
  description: string;
  quantity: number;
  rate: number;
  amount: number;
}

export interface InvoiceAdjustmentModel {
  invoiceAdjustmentId: number;
  adjustmentMonth: string;
  adjustmentAmount: number;
  adjustmentNote: string;
  referenceInvoiceNo?: string | null;
  sequence: number;
}

export interface InvoiceTaxLineModel {
  invoiceTaxLineId: number;
  taxName: string;
  taxCode: string;
  applicable: boolean;
  amount: number;
}

export interface InvoiceModel {
  invoiceId: number;
  invoiceNumber: string;
  version: number;
  customerId: number;
  customerCode: string;
  customerName: string;
  currencyCode: string;
  periodFrom: string;
  periodTo: string;
  invoiceDate: string;
  dueDate?: string | null;
  customerInvoiceTemplateId?: number | null;
  templateVersion?: number | null;
  customerBillingAddressId?: number | null;
  totalTripAmount: number;
  totalAdjustment: number;
  grossAmount: number;
  totalDeduction: number;
  netAmount: number;
  paidAmount: number;
  advanceAppliedAmount: number;
  writeOffAmount: number;
  discountAmount: number;
  transferredInAmount: number;
  transferredOutAmount: number;
  carryForwardInAmount: number;
  carryForwardOutAmount: number;
  refundedAmount: number;
  balanceAmount: number;
  status: InvoiceStatus;
  paymentStatus: InvoicePaymentStatus;
  isActive: boolean;
  submittedOn?: string | null;
  submissionChannel?: SubmissionChannel | null;
  cancelledOn?: string | null;
  cancelReason?: string | null;
  remarks?: string | null;
  rowVersion: string;
  lines: InvoiceLineModel[];
  adjustments: InvoiceAdjustmentModel[];
  taxLines: InvoiceTaxLineModel[];
  evidenceStatus: string;
  warnings: string[];
}

// ── Submit / cancel (§36, §47.3) ────────────────────────────────────────────────────

export interface SubmitInvoiceRequest {
  rowVersion: string;
  submissionChannel?: SubmissionChannel | null;
  /** Defaults to today (the ledger date). */
  submittedOn?: string | null;
  acknowledgementDocumentId?: number | null;
}

export interface CancelInvoiceRequest {
  rowVersion: string;
  reason: string;
}

// ── List / search (§48.5 — no numbered "Invoice List" screen; see InvoicesApi's own doc comment) ──────────────

export interface InvoiceListItem {
  invoiceId: number;
  invoiceNumber: string;
  version: number;
  customerId: number;
  customerName: string;
  periodFrom: string;
  periodTo: string;
  invoiceDate: string;
  dueDate?: string | null;
  netAmount: number;
  balanceAmount: number;
  currencyCode: string;
  status: InvoiceStatus;
  paymentStatus: InvoicePaymentStatus;
  isActive: boolean;
}

export interface InvoiceSearchFilter {
  customerId?: number | null;
  status?: string | null;
  paymentStatus?: string | null;
  fromDate?: string | null;
  toDate?: string | null;
  isActive?: boolean | null;
  search?: string | null;
}

// ── Regeneration (§38, §39, screen 21's own "Versions" tab) ─────────────────────────

export interface RegenerateInvoiceRequest {
  periodFrom?: string | null;
  periodTo?: string | null;
  invoiceDate?: string | null;
  customerInvoiceTemplateId?: number | null;
  customerBillingAddressId?: number | null;
  tripIds: number[];
  adjustments: CreateInvoiceAdjustmentRequest[];
  /** §38 step 3: "Re-price trips from rate master." */
  repriceTrips: boolean;
  regenerationReason: string;
  remarks?: string | null;
}

export interface PaymentTransferModel {
  invoicePaymentTransferId: number;
  transferNumber: string;
  oldInvoiceId: number;
  oldInvoiceNumber: string;
  newInvoiceId: number;
  newInvoiceNumber: string;
  sourceKind: string;
  sourceId: number;
  amountTransferred: number;
  transferDate: string;
  reason: string;
}

export interface InvoiceRegenerationModel {
  invoice: InvoiceModel;
  previousInvoiceId: number;
  previousInvoiceNumber: string;
  releasedTripNumbers: string[];
  transfers: PaymentTransferModel[];
  warnings: string[];
}

/** §48.5's own literal `GET /api/invoices/{id}/versions` — every invoice sharing this one's own regeneration
 * chain, oldest first. A chain of exactly one is an invoice that has never been regenerated. */
export interface InvoiceVersionModel {
  invoiceId: number;
  invoiceNumber: string;
  version: number;
  invoiceDate: string;
  status: string;
  isActive: boolean;
  netAmount: number;
  balanceAmount: number;
  regenerationReason?: string | null;
  regeneratedOn?: string | null;
}

// ── Evidence (§41, screen 24) ────────────────────────────────────────────────────────

export interface InvoiceEvidenceVehiclePageModel {
  vehicleRegNo: string;
  firstPage: number;
  lastPage: number;
  lineCount: number;
  subtotal: number;
}

export interface InvoiceEvidenceModel {
  invoiceEvidenceId: number;
  invoiceId: number;
  evidenceVersion: number;
  groupBy: string;
  pageSize: number;
  status: string;
  errorMessage?: string | null;
  pageCount: number;
  lineCount: number;
  sizeBytes: number;
  sha256: string;
  generatedBy?: number | null;
  generatedAtUtc?: string | null;
  vehiclePages: InvoiceEvidenceVehiclePageModel[];
}

export interface InvoiceEvidenceDownloadModel {
  evidence: InvoiceEvidenceModel;
  url: string;
  expiresAtUtc: string;
}

// ── History (§48.1) ──────────────────────────────────────────────────────────────────

export interface InvoiceAuditHistoryChange {
  id: number;
  occurredAt: string;
  groupId: string;
  userName?: string | null;
  entity: string;
  recordId: string;
  action: string;
  field?: string | null;
  oldValue?: string | null;
  newValue?: string | null;
  reason?: string | null;
  restricted: boolean;
}

export interface InvoiceAuditHistory {
  changes: { items: InvoiceAuditHistoryChange[]; totalCount: number; page: number; pageSize: number; totalPages: number };
}
