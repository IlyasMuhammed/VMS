import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { NotificationRulesApi, RolesApi } from '../../core/api.services';
import { NotificationRule, SaveNotificationRuleRequest } from '../../core/notification.models';
import { NotifyService } from '../../core/notify.service';

/** A permission code offered in the recipient / escalation dropdowns, with its name for the label. */
interface PermissionOption {
  code: string;
  label: string;
}

const EVENT_LABELS: Record<string, string> = {
  DocumentExpiry: 'A document is approaching its expiry date.',
  ChargeDue: 'A recurring charge entry has become due.',
  ChargeOverdue: 'A recurring charge entry is overdue.',
  ChargeEnding: "A recurring charge's own schedule is ending.",
  InstallmentDue: 'A bank lease installment is approaching its due date.',
  ItemWarrantyEnd: "An attached item's warranty is approaching its end.",
};

/**
 * Administration → Notification rules (FSD §9, S7-NOT-05): the lead days and recipients behind every alert.
 * The six event types are fixed (§19A.6, §23.4, BR-NOT-001) — only their lead days, recipient and, for the
 * overdue rule, its escalation are ever edited.
 */
@Component({
  selector: 'app-notification-rules',
  standalone: true,
  imports: [FormsModule, TableModule, ButtonModule, DialogModule, SelectModule, CheckboxModule, InputNumberModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1>Notification rules</h1>
          <div class="sub">How many days ahead each kind of alert fires, and who it goes to. All lead times live here, never as fixed numbers in the app.</div>
        </div>
      </div>

      <div class="card">
        <p-table [value]="rules()" [loading]="loading()">
          <ng-template pTemplate="header">
            <tr>
              <th>Event</th>
              <th class="num">Lead days</th>
              <th>Recipient</th>
              <th>Escalation</th>
              <th>Status</th>
              <th></th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-r>
            <tr [class.inactive]="!r.isActive">
              <td>
                <div class="event">{{ r.name }}</div>
                <div class="muted">{{ eventDescription(r.eventType) }}</div>
              </td>
              <td class="num">{{ r.leadDays }}</td>
              <td>{{ label(r.recipientPermission) }}</td>
              <td>
                @if (r.escalationAfterDays) {
                  <span>After {{ r.escalationAfterDays }} day{{ r.escalationAfterDays === 1 ? '' : 's' }}, also to {{ label(r.escalationPermission) }}</span>
                } @else {
                  <span class="muted">—</span>
                }
              </td>
              <td><span class="chip" [class.chip--success]="r.isActive">{{ r.isActive ? 'Active' : 'Off' }}</span></td>
              <td class="actions">
                <p-button icon="pi pi-pencil" [text]="true" size="small" [ariaLabel]="'Edit ' + r.name" (onClick)="openEdit(r)" />
              </td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr><td colspan="6" class="muted">@if (loading()) { Loading… } @else { No rules yet. }</td></tr>
          </ng-template>
        </p-table>
      </div>
    </div>

    <p-dialog [header]="'Edit ' + (editing?.name ?? '')" [(visible)]="dialogOpen" [modal]="true" [style]="{ width: '520px' }">
      @if (editing; as r) {
        <div class="form-grid">
          <div class="field full">
            <div class="muted">{{ eventDescription(r.eventType) }}</div>
          </div>
          <div class="field">
            <label for="rule-lead">Lead days *</label>
            <p-inputnumber inputId="rule-lead" [(ngModel)]="draft.leadDays" [min]="0" [max]="365" [showButtons]="true" [fluid]="true" />
            <span class="hint">{{ r.eventType === 'ChargeOverdue' ? 'Notify this many days after the due date.' : 'Notify this many days before the trigger date.' }}</span>
          </div>
          <div class="field full">
            <label for="rule-recipient">Recipient *</label>
            <p-select inputId="rule-recipient" [options]="permissionOptions()" optionLabel="label" optionValue="code" [(ngModel)]="draft.recipientPermission"
                      [filter]="true" placeholder="Choose who holds this permission…" [fluid]="true" appendTo="body" />
            <span class="hint">Whoever currently holds this permission, through any role, is told.</span>
          </div>

          @if (r.eventType === 'ChargeOverdue') {
            <div class="field">
              <label for="rule-escalate-days">Escalate after *</label>
              <p-inputnumber inputId="rule-escalate-days" [(ngModel)]="draft.escalationAfterDays" [min]="1" [max]="90" [showButtons]="true" suffix=" days" [fluid]="true" />
              <span class="hint">Repeats the reminder and adds the escalation recipient once it has been overdue this long.</span>
            </div>
            <div class="field full">
              <label for="rule-escalation">Escalation recipient *</label>
              <p-select inputId="rule-escalation" [options]="permissionOptions()" optionLabel="label" optionValue="code" [(ngModel)]="draft.escalationPermission"
                        [filter]="true" placeholder="Choose who to escalate to…" [fluid]="true" appendTo="body" />
            </div>
          }

          <div class="field full">
            <label class="check"><p-checkbox [(ngModel)]="draft.isActive" [binary]="true" inputId="rule-active" /> Active</label>
          </div>
        </div>
      }
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="dialogOpen = false" />
        <p-button label="Save" [loading]="busy()" [disabled]="!canSave()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .num { text-align: right; width: 6rem; }
      .event { font-weight: 600; }
      tr.inactive td { color: var(--vms-muted); }
    `,
  ],
})
export class NotificationRulesComponent implements OnInit {
  private readonly api = inject(NotificationRulesApi);
  private readonly rolesApi = inject(RolesApi);
  private readonly notify = inject(NotifyService);

  readonly rules = signal<NotificationRule[]>([]);
  private readonly permissions = signal<PermissionOption[]>([]);
  readonly permissionOptions = computed(() => this.permissions());

  readonly loading = signal(false);
  readonly busy = signal(false);

  dialogOpen = false;
  editing: NotificationRule | null = null;
  draft: SaveNotificationRuleRequest = { leadDays: 0, recipientPermission: '', escalationAfterDays: null, escalationPermission: null, isActive: true };

  ngOnInit(): void {
    this.load();
    this.rolesApi.catalog().subscribe({
      next: (groups) => this.permissions.set(groups.flatMap((g) => g.permissions).map((p) => ({ code: p.code, label: `${p.name} (${p.code})` })).sort((a, b) => a.label.localeCompare(b.label))),
      error: () => undefined,
    });
  }

  private load(): void {
    this.loading.set(true);
    this.api.list().subscribe({
      next: (rules) => {
        this.rules.set(rules);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.notify.error(err);
      },
    });
  }

  eventDescription(eventType: string): string {
    return EVENT_LABELS[eventType] ?? '';
  }

  label(code: string | null | undefined): string {
    if (!code) return '—';
    return this.permissions().find((p) => p.code === code)?.label ?? code;
  }

  openEdit(rule: NotificationRule): void {
    this.editing = rule;
    this.draft = {
      leadDays: rule.leadDays,
      recipientPermission: rule.recipientPermission,
      escalationAfterDays: rule.escalationAfterDays ?? null,
      escalationPermission: rule.escalationPermission ?? null,
      isActive: rule.isActive,
    };
    this.dialogOpen = true;
  }

  canSave(): boolean {
    const r = this.editing;
    if (!r || this.busy()) return false;
    if (this.draft.leadDays === null || this.draft.leadDays < 0 || this.draft.leadDays > 365) return false;
    if (!this.draft.recipientPermission) return false;
    if (r.eventType === 'ChargeOverdue') {
      if (!this.draft.escalationAfterDays || this.draft.escalationAfterDays < 1 || this.draft.escalationAfterDays > 90) return false;
      if (!this.draft.escalationPermission) return false;
    }
    return true;
  }

  save(): void {
    const rule = this.editing;
    if (!rule || !this.canSave()) return;
    this.busy.set(true);
    const body: SaveNotificationRuleRequest = { ...this.draft };
    if (rule.eventType !== 'ChargeOverdue') { body.escalationAfterDays = null; body.escalationPermission = null; }

    this.api.update(rule.id, body).subscribe({
      next: () => {
        this.busy.set(false);
        this.dialogOpen = false;
        this.notify.success(`${rule.name} saved.`, 'Saved');
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.notify.error(err);
      },
    });
  }
}
