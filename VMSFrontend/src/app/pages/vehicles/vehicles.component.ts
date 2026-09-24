import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { VehiclesApi } from '../../core/api.services';
import { AuthService } from '../../core/auth.service';
import { InstantPipe } from '../../core/datetime/datetime.pipes';
import { NotifyService } from '../../core/notify.service';
import { DataGridComponent, GridActionsDirective, GridCellDirective, GridColumn } from '../../shared/data-grid.component';
import { FilterBarComponent, FilterDef } from '../../shared/filter-bar.component';
import { FilterValue, ListQuery, isFiltering, noFilters } from '../../shared/list-query';
import { VehicleStatusComponent } from './vehicle-bits.component';
import { CATEGORIES, VEHICLE_STATUSES, isDraft, optionsOf, words } from './vehicle-logic';

/** The vehicle list (FSD §21): search by registration, chassis, engine or code; filters; server paging and sorting. */
@Component({
  selector: 'app-vehicles',
  standalone: true,
  imports: [ButtonModule, InstantPipe, DataGridComponent, GridCellDirective, GridActionsDirective, FilterBarComponent, VehicleStatusComponent],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Vehicles</h1><div class="sub">The fleet, from a purchase being entered to a vehicle sold.</div></div>
        <div class="header-actions">
          @if (canExport()) { <p-button label="Export" icon="pi pi-download" severity="secondary" [outlined]="true" [loading]="exporting()" (onClick)="export()" /> }
          @if (canCreate()) { <p-button label="New vehicle" icon="pi pi-plus" (onClick)="create()" /> }
        </div>
      </div>

      <div class="card">
        <vms-filter-bar [(value)]="bar" [filters]="filterDefs" searchPlaceholder="Search registration, chassis, engine or code" />

        <vms-data-grid label="Vehicles" [columns]="columns" [load]="load" [search]="bar().search" [filters]="bar().filters" [filtering]="filtering()"
                       [defaultSort]="{ field: 'modifiedOn', order: -1 }" emptyMessage="No vehicles yet. Add the first one with New vehicle."
                       noMatchMessage="No vehicles match these filters." (clearFilters)="bar.set(none())">
          <ng-template vmsCell="registrationNo" let-v><a class="mono reg" [href]="href(v)" (click)="open($event, v)">{{ v.registrationNo }}</a><div class="muted small mono">{{ v.vehicleCode }}</div></ng-template>
          <ng-template vmsCell="vehicle" let-v>{{ v.vehicleType || '—' }}<div class="muted small">{{ v.make }} {{ v.model }}</div></ng-template>
          <ng-template vmsCell="currentCategory" let-v>{{ v.currentCategory ? words(v.currentCategory) : '—' }}</ng-template>
          <ng-template vmsCell="counterparty" let-v>{{ v.counterparty?.name || '—' }}</ng-template>
          <ng-template vmsCell="branch" let-v>{{ v.branch || '—' }}</ng-template>
          <ng-template vmsCell="status" let-v><vms-vehicle-status [status]="v.status" /></ng-template>
          <ng-template vmsCell="driver" let-v>{{ v.driver?.name || '—' }}</ng-template>
          <ng-template vmsCell="modifiedOn" let-v>{{ v.modifiedOn | vmsInstant }}</ng-template>
          <ng-template vmsRowActions let-v>
            <p-button icon="pi pi-eye" [text]="true" severity="secondary" title="Open" [ariaLabel]="'Open ' + v.registrationNo" (onClick)="open(null, v)" />
          </ng-template>
        </vms-data-grid>
      </div>
    </div>
  `,
  styles: [`.header-actions { display: flex; gap: .5rem; } .small { font-size: .8rem; } a.reg { text-decoration: none; font-weight: 600; } a.reg:hover { text-decoration: underline; }`],
})
export class VehiclesComponent {
  private readonly api = inject(VehiclesApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);

  readonly canCreate = computed(() => this.auth.hasPermission('VEH.CREATE'));
  readonly canExport = computed(() => this.auth.hasPermission('VEH.EXPORT'));
  readonly exporting = signal(false);
  readonly bar = signal<FilterValue>(noFilters());
  readonly filtering = computed(() => isFiltering(this.bar()));
  protected readonly none = noFilters;
  protected readonly words = words;

  readonly columns: GridColumn[] = [
    { key: 'registrationNo', header: 'Registration', sortable: true },
    { key: 'vehicle', header: 'Type and make' },
    { key: 'currentCategory', header: 'Category', sortable: true },
    { key: 'counterparty', header: 'Counterparty' },
    { key: 'branch', header: 'Branch' },
    { key: 'status', header: 'Status', sortable: true },
    { key: 'driver', header: 'Driver' },
    { key: 'modifiedOn', header: 'Modified', sortable: true },
  ];

  readonly filterDefs: FilterDef[] = [
    { key: 'category', label: 'Category', kind: 'multiselect', options: optionsOf(CATEGORIES) },
    { key: 'status', label: 'Status', kind: 'multiselect', options: optionsOf(VEHICLE_STATUSES) },
    { key: 'vehicleTypeId', label: 'Type', kind: 'select', lookup: 'VEHICLE_TYPE', lookupValue: 'id' },
    { key: 'makeId', label: 'Make', kind: 'select', lookup: 'MAKE', lookupValue: 'id' },
    { key: 'branchId', label: 'Branch', kind: 'select', branches: true },
  ];

  readonly load = (query: ListQuery) => this.api.list(query);

  export(): void {
    this.exporting.set(true);
    this.api.export({ page: 1, pageSize: 25, search: this.bar().search, filters: this.bar().filters }).subscribe({
      next: ({ blob, fileName }) => {
        this.exporting.set(false);
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        link.click();
        URL.revokeObjectURL(url);
        this.notify.success('Export downloaded');
      },
      error: async (err) => {
        this.exporting.set(false);
        // A failed download's body is a Blob: read the server's message out of it.
        let message: string | null = null;
        try { message = err?.error instanceof Blob ? ((JSON.parse(await err.error.text()) as { message?: string }).message ?? null) : null; } catch { /* not JSON */ }
        this.notify.error(message ?? err);
      },
    });
  }

  create(): void {
    void this.router.navigate(['/vehicles/new']);
  }

  /** A Draft is still being entered, so it opens in the wizard; a vehicle in the fleet opens on its own screen. */
  protected path(v: { id: number; status: string }): string[] {
    return isDraft(v.status) ? ['/vehicles', String(v.id), 'edit'] : ['/vehicles', String(v.id)];
  }

  protected href(v: { id: number; status: string }): string {
    return this.path(v).join('/');
  }

  open(event: MouseEvent | null, v: { id: number; status: string }): void {
    if (event && (event.ctrlKey || event.metaKey || event.shiftKey || event.button === 1)) return;
    event?.preventDefault();
    void this.router.navigate(this.path(v));
  }
}
