export interface PartnerRef {
  id: number;
  bpCode: string;
  name: string;
}

/** What is sent to save a vehicle's identity, technical and operational fields (FSD §16). */
export interface VehicleInput {
  registrationNo: string;
  registrationCityId?: number | null;
  chassisNo?: string | null;
  engineNo?: string | null;
  vehicleTypeId: number | null;
  makeId: number | null;
  model: string;
  manufacturingYear?: number | null;
  colour?: string | null;
  fuelType: string;
  tankCapacity?: number | null;
  loadCapacity?: number | null;
  capacityUnit?: string | null;
  axleConfigurationId?: number | null;
  bodyTypeId?: number | null;
  tyreCount?: number | null;
  gvw?: number | null;
  branchId?: string | null;
  openingOdometer?: number | null;
  openingOdometerDate?: string | null;
  defaultDriverId?: number | null;
  fuelCardCompanyId?: number | null;
  fuelCardNumber?: string | null;
  trackerCompanyId?: number | null;
  trackerDeviceId?: string | null;
  remarks?: string | null;
  acquisitionDate?: string | null;
  acquisitionType?: string | null;
  draftData?: string | null;
  reassignFuelCard?: boolean;
}

export interface UpdateVehicleRequest extends VehicleInput {
  rowVersion: string;
}

export interface Relation {
  id: number;
  category: string;
  counterparty?: PartnerRef | null;
  effectiveFrom: string;
  effectiveTo?: string | null;
  agreementEndDate?: string | null;
  agreementReference?: string | null;
  sharePercent?: number | null;
  sharingBasis?: string | null;
  fixedMonthlyAmount?: number | null;
  expenseSharingRule?: string | null;
  rentAmount?: number | null;
  rentFrequency?: string | null;
  rentDueDay?: number | null;
  securityDeposit?: number | null;
  arrangementType?: string | null;
  agreedAmount?: number | null;
  revenueSharePercent?: number | null;
}

export interface Vehicle extends VehicleInput {
  id: number;
  vehicleCode: string;
  status: string;
  currentCategory?: string | null;
  currentCounterparty?: PartnerRef | null;
  defaultDriver?: PartnerRef | null;
  fuelCardCompany?: PartnerRef | null;
  trackerCompany?: PartnerRef | null;
  relation?: Relation | null;
  relationHistory: Relation[];
  createdOn: string;
  modifiedOn: string;
  rowVersion: string;
}

export interface VehicleListItem {
  id: number;
  vehicleCode: string;
  registrationNo: string;
  vehicleTypeId: number;
  vehicleType?: string | null;
  makeId: number;
  make?: string | null;
  model: string;
  currentCategory?: string | null;
  counterparty?: PartnerRef | null;
  branchId?: string | null;
  branch?: string | null;
  status: string;
  driver?: PartnerRef | null;
  modifiedOn: string;
}

export interface CategoryDetails {
  counterpartyId?: number | null;
  endDate?: string | null;
  agreementReference?: string | null;
  sharePercent?: number | null;
  sharingBasis?: string | null;
  fixedMonthlyAmount?: number | null;
  expenseSharingRule?: string | null;
  rentAmount?: number | null;
  rentFrequency?: string | null;
  rentDueDay?: number | null;
  securityDeposit?: number | null;
  arrangementType?: string | null;
  agreedAmount?: number | null;
  revenueSharePercent?: number | null;
}

export interface ChangeCategoryRequest {
  category: string;
  effectiveDate?: string | null;
  reason?: string | null;
  details: CategoryDetails;
}

export interface DisposeRequest {
  kind: string;
  date?: string | null;
  reason?: string | null;
  counterpartyId?: number | null;
  amount?: number | null;
  reference?: string | null;
}

export interface AttachedItem {
  id: number;
  vehicleId: number;
  itemTypeId: number;
  itemType?: string | null;
  description: string;
  serialNo?: string | null;
  supplier?: PartnerRef | null;
  installationDate: string;
  /** Absent when the signed-in user may not see costs. */
  cost?: number | null;
  warrantyUntil?: string | null;
  condition?: string | null;
  status: string;
  detachedOn?: string | null;
  detachReason?: string | null;
  transferredFromItemId?: number | null;
  transferredToVehicleId?: number | null;
}

export interface AttachItemRequest {
  itemTypeId: number | null;
  description: string;
  serialNo?: string | null;
  supplierId?: number | null;
  installationDate?: string | null;
  cost?: number | null;
  warrantyUntil?: string | null;
  condition?: string | null;
}

export interface OdometerReading {
  id: number;
  readingDate: string;
  km: number;
  source: string;
  notes?: string | null;
}

