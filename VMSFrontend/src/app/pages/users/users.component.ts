import { Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { AbstractControl, FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { RoleOption, UsersApi } from '../../core/api.services';
import { AuthService } from '../../core/auth.service';
import { apiErrors } from '../../core/api-error';
import { InstantPipe } from '../../core/datetime/datetime.pipes';
import { MessagesService } from '../../core/messages.service';
import { EffectivePermission, UserDetail, UserListItem, UserRoleItem } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { BranchPickerComponent } from '../../shared/branch-picker.component';
import { ConfirmService } from '../../shared/confirm.service';
import { DataGridComponent, GridActionsDirective, GridCellDirective, GridColumn } from '../../shared/data-grid.component';
import { FieldComponent } from '../../shared/field.component';
import { FilterBarComponent, FilterDef } from '../../shared/filter-bar.component';
import { FilterValue, ListQuery, isFiltering, noFilters } from '../../shared/list-query';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { applyServerErrors } from '../../shared/server-errors';
import { optionsOf, words } from '../vehicles/vehicle-logic';

const SCOPE_OPTIONS = optionsOf(['AllBranches', 'OwnBranch', 'OwnVehicles', 'OwnRecords']);

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [
    InstantPipe, FormsModule, ReactiveFormsModule, ButtonModule, DialogModule, InputTextModule, SelectModule, TagModule,
    DataGridComponent, GridCellDirective, GridActionsDirective, FilterBarComponent, FieldComponent, BranchPickerComponent, PartnerPickerComponent,
  ],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Users</h1><div class="sub">People who can sign in to this tenant.</div></div>
        @if (canManage()) { <p-button label="Invite user" icon="pi pi-user-plus" (onClick)="openCreate()" /> }
      </div>

      <div class="card">
        <vms-filter-bar [(value)]="bar" [filters]="filterDefs()" searchPlaceholder="Search name or email" [minSearch]="2" />

        <vms-data-grid label="Users" [columns]="columns" [load]="loadUsers" [search]="bar().search" [filters]="bar().filters" [filtering]="filtering()"
                       emptyMessage="No users yet." noMatchMessage="No users match these filters." (clearFilters)="clearFilters()">
          <ng-template vmsCell="name" let-u>{{ u.firstName }} {{ u.lastName }}</ng-template>
          <ng-template vmsCell="role" let-u>{{ u.role?.value }}</ng-template>
          <ng-template vmsCell="scope" let-u>{{ words(u.scopeType) }}@if (u.branchName) { <span class="muted"> — {{ u.branchName }}</span> }</ng-template>
          <ng-template vmsCell="status" let-u>
            @if (u.invitePending && !u.isActive) { <p-tag value="Invited" severity="info" /> }
            @else if (u.isActive) { <p-tag value="Active" severity="success" /> }
            @else { <p-tag value="Inactive" severity="secondary" /> }
          </ng-template>
          <ng-template vmsCell="lastLoginAt" let-u>{{ u.lastLoginAt | vmsInstant }}</ng-template>
          <ng-template vmsRowActions let-u>
            @if (canManage() && u.userId !== auth.user()?.userId) {
              <p-button icon="pi pi-pencil" [text]="true" severity="secondary" title="Edit" [ariaLabel]="'Edit ' + u.firstName" (onClick)="openEdit(u)" />
              <p-button icon="pi pi-shield" [text]="true" severity="secondary" title="Change role" [ariaLabel]="'Change role of ' + u.firstName" (onClick)="openRole(u)" />
              <p-button icon="pi pi-sitemap" [text]="true" severity="secondary" title="Manage access" [ariaLabel]="'Manage access of ' + u.firstName" (onClick)="openAccess(u)" />
              <p-button icon="pi pi-eye" [text]="true" severity="secondary" title="Effective permissions" [ariaLabel]="'Effective permissions of ' + u.firstName" (onClick)="openEffective(u)" />
              <p-button icon="pi pi-key" [text]="true" severity="secondary" title="Issue password reset link" [ariaLabel]="'Reset password of ' + u.firstName" (onClick)="resetPassword(u)" />
              <p-button [icon]="u.isActive ? 'pi pi-ban' : 'pi pi-check-circle'" [text]="true" severity="secondary" [title]="u.isActive ? 'Deactivate' : 'Activate'"
                        [ariaLabel]="(u.isActive ? 'Deactivate ' : 'Activate ') + u.firstName" (onClick)="toggleActive(u)" />
              <p-button icon="pi pi-trash" [text]="true" severity="danger" title="Delete" [ariaLabel]="'Delete ' + u.firstName" (onClick)="remove(u)" />
            }
          </ng-template>
        </vms-data-grid>
      </div>
    </div>

    <!-- Invite -->
    <p-dialog header="Invite user" [(visible)]="createVisible" [modal]="true" [style]="{ width: '560px' }">
      <form [formGroup]="createForm" class="form-grid" (ngSubmit)="create()" novalidate>
        <vms-field label="First name" [control]="createForm.controls.firstName" for="cu-first"><input pInputText id="cu-first" formControlName="firstName" /></vms-field>
        <vms-field label="Last name" [control]="createForm.controls.lastName" for="cu-last"><input pInputText id="cu-last" formControlName="lastName" /></vms-field>
        <vms-field class="full" label="Email" [control]="createForm.controls.email" for="cu-email"><input pInputText id="cu-email" type="email" formControlName="email" /></vms-field>
        <vms-field label="Phone" [control]="createForm.controls.phone" for="cu-phone"><input pInputText id="cu-phone" formControlName="phone" /></vms-field>
        <vms-field label="Department" [control]="createForm.controls.department" for="cu-dept"><input pInputText id="cu-dept" formControlName="department" /></vms-field>
        <vms-field class="full" label="Role" [control]="createForm.controls.roleId" for="cu-role">
          <p-select inputId="cu-role" [options]="roleOptions()" optionLabel="name" optionValue="roleId" formControlName="roleId" placeholder="Choose a role" [fluid]="true" appendTo="body" />
        </vms-field>
        <div class="full muted">They get an email with a one-time link to set their password.</div>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="createVisible = false" />
        <p-button label="Send invite" icon="pi pi-send" [loading]="busy()" (onClick)="create()" />
      </ng-template>
    </p-dialog>

    <!-- Edit -->
    <p-dialog header="Edit user" [(visible)]="editVisible" [modal]="true" [style]="{ width: '560px' }">
      <form [formGroup]="editForm" class="form-grid" novalidate>
        <vms-field label="First name" [control]="editForm.controls.firstName" for="eu-first"><input pInputText id="eu-first" formControlName="firstName" /></vms-field>
        <vms-field label="Last name" [control]="editForm.controls.lastName" for="eu-last"><input pInputText id="eu-last" formControlName="lastName" /></vms-field>
        <vms-field label="Phone" [control]="editForm.controls.phone" for="eu-phone"><input pInputText id="eu-phone" formControlName="phone" /></vms-field>
        <vms-field label="Department" [control]="editForm.controls.department" for="eu-dept"><input pInputText id="eu-dept" formControlName="department" /></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="editVisible = false" />
        <p-button label="Save" [loading]="busy()" (onClick)="saveEdit()" />
      </ng-template>
    </p-dialog>

    <!-- Change role -->
    <p-dialog header="Change role" [(visible)]="roleVisible" [modal]="true" [style]="{ width: '420px' }">
      <div class="field">
        <label>Role for {{ target()?.firstName }} {{ target()?.lastName }}</label>
        <p-select [options]="roleOptions()" optionLabel="name" optionValue="roleId" [(ngModel)]="newRoleId" [fluid]="true" appendTo="body" />
        <span class="hint">They will be signed out so the new role takes effect.</span>
      </div>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="roleVisible = false" />
        <p-button label="Assign" [loading]="busy()" [disabled]="!newRoleId" (onClick)="saveRole()" />
      </ng-template>
    </p-dialog>

    <!-- Manage access: roles, scope, driver link (§23B) -->
    <p-dialog header="Manage access" [(visible)]="accessVisible" [modal]="true" [style]="{ width: '620px' }">
      @if (accessDetail(); as d) {
        <h3>Roles</h3>
        <table class="roles-table">
          <thead><tr><th>Role</th><th>Scope</th><th></th></tr></thead>
          <tbody>
            @for (r of d.roles; track r.roleId) {
              <tr>
                <td>{{ r.roleName }} @if (r.isPrimary) { <span class="muted small">(primary)</span> }</td>
                <td>{{ words(r.scopeType) }}@if (r.branchName) { — {{ r.branchName }} }</td>
                <td class="row-actions">
                  @if (d.roles.length > 1) { <p-button label="Remove" [text]="true" size="small" severity="danger" [loading]="busy()" (onClick)="removeRole(d, r)" /> }
                </td>
              </tr>
            }
          </tbody>
        </table>

        <div class="add-role">
          <p-select [options]="addableRoles(d)" optionLabel="name" optionValue="roleId" [(ngModel)]="addRoleId" [ngModelOptions]="{ standalone: true }" placeholder="Add a role…" [fluid]="true" appendTo="body" />
          <p-select [options]="scopeOptions" optionLabel="label" optionValue="value" [(ngModel)]="addRoleScope" [ngModelOptions]="{ standalone: true }" [fluid]="true" appendTo="body" />
          @if (addRoleScope === 'OwnBranch') { <vms-branch-picker [(ngModel)]="addRoleBranch" [ngModelOptions]="{ standalone: true }" placeholder="Branch" /> }
          <p-button label="Add" icon="pi pi-plus" [disabled]="!addRoleId" [loading]="busy()" (onClick)="addRole(d)" />
        </div>

        <h3>Primary role's scope</h3>
        <div class="scope-row">
          <p-select [options]="scopeOptions" optionLabel="label" optionValue="value" [(ngModel)]="scopeType" [ngModelOptions]="{ standalone: true }" [fluid]="true" appendTo="body" />
          @if (scopeType === 'OwnBranch') { <vms-branch-picker [(ngModel)]="scopeBranch" [ngModelOptions]="{ standalone: true }" placeholder="Branch" /> }
          <p-button label="Save scope" [loading]="busy()" (onClick)="saveScope(d)" />
        </div>

        <h3>Driver link</h3>
        <p class="muted small">§23B.1 — the Business Partner this user is the same person as, when they are also a driver. Sets which vehicles "Own vehicles" scope shows them.</p>
        <div class="scope-row">
          <vms-partner-picker role="Driver" [allowCreate]="false" [(ngModel)]="driverPartnerId" [ngModelOptions]="{ standalone: true }" placeholder="No linked partner" />
          <p-button label="Save link" [loading]="busy()" (onClick)="saveDriverLink(d)" />
        </div>
      } @else {
        <p class="muted">Loading…</p>
      }
      <ng-template pTemplate="footer"><p-button label="Done" (onClick)="accessVisible = false" /></ng-template>
    </p-dialog>

    <!-- Effective permissions (§23B.6: "why can they see that?") -->
    <p-dialog header="Effective permissions" [(visible)]="effectiveVisible" [modal]="true" [style]="{ width: '620px' }">
      @if (target(); as t) { <p class="muted">{{ t.firstName }} {{ t.lastName }} — the union of every role they hold.</p> }
      <div class="eff-list">
        @for (g of effectiveByModule(); track g.module) {
          <div class="group">
            <h4>{{ g.module }}</h4>
            @for (p of g.permissions; track p.code) {
              <div class="eff-row">
                <span><strong>{{ p.name }}</strong><span class="muted small"> ({{ p.code }})</span></span>
                <span class="muted small">Granted by: {{ p.grantedByRoles.join(', ') }}</span>
              </div>
            }
          </div>
        }
        @if (effectivePermissions().length === 0) { <p class="muted">Loading…</p> }
      </div>
      <ng-template pTemplate="footer"><p-button label="Done" (onClick)="effectiveVisible = false" /></ng-template>
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
  styles: [
    `
      h3 { font-size: .95rem; margin: 1.25rem 0 .5rem; } h3:first-of-type { margin-top: 0; }
      .roles-table { width: 100%; border-collapse: collapse; }
      .roles-table th, .roles-table td { text-align: left; padding: .4rem .5rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; }
      .roles-table th { color: var(--vms-muted); font-weight: 600; } .row-actions { text-align: right; }
      .add-role, .scope-row { display: grid; grid-template-columns: 1fr 1fr auto; gap: .5rem; align-items: center; margin-top: .5rem; }
      .small { font-size: .8rem; }
      .eff-list { max-height: 24rem; overflow: auto; }
      .eff-list .group { margin-bottom: 1rem; }
      .eff-list h4 { margin: 0 0 .35rem; color: var(--vms-muted); text-transform: uppercase; font-size: .75rem; letter-spacing: .05em; }
      .eff-row { display: flex; justify-content: space-between; gap: 1rem; padding: .3rem 0; border-bottom: 1px solid var(--vms-border); font-size: .875rem; }
    `,
  ],
})
export class UsersComponent implements OnInit {
  readonly auth = inject(AuthService);
  private readonly api = inject(UsersApi);
  private readonly fb = inject(FormBuilder);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);
  private readonly confirm = inject(ConfirmService);
  private readonly grid = viewChild(DataGridComponent);

  readonly canManage = computed(() => this.auth.hasPermission('USER_MANAGE'));
  readonly busy = signal(false);
  readonly roleOptions = signal<RoleOption[]>([]);
  readonly target = signal<UserListItem | null>(null);
  readonly link = signal('');
  readonly linkTitle = signal('');
  protected readonly words = words;
  protected readonly scopeOptions = SCOPE_OPTIONS;

  // Manage access (roles, scope, driver link — §23B)
  accessVisible = false;
  readonly accessDetail = signal<UserDetail | null>(null);
  addRoleId: number | null = null;
  addRoleScope = 'AllBranches';
  addRoleBranch: string | null = null;
  scopeType = 'AllBranches';
  scopeBranch: string | null = null;
  driverPartnerId: number | null = null;

  // Effective permissions viewer (§23B.6)
  effectiveVisible = false;
  readonly effectivePermissions = signal<EffectivePermission[]>([]);
  readonly effectiveByModule = computed(() => {
    const byModule = new Map<string, EffectivePermission[]>();
    for (const p of this.effectivePermissions()) {
      const list = byModule.get(p.module) ?? [];
      list.push(p);
      byModule.set(p.module, list);
    }
    return [...byModule.entries()].map(([module, permissions]) => ({ module, permissions })).sort((a, b) => a.module.localeCompare(b.module));
  });

  // The list: what the filter bar holds is what the grid asks the server for.
  readonly bar = signal<FilterValue>(noFilters());
  readonly filtering = computed(() => isFiltering(this.bar()));
  readonly columns: GridColumn[] = [
    { key: 'name', header: 'Name' },
    { key: 'email', header: 'Email' },
    { key: 'role', header: 'Role' },
    { key: 'scope', header: 'Scope' },
    { key: 'department', header: 'Department' },
    { key: 'status', header: 'Status' },
    { key: 'lastLoginAt', header: 'Last sign-in' },
  ];
  readonly filterDefs = computed<FilterDef[]>(() => [
    { key: 'status', label: 'Status', kind: 'select', options: [{ label: 'Active', value: 'active' }, { label: 'Inactive', value: 'inactive' }] },
    { key: 'roleId', label: 'Role', kind: 'select', options: this.roleOptions().map((r) => ({ label: r.name, value: r.roleId })) },
  ]);
  readonly loadUsers = (q: ListQuery) =>
    this.api.list({
      search: q.search,
      status: (q.filters['status'] as '' | 'active' | 'inactive' | undefined) ?? '',
      roleId: (q.filters['roleId'] as number | undefined) ?? null,
      page: q.page,
      pageSize: q.pageSize,
    });

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
    roleId: this.fb.control<number | null>(null, Validators.required),
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

  clearFilters(): void {
    this.bar.set(noFilters());
  }

  /** Fetches the current page again after a change made from this screen. */
  private load(): void {
    this.grid()?.reload();
  }

  openCreate(): void {
    this.createForm.reset({ firstName: '', lastName: '', email: '', phone: '', department: '', roleId: null });
    this.createVisible = true;
  }

  create(): void {
    if (this.busy()) return;
    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched(); // show every problem at once, next to its field
      return;
    }
    this.busy.set(true);
    const v = this.createForm.getRawValue();
    this.api.create({ ...v, roleId: v.roleId! }).subscribe({
      next: (u) => {
        this.busy.set(false);
        this.createVisible = false;
        this.showLink(u, `Invite for ${u.firstName}`);
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.rejected(this.createForm, err);
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
    if (!u || this.busy()) return;
    if (this.editForm.invalid) {
      this.editForm.markAllAsTouched();
      return;
    }
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
        this.rejected(this.editForm, err);
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

  openAccess(u: UserListItem): void {
    this.target.set(u);
    this.accessDetail.set(null);
    this.addRoleId = null;
    this.addRoleScope = 'AllBranches';
    this.addRoleBranch = null;
    this.accessVisible = true;
    this.loadAccess(u.userId);
  }

  private loadAccess(userId: number): void {
    this.api.get(userId).subscribe({
      next: (d) => {
        this.accessDetail.set(d);
        const primary = d.roles.find((r) => r.isPrimary);
        this.scopeType = primary?.scopeType ?? 'AllBranches';
        this.scopeBranch = primary?.branchId ?? null;
        this.driverPartnerId = d.linkedPartnerId ?? null;
      },
      error: (err) => { this.fail(err); this.accessVisible = false; },
    });
  }

  /** Roles the user does not already hold, and (mirroring the invite dialog) only ones the caller may hand out. */
  protected addableRoles(d: UserDetail): RoleOption[] {
    const held = new Set(d.roles.map((r) => r.roleId));
    return this.roleOptions().filter((r) => !held.has(r.roleId));
  }

  addRole(d: UserDetail): void {
    if (!this.addRoleId || this.busy()) return;
    this.busy.set(true);
    this.api.addRole(d.userId, { roleId: this.addRoleId, scopeType: this.addRoleScope, branchId: this.addRoleScope === 'OwnBranch' ? this.addRoleBranch : null }).subscribe({
      next: () => {
        this.busy.set(false);
        this.addRoleId = null;
        this.ok('Role added');
        this.loadAccess(d.userId);
        this.load();
      },
      error: (err) => { this.busy.set(false); this.fail(err); },
    });
  }

  removeRole(d: UserDetail, role: UserRoleItem): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api.removeRole(d.userId, role.roleId).subscribe({
      next: () => {
        this.busy.set(false);
        this.ok('Role removed');
        this.loadAccess(d.userId);
        this.load();
      },
      error: (err) => { this.busy.set(false); this.fail(err); },
    });
  }

  saveScope(d: UserDetail): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api.setScope(d.userId, { scopeType: this.scopeType, branchId: this.scopeType === 'OwnBranch' ? this.scopeBranch : null }).subscribe({
      next: () => {
        this.busy.set(false);
        this.ok('Scope saved — they are signed out so it takes effect.');
        this.loadAccess(d.userId);
      },
      error: (err) => { this.busy.set(false); this.fail(err); },
    });
  }

  saveDriverLink(d: UserDetail): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api.setDriverLink(d.userId, { partnerId: this.driverPartnerId }).subscribe({
      next: () => {
        this.busy.set(false);
        this.ok('Driver link saved');
        this.loadAccess(d.userId);
      },
      error: (err) => { this.busy.set(false); this.fail(err); },
    });
  }

  openEffective(u: UserListItem): void {
    this.target.set(u);
    this.effectivePermissions.set([]);
    this.effectiveVisible = true;
    this.api.effectivePermissions(u.userId).subscribe({ next: (p) => this.effectivePermissions.set(p), error: (err) => { this.fail(err); this.effectiveVisible = false; } });
  }

  async toggleActive(u: UserListItem): Promise<void> {
    const activate = !u.isActive;
    const go = await this.confirm.ask({
      title: activate ? 'Activate user' : 'Deactivate user',
      message: activate ? `Allow ${u.firstName} to sign in again?` : `${u.firstName} will be signed out and unable to sign in.`,
      confirmLabel: activate ? 'Activate' : 'Deactivate',
      icon: 'pi pi-exclamation-triangle',
    });
    if (!go) return;
    this.api.patch(u.userId, { isActive: activate }).subscribe({
      next: () => {
        this.ok(activate ? 'User activated' : 'User deactivated');
        this.load();
      },
      error: (err) => this.fail(err),
    });
  }

  async resetPassword(u: UserListItem): Promise<void> {
    const go = await this.confirm.ask({
      title: 'Issue password reset link',
      message: `${u.firstName} will be signed out everywhere. Continue?`,
      confirmLabel: 'Issue link',
      icon: 'pi pi-key',
    });
    if (!go) return;
    this.api.resetPassword(u.userId).subscribe({
      next: (d) => this.showLink(d, `Password reset for ${u.firstName}`),
      error: (err) => this.fail(err),
    });
  }

  async remove(u: UserListItem): Promise<void> {
    const go = await this.confirm.ask({
      title: 'Delete user',
      message: `Delete ${u.firstName} ${u.lastName ?? ''}? This cannot be undone.`,
      confirmLabel: 'Delete',
      danger: true,
    });
    if (!go) return;
    this.api.remove(u.userId).subscribe({
      next: () => {
        this.ok('User deleted');
        this.load();
      },
      error: (err) => this.fail(err),
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
    this.notify.success(detail);
  }

  private fail(err: unknown): void {
    this.notify.error(err);
  }

  /** The API refused the input: mark each field it named, and toast only what belongs to no field. */
  private rejected(form: AbstractControl, err: unknown): void {
    const problems = apiErrors(err);
    if (problems.length === 0 || applyServerErrors(form, problems, this.messages).length > 0) this.fail(err);
  }
}
