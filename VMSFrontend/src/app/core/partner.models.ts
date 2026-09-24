import { DuplicateMatch } from '../pages/partners/partner-logic';

export interface DriverModel {
  licenceNo: string;
  licenceType: string;
  licenceIssueDate?: string | null;
  licenceExpiryDate?: string | null;
  employmentType: string;
  dateOfJoining?: string | null;
  /** Absent when the signed-in user may not see salaries. */
  monthlyRate?: number | null;
  commissionBasis?: string | null;
  commissionValue?: number | null;
  bloodGroup?: string | null;
  emergencyContactName?: string | null;
  emergencyContactPhone?: string | null;
  guarantor?: string | null;
  driverAppAccess: boolean;
}

export interface VendorModel {
  supplyCategories: string[];
  /** Absent when the signed-in user may not see credit terms. */
  paymentTermDays?: number | null;
  creditLimit?: number | null;
}

export interface CustomerModel {
  customerType: string;
  billingCycle: string;
  rateBasis?: string | null;
  defaultRate?: number | null;
  creditLimit?: number | null;
  creditDays?: number | null;
}

export interface ContactModel {
  id?: number | null;
  contactName: string;
  designation?: string | null;
  mobile: string;
  email?: string | null;
  isPrimary: boolean;
  notes?: string | null;
}

export interface AddressModel {
  id?: number | null;
  addressType: string;
  line1: string;
  line2?: string | null;
  cityId: number | null;
  landmark?: string | null;
  isPrimary?: boolean;
}

export interface BankAccountModel {
  id?: number | null;
  accountTitle: string;
  bankName: string;
  branchCode?: string | null;
  accountNumber: string;
  iban?: string | null;
  isPrimary: boolean;
}

/** What is sent to create or update a partner. */
export interface PartnerInput {
  partyType: string;
  legalName: string;
  displayName?: string | null;
  cnic?: string | null;
  ntn?: string | null;
  strn?: string | null;
  filerStatus?: string | null;
  primaryMobile: string;
  alternatePhone?: string | null;
  email?: string | null;
  cityId: number | null;
  addressLine: string;
  branchId?: string | null;
  notes?: string | null;
  openingBalance?: number | null;
  openingBalanceDate?: string | null;
  driver?: DriverModel | null;
  vendor?: VendorModel | null;
  customer?: CustomerModel | null;
  contacts: ContactModel[];
  addresses: AddressModel[];
  bankAccounts: BankAccountModel[];
  acknowledgedDuplicateIds: number[];
}

export interface CreatePartnerRequest extends PartnerInput {
  roles: string[];
}

export interface UpdatePartnerRequest extends PartnerInput {
  rowVersion: string;
}

export interface PartnerRoleHistory {
  roleCode: string;
  isActive: boolean;
  effectiveFrom: string;
  effectiveTo?: string | null;
}

/** A partner as the API returns it. */
export interface Partner extends PartnerInput {
  id: number;
  bpCode: string;
  status: string;
  statusReason?: string | null;
  currency: string;
  roles: string[];
  roleHistory: PartnerRoleHistory[];
  createdOn: string;
  modifiedOn: string;
  rowVersion: string;
  /** A role's document is missing (BR-BP-005): a warning only, never a blocked save (OQ-04). */
  documentWarnings: string[];
}

export interface PartnerListItem {
  id: number;
  bpCode: string;
  legalName: string;
  displayName: string;
  partyType: string;
  roles: string[];
  cityId: number;
  city?: string | null;
  branchId?: string | null;
  branch?: string | null;
  primaryMobile: string;
  status: string;
  modifiedOn: string;
}

export interface PartnerPickerItem {
  id: number;
  bpCode: string;
  displayName: string;
  legalName: string;
  cityId: number;
  city?: string | null;
}

export interface PartnerUsage {
  kind: string;
  recordCode: string;
  description: string;
}

export interface HistoryChange {
  id: number;
  occurredAt: string;
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

export interface RoleLogItem {
  roleCode: string;
  action: string;
  effectiveDate: string;
  reason?: string | null;
  userName?: string | null;
  occurredOn: string;
}

export interface StatusLogItem {
  fromStatus: string;
  toStatus: string;
  reason?: string | null;
  effectiveDate: string;
  userName?: string | null;
  occurredOn: string;
}

export interface PartnerHistory {
  changes: { items: HistoryChange[]; totalCount: number; page: number; pageSize: number; totalPages: number };
  roles: RoleLogItem[];
  statuses: StatusLogItem[];
}

export interface DuplicateCheckRequest {
  partyType?: string | null;
  legalName?: string | null;
  cnic?: string | null;
  ntn?: string | null;
  primaryMobile?: string | null;
  cityId?: number | null;
  excludeId?: number | null;
}

export type { DuplicateMatch };
