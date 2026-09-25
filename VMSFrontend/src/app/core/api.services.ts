import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AddRoleRequest,
  ApiResponse,
  Branch,
  CreateRoleRequest,
  CreateTenantRequest,
  CreateTenantResult,
  CreateUserRequest,
  CurrentUser,
  EffectivePermission,
  LogoVariant,
  LookupItem,
  LookupType,
  Paged,
  PatchUserRequest,
  PermissionGroup,
  RoleDetail,
  RoleListItem,
  RoleUser,
  SaveLookupRequest,
  SetDriverLinkRequest,
  SetScopeRequest,
  TenantDetail,
  TenantListItem,
  UserDetail,
  UserListFilter,
  UserListItem,
} from './models';
import { ListQuery, toQueryParams } from '../shared/list-query';
import {
  CreatePartnerRequest,
  CustomerModel,
  DriverModel,
  DuplicateCheckRequest,
  DuplicateMatch,
  Partner,
  PartnerHistory,
  PartnerListItem,
  PartnerPickerItem,
  PartnerUsage,
  UpdatePartnerRequest,
  VendorModel,
} from './partner.models';
// `CustomerModel` above is the Business Partner module's own "Customer" role (a different concept that happens
// to share the word) — aliased here so it can sit beside the second FSD's own, independent Customer entity.
import {
  ActivateCustomerInvoiceTemplateRequest,
  ActivationCheckModel,
  ApplicableInvoiceTemplatesModel,
  ChangeCustomerStatusRequest,
  CreateCustomerInvoiceTemplateRequest,
  CustomerBillingAddressModel,
  CustomerBillingConfigurationModel,
  CustomerContactModel,
  CustomerContactSaveResult,
  CustomerHistory,
  CustomerInvoiceTemplateModel,
  CurrencyModel,
  CustomerBalanceModel,
  CustomerModel as TripCustomerModel,
  CustomerPickerItem,
  CustomerTaxRuleModel,
  NewInvoiceTemplateVersionRequest,
  SaveCustomerBillingAddressRequest,
  SaveCustomerBillingConfigurationRequest,
  SaveCustomerContactRequest,
  SaveCustomerRequest,
  SaveCustomerTaxRuleRequest,
  UpdateCustomerTaxRuleRequest,
} from './customer.models';
import { CityModel, SaveCityRequest } from './city.models';
import {
  CancelTripRequest,
  ChangeTripActiveRequest,
  CreateFixedTripRequest,
  CreateOpenTripRequest,
  HoldTripRequest,
  ReopenTripRequest,
  ResumeTripRequest,
  TransitionTripRequest,
  TripHistory,
  TripListItem,
  TripModel,
  TripSearchFilter,
} from './trip.models';
import {
  CreateTripEventRequest,
  CreateTripExpenseRequest,
  CreateTripFuelRequest,
  CreateTripIncomeRequest,
  CreateTripIssueRequest,
  DecideTripExpenseRequest,
  RejectPodRequest,
  ResolveTripIssueRequest,
  TripDocumentModel,
  TripDownloadLinkModel,
  TripEventModel,
  TripExpenseModel,
  TripFuelListModel,
  TripFuelModel,
  TripIncomeModel,
  TripIssueModel,
  TripPnLModel,
  TripPodModel,
  VoidTripExpenseRequest,
  VoidTripFuelRequest,
  VoidTripIncomeRequest,
} from './trip-operations.models';
import {
  CreateRouteRequest,
  RouteModel,
  UpdateRouteRequest,
  UpdateRouteStopsRequest,
} from './route.models';
import {
  AssignTripConfigurationVehicleRequest,
  ChangeTripConfigurationStatusRequest,
  CopyTripConfigurationRequest,
  CreateTripConfigurationRequest,
  InactivateTripRateRequest,
  RateResolutionResult,
  ResolveMissingRatesRequest,
  ResolveMissingRatesResult,
  SaveTripRateRequest,
  SplitTripRateRequest,
  TripConfigurationModel,
  TripConfigurationVehicleModel,
  TripRateModel,
  UpdateTripConfigurationRequest,
  UpdateTripConfigurationStopsRequest,
  UpdateTripConfigurationVehicleRequest,
  UpdateTripRateRequest,
} from './trip-configuration.models';
import {
  AssignFuelCardRequest,
  CreateFuelCardRequest,
  FuelCardAssignmentModel,
  FuelCardModel,
  SaveFuelCardRequest,
} from './fuel-card.models';
import {
  Acquisition,
  ActivateVehicleRequest,
  ActivationCheck,
  Agreement,
  AttachedItem,
  AttachItemRequest,
  BulkConfirmRequest,
  BulkConfirmResult,
  ChangeCategoryRequest,
  ChargeEntryPayment,
  ConfirmChargeEntryRequest,
  DisposeRequest,
  EndRecurringChargeRequest,
  FinancialSummary,
  Installment,
  InstallmentPayment,
  LedgerEntry,
  LinkedVehicle,
  OdometerReading,
  Payable,
  PayablesSummary,
  PayInstallmentRequest,
  RecurringCharge,
  SaveAcquisitionRequest,
  SaveRecurringChargeRequest,
  ScheduleRow,
  SaveFinanceRequest,
  UpdateVehicleRequest,
  Vehicle,
  VehicleHistory,
  VehicleInput,
  VehicleListItem,
  WaiveChargeEntryRequest,
} from './vehicle.models';
import {
  CalendarEntry,
  DocumentOwnerType,
  DocumentSlot,
  DocumentType,
  DownloadLink,
  MissingDocumentRow,
  RegisterQuery,
  RegisterRow,
} from './document.models';
import { AppNotification, NotificationRule, SaveNotificationRuleRequest } from './notification.models';

const api = environment.apiUrl;

/** `/api/vehicles/{id}/documents` for a vehicle, `/api/partners/{id}/documents` for a partner: one engine, two routes. */
const ownerRoute = (ownerType: DocumentOwnerType): string => (ownerType === 'Vehicle' ? 'vehicles' : 'partners');

/**
 * A root-relative path the API returned (e.g. a document download link's `/api/files/download/{token}`) made into a URL
 * the browser can open directly. `environment.apiUrl` is absolute in development (a different origin from the Angular dev
 * server) and same-origin in production (just `/api`), so the origin is taken from it when it has one, the page's own otherwise.
 */
export function apiFileUrl(path: string): string {
  try {
    return new URL(api).origin + path;
  } catch {
    return window.location.origin + path;
  }
}

/** A role the signed-in user is allowed to hand out (see GET /api/users/assignable-roles). */
export interface RoleOption {
  roleId: number;
  name: string;
  roleCode: string;
}

@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);

  list(filter: UserListFilter): Observable<Paged<UserListItem>> {
    let params = new HttpParams().set('page', filter.page).set('pageSize', filter.pageSize);
    if (filter.search) params = params.set('search', filter.search);
    if (filter.status) params = params.set('status', filter.status);
    if (filter.roleId) params = params.set('roleId', filter.roleId);
    return this.http.get<ApiResponse<Paged<UserListItem>>>(`${api}/users`, { params }).pipe(map((r) => r.data));
  }

  assignableRoles(): Observable<RoleOption[]> {
    return this.http.get<ApiResponse<RoleOption[]>>(`${api}/users/assignable-roles`).pipe(map((r) => r.data));
  }

  get(id: number): Observable<UserDetail> {
    return this.http.get<ApiResponse<UserDetail>>(`${api}/users/${id}`).pipe(map((r) => r.data));
  }

  create(body: CreateUserRequest): Observable<UserDetail> {
    return this.http.post<ApiResponse<UserDetail>>(`${api}/users`, body).pipe(map((r) => r.data));
  }

  patch(id: number, body: PatchUserRequest): Observable<unknown> {
    return this.http.patch(`${api}/users/${id}`, body);
  }

  /** Sets the user's one primary role, replacing every role they held (the common single-role case). */
  assignRole(id: number, roleId: number): Observable<unknown> {
    return this.http.put(`${api}/users/${id}/role`, { roleId });
  }

  /** Adds a role alongside the ones the user already holds, each with its own scope (§23B.1). */
  addRole(id: number, body: AddRoleRequest): Observable<unknown> {
    return this.http.post(`${api}/users/${id}/roles`, body);
  }

  /** Removes one role the user holds — refused if it is their only one, or the tenant's last administrator. */
  removeRole(id: number, roleId: number): Observable<unknown> {
    return this.http.delete(`${api}/users/${id}/roles/${roleId}`);
  }

  /** §23B.4: the primary role's data scope. */
  setScope(id: number, body: SetScopeRequest): Observable<unknown> {
    return this.http.put(`${api}/users/${id}/scope`, body);
  }

  /** §23B.1, OQ-20: the Business Partner this user is the same person as, when they are also a driver. */
  setDriverLink(id: number, body: SetDriverLinkRequest): Observable<unknown> {
    return this.http.put(`${api}/users/${id}/driver-link`, body);
  }

  /** §23B.6: "why can they see that?" — the flattened union of every permission the user holds, and which role(s) grant each one. */
  effectivePermissions(id: number): Observable<EffectivePermission[]> {
    return this.http.get<ApiResponse<EffectivePermission[]>>(`${api}/users/${id}/effective-permissions`).pipe(map((r) => r.data));
  }

  resetPassword(id: number): Observable<UserDetail> {
    return this.http.post<ApiResponse<UserDetail>>(`${api}/users/${id}/reset-password`, {}).pipe(map((r) => r.data));
  }

  remove(id: number): Observable<unknown> {
    return this.http.delete(`${api}/users/${id}`);
  }
}

