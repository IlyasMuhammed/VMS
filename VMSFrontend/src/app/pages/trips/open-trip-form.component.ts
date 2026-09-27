import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { apiErrors } from '../../core/api-error';
import { TripsApi } from '../../core/api.services';
import { LocationInput, OtherLocationType, TripLocationType } from '../../core/trip.models';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { TripCityPickerComponent } from '../../shared/trip-city-picker.component';
import { TripCustomerPickerComponent } from '../../shared/trip-customer-picker.component';
import { VehiclePickerComponent } from '../../shared/vehicle-picker.component';
import { OTHER_LOCATION_TYPES, optionsOf } from './trip-logic';

let nextRowId = 1;
interface LocationRow { id: number; locationType: TripLocationType; cityId: number | null; otherLocationType: OtherLocationType; otherLocationName: string }
const blankLocation = (): LocationRow => ({ id: nextRowId++, locationType: 'City', cityId: null, otherLocationType: 'Other', otherLocationName: '' });

/** New Open Trip (FSD §22, §48.4 screen 13): a customer's own one-off run, not against any Trip Configuration —
 * From/To/stops are each a City or a free-text Other Location, and the amount is entered directly (never
 * resolved from a rate master, since an open trip has none). */
@Component({
  selector: 'app-open-trip-form',
  standalone: true,
  imports: [
    ReactiveFormsModule, FormsModule, RouterLink, ButtonModule, InputTextModule, InputNumberModule, SelectModule, CheckboxModule, TextareaModule,
    FieldComponent, DatePickerComponent, TripCustomerPickerComponent, TripCityPickerComponent, VehiclePickerComponent, PartnerPickerComponent,
  ],
  template: `
    <div class="page">
      <div class="crumbs"><a routerLink="/trips">Trip Desk</a> <span aria-hidden="true">/</span> <span>New open trip</span></div>
      <h1>New open trip</h1>

      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (m of problems(); track m) { <li>{{ m }}</li> }</ul></div> }
      @for (w of warnings(); track w) { <div class="alert warning">{{ w }}</div> }

      <div class="card">
        <form [formGroup]="form" class="form-grid" novalidate>
          <vms-field label="Customer" [control]="form.controls.customerId" for="ot-customer"><vms-trip-customer-picker formControlName="customerId" /></vms-field>
          <vms-field label="Customer reference" [control]="form.controls.customerTripReference" for="ot-ref"><input pInputText id="ot-ref" formControlName="customerTripReference" /></vms-field>
          <vms-field label="Trip date" [control]="form.controls.tripDate" for="ot-date"><vms-date-picker inputId="ot-date" formControlName="tripDate" /></vms-field>
          <vms-field label="Vehicle" [control]="form.controls.vehicleId" for="ot-vehicle"><vms-vehicle-picker formControlName="vehicleId" /></vms-field>
          <vms-field label="Driver" [control]="form.controls.driverId" for="ot-driver" hint="Leave blank to use the vehicle's own default driver."><vms-partner-picker role="Driver" [allowCreate]="false" formControlName="driverId" inputId="ot-driver" /></vms-field>
          <vms-field label="Trip amount" [control]="form.controls.tripAmount" for="ot-amount"><p-inputnumber inputId="ot-amount" formControlName="tripAmount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
          <div><p-checkbox formControlName="isRoundTrip" [binary]="true" inputId="ot-round" /> <label for="ot-round">Round trip</label></div>
          <vms-field class="full" label="Remarks" [control]="form.controls.remarks" for="ot-remarks"><textarea pTextarea id="ot-remarks" formControlName="remarks" rows="2" [fluid]="true"></textarea></vms-field>
        </form>

        <h2>From</h2>
        <div class="loc-row">
          <p-select class="loc-type" [ngModel]="from().locationType" (ngModelChange)="setFromType($event)" [ngModelOptions]="{ standalone: true }" [options]="locationTypeOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
          @if (from().locationType === 'City') {
            <vms-trip-city-picker [ngModel]="from().cityId" (ngModelChange)="setFromCity($event)" [ngModelOptions]="{ standalone: true }" />
          } @else {
            <p-select class="loc-other-type" [ngModel]="from().otherLocationType" (ngModelChange)="setFromOtherType($event)" [ngModelOptions]="{ standalone: true }" [options]="otherTypeOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
            <input pInputText [ngModel]="from().otherLocationName" (ngModelChange)="setFromOtherName($event)" [ngModelOptions]="{ standalone: true }" placeholder="Location name" />
          }
        </div>

        <h2>To</h2>
        <div class="loc-row">
          <p-select class="loc-type" [ngModel]="to().locationType" (ngModelChange)="setToType($event)" [ngModelOptions]="{ standalone: true }" [options]="locationTypeOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
          @if (to().locationType === 'City') {
            <vms-trip-city-picker [ngModel]="to().cityId" (ngModelChange)="setToCity($event)" [ngModelOptions]="{ standalone: true }" />
          } @else {
            <p-select class="loc-other-type" [ngModel]="to().otherLocationType" (ngModelChange)="setToOtherType($event)" [ngModelOptions]="{ standalone: true }" [options]="otherTypeOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
            <input pInputText [ngModel]="to().otherLocationName" (ngModelChange)="setToOtherName($event)" [ngModelOptions]="{ standalone: true }" placeholder="Location name" />
          }
        </div>

        <h2>Stops <span class="muted">(optional, in order)</span></h2>
        @for (s of stops(); track s.id; let i = $index) {
          <div class="loc-row">
            <p-select class="loc-type" [ngModel]="s.locationType" (ngModelChange)="setStopType(i, $event)" [ngModelOptions]="{ standalone: true }" [options]="locationTypeOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
            @if (s.locationType === 'City') {
              <vms-trip-city-picker [ngModel]="s.cityId" (ngModelChange)="setStopCity(i, $event)" [ngModelOptions]="{ standalone: true }" />
            } @else {
              <p-select class="loc-other-type" [ngModel]="s.otherLocationType" (ngModelChange)="setStopOtherType(i, $event)" [ngModelOptions]="{ standalone: true }" [options]="otherTypeOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
              <input pInputText [ngModel]="s.otherLocationName" (ngModelChange)="setStopOtherName(i, $event)" [ngModelOptions]="{ standalone: true }" placeholder="Location name" />
            }
            <p-button icon="pi pi-trash" [text]="true" severity="danger" (onClick)="removeStop(i)" ariaLabel="Remove stop" />
          </div>
        }
        <p-button label="Add stop" icon="pi pi-plus" [text]="true" size="small" (onClick)="addStop()" />

        <div class="savebar">
          <span class="buttons">
            <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="cancel()" />
            <p-button label="Save Draft" severity="secondary" [outlined]="true" [loading]="busy()" (onClick)="save(false)" />
            <p-button label="Save & Plan" icon="pi pi-check" [loading]="busy()" (onClick)="save(true)" />
          </span>
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .crumbs { color: var(--vms-muted); margin-bottom: .75rem; font-size: .9rem; } h1 { font-size: 1.5rem; font-weight: 600; margin-bottom: 1rem; }
      .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; margin-bottom: 1rem; } .full { grid-column: 1 / -1; }
      h2 { font-size: 1.05rem; margin: 1rem 0 .5rem; }
      .loc-row { display: flex; gap: .5rem; align-items: center; margin-bottom: .5rem; } .loc-row > * { flex: 1 1 auto; }
      .loc-type { max-width: 10rem; flex: none; } .loc-other-type { max-width: 12rem; flex: none; }
      .savebar { margin-top: 1.5rem; display: flex; justify-content: flex-end; } .buttons { display: inline-flex; gap: .5rem; }
      .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class OpenTripFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(TripsApi);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);

  readonly problems = signal<string[]>([]);
  readonly warnings = signal<string[]>([]);
  readonly busy = signal(false);

  readonly from = signal<LocationRow>(blankLocation());
  readonly to = signal<LocationRow>(blankLocation());
  readonly stops = signal<LocationRow[]>([]);

  protected readonly locationTypeOptions = [{ label: 'City', value: 'City' }, { label: 'Other', value: 'Other' }];
  protected readonly otherTypeOptions = optionsOf(OTHER_LOCATION_TYPES);

  readonly form = this.fb.group({
    customerId: this.fb.control<number | null>(null, Validators.required),
    customerTripReference: this.fb.nonNullable.control(''),
    tripDate: this.fb.control<string | null>(null, Validators.required),
    vehicleId: this.fb.control<number | null>(null, Validators.required),
    driverId: this.fb.control<number | null>(null),
    tripAmount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    isRoundTrip: this.fb.nonNullable.control(false),
    remarks: this.fb.nonNullable.control(''),
  });

  setFromType(locationType: TripLocationType): void { this.from.update((r) => ({ ...r, locationType })); }
  setFromCity(cityId: number | null): void { this.from.update((r) => ({ ...r, cityId })); }
  setFromOtherType(otherLocationType: OtherLocationType): void { this.from.update((r) => ({ ...r, otherLocationType })); }
  setFromOtherName(otherLocationName: string): void { this.from.update((r) => ({ ...r, otherLocationName })); }

  setToType(locationType: TripLocationType): void { this.to.update((r) => ({ ...r, locationType })); }
  setToCity(cityId: number | null): void { this.to.update((r) => ({ ...r, cityId })); }
  setToOtherType(otherLocationType: OtherLocationType): void { this.to.update((r) => ({ ...r, otherLocationType })); }
  setToOtherName(otherLocationName: string): void { this.to.update((r) => ({ ...r, otherLocationName })); }

  addStop(): void {
    this.stops.update((rows) => [...rows, blankLocation()]);
  }

  removeStop(index: number): void {
    this.stops.update((rows) => rows.filter((_, i) => i !== index));
  }

  setStopType(index: number, locationType: TripLocationType): void {
    this.stops.update((rows) => rows.map((r, i) => (i === index ? { ...r, locationType } : r)));
  }

  setStopCity(index: number, cityId: number | null): void {
    this.stops.update((rows) => rows.map((r, i) => (i === index ? { ...r, cityId } : r)));
  }

  setStopOtherType(index: number, otherLocationType: OtherLocationType): void {
    this.stops.update((rows) => rows.map((r, i) => (i === index ? { ...r, otherLocationType } : r)));
  }

  setStopOtherName(index: number, value: string): void {
    this.stops.update((rows) => rows.map((r, i) => (i === index ? { ...r, otherLocationName: value } : r)));
  }

  private toInput(row: LocationRow): LocationInput {
    return row.locationType === 'City'
      ? { locationType: 'City', cityId: row.cityId }
      : { locationType: 'Other', otherLocationType: row.otherLocationType, otherLocationName: row.otherLocationName.trim() || null };
  }

  cancel(): void {
    void this.router.navigate(['/trips']);
  }

  save(andPlan: boolean): void {
    if (this.busy() || this.form.invalid) { this.form.markAllAsTouched(); return; }
    if (this.from().locationType === 'City' && !this.from().cityId) { this.notify.warn('Choose a From city.', 'Not saved'); return; }
    if (this.to().locationType === 'City' && !this.to().cityId) { this.notify.warn('Choose a To city.', 'Not saved'); return; }
    this.problems.set([]);
    this.warnings.set([]);
    const f = this.form.getRawValue();
    this.busy.set(true);
    this.api.createOpen({
      customerId: f.customerId!, vehicleId: f.vehicleId!, driverId: f.driverId, tripDate: f.tripDate!, customerTripReference: f.customerTripReference.trim() || null,
      from: this.toInput(this.from()), to: this.toInput(this.to()), stops: this.stops().map((s) => this.toInput(s)), isRoundTrip: f.isRoundTrip, tripAmount: f.tripAmount!, remarks: f.remarks.trim() || null,
    }).subscribe({
      next: (trip) => {
        this.warnings.set(trip.warnings);
        if (!andPlan) { this.busy.set(false); this.finish(trip.tripId); return; }
        this.api.transition(trip.tripId, 'Planned', { rowVersion: trip.rowVersion }).subscribe({
          next: () => { this.busy.set(false); this.finish(trip.tripId); },
          error: () => { this.busy.set(false); this.notify.warn('Trip saved, but could not be moved to Planned. Continue from Trip Details.', 'Partly done'); this.finish(trip.tripId); },
        });
      },
      error: (err) => this.refused(err),
    });
  }

  private finish(tripId: number): void {
    this.notify.success('Trip saved');
    void this.router.navigate(['/trips', tripId]);
  }

  private refused(err: unknown): void {
    this.busy.set(false);
    const found = apiErrors(err);
    if (found.length === 0) { this.notify.error(err); return; }
    this.problems.set(applyServerErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
  }
}
