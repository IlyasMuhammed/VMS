import { AbstractControl, FormBuilder, ValidatorFn, Validators } from '@angular/forms';
import { UpdateVehicleRequest, Vehicle, VehicleInput } from '../../core/vehicle.models';
import { needsCapacity } from './vehicle-logic';

const text = (max: number, ...more: ValidatorFn[]): ValidatorFn[] => [Validators.maxLength(max), ...more];

/** The vehicle's identity, technical and operational fields as one form (FSD §16). One flat group, so a server error's field name finds its control. */
export function buildVehicleForm(fb: FormBuilder) {
  const year = new Date().getFullYear();
  return fb.nonNullable.group({
    registrationNo: ['', [Validators.required, Validators.maxLength(20)]],
    registrationCityId: fb.control<number | null>(null),
    branchId: fb.control<string | null>(null),
    chassisNo: ['', text(30)],
    engineNo: ['', text(30)],
    vehicleTypeId: fb.control<number | null>(null, Validators.required),
    makeId: fb.control<number | null>(null, Validators.required),
    model: ['', [Validators.required, Validators.maxLength(60)]],
    manufacturingYear: fb.control<number | null>(null, [Validators.min(1950), Validators.max(year + 1)]),
    colour: ['', text(30)],
    fuelType: ['', Validators.required],
    tankCapacity: fb.control<number | null>(null, Validators.min(0)),
    loadCapacity: fb.control<number | null>(null, Validators.min(0)),
    capacityUnit: fb.control<string | null>(null),
    axleConfigurationId: fb.control<number | null>(null),
    bodyTypeId: fb.control<number | null>(null),
    tyreCount: fb.control<number | null>(null, [Validators.min(2), Validators.max(22)]),
    gvw: fb.control<number | null>(null, Validators.min(0)),
    openingOdometer: fb.control<number | null>(null, Validators.min(0)),
    openingOdometerDate: fb.control<string | null>(null),
    defaultDriverId: fb.control<number | null>(null),
    fuelCardCompanyId: fb.control<number | null>(null),
    fuelCardNumber: ['', text(30)],
    trackerCompanyId: fb.control<number | null>(null),
    trackerDeviceId: ['', text(40)],
    remarks: ['', text(1000)],
    acquisitionDate: fb.control<string | null>(null),
    acquisitionType: fb.control<string | null>(null),
  });
}

export type VehicleForm = ReturnType<typeof buildVehicleForm>;

/** Sets a control's validators and says so (an event) when it became mandatory or stopped being, so its star and message follow at once. */
function setRules(control: AbstractControl, required: boolean, ...others: ValidatorFn[]): void {
  const was = control.hasValidator(Validators.required);
  control.setValidators(required ? [Validators.required, ...others] : others);
  control.updateValueAndValidity({ emitEvent: was !== required });
}

/**
 * The rules that depend on other fields (FSD §16): a load capacity for a truck, trailer, tanker or prime mover; a unit once a
 * capacity is entered; a date with an opening odometer; a card number with a fuel card company.
 */
export function syncVehicleRules(form: VehicleForm, vehicleTypeCode: string | null | undefined): void {
  const c = form.controls;
  setRules(c.loadCapacity, needsCapacity(vehicleTypeCode), Validators.min(0));
  setRules(c.capacityUnit, c.loadCapacity.value !== null);
  setRules(c.openingOdometerDate, c.openingOdometer.value !== null);
  setRules(c.fuelCardNumber, c.fuelCardCompanyId.value !== null, Validators.maxLength(30));
}

const blank = (v: string | null | undefined): string | null => (v && v.trim() ? v.trim() : null);

/** The form as the API wants it. Acquisition details are sent only by someone who may enter them. */
/** `draftData` is what a Draft keeps for the steps that are not saved on their own (the ownership answers); undefined leaves it out. */
export function toVehicleInput(form: VehicleForm, mayEnterAcquisition: boolean, reassignFuelCard = false, draftData?: string | null): VehicleInput {
  const v = form.getRawValue();
  const input: VehicleInput = {
    registrationNo: v.registrationNo.trim(), registrationCityId: v.registrationCityId, branchId: v.branchId, chassisNo: blank(v.chassisNo), engineNo: blank(v.engineNo),
    vehicleTypeId: v.vehicleTypeId, makeId: v.makeId, model: v.model.trim(), manufacturingYear: v.manufacturingYear, colour: blank(v.colour), fuelType: v.fuelType,
    tankCapacity: v.tankCapacity, loadCapacity: v.loadCapacity, capacityUnit: v.loadCapacity === null ? null : v.capacityUnit, axleConfigurationId: v.axleConfigurationId,
    bodyTypeId: v.bodyTypeId, tyreCount: v.tyreCount, gvw: v.gvw, openingOdometer: v.openingOdometer, openingOdometerDate: v.openingOdometer === null ? null : v.openingOdometerDate,
    defaultDriverId: v.defaultDriverId, fuelCardCompanyId: v.fuelCardCompanyId, fuelCardNumber: v.fuelCardCompanyId === null ? null : blank(v.fuelCardNumber),
    trackerCompanyId: v.trackerCompanyId, trackerDeviceId: blank(v.trackerDeviceId), remarks: blank(v.remarks), reassignFuelCard,
  };
  if (draftData !== undefined) input.draftData = draftData;
  if (mayEnterAcquisition) {
    input.acquisitionDate = v.acquisitionDate;
    input.acquisitionType = v.acquisitionType;
  }
  return input;
}

export const toUpdateRequest = (form: VehicleForm, mayEnterAcquisition: boolean, rowVersion: string, reassignFuelCard = false, draftData?: string | null): UpdateVehicleRequest => ({
  ...toVehicleInput(form, mayEnterAcquisition, reassignFuelCard, draftData),
  rowVersion,
});

/** Fills the form from a saved vehicle. */
export function patchVehicle(form: VehicleForm, v: Vehicle): void {
  const text = (s: string | null | undefined): string => s ?? '';
  form.patchValue(
    {
      registrationNo: v.registrationNo, registrationCityId: v.registrationCityId ?? null, branchId: v.branchId ?? null, chassisNo: text(v.chassisNo), engineNo: text(v.engineNo),
      vehicleTypeId: v.vehicleTypeId, makeId: v.makeId, model: v.model, manufacturingYear: v.manufacturingYear ?? null, colour: text(v.colour), fuelType: v.fuelType,
      tankCapacity: v.tankCapacity ?? null, loadCapacity: v.loadCapacity ?? null, capacityUnit: v.capacityUnit ?? null, axleConfigurationId: v.axleConfigurationId ?? null,
      bodyTypeId: v.bodyTypeId ?? null, tyreCount: v.tyreCount ?? null, gvw: v.gvw ?? null, openingOdometer: v.openingOdometer ?? null,
      openingOdometerDate: v.openingOdometerDate ?? null, defaultDriverId: v.defaultDriverId ?? null, fuelCardCompanyId: v.fuelCardCompanyId ?? null,
      fuelCardNumber: text(v.fuelCardNumber), trackerCompanyId: v.trackerCompanyId ?? null, trackerDeviceId: text(v.trackerDeviceId), remarks: text(v.remarks),
      acquisitionDate: v.acquisitionDate ?? null, acquisitionType: v.acquisitionType ?? null,
    },
    { emitEvent: false },
  );
}

/** Counts the fields that show a problem, to badge a step. */
export function problemCount(form: VehicleForm): number {
  return Object.values(form.controls).filter((c) => c.enabled && c.invalid && (c.touched || c.dirty)).length;
}
