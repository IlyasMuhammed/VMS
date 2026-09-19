import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  ApiResponse,
  CreateRoleRequest,
  CreateTenantRequest,
  CreateTenantResult,
  CreateUserRequest,
  CurrentUser,
  Paged,
  PatchUserRequest,
  PermissionGroup,
  RoleDetail,
  RoleListItem,
  RoleUser,
  TenantDetail,
  TenantListItem,
  UserDetail,
  UserListFilter,
  UserListItem,
} from './models';

const api = environment.apiUrl;

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

  create(body: CreateUserRequest): Observable<UserDetail> {
    return this.http.post<ApiResponse<UserDetail>>(`${api}/users`, body).pipe(map((r) => r.data));
  }

  patch(id: number, body: PatchUserRequest): Observable<unknown> {
    return this.http.patch(`${api}/users/${id}`, body);
  }

  assignRole(id: number, roleId: number): Observable<unknown> {
    return this.http.put(`${api}/users/${id}/role`, { roleId });
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
