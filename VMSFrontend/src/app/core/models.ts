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
  /** The primary role's data scope (§23B.4). */
  scopeType: string;
  branchId?: string | null;
  branchName?: string | null;
}

export interface UserDetail extends UserListItem {
  phone?: string;
  inviteLink?: string;
  linkedPartnerId?: number | null;
  linkedPartnerName?: string | null;
  roles: UserRoleItem[];
}

/** One role a user holds, with its own scope (§23B.1, §23B.4) — the "why can they see that" data. */
export interface UserRoleItem {
  roleId: number;
  roleName: string;
  isPrimary: boolean;
  scopeType: string;
  branchId?: string | null;
  branchName?: string | null;
}

/** 'AllBranches' | 'OwnBranch' | 'OwnVehicles' | 'OwnRecords' (§23B.4) — a plain string here, like every other option-driven field; the server validates it. */
export type ScopeType = string;

export interface SetScopeRequest {
  scopeType: ScopeType;
  branchId?: string | null;
}

export interface SetDriverLinkRequest {
  partnerId?: number | null;
}

export interface AddRoleRequest {
  roleId: number;
  scopeType?: ScopeType;
  branchId?: string | null;
}

/** One capability a user effectively holds, and every role of theirs that grants it (§23B.6). */
export interface EffectivePermission {
  code: string;
  name: string;
  module: string;
  level: string;
  grantedByRoles: string[];
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
  /** Interface, Operation or Field (§23B) — the permission tree's second grouping level. */
  level: string;
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

// ── Master lists (lookups) ──────────────────────────────────────────────────────

export type LookupAttributeKind = 'Flag' | 'Number' | 'Text' | 'Lookup';

/** An extra field a list's values carry, beyond code, description, sort order and active flag. */
export interface LookupAttribute {
  key: string;
  label: string;
  kind: LookupAttributeKind;
  required: boolean;
  /** For a `Lookup` field: the list whose values can be chosen. */
  lookupType?: string | null;
}

export interface LookupType {
  code: string;
  name: string;
  description: string;
  attributes: LookupAttribute[];
  activeCount: number;
  totalCount: number;
}

export interface LookupItem {
  id: number;
  code: string;
  description: string;
  sortOrder: number;
  isActive: boolean;
  /** Extra fields as text: `'true'`/`'false'` for a flag, a whole number, text, or the code of another list's value. */
  attributes: Record<string, string | null>;
}

/** One of the tenant's own locations (FSD §6 field 15). A tenant starts with one, "Head Office" (OQ-10). */
export interface Branch {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

export interface SaveLookupRequest {
  /** Only when adding; a value's code never changes. */
  code?: string;
  description: string;
  /** Left out when adding, the value goes to the end of the list. */
  sortOrder?: number | null;
  isActive?: boolean;
  attributes: Record<string, string | null>;
}