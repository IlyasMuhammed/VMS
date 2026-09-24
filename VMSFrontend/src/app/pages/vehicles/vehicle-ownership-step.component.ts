import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { SelectModule } from 'primeng/select';
import { FieldComponent } from '../../shared/field.component';
import { CategoryFieldsComponent, buildCategoryDetails, toCategoryDetails } from './category-fields.component';
import { CATEGORIES, DraftOwnership, optionsOf } from './vehicle-logic';

/** The ownership of a Draft: the category, and the counterparty and terms it asks for. Kept with the draft until activation. */
export function buildOwnershipForm(fb: FormBuilder) {
  return fb.group({ category: fb.control<string | null>(null), details: buildCategoryDetails(fb) });
}
export type OwnershipForm = ReturnType<typeof buildOwnershipForm>;

export function patchOwnership(form: OwnershipForm, o: DraftOwnership | null): void {
  form.reset({ category: o?.category ?? null }, { emitEvent: true });
  form.controls.details.reset({ agreementReference: '' }, { emitEvent: false });
  if (o) form.controls.details.patchValue({ ...o.details, agreementReference: (o.details['agreementReference'] as string | undefined) ?? '' }, { emitEvent: true });
}

export function toOwnership(form: OwnershipForm): DraftOwnership {
  return { category: form.controls.category.value, details: { ...toCategoryDetails(form.controls.details) } };
}

/**
 * Step 2 of the vehicle wizard: how the fleet comes to have the vehicle (FSD §17). Nothing is recorded as an arrangement yet:
 * the answers are kept with the draft and become the vehicle's first ownership relation, dated by the acquisition date, when it is activated.
 */
@Component({
  selector: 'app-vehicle-ownership-step',
  standalone: true,
  imports: [ReactiveFormsModule, SelectModule, FieldComponent, CategoryFieldsComponent],
  template: `
    <h2>Ownership</h2>
    <p class="muted lead">How the fleet comes to have this vehicle. Nothing is recorded until the vehicle is activated on the last step; what you choose here is kept with the draft.</p>
    <div class="form-grid" [formGroup]="form()">
      <vms-field class="full" label="Ownership category" [control]="form().controls.category" for="ow-category" hint="Required to activate. It can be changed later, from the vehicle's own screen, once it is in the fleet.">
        <p-select inputId="ow-category" formControlName="category" [options]="categories" optionLabel="label" optionValue="value" placeholder="Choose…" [showClear]="true" [fluid]="true" appendTo="body" />
      </vms-field>
    </div>
    @if (category() === 'SelfOwned') { <p class="muted">Self owned: there is no counterparty and there are no terms.</p> }
    @if (category()) {
      <app-category-fields [details]="form().controls.details" [category]="category()" idPrefix="ow" [finance]="mayEnterFinance()" [allowCreate]="true" bankHint="Leave empty to use the bank of the finance agreement entered on the next step." />
    }
  `,
  styles: [`h2 { font-size: 1.05rem; margin: 0 0 .25rem; } .lead { margin: 0 0 .75rem; } p.muted { margin: .75rem 0 0; }`],
})
export class VehicleOwnershipStepComponent {
  private readonly destroyRef = inject(DestroyRef);
  readonly form = input.required<OwnershipForm>();
  /** Amounts (rent, deposit, agreed amount) are asked only of someone who may see them. */
  readonly mayEnterFinance = input(false);

  protected readonly categories = optionsOf(CATEGORIES);
  protected readonly category = signal<string>('');

  constructor() {
    effect(() => {
      const form = this.form();
      untracked(() => {
        this.category.set(form.controls.category.value ?? '');
        form.controls.category.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((c) => this.category.set(c ?? ''));
      });
    });
  }
}
