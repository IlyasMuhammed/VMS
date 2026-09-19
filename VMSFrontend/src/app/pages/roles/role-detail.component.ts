import { Component, Input, OnChanges, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { RolesApi } from '../../core/api.services';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/api-error';
import { RoleDetail, RoleUser } from '../../core/models';

@Component({
  selector: 'app-role-detail',
  standalone: true,
  imports: [FormsModule, ReactiveFormsModule, RouterLink, ButtonModule, CheckboxModule, InputTextModule, TableModule, TagModule],
  template: `
    <div class="page">
      <a routerLink="/roles" class="muted"><i class="pi pi-arrow-left"></i> All roles</a>

      @if (role(); as r) {
        <div class="page-header" style="margin-top: .5rem">
          <div>
            <h1>{{ r.name }} <p-tag [value]="r.isGlobal ? 'Platform' : 'Custom'" [severity]="r.isGlobal ? 'info' : 'secondary'" /></h1>
            <div class="sub">{{ r.roleCode }} · {{ r.activeUserCount }} active user(s)</div>
          </div>
          @if (canEdit() && r.isActive) { <p-button label="Deactivate role" icon="pi pi-ban" severity="danger" [outlined]="true" (onClick)="deactivate()" /> }
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
          <h3>Permissions</h3>
          @for (g of r.permissionGroups; track g.module) {
            <div class="group">
              <h4>{{ g.module }}</h4>
              @for (p of g.permissions; track p.permissionId) {
                <label class="perm">
                  <p-checkbox [binary]="true" [ngModel]="allowed().has(p.permissionId)" (ngModelChange)="toggle(p.permissionId, $event)" [disabled]="!canEdit() || !canGrant(p.code, p.permissionId)" [ngModelOptions]="{ standalone: true }" />
                  <span><strong>{{ p.name }}</strong><small class="muted"> — {{ p.description }}</small></span>
                </label>
              }
            </div>
          }
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
  `,
  styles: [
    `
      .group { margin-top: 1rem; }
      .group h4 { margin: 0 0 .5rem; color: var(--vms-muted); text-transform: uppercase; font-size: .75rem; letter-spacing: .05em; }
      .perm { display: flex; gap: .6rem; align-items: center; padding: .35rem 0; }
    `,
  ],
})
export class RoleDetailComponent implements OnChanges {
  readonly auth = inject(AuthService);
  private readonly api = inject(RolesApi);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  /** Route param `:id` (withComponentInputBinding). */
  @Input() id = '';

  readonly role = signal<RoleDetail | null>(null);
  readonly users = signal<RoleUser[]>([]);
  readonly allowed = signal<Set<number>>(new Set());
  readonly busy = signal(false);
  private original = new Set<number>();

  readonly details = this.fb.nonNullable.group({ name: ['', Validators.required], description: [''] });

  readonly canEdit = computed(() => {
    const r = this.role();
    return !!r && this.auth.hasPermission('ROLE_MANAGE') && (!r.isGlobal || this.auth.isSuperAdmin());
  });

  readonly permissionsDirty = computed(() => {
    const now = this.allowed();
    return now.size !== this.original.size || [...now].some((id) => !this.original.has(id));
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
    this.toast.add({ severity: 'success', summary: 'Done', detail });
  }

  private fail(err: unknown): void {
    this.toast.add({ severity: 'error', summary: 'Error', detail: errorMessage(err) });
  }
}
