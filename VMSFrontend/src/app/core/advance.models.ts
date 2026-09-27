/** Customer advances on Open trips (FSD §37.4, §48.8: "Record Advance | From an Open trip's Payments tab or
 * Receipts → New Advance"). Built here as its own small cluster — a genuinely separate, Open-trip-first
 * workflow from the invoice-centric Payments cluster (Record Payment/Write-off/Carry-Forward/Refund). */
export interface CreateAdvanceRequest {
  advanceDate: string;
  amount: number;
  paymentMethod: string;
  bankCashAccountId: number;
  instrumentNo: string;
  instrumentDate?: string | null;
  drawnOnBank?: string | null;
  paymentReference?: string | null;
  remarks?: string | null;
}

export interface MoveAdvanceRequest {
  toTripId: number;
  reason: string;
}

export interface RefundAdvanceRequest {
  reason: string;
  refundDate?: string | null;
}

export interface ReverseAdvanceRequest {
  reason: string;
  reversalDate?: string | null;
}

export interface CustomerAdvanceModel {
  customerAdvanceId: number;
  advanceNumber: string;
  customerId: number;
  tripId: number;
  tripNumber: string;
  advanceDate: string;
  amount: number;
  appliedAmount: number;
  refundedAmount: number;
  status: string;
  ledgerEntryId?: number | null;
}
