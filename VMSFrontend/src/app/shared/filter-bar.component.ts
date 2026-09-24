import { Component, computed, effect, inject, input, model, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { SelectModule } from 'primeng/select';
import { debounce, distinctUntilChanged, timer } from 'rxjs';
import { BranchesApi, LookupsApi } from '../core/api.services';
import { FilterValue, MIN_SEARCH_LENGTH, hasValue, isFiltering, noFilters, searchToApply } from './list-query';

export interface FilterOption {
  label: string;
  value: unknown;
}

/** One filter beside the search box. */
export interface FilterDef {
  key: string;
  label: string;
  /** `select` picks one value, `multiselect` several, `toggle` is a yes/no box that filters only while ticked. */
  kind: 'select' | 'multiselect' | 'toggle';
  options?: FilterOption[];
  /** Fill the options from a master list (for example `CITY`) instead of listing them. */
  lookup?: string;
  /** With `lookup`: filter on the value's `code` (default) or its `id`. */
  lookupValue?: 'code' | 'id';
  /** Fill the options from the tenant's branches instead of listing them. */
  branches?: boolean;
}

/**
 * The search box and filters above a list (FSD §9.1). The search waits until the person has typed three
 * characters (or cleared the box), and a moment after the last keystroke, so a list does not refresh on
 * every letter and a two-letter search does not match half the table. Bind it two ways and give the same
 * value to the grid:
 * `<vms-filter-bar [(value)]="bar" [filters]="defs" /> <vms-data-grid [search]="bar().search" [filters]="bar().filters" … />`
 */
@Component({
  selector: 'vms-filter-bar',
  standalone: true,
  imports: [FormsModule, InputTextModule, SelectModule, MultiSelectModule, CheckboxModule, ButtonModule],
  template: `
    <div class="bar" role="search">
      @if (showSearch()) {
        <div class="search">
          <input pInputText type="search" [ngModel]="text()" (ngModelChange)="text.set($event)" (keyup.enter)="commitNow()" [placeholder]="searchPlaceholder()"
                 [attr.aria-label]="searchPlaceholder()" [attr.aria-describedby]="tooShort() ? hintId : null" autocomplete="off" />
          @if (tooShort()) { <span class="hint" [id]="hintId">Type at least {{ minSearch() }} characters to search.</span> }
        </div>
      }

      @for (f of filters(); track f.key) {
        @switch (f.kind) {
          @case ('select') {
            <p-select [options]="optionsOf(f)" optionLabel="label" optionValue="value" [ngModel]="valueOf(f)" (ngModelChange)="set(f.key, $event)"
                      [placeholder]="f.label" [showClear]="true" [ariaLabel]="f.label" appendTo="body" />
          }
          @case ('multiselect') {
            <p-multiselect [options]="optionsOf(f)" optionLabel="label" optionValue="value" [ngModel]="valueOf(f)" (ngModelChange)="set(f.key, $event)"
                           [placeholder]="f.label" [showClear]="true" [ariaLabel]="f.label" display="chip" [maxSelectedLabels]="2" appendTo="body" />
          }
          @case ('toggle') {
            <label class="toggle"><p-checkbox [ngModel]="valueOf(f) === true" (ngModelChange)="set(f.key, $event)" [binary]="true" /> {{ f.label }}</label>
          }
        }
      }

      @if (active()) { <button type="button" class="clear" (click)="clear()">Clear filters</button> }
    </div>
  `,
  styles: [
    `
      .bar { display: flex; flex-wrap: wrap; align-items: flex-start; gap: .75rem; margin-bottom: 1rem; }
      .search { display: flex; flex-direction: column; gap: .25rem; min-width: 18rem; }
      .search input { width: 100%; }
      .hint { color: var(--vms-muted); font-size: .8rem; }
      .toggle { display: inline-flex; align-items: center; gap: .5rem; height: 2.5rem; }
      .clear { border: 0; background: none; padding: .55rem .25rem; font: inherit; cursor: pointer; color: var(--vms-brand-text); text-decoration: underline; }
    `,
  ],
})
export class FilterBarComponent {
  private readonly lookups = inject(LookupsApi);
  private readonly branchesApi = inject(BranchesApi);
  protected readonly hintId = `filter-hint-${Math.random().toString(36).slice(2, 8)}`;

  /** The applied search and filters. Two-way bindable. */
  readonly value = model<FilterValue>(noFilters());
  readonly filters = input<readonly FilterDef[]>([]);
  readonly showSearch = input(true);
  readonly searchPlaceholder = input('Search');
  readonly minSearch = input(MIN_SEARCH_LENGTH);
  readonly debounceMs = input(300);

  /** What is typed, whether or not it has been applied yet. */
  protected readonly text = signal('');
  protected readonly tooShort = computed(() => {
    const typed = this.text().trim();
    return typed.length > 0 && typed.length < this.minSearch();
  });
  protected readonly active = computed(() => isFiltering(this.value()) || this.text().trim().length > 0);

  private readonly lookupOptions = signal<Record<string, FilterOption[]>>({});
  private readonly branchOptions = signal<FilterOption[] | null>(null);
  private readonly appliedSearch = computed(() => this.value().search);

  constructor() {
    // Typing waits for a pause, then applies only if long enough (or empty).
    toObservable(this.text)
      .pipe(debounce(() => timer(this.debounceMs())), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe(() => this.commitSearch());

    // Someone else changed the search (Clear filters, a saved view): show it in the box. Only the search part
    // is watched, so changing a filter while the person is mid-word does not wipe what they have typed.
    effect(() => {
      const applied = this.appliedSearch();
      untracked(() => {
        if (searchToApply(this.text(), this.minSearch()) !== applied) this.text.set(applied);
      });
    });

    // Filters filled from a master list load once.
    effect(() => {
      for (const f of this.filters().filter((d) => d.lookup)) {
        if (this.lookupOptions()[f.lookup!]) continue;
        untracked(() =>
          this.lookups.active(f.lookup!).subscribe((items) =>
            this.lookupOptions.update((all) => ({ ...all, [f.lookup!]: items.map((i) => ({ label: i.description, value: (f.lookupValue ?? 'code') === 'id' ? i.id : i.code })) })),
          ),
        );
      }
    });

    // A branch filter loads once too, from the tenant's own locations rather than a lookup list.
    effect(() => {
      if (this.branchOptions() !== null || !this.filters().some((d) => d.branches)) return;
      untracked(() => this.branchesApi.list().subscribe((items) => this.branchOptions.set(items.map((b) => ({ label: b.name, value: b.id })))));
    });
  }

  protected optionsOf(f: FilterDef): FilterOption[] {
    if (f.branches) return this.branchOptions() ?? [];
    return f.lookup ? (this.lookupOptions()[f.lookup] ?? []) : (f.options ?? []);
  }

  protected valueOf(f: FilterDef): unknown {
    return this.value().filters[f.key] ?? null;
  }

  protected set(key: string, value: unknown): void {
    const filters = { ...this.value().filters };
    if (hasValue(value)) filters[key] = value;
    else delete filters[key];
    this.value.set({ ...this.value(), filters });
  }

  protected commitNow(): void {
    this.commitSearch();
  }

  private commitSearch(): void {
    const next = searchToApply(this.text(), this.minSearch());
    if (next !== null && next !== this.value().search) this.value.set({ ...this.value(), search: next });
  }

  protected clear(): void {
    this.text.set('');
    this.value.set(noFilters());
  }
}
