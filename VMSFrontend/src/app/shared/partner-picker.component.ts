import { Component, computed, effect, forwardRef, inject, input, signal, untracked } from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { Subject, catchError, debounceTime, of, switchMap } from 'rxjs';
import { PartnersApi } from '../core/api.services';
import { AuthService } from '../core/auth.service';
import { PartnerPickerItem } from '../core/partner.models';
import { PartnerQuickDialogComponent } from '../pages/partners/partner-quick-dialog.component';
import { roleLabel } from '../pages/partners/partner-logic';

interface Option {
  label: string;
  value: number;
  disabled?: boolean;
}

const labelOf = (p: { displayName?: string | null; legalName: string; bpCode: string }): string => `${p.displayName || p.legalName} (${p.bpCode})`;

/**
 * Choose a business partner by role, for any form that points at one (a vehicle's driver, a lease's bank): only Active
 * partners are offered (BR-BP-020), searched on the server as the person types. If the one wanted is not there, "New" opens a
 * compact partner form with the role already chosen, and the new partner is selected when it is saved (FR-BP-014).
 *
 * ```html
 * <vms-partner-picker role="Driver" formControlName="driverId" />
 * ```
 * The value is the partner's id. A partner already chosen that has since gone Inactive is still shown, so an old record is not blank.
 */
@Component({
  selector: 'vms-partner-picker',
  standalone: true,
  imports: [FormsModule, SelectModule, ButtonModule, PartnerQuickDialogComponent],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => PartnerPickerComponent), multi: true }],
  template: `
    <div class="picker">
      <p-select
        [options]="options()"
        optionLabel="label"
        optionValue="value"
        optionDisabled="disabled"
        [ngModel]="value()"
        (ngModelChange)="pick($event)"
        (onBlur)="onTouched()"
        (onFilter)="search.next($event.filter ?? '')"
        [filter]="true"
        filterPlaceholder="Type a name, code or mobile"
        [filterFields]="['label']"
        [resetFilterOnHide]="true"
        [placeholder]="placeholder()"
        [showClear]="showClear()"
        [disabled]="disabled()"
        [loading]="loading()"
        [inputId]="inputId()"
        [ariaLabel]="ariaLabel() ?? (role() ? roleLabelOf(role()!) : 'Partner')"
        [emptyFilterMessage]="'No ' + (role() ? roleLabelOf(role()!).toLowerCase() + 's' : 'partners') + ' match.'"
        [fluid]="true"
        appendTo="body"
      />
      @if (canCreate()) {
        <p-button icon="pi pi-plus" label="New" severity="secondary" [outlined]="true" [disabled]="disabled()" (onClick)="creating.set(true)" title="Add a new partner" />
      }
    </div>

    <app-partner-quick-dialog [role]="creating() ? role() : null" (created)="onCreated($event)" (closed)="creating.set(false)" />
  `,
  styles: [`.picker { display: flex; gap: .5rem; align-items: stretch; } .picker p-select { flex: 1; min-width: 0; }`],
})
export class PartnerPickerComponent implements ControlValueAccessor {
  private readonly api = inject(PartnersApi);
  private readonly auth = inject(AuthService);

  /** Offer only partners holding this role. Leave out to offer every active partner (and no "New" button, which needs a role). */
  readonly role = input<string | null>(null);
  readonly placeholder = input('Choose…');
  readonly showClear = input(true);
  readonly inputId = input<string | undefined>(undefined);
  readonly ariaLabel = input<string | undefined>(undefined);
  /** Show the "New" button to people who may create partners. */
  readonly allowCreate = input(true);

  protected readonly value = signal<number | null>(null);
  protected readonly options = signal<Option[]>([]);
  protected readonly loading = signal(false);
  protected readonly disabled = signal(false);
  protected readonly creating = signal(false);
  protected readonly search = new Subject<string>();
  protected readonly roleLabelOf = roleLabel;
  protected readonly canCreate = computed(() => this.allowCreate() && !!this.role() && this.auth.hasPermission('BP.CREATE'));

  private onChange: (value: number | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  constructor() {
    // The list follows the role, and the search box: only the newest answer is used.
    this.search
      .pipe(
        debounceTime(250),
        switchMap((term) => {
          this.loading.set(true);
          return this.api.picker(this.role(), term).pipe(catchError(() => of([] as PartnerPickerItem[])));
        }),
      )
      .subscribe((found) => {
        this.loading.set(false);
        this.setOptions(found);
      });

    effect(() => {
      this.role();
      untracked(() => this.search.next(''));
    });
  }

  /** The found partners, plus the one already chosen even if it is not among them (it may be Inactive now, or beyond the first page). */
  private setOptions(found: PartnerPickerItem[]): void {
    const options: Option[] = found.map((p) => ({ label: labelOf(p), value: p.id }));
    const current = this.value();
    if (current !== null && !options.some((o) => o.value === current)) {
      const known = this.options().find((o) => o.value === current);
      if (known) options.unshift(known);
      else this.loadCurrent(current);
    }
    this.options.set(options);
  }

  private loadCurrent(id: number): void {
    this.api.get(id).subscribe({
      next: (p) => this.options.update((o) => (o.some((x) => x.value === id) ? o : [{ label: `${labelOf(p)}${p.status === 'Active' ? '' : ' — ' + p.status}`, value: id, disabled: p.status !== 'Active' }, ...o])),
      error: () => this.options.update((o) => (o.some((x) => x.value === id) ? o : [{ label: `Partner #${id}`, value: id, disabled: true }, ...o])),
    });
  }

  protected onCreated(p: PartnerPickerItem): void {
    this.creating.set(false);
    this.options.update((o) => [{ label: labelOf(p), value: p.id }, ...o.filter((x) => x.value !== p.id)]);
    this.pick(p.id);
  }

  protected pick(value: number | null): void {
    this.value.set(value);
    this.onChange(value);
  }

  writeValue(value: number | null): void {
    this.value.set(value ?? null);
    if (value !== null && value !== undefined && !this.options().some((o) => o.value === value)) this.loadCurrent(value);
  }

  registerOnChange(fn: (value: number | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }
}