@Injectable({ providedIn: 'root' })
export class RolesApi {
  private readonly http = inject(HttpClient);

  list(): Observable<RoleListItem[]> {
    return this.http.get<ApiResponse<RoleListItem[]>>(`${api}/roles`).pipe(map((r) => r.data));
  }

  get(id: number): Observable<RoleDetail> {
    return this.http.get<ApiResponse<RoleDetail>>(`${api}/roles/${id}`).pipe(map((r) => r.data));
  }

  users(id: number): Observable<RoleUser[]> {
    return this.http.get<ApiResponse<RoleUser[]>>(`${api}/roles/${id}/users`).pipe(map((r) => r.data));
  }

  catalog(): Observable<PermissionGroup[]> {
    return this.http.get<ApiResponse<PermissionGroup[]>>(`${api}/roles/permissions`).pipe(map((r) => r.data));
  }

  create(body: CreateRoleRequest): Observable<RoleDetail> {
    return this.http.post<ApiResponse<RoleDetail>>(`${api}/roles`, body).pipe(map((r) => r.data));
  }

  update(id: number, body: { name: string; description?: string; isActive: boolean }): Observable<unknown> {
    return this.http.put(`${api}/roles/${id}`, body);
  }

  savePermissions(id: number, allowedPermissionIds: number[]): Observable<unknown> {
    return this.http.put(`${api}/roles/${id}/permissions`, { allowedPermissionIds });
  }

  deactivate(id: number): Observable<unknown> {
    return this.http.patch(`${api}/roles/${id}/deactivate`, {});
  }
}

@Injectable({ providedIn: 'root' })
export class TenantsApi {
  private readonly http = inject(HttpClient);

  list(search: string, page: number, pageSize: number): Observable<Paged<TenantListItem>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search) params = params.set('search', search);
    return this.http.get<ApiResponse<Paged<TenantListItem>>>(`${api}/system/tenants`, { params }).pipe(map((r) => r.data));
  }

  get(id: string): Observable<TenantDetail> {
    return this.http.get<ApiResponse<TenantDetail>>(`${api}/system/tenants/${id}`).pipe(map((r) => r.data));
  }

  current(): Observable<TenantDetail> {
    return this.http.get<ApiResponse<TenantDetail>>(`${api}/tenant`).pipe(map((r) => r.data));
  }

  create(body: CreateTenantRequest): Observable<CreateTenantResult> {
    return this.http.post<ApiResponse<CreateTenantResult>>(`${api}/system/tenants`, body).pipe(map((r) => r.data));
  }

  update(id: string, body: Partial<TenantDetail>): Observable<unknown> {
    return this.http.put(`${api}/system/tenants/${id}`, body);
  }

  setStatus(id: string, isActive: boolean): Observable<unknown> {
    return this.http.patch(`${api}/system/tenants/${id}/status`, { isActive });
  }

  /** Stores or replaces one of a tenant's logos (Super Admin). PNG, JPEG or WebP, up to 512 KB. */
  uploadLogo(id: string, variant: LogoVariant, file: File): Observable<unknown> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http.put(`${api}/system/tenants/${id}/logos/${variant}`, body);
  }

  removeLogo(id: string, variant: LogoVariant): Observable<unknown> {
    return this.http.delete(`${api}/system/tenants/${id}/logos/${variant}`);
  }

  /** A tenant's logo as an image (Super Admin). 404 when none is uploaded. */
  logo(id: string, variant: LogoVariant): Observable<Blob> {
    return this.http.get(`${api}/system/tenants/${id}/logos/${variant}`, { responseType: 'blob' });
  }

  /** The signed-in user's own tenant logo. Fetched with the bearer token, so it is never a public URL. */
  currentLogo(variant: LogoVariant): Observable<Blob> {
    return this.http.get(`${api}/tenant/logos/${variant}`, { responseType: 'blob' });
  }
}

@Injectable({ providedIn: 'root' })
export class AccountApi {
  private readonly http = inject(HttpClient);

  me(): Observable<CurrentUser> {
    return this.http.get<ApiResponse<CurrentUser>>(`${api}/auth/me`).pipe(map((r) => r.data));
  }

  changePassword(currentPassword: string, newPassword: string): Observable<unknown> {
    return this.http.put(`${api}/auth/password`, { currentPassword, newPassword });
  }

  forgotPassword(email: string): Observable<unknown> {
    return this.http.post(`${api}/auth/forgot-password`, { email });
  }

  resetPassword(email: string, code: string, newPassword: string): Observable<unknown> {
    return this.http.post(`${api}/auth/reset-password`, { email, code, newPassword });
  }

  acceptInvite(token: string, newPassword: string): Observable<unknown> {
    return this.http.post(`${api}/auth/accept-invite`, { token, newPassword });
  }
}

/** Business partners (`api/partners`). */
@Injectable({ providedIn: 'root' })
export class PartnersApi {
  private readonly http = inject(HttpClient);

  private params(query: ListQuery): HttpParams {
    return new HttpParams({ fromObject: toQueryParams(query) });
  }

  list(query: ListQuery): Observable<Paged<PartnerListItem>> {
    return this.http.get<ApiResponse<Paged<PartnerListItem>>>(`${api}/partners`, { params: this.params(query) }).pipe(map((r) => r.data));
  }

  /** The list as an Excel file: everything the filters match, not just the page on screen. */
  export(query: ListQuery): Observable<{ blob: Blob; fileName: string }> {
    // Paging is ignored by the export, so the whole result comes back whatever page the screen is on.
    return this.http.get(`${api}/partners/export`, { params: this.params(query), responseType: 'blob', observe: 'response' }).pipe(
      map((response) => {
        const disposition = response.headers.get('Content-Disposition') ?? '';
        const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1];
        return { blob: response.body as Blob, fileName: name ? decodeURIComponent(name) : 'business-partners.xlsx' };
      }),
    );
  }

  picker(role: string | null, search = '', take = 50): Observable<PartnerPickerItem[]> {
    let params = new HttpParams().set('take', take);
    if (role) params = params.set('role', role);
    if (search.trim()) params = params.set('search', search.trim());
    return this.http.get<ApiResponse<PartnerPickerItem[]>>(`${api}/partners/picker`, { params }).pipe(map((r) => r.data));
  }

  get(id: number): Observable<Partner> {
    return this.http.get<ApiResponse<Partner>>(`${api}/partners/${id}`).pipe(map((r) => r.data));
  }

  create(body: CreatePartnerRequest): Observable<Partner> {
    return this.http.post<ApiResponse<Partner>>(`${api}/partners`, body).pipe(map((r) => r.data));
  }

  update(id: number, body: UpdatePartnerRequest): Observable<Partner> {
    return this.http.put<ApiResponse<Partner>>(`${api}/partners/${id}`, body).pipe(map((r) => r.data));
  }

  duplicateCheck(body: DuplicateCheckRequest): Observable<DuplicateMatch[]> {
    return this.http.post<ApiResponse<{ matches: DuplicateMatch[] }>>(`${api}/partners/duplicate-check`, body).pipe(map((r) => r.data.matches));
  }

  addRole(id: number, body: { roleCode: string; reason?: string | null; driver?: DriverModel | null; vendor?: VendorModel | null; customer?: CustomerModel | null }): Observable<Partner> {
    return this.http.post<ApiResponse<Partner>>(`${api}/partners/${id}/roles`, body).pipe(map((r) => r.data));
  }

  removeRole(id: number, roleCode: string, reason: string | null): Observable<Partner> {
    let params = new HttpParams();
    if (reason) params = params.set('reason', reason);
    return this.http.delete<ApiResponse<Partner>>(`${api}/partners/${id}/roles/${roleCode}`, { params }).pipe(map((r) => r.data));
  }

  changeStatus(id: number, body: { status: string; reason?: string | null; effectiveDate?: string | null }): Observable<Partner> {
    return this.http.post<ApiResponse<Partner>>(`${api}/partners/${id}/status`, body).pipe(map((r) => r.data));
  }

  usage(id: number, intent: 'RemoveRole' | 'Deactivate' | 'ChangePartyType', role?: string): Observable<PartnerUsage[]> {
    let params = new HttpParams().set('intent', intent);
    if (role) params = params.set('role', role);
    return this.http.get<ApiResponse<PartnerUsage[]>>(`${api}/partners/${id}/usage`, { params }).pipe(map((r) => r.data));
  }

  history(id: number, page = 1, pageSize = 50): Observable<PartnerHistory> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<ApiResponse<PartnerHistory>>(`${api}/partners/${id}/history`, { params }).pipe(map((r) => r.data));
  }
}

