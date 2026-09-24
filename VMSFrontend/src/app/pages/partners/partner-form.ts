import { DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { merge } from 'rxjs';
import { AbstractControl, FormArray, FormBuilder, FormControl, FormGroup, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import {
  AddressModel, BankAccountModel, ContactModel, CreatePartnerRequest, CustomerModel, DriverModel, Partner, PartnerInput, UpdatePartnerRequest, VendorModel,
} from '../../core/partner.models';
import { ApiFieldError } from '../../core/message-format';
import { MessagesService } from '../../core/messages.service';
import { applyServerErrors } from '../../shared/server-errors';
import { formPath, isAccountNumber, isCnic, isEmail, isIban, isNtn, isPhone, normalizeMobile, requirements } from './partner-logic';

/** Empty is fine (a required control says so itself); a value must match the format. */
const format =
  (test: (value: string) => boolean): ValidatorFn =>
  (control: AbstractControl): ValidationErrors | null => {
    const value = (control.value ?? '') as string;
    return value.trim() === '' || test(value.trim()) ? null : { pattern: true };
  };

export const cnicFormat = format(isCnic);
export const ntnFormat = format(isNtn);
export const mobileFormat = format((v) => normalizeMobile(v) !== null);
export const phoneFormat = format(isPhone);
export const emailFormat: ValidatorFn = (control) => {
  const value = ((control.value ?? '') as string).trim();
  return value === '' || isEmail(value) ? null : { email: true };
};
export const accountNumberFormat = format(isAccountNumber);
export const ibanFormat = format(isIban);

const text = (max: number, ...more: ValidatorFn[]): ValidatorFn[] => [Validators.maxLength(max), ...more];

// ── The form ───────────────────────────────────────────────────────────────────────

export function buildDriverGroup(fb: FormBuilder): FormGroup {
  return fb.nonNullable.group({
    licenceNo: ['', [Validators.required, Validators.maxLength(30)]],
    licenceType: ['', Validators.required],
    licenceIssueDate: fb.control<string | null>(null),
    licenceExpiryDate: fb.control<string | null>(null, Validators.required),
    employmentType: ['', Validators.required],
    dateOfJoining: fb.control<string | null>(null),
    monthlyRate: fb.control<number | null>(null, Validators.min(0)),
    commissionBasis: ['None'],
    commissionValue: fb.control<number | null>(null, Validators.min(0)),
    bloodGroup: ['', text(5)],
    emergencyContactName: ['', text(100)],
    emergencyContactPhone: ['', [Validators.maxLength(20), phoneFormat]],
    guarantor: ['', text(150)],
    driverAppAccess: [false],
  });
}

export function buildVendorGroup(fb: FormBuilder): FormGroup {
  return fb.nonNullable.group({
    supplyCategories: fb.nonNullable.control<string[]>([], Validators.required),
    paymentTermDays: fb.control<number | null>(null, Validators.min(0)),
    creditLimit: fb.control<number | null>(null, Validators.min(0)),
  });
}

export function buildCustomerGroup(fb: FormBuilder): FormGroup {
  return fb.nonNullable.group({
    customerType: ['', Validators.required],
    billingCycle: ['', Validators.required],
    rateBasis: fb.control<string | null>(null),
    defaultRate: fb.control<number | null>(null, Validators.min(0)),
    creditLimit: fb.control<number | null>(null, Validators.min(0)),
    creditDays: fb.control<number | null>(null, Validators.min(0)),
  });
}

/**
 * One form for the whole partner, flat at the top so a server error's field name (`legalName`, `driver.licenceNo`,
 * `contacts[1].mobile`) finds its control. The tabs each show a part of it. A panel of a role the partner does not hold
 * is disabled, so it neither blocks saving nor counts as an error.
 */
export function buildPartnerForm(fb: FormBuilder) {
  const form = fb.nonNullable.group({
    partyType: ['Person', Validators.required],
    legalName: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(150)]],
    displayName: ['', text(60)],
    cnic: ['', cnicFormat],
    ntn: ['', ntnFormat],
    strn: ['', text(20)],
    filerStatus: ['Unknown'],
    primaryMobile: ['', [Validators.required, mobileFormat]],
    alternatePhone: ['', [Validators.maxLength(20), phoneFormat]],
    email: ['', [Validators.maxLength(100), emailFormat]],
    cityId: fb.control<number | null>(null, Validators.required),
    addressLine: ['', [Validators.required, Validators.maxLength(250)]],
    branchId: fb.control<string | null>(null),
    openingBalance: fb.control<number | null>(null),
    openingBalanceDate: fb.control<string | null>(null),
    notes: ['', text(1000)],
    roles: fb.nonNullable.control<string[]>([], Validators.required),
    driver: buildDriverGroup(fb),
    vendor: buildVendorGroup(fb),
    customer: buildCustomerGroup(fb),
    contacts: fb.array<FormGroup>([]),
    addresses: fb.array<FormGroup>([]),
    bankAccounts: fb.array<FormGroup>([]),
  });
  syncRequirements(form);
  return form;
}

