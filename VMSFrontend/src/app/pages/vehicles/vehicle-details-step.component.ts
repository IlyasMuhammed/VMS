import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { LookupsApi } from '../../core/api.services';
import { BranchPickerComponent } from '../../shared/branch-picker.component';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { LookupPickerComponent } from '../../shared/lookup-picker.component';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { VehicleForm, syncVehicleRules } from './vehicle-form';
import { ACQUISITION_TYPES, CAPACITY_UNITS, FUEL_TYPES, needsCapacity, optionsOf } from './vehicle-logic';

/**
 * Step 1 of the vehicle wizard: identity, technical and operational fields (FSD §16.1 to §16.3). It draws the form it is given
 * and keeps the dependent rules in step (a truck needs a load capacity), and knows nothing of the wizard around it.
 */
@Component({
  selector: 'app-vehicle-details-step',
  standalone: true,
  imports: [ReactiveFormsModule, InputTextModule, InputNumberModule, SelectModule, TextareaModule, FieldComponent, DatePickerComponent, LookupPickerComponent, BranchPickerComponent, PartnerPickerComponent],
  template: `
    <div [formGroup]="form()">
      <h2>Identity</h2>
      <div class="form-grid">
        <vms-field label="Registration number" [control]="form().controls.registrationNo" for="vh-reg" hint="Spaces and dashes do not matter: LES-1234 and LES 1234 are the same.">
          <input pInputText id="vh-reg" formControlName="registrationNo" autocomplete="off" />
        </vms-field>
        <vms-field label="Registration city" [control]="form().controls.registrationCityId" for="vh-city">
          <vms-lookup-picker type="CITY" formControlName="registrationCityId" inputId="vh-city" placeholder="Choose a city" />
        </vms-field>
        <vms-field label="Branch" [control]="form().controls.branchId" for="vh-branch" hint="Which of the company's own locations this vehicle belongs to.">
          <vms-branch-picker formControlName="branchId" inputId="vh-branch" placeholder="Choose a branch" />
        </vms-field>
        <vms-field label="Chassis number" [control]="form().controls.chassisNo" for="vh-chassis" hint="Unique when entered."><input pInputText id="vh-chassis" formControlName="chassisNo" autocomplete="off" /></vms-field>
        <vms-field label="Engine number" [control]="form().controls.engineNo" for="vh-engine" hint="Unique when entered."><input pInputText id="vh-engine" formControlName="engineNo" autocomplete="off" /></vms-field>
        <vms-field label="Vehicle type" [control]="form().controls.vehicleTypeId" for="vh-type"><vms-lookup-picker type="VEHICLE_TYPE" formControlName="vehicleTypeId" inputId="vh-type" placeholder="Choose a type" [showClear]="false" /></vms-field>
        <vms-field label="Make" [control]="form().controls.makeId" for="vh-make"><vms-lookup-picker type="MAKE" formControlName="makeId" inputId="vh-make" placeholder="Choose a make" [showClear]="false" /></vms-field>
        <vms-field label="Model" [control]="form().controls.model" for="vh-model"><input pInputText id="vh-model" formControlName="model" /></vms-field>
        <vms-field label="Manufacturing year" [control]="form().controls.manufacturingYear" for="vh-year">
          <p-inputnumber inputId="vh-year" formControlName="manufacturingYear" [useGrouping]="false" [fluid]="true" />
        </vms-field>
        <vms-field label="Colour" [control]="form().controls.colour" for="vh-colour"><input pInputText id="vh-colour" formControlName="colour" /></vms-field>
      </div>

      <h2>Technical and capacity</h2>
      <div class="form-grid">
        <vms-field label="Fuel type" [control]="form().controls.fuelType" for="vh-fuel">
          <p-select inputId="vh-fuel" formControlName="fuelType" [options]="fuelTypes" optionLabel="label" optionValue="value" placeholder="Choose…" [fluid]="true" appendTo="body" />
        </vms-field>
        <vms-field label="Tank capacity (litres)" [control]="form().controls.tankCapacity" for="vh-tank">
          <p-inputnumber inputId="vh-tank" formControlName="tankCapacity" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" />
        </vms-field>
        <vms-field label="Load capacity" [control]="form().controls.loadCapacity" for="vh-load" [hint]="capacityRequired() ? 'Required for this type of vehicle.' : ''">
          <p-inputnumber inputId="vh-load" formControlName="loadCapacity" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" />
        </vms-field>
        <vms-field label="Capacity unit" [control]="form().controls.capacityUnit" for="vh-unit">
          <p-select inputId="vh-unit" formControlName="capacityUnit" [options]="units" optionLabel="label" optionValue="value" placeholder="Choose…" [showClear]="true" [fluid]="true" appendTo="body" />
        </vms-field>
        <vms-field label="Axle configuration" [control]="form().controls.axleConfigurationId" for="vh-axle"><vms-lookup-picker type="AXLE_CONFIGURATION" formControlName="axleConfigurationId" inputId="vh-axle" /></vms-field>
        <vms-field label="Body type" [control]="form().controls.bodyTypeId" for="vh-body"><vms-lookup-picker type="BODY_TYPE" formControlName="bodyTypeId" inputId="vh-body" /></vms-field>
        <vms-field label="Tyre count" [control]="form().controls.tyreCount" for="vh-tyres" hint="2 to 22.">
          <p-inputnumber inputId="vh-tyres" formControlName="tyreCount" [useGrouping]="false" [fluid]="true" />
        </vms-field>
        <vms-field label="GVW (kg)" [control]="form().controls.gvw" for="vh-gvw" hint="Gross vehicle weight from the registration book.">
          <p-inputnumber inputId="vh-gvw" formControlName="gvw" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" />
        </vms-field>
      </div>

      <h2>Operational</h2>
      <div class="form-grid">
        <vms-field label="Opening odometer (km)" [control]="form().controls.openingOdometer" for="vh-odo" hint="The reading on the day the vehicle enters the system. Later readings cannot be lower.">
          <p-inputnumber inputId="vh-odo" formControlName="openingOdometer" [useGrouping]="false" [fluid]="true" />
        </vms-field>
        <vms-field label="Opening odometer date" [control]="form().controls.openingOdometerDate" for="vh-odo-date">
          <vms-date-picker inputId="vh-odo-date" formControlName="openingOdometerDate" [notFuture]="true" />
        </vms-field>
        <vms-field label="Default driver" [control]="form().controls.defaultDriverId" for="vh-driver" hint="Optional. The formal assignment is made when the vehicle is in the fleet.">
          <vms-partner-picker role="Driver" formControlName="defaultDriverId" inputId="vh-driver" />
        </vms-field>
        <div></div>
        <vms-field label="Fuel card company" [control]="form().controls.fuelCardCompanyId" for="vh-fuelco"><vms-partner-picker role="FuelCardCompany" formControlName="fuelCardCompanyId" inputId="vh-fuelco" /></vms-field>
        <vms-field label="Fuel card number" [control]="form().controls.fuelCardNumber" for="vh-fuelno" hint="On one vehicle in the fleet at a time."><input pInputText id="vh-fuelno" formControlName="fuelCardNumber" autocomplete="off" /></vms-field>
        <vms-field label="Tracker company" [control]="form().controls.trackerCompanyId" for="vh-tracker"><vms-partner-picker role="TrackerCompany" formControlName="trackerCompanyId" inputId="vh-tracker" /></vms-field>
        <vms-field label="Tracker device ID" [control]="form().controls.trackerDeviceId" for="vh-device"><input pInputText id="vh-device" formControlName="trackerDeviceId" autocomplete="off" /></vms-field>
        @if (mayEnterAcquisition()) {
          <vms-field label="Acquisition date" [control]="form().controls.acquisitionDate" for="vh-acq" hint="The day the vehicle entered the fleet. Not in the future.">
            <vms-date-picker inputId="vh-acq" formControlName="acquisitionDate" [notFuture]="true" />
          </vms-field>
          <vms-field label="Acquisition type" [control]="form().controls.acquisitionType" for="vh-acqtype">
            <p-select inputId="vh-acqtype" formControlName="acquisitionType" [options]="acquisitionTypes" optionLabel="label" optionValue="value" placeholder="Choose…" [showClear]="true" [fluid]="true" appendTo="body" />
          </vms-field>
        }
        <vms-field class="full" label="Remarks" [control]="form().controls.remarks" for="vh-remarks"><textarea pTextarea id="vh-remarks" formControlName="remarks" rows="3" [fluid]="true"></textarea></vms-field>
      </div>
    </div>
  `,
  styles: [`h2 { font-size: 1.05rem; margin: 0 0 .75rem; } h2:not(:first-child) { margin-top: 1.5rem; }`],
})
export class VehicleDetailsStepComponent {
  private readonly lookups = inject(LookupsApi);
  private readonly destroyRef = inject(DestroyRef);