/** Vehicles (`api/vehicles`). */
@Injectable({ providedIn: 'root' })
export class VehiclesApi {
  private readonly http = inject(HttpClient);

  list(query: ListQuery): Observable<Paged<VehicleListItem>> {
    return this.http.get<ApiResponse<Paged<VehicleListItem>>>(`${api}/vehicles`, { params: new HttpParams({ fromObject: toQueryParams(query) }) }).pipe(map((r) => r.data));
  }

  /** The list as an Excel file: everything the filters match, not just the page on screen. */
  export(query: ListQuery): Observable<{ blob: Blob; fileName: string }> {
    return this.http.get(`${api}/vehicles/export`, { params: new HttpParams({ fromObject: toQueryParams(query) }), responseType: 'blob', observe: 'response' }).pipe(
      map((response) => {
        const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(response.headers.get('Content-Disposition') ?? '')?.[1];
        return { blob: response.body as Blob, fileName: name ? decodeURIComponent(name) : 'vehicles.xlsx' };
      }),
    );
  }

  get(id: number): Observable<Vehicle> {
    return this.http.get<ApiResponse<Vehicle>>(`${api}/vehicles/${id}`).pipe(map((r) => r.data));
  }

  create(body: VehicleInput): Observable<Vehicle> {
    return this.http.post<ApiResponse<Vehicle>>(`${api}/vehicles`, body).pipe(map((r) => r.data));
  }

  update(id: number, body: UpdateVehicleRequest): Observable<Vehicle> {
    return this.http.put<ApiResponse<Vehicle>>(`${api}/vehicles/${id}`, body).pipe(map((r) => r.data));
  }

  acquisition(id: number): Observable<Acquisition> {
    return this.http.get<ApiResponse<Acquisition>>(`${api}/vehicles/${id}/acquisition`).pipe(map((r) => r.data));
  }

  saveAcquisition(id: number, body: SaveAcquisitionRequest): Observable<Acquisition> {
    return this.http.put<ApiResponse<Acquisition>>(`${api}/vehicles/${id}/acquisition`, body).pipe(map((r) => r.data));
  }

  /** The vehicle's open finance agreement, or null when it has none. */
  finance(id: number): Observable<Agreement | null> {
    return this.http.get<ApiResponse<Agreement | null>>(`${api}/vehicles/${id}/finance`).pipe(map((r) => r.data ?? null));
  }

  saveFinance(id: number, body: SaveFinanceRequest): Observable<Agreement> {
    return this.http.put<ApiResponse<Agreement>>(`${api}/vehicles/${id}/finance`, body).pipe(map((r) => r.data));
  }

  removeFinance(id: number): Observable<void> {
    return this.http.delete<ApiResponse<unknown>>(`${api}/vehicles/${id}/finance`).pipe(map(() => undefined));
  }
  /** The schedule a Draft's saved agreement would generate. */
  schedulePreview(id: number): Observable<ScheduleRow[]> {
    return this.http.get<ApiResponse<ScheduleRow[]>>(`${api}/vehicles/${id}/finance/schedule`).pipe(map((r) => r.data));
  }

  /** What is missing before a Draft can be activated, and what activating would write. Writes nothing. */
  activationCheck(id: number, body: ActivateVehicleRequest): Observable<ActivationCheck> {
    return this.http.post<ApiResponse<ActivationCheck>>(`${api}/vehicles/${id}/activation-check`, body).pipe(map((r) => r.data));
  }

  activate(id: number, body: ActivateVehicleRequest): Observable<Vehicle> {
    return this.http.post<ApiResponse<Vehicle>>(`${api}/vehicles/${id}/activate`, body).pipe(map((r) => r.data));
  }

  financialSummary(id: number): Observable<FinancialSummary> {
    return this.http.get<ApiResponse<FinancialSummary>>(`${api}/vehicles/${id}/financial-summary`).pipe(map((r) => r.data));
  }

  installments(id: number): Observable<Installment[]> {
    return this.http.get<ApiResponse<Installment[]>>(`${api}/vehicles/${id}/installments`).pipe(map((r) => r.data));
  }

  payInstallment(id: number, installmentId: number, body: PayInstallmentRequest): Observable<InstallmentPayment> {
    return this.http.post<ApiResponse<InstallmentPayment>>(`${api}/vehicles/${id}/installments/${installmentId}/payments`, body).pipe(map((r) => r.data));
  }

  transactions(id: number): Observable<LedgerEntry[]> {
    return this.http.get<ApiResponse<LedgerEntry[]>>(`${api}/vehicles/${id}/transactions`).pipe(map((r) => r.data));
  }

  reverseTransaction(id: number, transactionId: number, body: { reason: string; date?: string | null }): Observable<LedgerEntry> {
    return this.http.post<ApiResponse<LedgerEntry>>(`${api}/vehicles/${id}/transactions/${transactionId}/reverse`, body).pipe(map((r) => r.data));
  }

  /** Where a receipt for an installment payment is uploaded (source §7), for `uploadFile`. */
  receiptUploadUrl(id: number, installmentId: number, transactionId: number): string {
    return `${api}/vehicles/${id}/installments/${installmentId}/payments/${transactionId}/receipt`;
  }

  /** The receipt attached to a ledger entry, as a file to save. */
  downloadReceipt(id: number, transactionId: number): Observable<{ blob: Blob; fileName: string }> {
    return this.http.get(`${api}/vehicles/${id}/transactions/${transactionId}/receipt`, { responseType: 'blob', observe: 'response' }).pipe(
      map((response) => {
        const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(response.headers.get('Content-Disposition') ?? '')?.[1];
        return { blob: response.body as Blob, fileName: name ? decodeURIComponent(name) : 'receipt' };
      }),
    );
  }
  changeStatus(id: number, body: { status: string; reason?: string | null; effectiveDate?: string | null }): Observable<Vehicle> {
    return this.http.post<ApiResponse<Vehicle>>(`${api}/vehicles/${id}/status`, body).pipe(map((r) => r.data));
  }

  changeCategory(id: number, body: ChangeCategoryRequest): Observable<Vehicle> {
    return this.http.post<ApiResponse<Vehicle>>(`${api}/vehicles/${id}/category`, body).pipe(map((r) => r.data));
  }

  dispose(id: number, body: DisposeRequest): Observable<Vehicle> {
    return this.http.post<ApiResponse<Vehicle>>(`${api}/vehicles/${id}/dispose`, body).pipe(map((r) => r.data));
  }

  history(id: number, page = 1, pageSize = 50): Observable<VehicleHistory> {
    return this.http.get<ApiResponse<VehicleHistory>>(`${api}/vehicles/${id}/history`, { params: new HttpParams().set('page', page).set('pageSize', pageSize) }).pipe(map((r) => r.data));
  }

  assignDriver(id: number, body: { driverId: number; effectiveDate?: string | null; releaseFromOther?: boolean }): Observable<Vehicle> {
    return this.http.post<ApiResponse<Vehicle>>(`${api}/vehicles/${id}/driver`, body).pipe(map((r) => r.data));
  }

  releaseDriver(id: number, date?: string | null, reason?: string | null): Observable<Vehicle> {
    let params = new HttpParams();
    if (date) params = params.set('date', date);
    if (reason) params = params.set('reason', reason);
    return this.http.delete<ApiResponse<Vehicle>>(`${api}/vehicles/${id}/driver`, { params }).pipe(map((r) => r.data));
  }

  items(id: number, includeGone = false): Observable<AttachedItem[]> {
    return this.http.get<ApiResponse<AttachedItem[]>>(`${api}/vehicles/${id}/items`, { params: new HttpParams().set('includeGone', includeGone) }).pipe(map((r) => r.data));
  }

  attach(id: number, body: AttachItemRequest): Observable<AttachedItem> {
    return this.http.post<ApiResponse<AttachedItem>>(`${api}/vehicles/${id}/items`, body).pipe(map((r) => r.data));
  }

  detach(id: number, itemId: number, body: { date?: string | null; reason: string }): Observable<AttachedItem> {
    return this.http.post<ApiResponse<AttachedItem>>(`${api}/vehicles/${id}/items/${itemId}/detach`, body).pipe(map((r) => r.data));
  }

  transfer(id: number, itemId: number, body: { targetVehicleId: number; date?: string | null; reason?: string | null }): Observable<AttachedItem> {
    return this.http.post<ApiResponse<AttachedItem>>(`${api}/vehicles/${id}/items/${itemId}/transfer`, body).pipe(map((r) => r.data));
  }