export type PartnerForm = ReturnType<typeof buildPartnerForm>;

/**
 * Sets a control's validators, and says so (an event) when it became mandatory or stopped being, so the field's star and
 * message follow at once. Nothing is emitted when only the format rules were re-applied, which keeps typing quiet.
 */
function setRules(control: AbstractControl, required: boolean, ...others: ValidatorFn[]): void {
  const was = control.hasValidator(Validators.required);
  control.setValidators(required ? [Validators.required, ...others] : others);
  control.updateValueAndValidity({ emitEvent: was !== required });
}
/**
 * Makes the form say what is mandatory now (FSD §6, FR-BP-009): CNIC for a Person, NTN for a Company, email for a Customer
 * or Bank; and switches on the panel of each role held and off the panels of the rest. Call after party type or roles change.
 */
export function syncRequirements(form: PartnerForm): void {
  const roles = form.controls.roles.value;
  const need = requirements(form.controls.partyType.value, roles);

  setRules(form.controls.cnic, need.cnic, cnicFormat);
  setRules(form.controls.ntn, need.ntn, ntnFormat);
  setRules(form.controls.email, need.email, Validators.maxLength(100), emailFormat);

  const toggle = (group: FormGroup, on: boolean): void => {
    if (on && group.disabled) group.enable({ emitEvent: false });
    if (!on && group.enabled) group.disable({ emitEvent: false });
  };
  toggle(form.controls.driver, roles.includes('Driver'));
  toggle(form.controls.vendor, roles.includes('Vendor'));
  toggle(form.controls.customer, roles.includes('Customer'));
  syncDriverRules(form.controls.driver);
}

/** The driver rules that depend on other fields: a date of joining for an Employee, a value once a commission basis is chosen. */
export function syncDriverRules(driver: FormGroup): void {
  const joining = driver.controls['dateOfJoining'];
  setRules(joining, driver.controls['employmentType'].value === 'Employee');

  const basis = driver.controls['commissionBasis'].value as string;
  const value = driver.controls['commissionValue'];
  setRules(value, basis !== 'None', Validators.min(0), ...(basis === 'Percent' ? [Validators.max(100)] : []));
}

/**
 * Keeps the form's rules in step with what the person chooses: change the party type or the roles and what is mandatory
 * and which panels are on follows; choose Employee or a commission basis and the driver rules follow. Lasts as long as the caller.
 */
export function wireRules(form: PartnerForm, destroyRef: DestroyRef): void {
  merge(form.controls.partyType.valueChanges, form.controls.roles.valueChanges)
    .pipe(takeUntilDestroyed(destroyRef))
    .subscribe(() => syncRequirements(form));
  merge(form.controls.driver.controls['employmentType'].valueChanges, form.controls.driver.controls['commissionBasis'].valueChanges)
    .pipe(takeUntilDestroyed(destroyRef))
    .subscribe(() => syncDriverRules(form.controls.driver));
}

// ── Child rows ─────────────────────────────────────────────────────────────────────

export function newContact(fb: FormBuilder, model?: Partial<ContactModel>): FormGroup {
  return fb.nonNullable.group({
    id: fb.control<number | null>(model?.id ?? null),
    contactName: [model?.contactName ?? '', [Validators.required, Validators.maxLength(100)]],
    designation: [model?.designation ?? '', text(60)],
    mobile: [model?.mobile ?? '', [Validators.required, mobileFormat]],
    email: [model?.email ?? '', [Validators.maxLength(100), emailFormat]],
    isPrimary: [model?.isPrimary ?? false],
    notes: [model?.notes ?? '', text(250)],
  });
}

export function newAddress(fb: FormBuilder, model?: Partial<AddressModel>): FormGroup {
  return fb.nonNullable.group({
    id: fb.control<number | null>(model?.id ?? null),
    addressType: [model?.addressType ?? '', Validators.required],
    line1: [model?.line1 ?? '', [Validators.required, Validators.maxLength(250)]],
    line2: [model?.line2 ?? '', text(250)],
    cityId: fb.control<number | null>(model?.cityId ?? null, Validators.required),
    landmark: [model?.landmark ?? '', text(150)],
  });
}

