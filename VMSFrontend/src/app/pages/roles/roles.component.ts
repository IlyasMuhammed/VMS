import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { RolesApi } from '../../core/api.services';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/api-error';
import { RoleListItem } from '../../core/models';

@Component({
  selector: 'app-roles',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, TableModule, ButtonModule, DialogModule, InputTextModule, TagModule, CheckboxModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Roles</h1><div class="sub">A role is a named set of permissions you can give to users.</div></div>
        @if (canManage()) { <p-button label="New role" icon="pi pi-plus" (onClick)="openCreate()" /> }
      </div>

      <div class="card">
        <p-table [value]="rows()" [loading]="loading()">
          <ng-template pTemplate="header">
            <tr><th>Name</th><th>Code</th><th>Type</th><th>Permissions</th><th>Active users</th><th>Status</th></tr>
          </ng-template>
          <ng-template pTemplate="body" let-r>
            <tr>
              <td><a [routerLink]="['/roles', r.roleId]">{{ r.name }}</a><div class="muted">{{ r.description }}</div></td>
              <td>{{ r.roleCode }}</td>
              <td><p-tag [value]="r.isGlobal ? 'Platform' : 'Custom'" [severity]="r.isGlobal ? 'info' : 'secondary'" /></td>
              <td>{{ r.permissionCount }}</td>
              <td>{{ r.activeUserCount }}</td>
              <td><p-tag [value]="r.isActive ? 'Active' : 'Inactive'" [severity]="r.isActive ? 'success' : 'secondary'" /></td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage"><tr><td colspan="6" class="muted">No roles.</td></tr></ng-template>
        </p-table>
      </div>
    </div>

    <p-dialog header="New role" [(visible)]="visible" [modal]="true" [style]="{ width: '480px' }">
      <form [formGroup]="form" class="form-grid">
        <div class="field full"><label>Name *</label><input pInputText formControlName="name" /></div>
        <div class="field full">
          <label>Code *</label>
          <input pInputText formControlName="roleCode" style="text-transform: uppercase" />
          <span class="hint">Capital letters, digits and underscores, e.g. DISPATCHER.</span>
        </div>
        <div class="field full"><label>Description</label><input pInputText formControlName="description" /></div>
        @if (auth.isSuperAdmin()) {
          <div class="field full"><label><p-checkbox formControlName="isGlobal" [binary]="true" /> Platform role (available to every tenant)</label></div>
        }
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="visible = false" />
        <p-button label="Create" [loading]="busy()" [disabled]="form.invalid" (onClick)="create()" />
      </ng-template>
    </p-dialog>
  `,
})
export class RolesComponent implements OnInit {
  readonly auth = inject(AuthService);
  private readonly api = inject(RolesApi);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly toast = inject(MessageService);

  readonly canManage = computed(() => this.auth.hasPermission('ROLE_MANAGE'));
  readonly rows = signal<RoleListItem[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  visible = false;

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    roleCode: ['', [Validators.required, Validators.pattern(/^[A-Za-z][A-Za-z0-9_]{1,49}$/)]],
    description: [''],
    isGlobal: [false],
  });

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.list().subscribe({
      next: (r) => {
        this.rows.set(r);
        this.loading.set(false);
      },
      error: (err) => {
        this.toast.add({ severity: 'error', summary: 'Error', detail: errorMessage(err) });
        this.loading.set(false);
      },
    });
  }

  openCreate(): void {
    this.form.reset({ name: '', roleCode: '', description: '', isGlobal: false });
    this.visible = true;
  }

  create(): void {
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.api.create(this.form.getRawValue()).subscribe({
      next: (role) => {
        this.busy.set(false);
        this.visible = false;
        this.router.navigate(['/roles', role.roleId]); // straight to the permission picker
      },
      error: (err) => {
        this.busy.set(false);
        this.toast.add({ severity: 'error', summary: 'Error', detail: errorMessage(err) });
      },
    });
  }
}
