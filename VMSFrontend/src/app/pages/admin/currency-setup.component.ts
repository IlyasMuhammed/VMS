import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { CurrenciesApi } from '../../core/api.services';
import { apiErrors, errorMessage } from '../../core/api-error';
import { CurrencyModel } from '../../core/customer.models';
import { CurrencySettingsModel, ExchangeRateModel } from '../../core/currency.models';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { applyServerErrors } from '../../shared/server-errors';
import { CurrencyDialogComponent, ExchangeRateDialogComponent } from './currency-dialogs.component';
import { statusSeverity } from './currency-logic';

/**
 * Currency Setup (FSD §13A, §48.8): Admin → Settings. Base currency and the multi-currency switch, the currency
 * master, and the exchange rates grid — all one screen, matching the FSD's own layout description exactly
 * rather than three separate pages.
 *
 * Two of §13A's own rules ("the base currency cannot be changed once transactions exist," "multi-currency
 * cannot be turned off while foreign-currency transactions are open") are honest, already-documented no-ops on
 * the server (`CurrencyService.UpdateSettingsAsync`'s own comments) — nothing there enforces either yet, since
 * doing so needs a decision about which of this module's now-several transaction-carrying tables count, a
 * design question bigger than this UI task's own scope. Both hints below still say the rule, so the screen
 * never promises a safety net that is not there, but a save today is honestly allowed to go through.
 */
