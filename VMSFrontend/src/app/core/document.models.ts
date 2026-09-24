/** The two kinds of owner a document belongs to (FSD §23A): one engine, reached from a route of its own for each. */
export type DocumentOwnerType = 'BusinessPartner' | 'Vehicle';

/** The document type master (§23A.1): what decides everything about how one kind of document behaves. */
export interface DocumentType {
  id: number;
  code: string;
  name: string;
  appliesTo: DocumentOwnerType[];
  partnerRole?: string | null;
  isExpirable: boolean;
  defaultValidityValue?: number | null;
  defaultValidityUnit?: string | null;
  isPeriodic: boolean;
  renewalLeadDays: number;
  mandatoryLevel: 'None' | 'Warn' | 'Required';
  requiresDocumentNumber: boolean;
  hasCost: boolean;
  allowedFormats: string[];
  maxFileSizeMb: number;
  retentionYears: number;
  isActive: boolean;
  /** BR-DOC-006: the Recurring Charge Type code this type's renewal can offer to link a payment to, where one exists. */
  linkedChargeTypeCode?: string | null;
}

/** One version of a document (§23A.3), current or superseded. */
export interface DocumentVersion {
  id: number;
  documentCode: string;
  ownerType: DocumentOwnerType;
  ownerId: number;
  documentTypeId: number;
  documentTypeName?: string | null;
  versionNo: number;
  isCurrent: boolean;
  status: 'Active' | 'ExpiringSoon' | 'Expired' | 'Superseded' | 'Rejected';
  documentNumber?: string | null;
  provider?: string | null;
  issueDate?: string | null;
  expiryDate?: string | null;
  daysRemaining?: number | null;
  originalFileName: string;
  sizeBytes: number;
  rejectReason?: string | null;
  linkedTransactionId?: number | null;
  createdOn: string;
}

/** One document slot: its current version (if any) plus, on request, its history. */
export interface DocumentSlot {
  documentTypeId: number;
  documentTypeName: string;
  mandatoryLevel: 'None' | 'Warn' | 'Required';
  hasCost: boolean;
  current?: DocumentVersion | null;
  history: DocumentVersion[];
}

/** Fields sent with an upload or a renewal, as `uploadFile`'s extra form fields. */
export interface UploadDocumentFields {
  /** Upload only: a renewal's type comes from the URL, not a field. */
  documentTypeId?: string;
  documentNumber?: string;
  provider?: string;
  issueDate?: string;
  expiryDate?: string;
  /** Renew only (BR-DOC-006): the transaction already posted for this premium or fee, to tag the new version with. */
  linkedTransactionId?: string;
}

export interface DownloadLink {
  url: string;
  expiresAtUtc: string;
}

/** Filters for the Document Register (§23A.4). No branch, vehicle-category or partner-role filter yet — see the register's own note. */
export interface RegisterQuery {
  ownerType?: DocumentOwnerType | '';
  documentTypeId?: number | null;
  status?: string;
  expiringWithinDays?: number | null;
}

export interface RegisterRow {
  documentId: number;
  documentCode: string;
  ownerType: DocumentOwnerType;
  ownerId: number;
  ownerName: string;
  documentTypeId: number;
  documentTypeName: string;
  status: string;
  expiryDate?: string | null;
  daysRemaining?: number | null;
}

/** One owner missing one mandatory or periodic document (the Missing Documents report, §23A.4). */
export interface MissingDocumentRow {
  ownerType: DocumentOwnerType;
  ownerId: number;
  ownerName: string;
  documentTypeCode: string;
  documentTypeName: string;
  required: boolean;
}

/** One document due to expire within the requested month (the Expiry Calendar, §23A.4). */
export interface CalendarEntry {
  date: string;
  ownerType: DocumentOwnerType;
  ownerId: number;
  ownerName: string;
  documentTypeName: string;
  documentId: number;
}

export interface DocumentJobsResult {
  expiry: { recalculated: number; becameExpiringSoon: number; becameExpired: number };
  retention: { removed: number };
}
