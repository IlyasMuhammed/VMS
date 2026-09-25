/**
 * The Customer master and its child records (FSD §10-§15) — the second FSD's own entity, deliberately independent
 * of the Business Partner "Customer" role that already exists in `partner.models.ts` (a different concept that
 * happens to share an English word; see Business Rule §49.1: "Customer is independent from Business Partner").
 */

// ── Customer ───────────────────────────────────────────────────────────────────────

export interface CustomerModel {
  customerId: number;
  customerCode: string;
  customerName: string;
  shortName?: string | null;
  addressLine1: string;
  addressLine2?: string | null;
  countryId: number;
  provinceState?: string | null;
  cityId?: number | null;
  postalCode?: string | null;
  ntn?: string | null;
  strn?: string | null;
  otherRegistrationNo?: string | null;
  currencyCode: string;
  paymentTermsDays: number;
  creditLimit?: number | null;
  status: 'Draft' | 'Active' | 'Inactive';
  inactiveReason?: string | null;
  remarks?: string | null;
  rowVersion: string;
}

export interface SaveCustomerRequest {
  customerCode?: string | null;
  customerName: string;
  shortName?: string | null;
  addressLine1: string;
  addressLine2?: string | null;
  countryId?: number | null;
  provinceState?: string | null;
  cityId?: number | null;
  postalCode?: string | null;
  ntn?: string | null;
  strn?: string | null;
  otherRegistrationNo?: string | null;
  currencyCode?: string | null;
  paymentTermsDays?: number | null;
  creditLimit?: number | null;
  remarks?: string | null;
  rowVersion?: string | null;
}

export interface ChangeCustomerStatusRequest {
  reason?: string | null;
  rowVersion: string;
}

export interface ActivationCheckModel {
  canActivate: boolean;
  missingItems: string[];
}