  odometer(id: number): Observable<OdometerReading[]> {
    return this.http.get<ApiResponse<OdometerReading[]>>(`${api}/vehicles/${id}/odometer`).pipe(map((r) => r.data));
  }

  addOdometer(id: number, body: { km: number; readingDate?: string | null; notes?: string | null }): Observable<OdometerReading> {
    return this.http.post<ApiResponse<OdometerReading>>(`${api}/vehicles/${id}/odometer`, body).pipe(map((r) => r.data));
  }

  /** The vehicles a partner is linked to (for the partner screen). */
  linkedTo(partnerId: number): Observable<LinkedVehicle[]> {
    return this.http.get<ApiResponse<LinkedVehicle[]>>(`${api}/partners/${partnerId}/vehicles`).pipe(map((r) => r.data));
  }

  // ── Recurring charges (FSD §19A.1) ─────────────────────────────────────────────

  recurringCharges(id: number): Observable<RecurringCharge[]> {
    return this.http.get<ApiResponse<RecurringCharge[]>>(`${api}/vehicles/${id}/recurring-charges`).pipe(map((r) => r.data));
  }

  createRecurringCharge(id: number, body: SaveRecurringChargeRequest): Observable<RecurringCharge> {
    return this.http.post<ApiResponse<RecurringCharge>>(`${api}/vehicles/${id}/recurring-charges`, body).pipe(map((r) => r.data));
  }

  /** Amends the charge: the server ends the current row and starts a new one from the effective date (BR-VH-033). */
  amendRecurringCharge(id: number, chargeId: number, body: SaveRecurringChargeRequest): Observable<RecurringCharge> {
    return this.http.put<ApiResponse<RecurringCharge>>(`${api}/vehicles/${id}/recurring-charges/${chargeId}`, body).pipe(map((r) => r.data));
  }

  endRecurringCharge(id: number, chargeId: number, body: EndRecurringChargeRequest): Observable<RecurringCharge> {
    return this.http.post<ApiResponse<RecurringCharge>>(`${api}/vehicles/${id}/recurring-charges/${chargeId}/end`, body).pipe(map((r) => r.data));
  }

  /** This vehicle's Due and Overdue items — its recurring charge entries and its unpaid installments together (BR-VH-029). */
  payables(id: number): Observable<Payable[]> {
    return this.http.get<ApiResponse<Payable[]>>(`${api}/vehicles/${id}/payables`).pipe(map((r) => r.data));
  }

  confirmChargeEntry(id: number, entryId: number, body: ConfirmChargeEntryRequest): Observable<ChargeEntryPayment> {
    return this.http.post<ApiResponse<ChargeEntryPayment>>(`${api}/vehicles/${id}/recurring-charges/entries/${entryId}/confirm`, body).pipe(map((r) => r.data));
  }

  waiveChargeEntry(id: number, entryId: number, body: WaiveChargeEntryRequest): Observable<unknown> {
    return this.http.post(`${api}/vehicles/${id}/recurring-charges/entries/${entryId}/waive`, body);
  }

  cancelChargeEntry(id: number, entryId: number, body: WaiveChargeEntryRequest): Observable<unknown> {
    return this.http.post(`${api}/vehicles/${id}/recurring-charges/entries/${entryId}/cancel`, body);
  }
}

/** The Payables Due workbench: fleet-wide Due and Overdue items across every vehicle and charge type (`api/payables`, §19A.5). */
@Injectable({ providedIn: 'root' })
export class PayablesApi {
  private readonly http = inject(HttpClient);

  list(filters: { chargeTypeId?: number | null; payeeId?: number | null; branchId?: string | null; dueFrom?: string | null; dueTo?: string | null; overdueOnly?: boolean } = {}): Observable<Payable[]> {
    let params = new HttpParams();
    if (filters.chargeTypeId) params = params.set('chargeTypeId', filters.chargeTypeId);
    if (filters.payeeId) params = params.set('payeeId', filters.payeeId);
    if (filters.branchId) params = params.set('branchId', filters.branchId);
    if (filters.dueFrom) params = params.set('dueFrom', filters.dueFrom);
    if (filters.dueTo) params = params.set('dueTo', filters.dueTo);
    if (filters.overdueOnly) params = params.set('overdueOnly', true);
    return this.http.get<ApiResponse<Payable[]>>(`${api}/payables`, { params }).pipe(map((r) => r.data));
  }

  bulkConfirm(body: BulkConfirmRequest): Observable<BulkConfirmResult> {
    return this.http.post<ApiResponse<BulkConfirmResult>>(`${api}/payables/bulk-confirm`, body).pipe(map((r) => r.data));
  }

  /** The dashboard tile: due within 7 days and overdue, fleet-wide. */
  summary(): Observable<PayablesSummary> {
    return this.http.get<ApiResponse<PayablesSummary>>(`${api}/payables/summary`).pipe(map((r) => r.data));
  }
}

/** Documents for a vehicle or a partner (FSD §23A): the type master, the slots screen, upload/renew URLs, reject, download, and the fleet-wide read APIs. */
@Injectable({ providedIn: 'root' })
export class DocumentsApi {
  private readonly http = inject(HttpClient);

  /** The tenant's document types (seeded lazily the first time they are asked for). */
  types(appliesTo?: DocumentOwnerType): Observable<DocumentType[]> {
    let params = new HttpParams();
    if (appliesTo) params = params.set('appliesTo', appliesTo);
    return this.http.get<ApiResponse<DocumentType[]>>(`${api}/documents/types`, { params }).pipe(map((r) => r.data));
  }

  /** One owner's documents, a slot per applicable type: its current version (if any), and history on request. */
  slots(ownerType: DocumentOwnerType, ownerId: number, includeHistory = false): Observable<DocumentSlot[]> {
    return this.http.get<ApiResponse<DocumentSlot[]>>(`${api}/${ownerRoute(ownerType)}/${ownerId}/documents`, { params: new HttpParams().set('includeHistory', includeHistory) }).pipe(map((r) => r.data));
  }

  /** Where a new document goes for this owner and type, for `uploadFile` (extra field `documentTypeId`). */
  uploadUrl(ownerType: DocumentOwnerType, ownerId: number): string {
    return `${api}/${ownerRoute(ownerType)}/${ownerId}/documents`;
  }

  /** Where a renewal of this owner's slot goes, for `uploadFile`. */
  renewUrl(ownerType: DocumentOwnerType, ownerId: number, documentTypeId: number): string {
    return `${api}/${ownerRoute(ownerType)}/${ownerId}/documents/${documentTypeId}/renew`;
  }

  reject(documentId: number, reason: string): Observable<unknown> {
    return this.http.post(`${api}/documents/${documentId}/reject`, { reason });
  }

  /** A short-lived, authorised link to the file (never the storage path); open its `url` directly, no bearer token needed. */
  downloadLink(documentId: number): Observable<DownloadLink> {
    return this.http.post<ApiResponse<DownloadLink>>(`${api}/documents/${documentId}/download-link`, {}).pipe(map((r) => r.data));
  }

  /** The fleet-wide/partner-wide Document Register (§23A.4), filtered. */
  register(query: RegisterQuery): Observable<RegisterRow[]> {
    let params = new HttpParams();
    if (query.ownerType) params = params.set('ownerType', query.ownerType);
    if (query.documentTypeId) params = params.set('documentTypeId', query.documentTypeId);
    if (query.status) params = params.set('status', query.status);
    if (query.expiringWithinDays) params = params.set('expiringWithinDays', query.expiringWithinDays);
    return this.http.get<ApiResponse<RegisterRow[]>>(`${api}/documents/register`, { params }).pipe(map((r) => r.data));
  }

  /** Every owner missing a mandatory or periodic document, across one or both owner types. */
  missing(ownerType?: DocumentOwnerType): Observable<MissingDocumentRow[]> {
    let params = new HttpParams();
    if (ownerType) params = params.set('ownerType', ownerType);
    return this.http.get<ApiResponse<MissingDocumentRow[]>>(`${api}/documents/missing`, { params }).pipe(map((r) => r.data));
  }

  /** Everything due to expire within the given month, for the Expiry Calendar. */
  calendar(year: number, month: number): Observable<CalendarEntry[]> {
    return this.http.get<ApiResponse<CalendarEntry[]>>(`${api}/documents/expiry-calendar`, { params: new HttpParams().set('year', year).set('month', month) }).pipe(map((r) => r.data));
  }
}

/**
 * Master lists (Vehicle Type, Make, City, …). `active` is what every dropdown uses; the rest is the
 * Administration → Master data screen. Filter a list by one of its fields to cascade dropdowns:
 * `active('CITY', { province: 'PUNJAB' })`.
 */
@Injectable({ providedIn: 'root' })
export class LookupsApi {
  private readonly http = inject(HttpClient);

