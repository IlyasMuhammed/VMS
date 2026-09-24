import { AbstractControl, FormBuilder, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { Acquisition, Agreement, SaveAcquisitionRequest, SaveFinanceRequest } from '../../core/vehicle.models';
import { MAX_TENURE, agreementDateLimit, downPaymentMatches } from './vehicle-logic';

const blank = (v: string | null | undefined): string | null => (v && v.trim() ? v.trim() : null);

/** Sets a control's validators and says so (an event) when it became mandatory or stopped being, so its star and message follow at once. */
function setRules(control: AbstractControl, required: boolean, ...others: ValidatorFn[]): void {
  const was = control.hasValidator(Validators.required);
  control.setValidators(required ? [Validators.required, ...others] : others);
  control.updateValueAndValidity({ emitEvent: was !== required });
}

const money = (max = Number.MAX_SAFE_INTEGER): ValidatorFn[] => [Validators.min(0), Validators.max(max)];

/** What was paid cannot be more than the price (BR-VH-021). Reads its sibling, so the price is checked again whenever the paid amount is. */
const notMoreThanPrice: ValidatorFn = (control): ValidationErrors | null => {
  const price = control.parent?.get('purchasePrice')?.value as number | null | undefined;
  const paid = control.value as number | null;
  return price !== null && price !== undefined && paid !== null && paid > price ? { max: { max: price } } : null;
};

/** The acquisition block (FSD §18.1) as one flat form, so a server error's field name finds its control. */
export function buildAcquisitionForm(fb: FormBuilder) {
  return fb.nonNullable.group({
    acquisitionDate: fb.control<string | null>(null, Validators.required),
    acquisitionType: fb.control<string | null>(null, Validators.required),
    sellerId: fb.control<number | null>(null),
    purchasePrice: fb.control<number | null>(null, Validators.min(0.01)),
    amountPaid: fb.control<number | null>(null, [Validators.min(0), notMoreThanPrice]),
    paymentMode: fb.control<string | null>(null),
    paymentReference: ['', Validators.maxLength(60)],
    registrationCost: fb.control<number | null>(null, money()),
  });
}
export type AcquisitionForm = ReturnType<typeof buildAcquisitionForm>;

/** A price needs an amount paid (which may be nothing), and something paid needs a payment mode (FSD §18.1). */
export function syncAcquisitionRules(form: AcquisitionForm): void {
  const c = form.controls;
  setRules(c.amountPaid, c.purchasePrice.value !== null, Validators.min(0), notMoreThanPrice);
  setRules(c.paymentMode, (c.amountPaid.value ?? 0) > 0);
}

export function patchAcquisition(form: AcquisitionForm, a: Acquisition): void {
  form.reset(
    {
      acquisitionDate: a.acquisitionDate ?? null, acquisitionType: a.acquisitionType ?? null, sellerId: a.sellerId ?? null, purchasePrice: a.purchasePrice ?? null,
      amountPaid: a.amountPaid ?? null, paymentMode: a.paymentMode ?? null, paymentReference: a.paymentReference ?? '', registrationCost: a.registrationCost ?? null,
    },
    { emitEvent: false },
  );
  syncAcquisitionRules(form);
}

export function toAcquisitionRequest(form: AcquisitionForm, rowVersion: string): SaveAcquisitionRequest {
  const v = form.getRawValue();
  const paid = (v.amountPaid ?? 0) > 0;
  return {
    acquisitionDate: v.acquisitionDate, acquisitionType: v.acquisitionType, sellerId: v.sellerId, purchasePrice: v.purchasePrice, amountPaid: v.purchasePrice === null ? null : v.amountPaid,
    paymentMode: paid ? v.paymentMode : null, paymentReference: paid ? blank(v.paymentReference) : null, registrationCost: v.registrationCost, rowVersion,
  };
}

/** The bank block (FSD §18.2) as one flat form. */
export function buildFinanceForm(fb: FormBuilder) {
  return fb.nonNullable.group({
    financeTypeId: fb.control<number | null>(null, Validators.required),
    bankId: fb.control<number | null>(null, Validators.required),
    agreementNo: ['', [Validators.required, Validators.maxLength(60)]],
    agreementDate: fb.control<string | null>(null, Validators.required),
    financeAmount: fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    downPayment: fb.control<number | null>(null, [Validators.required, Validators.min(0)]),
    installmentAmount: fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    frequency: ['Monthly', Validators.required],
    tenure: fb.control<number | null>(null, [Validators.required, Validators.min(1), Validators.max(MAX_TENURE)]),
    firstDueDate: fb.control<string | null>(null, Validators.required),
    markupRate: fb.control<number | null>(null, [Validators.min(0), Validators.max(100)]),
    residualAmount: fb.control<number | null>(null, money()),
    securityDeposit: fb.control<number | null>(null, money()),
  });
}
export type FinanceForm = ReturnType<typeof buildFinanceForm>;

/**
 * The checks that read across the two blocks: the down payment is the amount paid (BR-VH-009), the agreement is dated within
 * 90 days of the acquisition, and the first installment is not before the agreement. Called whenever any of them changes.
 */
export function syncFinanceRules(finance: FinanceForm, acquisition: AcquisitionForm): void {
  const f = finance.controls;
  const paid = acquisition.controls.amountPaid.value;
  setRules(f.downPayment, true, Validators.min(0), (c) => (downPaymentMatches(c.value, paid) ? null : { custom: { message: 'The down payment must be the same as the amount paid at creation.' } }));

  const limit = agreementDateLimit(acquisition.controls.acquisitionDate.value);
  const agreementDate = f.agreementDate.value;
  setRules(f.agreementDate, true, () => (limit && agreementDate && agreementDate > limit ? { max: { max: limit } } : null));
  setRules(f.firstDueDate, true, () => (agreementDate && f.firstDueDate.value && f.firstDueDate.value < agreementDate ? { min: { min: agreementDate } } : null));
}

export function patchFinance(form: FinanceForm, a: Agreement | null): void {
  form.reset(
    a
      ? {
          financeTypeId: a.financeTypeId, bankId: a.bankId, agreementNo: a.agreementNo, agreementDate: a.agreementDate, financeAmount: a.financeAmount ?? null,
          downPayment: a.downPayment ?? null, installmentAmount: a.installmentAmount ?? null, frequency: a.frequency, tenure: a.tenure, firstDueDate: a.firstDueDate,
          markupRate: a.markupRate ?? null, residualAmount: a.residualAmount ?? null, securityDeposit: a.securityDeposit ?? null,
        }
      : undefined,
    { emitEvent: false },
  );
}

export function toFinanceRequest(form: FinanceForm, rowVersion: string | null, confirmMismatch: boolean): SaveFinanceRequest {
  const v = form.getRawValue();
  return {
    financeTypeId: v.financeTypeId, bankId: v.bankId, agreementNo: v.agreementNo.trim(), agreementDate: v.agreementDate, financeAmount: v.financeAmount, downPayment: v.downPayment,
    installmentAmount: v.installmentAmount, frequency: v.frequency, tenure: v.tenure, firstDueDate: v.firstDueDate, markupRate: v.markupRate, residualAmount: v.residualAmount,
    securityDeposit: v.securityDeposit, confirmMismatch, rowVersion,
  };
}

/** How many fields show a problem, to badge the step. */
export function problemsIn(...forms: AbstractControl[]): number {
  let n = 0;
  const walk = (c: AbstractControl): void => {
    const group = (c as { controls?: Record<string, AbstractControl> }).controls;
    if (group) Object.values(group).forEach(walk);
    else if (c.enabled && c.invalid && (c.touched || c.dirty)) n++;
  };
  forms.forEach(walk);
  return n;
}
