/** A trip's own operational tabs (FSD §25, §27, §29, §30, §23, §31, §48.4 screen 14's tab list): Timeline
 * events, Fuel, Expenses, Income, Documents/POD, Issues, P&L. */

// ── Events / Timeline (§25) ──────────────────────────────────────────────────────────

export type TripEventType =
  | 'Created' | 'Planned' | 'Assigned' | 'Started' | 'InTransit' | 'ArrivedPickup' | 'LoadingComplete' | 'ArrivedDelivery' | 'Delivered'
  | 'PODUploaded' | 'Completed' | 'OnHold' | 'Resumed' | 'Cancelled' | 'Fuel' | 'Expense' | 'Issue' | 'Note' | 'StatusSkipped';
export type TripEventSource = 'Manual' | 'DriverApp' | 'GPS' | 'System';

export interface TripEventModel {
  tripEventId: number;
  tripId: number;
  eventType: TripEventType;
  eventDateTime: string;
  cityId?: number | null;
  locationText?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  odometer?: number | null;
  userId: number;
  remarks?: string | null;
  attachmentId?: number | null;
  source: TripEventSource;
  clientEventId?: string | null;
}

/** Only `Note` may be created directly here — every other event type is produced by its own dedicated action
 * (a status transition, an issue, a POD upload). */
export interface CreateTripEventRequest {
  eventType: 'Note';
  eventDateTime?: string | null;
  cityId?: number | null;
  locationText?: string | null;
  odometer?: number | null;
  remarks?: string | null;
}

// ── Fuel (§27) ───────────────────────────────────────────────────────────────────────

export type FuelType = 'Diesel' | 'Petrol' | 'CNG' | 'Other';
export type FuelPaymentMethod = 'Cash' | 'FuelCard' | 'Other';

export interface TripFuelModel {
  tripFuelId: number;
  tripId: number;
  vehicleId: number;
  fuelDateTime: string;
  fuelType: FuelType;
  quantity: number;
  rate: number;
  amount: number;
  odometer?: number | null;
  stationName?: string | null;
  cityId?: number | null;
  paymentMethod: FuelPaymentMethod;
  fuelCardId?: number | null;
  otherPaymentText?: string | null;
  attachmentId?: number | null;
  remarks?: string | null;
  currencyCode: string;
  source: TripEventSource;
  isVoided: boolean;
  voidReason?: string | null;
  voidedBy?: number | null;
  voidedAtUtc?: string | null;
  warnings: string[];
}

export interface CreateTripFuelRequest {
  fuelDateTime?: string | null;
  fuelType: FuelType;
  quantity: number;
  rate: number;
  amount?: number | null;
  odometer?: number | null;
  stationName?: string | null;
  cityId?: number | null;
  paymentMethod: FuelPaymentMethod;
  fuelCardId?: number | null;
  otherPaymentText?: string | null;
  remarks?: string | null;
  currencyCode?: string | null;
}

export interface VoidTripFuelRequest {
  reason: string;
}

export interface TripFuelListModel {
  entries: TripFuelModel[];
  totalQuantity: number;
  totalAmount: number;
  fuelEfficiencyKmPerLitre?: number | null;
}

// ── Expenses (§29) ───────────────────────────────────────────────────────────────────

export type ExpenseApprovalStatus = 'Pending' | 'Approved' | 'Rejected';

export interface TripExpenseModel {
  tripExpenseId: number;
  tripId: number;
  expenseDate: string;
  expenseTypeId: number;
  otherExpenseType?: string | null;
  description?: string | null;
  quantity?: number | null;
  rate?: number | null;
  amount: number;
  reference?: string | null;
  businessPartnerId?: number | null;
  paymentMethod: string;
  attachmentId?: number | null;
  approvalStatus: ExpenseApprovalStatus;
  rejectionReason?: string | null;
  decidedBy?: number | null;
  decidedAtUtc?: string | null;
  source: TripEventSource;
  isVoided: boolean;
  voidReason?: string | null;
  voidedBy?: number | null;
  voidedAtUtc?: string | null;
}

