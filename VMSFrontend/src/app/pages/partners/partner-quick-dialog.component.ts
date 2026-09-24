import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectButtonModule } from 'primeng/selectbutton';
import { PartnersApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { PartnerPickerItem } from '../../core/partner.models';
import { FieldComponent } from '../../shared/field.component';
import { LookupPickerComponent } from '../../shared/lookup-picker.component';
import { AuthService } from '../../core/auth.service';
import { DuplicatePanelComponent } from './duplicate-panel.component';
import { DuplicateWatcher, watchDuplicates } from './duplicate-watcher';
import { buildPartnerForm, placeErrors, syncRequirements, toCreateRequest, wireRules } from './partner-form';
import { PARTY_TYPES, maskCnic, optionsOf, panelsFor, roleLabel } from './partner-logic';
import { CustomerPanelComponent, DriverPanelComponent, VendorPanelComponent } from './role-panels.component';

/**
 * A compact "new partner" for use from inside another screen (FSD §10, FR-BP-014): a vehicle's driver is not in the list, so
 * add one without leaving the vehicle. The role is chosen by the screen and cannot be changed here; only what the API needs
 * for that role is asked. It is the same form, rules and duplicate check as the full screen, and the partner can be completed
 * later from the Business partners list.
 */
@Component({
  selector: 'app-partner-quick-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule, DialogModule, ButtonModule, InputTextModule, SelectButtonModule, FieldComponent, LookupPickerComponent,
    DuplicatePanelComponent, DriverPanelComponent, VendorPanelComponent, CustomerPanelComponent,
  ],
  template: `
    <p-dialog [visible]="!!role()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '680px' }" [header]="'New ' + label().toLowerCase()" [closable]="!busy()">
      @if (role()) {
        @if (problems().length > 0) {
          <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div>
        }

        <app-duplicate-panel [matches]="watcher.matches()" [(acknowledged)]="watcher.acknowledged" />

        <form [formGroup]="form" class="form-grid" (ngSubmit)="save()" novalidate>
          <vms-field class="full" label="Party type" [control]="form.controls.partyType">
            <p-selectbutton formControlName="partyType" [options]="partyTypes" optionLabel="label" optionValue="value" [allowEmpty]="false" ariaLabel="Party type" />
          </vms-field>
          <vms-field class="full" label="Legal name" [control]="form.controls.legalName" for="qd-legal"><input pInputText id="qd-legal" formControlName="legalName" autocomplete="off" /></vms-field>

          @if (form.controls.partyType.value === 'Person') {
            <vms-field label="CNIC" [control]="form.controls.cnic" for="qd-cnic" hint="00000-0000000-0">
              <input pInputText id="qd-cnic" formControlName="cnic" inputmode="numeric" maxlength="15" autocomplete="off" (input)="mask($event)" />
            </vms-field>
          } @else {
            <vms-field label="NTN" [control]="form.controls.ntn" for="qd-ntn" hint="For example 1234567-8."><input pInputText id="qd-ntn" formControlName="ntn" autocomplete="off" /></vms-field>
          }
          <vms-field label="Primary mobile" [control]="form.controls.primaryMobile" for="qd-mobile" hint="0300-1234567"><input pInputText id="qd-mobile" formControlName="primaryMobile" inputmode="tel" autocomplete="off" /></vms-field>

          @if (needsEmail()) {
            <vms-field class="full" label="Email" [control]="form.controls.email" for="qd-email"><input pInputText id="qd-email" type="email" formControlName="email" autocomplete="off" /></vms-field>
          }

          <vms-field label="City" [control]="form.controls.cityId" for="qd-city"><vms-lookup-picker type="CITY" formControlName="cityId" inputId="qd-city" placeholder="Choose a city" /></vms-field>
          <vms-field label="Address" [control]="form.controls.addressLine" for="qd-address"><input pInputText id="qd-address" formControlName="addressLine" /></vms-field>

          @if (panel() === 'Driver') { <div class="full"><app-driver-panel [group]="form.controls.driver" [salary]="salary()" [creating]="true" /></div> }
          @if (panel() === 'Vendor') { <div class="full"><app-vendor-panel [group]="form.controls.vendor" [credit]="credit()" /></div> }
          @if (panel() === 'Customer') { <div class="full"><app-customer-panel [group]="form.controls.customer" [credit]="credit()" /></div> }
        </form>
      }
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Save partner" icon="pi pi-check" [loading]="busy()" [disabled]="!watcher.canSave()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; }`],
})
export class PartnerQuickDialogComponent {
  private readonly api = inject(PartnersApi);
  private readonly fb = inject(FormBuilder);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  /** The role the new partner will hold. The dialog is open while this is set. */
  readonly role = input<string | null>(null);
  /** A name to start with, for example what was typed in the picker's search box. */
  readonly seedName = input('');
  readonly created = output<PartnerPickerItem>();
  readonly closed = output<void>();

  readonly form = buildPartnerForm(this.fb);
  readonly watcher: DuplicateWatcher = watchDuplicates(this.api, this.form, this.destroyRef, { excludeId: () => null, baseline: () => null });
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);

  protected readonly partyTypes = optionsOf(PARTY_TYPES);

  constructor() {
    wireRules(this.form, this.destroyRef);
    effect(() => {
      const role = this.role();
      const name = this.seedName();
      if (!role) return;
      untracked(() => this.reset(role, name));
    });
  }

  protected label = () => roleLabel(this.role() ?? '');
  protected salary = () => this.auth.hasPermission('BP.FIELD.SALARY.VIEW');
  protected credit = () => this.auth.hasPermission('BP.FIELD.CREDIT.VIEW');
  protected panel = () => panelsFor(this.form.controls.roles.value)[0] ?? null;
  protected needsEmail = () => this.form.controls.roles.value.some((r) => r === 'Customer' || r === 'Bank');

  protected mask(event: Event): void {
    const control = this.form.controls.cnic;
    const masked = maskCnic((event.target as HTMLInputElement).value);
    if (masked !== control.value) control.setValue(masked);
  }

  private reset(role: string, name: string): void {
    this.form.reset({ partyType: 'Person', legalName: name, filerStatus: 'Unknown', roles: [role] });
    this.form.controls.driver.patchValue({ commissionBasis: 'None' });
    this.form.controls.vendor.patchValue({ supplyCategories: [] });
    this.form.controls.contacts.clear();
    this.form.controls.addresses.clear();
    this.form.controls.bankAccounts.clear();
    syncRequirements(this.form);
    this.watcher.clear();
    this.problems.set([]);
    this.busy.set(false);
  }

  save(): void {
    if (this.busy() || !this.watcher.canSave()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const allowed = { salary: this.salary(), credit: this.credit(), opening: false };
    this.busy.set(true);
    this.api.create(toCreateRequest(this.form, allowed, this.watcher.ackIds())).subscribe({
      next: (p) => {
        this.busy.set(false);
        this.notify.success(`${p.legalName} saved as ${p.bpCode}`);
        this.created.emit({ id: p.id, bpCode: p.bpCode, displayName: p.displayName || p.legalName, legalName: p.legalName, cityId: p.cityId ?? 0 });
      },
      error: (err) => {
        this.busy.set(false);
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        // A duplicate the panel had not shown yet: show it now, and say what stopped the save.
        this.watcher.refresh();
        this.problems.set(placeErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }
}
