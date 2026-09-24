import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { VehiclesApi } from '../../core/api.services';
import { apiErrors, errorMessage } from '../../core/api-error';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { ActivateVehicleRequest, ActivationCheck, Acquisition, Agreement, Vehicle } from '../../core/vehicle.models';
import { ConfirmService } from '../../shared/confirm.service';
import { toCategoryDetails } from './category-fields.component';
import { OwnershipForm } from './vehicle-ownership-step.component';
import { DRIVER_ALREADY_ASSIGNED, DraftItem, FUEL_CARD_IN_USE, itemRequests, words } from './vehicle-logic';

/**
 * Step 5 of the vehicle wizard: everything that has been entered, what is still missing, the entries and the schedule that activating
 * would write, and the Activate button (FSD §21, FR-VH-011, BR-VH-019). Looking writes nothing. Activating writes everything in one
 * transaction on the server, or nothing.
 */
@Component({
  selector: 'app-vehicle-review-step',
  standalone: true,
  imports: [DecimalPipe, ButtonModule, BusinessDatePipe],
  template: `
    <h2>Review and activate</h2>
    <p class="muted lead">Check what has been entered. Activating puts the vehicle in the fleet and writes its opening entries, dated by the acquisition date. Those entries are never edited afterwards: a mistake is corrected by reversing it.</p>

    @if (loadError(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="check()" /></div> }
    @if (conflict(); as message) { <div class="alert warning" role="alert">{{ message }}</div> }
    @if (problems().length > 0) { <div class="alert error" role="alert"><strong>The vehicle was not activated.</strong><ul>@for (m of problems(); track m) { <li>{{ m }}</li> }</ul></div> }

    <section aria-labelledby="rv-summary">
      <h3 id="rv-summary">What has been entered</h3>
      <dl class="facts">
        <div><dt>Vehicle</dt><dd>{{ vehicle().registrationNo }} · {{ vehicle().model }} · {{ vehicle().fuelType }}</dd></div>
        <div><dt>Ownership</dt><dd>{{ categoryWords() || 'Not chosen yet' }}</dd></div>
        <div><dt>Attached items</dt><dd>{{ items().length === 0 ? 'None' : items().length + (items().length === 1 ? ' item' : ' items') }}</dd></div>
        <div><dt>Acquired</dt><dd>@if (acquisition()?.acquisitionDate) { {{ acquisition()!.acquisitionDate | vmsDate }} · {{ words(acquisition()!.acquisitionType) }} } @else { Not entered yet }</dd></div>
        @if (acquisition()?.purchasePrice !== undefined && acquisition()?.purchasePrice !== null) {
          <div><dt>Price and paid</dt><dd>PKR {{ acquisition()!.purchasePrice | number: '1.2-2' }} · paid {{ (acquisition()!.amountPaid ?? 0) | number: '1.2-2' }}</dd></div>
        }
        @if (agreement(); as a) {
          <div><dt>Bank finance</dt><dd>{{ a.bank?.name }} · {{ a.agreementNo }}@if (a.installmentAmount !== undefined) { · {{ a.tenure }} × {{ a.installmentAmount | number: '1.2-2' }} {{ words(a.frequency).toLowerCase() }} }</dd></div>
        }
      </dl>
    </section>

    <section aria-labelledby="rv-checklist">
      <h3 id="rv-checklist">What is needed</h3>
      @if (result(); as r) {
        <ul class="checklist">
          @for (item of r.items; track item.code) {
            <li [class.bad]="!item.ok && item.blocking" [class.warn]="!item.ok && !item.blocking">
              <i class="pi" [class.pi-check-circle]="item.ok" [class.pi-times-circle]="!item.ok && item.blocking" [class.pi-exclamation-triangle]="!item.ok && !item.blocking" aria-hidden="true"></i>
              <span><strong>{{ item.label }}</strong>@if (item.message) { <span class="muted"> — {{ item.message }}</span> }</span>
              <span class="sr-only">{{ item.ok ? 'Done' : item.blocking ? 'Missing' : 'Warning' }}</span>
            </li>
          }
        </ul>
      } @else if (checking()) { <p class="muted">Checking…</p> }
    </section>

    @if (result(); as r) {
      <section aria-labelledby="rv-postings">
        <h3 id="rv-postings">Entries that will be written</h3>
        @if (r.postings.length === 0) { <p class="muted">None. Nothing was bought or paid for, or the price has not been entered.</p> }
        @else {
          <table>
            <thead><tr><th>Entry</th><th>With</th><th>Date</th><th class="num">Amount (PKR)</th><th>Reference</th></tr></thead>
            <tbody>@for (p of r.postings; track $index) {
              <tr><td>{{ words(p.type) }}@if (p.subType) { <span class="muted"> · {{ words(p.subType) }}</span> }</td><td>{{ p.partner?.name || '—' }}</td><td class="nowrap">{{ p.date | vmsDate }}</td>
                <td class="num">@if (p.amount !== undefined) { {{ p.amount | number: '1.2-2' }} } @else { — }</td><td>{{ p.reference }}</td></tr>
            }</tbody>
          </table>
        }
      </section>

      @if (r.schedule.length > 0) {
        <section aria-labelledby="rv-schedule">
          <h3 id="rv-schedule">Installment schedule</h3>
          <p class="muted">{{ r.schedule.length }} rows, generated from the agreement. If the bank's dates are not regular, correct them here before activating.</p>
          <div class="scroll">
            <table>
              <thead><tr><th>No.</th><th>Due date</th><th class="num">Amount (PKR)</th><th></th></tr></thead>
              <tbody>@for (row of r.schedule; track row.installmentNo) {
                <tr>
                  <td>{{ row.isResidual ? 'Final' : row.installmentNo }}</td>
                  <td><input type="date" class="date" [value]="row.dueDate" (change)="dueDateChanged(row.installmentNo, $any($event.target).value)" [attr.aria-label]="'Due date of installment ' + row.installmentNo" /></td>
                  <td class="num">@if (row.amount !== undefined) { {{ row.amount | number: '1.2-2' }} } @else { — }</td>
                  <td class="muted">{{ row.isResidual ? 'Residual, due with the last installment' : '' }}@if (edited(row.installmentNo)) { <span class="chip chip--info">changed</span> }</td>
                </tr>
              }</tbody>
            </table>
          </div>
        </section>
      }
    }

    <div class="actions">
      <span class="muted">{{ hint() }}</span>
      <span class="buttons">
        <p-button label="Check again" icon="pi pi-refresh" severity="secondary" [outlined]="true" [loading]="checking()" (onClick)="check()" />
        <p-button label="Activate vehicle" icon="pi pi-check" [loading]="busy()" [disabled]="!canActivate()" (onClick)="activate()" />
      </span>
    </div>
  `,
  styles: [
    `
      h2 { font-size: 1.05rem; margin: 0 0 .25rem; } h3 { font-size: .95rem; margin: 1.5rem 0 .5rem; } .lead { margin: 0 0 .5rem; }
      .alert { margin-bottom: 1rem; } .alert ul { margin: .35rem 0 0; padding-left: 1.25rem; }
      .facts { display: grid; grid-template-columns: repeat(auto-fill, minmax(18rem, 1fr)); gap: .75rem 2rem; margin: 0; } dt { color: var(--vms-muted); font-size: .8rem; } dd { margin: .1rem 0 0; font-weight: 500; }
      .checklist { list-style: none; margin: 0; padding: 0; display: grid; gap: .4rem; } .checklist li { display: flex; gap: .6rem; align-items: baseline; }
      .checklist .pi-check-circle { color: var(--vms-success); } .checklist .bad .pi { color: var(--vms-danger); } .checklist .warn .pi { color: var(--vms-warning); }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .4rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: middle; } th { color: var(--vms-muted); font-weight: 600; }
      .num { text-align: right; font-variant-numeric: tabular-nums; } .nowrap { white-space: nowrap; }
      .scroll { max-height: 22rem; overflow: auto; border: 1px solid var(--vms-border); border-radius: var(--vms-radius); }
      .date { font: inherit; padding: .2rem .4rem; border: 1px solid var(--vms-border); border-radius: var(--vms-radius); background: var(--vms-surface); color: inherit; }
      .actions { display: flex; align-items: center; justify-content: space-between; gap: 1rem; flex-wrap: wrap; margin-top: 1.5rem; padding-top: 1rem; border-top: 1px solid var(--vms-border); }
      .buttons { display: inline-flex; gap: .5rem; }
      .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
    `,
  ],
})
export class VehicleReviewStepComponent {
  private readonly api = inject(VehiclesApi);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);
  private readonly confirm = inject(ConfirmService);

  readonly vehicle = input.required<Vehicle>();
  readonly ownership = input.required<OwnershipForm>();
  /** The items entered on step 4: they are attached, and their costs posted, when the vehicle is activated. */
  readonly items = input<readonly DraftItem[]>([]);
  /** Step 1 or 2 has edits that are not saved: what would be activated is not what is on screen. */
  readonly unsaved = input(false);
  /** The vehicle is in the fleet: the wizard takes over from here. */
  readonly activated = output<Vehicle>();

  protected readonly result = signal<ActivationCheck | null>(null);
  protected readonly checking = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly conflict = signal<string | null>(null);
  protected readonly problems = signal<string[]>([]);
  protected readonly acquisition = signal<Acquisition | null>(null);
  protected readonly agreement = signal<Agreement | null>(null);
  /** Due dates the person corrected, by installment number. */
  private readonly changedDates = signal<Record<number, string>>({});
  private generated: Record<number, string> = {};
  protected readonly words = words;

  protected readonly categoryWords = computed(() => {
    this.result();
    return words(this.ownership().controls.category.value);
  });
  protected readonly canActivate = computed(() => (this.result()?.canActivate ?? false) && !this.unsaved() && !this.busy() && !this.checking());
  protected readonly hint = computed(() =>
    this.unsaved() ? 'Save your changes on the other steps first: what is activated is what is saved.' : this.result()?.canActivate === false ? 'Something above is still missing.' : '',
  );

  constructor() {
    effect(() => {
      this.vehicle().id;
      untracked(() => { this.loadSummary(); this.check(); });
    });
  }

  private request(reassignFuelCard = false, releaseFromOther = false): ActivateVehicleRequest {
    const o = this.ownership();
    // Only the dates that differ from the generated ones are sent: the rest follow the agreement.
    const dueDates = Object.entries(this.changedDates()).filter(([no, due]) => this.generated[Number(no)] !== due).map(([no, due]) => ({ installmentNo: Number(no), dueDate: due }));
    return { category: o.controls.category.value ?? '', details: toCategoryDetails(o.controls.details), items: itemRequests(this.items()), dueDates, reassignFuelCard, releaseFromOther, rowVersion: this.vehicle().rowVersion };
  }

  private loadSummary(): void {
    const id = this.vehicle().id;
    this.api.acquisition(id).subscribe({ next: (a) => this.acquisition.set(a), error: () => this.acquisition.set(null) });
    this.api.finance(id).subscribe({ next: (a) => this.agreement.set(a), error: () => this.agreement.set(null) });
  }

  edited(no: number): boolean {
    const now = this.changedDates()[no];
    return now !== undefined && now !== this.generated[no];
  }

  dueDateChanged(no: number, value: string): void {
    if (!value) return;
    this.changedDates.update((c) => ({ ...c, [no]: value }));
    this.check();
  }

  check(): void {
    this.checking.set(true);
    this.loadError.set(null);
    this.api.activationCheck(this.vehicle().id, this.request()).subscribe({
      next: (r) => {
        this.checking.set(false);
        // The first answer shows the schedule as the agreement generates it: what a corrected date is judged against.
        if (Object.keys(this.generated).length === 0 && Object.keys(this.changedDates()).length === 0) this.generated = Object.fromEntries(r.schedule.map((s) => [s.installmentNo, s.dueDate]));
        this.result.set(r);
      },
      error: (err: unknown) => { this.checking.set(false); this.loadError.set(errorMessage(err, 'The checklist could not be loaded.')); },
    });
  }

  async activate(reassignFuelCard = false, releaseFromOther = false): Promise<void> {
    if (!this.canActivate() && !reassignFuelCard && !releaseFromOther) return;
    this.problems.set([]);
    this.conflict.set(null);
    if (!reassignFuelCard && !releaseFromOther) {
      const go = await this.confirm.ask({
        title: `Activate ${this.vehicle().registrationNo}?`,
        message: 'The vehicle goes into the fleet and its opening entries are written. They cannot be edited afterwards, only reversed.',
        confirmLabel: 'Activate', cancelLabel: 'Not yet', icon: 'pi pi-check-circle',
      });
      if (!go) return;
    }
    this.busy.set(true);
    this.api.activate(this.vehicle().id, this.request(reassignFuelCard, releaseFromOther)).subscribe({
      next: (v) => { this.busy.set(false); this.notify.success(`${v.registrationNo} is active`); this.activated.emit(v); },
      error: (err: unknown) => void this.refused(err, reassignFuelCard, releaseFromOther),
    });
  }

  private async refused(err: unknown, reassignFuelCard: boolean, releaseFromOther: boolean): Promise<void> {
    this.busy.set(false);
    if (err instanceof HttpErrorResponse && err.status === 409) {
      this.conflict.set(errorMessage(err, 'The vehicle changed while it was being activated. Nothing was saved.'));
      return;
    }
    const found = apiErrors(err);
    if (found.length === 0) return this.notify.error(err);

    // The fuel card or the driver is on another vehicle: ask, and if the person agrees send the same activation again with the yes.
    const card = found.find((e) => e.code === FUEL_CARD_IN_USE);
    const driver = found.find((e) => e.code === DRIVER_ALREADY_ASSIGNED);
    if ((card || driver) && found.every((e) => e === card || e === driver)) {
      for (const question of [card, driver]) {
        if (!question) continue;
        const yes = await this.confirm.ask({ title: question === card ? 'Fuel card in use' : 'Driver already assigned', message: this.messages.describe(question), confirmLabel: question === card ? 'Reassign it' : 'Release it', cancelLabel: 'Not now', icon: 'pi pi-question-circle' });
        if (!yes) return;
      }
      return this.activate(reassignFuelCard || !!card, releaseFromOther || !!driver);
    }
    this.problems.set(found.map((e) => this.messages.describe(e)));
    this.check();
  }
}