export function newBankAccount(fb: FormBuilder, model?: Partial<BankAccountModel>): FormGroup {
  return fb.nonNullable.group({
    id: fb.control<number | null>(model?.id ?? null),
    accountTitle: [model?.accountTitle ?? '', [Validators.required, Validators.maxLength(150)]],
    bankName: [model?.bankName ?? '', [Validators.required, Validators.maxLength(100)]],
    branchCode: [model?.branchCode ?? '', text(80)],
    accountNumber: [model?.accountNumber ?? '', [Validators.required, accountNumberFormat]],
    iban: [model?.iban ?? '', ibanFormat],
    isPrimary: [model?.isPrimary ?? false],
  });
}

/** Marks one row primary and clears the others: only one per collection (FSD §8). Passing null clears all. */
export function setPrimary(rows: FormArray<FormGroup>, index: number | null): void {
  rows.controls.forEach((row, i) => row.controls['isPrimary'].setValue(index !== null && i === index));
  rows.markAsDirty();
}

// ── To and from the API ────────────────────────────────────────────────────────────

const blank = (v: string | null | undefined): string | null => (v && v.trim() ? v.trim() : null);
const num = (v: number | string | null | undefined): number | null => (v === null || v === undefined || v === '' ? null : Number(v));

function driverOf(g: FormGroup, salary: boolean): DriverModel {
  const v = g.getRawValue();
  const model: DriverModel = {
    licenceNo: (v.licenceNo as string).trim().toUpperCase(),
    licenceType: v.licenceType,
    licenceIssueDate: v.licenceIssueDate,
    licenceExpiryDate: v.licenceExpiryDate,
    employmentType: v.employmentType,
    dateOfJoining: v.dateOfJoining,
    bloodGroup: blank(v.bloodGroup),
    emergencyContactName: blank(v.emergencyContactName),
    emergencyContactPhone: blank(v.emergencyContactPhone),
    guarantor: blank(v.guarantor),
    driverAppAccess: !!v.driverAppAccess,
  };
  if (salary) {
    model.monthlyRate = num(v.monthlyRate);
    model.commissionBasis = v.commissionBasis || 'None';
    model.commissionValue = v.commissionBasis === 'None' ? null : num(v.commissionValue);
  }
  return model;
}

function vendorOf(g: FormGroup, credit: boolean): VendorModel {
  const v = g.getRawValue();
  const model: VendorModel = { supplyCategories: v.supplyCategories };
  if (credit) {
    model.paymentTermDays = num(v.paymentTermDays);
    model.creditLimit = num(v.creditLimit);
  }
  return model;
}

function customerOf(g: FormGroup, credit: boolean): CustomerModel {
  const v = g.getRawValue();
  const model: CustomerModel = { customerType: v.customerType, billingCycle: v.billingCycle, rateBasis: v.rateBasis || null, defaultRate: num(v.defaultRate) };
  if (credit) {
    model.creditLimit = num(v.creditLimit);
    model.creditDays = num(v.creditDays);
  }
  return model;
}

export interface Permissions {
  salary: boolean;
  credit: boolean;
  opening: boolean;
}

/** The form as the API wants it. Panels of roles not held are left out; values the person may not see are not sent (the server ignores them anyway). */
export function toInput(form: PartnerForm, allowed: Permissions, acknowledgedDuplicateIds: number[]): PartnerInput {
  const v = form.getRawValue();
  const roles = v.roles;
  const input: PartnerInput = {
    partyType: v.partyType,
    legalName: v.legalName.trim(),
    displayName: blank(v.displayName),
    cnic: blank(v.cnic),
    ntn: blank(v.ntn),
    strn: blank(v.strn),
    filerStatus: v.filerStatus || 'Unknown',
    primaryMobile: v.primaryMobile.trim(),
    alternatePhone: blank(v.alternatePhone),
    email: blank(v.email),
    cityId: v.cityId,
    addressLine: v.addressLine.trim(),
    branchId: v.branchId,
    notes: blank(v.notes),
    driver: roles.includes('Driver') ? driverOf(form.controls.driver, allowed.salary) : null,
    vendor: roles.includes('Vendor') ? vendorOf(form.controls.vendor, allowed.credit) : null,
    customer: roles.includes('Customer') ? customerOf(form.controls.customer, allowed.credit) : null,
    contacts: form.controls.contacts.controls.map((g) => {
      const c = g.getRawValue();
      return { id: c.id, contactName: c.contactName.trim(), designation: blank(c.designation), mobile: c.mobile.trim(), email: blank(c.email), isPrimary: !!c.isPrimary, notes: blank(c.notes) };
    }),
    addresses: form.controls.addresses.controls.map((g) => {
      const a = g.getRawValue();
      return { id: a.id, addressType: a.addressType, line1: a.line1.trim(), line2: blank(a.line2), cityId: a.cityId, landmark: blank(a.landmark) };
    }),
    bankAccounts: form.controls.bankAccounts.controls.map((g) => {
      const b = g.getRawValue();
      return {
        id: b.id, accountTitle: b.accountTitle.trim(), bankName: b.bankName.trim(), branchCode: blank(b.branchCode),
        accountNumber: b.accountNumber.trim(), iban: blank(b.iban), isPrimary: !!b.isPrimary,
      };
    }),
    acknowledgedDuplicateIds,
  };
  if (allowed.opening) {
    input.openingBalance = num(v.openingBalance);
    input.openingBalanceDate = v.openingBalanceDate;
  }
  return input;
}

