import { Input, Component, OnChanges, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { RolesApi } from '../../core/api.services';
import { AuthService } from '../../core/auth.service';
import { NotifyService } from '../../core/notify.service';
import { PermissionItem, RoleDetail, RoleUser } from '../../core/models';

/** One module's permissions, split into its Interface / Operation / Field sub-groups (§23B.6's permission tree). */
interface LevelGroup {
  level: string;
  permissions: PermissionItem[];
}
interface ModuleGroup {
  module: string;
  levels: LevelGroup[];
  /** Every permission in the module, for the module-wide select-all. */
  all: PermissionItem[];
}

const LEVEL_ORDER = ['Interface', 'Operation', 'Field'];

@Component({
  selector: 'app-role-detail',
  standalone: true,
  imports: [FormsModule, ReactiveFormsModule, RouterLink, ButtonModule, CheckboxModule, DialogModule, InputTextModule, TableModule, TagModule],
  template: `
    <div class="page">
      <a routerLink="/roles" class="muted"><i class="pi pi-arrow-left"></i> All roles</a>

      @if (role(); as r) {
        <div class="page-header" style="margin-top: .5rem">
          <div>
            <h1>{{ r.name }} <p-tag [value]="r.isGlobal ? 'Platform' : 'Custom'" [severity]="r.isGlobal ? 'info' : 'secondary'" /></h1>
            <div class="sub">{{ r.roleCode }} · {{ r.activeUserCount }} active user(s)</div>
          </div>
          @if (r.isActive) {
            <div class="actions-bar">
              @if (auth.hasPermission('ROLE_MANAGE')) { <p-button label="Clone this role" icon="pi pi-copy" severity="secondary" [outlined]="true" (onClick)="clone()" /> }
              @if (canEdit()) { <p-button label="Deactivate role" icon="pi pi-ban" severity="danger" [outlined]="true" (onClick)="deactivate()" /> }
            </div>
          }
        </div>

        @if (!canEdit()) {
          <div class="alert info" style="margin-bottom: 1rem">
            @if (r.isGlobal && !auth.isSuperAdmin()) { Platform roles are managed by the platform administrator — you can view but not change this role. }
            @else { You can view this role but do not have permission to change it. }
          </div>
        }

        <div class="card" style="margin-bottom: 1rem">
          <h3>Details</h3>
          <form [formGroup]="details" class="form-grid" style="margin-top: 1rem">
            <div class="field"><label>Name</label><input pInputText formControlName="name" /></div>
            <div class="field"><label>Description</label><input pInputText formControlName="description" /></div>
          </form>
          @if (canEdit()) {
            <div style="margin-top: 1rem"><p-button label="Save details" [loading]="busy()" [disabled]="details.invalid || details.pristine" (onClick)="saveDetails()" /></div>
          }
        </div>

        <div class="card" style="margin-bottom: 1rem">
          <div class="perm-header">
            <h3>Permissions</h3>
            <span class="p-input-icon-left search">
              <i class="pi pi-search"></i>
              <input pInputText type="search" placeholder="Search permissions…" [ngModel]="search()" (ngModelChange)="search.set($event)" [ngModelOptions]="{ standalone: true }" />
            </span>
          </div>

          @for (g of moduleGroups(); track g.module) {
            <div class="group">
              <div class="group-head">
                <h4>{{ g.module }}</h4>
                <label class="select-all"><p-checkbox [binary]="true" [ngModel]="allChecked(g.all)" (ngModelChange)="toggleAll(g.all, $event)" [ngModelOptions]="{ standalone: true }" [disabled]="!canEdit()" /> Select all</label>
              </div>
              @for (lg of g.levels; track lg.level) {
                <div class="level">
                  <div class="level-head">
                    <span class="level-tag">{{ lg.level }}</span>
                    <label class="select-all small"><p-checkbox [binary]="true" [ngModel]="allChecked(lg.permissions)" (ngModelChange)="toggleAll(lg.permissions, $event)" [ngModelOptions]="{ standalone: true }" [disabled]="!canEdit()" /> All</label>
                  </div>
                  @for (p of lg.permissions; track p.permissionId) {
                    <label class="perm">
                      <p-checkbox [binary]="true" [ngModel]="allowed().has(p.permissionId)" (ngModelChange)="toggle(p.permissionId, $event)" [disabled]="!canEdit() || !canGrant(p.code, p.permissionId)" [ngModelOptions]="{ standalone: true }" />
                      <span><strong>{{ p.name }}</strong><small class="muted"> — {{ p.description }}</small></span>
                    </label>
                  }
                </div>
              }
            </div>
          }
          @if (moduleGroups().length === 0) { <p class="muted">No permission matches "{{ search() }}".</p> }

          @if (canEdit()) {
            <div style="margin-top: 1rem"><p-button label="Save permissions" [loading]="busy()" [disabled]="!permissionsDirty()" (onClick)="savePermissions()" /></div>
          }
        </div>

        <div class="card">
          <h3>Users with this role</h3>
          <p-table [value]="users()" styleClass="mt-3">
            <ng-template pTemplate="header"><tr><th>Name</th><th>Email</th><th>Department</th><th>Status</th></tr></ng-template>
            <ng-template pTemplate="body" let-u>
              <tr><td>{{ u.firstName }} {{ u.lastName }}</td><td>{{ u.email }}</td><td>{{ u.department }}</td><td><p-tag [value]="u.isActive ? 'Active' : 'Inactive'" [severity]="u.isActive ? 'success' : 'secondary'" /></td></tr>
            </ng-template>
            <ng-template pTemplate="emptymessage"><tr><td colspan="4" class="muted">Nobody holds this role yet.</td></tr></ng-template>
          </p-table>
        </div>
      }
    </div>

    <p-dialog header="Clone this role" [(visible)]="cloneVisible" [modal]="true" [style]="{ width: '440px' }">
      <form [formGroup]="cloneForm" class="form-grid">
        <div class="field full"><label>Name *</label><input pInputText formControlName="name" /></div>
        <div class="field full">
          <label>Code *</label>
          <input pInputText formControlName="roleCode" style="text-transform: uppercase" />
          <span class="hint">Capital letters, digits and underscores, e.g. DISPATCHER.</span>
        </div>
        <div class="field full muted">Starts with {{ role()?.name }}'s current permissions — you can change them after.</div>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="cloneVisible = false" />
        <p-button label="Clone" icon="pi pi-copy" [loading]="busy()" [disabled]="cloneForm.invalid" (onClick)="confirmClone()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .actions-bar { display: flex; gap: .5rem; }
      .perm-header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; flex-wrap: wrap; }
      .search { position: relative; } .search input { padding-left: 2rem; }
      .group { margin-top: 1.25rem; border-top: 1px solid var(--vms-border); padding-top: .75rem; }
      .group:first-of-type { border-top: none; padding-top: 0; }
      .group-head { display: flex; align-items: center; justify-content: space-between; }
      .group-head h4 { margin: 0; color: var(--vms-muted); text-transform: uppercase; font-size: .78rem; letter-spacing: .05em; }
      .level { margin-top: .5rem; padding-left: .75rem; border-left: 2px solid var(--vms-border); }
      .level-head { display: flex; align-items: center; gap: .6rem; margin-bottom: .15rem; }
      .level-tag { font-size: .72rem; font-weight: 600; color: var(--vms-brand-text); background: var(--vms-brand-tint); border-radius: var(--vms-radius-sm); padding: .05rem .5rem; }
      .select-all { display: flex; align-items: center; gap: .4rem; font-size: .8rem; cursor: pointer; }
      .select-all.small { font-size: .74rem; color: var(--vms-muted); }
      .perm { display: flex; gap: .6rem; align-items: center; padding: .3rem 0; }
    `,
  ],
})
export class RoleDetailComponent implements OnChanges {
  readonly auth = inject(AuthService);
  private readonly api = inject(RolesApi);
  private readonly fb = inject(FormBuilder);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmationService);
  private readonly router = inject(Router);

  /** Route param `:id` (withComponentInputBinding). */
  @Input() id = '';

  readonly role = signal<RoleDetail | null>(null);
  readonly users = signal<RoleUser[]>([]);
  readonly allowed = signal<Set<number>>(new Set());
  readonly busy = signal(false);
  readonly search = signal('');
  private original = new Set<number>();

  readonly details = this.fb.nonNullable.group({ name: ['', Validators.required], description: [''] });
  cloneVisible = false;
  readonly cloneForm = this.fb.nonNullable.group({
    name: ['', Validators.required],
    roleCode: ['', [Validators.required, Validators.pattern(/^[A-Za-z][A-Za-z0-9_]{1,49}$/)]],
  });

  readonly canEdit = computed(() => {
    const r = this.role();
    return !!r && this.auth.hasPermission('ROLE_MANAGE') && (!r.isGlobal || this.auth.isSuperAdmin());
  });

  readonly permissionsDirty = computed(() => {
    const now = this.allowed();
    return now.size !== this.original.size || [...now].some((id) => !this.original.has(id));
  });

  /** The permission tree — module, then level, filtered by the search box. */
  readonly moduleGroups = computed<ModuleGroup[]>(() => {
    const r = this.role();
    if (!r) return [];
    const term = this.search().trim().toLowerCase();
    const matches = (p: PermissionItem): boolean =>
      !term || p.name.toLowerCase().includes(term) || p.code.toLowerCase().includes(term) || (p.description ?? '').toLowerCase().includes(term);

    return r.permissionGroups
      .map((g) => {
        const all = g.permissions.filter(matches);
        const levels = LEVEL_ORDER
          .map((level) => ({ level, permissions: all.filter((p) => p.level === level) }))
          .filter((lg) => lg.permissions.length > 0);
        // A level this codebase never used yet still shows, rather than silently dropping permissions.
        const known = new Set(LEVEL_ORDER);
        const other = all.filter((p) => !known.has(p.level));
        if (other.length > 0) levels.push({ level: other[0].level || 'Other', permissions: other });
        return { module: g.module, levels, all };
      })
      .filter((g) => g.all.length > 0);
  });

  ngOnChanges(): void {
    this.load();
  }

  /** You can only hand out what you hold — but a permission the role already has stays, whoever edits. */
  canGrant(code: string, permissionId: number): boolean {
    return this.auth.hasPermission(code) || this.original.has(permissionId);
  }

  toggle(permissionId: number, on: boolean): void {
    const next = new Set(this.allowed());
    if (on) next.add(permissionId);
    else next.delete(permissionId);
    this.allowed.set(next);
  }

  protected allChecked(permissions: PermissionItem[]): boolean {
    return permissions.length > 0 && permissions.every((p) => this.allowed().has(p.permissionId));
  }

  protected toggleAll(permissions: PermissionItem[], on: boolean): void {
    const next = new Set(this.allowed());
    for (const p of permissions) {
      if (!this.canGrant(p.code, p.permissionId)) continue; // never grant what you cannot hand out, even via select-all
      if (on) next.add(p.permissionId);
      else next.delete(p.permissionId);
    }
    this.allowed.set(next);
  }

  private load(): void {
    const id = Number(this.id);
    if (!id) return;
    this.api.get(id).subscribe({
      next: (r) => {
        this.role.set(r);
        this.details.reset({ name: r.name, description: r.description ?? '' });
        this.original = new Set(r.permissionGroups.flatMap((g) => g.permissions.filter((p) => p.isAllowed).map((p) => p.permissionId)));
        this.allowed.set(new Set(this.original));
      },
      error: (err) => this.fail(err),
    });
    this.api.users(id).subscribe({ next: (u) => this.users.set(u), error: () => this.users.set([]) });
  }

  saveDetails(): void {
    const r = this.role();
    if (!r || this.details.invalid) return;
    this.busy.set(true);
    const v = this.details.getRawValue();
    this.api.update(r.roleId, { name: v.name, description: v.description, isActive: r.isActive }).subscribe({
      next: () => {
        this.busy.set(false);
        this.ok('Role updated');
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.fail(err);
      },
    });
  }

  savePermissions(): void {
    const r = this.role();
    if (!r) return;
    this.busy.set(true);
    this.api.savePermissions(r.roleId, [...this.allowed()]).subscribe({
      next: () => {
        this.busy.set(false);
        this.ok('Permissions saved — affected users are signed out so the change takes effect.');
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.fail(err);
      },
    });
  }

  /** FR-SEC-001: cloning is the intended way to start a new role. */
  clone(): void {
    const r = this.role();
    if (!r) return;
    this.cloneForm.reset({ name: `${r.name} (copy)`, roleCode: '' });
    this.cloneVisible = true;
  }

  /** Creates the new role, then copies this role's own current permissions onto it. */
  confirmClone(): void {
    const r = this.role();
    if (!r || this.cloneForm.invalid || this.busy()) return;
    const v = this.cloneForm.getRawValue();

    this.busy.set(true);
    this.api.create({ name: v.name, roleCode: v.roleCode.toUpperCase(), description: r.description, isGlobal: false }).subscribe({
      next: (created) => {
        const wanted = r.permissionGroups.flatMap((g) => g.permissions.filter((p) => p.isAllowed && this.canGrant(p.code, p.permissionId)).map((p) => p.permissionId));
        this.api.savePermissions(created.roleId, wanted).subscribe({
          next: () => {
            this.busy.set(false);
            this.cloneVisible = false;
            this.ok(`"${created.name}" created with ${r.name}'s permissions.`);
            this.router.navigate(['/roles', created.roleId]);
          },
          error: (err) => { this.busy.set(false); this.fail(err); },
        });
      },
      error: (err) => { this.busy.set(false); this.fail(err); },
    });
  }

  deactivate(): void {
    const r = this.role();
    if (!r) return;
    this.confirm.confirm({
      header: 'Deactivate role',
      message: `Deactivate "${r.name}"? It can no longer be assigned.`,
      icon: 'pi pi-exclamation-triangle',
      accept: () =>
        this.api.deactivate(r.roleId).subscribe({
          next: () => {
            this.ok('Role deactivated');
            this.load();
          },
          error: (err) => this.fail(err),
        }),
    });
  }

  private ok(detail: string): void {
    this.notify.success(detail);
  }

  private fail(err: unknown): void {
    this.notify.error(err);
  }
}
