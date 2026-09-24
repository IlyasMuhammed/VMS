import { Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { PartnersApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { ApiFieldError } from '../../core/message-format';
import { Partner, PartnerUsage } from '../../core/partner.models';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { buildCustomerGroup, buildDriverGroup, buildVendorGroup, syncDriverRules } from './partner-form';
import { ROLES, roleLabel } from './partner-logic';
import { CustomerPanelComponent, DriverPanelComponent, VendorPanelComponent } from './role-panels.component';

/** What the dialog is for: giving the partner a role, or taking one away. */
export type RoleAction = { kind: 'add' } | { kind: 'remove'; role: string };

/**
 * Adds or removes a role on a saved partner (FSD §11). Each change is recorded with who, when and why, so it is done here
 * and not by editing the role list on the form. A role with fields of its own asks for them (unless the partner held it
 * before and still has them); removing one is refused while records depend on it, and those records are listed (BR-BP-011).
 */
@Component({
  selector: 'app-partner-role-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, TextareaModule, FieldComponent, DriverPanelComponent, VendorPanelComponent, CustomerPanelComponent],
  template: `
    <p-dialog [visible]="!!action()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '640px' }" [header]="title()" [closable]="!busy()">
      @if (action(); as a) {
        @if (problems().length > 0) {
          <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div>
        }

        @if (a.kind === 'add') {
          <div class="stack">
            <vms-field label="Role" [control]="roleControl" for="rd-role">
              <p-select inputId="rd-role" [formControl]="roleControl" [options]="available()" optionLabel="label" optionValue="value" placeholder="Choose a role" [fluid]="true" appendTo="body" />
            </vms-field>

            @if (needsPanel() === 'Driver') { <app-driver-panel [group]="driver" [salary]="salary()" [creating]="true" /> }
            @if (needsPanel() === 'Vendor') { <app-vendor-panel [group]="vendor" [credit]="credit()" /> }
            @if (needsPanel() === 'Customer') { <app-customer-panel [group]="customer" [credit]="credit()" /> }

            <vms-field label="Reason" [control]="reason" for="rd-reason" hint="Optional. Kept in the history.">
              <textarea pTextarea id="rd-reason" [formControl]="reason" rows="2" [fluid]="true"></textarea>
            </vms-field>
          </div>
        } @else {
          <p>Remove <strong>{{ label(a.role) }}</strong> from <strong>{{ partner()?.legalName }}</strong>? What was recorded for the role is kept, and comes back if the role is added again.</p>
          @if (blockers().length > 0) {
            <div class="alert warning" role="alert">
              <strong>Not yet: {{ blockers().length }} {{ blockers().length === 1 ? 'record still uses' : 'records still use' }} this role.</strong>
              <ul>@for (b of blockers(); track b.recordCode) { <li><span class="mono">{{ b.recordCode }}</span> — {{ b.description }}</li> }</ul>
            </div>
          }
          <vms-field label="Reason" [control]="reason" for="rd-remove-reason" hint="Optional. Kept in the history.">
            <textarea pTextarea id="rd-remove-reason" [formControl]="reason" rows="2" [fluid]="true"></textarea>
          </vms-field>
        }
      }
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="action()?.kind === 'add' ? 'Add role' : 'Remove role'" [icon]="action()?.kind === 'add' ? 'pi pi-plus' : 'pi pi-minus'" [loading]="busy()"
                  [severity]="action()?.kind === 'add' ? 'primary' : 'danger'" [disabled]="action()?.kind === 'remove' && blockers().length > 0" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`.stack { display: flex; flex-direction: column; gap: 1rem; } ul { margin: .25rem 0; padding-left: 1.25rem; }`],
})
export class PartnerRoleDialogComponent {
  private readonly api = inject(PartnersApi);
  private readonly fb = inject(FormBuilder);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);

  readonly partner = input<Partner | null>(null);
  readonly action = input<RoleAction | null>(null);
  readonly salary = input(false);
  readonly credit = input(false);
  /** The partner as it is after the change. */
  readonly changed = output<Partner>();
  readonly closed = output<void>();

  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);
  readonly blockers = signal<PartnerUsage[]>([]);
  readonly available = signal<{ label: string; value: string }[]>([]);
  readonly needsPanel = signal<string | null>(null);

  readonly roleControl = this.fb.control<string | null>(null, Validators.required);
  private readonly roleForm = this.fb.group({ roleCode: this.roleControl });
  readonly reason = this.fb.nonNullable.control('', Validators.maxLength(500));
  readonly driver: FormGroup = buildDriverGroup(this.fb);
  readonly vendor: FormGroup = buildVendorGroup(this.fb);
  readonly customer: FormGroup = buildCustomerGroup(this.fb);

  protected readonly label = roleLabel;

  constructor() {
    effect(() => {
      const a = this.action();
      const p = this.partner();
      if (!a || !p) return;
      untracked(() => this.open(a, p));
    });

    this.roleControl.valueChanges.subscribe((role) => this.needsPanel.set(this.panelNeeded(role)));
    this.driver.controls['employmentType'].valueChanges.subscribe(() => syncDriverRules(this.driver));
    this.driver.controls['commissionBasis'].valueChanges.subscribe(() => syncDriverRules(this.driver));
  }

  protected title(): string {
    return this.action()?.kind === 'add' ? 'Add a role' : 'Remove a role';
  }

  private open(a: RoleAction, p: Partner): void {
    this.problems.set([]);
    this.blockers.set([]);
    this.reason.reset('');
    this.roleControl.reset(null);
    this.needsPanel.set(null);
    for (const g of [this.driver, this.vendor, this.customer]) g.reset();
    this.driver.patchValue({ commissionBasis: 'None', driverAppAccess: false });
    this.vendor.patchValue({ supplyCategories: [] });

    if (a.kind === 'add') {
      const held = new Set(p.roles);
      this.available.set(ROLES.filter((r) => !held.has(r.code)).map((r) => ({ label: r.label, value: r.code })));
    } else {
      this.api.usage(p.id, 'RemoveRole', a.role).subscribe({ next: (u) => this.blockers.set(u), error: () => undefined });
    }
  }

  /** A role with a panel asks for it only if the partner has none saved from an earlier time as that role. */
  private panelNeeded(role: string | null): string | null {
    const p = this.partner();
    if (!p || !role) return null;
    if (role === 'Driver' && !p.driver) return 'Driver';
    if (role === 'Vendor' && !p.vendor) return 'Vendor';
    if (role === 'Customer' && !p.customer) return 'Customer';
    return null;
  }

  save(): void {
    const a = this.action();
    const p = this.partner();
    if (!a || !p || this.busy()) return;
    this.problems.set([]);

    if (a.kind === 'remove') {
      this.busy.set(true);
      this.api.removeRole(p.id, a.role, this.reason.value.trim() || null).subscribe({
        next: (updated) => this.done(updated, `${roleLabel(a.role)} removed`),
        error: (err) => this.refused(err),
      });
      return;
    }

    const role = this.roleControl.value;
    const panel = this.needsPanel();
    const group = panel === 'Driver' ? this.driver : panel === 'Vendor' ? this.vendor : panel === 'Customer' ? this.customer : null;
    this.roleControl.markAsTouched();
    group?.markAllAsTouched();
    if (!role || (group && group.invalid)) return;

    const body: Parameters<PartnersApi['addRole']>[1] = { roleCode: role, reason: this.reason.value.trim() || null };
    if (panel === 'Driver') {
      const v = this.driver.getRawValue();
      body.driver = {
        licenceNo: String(v.licenceNo).trim().toUpperCase(), licenceType: v.licenceType, licenceIssueDate: v.licenceIssueDate, licenceExpiryDate: v.licenceExpiryDate,
        employmentType: v.employmentType, dateOfJoining: v.dateOfJoining, bloodGroup: v.bloodGroup?.trim() || null, emergencyContactName: v.emergencyContactName?.trim() || null,
        emergencyContactPhone: v.emergencyContactPhone?.trim() || null, guarantor: v.guarantor?.trim() || null, driverAppAccess: !!v.driverAppAccess,
        ...(this.salary() ? { monthlyRate: v.monthlyRate, commissionBasis: v.commissionBasis, commissionValue: v.commissionBasis === 'None' ? null : v.commissionValue } : {}),
      };
    } else if (panel === 'Vendor') {
      const v = this.vendor.getRawValue();
      body.vendor = { supplyCategories: v.supplyCategories, ...(this.credit() ? { paymentTermDays: v.paymentTermDays, creditLimit: v.creditLimit } : {}) };
    } else if (panel === 'Customer') {
      const v = this.customer.getRawValue();
      body.customer = {
        customerType: v.customerType, billingCycle: v.billingCycle, rateBasis: v.rateBasis || null, defaultRate: v.defaultRate,
        ...(this.credit() ? { creditLimit: v.creditLimit, creditDays: v.creditDays } : {}),
      };
    }

    this.busy.set(true);
    this.api.addRole(p.id, body).subscribe({
      next: (updated) => this.done(updated, `${roleLabel(role)} added`),
      error: (err) => this.refused(err),
    });
  }

  private done(updated: Partner, message: string): void {
    this.busy.set(false);
    this.notify.success(message);
    this.changed.emit(updated);
  }

  private refused(err: unknown): void {
    this.busy.set(false);
    const found = apiErrors(err);
    if (found.length === 0) {
      this.notify.error(err);
      return;
    }
    // A field error lands on its control (a bad licence number beside the licence field); the rest are listed at the top.
    const groups: Record<string, FormGroup> = { driver: this.driver, vendor: this.vendor, customer: this.customer };
    const unplaced: ApiFieldError[] = [];
    for (const e of found) {
      const field = e.field ?? '';
      const panel = Object.keys(groups).find((name) => field.startsWith(`${name}.`));
      const target = panel ? groups[panel] : field === 'roleCode' ? this.roleForm : null;
      const path = panel ? field.slice(panel.length + 1) : field;
      if (!target || applyServerErrors(target, [{ ...e, field: path }], this.messages).length > 0) unplaced.push(e);
    }
    this.problems.set(unplaced.map((e) => this.messages.describe(e)));
  }
}