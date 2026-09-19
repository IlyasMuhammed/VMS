import { DatePipe } from '@angular/common';
import { Component, OnDestroy, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { Observable, catchError, forkJoin, map, of, switchMap } from 'rxjs';
import { TenantsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { LogoVariant, TenantListItem } from '../../core/models';
import { LogoUploadComponent } from './logo-upload.component';

const VARIANTS: readonly LogoVariant[] = ['light', 'dark'];

@Component({
  selector: 'app-tenants',
  standalone: true,
  imports: [DatePipe, FormsModule, ReactiveFormsModule, TableModule, ButtonModule, DialogModule, InputTextModule, TagModule, LogoUploadComponent],
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

    <p-dialog header="New tenant" [(visible)]="createVisible" [modal]="true" [style]="{ width: '680px' }">
      <form [formGroup]="createForm" class="form-grid">
        <div class="field"><label>Tenant code *</label><input pInputText formControlName="tenantCode" style="text-transform: uppercase" /><span class="hint">Letters, digits, hyphens.</span></div>
        <div class="field"><label>Tenant name *</label><input pInputText formControlName="tenantName" /></div>
        <div class="field"><label>Contact email</label><input pInputText type="email" formControlName="contactEmail" /></div>
        <div class="field"><label>Contact phone</label><input pInputText formControlName="contactPhone" /></div>
        <div class="field"><label>Country</label><input pInputText formControlName="country" /></div>
        <div class="field"><label>Time zone</label><input pInputText formControlName="timeZone" placeholder="e.g. Asia/Kolkata" /></div>
        <div class="field full"><label>Address</label><input pInputText formControlName="address" /></div>

        <div class="full"><h3>Branding</h3><span class="muted">Optional. The logo appears next to the tenant name. Add one for each appearance mode; PNG, JPEG or WebP, up to 512 KB.</span></div>
        <app-logo-upload variant="light" label="Logo for light mode" hint="Shown on light backgrounds. Use a dark logo." [(file)]="newLogo.light" />
        <app-logo-upload variant="dark" label="Logo for dark mode" hint="Shown on dark backgrounds. Use a light logo." [(file)]="newLogo.dark" />

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

    <p-dialog header="Edit tenant" [(visible)]="editVisible" [modal]="true" [style]="{ width: '640px' }" (onHide)="releaseEditLogos()">
      <form [formGroup]="editForm" class="form-grid">
        <div class="field full"><label>Tenant name *</label><input pInputText formControlName="tenantName" /></div>
        <div class="field"><label>Contact email</label><input pInputText type="email" formControlName="contactEmail" /></div>
        <div class="field"><label>Contact phone</label><input pInputText formControlName="contactPhone" /></div>
        <div class="field"><label>Country</label><input pInputText formControlName="country" /></div>
        <div class="field"><label>Time zone</label><input pInputText formControlName="timeZone" /></div>
        <div class="field full"><label>Address</label><input pInputText formControlName="address" /></div>

        <div class="full"><h3>Branding</h3><span class="muted">Replace or remove a logo, then save.</span></div>
        <app-logo-upload variant="light" label="Logo for light mode" hint="Shown on light backgrounds. Use a dark logo." [currentUrl]="editLogo.url.light()" [(file)]="editLogo.file.light" [(removed)]="editLogo.removed.light" />
        <app-logo-upload variant="dark" label="Logo for dark mode" hint="Shown on dark backgrounds. Use a light logo." [currentUrl]="editLogo.url.dark()" [(file)]="editLogo.file.dark" [(removed)]="editLogo.removed.dark" />
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
export class TenantsComponent implements OnDestroy {
  private readonly api = inject(TenantsApi);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  readonly rows = signal<TenantListItem[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly link = signal('');

  /** Files chosen in the New tenant dialog. Sent right after the tenant is created. */
  readonly newLogo = { light: signal<File | null>(null), dark: signal<File | null>(null) };

  /** State of the two logo tiles in the Edit tenant dialog. */
  readonly editLogo = {
    url: { light: signal<string | null>(null), dark: signal<string | null>(null) },
    file: { light: signal<File | null>(null), dark: signal<File | null>(null) },
    removed: { light: signal(false), dark: signal(false) },
  };

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

  ngOnDestroy(): void {
    this.releaseEditLogos();
  }

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
    this.newLogo.light.set(null);
    this.newLogo.dark.set(null);
    this.createVisible = true;
  }

  create(): void {
    if (this.createForm.invalid || this.busy()) return;
    this.busy.set(true);
    const files: Record<LogoVariant, File | null> = { light: this.newLogo.light(), dark: this.newLogo.dark() };

    this.api
      .create(this.createForm.getRawValue())
      .pipe(switchMap((r) => this.uploadLogos(r.tenantId, files).pipe(map((failed) => ({ r, failed })))))
      .subscribe({
        next: ({ r, failed }) => {
          this.busy.set(false);
          this.createVisible = false;
          this.link.set(r.adminInviteLink);
          this.linkVisible = true;
          if (failed.length) this.warnLogos(failed);
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
        this.releaseEditLogos();
        this.editVisible = true;

        const versions: Record<LogoVariant, string | null | undefined> = { light: d.logoLightVersion, dark: d.logoDarkVersion };
        for (const v of VARIANTS) {
          if (!versions[v]) continue;
          this.api
            .logo(d.id, v)
            .pipe(catchError(() => of(null)))
            .subscribe((blob) => {
              if (blob && this.editVisible && this.editId === d.id) this.editLogo.url[v].set(URL.createObjectURL(blob));
            });
        }
      },
      error: (err) => this.fail(err),
    });
  }

  saveEdit(): void {
    if (this.editForm.invalid || this.busy()) return;
    this.busy.set(true);
    const id = this.editId;

    this.api
      .update(id, this.editForm.getRawValue())
      .pipe(switchMap(() => this.applyLogoChanges(id)))
      .subscribe({
        next: (failed) => {
          this.busy.set(false);
          this.editVisible = false;
          if (failed.length) this.warnLogos(failed, 'Tenant details saved');
          else this.ok('Tenant updated');
          this.load();
        },
        error: (err) => {
          this.busy.set(false);
          this.fail(err);
        },
      });
  }

  /** Frees the preview blobs and clears the tiles. Runs when the edit dialog closes. */
  releaseEditLogos(): void {
    for (const v of VARIANTS) {
      const url = this.editLogo.url[v]();
      if (url) URL.revokeObjectURL(url);
      this.editLogo.url[v].set(null);
      this.editLogo.file[v].set(null);
      this.editLogo.removed[v].set(false);
    }
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

  /** Uploads whichever logos were chosen. Never throws: it returns a message per logo that failed. */
  private uploadLogos(id: string, files: Record<LogoVariant, File | null>): Observable<string[]> {
    const jobs = VARIANTS.filter((v) => files[v]).map((v) =>
      this.api.uploadLogo(id, v, files[v]!).pipe(
        map(() => ''),
        catchError((err) => of(`The ${v}-mode logo was not saved: ${errorMessage(err)}`)),
      ),
    );
    return jobs.length ? forkJoin(jobs).pipe(map((r) => r.filter(Boolean))) : of([]);
  }

  /** Applies the Edit dialog's logo choices: upload a new file, or delete the existing one. */
  private applyLogoChanges(id: string): Observable<string[]> {
    const jobs = VARIANTS.flatMap((v) => {
      const file = this.editLogo.file[v]();
      if (file) return [this.api.uploadLogo(id, v, file)];
      if (this.editLogo.removed[v]()) return [this.api.removeLogo(id, v)];
      return [];
    }).map((job, i) =>
      job.pipe(
        map(() => ''),
        catchError((err) => of(`A logo change was not saved: ${errorMessage(err)}`)),
      ),
    );
    return jobs.length ? forkJoin(jobs).pipe(map((r) => r.filter(Boolean))) : of([]);
  }

  private warnLogos(failed: string[], summary = 'Tenant created'): void {
    this.toast.add({ severity: 'warn', summary, detail: `${failed.join(' ')} You can add logos from Edit tenant.`, life: 9000 });
  }

  private ok(detail: string): void {
    this.toast.add({ severity: 'success', summary: 'Done', detail });
  }

  private fail(err: unknown): void {
    this.toast.add({ severity: 'error', summary: 'Error', detail: errorMessage(err) });
  }
}
