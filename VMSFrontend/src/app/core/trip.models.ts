/**
 * Trips — fixed and open, their lifecycle, list/search and history (FSD §21, §22, §24, §48.4 screens 12-15 +
 * the unnumbered "Trip List (desk)" row).
 */

export type TripType = 'Fixed' | 'Open';
export type TripStatus = 'Draft' | 'Planned' | 'Assigned' | 'Started' | 'InTransit' | 'AtPickup' | 'Loaded' | 'AtDelivery' | 'Delivered' | 'Completed' | 'OnHold' | 'Cancelled';
export type TripLocationType = 'City' | 'Other';
export type OtherLocationType = 'Warehouse' | 'Factory' | 'CustomerSite' | 'Depot' | 'Terminal' | 'ConstructionSite' | 'Other';
export type RateSource = 'Configured' | 'Manual' | 'Missing';

export interface LocationModel {
  locationType: TripLocationType;
  cityId?: number | null;
  otherLocationType?: OtherLocationType | null;
  otherLocationName?: string | null;
  otherNearestCityId?: number | null;
  label: string;
}

export interface LocationInput {
  locationType: TripLocationType;
  cityId?: number | null;
  otherLocationType?: OtherLocationType | null;
  otherLocationName?: string | null;
  otherNearestCityId?: number | null;
}

export interface TripModel {
  tripId: number;
  tripNumber: string;
  tripType: TripType;
  customerId: number;
  customerTripReference?: string | null;
  tripConfigurationId?: number | null;
  routeId?: number | null;
  vehicleId: number;
  driverId?: number | null;
  defaultDriverId?: number | null;
  isDriverOverridden: boolean;
  driverOverrideReason?: string | null;
  tripDate: string;
  plannedStart?: string | null;
  actualStart?: string | null;
  actualEnd?: string | null;
  startOdometer?: number | null;
  endOdometer?: number | null;
  tripRateId?: number | null;
  tripRateAmount?: number | null;
  rateEffectiveFrom?: string | null;
  rateEffectiveTo?: string | null;
  rateSource: RateSource;
  tripAmount?: number | null;
  currencyCode?: string | null;
  rateMissing: boolean;
  status: TripStatus;
  heldFromStatus?: string | null;
  holdReason?: string | null;
  cancelReason?: string | null;
  completionDate?: string | null;
  isActive: boolean;
  inactiveReason?: string | null;
  invoiceId?: number | null;
  remarks?: string | null;
  rowVersion: string;
  warnings: string[];
  // Open trips only
  from?: LocationModel | null;
  to?: LocationModel | null;
  stops: LocationModel[];
  isRoundTrip: boolean;
  routeLabel?: string | null;
}

export interface CreateFixedTripRequest {
  customerId: number;
  tripConfigurationId: number;
  vehicleId: number;
  driverId?: number | null;
  driverOverrideReason?: string | null;
  tripDate: string;
  customerTripReference?: string | null;
  plannedStart?: string | null;
  remarks?: string | null;
}

export interface CreateOpenTripRequest {
  customerId: number;
  vehicleId: number;
  driverId?: number | null;
  driverOverrideReason?: string | null;
  tripDate: string;
  customerTripReference?: string | null;
  plannedStart?: string | null;
  from: LocationInput;
  to: LocationInput;
  stops: LocationInput[];
  isRoundTrip: boolean;
  tripAmount: number;
  remarks?: string | null;
}

// ── Lifecycle (§24) ──────────────────────────────────────────────────────────────────

export interface TransitionTripRequest {
  startOdometer?: number | null;
  endOdometer?: number | null;
  rowVersion: string;
}

export interface HoldTripRequest {
  reason: string;
  rowVersion: string;
}

export interface ResumeTripRequest {
  rowVersion: string;
}

export interface CancelTripRequest {
  reason: string;
  rowVersion: string;
}

export interface ReopenTripRequest {
  rowVersion: string;
}

export interface ChangeTripActiveRequest {
  reason?: string | null;
  rowVersion: string;
}

// ── List / search (§48.4's unnumbered "Trip List (desk)" row) ──────────────────────────

export interface TripListItem {
  tripId: number;
  tripNumber: string;
  tripType: TripType;
  tripDate: string;
  customerId: number;
  customerName: string;
  customerTripReference?: string | null;
  routeLabel?: string | null;
  vehicleId: number;
  vehicleRegistrationNo?: string | null;
  driverId?: number | null;
  driverName?: string | null;
  status: TripStatus;
  tripAmount?: number | null;
  currencyCode?: string | null;
  rateMissing: boolean;
  podStatus?: string | null;
  invoiceId?: number | null;
  invoiceNumber?: string | null;
  isActive: boolean;
}

export interface TripSearchFilter {
  fromDate?: string | null;
  toDate?: string | null;
  customerId?: number | null;
  vehicleId?: number | null;
  driverId?: number | null;
  status?: string | null;
  tripType?: string | null;
  isActive?: boolean | null;
  invoiced?: boolean | null;
}

// ── History (§48.1) ──────────────────────────────────────────────────────────────────

export interface TripHistoryChange {
  id: number;
  occurredAt: string;
  groupId: string;
  userName?: string | null;
  entity: string;
  recordId: string;
  action: string;
  field?: string | null;
  oldValue?: string | null;
  newValue?: string | null;
  reason?: string | null;
  restricted: boolean;
}

export interface TripHistory {
  changes: { items: TripHistoryChange[]; totalCount: number; page: number; pageSize: number; totalPages: number };
}
