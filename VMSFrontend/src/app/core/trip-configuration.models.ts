/** Trip Configurations, their stops and allowed vehicles (FSD §18, §19, §48.3 screen 10), and their effective-dated
 * rates and re-pricing (FSD §26, §48.3 screen 11). */
export type TripDirectionType = 'OneWay' | 'Return' | 'RoundTrip';
export type TripConfigurationStatus = 'Draft' | 'Active' | 'Inactive';

export interface TripConfigurationStopModel {
  tripConfigurationStopId: number;
  cityId?: number | null;
  otherLocation?: string | null;
  sequence: number;
  stopType: string;
}

export interface TripConfigurationStopInput {
  cityId?: number | null;
  otherLocation?: string | null;
  stopType: string;
}

export interface TripConfigurationModel {
  tripConfigurationId: number;
  customerId: number;
  tripCode: string;
  name: string;
  routeId: number;
  directionType: TripDirectionType;
  status: TripConfigurationStatus;
  remarks?: string | null;
  rowVersion: string;
  stops: TripConfigurationStopModel[];
  /** Non-blocking warnings from the last action (Activate's "no active rate yet" check) — empty otherwise. */
  warnings: string[];
}

/** Left blank, the trip code is auto-suggested as `{CustShort}-{Orig}-{Dest}-01` (`-RT` for a round trip). */
export interface CreateTripConfigurationRequest {
  customerId: number;
  tripCode?: string | null;
  name: string;
  routeId: number;
  directionType: TripDirectionType;
  remarks?: string | null;
}

export interface UpdateTripConfigurationRequest {
  name: string;
  directionType: TripDirectionType;
  remarks?: string | null;
  rowVersion: string;
}

export interface ChangeTripConfigurationStatusRequest {
  rowVersion: string;
}

export interface UpdateTripConfigurationStopsRequest {
  stops: TripConfigurationStopInput[];
}

export interface CopyTripConfigurationRequest {
  customerId?: number | null;
  tripCode?: string | null;
  name?: string | null;
}

export interface TripConfigurationVehicleModel {
  tripConfigurationVehicleId: number;
  vehicleId: number;
  vehicleCode?: string | null;
  registrationNo?: string | null;
  category?: string | null;
  effectiveFrom: string;
  effectiveTo?: string | null;
  status: 'Active' | 'Inactive';
  remarks?: string | null;
}

export interface AssignTripConfigurationVehicleRequest {
  vehicleId: number;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  remarks?: string | null;
}

export interface UpdateTripConfigurationVehicleRequest {
  effectiveTo?: string | null;
  status?: 'Active' | 'Inactive' | null;
  remarks?: string | null;
}

// ── Rates (§26) ──────────────────────────────────────────────────────────────────────

export interface TripRateModel {
  tripRateId: number;
  customerId: number;
  tripConfigurationId: number;
  effectiveFrom: string;
  effectiveTo?: string | null;
  rateAmount: number;
  currencyCode: string;
  status: 'Active' | 'Inactive';
  remarks?: string | null;
  rowVersion: string;
}

export interface SaveTripRateRequest {
  effectiveFrom: string;
  /** Blank = open-ended. */
  effectiveTo?: string | null;
  rateAmount: number;
  currencyCode?: string | null;
  remarks?: string | null;
}

export interface UpdateTripRateRequest {
  rateAmount?: number | null;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  remarks?: string | null;
  rowVersion: string;
}

export interface InactivateTripRateRequest {
  rowVersion: string;
}

/** Splits the rate covering `date` into up to three rows: before/`date`/after — §26's own worked example. */
export interface SplitTripRateRequest {
  date: string;
  rateAmount: number;
  remarks?: string | null;
}

export interface RateResolutionResult {
  found: boolean;
  tripRateId?: number | null;
  rateAmount?: number | null;
  currencyCode?: string | null;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
}

// ── Re-pricing (§26) ─────────────────────────────────────────────────────────────────

export interface ResolveMissingRatesRequest {
  customerId?: number | null;
  tripConfigurationId?: number | null;
}

export interface ResolveMissingRatesResult {
  considered: number;
  updated: number;
  stillMissing: number;
  updatedTripIds: number[];
}

/**
 * "Re-price Trips" (preview grid old/new) needs a list of trip ids to consider, and there is nowhere yet to pick
 * them from: the Trip module has no list/picker endpoint (only `GET /trips/{id}`) until the Trip screens cluster
 * (§48.4, screens 12-18) exists. `RepriceTripsApi` is defined here for when that cluster adds a trip picker; no
 * screen in this Setup cluster calls it.
 */
export interface RepriceTripsRequest {
  tripIds: number[];
  reason?: string | null;
  commit: boolean;
}

export interface RepriceResultItem {
  tripId: number;
  tripNumber: string;
  oldAmount?: number | null;
  oldRateSource: string;
  newAmount?: number | null;
  newRateSource: string;
  excluded: boolean;
  excludedReason?: string | null;
}

export interface RepriceResult {
  committed: boolean;
  items: RepriceResultItem[];
}
