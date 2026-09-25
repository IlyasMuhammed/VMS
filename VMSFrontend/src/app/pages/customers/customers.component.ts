import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { AuthService } from '../../core/auth.service';
import { CustomersApi } from '../../core/api.services';
import { CustomerStatusComponent } from './customer-bits.component';
import { DataGridComponent, GridCellDirective, GridColumn } from '../../shared/data-grid.component';
import { FilterBarComponent, FilterDef } from '../../shared/filter-bar.component';
import { FilterValue, ListQuery, isFiltering, noFilters } from '../../shared/list-query';
import { optionsOf } from './customer-logic';

/** The customer list (FSD §48.2 screen 1): quick search, status/city filters, server paging. */
@Component({
  selector: 'app-customers',
  standalone: true,
  imports: [ButtonModule, DataGridComponent, GridCellDirective, FilterBarComponent, CustomerStatusComponent],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Customers</h1><div class="sub">Who trips are run for and invoices are billed to.</div></div>
        <div class="header-actions">
          @if (canCreate()) { <p-button label="New customer" icon="pi pi-plus" (onClick)="create()" /> }
        </div>
      </div>

      <div class="card">
        <vms-filter-bar [(value)]="bar" [filters]="filterDefs" searchPlaceholder="Search code or name" />

        <vms-data-grid label="Customers" [columns]="columns" [load]="load" [search]="bar().search" [filters]="bar().filters" [filtering]="filtering()"
                       emptyMessage="No customers found. Add the first one with New customer." noMatchMessage="No customers match these filters."
                       (clearFilters)="clearFilters()">
          <ng-template vmsCell="customerCode" let-c><a class="mono" [href]="'/customers/' + c.customerId" (click)="open($event, c)">{{ c.customerCode }}</a></ng-template>
          <ng-template vmsCell="customerName" let-c>
            {{ c.customerName }}
            @if (c.shortName) { <div class="muted small">{{ c.shortName }}</div> }
          </ng-template>
          <ng-template vmsCell="currencyCode" let-c>{{ c.currencyCode }}</ng-template>
          <ng-template vmsCell="paymentTermsDays" let-c>{{ c.paymentTermsDays }} days</ng-template>
          <ng-template vmsCell="status" let-c><vms-customer-status [status]="c.status" /></ng-template>
        </vms-data-grid>
      </div>
    </div>
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
export class CustomersComponent {
  private readonly api = inject(CustomersApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly canCreate = computed(() => this.auth.hasPermission('TRP.CUSTOMER.EDIT'));

  readonly bar = signal<FilterValue>(noFilters());
  readonly filtering = computed(() => isFiltering(this.bar()));

  // No "sortable" columns: the backend's own customer list has no sort parameter, unlike Partners/Vehicles —
  // marking a header sortable here would offer something the server silently ignores.
  readonly columns: GridColumn[] = [
    { key: 'customerCode', header: 'Code', width: '9rem' },
    { key: 'customerName', header: 'Name' },
    { key: 'currencyCode', header: 'Currency', width: '7rem' },
    { key: 'paymentTermsDays', header: 'Terms', width: '7rem' },
    { key: 'status', header: 'Status', width: '8rem' },
  ];

  readonly filterDefs: FilterDef[] = [{ key: 'status', label: 'Status', kind: 'select', options: optionsOf(['Draft', 'Active', 'Inactive']) }];

  readonly load = (query: ListQuery) => this.api.list(query);

  clearFilters(): void {
    this.bar.set(noFilters());
  }

  create(): void {
    void this.router.navigate(['/customers/new']);
  }

  open(event: MouseEvent | null, customer: { customerId: number }): void {
    if (event && (event.ctrlKey || event.metaKey || event.shiftKey || event.button === 1)) return;
    event?.preventDefault();
    void this.router.navigate(['/customers', customer.customerId]);
  }
}