  active(type: string, filter: Record<string, string> = {}): Observable<LookupItem[]> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) params = params.set(key, value);
    return this.http.get<ApiResponse<LookupItem[]>>(`${api}/lookups/${type}`, { params }).pipe(map((r) => r.data));
  }

  /** One value, retired or not: to label the value a record was saved with even after it was retired. */
  one(type: string, id: number): Observable<LookupItem> {
    return this.http.get<ApiResponse<LookupItem>>(`${api}/lookups/${type}/${id}`).pipe(map((r) => r.data));
  }

  types(): Observable<LookupType[]> {
    return this.http.get<ApiResponse<LookupType[]>>(`${api}/admin/lookups`).pipe(map((r) => r.data));
  }

  values(type: string): Observable<LookupItem[]> {
    return this.http.get<ApiResponse<LookupItem[]>>(`${api}/admin/lookups/${type}`).pipe(map((r) => r.data));
  }

  create(type: string, body: SaveLookupRequest): Observable<LookupItem> {
    return this.http.post<ApiResponse<LookupItem>>(`${api}/admin/lookups/${type}`, body).pipe(map((r) => r.data));
  }

  update(type: string, id: number, body: SaveLookupRequest): Observable<LookupItem> {
    return this.http.put<ApiResponse<LookupItem>>(`${api}/admin/lookups/${type}/${id}`, body).pipe(map((r) => r.data));
  }
}

/** The notification rule master (FSD §9), configurable under Administration (S7-NOT-05). */
@Injectable({ providedIn: 'root' })
export class NotificationRulesApi {
  private readonly http = inject(HttpClient);

  list(): Observable<NotificationRule[]> {
    return this.http.get<ApiResponse<NotificationRule[]>>(`${api}/admin/notification-rules`).pipe(map((r) => r.data));
  }

  update(id: number, body: SaveNotificationRuleRequest): Observable<NotificationRule> {
    return this.http.put<ApiResponse<NotificationRule>>(`${api}/admin/notification-rules/${id}`, body).pipe(map((r) => r.data));
  }
}

/** The signed-in user's own in-app notification list (S7-NOT-06, FSD §23.4): the shell's bell icon and its dropdown. */
@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly http = inject(HttpClient);

  list(unreadOnly = false): Observable<AppNotification[]> {
    return this.http.get<ApiResponse<AppNotification[]>>(`${api}/notifications`, { params: new HttpParams().set('unreadOnly', unreadOnly) }).pipe(map((r) => r.data));
  }

  unreadCount(): Observable<number> {
    return this.http.get<ApiResponse<{ count: number }>>(`${api}/notifications/unread-count`).pipe(map((r) => r.data.count));
  }

  markRead(id: number): Observable<unknown> {
    return this.http.post(`${api}/notifications/${id}/read`, {});
  }

  markAllRead(): Observable<unknown> {
    return this.http.post(`${api}/notifications/read-all`, {});
  }
}

/** The tenant's own locations (FSD §6 field 15). A tenant starts with one, "Head Office" (OQ-10), made the first time this is asked for. */
@Injectable({ providedIn: 'root' })
export class BranchesApi {
  private readonly http = inject(HttpClient);

  list(): Observable<Branch[]> {
    return this.http.get<ApiResponse<Branch[]>>(`${api}/branches`).pipe(map((r) => r.data));
  }
}

/**
 * The Customer master and its child records (`api/customers`, FSD §10-§15, CC-43). No `export` method — the
 * backend has no `/customers/export` endpoint (unlike Partners/Vehicles), so the List screen (§48.2 screen 1)
 * leaves that button out rather than pointing it at a route that doesn't exist.
 */
@Injectable({ providedIn: 'root' })
export class CustomersApi {
  private readonly http = inject(HttpClient);

  private params(query: ListQuery): HttpParams {
    return new HttpParams({ fromObject: toQueryParams(query) });
  }

  list(query: ListQuery): Observable<Paged<TripCustomerModel>> {
    return this.http.get<ApiResponse<Paged<TripCustomerModel>>>(`${api}/customers`, { params: this.params(query) }).pipe(map((r) => r.data));
  }

  picker(search = ''): Observable<CustomerPickerItem[]> {
    let params = new HttpParams();
    if (search.trim()) params = params.set('search', search.trim());
    return this.http.get<ApiResponse<CustomerPickerItem[]>>(`${api}/customers/picker`, { params }).pipe(map((r) => r.data));
  }

  get(id: number): Observable<TripCustomerModel> {
    return this.http.get<ApiResponse<TripCustomerModel>>(`${api}/customers/${id}`).pipe(map((r) => r.data));
  }

  create(body: SaveCustomerRequest): Observable<TripCustomerModel> {
    return this.http.post<ApiResponse<TripCustomerModel>>(`${api}/customers`, body).pipe(map((r) => r.data));
  }

  update(id: number, body: SaveCustomerRequest): Observable<TripCustomerModel> {
    return this.http.put<ApiResponse<TripCustomerModel>>(`${api}/customers/${id}`, body).pipe(map((r) => r.data));
  }

  activationCheck(id: number): Observable<ActivationCheckModel> {
    return this.http.get<ApiResponse<ActivationCheckModel>>(`${api}/customers/${id}/activation-check`).pipe(map((r) => r.data));
  }

  activate(id: number, body: ChangeCustomerStatusRequest): Observable<TripCustomerModel> {
    return this.http.post<ApiResponse<TripCustomerModel>>(`${api}/customers/${id}/activate`, body).pipe(map((r) => r.data));
  }

  deactivate(id: number, body: ChangeCustomerStatusRequest): Observable<TripCustomerModel> {
    return this.http.post<ApiResponse<TripCustomerModel>>(`${api}/customers/${id}/deactivate`, body).pipe(map((r) => r.data));
  }

  reactivate(id: number, body: ChangeCustomerStatusRequest): Observable<TripCustomerModel> {
    return this.http.post<ApiResponse<TripCustomerModel>>(`${api}/customers/${id}/reactivate`, body).pipe(map((r) => r.data));
  }

  delete(id: number, body: ChangeCustomerStatusRequest): Observable<unknown> {
    return this.http.delete(`${api}/customers/${id}`, { body });
  }

