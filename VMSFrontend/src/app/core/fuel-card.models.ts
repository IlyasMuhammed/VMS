/** Fuel Card Management (FSD §28, §48.3 screen 17). `fuelCardCompanyId` and `driverId` are Business Partner ids
 * (roles `FuelCardCompany` and `Driver`) — reuse `vms-partner-picker`, not a lookup. */
export type FuelCardStatus = 'Active' | 'Inactive' | 'Expired' | 'Blocked';

export interface FuelCardModel {
  fuelCardId: number;
  /** Masked except the last 4 digits (e.g. `****1234`) — the full number never leaves the server. */
  maskedCardNumber: string;
  fuelCardCompanyId: number;
  vehicleId?: number | null;
  driverId?: number | null;
  cardHolderName?: string | null;
  expiryDate: string;
  monthlyLimit?: number | null;
  status: FuelCardStatus;
  remarks?: string | null;
  rowVersion: string;
}

export interface CreateFuelCardRequest {
  cardNumber: string;
  fuelCardCompanyId: number;
  cardHolderName?: string | null;
  expiryDate: string;
  monthlyLimit?: number | null;
  remarks?: string | null;
}

/** The card number and its issuing company are set once, at creation, and not editable here. */
export interface SaveFuelCardRequest {
  cardHolderName?: string | null;
  expiryDate: string;
  /** Restricted server-side to Active/Inactive/Blocked — Expired is system-set only. */
  status: 'Active' | 'Inactive' | 'Blocked';
  monthlyLimit?: number | null;
  remarks?: string | null;
  rowVersion: string;
}

export interface AssignFuelCardRequest {
  vehicleId?: number | null;
  driverId?: number | null;
  /** Defaults to today. */
  assignedFrom?: string | null;
  reason?: string | null;
  rowVersion: string;
}

export interface FuelCardAssignmentModel {
  fuelCardAssignmentId: number;
  fuelCardId: number;
  vehicleId?: number | null;
  driverId?: number | null;
  assignedFrom: string;
  assignedTo?: string | null;
  reason?: string | null;
}
