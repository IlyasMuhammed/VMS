import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { RoleOption, UsersApi } from '../../core/api.services';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/api-error';
import { UserDetail, UserListFilter, UserListItem } from '../../core/models';

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [DatePipe, FormsModule, ReactiveFormsModule, TableModule, ButtonModule, DialogModule, InputTextModule, SelectModule, TagModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Users</h1><div class="sub">People who can sign in to this tenant.</div></div>
        @if (canManage()) { <p-button label="Invite user" icon="pi pi-user-plus" (onClick)="openCreate()" /> }
      </div>

      <div class="card">
        <div class="toolbar">
          <input pInputText placeholder="Search name or email" [(ngModel)]="filter.search" (keyup.enter)="reload()" style="min-width: 260px" />
          <p-select [options]="statusOptions" optionLabel="label" optionValue="value" [(ngModel)]="filter.status" (onChange)="reload()" placeholder="Any status" [showClear]="true" />
          <p-select [options]="roleOptions()" optionLabel="name" optionValue="roleId" [(ngModel)]="filter.roleId" (onChange)="reload()" placeholder="Any role" [showClear]="true" />
          <p-button label="Search" icon="pi pi-search" severity="secondary" (onClick)="reload()" />
        </div>

        <p-table [value]="rows()" [lazy]="true" (onLazyLoad)="onLazy($event)" [paginator]="true" [rows]="filter.pageSize" [totalRecords]="total()" [loading]="loading()" [rowsPerPageOptions]="[10, 20, 50]">
          <ng-template pTemplate="header">
            <tr><th>Name</th><th>Email</th><th>Role</th><th>Department</th><th>Status</th><th>Last sign-in</th><th></th></tr>
          </ng-template>
          <ng-template pTemplate="body" let-u>
            <tr>
              <td>{{ u.firstName }} {{ u.lastName }}</td>
              <td>{{ u.email }}</td>
              <td>{{ u.role?.value }}</td>
              <td>{{ u.department }}</td>
              <td>
                @if (u.invitePending && !u.isActive) { <p-tag value="Invited" severity="info" /> }
                @else if (u.isActive) { <p-tag value="Active" severity="success" /> }
                @else { <p-tag value="Inactive" severity="secondary" /> }
              </td>
              <td>{{ u.lastLoginAt ? (u.lastLoginAt | date: 'medium') : '—' }}</td>
              <td>
                @if (canManage() && u.userId !== auth.user()?.userId) {
                  <div class="actions">
                    <p-button icon="pi pi-pencil" [text]="true" severity="secondary" title="Edit" (onClick)="openEdit(u)" />
                    <p-button icon="pi pi-shield" [text]="true" severity="secondary" (onClick)="openRole(u)" title="Change role" />
                    <p-button icon="pi pi-key" [text]="true" severity="secondary" (onClick)="resetPassword(u)" title="Issue password reset link" />
                    <p-button [icon]="u.isActive ? 'pi pi-ban' : 'pi pi-check-circle'" [text]="true" severity="secondary" (onClick)="toggleActive(u)" [title]="u.isActive ? 'Deactivate' : 'Activate'" />
                    <p-button icon="pi pi-trash" [text]="true" severity="danger" (onClick)="remove(u)" title="Delete" />
                  </div>
                }
              </td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage"><tr><td colspan="7" class="muted">No users found.</td></tr></ng-template>
        </p-table>
      </div>
    </div>

    <!-- Invite -->
    <p-dialog header="Invite user" [(visible)]="createVisible" [modal]="true" [style]="{ width: '560px' }">
      <form [formGroup]="createForm" class="form-grid" (ngSubmit)="create()">
        <div class="field"><label>First name *</label><input pInputText formControlName="firstName" /></div>
        <div class="field"><label>Last name</label><input pInputText formControlName="lastName" /></div>
        <div class="field full"><label>Email *</label><input pInputText type="email" formControlName="email" /></div>
        <div class="field"><label>Phone</label><input pInputText formControlName="phone" /></div>
        <div class="field"><label>Department</label><input pInputText formControlName="department" /></div>
        <div class="field full"><label>Role *</label>
          <p-select [options]="roleOptions()" optionLabel="name" optionValue="roleId" formControlName="roleId" placeholder="Choose a role" [fluid]="true" />
        </div>
        <div class="full muted">They get an email with a one-time link to set their password.</div>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="createVisible = false" />
        <p-button label="Send invite" icon="pi pi-send" [loading]="busy()" [disabled]="createForm.invalid" (onClick)="create()" />
      </ng-template>
    </p-dialog>

    <!-- Edit -->
    <p-dialog header="Edit user" [(visible)]="editVisible" [modal]="true" [style]="{ width: '560px' }">
      <form [formGroup]="editForm" class="form-grid">
        <div class="field"><label>First name *</label><input pInputText formControlName="firstName" /></div>
        <div class="field"><label>Last name</label><input pInputText formControlName="lastName" /></div>
        <div class="field"><label>Phone</label><input pInputText formControlName="phone" /></div>
        <div class="field"><label>Department</label><input pInputText formControlName="department" /></div>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="editVisible = false" />
        <p-button label="Save" [loading]="busy()" [disabled]="editForm.invalid" (onClick)="saveEdit()" />
      </ng-template>
    </p-dialog>

    <!-- Change role -->
    <p-dialog header="Change role" [(visible)]="roleVisible" [modal]="true" [style]="{ width: '420px' }">
      <div class="field">
        <label>Role for {{ target()?.firstName }} {{ target()?.lastName }}</label>
        <p-select [options]="roleOptions()" optionLabel="name" optionValue="roleId" [(ngModel)]="newRoleId" [fluid]="true" />
        <span class="hint">They will be signed out so the new role takes effect.</span>
      </div>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="roleVisible = false" />
        <p-button label="Assign" [loading]="busy()" [disabled]="!newRoleId" (onClick)="saveRole()" />
      </ng-template>
    </p-dialog>

    <!-- One-time link -->
    <p-dialog [header]="linkTitle()" [(visible)]="linkVisible" [modal]="true" [style]="{ width: '620px' }">
      <p>Share this one-time link with the user. It is valid for 72 hours and is <strong>not shown again</strong>.</p>
      <div class="link-box">
        <input pInputText readonly [value]="link()" />
        <p-button icon="pi pi-copy" severity="secondary" (onClick)="copyLink()" title="Copy" />
      </div>
      <p class="muted">An email with the same link was also sent, if outbound email is configured.</p>
      <ng-template pTemplate="footer"><p-button label="Done" (onClick)="linkVisible = false" /></ng-template>
    </p-dialog>
  `,
})
export class UsersComponent implements OnInit {
  readonly auth = inject(AuthService);
  private readonly api = inject(UsersApi);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  readonly canManage = computed(() => this.auth.hasPermission('USER_MANAGE'));
  readonly rows = signal<UserListItem[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly roleOptions = signal<RoleOption[]>([]);
  readonly target = signal<UserListItem | null>(null);
  readonly link = signal('');
  readonly linkTitle = signal('');

  readonly statusOptions = [
    { label: 'Active', value: 'active' },
    { label: 'Inactive', value: 'inactive' },
  ];
  filter: UserListFilter = { search: '', status: '', roleId: null, page: 1, pageSize: 20 };

  createVisible = false;
  editVisible = false;
  roleVisible = false;
  linkVisible = false;
  newRoleId: number | null = null;

  readonly createForm = this.fb.nonNullable.group({
    firstName: ['', Validators.required],
    lastName: [''],
    email: ['', [Validators.required, Validators.email]],
    phone: [''],
    department: [''],
    roleId: [0, Validators.min(1)],
  });
  readonly editForm = this.fb.nonNullable.group({
    firstName: ['', Validators.required],
    lastName: [''],
    phone: [''],
    department: [''],
  });

  ngOnInit(): void {
    this.api.assignableRoles().subscribe({ next: (r) => this.roleOptions.set(r), error: () => this.roleOptions.set([]) });
  }

  onLazy(e: TableLazyLoadEvent): void {
    const rows = e.rows ?? this.filter.pageSize;
    this.filter.pageSize = rows;
    this.filter.page = Math.floor((e.first ?? 0) / rows) + 1;
    this.load();
  }

  reload(): void {
    this.filter.page = 1;
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.list(this.filter).subscribe({
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
    this.createForm.reset({ firstName: '', lastName: '', email: '', phone: '', department: '', roleId: 0 });
    this.createVisible = true;
  }

  create(): void {
    if (this.createForm.invalid || this.busy()) return;
    this.busy.set(true);
    const v = this.createForm.getRawValue();
    this.api.create(v).subscribe({
      next: (u) => {
        this.busy.set(false);
        this.createVisible = false;
        this.showLink(u, `Invite for ${u.firstName}`);
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.fail(err);
      },
    });
  }

  openEdit(u: UserListItem): void {
    this.target.set(u);
    this.editForm.reset({ firstName: u.firstName, lastName: u.lastName ?? '', phone: '', department: u.department ?? '' });
    this.editVisible = true;
  }

  saveEdit(): void {
    const u = this.target();
    if (!u || this.editForm.invalid || this.busy()) return;
    this.busy.set(true);
    this.api.patch(u.userId, this.editForm.getRawValue()).subscribe({
      next: () => {
        this.busy.set(false);
        this.editVisible = false;
        this.ok('User updated');
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.fail(err);
      },
    });
  }

  openRole(u: UserListItem): void {
    this.target.set(u);
    this.newRoleId = u.role?.id ?? null;
    this.roleVisible = true;
  }

  saveRole(): void {
    const u = this.target();
    if (!u || !this.newRoleId || this.busy()) return;
    this.busy.set(true);
    this.api.assignRole(u.userId, this.newRoleId).subscribe({
      next: () => {
        this.busy.set(false);
        this.roleVisible = false;
        this.ok('Role assigned');
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.fail(err);
      },
    });
  }

  toggleActive(u: UserListItem): void {
    const activate = !u.isActive;
    this.confirm.confirm({
      header: activate ? 'Activate user' : 'Deactivate user',
      message: activate ? `Allow ${u.firstName} to sign in again?` : `${u.firstName} will be signed out and unable to sign in.`,
      icon: 'pi pi-exclamation-triangle',
      accept: () =>
        this.api.patch(u.userId, { isActive: activate }).subscribe({
          next: () => {
            this.ok(activate ? 'User activated' : 'User deactivated');
            this.load();
          },
          error: (err) => this.fail(err),
        }),
    });
  }

  resetPassword(u: UserListItem): void {
    this.confirm.confirm({
      header: 'Issue password reset link',
      message: `${u.firstName} will be signed out everywhere. Continue?`,
      icon: 'pi pi-key',
      accept: () =>
        this.api.resetPassword(u.userId).subscribe({
          next: (d) => this.showLink(d, `Password reset for ${u.firstName}`),
          error: (err) => this.fail(err),
        }),
    });
  }

  remove(u: UserListItem): void {
    this.confirm.confirm({
      header: 'Delete user',
      message: `Delete ${u.firstName} ${u.lastName ?? ''}? This cannot be undone.`,
      icon: 'pi pi-trash',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () =>
        this.api.remove(u.userId).subscribe({
          next: () => {
            this.ok('User deleted');
            this.load();
          },
          error: (err) => this.fail(err),
        }),
    });
  }

  copyLink(): void {
    navigator.clipboard?.writeText(this.link()).then(() => this.ok('Link copied'));
  }

  private showLink(u: UserDetail, title: string): void {
    this.linkTitle.set(title);
    this.link.set(u.inviteLink ?? '');
    this.linkVisible = !!u.inviteLink;
  }

  private ok(detail: string): void {
    this.toast.add({ severity: 'success', summary: 'Done', detail });
  }

  private fail(err: unknown): void {
    this.toast.add({ severity: 'error', summary: 'Error', detail: errorMessage(err) });
  }
}