  history(id: number, page = 1, pageSize = 50): Observable<CustomerHistory> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<ApiResponse<CustomerHistory>>(`${api}/customers/${id}/history`, { params }).pipe(map((r) => r.data));
  }

  /** §40A (CC-38): per-currency running balance — used for the balance figure on the List and Details screens. */
  balance(id: number): Observable<CustomerBalanceModel[]> {
    return this.http.get<ApiResponse<CustomerBalanceModel[]>>(`${api}/customers/${id}/balance`).pipe(map((r) => r.data));
  }

  // ── Contacts (§11) ───────────────────────────────────────────────────────────────

  contacts(customerId: number, includeInactive = false): Observable<CustomerContactModel[]> {
    const params = new HttpParams().set('includeInactive', includeInactive);
    return this.http.get<ApiResponse<CustomerContactModel[]>>(`${api}/customers/${customerId}/contacts`, { params }).pipe(map((r) => r.data));
  }

  createContact(customerId: number, body: SaveCustomerContactRequest): Observable<CustomerContactSaveResult> {
    return this.http.post<ApiResponse<CustomerContactSaveResult>>(`${api}/customers/${customerId}/contacts`, body).pipe(map((r) => r.data));
  }

  updateContact(contactId: number, body: SaveCustomerContactRequest): Observable<CustomerContactSaveResult> {
    return this.http.put<ApiResponse<CustomerContactSaveResult>>(`${api}/customer-contacts/${contactId}`, body).pipe(map((r) => r.data));
  }

  deactivateContact(contactId: number): Observable<CustomerContactSaveResult> {
    return this.http.post<ApiResponse<CustomerContactSaveResult>>(`${api}/customer-contacts/${contactId}/deactivate`, {}).pipe(map((r) => r.data));
  }

  activateContact(contactId: number): Observable<CustomerContactSaveResult> {
    return this.http.post<ApiResponse<CustomerContactSaveResult>>(`${api}/customer-contacts/${contactId}/activate`, {}).pipe(map((r) => r.data));
  }

  // ── Billing addresses (§12) ──────────────────────────────────────────────────────

  billingAddresses(customerId: number, includeInactive = false): Observable<CustomerBillingAddressModel[]> {
    const params = new HttpParams().set('includeInactive', includeInactive);
    return this.http.get<ApiResponse<CustomerBillingAddressModel[]>>(`${api}/customers/${customerId}/billing-addresses`, { params }).pipe(map((r) => r.data));
  }

  createBillingAddress(customerId: number, body: SaveCustomerBillingAddressRequest): Observable<CustomerBillingAddressModel> {
    return this.http.post<ApiResponse<CustomerBillingAddressModel>>(`${api}/customers/${customerId}/billing-addresses`, body).pipe(map((r) => r.data));
  }

  updateBillingAddress(addressId: number, body: SaveCustomerBillingAddressRequest): Observable<CustomerBillingAddressModel> {
    return this.http.put<ApiResponse<CustomerBillingAddressModel>>(`${api}/customer-billing-addresses/${addressId}`, body).pipe(map((r) => r.data));
  }

  setDefaultBillingAddress(addressId: number): Observable<CustomerBillingAddressModel> {
    return this.http.post<ApiResponse<CustomerBillingAddressModel>>(`${api}/customer-billing-addresses/${addressId}/set-default`, {}).pipe(map((r) => r.data));
  }

  deactivateBillingAddress(addressId: number): Observable<CustomerBillingAddressModel> {
    return this.http.post<ApiResponse<CustomerBillingAddressModel>>(`${api}/customer-billing-addresses/${addressId}/deactivate`, {}).pipe(map((r) => r.data));
  }

  activateBillingAddress(addressId: number): Observable<CustomerBillingAddressModel> {
    return this.http.post<ApiResponse<CustomerBillingAddressModel>>(`${api}/customer-billing-addresses/${addressId}/activate`, {}).pipe(map((r) => r.data));
  }

  // ── Billing configuration (§13) ──────────────────────────────────────────────────

  billingConfiguration(customerId: number, asOf?: string | null): Observable<CustomerBillingConfigurationModel> {
    let params = new HttpParams();
    if (asOf) params = params.set('asOf', asOf);
    return this.http.get<ApiResponse<CustomerBillingConfigurationModel>>(`${api}/customers/${customerId}/billing-configuration`, { params }).pipe(map((r) => r.data));
  }

  saveBillingConfiguration(customerId: number, body: SaveCustomerBillingConfigurationRequest): Observable<CustomerBillingConfigurationModel> {
    return this.http.put<ApiResponse<CustomerBillingConfigurationModel>>(`${api}/customers/${customerId}/billing-configuration`, body).pipe(map((r) => r.data));
  }

  // ── Tax / deduction rules (§14) ───────────────────────────────────────────────────

  taxRules(customerId: number, includeInactive = false): Observable<CustomerTaxRuleModel[]> {
    const params = new HttpParams().set('includeInactive', includeInactive);
    return this.http.get<ApiResponse<CustomerTaxRuleModel[]>>(`${api}/customers/${customerId}/tax-rules`, { params }).pipe(map((r) => r.data));
  }

  createTaxRule(customerId: number, body: SaveCustomerTaxRuleRequest): Observable<CustomerTaxRuleModel> {
    return this.http.post<ApiResponse<CustomerTaxRuleModel>>(`${api}/customers/${customerId}/tax-rules`, body).pipe(map((r) => r.data));
  }

  updateTaxRule(ruleId: number, body: UpdateCustomerTaxRuleRequest): Observable<CustomerTaxRuleModel> {
    return this.http.put<ApiResponse<CustomerTaxRuleModel>>(`${api}/customer-tax-rules/${ruleId}`, body).pipe(map((r) => r.data));
  }

  replaceTaxRule(ruleId: number, body: SaveCustomerTaxRuleRequest): Observable<CustomerTaxRuleModel> {
    return this.http.post<ApiResponse<CustomerTaxRuleModel>>(`${api}/customer-tax-rules/${ruleId}/replace`, body).pipe(map((r) => r.data));
  }

  // ── Invoice templates (§15) ───────────────────────────────────────────────────────

  invoiceTemplates(customerId: number, includeInactive = false): Observable<CustomerInvoiceTemplateModel[]> {
    const params = new HttpParams().set('includeInactive', includeInactive);
    return this.http.get<ApiResponse<CustomerInvoiceTemplateModel[]>>(`${api}/customers/${customerId}/invoice-templates`, { params }).pipe(map((r) => r.data));
  }

  applicableInvoiceTemplates(customerId: number, invoiceDate: string): Observable<ApplicableInvoiceTemplatesModel> {
    const params = new HttpParams().set('invoiceDate', invoiceDate);
    return this.http.get<ApiResponse<ApplicableInvoiceTemplatesModel>>(`${api}/customers/${customerId}/invoice-templates/applicable`, { params }).pipe(map((r) => r.data));
  }

  createInvoiceTemplate(customerId: number, body: CreateCustomerInvoiceTemplateRequest): Observable<CustomerInvoiceTemplateModel> {
    return this.http.post<ApiResponse<CustomerInvoiceTemplateModel>>(`${api}/customers/${customerId}/invoice-templates`, body).pipe(map((r) => r.data));
  }

  newInvoiceTemplateVersion(templateId: number, body: NewInvoiceTemplateVersionRequest): Observable<CustomerInvoiceTemplateModel> {
    return this.http.post<ApiResponse<CustomerInvoiceTemplateModel>>(`${api}/customer-invoice-templates/${templateId}/new-version`, body).pipe(map((r) => r.data));
  }

  activateInvoiceTemplate(templateId: number, body: ActivateCustomerInvoiceTemplateRequest): Observable<CustomerInvoiceTemplateModel> {
    return this.http.post<ApiResponse<CustomerInvoiceTemplateModel>>(`${api}/customer-invoice-templates/${templateId}/activate`, body).pipe(map((r) => r.data));
  }

  setDefaultInvoiceTemplate(templateId: number): Observable<CustomerInvoiceTemplateModel> {
    return this.http.post<ApiResponse<CustomerInvoiceTemplateModel>>(`${api}/customer-invoice-templates/${templateId}/set-default`, {}).pipe(map((r) => r.data));
  }

  deactivateInvoiceTemplate(templateId: number): Observable<CustomerInvoiceTemplateModel> {
    return this.http.post<ApiResponse<CustomerInvoiceTemplateModel>>(`${api}/customer-invoice-templates/${templateId}/deactivate`, {}).pipe(map((r) => r.data));
  }
}

/** §13A's own currency master (`api/currencies`) — `TRP.CURRENCY.MANAGE` only (Admin, §48.8's Currency Setup
 * screen); see {@link CurrencyModel}'s own doc comment for why a customer's currency field degrades gracefully
 * rather than this task widening that gate. */
@Injectable({ providedIn: 'root' })
export class CurrenciesApi {
  private readonly http = inject(HttpClient);

  list(): Observable<CurrencyModel[]> {
    return this.http.get<ApiResponse<CurrencyModel[]>>(`${api}/currencies`).pipe(map((r) => r.data));
  }
}

/** The city master (FSD §16, §48.3 screen 8) — `TRP.CITY.EDIT` to write, a valid sign-in alone to view (the
 * same as the platform's own lookups). */
@Injectable({ providedIn: 'root' })
export class CitiesApi {
  private readonly http = inject(HttpClient);

  list(includeInactive = false): Observable<CityModel[]> {
    return this.http.get<ApiResponse<CityModel[]>>(`${api}/cities`, { params: new HttpParams().set('includeInactive', includeInactive) }).pipe(map((r) => r.data));
  }

  create(body: SaveCityRequest): Observable<CityModel> {
    return this.http.post<ApiResponse<CityModel>>(`${api}/cities`, body).pipe(map((r) => r.data));
  }

  update(cityId: number, body: SaveCityRequest): Observable<CityModel> {
    return this.http.put<ApiResponse<CityModel>>(`${api}/cities/${cityId}`, body).pipe(map((r) => r.data));
  }
}

/** Routes and route stops (FSD §17, §48.3 screen 9) — `TRP.ROUTE.EDIT` for everything, view included (unlike
 * City, there is no separate "all view" row). */
@Injectable({ providedIn: 'root' })
export class RoutesApi {
  private readonly http = inject(HttpClient);

  list(includeInactive = false): Observable<RouteModel[]> {
    return this.http.get<ApiResponse<RouteModel[]>>(`${api}/routes`, { params: new HttpParams().set('includeInactive', includeInactive) }).pipe(map((r) => r.data));
  }

  get(routeId: number): Observable<RouteModel> {
    return this.http.get<ApiResponse<RouteModel>>(`${api}/routes/${routeId}`).pipe(map((r) => r.data));
  }

  create(body: CreateRouteRequest): Observable<RouteModel> {
    return this.http.post<ApiResponse<RouteModel>>(`${api}/routes`, body).pipe(map((r) => r.data));
  }

  update(routeId: number, body: UpdateRouteRequest): Observable<RouteModel> {
    return this.http.put<ApiResponse<RouteModel>>(`${api}/routes/${routeId}`, body).pipe(map((r) => r.data));
  }

  updateStops(routeId: number, body: UpdateRouteStopsRequest): Observable<RouteModel> {
    return this.http.put<ApiResponse<RouteModel>>(`${api}/routes/${routeId}/stops`, body).pipe(map((r) => r.data));
  }
}

/** Trip configurations, their stops and vehicles (FSD §18, §19, §48.3 screen 10), and their rates and re-pricing
 * (FSD §26, §48.3 screen 11). View — `TRP.TRIPCONFIG.VIEW`; edit — `TRP.TRIPCONFIG.EDIT`; rates — `TRP.RATE.VIEW`/
 * `TRP.RATE.CONFIGURE`/`TRP.RATE.REPRICE`. */
