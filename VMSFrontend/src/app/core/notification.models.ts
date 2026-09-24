/** The notification rule master (FSD §9, §19A.6, §23.4): one row per event type, admin-configurable. */
export interface NotificationRule {
  id: number;
  eventType: 'DocumentExpiry' | 'ChargeDue' | 'ChargeOverdue' | 'ChargeEnding' | 'InstallmentDue' | 'ItemWarrantyEnd';
  name: string;
  leadDays: number;
  recipientPermission: string;
  /** Only set for the ChargeOverdue rule. */
  escalationAfterDays?: number | null;
  escalationPermission?: string | null;
  isActive: boolean;
}

/** Only the lead days, recipient, escalation and active flag are editable — the event type and name are fixed. */
export interface SaveNotificationRuleRequest {
  leadDays: number;
  recipientPermission: string;
  escalationAfterDays?: number | null;
  escalationPermission?: string | null;
  isActive: boolean;
}

/** One row of the in-app notification list. */
export interface AppNotification {
  id: number;
  eventType: string;
  title: string;
  message: string;
  ownerType: 'Vehicle' | 'BusinessPartner';
  ownerId: number;
  isEscalation: boolean;
  occurredOn: string;
  isRead: boolean;
}
