import { NgTemplateOutlet } from '@angular/common';
import { Component, Directive, TemplateRef, computed, contentChild, contentChildren, effect, inject, input, linkedSignal, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ButtonModule } from 'primeng/button';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { Observable, Subject, catchError, of, switchMap, tap } from 'rxjs';
import { errorMessage } from '../core/api-error';
import { EMPTY } from '../core/datetime/datetime';
import { Paged } from '../core/models';
import { DEFAULT_PAGE_SIZE, ListQuery, PAGE_SIZE_OPTIONS, SortState } from './list-query';

export interface GridColumn {
  /** The property of the row shown here, and the sort field sent to the server. */
  key: string;
  header: string;
  sortable?: boolean;
  /** A CSS class for the header and its cells, for example to right-align a number. */
  class?: string;
  width?: string;
}

/**
 * Draws a column's cell yourself: `<ng-template vmsCell="status" let-row><p-tag … /></ng-template>`.
 * A column with no template shows the row's property of that name, or a dash if it is empty.
 */
@Directive({ selector: 'ng-template[vmsCell]', standalone: true })
export class GridCellDirective {
  readonly name = input.required<string>({ alias: 'vmsCell' });
  readonly template = inject(TemplateRef<unknown>);
}

/** The buttons at the end of each row: `<ng-template vmsRowActions let-row>…</ng-template>`. */
@Directive({ selector: 'ng-template[vmsRowActions]', standalone: true })
export class GridActionsDirective {
  readonly template = inject(TemplateRef<unknown>);
}

/**
 * A list with server-side paging and sorting (FSD §9.1), for every list screen. Give it the columns and a
 * function that fetches a page; it does the rest: loading state, an error with "Try again", only the latest
 * request is shown (typing quickly cannot leave an old answer on screen), a changed search or filter goes
 * back to page 1, and an empty list says whether nothing exists or nothing matches, with a Clear filters link.
 *
 * ```html
 * <vms-data-grid [columns]="columns" [load]="load" [search]="bar.search" [filters]="bar.filters"
 *                [filtering]="isFiltering" (clearFilters)="clear()">
 *   <ng-template vmsCell="status" let-row><p-tag [value]="row.status" /></ng-template>
 *   <ng-template vmsRowActions let-row><p-button icon="pi pi-pencil" (onClick)="edit(row)" /></ng-template>
 * </vms-data-grid>
 * ```
 */
@Component({
  selector: 'vms-data-grid',
  standalone: true,
  imports: [TableModule, ButtonModule, NgTemplateOutlet],
  template: `
    @if (error(); as message) {
      <div class="alert error error-box" role="alert">
        <span>{{ message }}</span>
        <p-button label="Try again" [text]="true" size="small" (onClick)="reload()" />
      </div>
    }

    <p-table
      [value]="rows()"
      [lazy]="true"
      [lazyLoadOnInit]="false"
      (onLazyLoad)="onLazy($event)"
      [paginator]="true"
      [rows]="size()"
      [first]="first()"
      [totalRecords]="total()"
      [loading]="loading()"
      [rowsPerPageOptions]="pageSizeOptions()"
      [showCurrentPageReport]="true"
      currentPageReportTemplate="{first}–{last} of {totalRecords}"
      [sortField]="sort()?.field"
      [sortOrder]="sort()?.order ?? 1"
      [tableStyle]="{ 'min-width': '40rem' }"
      [attr.aria-label]="label()"
      [attr.aria-busy]="loading()"
    >
      <ng-template pTemplate="header">
        <tr>
          @for (c of columns(); track c.key) {
            @if (c.sortable) {
              <th [pSortableColumn]="c.key" [class]="c.class ?? ''" [style.width]="c.width">{{ c.header }} <p-sortIcon [field]="c.key" /></th>
            } @else {
              <th [class]="c.class ?? ''" [style.width]="c.width">{{ c.header }}</th>
            }
          }
          @if (actions()) { <th class="actions-col"><span class="sr-only">Actions</span></th> }
        </tr>
      </ng-template>

      <ng-template pTemplate="body" let-row>
        <tr>
          @for (c of columns(); track c.key) {
            <td [class]="c.class ?? ''">
              @if (cellTemplates().get(c.key); as template) {
                <ng-container [ngTemplateOutlet]="template" [ngTemplateOutletContext]="{ $implicit: row }" />
              } @else {
                {{ plain(row, c.key) }}
              }
            </td>
          }
          @if (actions(); as rowActions) {
            <td class="actions-col"><div class="actions"><ng-container [ngTemplateOutlet]="rowActions.template" [ngTemplateOutletContext]="{ $implicit: row }" /></div></td>
          }
        </tr>
      </ng-template>

      <ng-template pTemplate="emptymessage">
        <tr>
          <td [attr.colspan]="columns().length + (actions() ? 1 : 0)" class="empty muted">
            @if (loading()) {
              Loading…
            } @else if (filtering()) {
              {{ noMatchMessage() }}
              <button type="button" class="link" (click)="clearFilters.emit()">Clear filters</button>
            } @else {
              {{ emptyMessage() }}
            }
          </td>
        </tr>
      </ng-template>
    </p-table>
  `,
  styles: [
    `
      .error-box { display: flex; align-items: center; justify-content: space-between; gap: 1rem; margin-bottom: .75rem; }
      .empty { text-align: center; padding: 2rem 1rem; }
      .link { border: 0; background: none; padding: 0 0 0 .35rem; font: inherit; cursor: pointer; color: var(--vms-brand-text); text-decoration: underline; }
      .actions-col { width: 1%; white-space: nowrap; }
      .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
    `,
  ],
})
export class DataGridComponent<T = unknown> {
  readonly columns = input.required<readonly GridColumn[]>();
  /** Fetches one page. Called again whenever the page, sort, search or filters change. */
  readonly load = input.required<(query: ListQuery) => Observable<Paged<T>>>();