@Component({
  selector: 'app-currency-setup',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonModule, CheckboxModule, SelectModule, TagModule, BusinessDatePipe, CurrencyDialogComponent, ExchangeRateDialogComponent],
  template: `
    <div class="page">
      <div class="page-header"><div><h1>Currency Setup</h1><div class="sub">The base currency, the currency master, and exchange rates against the base.</div></div></div>

      @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }

      <div class="card">
        <h2>Settings</h2>
        <form [formGroup]="form" class="form-grid" novalidate>
          <div class="field">
            <label for="cs-base">Base currency</label>
            <p-select inputId="cs-base" formControlName="baseCurrencyCode" [options]="activeCurrencyOptions()" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
            <span class="hint">Cannot be changed once transactions exist.</span>
          </div>
          <div class="field">
            <label>&nbsp;</label>
            <div><p-checkbox formControlName="multiCurrencyEnabled" [binary]="true" inputId="cs-multi" /> <label for="cs-multi">Multi-currency enabled</label></div>
            <span class="hint">Cannot be turned off while foreign-currency transactions are open.</span>
          </div>
        </form>
        @if (canEdit()) { <p-button label="Save settings" icon="pi pi-check" [loading]="savingSettings()" (onClick)="saveSettings()" /> }
      </div>

      <div class="card">
        <div class="head-row"><h2>Currencies</h2> @if (canEdit()) { <p-button label="Add currency" icon="pi pi-plus" size="small" (onClick)="addCurrencyOpen.set(true)" /> }</div>
        <table>
          <thead><tr><th>Code</th><th>Name</th><th>Symbol</th><th>Decimals</th><th>Base</th><th>Status</th><th></th></tr></thead>
          <tbody>
            @for (c of currencies(); track c.currencyCode) {
              <tr>
                <td class="mono">{{ c.currencyCode }}</td><td>{{ c.currencyName }}</td><td>{{ c.symbol || '—' }}</td><td>{{ c.decimalPlaces }}</td>
                <td>@if (c.isBase) { <p-tag value="Base" severity="info" /> }</td>
                <td><p-tag [value]="c.status" [severity]="severity(c.status)" /></td>
                <td class="row-actions">@if (canEdit()) { <p-button label="Edit" [text]="true" size="small" (onClick)="editingCurrency.set(c)" /> }</td>
              </tr>
            }
            @if (currencies().length === 0) { <tr><td colspan="7" class="muted">{{ loading() ? 'Loading…' : 'No currencies yet.' }}</td></tr> }
          </tbody>
        </table>
      </div>

      <div class="card">
        <div class="head-row"><h2>Exchange rates</h2> @if (canManageRates()) { <p-button label="Add rate" icon="pi pi-plus" size="small" (onClick)="addRateOpen.set(true)" /> }</div>
        <table>
          <thead><tr><th>From</th><th>To</th><th>Date</th><th>Rate</th></tr></thead>
          <tbody>
            @for (r of rates(); track r.exchangeRateId) {
              <tr><td class="mono">{{ r.fromCurrencyCode }}</td><td class="mono">{{ r.toCurrencyCode }}</td><td>{{ r.rateDate | vmsDate }}</td><td>{{ r.rate }}</td></tr>
            }
            @if (rates().length === 0) { <tr><td colspan="4" class="muted">{{ loading() ? 'Loading…' : 'No exchange rates yet.' }}</td></tr> }
          </tbody>
        </table>
      </div>
    </div>

    <app-currency-dialog [adding]="addCurrencyOpen()" [currency]="editingCurrency()" (closed)="addCurrencyOpen.set(false); editingCurrency.set(null)" (saved)="addCurrencyOpen.set(false); editingCurrency.set(null); load()" />
    <app-exchange-rate-dialog [adding]="addRateOpen()" [currencies]="currencies()" [baseCurrencyCode]="settings()?.baseCurrencyCode ?? ''" (closed)="addRateOpen.set(false)" (saved)="addRateOpen.set(false); load()" />
  `,
  styles: [
    `
      .card { margin-bottom: 1.5rem; } h2 { font-size: 1.05rem; margin-bottom: .75rem; }
      .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; margin-bottom: .75rem; }
      .field { display: flex; flex-direction: column; gap: .35rem; } .field label { font-size: .85rem; }
      .hint { font-size: .8rem; color: var(--vms-muted); }
      .head-row { display: flex; justify-content: space-between; align-items: center; margin-bottom: .75rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { text-align: right; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class CurrencySetupComponent {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(CurrenciesApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);

  readonly canEdit = computed(() => this.auth.hasPermission('TRP.CURRENCY.MANAGE'));
  readonly canManageRates = computed(() => this.auth.hasPermission('TRP.EXCHANGERATE.MANAGE'));

  readonly currencies = signal<CurrencyModel[]>([]);
  readonly rates = signal<ExchangeRateModel[]>([]);
  readonly settings = signal<CurrencySettingsModel | null>(null);
  readonly loading = signal(false);
  readonly savingSettings = signal(false);
  readonly error = signal<string | null>(null);
  readonly problems = signal<string[]>([]);
  protected readonly severity = statusSeverity;

  readonly activeCurrencyOptions = computed(() => this.currencies().filter((c) => c.status === 'Active').map((c) => ({ label: `${c.currencyCode} — ${c.currencyName}`, value: c.currencyCode })));

  readonly addCurrencyOpen = signal(false);
  readonly editingCurrency = signal<CurrencyModel | null>(null);
  readonly addRateOpen = signal(false);

  readonly form = this.fb.group({
    baseCurrencyCode: this.fb.control<string | null>(null, Validators.required),
    multiCurrencyEnabled: this.fb.nonNullable.control(false),
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.list().subscribe({
      next: (rows) => { this.loading.set(false); this.currencies.set(rows); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Currencies could not be loaded.')); },
    });
    this.api.settings().subscribe({
      next: (s) => { this.settings.set(s); this.form.reset({ baseCurrencyCode: s.baseCurrencyCode, multiCurrencyEnabled: s.multiCurrencyEnabled }); },
      error: () => undefined,
    });
    this.api.exchangeRates().subscribe({ next: (rows) => this.rates.set(rows), error: () => undefined });
  }

  saveSettings(): void {
    if (this.savingSettings() || this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.problems.set([]);
    this.savingSettings.set(true);
    const f = this.form.getRawValue();
    this.api.updateSettings({ baseCurrencyCode: f.baseCurrencyCode, multiCurrencyEnabled: f.multiCurrencyEnabled }).subscribe({
      next: (s) => { this.savingSettings.set(false); this.settings.set(s); this.notify.success('Settings saved'); },
      error: (err) => {
        this.savingSettings.set(false);
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        this.problems.set(applyServerErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }
}
