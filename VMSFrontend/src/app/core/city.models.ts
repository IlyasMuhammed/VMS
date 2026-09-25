/** The city master (FSD §16, §48.3 screen 8) — its own Trips-module entity, richer than the platform's generic
 * `CITY` lookup (name/abbreviation/province) which every other picker (`vms-lookup-picker type="CITY"`) reads. */
export interface CityModel {
  cityId: number;
  cityName: string;
  abbreviation: string;
  countryId: number;
  provinceState?: string | null;
  status: 'Active' | 'Inactive';
}

export interface SaveCityRequest {
  cityName: string;
  abbreviation: string;
  countryId?: number | null;
  provinceState?: string | null;
  status: 'Active' | 'Inactive';
}
