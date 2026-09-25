import { FormBuilder, Validators } from '@angular/forms';
import { CustomerModel, SaveCustomerRequest } from '../../core/customer.models';

/** The General tab's own reactive form (FSD §10) — one flat group, unlike the Business Partner form's several
 * nested sections, since a customer's own General fields are all that a "create" ever needs; every child record
 * (contacts, addresses, …) is saved through its own dedicated endpoint once the customer itself exists. */
export function buildCustomerForm(fb: FormBuilder) {
  return fb.nonNullable.group({
    customerCode: fb.nonNullable.control(''),
    customerName: fb.nonNullable.control('', Validators.required),
    shortName: fb.nonNullable.control(''),
    addressLine1: fb.nonNullable.control('', Validators.required),
    addressLine2: fb.nonNullable.control(''),
    countryId: fb.control<number | null>(null),
    provinceState: fb.nonNullable.control(''),
    cityId: fb.control<number | null>(null),
    postalCode: fb.nonNullable.control(''),
    ntn: fb.nonNullable.control(''),
    strn: fb.nonNullable.control(''),
    otherRegistrationNo: fb.nonNullable.control(''),
    currencyCode: fb.nonNullable.control('PKR', Validators.required),
    paymentTermsDays: fb.nonNullable.control(30, [Validators.required, Validators.min(0), Validators.max(365)]),
    creditLimit: fb.control<number | null>(null, Validators.min(0)),
    remarks: fb.nonNullable.control(''),
  });
}

export type CustomerForm = ReturnType<typeof buildCustomerForm>;

export function patchCustomerForm(form: CustomerForm, c: CustomerModel): void {
  form.reset({
    customerCode: c.customerCode, customerName: c.customerName, shortName: c.shortName ?? '', addressLine1: c.addressLine1, addressLine2: c.addressLine2 ?? '',
    countryId: c.countryId, provinceState: c.provinceState ?? '', cityId: c.cityId ?? null, postalCode: c.postalCode ?? '', ntn: c.ntn ?? '', strn: c.strn ?? '',
    otherRegistrationNo: c.otherRegistrationNo ?? '', currencyCode: c.currencyCode, paymentTermsDays: c.paymentTermsDays, creditLimit: c.creditLimit ?? null, remarks: c.remarks ?? '',
  });
}

const blank = (v: string): string | null => (v.trim() === '' ? null : v.trim());

export function toSaveRequest(form: CustomerForm, rowVersion: string | null): SaveCustomerRequest {
  const f = form.getRawValue();
  return {
    customerCode: blank(f.customerCode), customerName: f.customerName.trim(), shortName: blank(f.shortName), addressLine1: f.addressLine1.trim(),
    addressLine2: blank(f.addressLine2), countryId: f.countryId, provinceState: blank(f.provinceState), cityId: f.cityId, postalCode: blank(f.postalCode),
    ntn: blank(f.ntn), strn: blank(f.strn), otherRegistrationNo: blank(f.otherRegistrationNo), currencyCode: f.currencyCode, paymentTermsDays: f.paymentTermsDays,
    creditLimit: f.creditLimit, remarks: blank(f.remarks), rowVersion,
  };
}
