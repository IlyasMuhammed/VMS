import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { AuthService } from '../../core/auth.service';
import { PartnersApi } from '../../core/api.services';
import { InstantPipe } from '../../core/datetime/datetime.pipes';
import { NotifyService } from '../../core/notify.service';
import { PartnerListItem } from '../../core/partner.models';
import { DataGridComponent, GridActionsDirective, GridCellDirective, GridColumn } from '../../shared/data-grid.component';
import { FilterBarComponent, FilterDef } from '../../shared/filter-bar.component';
import { FilterValue, ListQuery, isFiltering, noFilters } from '../../shared/list-query';
import { PartnerStatusComponent, RoleChipsComponent } from './partner-bits.component';
import { PARTY_TYPES, ROLES, optionsOf } from './partner-logic';
import { PartnerStatusDialogComponent } from './partner-status-dialog.component';

/** The partner list (FSD §9.1): quick search, filters, server paging and sorting, and the row actions. */
@Component({
  selector: 'app-partners',
  standalone: true,
  imports: [
    ButtonModule, InstantPipe, DataGridComponent, GridCellDirective, GridActionsDirective, FilterBarComponent,
    PartnerStatusComponent, RoleChipsComponent, PartnerStatusDialogComponent,
  ],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Business partners</h1><div class="sub">Customers, drivers, workshops, banks and vendors, in one list.</div></div>
        <div class="header-actions">
          @if (canExport()) { <p-button label="Export" icon="pi pi-download" severity="secondary" [outlined]="true" [loading]="exporting()" (onClick)="export()" /> }
          @if (canCreate()) { <p-button label="New partner" icon="pi pi-plus" (onClick)="create()" /> }
        </div>
      </div>

      <div class="card">
        <vms-filter-bar [(value)]="bar" [filters]="filterDefs" searchPlaceholder="Search code, name, CNIC, NTN or mobile" />

        <vms-data-grid label="Business partners" [columns]="columns" [load]="load" [search]="bar().search" [filters]="bar().filters" [filtering]="filtering()"
                       [defaultSort]="{ field: 'modifiedOn', order: -1 }" emptyMessage="No partners yet. Add the first one with New partner."
                       noMatchMessage="No partners match these filters." (clearFilters)="clearFilters()">
          <ng-template vmsCell="bpCode" let-p><a class="mono" [href]="'/partners/' + p.id" (click)="open($event, p)">{{ p.bpCode }}</a></ng-template>
          <ng-template vmsCell="legalName" let-p>
            {{ p.legalName }}
            @if (p.displayName && p.displayName !== p.legalName) { <div class="muted small">{{ p.displayName }}</div> }
          </ng-template>
          <ng-template vmsCell="roles" let-p><vms-role-chips [roles]="p.roles" /></ng-template>
          <ng-template vmsCell="branch" let-p>{{ p.branch || '—' }}</ng-template>
          <ng-template vmsCell="status" let-p><vms-partner-status [status]="p.status" /></ng-template>
          <ng-template vmsCell="modifiedOn" let-p>{{ p.modifiedOn | vmsInstant }}</ng-template>
          <ng-template vmsRowActions let-p>
            <p-button icon="pi pi-pencil" [text]="true" severity="secondary" [title]="canEdit() ? 'Open' : 'View'" [ariaLabel]="'Open ' + p.legalName" (onClick)="open(null, p)" />
            @if (canChangeStatus() && p.status !== 'Merged') {
              <p-button icon="pi pi-flag" [text]="true" severity="secondary" title="Change status" [ariaLabel]="'Change status of ' + p.legalName" (onClick)="statusTarget.set(p)" />
            }
          </ng-template>
        </vms-data-grid>
      </div>
    </div>

    <app-partner-status-dialog [partner]="statusTarget()" (closed)="statusTarget.set(null)" (changed)="onStatusChanged()" />
  `,
  styles: [
    `
      .header-actions { display: flex; gap: .5rem; }
      .small { font-size: .8rem; }
      a.mono { text-decoration: none; }
      a.mono:hover { text-decoration: underline; }
    `,
  ],
})
export class PartnersComponent {
  private readonly api = inject(PartnersApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly grid = viewChild(DataGridComponent);

  readonly canCreate = computed(() => this.auth.hasPermission('BP.CREATE'));
  readonly canEdit = computed(() => this.auth.hasPermission('BP.EDIT'));
  readonly canExport = computed(() => this.auth.hasPermission('BP.EXPORT'));
  readonly canChangeStatus = computed(() => this.auth.hasPermission('BP.STATUS.CHANGE'));

  readonly bar = signal<FilterValue>(noFilters());
  readonly filtering = computed(() => isFiltering(this.bar()));
  readonly exporting = signal(false);
  readonly statusTarget = signal<PartnerListItem | null>(null);

  readonly columns: GridColumn[] = [
    { key: 'bpCode', header: 'Code', sortable: true, width: '9rem' },
    { key: 'legalName', header: 'Legal name', sortable: true },
    { key: 'roles', header: 'Roles' },
    { key: 'city', header: 'City' },
    { key: 'branch', header: 'Branch' },
    { key: 'primaryMobile', header: 'Mobile' },
    { key: 'status', header: 'Status', sortable: true },
    { key: 'modifiedOn', header: 'Modified', sortable: true },
  ];

  readonly filterDefs: FilterDef[] = [
    { key: 'roles', label: 'Role', kind: 'multiselect', options: ROLES.map((r) => ({ label: r.label, value: r.code })) },
    { key: 'status', label: 'Status', kind: 'select', options: optionsOf(['Active', 'Inactive', 'Blacklisted', 'Merged']) },
    { key: 'cityId', label: 'City', kind: 'select', lookup: 'CITY', lookupValue: 'id' },
    { key: 'branchId', label: 'Branch', kind: 'select', branches: true },
    { key: 'partyType', label: 'Party type', kind: 'select', options: optionsOf(PARTY_TYPES) },
  ];

  readonly load = (query: ListQuery) => this.api.list(query);

  clearFilters(): void {
    this.bar.set(noFilters());
  }

  create(): void {
    void this.router.navigate(['/partners/new']);
  }

  /** A plain click opens the partner in this tab; ctrl/middle-click keeps the link's own behaviour (a new tab). */
  open(event: MouseEvent | null, partner: PartnerListItem): void {
    if (event && (event.ctrlKey || event.metaKey || event.shiftKey || event.button === 1)) return;
    event?.preventDefault();
    void this.router.navigate(['/partners', partner.id]);
  }

  onStatusChanged(): void {
    this.statusTarget.set(null);
    this.grid()?.reload();
  }

  export(): void {
    this.exporting.set(true);
    const query: ListQuery = { page: 1, pageSize: 25, search: this.bar().search, filters: this.bar().filters };
    this.api.export(query).subscribe({
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
        // The body of a failed download is a Blob: read the server's message out of it.
        const text = err?.error instanceof Blob ? await err.error.text() : null;
        let message: string | null = null;
        try {
          message = text ? (JSON.parse(text) as { message?: string }).message ?? null : null;
        } catch {
          /* not JSON */
        }
        this.notify.error(message ?? err);
      },
    });
  }
}
