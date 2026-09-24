import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { firstValueFrom } from 'rxjs';
import { LookupsApi, PartnersApi } from '../../core/api.services';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { LookupPickerComponent } from '../../shared/lookup-picker.component';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { DraftItem, ITEM_CONDITIONS, optionsOf, words } from './vehicle-logic';

/**
 * Adding or changing one item of a Draft. Nothing is sent to the server: the item is kept with the draft, and is attached (and its cost
 * posted as a major expense) when the vehicle is activated. The server judges it then, by the rules of any attached item.
 */
@Component({
  selector: 'app-draft-item-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, InputTextModule, InputNumberModule, FieldComponent, DatePickerComponent, LookupPickerComponent, PartnerPickerComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '620px' }" [header]="editing() ? 'Change item' : 'Add an item'" [closable]="!busy()">
      <form [formGroup]="form" class="form-grid" novalidate>
        <vms-field label="Item type" [control]="form.controls.itemTypeId" for="di-type"><vms-lookup-picker type="ATTACHED_ITEM_TYPE" formControlName="itemTypeId" inputId="di-type" placeholder="Choose a type" [showClear]="false" /></vms-field>
        <vms-field label="Description" [control]="form.controls.description" for="di-desc" hint="Size, spec, brand."><input pInputText id="di-desc" formControlName="description" /></vms-field>
        <vms-field label="Serial or identification number" [control]="form.controls.serialNo" for="di-serial" hint="Unique among attached items. A container number for a container."><input pInputText id="di-serial" formControlName="serialNo" autocomplete="off" /></vms-field>
        <vms-field label="Supplier or body maker" [control]="form.controls.supplierId" for="di-supplier"><vms-partner-picker formControlName="supplierId" inputId="di-supplier" [allowCreate]="false" /></vms-field>
        <vms-field label="Installation date" [control]="form.controls.installationDate" for="di-date" hint="The acquisition date unless you enter a later one."><vms-date-picker inputId="di-date" formControlName="installationDate" [notFuture]="true" /></vms-field>
        <vms-field label="Warranty until" [control]="form.controls.warrantyUntil" for="di-warranty"><vms-date-picker inputId="di-warranty" formControlName="warrantyUntil" /></vms-field>
        @if (mayEnterCost()) { <vms-field label="Cost (PKR)" [control]="form.controls.cost" for="di-cost" hint="Posted as a major expense when the vehicle is activated."><p-inputnumber inputId="di-cost" formControlName="cost" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [min]="0" [fluid]="true" /></vms-field> }
        <vms-field label="Condition" [control]="form.controls.condition" for="di-condition"><p-select inputId="di-condition" formControlName="condition" [options]="conditions" optionLabel="label" optionValue="value" placeholder="Choose…" [showClear]="true" [fluid]="true" appendTo="body" /></vms-field>
      </form>
      <ng-template pTemplate="footer"><p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" /><p-button [label]="editing() ? 'Keep changes' : 'Add item'" icon="pi pi-check" [loading]="busy()" (onClick)="save()" /></ng-template>
    </p-dialog>
  `,
})
export class DraftItemDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly lookups = inject(LookupsApi);
  private readonly partners = inject(PartnersApi);

  /** The item being changed, or a blank one to add. Null keeps the dialog closed. */
  readonly item = input<DraftItem | null>(null);
  readonly editing = input(false);
  readonly mayEnterCost = input(false);
  readonly closed = output<void>();
  readonly saved = output<DraftItem>();

  protected readonly open = computed(() => this.item() !== null);
  protected readonly busy = signal(false);
  protected readonly conditions = optionsOf(ITEM_CONDITIONS);
  readonly form = this.fb.group({
    itemTypeId: this.fb.control<number | null>(null, Validators.required),
    description: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(150)]),
    serialNo: ['', Validators.maxLength(60)],
    supplierId: this.fb.control<number | null>(null),
    installationDate: this.fb.control<string | null>(null),
    warrantyUntil: this.fb.control<string | null>(null),
    cost: this.fb.control<number | null>(null, Validators.min(0)),
    condition: this.fb.control<string | null>(null),
  });

  constructor() {
    effect(() => {
      const i = this.item();
      if (!i) return;
      untracked(() =>
        this.form.reset({
          itemTypeId: i.itemTypeId, description: i.description, serialNo: i.serialNo ?? '', supplierId: i.supplierId ?? null, installationDate: i.installationDate ?? null,
          warrantyUntil: i.warrantyUntil ?? null, cost: i.cost ?? null, condition: i.condition ?? null,
        }),
      );
    });
  }

  async save(): Promise<void> {
    if (this.busy()) return;
    this.form.controls.description.setValue(this.form.controls.description.value.trim());
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const f = this.form.getRawValue();
    this.busy.set(true);
    try {
      // The grid names the type and the supplier, so look their names up now rather than draw ids.
      const types = await firstValueFrom(this.lookups.active('ATTACHED_ITEM_TYPE'));
      const supplier = f.supplierId ? await firstValueFrom(this.partners.get(f.supplierId)).catch(() => null) : null;
      this.saved.emit({
        itemTypeId: f.itemTypeId, itemTypeName: types.find((t) => t.id === f.itemTypeId)?.description ?? null, description: f.description, serialNo: f.serialNo?.trim() || null,
        supplierId: f.supplierId, supplierName: supplier?.displayName ?? null, installationDate: f.installationDate, cost: this.mayEnterCost() ? f.cost : null,
        warrantyUntil: f.warrantyUntil, condition: f.condition,
      });
    } finally {
      this.busy.set(false);
    }
  }
}

/**
 * Step 4 of the vehicle wizard: the major things fitted to the vehicle (FSD §20.1): a container, an AC unit, a tyre set. They are kept
 * with the draft. When the vehicle is activated each is attached and its cost is posted as a major expense, dated by its installation date.
 * Documents come with the documents module: the registration book, which a self-owned or bank-leased vehicle needs to be activated, is asked for there.
 */
@Component({
  selector: 'app-vehicle-items-step',
  standalone: true,
  imports: [DecimalPipe, ButtonModule, BusinessDatePipe, DraftItemDialogComponent],
  template: `
    <h2>Attached items</h2>
    <p class="muted lead">Anything major fitted to the vehicle when it joins the fleet. Nothing is recorded until the vehicle is activated; its cost then becomes a major expense.</p>

    <table>
      <thead><tr><th>Type</th><th>Description</th><th>Serial</th><th>Supplier</th><th>Installed</th>@if (mayEnterCost()) { <th class="num">Cost (PKR)</th> }<th></th></tr></thead>
      <tbody>
        @for (i of items(); track $index) {
          <tr>
            <td>{{ i.itemTypeName || '—' }}</td><td>{{ i.description }}@if (i.condition) { <span class="muted"> · {{ words(i.condition) }}</span> }</td><td class="mono">{{ i.serialNo || '—' }}</td>
            <td>{{ i.supplierName || '—' }}</td><td class="nowrap">@if (i.installationDate) { {{ i.installationDate | vmsDate }} } @else { <span class="muted">Acquisition date</span> }</td>
            @if (mayEnterCost()) { <td class="num">@if (i.cost) { {{ i.cost | number: '1.2-2' }} } @else { — }</td> }
            <td class="row-actions">
              <p-button label="Change" [text]="true" size="small" (onClick)="change($index)" />
              <p-button label="Remove" [text]="true" size="small" severity="danger" (onClick)="remove($index)" />
            </td>
          </tr>
        }
        @if (items().length === 0) { <tr><td [attr.colspan]="mayEnterCost() ? 7 : 6" class="muted">No items yet. Add one, or skip this step: items can also be attached from the vehicle's own screen once it is active.</td></tr> }
      </tbody>
      @if (mayEnterCost() && total() > 0) { <tfoot><tr><td colspan="5">Total cost of items</td><td class="num">{{ total() | number: '1.2-2' }}</td><td></td></tr></tfoot> }
    </table>
    <p-button label="Add an item" icon="pi pi-plus" size="small" (onClick)="add()" />

    <h2 class="docs">Documents</h2>
    <p class="muted">Documents come with the documents module. A self-owned or bank-leased vehicle will then need its registration book on file to be activated; until then activation does not check for it, and the review step says so.</p>

    <app-draft-item-dialog [item]="editingItem()" [editing]="editIndex() >= 0" [mayEnterCost]="mayEnterCost()" (closed)="editingItem.set(null)" (saved)="keep($event)" />
  `,
  styles: [
    `
      h2 { font-size: 1.05rem; margin: 0 0 .25rem; } h2.docs { margin-top: 2rem; } .lead { margin: 0 0 .75rem; }
      table { width: 100%; border-collapse: collapse; margin: 0 0 .75rem; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; }
      tfoot td { font-weight: 600; } .num { text-align: right; font-variant-numeric: tabular-nums; } .nowrap { white-space: nowrap; } .row-actions { white-space: nowrap; text-align: right; }
    `,
  ],
})
export class VehicleItemsStepComponent {
  /** The items of the draft. Two-way: the wizard keeps them and saves them with the draft. */
  readonly items = model<DraftItem[]>([]);
  /** Costs are entered and shown only by someone who may see cost. */
  readonly mayEnterCost = input(false);
  /** Something was added, changed or removed. */
  readonly edited = output<void>();

  protected readonly editingItem = signal<DraftItem | null>(null);
  protected readonly editIndex = signal(-1);
  protected readonly total = computed(() => this.items().reduce((sum, i) => sum + (i.cost ?? 0), 0));
  protected readonly words = words;

  protected add(): void {
    this.editIndex.set(-1);
    this.editingItem.set({ itemTypeId: null, description: '' });
  }

  protected change(index: number): void {
    this.editIndex.set(index);
    this.editingItem.set({ ...this.items()[index] });
  }

  protected remove(index: number): void {
    this.items.update((list) => list.filter((_, i) => i !== index));
    this.edited.emit();
  }

  protected keep(item: DraftItem): void {
    const index = this.editIndex();
    this.items.update((list) => (index >= 0 ? list.map((existing, i) => (i === index ? item : existing)) : [...list, item]));
    this.editingItem.set(null);
    this.edited.emit();
  }
}
