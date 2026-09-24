import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputNumberModule } from 'primeng/inputnumber';
import { SelectModule } from 'primeng/select';
import { TabsModule } from 'primeng/tabs';
import { TagModule } from 'primeng/tag';
import { DocumentsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { CalendarEntry, DocumentType, MissingDocumentRow, RegisterRow } from '../../core/document.models';
import { DOCUMENT_STATUSES, mandatorySeverity, ownerLink, statusSeverity, words } from './document-logic';

/** The month a calendar grid draws: its weeks, each week's days, and what falls on each one. */
interface CalendarDay {
  date: string;
  inMonth: boolean;
  entries: CalendarEntry[];
}

/**
 * The compliance officer's three reports (§23A.4): the fleet-wide/partner-wide Document Register, what is missing, and an
 * Expiry Calendar. One screen, one permission (DOC.REGISTER.VIEW) — a tab each, since they answer three different questions
 * about the same data rather than needing three places in the nav.
 */
@Component({
  selector: 'app-documents-reports',
  standalone: true,
  imports: [RouterLink, ReactiveFormsModule, ButtonModule, SelectModule, InputNumberModule, TabsModule, TagModule, BusinessDatePipe],
  template: `
    <div class="page">
      <div class="page-header"><div><h1>Documents</h1><div class="sub">The register of what is on file, what is missing, and what is expiring soon, across vehicles and partners.</div></div></div>

      <div class="card body">
        <p-tabs [value]="tab()" (valueChange)="tab.set($any($event))">
          <p-tablist>
            <p-tab value="register">Register</p-tab>
            <p-tab value="missing">Missing documents</p-tab>
            <p-tab value="calendar">Expiry calendar</p-tab>
          </p-tablist>
          <p-tabpanels>

            <p-tabpanel value="register">
              <form [formGroup]="registerFilters" class="filter-grid">
                <p-select [options]="ownerOptions" optionLabel="label" optionValue="value" formControlName="ownerType" placeholder="Any owner type" [showClear]="true" appendTo="body" />
                <p-select [options]="typeOptions()" optionLabel="label" optionValue="value" formControlName="documentTypeId" placeholder="Any document type" [showClear]="true" appendTo="body" [filter]="true" />
                <p-select [options]="statusOptions" optionLabel="label" optionValue="value" formControlName="status" placeholder="Any status" [showClear]="true" appendTo="body" />
                <p-inputnumber formControlName="expiringWithinDays" placeholder="Expiring within (days)" [min]="0" [useGrouping]="false" [fluid]="true" />
              </form>
              @if (registerError(); as m) { <div class="alert error" role="alert">{{ m }} <p-button label="Try again" [text]="true" size="small" (onClick)="loadRegister()" /></div> }
              <table>
                <thead><tr><th>Owner</th><th>Type</th><th>Status</th><th>Expiry</th><th>Days</th></tr></thead>
                <tbody>
                  @for (r of register(); track r.documentId) {
                    <tr>
                      <td><a [routerLink]="link(r.ownerType, r.ownerId)">{{ r.ownerName }}</a> <span class="muted small">({{ words(r.ownerType) }})</span></td>
                      <td>{{ r.documentTypeName }}</td>
                      <td><p-tag [value]="words(r.status)" [severity]="statusOf(r.status)" /></td>
                      <td class="nowrap">{{ r.expiryDate ? (r.expiryDate | vmsDate) : '—' }}</td>
                      <td class="num">{{ r.daysRemaining ?? '—' }}</td>
                    </tr>
                  }
                  @if (register().length === 0) { <tr><td colspan="5" class="muted">{{ registerLoading() ? 'Loading…' : 'Nothing matches these filters.' }}</td></tr> }
                </tbody>
              </table>
            </p-tabpanel>

            <p-tabpanel value="missing">
              <form [formGroup]="missingFilters" class="filter-grid">
                <p-select [options]="ownerOptions" optionLabel="label" optionValue="value" formControlName="ownerType" placeholder="Any owner type" [showClear]="true" appendTo="body" />
              </form>
              @if (missingError(); as m) { <div class="alert error" role="alert">{{ m }} <p-button label="Try again" [text]="true" size="small" (onClick)="loadMissing()" /></div> }
              <table>
                <thead><tr><th>Owner</th><th>Document type</th><th>Level</th></tr></thead>
                <tbody>
                  @for (r of missing(); track r.ownerType + '-' + r.ownerId + '-' + r.documentTypeCode) {
                    <tr>
                      <td><a [routerLink]="link(r.ownerType, r.ownerId)">{{ r.ownerName }}</a> <span class="muted small">({{ words(r.ownerType) }})</span></td>
                      <td>{{ r.documentTypeName }}</td>
                      <td><p-tag [value]="r.required ? 'Required' : 'Warn'" [severity]="mandOf(r.required ? 'Required' : 'Warn')" /></td>
                    </tr>
                  }
                  @if (missing().length === 0) { <tr><td colspan="3" class="muted">{{ missingLoading() ? 'Loading…' : 'Nothing is missing.' }}</td></tr> }
                </tbody>
              </table>
            </p-tabpanel>

            <p-tabpanel value="calendar">
              <div class="cal-nav">
                <p-button icon="pi pi-chevron-left" [text]="true" (onClick)="shiftMonth(-1)" [attr.aria-label]="'Previous month'" />
                <strong>{{ monthLabel() }}</strong>
                <p-button icon="pi pi-chevron-right" [text]="true" (onClick)="shiftMonth(1)" [attr.aria-label]="'Next month'" />
              </div>
              @if (calendarError(); as m) { <div class="alert error" role="alert">{{ m }} <p-button label="Try again" [text]="true" size="small" (onClick)="loadCalendar()" /></div> }
              <div class="cal-grid">
                @for (h of weekdayLabels; track h) { <div class="cal-head">{{ h }}</div> }
                @for (d of calendarDays(); track d.date) {
                  <div class="cal-cell" [class.out]="!d.inMonth">
                    <div class="cal-date">{{ dayNum(d.date) }}</div>
                    @for (e of d.entries; track e.documentId) {
                      <a [routerLink]="link(e.ownerType, e.ownerId)" class="cal-entry">{{ e.ownerName }} · {{ e.documentTypeName }}</a>
                    }
                  </div>
                }
              </div>
            </p-tabpanel>

          </p-tabpanels>
        </p-tabs>
      </div>
    </div>
  `,
  styles: [
    `
      .filter-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr)); gap: .75rem; margin-bottom: 1rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; }
      .num { text-align: right; font-variant-numeric: tabular-nums; } .nowrap { white-space: nowrap; } .small { font-size: .8rem; } .alert { margin-bottom: 1rem; }
      .cal-nav { display: flex; align-items: center; gap: .75rem; margin-bottom: 1rem; }
      .cal-grid { display: grid; grid-template-columns: repeat(7, 1fr); gap: 1px; background: var(--vms-border); border: 1px solid var(--vms-border); border-radius: var(--vms-radius); overflow: hidden; }
      .cal-head { background: var(--vms-surface-soft); padding: .4rem .5rem; font-size: .75rem; font-weight: 600; color: var(--vms-muted); text-align: center; }
      .cal-cell { background: var(--vms-surface); min-height: 5.5rem; padding: .35rem; display: flex; flex-direction: column; gap: .15rem; }
      .cal-cell.out { background: var(--vms-surface-soft); opacity: .55; }
      .cal-date { font-size: .78rem; font-weight: 600; color: var(--vms-muted); }
      .cal-entry { font-size: .72rem; display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    `,
  ],
})
export class DocumentsReportsComponent {
  private readonly api = inject(DocumentsApi);
  private readonly fb = inject(FormBuilder);

  protected readonly tab = signal<'register' | 'missing' | 'calendar'>('register');
  protected readonly words = words;
  protected readonly statusOf = statusSeverity;
  protected readonly mandOf = mandatorySeverity;
  protected readonly weekdayLabels = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

  protected readonly ownerOptions = [{ label: 'Vehicle', value: 'Vehicle' }, { label: 'Business partner', value: 'BusinessPartner' }];
  protected readonly statusOptions = DOCUMENT_STATUSES.map((s) => ({ label: words(s), value: s }));
  private readonly allTypes = signal<DocumentType[]>([]);
  protected readonly typeOptions = computed(() => this.allTypes().map((t) => ({ label: t.name, value: t.id })));

  // ── Register ───────────────────────────────────────────────────────────────────
  protected readonly register = signal<RegisterRow[]>([]);
  protected readonly registerLoading = signal(false);
  protected readonly registerError = signal<string | null>(null);
  readonly registerFilters = this.fb.group({
    ownerType: this.fb.control<string | null>(null),
    documentTypeId: this.fb.control<number | null>(null),
    status: this.fb.control<string | null>(null),
    expiringWithinDays: this.fb.control<number | null>(null),
  });

  // ── Missing ────────────────────────────────────────────────────────────────────
  protected readonly missing = signal<MissingDocumentRow[]>([]);
  protected readonly missingLoading = signal(false);
  protected readonly missingError = signal<string | null>(null);
  readonly missingFilters = this.fb.group({ ownerType: this.fb.control<string | null>(null) });

  // ── Calendar ───────────────────────────────────────────────────────────────────
  protected readonly month = signal(new Date().getMonth() + 1);
  protected readonly year = signal(new Date().getFullYear());
  protected readonly calendarEntries = signal<CalendarEntry[]>([]);
  protected readonly calendarError = signal<string | null>(null);
  protected readonly monthLabel = computed(() => new Date(Date.UTC(this.year(), this.month() - 1, 1)).toLocaleDateString('en-GB', { month: 'long', year: 'numeric', timeZone: 'UTC' }));

  protected readonly calendarDays = computed<CalendarDay[]>(() => {
    const y = this.year(); const m = this.month();
    const startOffset = (new Date(Date.UTC(y, m - 1, 1)).getUTCDay() + 6) % 7; // Monday-first grid
    const daysInMonth = new Date(Date.UTC(y, m, 0)).getUTCDate();
    const totalCells = Math.ceil((startOffset + daysInMonth) / 7) * 7; // whole weeks only, 5 or 6 rows depending on the month
    const byDate = new Map<string, CalendarEntry[]>();
    for (const e of this.calendarEntries()) { const list = byDate.get(e.date) ?? []; list.push(e); byDate.set(e.date, list); }
    const days: CalendarDay[] = [];
    for (let i = 0; i < totalCells; i++) {
      const d = new Date(Date.UTC(y, m - 1, 1 - startOffset + i));
      const iso = d.toISOString().slice(0, 10);
      days.push({ date: iso, inMonth: d.getUTCMonth() === m - 1, entries: byDate.get(iso) ?? [] });
    }
    return days;
  });

  constructor() {
    this.api.types().subscribe({ next: (rows) => this.allTypes.set(rows), error: () => undefined });
    this.registerFilters.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.loadRegister());
    this.missingFilters.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.loadMissing());
    this.loadRegister();
    this.loadMissing();
    this.loadCalendar();
  }

  protected link(ownerType: string, ownerId: number): string[] {
    return ownerLink(ownerType, ownerId);
  }

  protected dayNum(iso: string): number {
    return Number(iso.slice(8, 10));
  }

  loadRegister(): void {
    this.registerLoading.set(true);
    this.registerError.set(null);
    const f = this.registerFilters.getRawValue();
    this.api.register({ ownerType: (f.ownerType as 'Vehicle' | 'BusinessPartner' | '' | undefined) ?? '', documentTypeId: f.documentTypeId, status: f.status ?? undefined, expiringWithinDays: f.expiringWithinDays }).subscribe({
      next: (rows) => { this.registerLoading.set(false); this.register.set(rows); },
      error: (err) => { this.registerLoading.set(false); this.registerError.set(errorMessage(err, 'The register could not be loaded.')); },
    });
  }

  loadMissing(): void {
    this.missingLoading.set(true);
    this.missingError.set(null);
    const ownerType = this.missingFilters.getRawValue().ownerType;
    this.api.missing(ownerType ? (ownerType as 'Vehicle' | 'BusinessPartner') : undefined).subscribe({
      next: (rows) => { this.missingLoading.set(false); this.missing.set(rows); },
      error: (err) => { this.missingLoading.set(false); this.missingError.set(errorMessage(err, 'The missing documents report could not be loaded.')); },
    });
  }

  protected shiftMonth(delta: number): void {
    let m = this.month() + delta; let y = this.year();
    if (m < 1) { m = 12; y -= 1; } else if (m > 12) { m = 1; y += 1; }
    this.month.set(m); this.year.set(y);
    this.loadCalendar();
  }

  loadCalendar(): void {
    this.calendarError.set(null);
    this.api.calendar(this.year(), this.month()).subscribe({
      next: (rows) => this.calendarEntries.set(rows),
      error: (err) => this.calendarError.set(errorMessage(err, 'The expiry calendar could not be loaded.')),
    });
  }
}
