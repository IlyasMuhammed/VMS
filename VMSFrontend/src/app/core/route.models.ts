/** Routes and route stops (FSD §17, §48.3 screen 9). */
export type RouteStopType = 'Origin' | 'Pickup' | 'Via' | 'Delivery' | 'Destination';

export interface RouteStopModel {
  routeStopId: number;
  cityId: number;
  sequence: number;
  stopType: RouteStopType;
  plannedDurationMin?: number | null;
  remarks?: string | null;
}

export interface RouteModel {
  routeId: number;
  routeCode: string;
  routeName: string;
  originCityId: number;
  destinationCityId: number;
  isRoundTrip: boolean;
  distanceKm?: number | null;
  standardDurationMin?: number | null;
  status: 'Active' | 'Inactive';
  remarks?: string | null;
  stops: RouteStopModel[];
}

export interface RouteStopInput {
  cityId: number;
  stopType: RouteStopType;
  plannedDurationMin?: number | null;
  remarks?: string | null;
}

/** Left blank, the code is auto-suggested as `RT-{OriginAbbr}-{DestAbbr}` (`-2`, `-3`, … if taken). */
export interface CreateRouteRequest {
  routeCode?: string | null;
  routeName: string;
  isRoundTrip: boolean;
  distanceKm?: number | null;
  standardDurationMin?: number | null;
  remarks?: string | null;
  stops: RouteStopInput[];
}

export interface UpdateRouteRequest {
  routeName: string;
  isRoundTrip: boolean;
  distanceKm?: number | null;
  standardDurationMin?: number | null;
  status?: 'Active' | 'Inactive' | null;
  remarks?: string | null;
}

export interface UpdateRouteStopsRequest {
  stops: RouteStopInput[];
}