  readonly form = input.required<VehicleForm>();
  readonly mayEnterAcquisition = input(false);

  protected readonly fuelTypes = optionsOf(FUEL_TYPES);
  protected readonly units = optionsOf(CAPACITY_UNITS);
  protected readonly acquisitionTypes = optionsOf(ACQUISITION_TYPES);
  protected readonly capacityRequired = signal(false);

  /** Vehicle Type value id → its code, to know whether this type needs a load capacity. */
  private typeCodes = new Map<number, string>();

  constructor() {
    this.lookups.active('VEHICLE_TYPE').subscribe({ next: (items) => { this.typeCodes = new Map(items.map((i) => [i.id, i.code])); this.sync(); }, error: () => undefined });

    effect(() => {
      const form = this.form();
      untracked(() => {
        const c = form.controls;
        for (const control of [c.vehicleTypeId, c.loadCapacity, c.openingOdometer, c.fuelCardCompanyId]) control.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.sync());
        this.sync();
      });
    });
  }

  /** Re-applies the dependent rules. Also called by the wizard after it fills the form from a saved vehicle. */
  sync(): void {
    const form = this.form();
    const code = this.typeCodes.get(form.controls.vehicleTypeId.value ?? -1) ?? null;
    this.capacityRequired.set(needsCapacity(code));
    syncVehicleRules(form, code);
  }
}
