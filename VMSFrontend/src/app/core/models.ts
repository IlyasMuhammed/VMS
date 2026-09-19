export interface ApiResponse<T = void> {
  success: boolean;
  message: string;
  data: T;
}

export interface Paged<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface DropDown {
  id: number;
  value: string;
}

export interface CurrentUser {
  userId: number;
  tenantId: string;
  email: string;
  firstName: string;
  lastName?: string;
  phone?: string;
  department?: string;
  role: DropDown;
  isSuperAdmin: boolean;
  permissions: string[];
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  user: CurrentUser;
}

export interface RefreshResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

// ── Users ──
export interface UserListItem {
  userId: number;
  tenantId: string;
  firstName: string;
  lastName?: string;
  email: string;
  department?: string;
  isActive: boolean;
  invitePending: boolean;
  role?: DropDown;
  createdDate: string;
  lastLoginAt?: string;
}

export interface UserDetail extends UserListItem {
  phone?: string;
  inviteLink?: string;
}

export interface UserListFilter {
  search?: string;
  status?: 'active' | 'inactive' | '';
  roleId?: number | null;
  page: number;
  pageSize: number;
}

export interface CreateUserRequest {
  firstName: string;
  lastName?: string;
  email: string;
  phone?: string;
  department?: string;
  roleId: number;
}

export interface PatchUserRequest {
  firstName?: string;
  lastName?: string;
  phone?: string;
  department?: string;
  isActive?: boolean;
}

// ── Roles ──
export interface RoleListItem {
  roleId: number;
  name: string;
  roleCode: string;
  description?: string;
  isActive: boolean;
  isGlobal: boolean;
  activeUserCount: number;
  permissionCount: number;
}

export interface PermissionItem {
  permissionId: number;
  name: string;
  code: string;
  description?: string;
  isAllowed: boolean;
}

export interface PermissionGroup {
  module: string;
  permissions: PermissionItem[];
}

export interface RoleDetail extends RoleListItem {
  permissionGroups: PermissionGroup[];
}

export interface RoleUser {
  userId: number;
  firstName: string;
  lastName?: string;
  email: string;
  department?: string;
  isActive: boolean;
}

export interface CreateRoleRequest {
  name: string;
  roleCode: string;
  description?: string;
  isGlobal?: boolean;
}

// ── Tenants ──
export interface TenantListItem {
  id: string;
  tenantCode: string;
  tenantName: string;
  isActive: boolean;
  contactEmail?: string;
  createdDate: string;
}

/** Which appearance mode a logo is for. A "light" logo sits on light backgrounds, a "dark" one on dark. */
export type LogoVariant = 'light' | 'dark';

export interface TenantDetail extends TenantListItem {
  contactPhone?: string;
  address?: string;
  country?: string;
  timeZone?: string;
  /** Content hash of the uploaded logo, or null/absent when there is none. */
  logoLightVersion?: string | null;
  logoDarkVersion?: string | null;
}

export interface CreateTenantRequest {
  tenantCode: string;
  tenantName: string;
  contactEmail?: string;
  contactPhone?: string;
  address?: string;
  country?: string;
  timeZone?: string;
  adminFirstName: string;
  adminLastName?: string;
  adminEmail: string;
}

export interface CreateTenantResult {
  tenantId: string;
  adminUserId: number;
  adminInviteLink: string;
}