export interface CustomerHistoryChange {
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

export interface CustomerHistory {
  changes: { items: CustomerHistoryChange[]; totalCount: number; page: number; pageSize: number; totalPages: number };
}

export interface CustomerPickerItem {
  customerId: number;
  customerCode: string;
  customerName: string;
}

/** §40A's own per-currency running balance (CC-38) — the List and Details screens (§48.2 screens 1-2) each show
 * it beside the customer, though the full statement/ledger screens themselves are a separate task (§48.5). */
export interface CustomerBalanceModel {
  customerId: number;
  currencyCode: string;
  balanceAmount: number;
}

/** §13A's own currency master — `GET /api/currencies` is gated to `TRP.CURRENCY.MANAGE` (the Currency Setup
 * screen's own Admin-only permission per §48.8), a narrower gate than `TRP.CUSTOMER.EDIT` needs just to populate
 * a currency dropdown; the Customer form degrades to a plain text field when it 403s (documented at the call
 * site) rather than this task widening a permission it does not own. */
export interface CurrencyModel {
  currencyId: number;
  currencyCode: string;
  currencyName: string;
  symbol?: string | null;
  decimalPlaces: number;
  isBase: boolean;
  status: string;
}

// ── Contacts (§11) ─────────────────────────────────────────────────────────────────

export type ContactPurpose = 'Billing' | 'Operations' | 'POD' | 'Other';

export interface CustomerContactModel {
  customerContactId: number;
  customerId: number;
  name: string;
  designation?: string | null;
  mobile1: string;
  mobile2?: string | null;
  telephone?: string | null;
  email?: string | null;
  availabilityTime?: string | null;
  purpose: ContactPurpose[];
  isPrimary: boolean;
  status: 'Active' | 'Inactive';
}

export interface CustomerContactSaveResult {
  contact: CustomerContactModel;
  warnings: string[];
}

export interface SaveCustomerContactRequest {
  name: string;
  designation?: string | null;
  mobile1: string;
  mobile2?: string | null;
  telephone?: string | null;
  email?: string | null;
  availabilityTime?: string | null;
  purpose?: ContactPurpose[] | null;
  isPrimary: boolean;
}

// ── Billing addresses (§12) ────────────────────────────────────────────────────────

export interface CustomerBillingAddressModel {
  customerBillingAddressId: number;
  customerId: number;
  addressName: string;
  addressLine1: string;
  addressLine2?: string | null;
  cityId: number;
  provinceState?: string | null;
  countryId: number;
  postalCode?: string | null;
  ntn?: string | null;
  strn?: string | null;
  isDefault: boolean;
  effectiveFrom: string;
  effectiveTo?: string | null;
  status: 'Active' | 'Inactive';
}

export interface SaveCustomerBillingAddressRequest {
  addressName: string;
  addressLine1: string;
  addressLine2?: string | null;
  cityId: number;
  provinceState?: string | null;
  countryId?: number | null;
  postalCode?: string | null;
  ntn?: string | null;
  strn?: string | null;
  isDefault: boolean;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
}

// ── Billing configuration (§13) ────────────────────────────────────────────────────

export type DuplicateReferenceBehaviour = 'Allow' | 'Warn' | 'Block';

export interface CustomerBillingConfigurationModel {
  customerBillingConfigurationId: number;
  customerId: number;
  paymentTermsDays: number;
  currencyCode: string;
  defaultBillingAddressId?: number | null;
  defaultInvoiceTemplateId?: number | null;
  invoiceNumberPrefix: string;
  podRequired: boolean;
  evidenceRequired: boolean;
  evidencePageSize: number;
  duplicateReferenceBehaviour: DuplicateReferenceBehaviour;
  customerReferenceRequired: boolean;
  statementEmailContactId?: number | null;
  effectiveFrom: string;
  effectiveTo?: string | null;
}

export interface SaveCustomerBillingConfigurationRequest {
  paymentTermsDays?: number | null;
  currencyCode?: string | null;
  defaultBillingAddressId?: number | null;
  defaultInvoiceTemplateId?: number | null;
  invoiceNumberPrefix?: string | null;
  podRequired: boolean;
  evidenceRequired: boolean;
  evidencePageSize?: number | null;
  duplicateReferenceBehaviour?: DuplicateReferenceBehaviour | null;
  customerReferenceRequired: boolean;
  statementEmailContactId?: number | null;
  effectiveFrom?: string | null;
}

// ── Tax / deduction rules (§14) ────────────────────────────────────────────────────

export type TaxType = 'Percentage' | 'Fixed';
export type CalculationBasis = 'GrossTripAmount' | 'InvoiceSubtotal';

export interface CustomerTaxRuleModel {
  customerTaxRuleId: number;
  customerId: number;
  taxName: string;
  taxCode: string;
  taxType: TaxType;
  taxPercentage?: number | null;
  fixedAmount?: number | null;
  applicable: boolean;
  calculationBasis: CalculationBasis;
  sequence: number;
  effectiveFrom: string;
  effectiveTo?: string | null;
  status: 'Active' | 'Inactive';
  supersedesRuleId?: number | null;
  remarks?: string | null;
}

/** Used for both "add a new rule" (POST) and "replace an existing one" (POST .../replace) — the same body shape. */
export interface SaveCustomerTaxRuleRequest {
  taxName: string;
  taxCode: string;
  taxType: TaxType;
  taxPercentage?: number | null;
  fixedAmount?: number | null;
  applicable: boolean;
  calculationBasis: CalculationBasis;
  sequence: number;
  effectiveFrom?: string | null;
  remarks?: string | null;
  confirmReplace: boolean;
}

/** In-place edit (PUT): identity fields (name/code/type/effective range) are deliberately not here — a rate
 * change goes through {@link SaveCustomerTaxRuleRequest} + `/replace` instead, so it leaves its own history row. */
export interface UpdateCustomerTaxRuleRequest {
  taxPercentage?: number | null;
  fixedAmount?: number | null;
  applicable: boolean;
  calculationBasis?: CalculationBasis | null;
  sequence: number;
  remarks?: string | null;
}

// ── Invoice templates (§15) ────────────────────────────────────────────────────────

export type InvoiceTemplateType = 'SystemStandard' | 'HTMLPDF' | 'DocumentTemplate' | 'ReportDefinition';

export interface CustomerInvoiceTemplateModel {
  customerInvoiceTemplateId: number;
  customerId: number;
  templateName: string;
  version: number;
  templateType: InvoiceTemplateType;
  templateReference: string;
  effectiveFrom: string;
  effectiveTo?: string | null;
  isDefault: boolean;
  status: 'Draft' | 'Active' | 'Inactive';
}

export interface CreateCustomerInvoiceTemplateRequest {
  templateName: string;
  templateType?: InvoiceTemplateType | null;
  templateReference?: string | null;
}

export interface NewInvoiceTemplateVersionRequest {
  templateType?: InvoiceTemplateType | null;
  templateReference?: string | null;
}

export interface ActivateCustomerInvoiceTemplateRequest {
  effectiveFrom?: string | null;
  isDefault: boolean;
}

export interface ApplicableInvoiceTemplatesModel {
  templates: CustomerInvoiceTemplateModel[];
  requiresSelection: boolean;
}
