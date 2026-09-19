import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TenantsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { TenantListItem } from '../../core/models';

@Component({
  selector: 'app-tenants',
  standalone: true,
  imports: [DatePipe, FormsModule, ReactiveFormsModule, TableModule, ButtonModule, DialogModule, InputTextModule, TagModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Tenants</h1><div class="sub">Each tenant is a separate customer with its own users and roles.</div></div>
        <p-button label="New tenant" icon="pi pi-plus" (onClick)="openCreate()" />
      </div>

      <div class="card">
        <div class="toolbar">
          <input pInputText placeholder="Search name or code" [(ngModel)]="search" (keyup.enter)="reload()" style="min-width: 260px" />
          <p-button label="Search" icon="pi pi-search" severity="secondary" (onClick)="reload()" />
        </div>

        <p-table [value]="rows()" [lazy]="true" (onLazyLoad)="onLazy($event)" [paginator]="true" [rows]="pageSize" [totalRecords]="total()" [loading]="loading()" [rowsPerPageOptions]="[10, 20, 50]">
          <ng-template pTemplate="header"><tr><th>Code</th><th>Name</th><th>Contact</th><th>Created</th><th>Status</th><th></th></tr></ng-template>
          <ng-template pTemplate="body" let-t>
            <tr>
              <td>{{ t.tenantCode }}</td>
              <td>{{ t.tenantName }}</td>
              <td>{{ t.contactEmail }}</td>
              <td>{{ t.createdDate | date: 'mediumDate' }}</td>
              <td><p-tag [value]="t.isActive ? 'Active' : 'Inactive'" [severity]="t.isActive ? 'success' : 'secondary'" /></td>
              <td>
                <div class="actions">
                  <p-button icon="pi pi-pencil" [text]="true" severity="secondary" (onClick)="openEdit(t)" title="Edit" />
                  @if (t.tenantCode !== 'VMS-PLATFORM') {
                    <p-button [icon]="t.isActive ? 'pi pi-ban' : 'pi pi-check-circle'" [text]="true" severity="secondary" (onClick)="toggle(t)" [title]="t.isActive ? 'Deactivate' : 'Activate'" />
                  }
                </div>
              </td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage"><tr><td colspan="6" class="muted">No tenants found.</td></tr></ng-template>
        </p-table>
      </div>
    </div>

    <p-dialog header="New tenant" [(visible)]="createVisible" [modal]="true" [style]="{ width: '640px' }">
      <form [formGroup]="createForm" class="form-grid">
        <div class="field"><label>Tenant code *</label><input pInputText formControlName="tenantCode" style="text-transform: uppercase" /><span class="hint">Letters, digits, hyphens.</span></div>
        <div class="field"><label>Tenant name *</label><input pInputText formControlName="tenantName" /></div>
        <div class="field"><label>Contact email</label><input pInputText type="email" formControlName="contactEmail" /></div>
        <div class="field"><label>Contact phone</label><input pInputText formControlName="contactPhone" /></div>
        <div class="field"><label>Country</label><input pInputText formControlName="country" /></div>
        <div class="field"><label>Time zone</label><input pInputText formControlName="timeZone" placeholder="e.g. Asia/Kolkata" /></div>
        <div class="field full"><label>Address</label><input pInputText formControlName="address" /></div>
        <div class="full"><h3>First administrator</h3><span class="muted">Invited by email to set their own password.</span></div>
        <div class="field"><label>First name *</label><input pInputText formControlName="adminFirstName" /></div>
        <div class="field"><label>Last name</label><input pInputText formControlName="adminLastName" /></div>
        <div class="field full"><label>Email *</label><input pInputText type="email" formControlName="adminEmail" /></div>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="createVisible = false" />
        <p-button label="Create tenant" [loading]="busy()" [disabled]="createForm.invalid" (onClick)="create()" />
      </ng-template>
    </p-dialog>

    <p-dialog header="Edit tenant" [(visible)]="editVisible" [modal]="true" [style]="{ width: '560px' }">
      <form [formGroup]="editForm" class="form-grid">
        <div class="field full"><label>Tenant name *</label><input pInputText formControlName="tenantName" /></div>
        <div class="field"><label>Contact email</label><input pInputText type="email" formControlName="contactEmail" /></div>
        <div class="field"><label>Contact phone</label><input pInputText formControlName="contactPhone" /></div>
        <div class="field"><label>Country</label><input pInputText formControlName="country" /></div>
        <div class="field"><label>Time zone</label><input pInputText formControlName="timeZone" /></div>
        <div class="field full"><label>Address</label><input pInputText formControlName="address" /></div>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="editVisible = false" />
        <p-button label="Save" [loading]="busy()" [disabled]="editForm.invalid" (onClick)="saveEdit()" />
      </ng-template>
    </p-dialog>

    <p-dialog header="Tenant created" [(visible)]="linkVisible" [modal]="true" [style]="{ width: '620px' }">
      <p>Give the administrator this one-time link to set their password. It is valid for 72 hours and is <strong>not shown again</strong>.</p>
      <div class="link-box">
        <input pInputText readonly [value]="link()" />
        <p-button icon="pi pi-copy" severity="secondary" (onClick)="copy()" title="Copy" />
      </div>
      <ng-template pTemplate="footer"><p-button label="Done" (onClick)="linkVisible = false" /></ng-template>
    </p-dialog>
  `,
})
export class TenantsComponent {
  private readonly api = inject(TenantsApi);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  readonly rows = signal<TenantListItem[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly link = signal('');

  search = '';
  page = 1;
  pageSize = 20;
  createVisible = false;
  editVisible = false;
  linkVisible = false;
  private editId = '';

  readonly createForm = this.fb.nonNullable.group({
    tenantCode: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9-]{1,29}$/)]],
    tenantName: ['', Validators.required],
    contactEmail: [''],
    contactPhone: [''],
    country: [''],
    timeZone: [''],
    address: [''],
    adminFirstName: ['', Validators.required],
    adminLastName: [''],
    adminEmail: ['', [Validators.required, Validators.email]],
  });

  readonly editForm = this.fb.nonNullable.group({
    tenantName: ['', Validators.required],
    contactEmail: [''],
    contactPhone: [''],
    country: [''],
    timeZone: [''],
    address: [''],
  });

  onLazy(e: TableLazyLoadEvent): void {
    this.pageSize = e.rows ?? this.pageSize;
    this.page = Math.floor((e.first ?? 0) / this.pageSize) + 1;
    this.load();
  }

  reload(): void {
    this.page = 1;
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.list(this.search, this.page, this.pageSize).subscribe({
      next: (p) => {
        this.rows.set(p.items);
        this.total.set(p.totalCount);
        this.loading.set(false);
      },
      error: (err) => {
        this.fail(err);
        this.loading.set(false);
      },
    });
  }

  openCreate(): void {
    this.createForm.reset();
    this.createVisible = true;
  }

  create(): void {
    if (this.createForm.invalid || this.busy()) return;
    this.busy.set(true);
    this.api.create(this.createForm.getRawValue()).subscribe({
      next: (r) => {
        this.busy.set(false);
        this.createVisible = false;
        this.link.set(r.adminInviteLink);
        this.linkVisible = true;
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.fail(err);
      },
    });
  }

  openEdit(t: TenantListItem): void {
    this.editId = t.id;
    this.api.get(t.id).subscribe({
      next: (d) => {
        this.editForm.reset({
          tenantName: d.tenantName,
          contactEmail: d.contactEmail ?? '',
          contactPhone: d.contactPhone ?? '',
          country: d.country ?? '',
          timeZone: d.timeZone ?? '',
          address: d.address ?? '',
        });
        this.editVisible = true;
      },
      error: (err) => this.fail(err),
    });
  }

  saveEdit(): void {
    if (this.editForm.invalid || this.busy()) return;
    this.busy.set(true);
    this.api.update(this.editId, this.editForm.getRawValue()).subscribe({
      next: () => {
        this.busy.set(false);
        this.editVisible = false;
        this.ok('Tenant updated');
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.fail(err);
      },
    });
  }

  toggle(t: TenantListItem): void {
    const activate = !t.isActive;
    this.confirm.confirm({
      header: activate ? 'Activate tenant' : 'Deactivate tenant',
      message: activate
        ? `Let ${t.tenantName}'s users sign in again?`
        : `Every user of ${t.tenantName} will be signed out and unable to sign in until it is reactivated.`,
      icon: 'pi pi-exclamation-triangle',
      accept: () =>
        this.api.setStatus(t.id, activate).subscribe({
          next: () => {
            this.ok(activate ? 'Tenant activated' : 'Tenant deactivated');
            this.load();
          },
          error: (err) => this.fail(err),
        }),
    });
  }

  copy(): void {
    navigator.clipboard?.writeText(this.link()).then(() => this.ok('Link copied'));
  }

  private ok(detail: string): void {
    this.toast.add({ severity: 'success', summary: 'Done', detail });
  }

  private fail(err: unknown): void {
    this.toast.add({ severity: 'error', summary: 'Error', detail: errorMessage(err) });
  }
}