export const toCreateRequest = (form: PartnerForm, allowed: Permissions, acknowledged: number[]): CreatePartnerRequest => ({
  ...toInput(form, allowed, acknowledged),
  roles: form.controls.roles.value,
});

export const toUpdateRequest = (form: PartnerForm, allowed: Permissions, acknowledged: number[], rowVersion: string): UpdatePartnerRequest => ({
  ...toInput(form, allowed, acknowledged),
  rowVersion,
});

/** Fills the form from a saved partner. Child rows are rebuilt; the panels a partner never had keep their empty defaults. */
export function patchPartner(fb: FormBuilder, form: PartnerForm, p: Partner): void {
  const text = (v: string | null | undefined): string => v ?? '';

  form.controls.contacts.clear({ emitEvent: false });
  form.controls.addresses.clear({ emitEvent: false });
  form.controls.bankAccounts.clear({ emitEvent: false });
  p.contacts.forEach((c) => form.controls.contacts.push(newContact(fb, c), { emitEvent: false }));
  p.addresses.forEach((a) => form.controls.addresses.push(newAddress(fb, a), { emitEvent: false }));
  p.bankAccounts.forEach((b) => form.controls.bankAccounts.push(newBankAccount(fb, b), { emitEvent: false }));

  form.patchValue(
    {
      partyType: p.partyType, legalName: p.legalName, displayName: text(p.displayName), cnic: text(p.cnic), ntn: text(p.ntn), strn: text(p.strn),
      filerStatus: p.filerStatus ?? 'Unknown', primaryMobile: p.primaryMobile, alternatePhone: text(p.alternatePhone), email: text(p.email),
      cityId: p.cityId, addressLine: p.addressLine, branchId: p.branchId ?? null, openingBalance: p.openingBalance ?? null, openingBalanceDate: p.openingBalanceDate ?? null,
      notes: text(p.notes), roles: p.roles,
    },
    { emitEvent: false },
  );

  if (p.driver) {
    const d = p.driver;
    form.controls.driver.patchValue({
      licenceNo: d.licenceNo, licenceType: d.licenceType, licenceIssueDate: d.licenceIssueDate ?? null, licenceExpiryDate: d.licenceExpiryDate ?? null,
      employmentType: d.employmentType, dateOfJoining: d.dateOfJoining ?? null, monthlyRate: d.monthlyRate ?? null, commissionBasis: d.commissionBasis ?? 'None',
      commissionValue: d.commissionValue ?? null, bloodGroup: text(d.bloodGroup), emergencyContactName: text(d.emergencyContactName),
      emergencyContactPhone: text(d.emergencyContactPhone), guarantor: text(d.guarantor), driverAppAccess: d.driverAppAccess,
    }, { emitEvent: false });
  }
  if (p.vendor) {
    form.controls.vendor.patchValue({ supplyCategories: p.vendor.supplyCategories, paymentTermDays: p.vendor.paymentTermDays ?? null, creditLimit: p.vendor.creditLimit ?? null }, { emitEvent: false });
  }
  if (p.customer) {
    const c = p.customer;
    form.controls.customer.patchValue({
      customerType: c.customerType, billingCycle: c.billingCycle, rateBasis: c.rateBasis ?? null, defaultRate: c.defaultRate ?? null,
      creditLimit: c.creditLimit ?? null, creditDays: c.creditDays ?? null,
    }, { emitEvent: false });
  }

  syncRequirements(form);
  form.updateValueAndValidity({ emitEvent: false });
}

/**
 * Puts the API's field errors on the form's controls (`contacts[1].mobile` finds `contacts.1.mobile`). Returns what has no
 * control here (the whole form, or a field this form does not show) for the caller to show once.
 */
export function placeErrors(form: AbstractControl, errors: readonly ApiFieldError[], messages: MessagesService): ApiFieldError[] {
  return applyServerErrors(form, errors.map((e) => (e.field ? { ...e, field: formPath(e.field) } : e)), messages);
}

/** The controls of each tab, to count the problems on it. */
export function invalidCount(control: AbstractControl | null | undefined): number {
  if (!control || control.disabled) return 0;
  if (control instanceof FormControl) return control.invalid && (control.touched || control.dirty) ? 1 : 0;
  const children = (control as FormGroup | FormArray).controls;
  return Object.values(children ?? {}).reduce((sum: number, c) => sum + invalidCount(c), 0);
}