@Injectable({ providedIn: 'root' })
export class TripConfigurationsApi {
  private readonly http = inject(HttpClient);

  // ── Configurations ─────────────────────────────────────────────────────────────
  list(customerId: number, includeInactive = false): Observable<TripConfigurationModel[]> {
    const params = new HttpParams().set('includeInactive', includeInactive);
    return this.http.get<ApiResponse<TripConfigurationModel[]>>(`${api}/customers/${customerId}/trip-configurations`, { params }).pipe(map((r) => r.data));
  }

  get(configurationId: number): Observable<TripConfigurationModel> {
    return this.http.get<ApiResponse<TripConfigurationModel>>(`${api}/trip-configurations/${configurationId}`).pipe(map((r) => r.data));
  }

  create(body: CreateTripConfigurationRequest): Observable<TripConfigurationModel> {
    return this.http.post<ApiResponse<TripConfigurationModel>>(`${api}/trip-configurations`, body).pipe(map((r) => r.data));
  }

  update(configurationId: number, body: UpdateTripConfigurationRequest): Observable<TripConfigurationModel> {
    return this.http.put<ApiResponse<TripConfigurationModel>>(`${api}/trip-configurations/${configurationId}`, body).pipe(map((r) => r.data));
  }

  updateStops(configurationId: number, body: UpdateTripConfigurationStopsRequest): Observable<TripConfigurationModel> {
    return this.http.put<ApiResponse<TripConfigurationModel>>(`${api}/trip-configurations/${configurationId}/stops`, body).pipe(map((r) => r.data));
  }

  activate(configurationId: number, body: ChangeTripConfigurationStatusRequest): Observable<TripConfigurationModel> {
    return this.http.post<ApiResponse<TripConfigurationModel>>(`${api}/trip-configurations/${configurationId}/activate`, body).pipe(map((r) => r.data));
  }

  deactivate(configurationId: number, body: ChangeTripConfigurationStatusRequest): Observable<TripConfigurationModel> {
    return this.http.post<ApiResponse<TripConfigurationModel>>(`${api}/trip-configurations/${configurationId}/deactivate`, body).pipe(map((r) => r.data));
  }

  copy(configurationId: number, body: CopyTripConfigurationRequest): Observable<TripConfigurationModel> {
    return this.http.post<ApiResponse<TripConfigurationModel>>(`${api}/trip-configurations/${configurationId}/copy`, body).pipe(map((r) => r.data));
  }

  // ── Vehicles (§19) ─────────────────────────────────────────────────────────────
  vehicles(configurationId: number): Observable<TripConfigurationVehicleModel[]> {
    return this.http.get<ApiResponse<TripConfigurationVehicleModel[]>>(`${api}/trip-configurations/${configurationId}/vehicles`).pipe(map((r) => r.data));
  }

  assignVehicle(configurationId: number, body: AssignTripConfigurationVehicleRequest): Observable<TripConfigurationVehicleModel> {
    return this.http.post<ApiResponse<TripConfigurationVehicleModel>>(`${api}/trip-configurations/${configurationId}/vehicles`, body).pipe(map((r) => r.data));
  }

  updateVehicle(assignmentId: number, body: UpdateTripConfigurationVehicleRequest): Observable<TripConfigurationVehicleModel> {
    return this.http.put<ApiResponse<TripConfigurationVehicleModel>>(`${api}/trip-configuration-vehicles/${assignmentId}`, body).pipe(map((r) => r.data));
  }

  // ── Rates (§26) ────────────────────────────────────────────────────────────────
  rates(configurationId: number, includeInactive = false): Observable<TripRateModel[]> {
    const params = new HttpParams().set('includeInactive', includeInactive);
    return this.http.get<ApiResponse<TripRateModel[]>>(`${api}/trip-configurations/${configurationId}/rates`, { params }).pipe(map((r) => r.data));
  }

  createRate(configurationId: number, body: SaveTripRateRequest): Observable<TripRateModel> {
    return this.http.post<ApiResponse<TripRateModel>>(`${api}/trip-configurations/${configurationId}/rates`, body).pipe(map((r) => r.data));
  }

  updateRate(rateId: number, body: UpdateTripRateRequest): Observable<TripRateModel> {
    return this.http.put<ApiResponse<TripRateModel>>(`${api}/trip-rates/${rateId}`, body).pipe(map((r) => r.data));
  }

  inactivateRate(rateId: number, body: InactivateTripRateRequest): Observable<unknown> {
    return this.http.post(`${api}/trip-rates/${rateId}/inactivate`, body);
  }

  splitRate(configurationId: number, body: SplitTripRateRequest): Observable<TripRateModel[]> {
    return this.http.post<ApiResponse<TripRateModel[]>>(`${api}/trip-configurations/${configurationId}/rates/split`, body).pipe(map((r) => r.data));
  }

  resolveRate(customerId: number, tripConfigurationId: number, date: string): Observable<RateResolutionResult> {
    const params = new HttpParams().set('customerId', customerId).set('tripConfigurationId', tripConfigurationId).set('date', date);
    return this.http.get<ApiResponse<RateResolutionResult>>(`${api}/trip-rates/resolve`, { params }).pipe(map((r) => r.data));
  }

  // ── Re-pricing (§26) — `TRP.RATE.REPRICE` ───────────────────────────────────────
  resolveMissingRates(body: ResolveMissingRatesRequest): Observable<ResolveMissingRatesResult> {
    return this.http.post<ApiResponse<ResolveMissingRatesResult>>(`${api}/trips/resolve-missing-rates`, body).pipe(map((r) => r.data));
  }
}

/** Fuel Card Management (FSD §28, §48.3 screen 17) — one permission, `TRP.FUELCARD.EDIT`, for everything. */
@Injectable({ providedIn: 'root' })
export class FuelCardsApi {
  private readonly http = inject(HttpClient);

  list(): Observable<FuelCardModel[]> {
    return this.http.get<ApiResponse<FuelCardModel[]>>(`${api}/fuel-cards`).pipe(map((r) => r.data));
  }

  get(id: number): Observable<FuelCardModel> {
    return this.http.get<ApiResponse<FuelCardModel>>(`${api}/fuel-cards/${id}`).pipe(map((r) => r.data));
  }

  create(body: CreateFuelCardRequest): Observable<FuelCardModel> {
    return this.http.post<ApiResponse<FuelCardModel>>(`${api}/fuel-cards`, body).pipe(map((r) => r.data));
  }

  save(id: number, body: SaveFuelCardRequest): Observable<FuelCardModel> {
    return this.http.put<ApiResponse<FuelCardModel>>(`${api}/fuel-cards/${id}`, body).pipe(map((r) => r.data));
  }

  assign(id: number, body: AssignFuelCardRequest): Observable<FuelCardModel> {
    return this.http.post<ApiResponse<FuelCardModel>>(`${api}/fuel-cards/${id}/assign`, body).pipe(map((r) => r.data));
  }

  assignments(id: number): Observable<FuelCardAssignmentModel[]> {
    return this.http.get<ApiResponse<FuelCardAssignmentModel[]>>(`${api}/fuel-cards/${id}/assignments`).pipe(map((r) => r.data));
  }
}

/** Fixed and Open trips, their lifecycle, list/search and history (FSD §21, §22, §24, §48.4 screens 12-15 +
 * the "Trip List (desk)" row). View — `TRP.TRIP.VIEW`; create — `TRP.TRIP.CREATE`; every lifecycle action is
 * conditional server-side (back-office `TRP.TRIP.STATUS` or the trip's own assigned driver) rather than gated
 * by one flat permission — this API never decides that itself, it just calls the endpoint and shows what comes back. */
@Injectable({ providedIn: 'root' })
export class TripsApi {
  private readonly http = inject(HttpClient);

