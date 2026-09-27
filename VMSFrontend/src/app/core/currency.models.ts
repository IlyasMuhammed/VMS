/** Currency master, tenant settings and exchange rates (FSD §13A, §48.8's Currency Setup screen). `CurrencyModel`
 * itself lives in `customer.models.ts` (added there first, for the Customer form's own currency field) — reused
 * here rather than duplicated. */

export interface SaveCurrencyRequest {
  /** Only read on create; immutable once the currency exists. */
  currencyCode: string;
  currencyName: string;
  symbol?: string | null;
  decimalPlaces: number;
  status: 'Active' | 'Inactive';
}

export interface CurrencySettingsModel {
  baseCurrencyCode: string;
  multiCurrencyEnabled: boolean;
}

export interface UpdateCurrencySettingsRequest {
  baseCurrencyCode?: string | null;
  multiCurrencyEnabled: boolean;
}

/** `toCurrencyCode` is always the tenant's own current base — the server sets it, never the caller. */
export interface ExchangeRateModel {
  exchangeRateId: number;
  fromCurrencyCode: string;
  toCurrencyCode: string;
  rateDate: string;
  rate: number;
}

export interface SaveExchangeRateRequest {
  fromCurrencyCode: string;
  rateDate?: string | null;
  rate?: number | null;
}
