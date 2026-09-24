/**
 * What every list screen has in common (FSD §9.1): server-side paging at 25 rows, a quick search that
 * only starts at three characters, and filters. No framework imports, so it can be tested under Node
 * (scripts/check-shared.test.mjs).
 */

export const DEFAULT_PAGE_SIZE = 25;
export const PAGE_SIZE_OPTIONS = [10, 25, 50, 100];

/** A quick search applies from this many characters (FSD §9.1): shorter text would match half the table. */
export const MIN_SEARCH_LENGTH = 3;

export interface SortState {
  field: string;
  /** 1 ascending, -1 descending (PrimeNG's convention). */
  order: 1 | -1;
}

/** Everything the server needs to return one page of a list. */
export interface ListQuery {
  /** 1-based. */
  page: number;
  pageSize: number;
  search: string;
  filters: Record<string, unknown>;
  sort?: SortState;
}

/** What the filter bar holds and the grid listens to. */
export interface FilterValue {
  search: string;
  filters: Record<string, unknown>;
}

export const noFilters = (): FilterValue => ({ search: '', filters: {} });

/** A filter counts as "set" when it holds something the user chose: not empty, not an empty selection, not an unticked box. */
export function hasValue(value: unknown): boolean {
  if (value === null || value === undefined || value === '' || value === false) return false;
  if (Array.isArray(value)) return value.length > 0;
  return true;
}

/** True if any filter or search text is narrowing the list: decides between "nothing here yet" and "nothing matches". */
export function isFiltering(value: FilterValue): boolean {
  return value.search.trim().length > 0 || Object.values(value.filters).some(hasValue);
}

/**
 * The search text to send, or null when it is too short to send yet. Empty text is sent (it clears the
 * search); one or two characters are held back until the person types a third.
 */
export function searchToApply(text: string, min: number = MIN_SEARCH_LENGTH): string | null {
  const trimmed = text.trim();
  if (trimmed.length === 0) return '';
  return trimmed.length >= min ? trimmed : null;
}

/** The query as URL parameters: search and paging as given, sort as `field,asc|desc`, a filter with several values repeated. */
export function toQueryParams(query: ListQuery): Record<string, string | string[]> {
  const params: Record<string, string | string[]> = { page: String(query.page), pageSize: String(query.pageSize) };
  if (query.search.trim()) params['search'] = query.search.trim();
  if (query.sort) params['sort'] = `${query.sort.field},${query.sort.order === 1 ? 'asc' : 'desc'}`;
  for (const [key, value] of Object.entries(query.filters)) {
    if (!hasValue(value)) continue;
    params[key] = Array.isArray(value) ? value.map(String) : String(value);
  }
  return params;
}
