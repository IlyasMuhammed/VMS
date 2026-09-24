import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { Observable } from 'rxjs';
import { VehiclesApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { ApiFieldError } from '../../core/message-format';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { AttachedItem, Vehicle, VehicleListItem } from '../../core/vehicle.models';
import { ConfirmService } from '../../shared/confirm.service';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { LookupPickerComponent } from '../../shared/lookup-picker.component';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { applyServerErrors } from '../../shared/server-errors';
import { CategoryFieldsComponent, buildCategoryDetails, toCategoryDetails } from './category-fields.component';
import { CATEGORIES, CATEGORY_RULES, DISPOSAL_KINDS, DRIVER_ALREADY_ASSIGNED, ITEM_CONDITIONS, Category, disposalKindsFor, optionsOf, statusMoves, words } from './vehicle-logic';

/**
 * A little of what every one of these dialogs does: send the request, put the API's field errors beside their fields, list the
 * rest at the top, and say what happened. The dialogs themselves only differ in their fields.
 */
export abstract class ActionDialog {
  protected readonly notify = inject(NotifyService);
  protected readonly messages = inject(MessagesService);
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);

  /** Sends the request; on success says `done` and calls `finished`. `form` receives the field errors. */
  protected run<T>(request: Observable<T>, form: AbstractControl, done: string, finished: (result: T) => void, onError?: (error: ApiFieldError) => boolean): void {
    this.busy.set(true);
    this.problems.set([]);
    request.subscribe({
      next: (result) => {
        this.busy.set(false);
        this.notify.success(done);
        finished(result);
      },
      error: (err) => {
        this.busy.set(false);
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        if (onError && found.length === 1 && onError(found[0])) return;
        this.problems.set(applyServerErrors(form, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }

  protected invalid(form: AbstractControl): boolean {
    if (!form.invalid) return false;
    form.markAllAsTouched();
    return true;
  }
}


// ── Status ─────────────────────────────────────────────────────────────────────────

@Component({
  selector: 'app-vehicle-status-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, TextareaModule, FieldComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="!!vehicle()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '480px' }" header="Change status" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="New status" [control]="form.controls.status" for="vs-status"><p-select inputId="vs-status" formControlName="status" [options]="options()" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
        <vms-field label="Reason" [control]="form.controls.reason" for="vs-reason" hint="Optional."><textarea pTextarea id="vs-reason" formControlName="reason" rows="2" [fluid]="true"></textarea></vms-field>
        <vms-field label="Effective from" [control]="form.controls.effectiveDate" for="vs-date" hint="Today unless you enter an earlier date."><vms-date-picker inputId="vs-date" formControlName="effectiveDate" [notFuture]="true" /></vms-field>
      </form>
      <ng-template pTemplate="footer"><p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" /><p-button label="Change status" icon="pi pi-check" [loading]="busy()" (onClick)="save()" /></ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class VehicleStatusDialogComponent extends ActionDialog {
  private readonly api = inject(VehiclesApi);
  private readonly fb = inject(FormBuilder);
  readonly vehicle = input<Vehicle | null>(null);
  readonly closed = output<void>();
  readonly changed = output<Vehicle>();
  readonly form = this.fb.nonNullable.group({ status: ['', Validators.required], reason: [''], effectiveDate: this.fb.control<string | null>(null) });
  readonly options = computed(() => optionsOf(statusMoves(this.vehicle()?.status ?? '')));

  constructor() {
    super();
    effect(() => {
      const v = this.vehicle();
      if (v) untracked(() => { this.form.reset({ status: statusMoves(v.status)[0] ?? '' }); this.problems.set([]); });
    });
  }

  save(): void {
    const v = this.vehicle();
    if (!v || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.changeStatus(v.id, { status: f.status, reason: f.reason.trim() || null, effectiveDate: f.effectiveDate }), this.form, `Status is now ${words(f.status)}`, (r) => this.changed.emit(r));
  }
}

// ── Category ───────────────────────────────────────────────────────────────────────

@Component({
  selector: 'app-change-category-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, TextareaModule, InputTextModule, InputNumberModule, FieldComponent, DatePickerComponent, CategoryFieldsComponent],
  template: `
    <p-dialog [visible]="!!vehicle()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '640px' }" header="Change ownership category" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="form-grid" novalidate>
        <vms-field class="full" label="New category" [control]="form.controls.category" for="cc-category" hint="The old arrangement ends the day before; what was recorded under it stays as it was.">
          <p-select inputId="cc-category" formControlName="category" [options]="categories()" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
        </vms-field>
        <vms-field label="Effective from" [control]="form.controls.effectiveDate" for="cc-date" hint="Today unless you enter an earlier date."><vms-date-picker inputId="cc-date" formControlName="effectiveDate" [notFuture]="true" /></vms-field>
        <vms-field label="Reason" [control]="form.controls.reason" for="cc-reason"><input pInputText id="cc-reason" formControlName="reason" /></vms-field>

        <app-category-fields class="full" [details]="details" [category]="category()" idPrefix="cc" [finance]="finance()" />
      </form>
      <ng-template pTemplate="footer"><p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" /><p-button label="Change category" icon="pi pi-check" [loading]="busy()" (onClick)="save()" /></ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class ChangeCategoryDialogComponent extends ActionDialog {
  private readonly api = inject(VehiclesApi);
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  readonly vehicle = input<Vehicle | null>(null);
  readonly closed = output<void>();
  readonly changed = output<Vehicle>();

  readonly details: FormGroup = buildCategoryDetails(this.fb);
  readonly form = this.fb.group({
    category: this.fb.nonNullable.control('', Validators.required),
    effectiveDate: this.fb.control<string | null>(null),
    reason: [''],
    details: this.details,
  });

  readonly category = signal<string>('');
  readonly rule = computed(() => CATEGORY_RULES[this.category() as Category] ?? CATEGORY_RULES.SelfOwned);
  readonly finance = computed(() => this.auth.hasPermission('VEH.FIELD.FINANCE.VIEW'));
  readonly categories = computed(() => optionsOf(CATEGORIES));


  constructor() {
    super();
    effect(() => {
      const v = this.vehicle();
      if (!v) return;
      untracked(() => {
        this.form.reset({ category: '' });
        this.details.reset({ agreementReference: '' });
        this.category.set('');
        this.problems.set([]);
      });
    });
    this.form.controls.category.valueChanges.subscribe((c) => this.category.set(c));
  }

  save(): void {
    const v = this.vehicle();
    if (!v || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(
      this.api.changeCategory(v.id, { category: f.category, effectiveDate: f.effectiveDate, reason: (f.reason ?? '').trim() || null, details: toCategoryDetails(this.details) }),
      this.form,
      `Category is now ${words(f.category)}`,
      (r) => this.changed.emit(r),
    );
  }
}

// ── Disposal ───────────────────────────────────────────────────────────────────────

@Component({
  selector: 'app-dispose-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, InputTextModule, InputNumberModule, TextareaModule, FieldComponent, DatePickerComponent, PartnerPickerComponent],
  template: `
    <p-dialog [visible]="!!vehicle()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '540px' }" header="Retire, sell or transfer" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <div class="alert warning">A sold or transferred vehicle leaves the fleet for good: its arrangement and driver end, and it can no longer be edited.</div>
        <vms-field label="What is happening to it" [control]="form.controls.kind" for="dp-kind"><p-select inputId="dp-kind" formControlName="kind" [options]="kinds()" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
        <vms-field label="Date" [control]="form.controls.date" for="dp-date" hint="Today unless you enter an earlier date."><vms-date-picker inputId="dp-date" formControlName="date" [notFuture]="true" /></vms-field>
        @if (kind() !== 'Retire') {
          <vms-field [label]="kind() === 'Sell' ? 'Buyer' : 'Moved to'" [control]="form.controls.counterpartyId" for="dp-party"><vms-partner-picker formControlName="counterpartyId" inputId="dp-party" [allowCreate]="false" /></vms-field>
        }
        @if (kind() === 'Sell') {
          <vms-field label="Sale amount (PKR)" [control]="form.controls.amount" for="dp-amount"><p-inputnumber inputId="dp-amount" formControlName="amount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [min]="0" [fluid]="true" /></vms-field>
        }
        <vms-field label="Reason" [control]="form.controls.reason" for="dp-reason"><textarea pTextarea id="dp-reason" formControlName="reason" rows="2" [fluid]="true"></textarea></vms-field>
        <vms-field label="Reference" [control]="form.controls.reference" for="dp-ref" hint="An invoice or transfer number. Optional."><input pInputText id="dp-ref" formControlName="reference" /></vms-field>
      </form>
      <ng-template pTemplate="footer"><p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" /><p-button label="Confirm" icon="pi pi-check" [loading]="busy()" (onClick)="save()" /></ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class DisposeDialogComponent extends ActionDialog {
  private readonly api = inject(VehiclesApi);
  private readonly fb = inject(FormBuilder);
  readonly vehicle = input<Vehicle | null>(null);
  readonly closed = output<void>();
  readonly changed = output<Vehicle>();
  readonly form = this.fb.group({
    kind: this.fb.nonNullable.control('', Validators.required), date: this.fb.control<string | null>(null), reason: this.fb.nonNullable.control('', Validators.required),
    counterpartyId: this.fb.control<number | null>(null), amount: this.fb.control<number | null>(null), reference: [''],
  });
  readonly kind = signal('');
  readonly kinds = computed(() => optionsOf(disposalKindsFor(this.vehicle()?.status ?? '').filter((k) => (DISPOSAL_KINDS as readonly string[]).includes(k))).map((o) => ({ ...o, label: o.value === 'Retire' ? 'Retire (still owned)' : o.value === 'Sell' ? 'Sell' : 'Transfer to another party' })));

  constructor() {
    super();
    effect(() => {
      const v = this.vehicle();
      if (v) untracked(() => { this.form.reset({ kind: disposalKindsFor(v.status)[0] ?? '' }); this.kind.set(disposalKindsFor(v.status)[0] ?? ''); this.problems.set([]); });
    });
    this.form.controls.kind.valueChanges.subscribe((k) => this.kind.set(k));
  }

  save(): void {
    const v = this.vehicle();
    if (!v || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.dispose(v.id, { kind: f.kind, date: f.date, reason: f.reason.trim(), counterpartyId: f.kind === 'Retire' ? null : f.counterpartyId, amount: f.kind === 'Sell' ? f.amount : null, reference: f.reference?.trim() || null }),
      this.form, f.kind === 'Sell' ? 'Vehicle sold' : f.kind === 'Transfer' ? 'Vehicle transferred' : 'Vehicle retired', (r) => this.changed.emit(r));
  }
}

// ── Driver and odometer ────────────────────────────────────────────────────────────

@Component({
  selector: 'app-assign-driver-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, FieldComponent, DatePickerComponent, PartnerPickerComponent],
  template: `
    <p-dialog [visible]="!!vehicle()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '480px' }" header="Assign the default driver" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Driver" [control]="form.controls.driverId" for="ad-driver" hint="A driver is the default driver of one vehicle at a time."><vms-partner-picker role="Driver" formControlName="driverId" inputId="ad-driver" /></vms-field>
        <vms-field label="From" [control]="form.controls.effectiveDate" for="ad-date" hint="Today unless you enter an earlier date."><vms-date-picker inputId="ad-date" formControlName="effectiveDate" [notFuture]="true" /></vms-field>
      </form>
      <ng-template pTemplate="footer"><p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" /><p-button label="Assign" icon="pi pi-user" [loading]="busy()" (onClick)="save()" /></ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class AssignDriverDialogComponent extends ActionDialog {
  private readonly api = inject(VehiclesApi);
  private readonly fb = inject(FormBuilder);
  private readonly confirm = inject(ConfirmService);
  readonly vehicle = input<Vehicle | null>(null);
  readonly closed = output<void>();
  readonly changed = output<Vehicle>();
  readonly form = this.fb.group({ driverId: this.fb.control<number | null>(null, Validators.required), effectiveDate: this.fb.control<string | null>(null) });

  constructor() {
    super();
    effect(() => { if (this.vehicle()) untracked(() => { this.form.reset(); this.problems.set([]); }); });
  }

  save(release = false): void {
    const v = this.vehicle();
    if (!v || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.assignDriver(v.id, { driverId: f.driverId!, effectiveDate: f.effectiveDate, releaseFromOther: release }), this.form, 'Driver assigned', (r) => this.changed.emit(r),
      // The driver already has a vehicle: ask, and if the person agrees send the same request again with the yes (VAL-VH-017).
      (error) => {
        if (error.code !== DRIVER_ALREADY_ASSIGNED) return false;
        void this.confirm.ask({ title: 'Driver already assigned', message: this.messages.describe(error), confirmLabel: 'Release and assign', cancelLabel: 'Keep as it is', icon: 'pi pi-user' }).then((go) => go && this.save(true));
        return true;
      });
  }
}

@Component({
  selector: 'app-odometer-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, InputTextModule, FieldComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '440px' }" header="Add an odometer reading" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Reading (km)" [control]="form.controls.km" for="od-km" hint="Not below the opening reading, and in step with the readings around this date."><p-inputnumber inputId="od-km" formControlName="km" [useGrouping]="false" [min]="0" [fluid]="true" /></vms-field>
        <vms-field label="Date" [control]="form.controls.readingDate" for="od-date" hint="Today unless you enter an earlier date."><vms-date-picker inputId="od-date" formControlName="readingDate" [notFuture]="true" /></vms-field>
        <vms-field label="Notes" [control]="form.controls.notes" for="od-notes"><input pInputText id="od-notes" formControlName="notes" /></vms-field>
      </form>
      <ng-template pTemplate="footer"><p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" /><p-button label="Save reading" icon="pi pi-check" [loading]="busy()" (onClick)="save()" /></ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class OdometerDialogComponent extends ActionDialog {
  private readonly api = inject(VehiclesApi);
  private readonly fb = inject(FormBuilder);
  readonly vehicleId = input<number | null>(null);
  readonly open = computed(() => this.vehicleId() !== null);
  readonly closed = output<void>();
  readonly saved = output<void>();
  readonly form = this.fb.group({ km: this.fb.control<number | null>(null, [Validators.required, Validators.min(0)]), readingDate: this.fb.control<string | null>(null), notes: [''] });

  constructor() {
    super();
    effect(() => { if (this.vehicleId() !== null) untracked(() => { this.form.reset(); this.problems.set([]); }); });
  }

  save(): void {
    const id = this.vehicleId();
    if (id === null || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.addOdometer(id, { km: f.km!, readingDate: f.readingDate, notes: f.notes?.trim() || null }), this.form, 'Reading saved', () => this.saved.emit());
  }
}

// ── Attached items ─────────────────────────────────────────────────────────────────

@Component({
  selector: 'app-attach-item-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, InputTextModule, InputNumberModule, FieldComponent, DatePickerComponent, LookupPickerComponent, PartnerPickerComponent],
  template: `
    <p-dialog [visible]="vehicleId() !== null" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '620px' }" header="Attach an item" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="form-grid" novalidate>
        <vms-field label="Item type" [control]="form.controls.itemTypeId" for="ai-type"><vms-lookup-picker type="ATTACHED_ITEM_TYPE" formControlName="itemTypeId" inputId="ai-type" placeholder="Choose a type" [showClear]="false" /></vms-field>
        <vms-field label="Description" [control]="form.controls.description" for="ai-desc" hint="Size, spec, brand."><input pInputText id="ai-desc" formControlName="description" /></vms-field>
        <vms-field label="Serial or identification number" [control]="form.controls.serialNo" for="ai-serial" hint="Unique among attached items. A container number for a container."><input pInputText id="ai-serial" formControlName="serialNo" autocomplete="off" /></vms-field>
        <vms-field label="Supplier or body maker" [control]="form.controls.supplierId" for="ai-supplier"><vms-partner-picker formControlName="supplierId" inputId="ai-supplier" [allowCreate]="false" /></vms-field>
        <vms-field label="Installation date" [control]="form.controls.installationDate" for="ai-date" hint="Today unless you enter an earlier date."><vms-date-picker inputId="ai-date" formControlName="installationDate" [notFuture]="true" /></vms-field>
        <vms-field label="Warranty until" [control]="form.controls.warrantyUntil" for="ai-warranty"><vms-date-picker inputId="ai-warranty" formControlName="warrantyUntil" /></vms-field>
        @if (canSeeCost()) { <vms-field label="Cost (PKR)" [control]="form.controls.cost" for="ai-cost"><p-inputnumber inputId="ai-cost" formControlName="cost" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [min]="0" [fluid]="true" /></vms-field> }
        <vms-field label="Condition" [control]="form.controls.condition" for="ai-condition"><p-select inputId="ai-condition" formControlName="condition" [options]="conditions" optionLabel="label" optionValue="value" placeholder="Choose…" [showClear]="true" [fluid]="true" appendTo="body" /></vms-field>
      </form>
      <ng-template pTemplate="footer"><p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" /><p-button label="Attach" icon="pi pi-plus" [loading]="busy()" (onClick)="save()" /></ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class AttachItemDialogComponent extends ActionDialog {
  private readonly api = inject(VehiclesApi);
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  readonly vehicleId = input<number | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();
  readonly canSeeCost = computed(() => this.auth.hasPermission('VEH.FIELD.COST.VIEW'));
  protected readonly conditions = optionsOf(ITEM_CONDITIONS);
  readonly form = this.fb.group({
    itemTypeId: this.fb.control<number | null>(null, Validators.required), description: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(150)]),
    serialNo: [''], supplierId: this.fb.control<number | null>(null), installationDate: this.fb.control<string | null>(null), warrantyUntil: this.fb.control<string | null>(null),
    cost: this.fb.control<number | null>(null, Validators.min(0)), condition: this.fb.control<string | null>(null),
  });

  constructor() {
    super();
    effect(() => { if (this.vehicleId() !== null) untracked(() => { this.form.reset(); this.problems.set([]); }); });
  }

  save(): void {
    const id = this.vehicleId();
    if (id === null || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.attach(id, { ...f, description: f.description.trim(), serialNo: f.serialNo?.trim() || null, cost: this.canSeeCost() ? f.cost : null }), this.form, 'Item attached', () => this.saved.emit());
  }
}

/** What the item dialog is doing to which item. */
export interface ItemAction {
  vehicleId: number;
  item: AttachedItem;
  kind: 'detach' | 'transfer';
}

@Component({
  selector: 'app-item-action-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, TextareaModule, FieldComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="!!action()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '500px' }" [header]="action()?.kind === 'detach' ? 'Detach item' : 'Transfer item'" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      @if (action(); as a) {
        <p><strong>{{ a.item.itemType }}</strong> — {{ a.item.description }} @if (a.item.serialNo) { <span class="mono">({{ a.item.serialNo }})</span> }</p>
        <form [formGroup]="form" class="stack" novalidate>
          @if (a.kind === 'transfer') {
            <vms-field label="Move it to" [control]="form.controls.targetVehicleId" for="ia-target" hint="A vehicle in the fleet. It keeps its serial number, and the chain is kept.">
              <p-select inputId="ia-target" formControlName="targetVehicleId" [options]="targets()" optionLabel="label" optionValue="value" [filter]="true" placeholder="Choose a vehicle" [fluid]="true" appendTo="body" />
            </vms-field>
          }
          <vms-field label="Date" [control]="form.controls.date" for="ia-date" hint="Today unless you enter an earlier date."><vms-date-picker inputId="ia-date" formControlName="date" [notFuture]="true" /></vms-field>
          <vms-field label="Reason" [control]="form.controls.reason" for="ia-reason"><textarea pTextarea id="ia-reason" formControlName="reason" rows="2" [fluid]="true"></textarea></vms-field>
        </form>
      }
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="action()?.kind === 'detach' ? 'Detach' : 'Transfer'" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class ItemActionDialogComponent extends ActionDialog {
  private readonly api = inject(VehiclesApi);
  private readonly fb = inject(FormBuilder);
  readonly action = input<ItemAction | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();
  readonly targets = signal<{ label: string; value: number }[]>([]);
  readonly form = this.fb.group({ targetVehicleId: this.fb.control<number | null>(null), date: this.fb.control<string | null>(null), reason: this.fb.nonNullable.control('') });

  constructor() {
    super();
    effect(() => {
      const a = this.action();
      if (!a) return;
      untracked(() => {
        this.form.reset();
        this.problems.set([]);
        this.form.controls.targetVehicleId.setValidators(a.kind === 'transfer' ? [Validators.required] : []);
        this.form.controls.reason.setValidators(a.kind === 'detach' ? [Validators.required] : []);
        if (a.kind === 'transfer')
          this.api.list({ page: 1, pageSize: 100, search: '', filters: { status: ['Active', 'Assigned', 'UnderMaintenance', 'TemporarilyUnavailable'] } }).subscribe({
            next: (page) => this.targets.set(page.items.filter((v: VehicleListItem) => v.id !== a.vehicleId).map((v) => ({ label: `${v.registrationNo} — ${v.vehicleType ?? ''} ${v.make ?? ''}`.trim(), value: v.id }))),
            error: () => this.targets.set([]),
          });
      });
    });
  }

  save(): void {
    const a = this.action();
    if (!a || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    const request = a.kind === 'detach'
      ? this.api.detach(a.vehicleId, a.item.id, { date: f.date, reason: f.reason.trim() })
      : this.api.transfer(a.vehicleId, a.item.id, { targetVehicleId: f.targetVehicleId!, date: f.date, reason: f.reason.trim() || null });
    this.run(request, this.form, a.kind === 'detach' ? 'Item detached' : 'Item transferred', () => this.saved.emit());
  }
}