export interface LifecycleItem {
  eventType: string;
  fromStatus?: string | null;
  toStatus?: string | null;
  fromCategory?: string | null;
  toCategory?: string | null;
  effectiveDate: string;
  reason?: string | null;
  reference?: string | null;
  counterparty?: PartnerRef | null;
  amount?: number | null;
  userName?: string | null;
  occurredOn: string;
}

export interface VehicleHistoryChange {
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

export interface VehicleHistory {
  changes: { items: VehicleHistoryChange[]; totalCount: number; page: number; pageSize: number; totalPages: number };
  lifecycle: LifecycleItem[];
}

/** A way a vehicle is linked to a partner. */
export interface LinkedVehicle {
  vehicleId: number;
  vehicleCode: string;
  registrationNo: string;
  vehicleStatus: string;
  link: string;
  detail?: string | null;
  from?: string | null;
  to?: string | null;
  isCurrent: boolean;
}

// ── Acquisition and finance (FSD §18) ──────────────────────────────────────────────

/** The acquisition block of a Draft. Amounts are absent for someone without `VEH.FIELD.COST.VIEW`. */
export interface Acquisition {
  vehicleId: number;
  vehicleStatus: string;
  acquisitionDate?: string | null;
  acquisitionType?: string | null;
  sellerId?: number | null;
  seller?: PartnerRef | null;
  purchasePrice?: number | null;
  amountPaid?: number | null;
  paymentMode?: string | null;
  paymentReference?: string | null;
  registrationCost?: number | null;
  /** The vehicle's row version: sent back so a change made by someone else in the meantime is not overwritten. */
  rowVersion: string;
}

export interface SaveAcquisitionRequest {
  acquisitionDate: string | null;
  acquisitionType: string | null;
  sellerId: number | null;
  purchasePrice: number | null;
  amountPaid: number | null;
  paymentMode: string | null;
  paymentReference: string | null;
  registrationCost: number | null;
  rowVersion: string;
}

/** The bank's agreement for a vehicle. Amounts are absent for someone without `VEH.FIELD.FINANCE.VIEW`. */
export interface Agreement {
  id: number;
  vehicleId: number;
  financeTypeId: number;
  financeType?: string | null;
  bankId: number;
  bank?: PartnerRef | null;
  agreementNo: string;
  agreementDate: string;
  financeAmount?: number;
  downPayment?: number;
  installmentAmount?: number;
  frequency: string;
  tenure: number;
  firstDueDate: string;
  markupRate?: number | null;
  residualAmount?: number | null;
  securityDeposit?: number | null;
  totalPayable?: number;
  status: string;
  rowVersion: string;
}

export interface SaveFinanceRequest {
  financeTypeId: number | null;
  bankId: number | null;
  agreementNo: string;
  agreementDate: string | null;
  financeAmount: number | null;
  downPayment: number | null;
  installmentAmount: number | null;
  frequency: string;
  tenure: number | null;
  firstDueDate: string | null;
  markupRate: number | null;
  residualAmount: number | null;
  securityDeposit: number | null;
  /** Set to go ahead when finance plus down payment is not the purchase price. */
  confirmMismatch: boolean;
  rowVersion?: string | null;
}
// ── Activation, the summary, the schedule and the ledger ───────────────────────────

/** The fields of an ownership category as the API takes them (FSD §17). Amounts need `VEH.FIELD.FINANCE.VIEW`. */
export interface CategoryDetails {
  counterpartyId?: number | null;
  endDate?: string | null;
  agreementReference?: string | null;
  sharePercent?: number | null;
  sharingBasis?: string | null;
  fixedMonthlyAmount?: number | null;
  expenseSharingRule?: string | null;
  rentAmount?: number | null;
  rentFrequency?: string | null;
  rentDueDay?: number | null;
  securityDeposit?: number | null;
  arrangementType?: string | null;
  agreedAmount?: number | null;
  revenueSharePercent?: number | null;
}

export interface ScheduleDueDate {
  installmentNo: number;
  dueDate: string;
}

export interface ActivateVehicleRequest {
  category: string;
  details: CategoryDetails;
  items: AttachItemRequest[];
  dueDates: ScheduleDueDate[];
  reassignFuelCard: boolean;
  releaseFromOther: boolean;
  rowVersion?: string | null;
}

export interface ChecklistItem {
  code: string;
  label: string;
  ok: boolean;
  /** False for something worth knowing that does not stop the activation. */
  blocking: boolean;
  message?: string | null;
}

export interface PostingPreview {
  type: string;
  subType?: string | null;
  amount?: number;
  date: string;
  partner?: PartnerRef | null;
  reference: string;
}

export interface ScheduleRow {
  installmentNo: number;
  dueDate: string;
  amount?: number;
  isResidual: boolean;
}

export interface ActivationCheck {
  canActivate: boolean;
  items: ChecklistItem[];
  postings: PostingPreview[];
  schedule: ScheduleRow[];
}

export interface AgreementSummary {
  agreementId: number;
  status: string;
  bank?: PartnerRef | null;
  totalPayable?: number;
  residual?: number;
  outstanding?: number;
  installmentsPaid: number;
  installmentsTotal: number;
  nextDueDate?: string | null;
  nextDueAmount?: number | null;
  overdueCount: number;
  overdueAmount?: number;
}

export interface FinancialSummary {
  vehicleId: number;
  acquisitionCost?: number;
  majorExpenses?: number;
  totalCost?: number;
  paidToDate?: number;
  deposits?: number;
  agreement?: AgreementSummary | null;
}

export interface Installment {
  id: number;
  agreementId: number;
  installmentNo: number;
  dueDate: string;
  expectedAmount?: number;
  paidAmount?: number;
  remainingAmount?: number;
  paidOn?: string | null;
  isResidual: boolean;
  status: string;
  rowVersion: string;
}

export interface PayInstallmentRequest {
  amount: number | null;
  paidOn: string | null;
  paymentMode: string | null;
  reference: string | null;
  rowVersion: string;
}

export interface InstallmentPayment {
  installment: Installment;
  transactionId: number;
  agreementSettled: boolean;
}

// ── Recurring charges and payables (FSD §19A) ───────────────────────────────────────

/** One row of a vehicle's configured recurring charges, current or historic (BR-VH-033). */
export interface RecurringCharge {
  id: number;
  seriesId: string;
  vehicleId: number;
  chargeTypeId: number;
  chargeType?: string | null;
  payee?: PartnerRef | null;
  expenseTypeId: number;
  expenseType?: string | null;
  amount?: number | null;
  amountBasis: string;
  frequency: string;
  dueDay?: number | null;
  dueMonth?: number | null;
  customIntervalDays?: number | null;
  startDate: string;
  endDate?: string | null;
  occurrenceCount?: number | null;
  generatedCount: number;
  postingMode: string;
  generateLeadDays: number;
  taxWithholdingPercent?: number | null;
  nextDueDate?: string | null;
  isActive: boolean;
  effectiveTo?: string | null;
  endReason?: string | null;
  rowVersion: string;
}

export interface SaveRecurringChargeRequest {
  chargeTypeId: number | null;
  payeeId: number | null;
  expenseTypeId: number | null;
  amount: number | null;
  amountBasis: string | null;
  frequency: string | null;
  dueDay?: number | null;
  dueMonth?: number | null;
  customIntervalDays?: number | null;
  startDate: string | null;
  endDate?: string | null;
  occurrenceCount?: number | null;
  postingMode: string | null;
  generateLeadDays?: number | null;
  taxWithholdingPercent?: number | null;
  rowVersion?: string | null;
}

export interface EndRecurringChargeRequest {
  endDate?: string | null;
  reason: string | null;
}

/** One Due or Overdue item, of either kind (BR-VH-029): a generated charge entry, or an installment surfaced without a parallel schedule. */
export interface Payable {
  id: number;
  kind: 'RecurringCharge' | 'Installment';
  vehicleId: number;
  vehicleRegistrationNo?: string | null;
  branchId?: string | null;
  branch?: string | null;
  chargeId?: number | null;
  chargeTypeId?: number | null;
  chargeType?: string | null;
  payee?: PartnerRef | null;
  dueDate: string;
  expectedAmount?: number | null;
  status: string;
}

export interface ConfirmChargeEntryRequest {
  amount: number | null;
  paidOn: string | null;
  paymentMode?: string | null;
  reference?: string | null;
  remarks?: string | null;
}

export interface WaiveChargeEntryRequest {
  reason: string | null;
}

export interface ChargeEntryPayment {
  transactionId: number;
}

export interface BulkConfirmItem {
  kind: 'RecurringCharge' | 'Installment';
  vehicleId: number;
  id: number;
  rowVersion?: string | null;
  amount?: number | null;
}

export interface BulkConfirmRequest {
  items: BulkConfirmItem[];
  paidOn: string | null;
  paymentMode?: string | null;
}

export interface BulkConfirmFailure {
  kind: string;
  vehicleId: number;
  id: number;
  message: string;
}

export interface BulkConfirmResult {
  succeeded: number;
  failed: BulkConfirmFailure[];
}

export interface PayablesSummary {
  dueWithin7Days: number;
  dueWithin7DaysAmount?: number;
  overdueCount: number;
  overdueAmount?: number;
}

export interface LedgerEntry {
  id: number;
  type: string;
  subType?: string | null;
  amount?: number;
  date: string;
  partner?: PartnerRef | null;
  reference?: string | null;
  source: string;
  isSystemGenerated: boolean;
  reason?: string | null;
  reversesTransactionId?: number | null;
  isReversed: boolean;
  /** A receipt was uploaded for this entry (source §7). */
  hasReceipt: boolean;
  receiptFileName?: string | null;
  createdOn: string;
}