export interface CreateTripExpenseRequest {
  expenseDate?: string | null;
  expenseTypeId: number;
  otherExpenseType?: string | null;
  description?: string | null;
  quantity?: number | null;
  rate?: number | null;
  amount?: number | null;
  reference?: string | null;
  businessPartnerId?: number | null;
  paymentMethod: string;
}

export interface DecideTripExpenseRequest {
  approved: boolean;
  reason?: string | null;
}

export interface VoidTripExpenseRequest {
  reason: string;
}

// ── Income (§30) ─────────────────────────────────────────────────────────────────────

export interface TripIncomeModel {
  tripIncomeId: number;
  tripId: number;
  customerId: number;
  incomeTypeId: number;
  amount: number;
  currencyCode: string;
  incomeDate: string;
  isBillable: boolean;
  invoiceLineId?: number | null;
  reference?: string | null;
  remarks?: string | null;
  isVoided: boolean;
  voidReason?: string | null;
  voidedBy?: number | null;
  voidedAtUtc?: string | null;
}

export interface CreateTripIncomeRequest {
  customerId?: number | null;
  incomeTypeId: number;
  amount: number;
  currencyCode?: string | null;
  incomeDate?: string | null;
  isBillable: boolean;
  reference?: string | null;
  remarks?: string | null;
}

export interface VoidTripIncomeRequest {
  reason: string;
}

// ── Documents & POD (§23) ────────────────────────────────────────────────────────────

export type TripDocumentType = 'LoadingSlip' | 'GatePass' | 'Challan' | 'Photo' | 'Other';

export interface TripDocumentModel {
  tripDocumentId: number;
  tripId: number;
  documentType: TripDocumentType;
  originalFileName: string;
  sizeBytes: number;
  uploadedBy: number;
  uploadedAtUtc: string;
  source: TripEventSource;
}

export type PodStatus = 'Uploaded' | 'Approved' | 'Rejected';

export interface TripPodModel {
  tripPODId: number;
  tripId: number;
  originalFileName: string;
  sizeBytes: number;
  uploadedBy: number;
  uploadedAtUtc: string;
  source: TripEventSource;
  status: PodStatus;
  approvedBy?: number | null;
  approvedAtUtc?: string | null;
  rejectedReason?: string | null;
}

export interface RejectPodRequest {
  reason: string;
}

export interface TripDownloadLinkModel {
  url: string;
  expiresAtUtc: string;
}

// ── Issues (§23) ─────────────────────────────────────────────────────────────────────

export type TripIssueType = 'Breakdown' | 'Accident' | 'Delay' | 'CustomerHold' | 'RouteBlocked' | 'Other';
export type IssueSeverity = 'Low' | 'Medium' | 'High';

export interface TripIssueModel {
  tripIssueId: number;
  tripId: number;
  issueType: TripIssueType;
  severity: IssueSeverity;
  description: string;
  photoDocumentId?: number | null;
  reportedBy: number;
  reportedAtUtc: string;
  isResolved: boolean;
  resolvedBy?: number | null;
  resolvedAtUtc?: string | null;
  resolutionNotes?: string | null;
}

export interface CreateTripIssueRequest {
  issueType: TripIssueType;
  severity: IssueSeverity;
  description: string;
  photoDocumentId?: number | null;
  /** §23: "An issue can put the trip On Hold" — the reporter's own choice. */
  putOnHold: boolean;
}

export interface ResolveTripIssueRequest {
  resolutionNotes?: string | null;
}

// ── Operational P&L (§31) ────────────────────────────────────────────────────────────

export interface TripPnLModel {
  tripId: number;
  tripNumber: string;
  isPriced: boolean;
  revenue?: number | null;
  approvedIncome: number;
  fuel: number;
  approvedExpenses: number;
  operationalPnL?: number | null;
  currencyCode: string;
}
