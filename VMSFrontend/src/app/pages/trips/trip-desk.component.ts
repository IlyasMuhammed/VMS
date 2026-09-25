import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { TripsApi } from '../../core/api.services';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { TripSearchFilter } from '../../core/trip.models';
import { DataGridComponent, GridCellDirective, GridColumn } from '../../shared/data-grid.component';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { ListQuery } from '../../shared/list-query';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { TripCustomerPickerComponent } from '../../shared/trip-customer-picker.component';
import { VehiclePickerComponent } from '../../shared/vehicle-picker.component';
import { TRIP_STATUSES, optionsOf, podSeverity, statusSeverity, words } from './trip-logic';

/** Trip List / Trip Desk (FSD §48.4's unnumbered row): the operational board every other Trip screen is reached
 * from — there is no other way into Trip Details than opening a row here (or a link from somewhere that already
 * has a trip id, like a future invoice line). No free-text search — the FSD's own filter list for this screen
 * is date/customer/vehicle/driver/status/type/active/invoiced only, so `vms-filter-bar`'s search box is not used;
 * these are picker/select controls `vms-filter-bar` itself does not support, fed into `vms-data-grid` as a plain
 * `filters` object instead. */
@Component({
  selector: 'app-trip-desk',
  standalone: true,
  imports: [
    FormsModule, ButtonModule, SelectModule, CheckboxModule, TagModule, BusinessDatePipe,
    DataGridComponent, GridCellDirective, DatePickerComponent, TripCustomerPickerComponent, VehiclePickerComponent, PartnerPickerComponent,
  ],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Trip Desk</h1><div class="sub">Every trip, fixed or open, and where it stands.</div></div>
        <div class="header-actions">
          @if (canCreate()) {
            <p-button label="New fixed trip" icon="pi pi-plus" severity="secondary" [outlined]="true" (onClick)="newFixed()" />
            <p-button label="New open trip" icon="pi pi-plus" (onClick)="newOpen()" />
          }
        </div>
      </div>

      <div class="card">
        <div class="toolbar">
          <div class="field"><label>From</label><vms-date-picker [ngModel]="fromDate()" (ngModelChange)="fromDate.set($event)" /></div>
          <div class="field"><label>To</label><vms-date-picker [ngModel]="toDate()" (ngModelChange)="toDate.set($event)" /></div>
          <div class="field"><label>Customer</label><vms-trip-customer-picker [ngModel]="customerId()" (ngModelChange)="customerId.set($event)" /></div>
          <div class="field"><label>Vehicle</label><vms-vehicle-picker [ngModel]="vehicleId()" (ngModelChange)="vehicleId.set($event)" /></div>
          <div class="field"><label>Driver</label><vms-partner-picker role="Driver" [allowCreate]="false" [ngModel]="driverId()" (ngModelChange)="driverId.set($event)" /></div>
          <div class="field"><label>Status</label><p-select [ngModel]="status()" (ngModelChange)="status.set($event)" [options]="statusOptions" optionLabel="label" optionValue="value" [showClear]="true" placeholder="Any" [fluid]="true" appendTo="body" /></div>
          <div class="field"><label>Type</label><p-select [ngModel]="tripType()" (ngModelChange)="tripType.set($event)" [options]="typeOptions" optionLabel="label" optionValue="value" [showClear]="true" placeholder="Any" [fluid]="true" appendTo="body" /></div>
          <label class="check"><input type="checkbox" [ngModel]="activeOnly()" (ngModelChange)="activeOnly.set($event)" /> Active only</label>
          <label class="check"><input type="checkbox" [ngModel]="invoicedOnly()" (ngModelChange)="invoicedOnly.set($event)" /> Invoiced only</label>
          <p-button label="Clear" [text]="true" size="small" (onClick)="clear()" />
        </div>

        <vms-data-grid label="Trips" [columns]="columns" [load]="load" [filters]="filters()" [filtering]="isFiltering()"
                       emptyMessage="No trips for the selected filters." noMatchMessage="No trips for the selected filters.">
          <ng-template vmsCell="tripNumber" let-t><a class="mono" [href]="'/trips/' + t.tripId" (click)="open($event, t)">{{ t.tripNumber }}</a></ng-template>
          <ng-template vmsCell="tripDate" let-t>{{ t.tripDate | vmsDate }}</ng-template>
          <ng-template vmsCell="customerName" let-t>{{ t.customerName }}@if (t.customerTripReference) { <div class="muted small">{{ t.customerTripReference }}</div> }</ng-template>
          <ng-template vmsCell="routeLabel" let-t>{{ t.routeLabel || '—' }}</ng-template>
          <ng-template vmsCell="vehicleRegistrationNo" let-t>{{ t.vehicleRegistrationNo || '#' + t.vehicleId }}</ng-template>
          <ng-template vmsCell="driverName" let-t>{{ t.driverName || '—' }}</ng-template>
          <ng-template vmsCell="status" let-t><p-tag [value]="words(t.status)" [severity]="severity(t.status)" /></ng-template>
          <ng-template vmsCell="tripAmount" let-t>@if (t.rateMissing) { <span class="chip chip--danger">Rate missing</span> } @else { {{ t.tripAmount }} {{ t.currencyCode }} }</ng-template>
          <ng-template vmsCell="podStatus" let-t>@if (t.podStatus) { <p-tag [value]="t.podStatus" [severity]="pod(t.podStatus)" /> } @else { — }</ng-template>
          <ng-template vmsCell="invoiceNumber" let-t>{{ t.invoiceNumber || '—' }}</ng-template>
        </vms-data-grid>
      </div>
    </div>
  `,
  styles: [
    `
      .header-actions { display: flex; gap: .5rem; }
      .toolbar { display: flex; align-items: flex-end; gap: 1rem; margin-bottom: .75rem; flex-wrap: wrap; }
      .field { display: flex; flex-direction: column; gap: .3rem; min-width: 11rem; } .field label { font-size: .8rem; color: var(--vms-muted); }
      .check { display: inline-flex; align-items: center; gap: .4rem; font-size: .9rem; padding-bottom: .5rem; }
      .small { font-size: .8rem; }
    `,
  ],
})
export class TripDeskComponent {
  private readonly api = inject(TripsApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly canCreate = computed(() => this.auth.hasPermission('TRP.TRIP.CREATE'));

  readonly fromDate = signal<string | null>(null);
  readonly toDate = signal<string | null>(null);
  readonly customerId = signal<number | null>(null);
  readonly vehicleId = signal<number | null>(null);
  readonly driverId = signal<number | null>(null);
  readonly status = signal<string | null>(null);
  readonly tripType = signal<string | null>(null);
  readonly activeOnly = signal(false);
  readonly invoicedOnly = signal(false);

  protected readonly statusOptions = optionsOf(TRIP_STATUSES);
  protected readonly typeOptions = [{ label: 'Fixed', value: 'Fixed' }, { label: 'Open', value: 'Open' }];
  protected readonly words = words;
  protected readonly severity = statusSeverity;
  protected readonly pod = podSeverity;

  readonly columns: GridColumn[] = [
    { key: 'tripNumber', header: 'Trip no.' },
    { key: 'tripDate', header: 'Date', width: '7rem' },
    { key: 'customerName', header: 'Customer' },
    { key: 'routeLabel', header: 'Route' },
    { key: 'vehicleRegistrationNo', header: 'Vehicle' },
    { key: 'driverName', header: 'Driver' },
    { key: 'status', header: 'Status', width: '9rem' },
    { key: 'tripAmount', header: 'Amount' },
    { key: 'podStatus', header: 'POD' },
    { key: 'invoiceNumber', header: 'Invoice' },
  ];

  readonly filters = computed<Record<string, unknown>>(() => ({
    fromDate: this.fromDate(), toDate: this.toDate(), customerId: this.customerId(), vehicleId: this.vehicleId(), driverId: this.driverId(),
    status: this.status(), tripType: this.tripType(), activeOnly: this.activeOnly(), invoicedOnly: this.invoicedOnly(),
  }));
  readonly isFiltering = computed(() => Object.values(this.filters()).some((v) => v !== null && v !== undefined && v !== false));

  readonly load = (query: ListQuery) => {
    const f = query.filters as ReturnType<typeof this.filters>;
    const filter: TripSearchFilter = {
      fromDate: f['fromDate'] as string | null, toDate: f['toDate'] as string | null, customerId: f['customerId'] as number | null,
      vehicleId: f['vehicleId'] as number | null, driverId: f['driverId'] as number | null, status: f['status'] as string | null, tripType: f['tripType'] as string | null,
      isActive: f['activeOnly'] ? true : null, invoiced: f['invoicedOnly'] ? true : null,
    };
    return this.api.search(filter, query.page, query.pageSize);
  };

  clear(): void {
    this.fromDate.set(null); this.toDate.set(null); this.customerId.set(null); this.vehicleId.set(null); this.driverId.set(null);
    this.status.set(null); this.tripType.set(null); this.activeOnly.set(false); this.invoicedOnly.set(false);
  }

  newFixed(): void {
    void this.router.navigate(['/trips/new-fixed']);
  }

  newOpen(): void {
    void this.router.navigate(['/trips/new-open']);
  }

  open(event: MouseEvent | null, trip: { tripId: number }): void {
    if (event && (event.ctrlKey || event.metaKey || event.shiftKey || event.button === 1)) return;
    event?.preventDefault();
    void this.router.navigate(['/trips', trip.tripId]);
  }
}