  search(filter: TripSearchFilter, page = 1, pageSize = 25): Observable<Paged<TripListItem>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (filter.fromDate) params = params.set('fromDate', filter.fromDate);
    if (filter.toDate) params = params.set('toDate', filter.toDate);
    if (filter.customerId != null) params = params.set('customerId', filter.customerId);
    if (filter.vehicleId != null) params = params.set('vehicleId', filter.vehicleId);
    if (filter.driverId != null) params = params.set('driverId', filter.driverId);
    if (filter.status) params = params.set('status', filter.status);
    if (filter.tripType) params = params.set('tripType', filter.tripType);
    if (filter.isActive != null) params = params.set('isActive', filter.isActive);
    if (filter.invoiced != null) params = params.set('invoiced', filter.invoiced);
    return this.http.get<ApiResponse<Paged<TripListItem>>>(`${api}/trips/search`, { params }).pipe(map((r) => r.data));
  }

  get(tripId: number): Observable<TripModel> {
    return this.http.get<ApiResponse<TripModel>>(`${api}/trips/${tripId}`).pipe(map((r) => r.data));
  }

  history(tripId: number, page = 1, pageSize = 50): Observable<TripHistory> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<ApiResponse<TripHistory>>(`${api}/trips/${tripId}/history`, { params }).pipe(map((r) => r.data));
  }

  createFixed(body: CreateFixedTripRequest): Observable<TripModel> {
    return this.http.post<ApiResponse<TripModel>>(`${api}/trips`, body).pipe(map((r) => r.data));
  }

  createOpen(body: CreateOpenTripRequest): Observable<TripModel> {
    return this.http.post<ApiResponse<TripModel>>(`${api}/trips/open`, body).pipe(map((r) => r.data));
  }

  // ── Lifecycle (§24) ──────────────────────────────────────────────────────────────
  transition(tripId: number, toStatus: string, body: TransitionTripRequest): Observable<TripModel> {
    return this.http.post<ApiResponse<TripModel>>(`${api}/trips/${tripId}/status/${toStatus}`, body).pipe(map((r) => r.data));
  }

  hold(tripId: number, body: HoldTripRequest): Observable<TripModel> {
    return this.http.post<ApiResponse<TripModel>>(`${api}/trips/${tripId}/hold`, body).pipe(map((r) => r.data));
  }

  resume(tripId: number, body: ResumeTripRequest): Observable<TripModel> {
    return this.http.post<ApiResponse<TripModel>>(`${api}/trips/${tripId}/resume`, body).pipe(map((r) => r.data));
  }

  cancel(tripId: number, body: CancelTripRequest): Observable<TripModel> {
    return this.http.post<ApiResponse<TripModel>>(`${api}/trips/${tripId}/cancel`, body).pipe(map((r) => r.data));
  }

  reopen(tripId: number, body: ReopenTripRequest): Observable<TripModel> {
    return this.http.post<ApiResponse<TripModel>>(`${api}/trips/${tripId}/reopen`, body).pipe(map((r) => r.data));
  }

  inactivate(tripId: number, body: ChangeTripActiveRequest): Observable<TripModel> {
    return this.http.post<ApiResponse<TripModel>>(`${api}/trips/${tripId}/inactivate`, body).pipe(map((r) => r.data));
  }

  reactivate(tripId: number, body: ChangeTripActiveRequest): Observable<TripModel> {
    return this.http.post<ApiResponse<TripModel>>(`${api}/trips/${tripId}/reactivate`, body).pipe(map((r) => r.data));
  }
}

/** A trip's own operational tabs (§48.4 screen 14): Timeline, Fuel, Expenses, Income, Documents/POD, Issues, P&L.
 * Upload endpoints hand back a plain URL (`documentUploadUrl`/`podUploadUrl`) for `uploadFile`/`<vms-file-upload>`
 * to post to directly — the same shape `DocumentsApi.uploadUrl` already established, not an `Observable` method
 * here, since the caller needs to show upload progress as it happens. */
@Injectable({ providedIn: 'root' })
export class TripOperationsApi {
  private readonly http = inject(HttpClient);

  // ── Timeline / Events (§25) ──────────────────────────────────────────────────────
  events(tripId: number): Observable<TripEventModel[]> {
    return this.http.get<ApiResponse<TripEventModel[]>>(`${api}/trips/${tripId}/events`).pipe(map((r) => r.data));
  }

  addEvent(tripId: number, body: CreateTripEventRequest): Observable<TripEventModel> {
    return this.http.post<ApiResponse<TripEventModel>>(`${api}/trips/${tripId}/events`, body).pipe(map((r) => r.data));
  }

  // ── Fuel (§27) ───────────────────────────────────────────────────────────────────
  fuel(tripId: number): Observable<TripFuelListModel> {
    return this.http.get<ApiResponse<TripFuelListModel>>(`${api}/trips/${tripId}/fuel`).pipe(map((r) => r.data));
  }

  addFuel(tripId: number, body: CreateTripFuelRequest): Observable<TripFuelModel> {
    return this.http.post<ApiResponse<TripFuelModel>>(`${api}/trips/${tripId}/fuel`, body).pipe(map((r) => r.data));
  }

  voidFuel(id: number, body: VoidTripFuelRequest): Observable<TripFuelModel> {
    return this.http.post<ApiResponse<TripFuelModel>>(`${api}/trip-fuel/${id}/void`, body).pipe(map((r) => r.data));
  }

  // ── Expenses (§29) ───────────────────────────────────────────────────────────────
  expenses(tripId: number): Observable<TripExpenseModel[]> {
    return this.http.get<ApiResponse<TripExpenseModel[]>>(`${api}/trips/${tripId}/expenses`).pipe(map((r) => r.data));
  }

  addExpense(tripId: number, body: CreateTripExpenseRequest): Observable<TripExpenseModel> {
    return this.http.post<ApiResponse<TripExpenseModel>>(`${api}/trips/${tripId}/expenses`, body).pipe(map((r) => r.data));
  }

  decideExpense(id: number, body: DecideTripExpenseRequest): Observable<TripExpenseModel> {
    return this.http.post<ApiResponse<TripExpenseModel>>(`${api}/trip-expenses/${id}/approve`, body).pipe(map((r) => r.data));
  }

  voidExpense(id: number, body: VoidTripExpenseRequest): Observable<TripExpenseModel> {
    return this.http.post<ApiResponse<TripExpenseModel>>(`${api}/trip-expenses/${id}/void`, body).pipe(map((r) => r.data));
  }

  // ── Income (§30) ─────────────────────────────────────────────────────────────────
  income(tripId: number): Observable<TripIncomeModel[]> {
    return this.http.get<ApiResponse<TripIncomeModel[]>>(`${api}/trips/${tripId}/income`).pipe(map((r) => r.data));
  }

  addIncome(tripId: number, body: CreateTripIncomeRequest): Observable<TripIncomeModel> {
    return this.http.post<ApiResponse<TripIncomeModel>>(`${api}/trips/${tripId}/income`, body).pipe(map((r) => r.data));
  }

  voidIncome(id: number, body: VoidTripIncomeRequest): Observable<TripIncomeModel> {
    return this.http.post<ApiResponse<TripIncomeModel>>(`${api}/trip-income/${id}/void`, body).pipe(map((r) => r.data));
  }

  // ── Documents & POD (§23) ────────────────────────────────────────────────────────
  documents(tripId: number): Observable<TripDocumentModel[]> {
    return this.http.get<ApiResponse<TripDocumentModel[]>>(`${api}/trips/${tripId}/documents`).pipe(map((r) => r.data));
  }

  documentUploadUrl(tripId: number): string {
    return `${api}/trips/${tripId}/documents`;
  }

  documentDownloadLink(id: number): Observable<TripDownloadLinkModel> {
    return this.http.post<ApiResponse<TripDownloadLinkModel>>(`${api}/trip-documents/${id}/download-link`, {}).pipe(map((r) => r.data));
  }

  pods(tripId: number): Observable<TripPodModel[]> {
    return this.http.get<ApiResponse<TripPodModel[]>>(`${api}/trips/${tripId}/pod`).pipe(map((r) => r.data));
  }

  podUploadUrl(tripId: number): string {
    return `${api}/trips/${tripId}/pod`;
  }

  approvePod(id: number): Observable<TripPodModel> {
    return this.http.post<ApiResponse<TripPodModel>>(`${api}/trip-pods/${id}/approve`, {}).pipe(map((r) => r.data));
  }

  rejectPod(id: number, body: RejectPodRequest): Observable<TripPodModel> {
    return this.http.post<ApiResponse<TripPodModel>>(`${api}/trip-pods/${id}/reject`, body).pipe(map((r) => r.data));
  }

  podDownloadLink(id: number): Observable<TripDownloadLinkModel> {
    return this.http.post<ApiResponse<TripDownloadLinkModel>>(`${api}/trip-pods/${id}/download-link`, {}).pipe(map((r) => r.data));
  }

  // ── Issues (§23) ─────────────────────────────────────────────────────────────────
  issues(tripId: number): Observable<TripIssueModel[]> {
    return this.http.get<ApiResponse<TripIssueModel[]>>(`${api}/trips/${tripId}/issues`).pipe(map((r) => r.data));
  }

  addIssue(tripId: number, body: CreateTripIssueRequest): Observable<TripIssueModel> {
    return this.http.post<ApiResponse<TripIssueModel>>(`${api}/trips/${tripId}/issues`, body).pipe(map((r) => r.data));
  }

  resolveIssue(id: number, body: ResolveTripIssueRequest): Observable<TripIssueModel> {
    return this.http.post<ApiResponse<TripIssueModel>>(`${api}/trip-issues/${id}/resolve`, body).pipe(map((r) => r.data));
  }

  // ── Operational P&L (§31) ────────────────────────────────────────────────────────
  pnl(tripId: number): Observable<TripPnLModel> {
    return this.http.get<ApiResponse<TripPnLModel>>(`${api}/trips/${tripId}/pnl`).pipe(map((r) => r.data));
  }
}