  readonly search = input('');
  readonly filters = input<Record<string, unknown>>({});
  /** True while a search or filter is narrowing the list; it changes what the empty state says. */
  readonly filtering = input(false);

  readonly pageSize = input(DEFAULT_PAGE_SIZE);
  readonly pageSizeOptions = input<number[]>(PAGE_SIZE_OPTIONS);
  readonly defaultSort = input<SortState | undefined>(undefined);
  readonly emptyMessage = input('Nothing here yet.');
  readonly noMatchMessage = input('Nothing matches these filters.');
  /** Names the table for screen readers, for example "Business partners". */
  readonly label = input('Results');

  readonly clearFilters = output<void>();

  protected readonly cells = contentChildren(GridCellDirective);
  protected readonly actions = contentChild(GridActionsDirective);
  protected readonly cellTemplates = computed(() => new Map(this.cells().map((c) => [c.name(), c.template] as const)));

  protected readonly rows = signal<T[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly page = signal(1);
  protected readonly size = linkedSignal(() => this.pageSize());
  protected readonly sort = linkedSignal<SortState | undefined>(() => this.defaultSort());
  protected readonly first = computed(() => (this.page() - 1) * this.size());

  private readonly requests = new Subject<ListQuery>();

  constructor() {
    // Only the newest request is shown: switchMap drops an older one still in flight.
    this.requests
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set(null);
        }),
        switchMap((query) =>
          this.load()(query).pipe(
            catchError((err) => {
              this.error.set(errorMessage(err, 'The list could not be loaded.'));
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        if (result) {
          this.rows.set(result.items);
          this.total.set(result.totalCount);
        }
        this.loading.set(false);
      });

    // Fetches at start-up too: the effect runs once straight away. A new search or filter starts again at page 1.
    effect(() => {
      this.search();
      this.filters();
      untracked(() => this.fetch(true));
    });
  }

  /** Fetches the current page again, for after a change made elsewhere on the screen. */
  reload(): void {
    this.fetch(false);
  }

  protected onLazy(event: TableLazyLoadEvent): void {
    const size = event.rows ?? this.size();
    this.size.set(size);
    this.page.set(Math.floor((event.first ?? 0) / size) + 1);
    const field = typeof event.sortField === 'string' ? event.sortField : undefined;
    this.sort.set(field ? { field, order: event.sortOrder === -1 ? -1 : 1 } : undefined);
    this.fetch(false);
  }

  private fetch(resetPage: boolean): void {
    if (resetPage) this.page.set(1);
    this.requests.next({ page: this.page(), pageSize: this.size(), search: this.search(), filters: this.filters(), sort: this.sort() });
  }

  protected plain(row: T, key: string): string {
    const value = (row as Record<string, unknown> | null)?.[key];
    return value === null || value === undefined || value === '' ? EMPTY : String(value);
  }
}
