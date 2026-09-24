import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, HostListener, ViewChild, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { map } from 'rxjs';
import { VehiclesApi } from '../../core/api.services';
import { apiErrors, errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { HasUnsavedChanges } from '../../core/unsaved-changes.guard';
import { Acquisition, Vehicle, VehicleListItem } from '../../core/vehicle.models';
import { ConfirmService } from '../../shared/confirm.service';
import { applyServerErrors } from '../../shared/server-errors';
import { WizardStep, WizardStepsComponent } from '../../shared/wizard-steps.component';
import { VehicleStatusComponent } from './vehicle-bits.component';
import { VehicleDetailsStepComponent } from './vehicle-details-step.component';
import { VehicleFinanceStepComponent } from './vehicle-finance-step.component';
import { VehicleItemsStepComponent } from './vehicle-items-step.component';
import { RecurringChargesTabComponent } from './vehicle-recurring-charges-tab.component';
import { VehicleOwnershipStepComponent, buildOwnershipForm, patchOwnership, toOwnership } from './vehicle-ownership-step.component';
import { VehicleReviewStepComponent } from './vehicle-review-step.component';
import { buildVehicleForm, patchVehicle, problemCount, toUpdateRequest, toVehicleInput } from './vehicle-form';
import { DraftItem, FUEL_CARD_IN_USE, WIZARD_STEPS, isDraft, isDisposed, mayEnterFinance, parseDraftData, serializeDraftData, stepReachable } from './vehicle-logic';

/**
 * Adding a vehicle, as the five-step wizard of FSD §21, and editing one that is already saved. A new vehicle is saved as a
 * Draft from step 1 (FR-VH-012). Step 1 is built; the steps after it say what they wait for, so nobody wonders whether they are
 * missing. The steps only draw parts of one form and the bar above them only says which is open, so the layout can change alone.
 */
@Component({
  selector: 'app-vehicle-wizard',
  standalone: true,
  imports: [RouterLink, FormsModule, ButtonModule, DialogModule, InputTextModule, WizardStepsComponent, VehicleDetailsStepComponent, VehicleOwnershipStepComponent, VehicleFinanceStepComponent, VehicleItemsStepComponent, RecurringChargesTabComponent, VehicleReviewStepComponent, VehicleStatusComponent],
  template: `
    <div class="page">
      <div class="crumbs"><a routerLink="/vehicles">Vehicles</a> <span aria-hidden="true">/</span> <span>{{ vehicle()?.registrationNo ?? 'New vehicle' }}</span></div>

      @if (loadError(); as message) {
        <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div>
      } @else if (loading()) {
        <p class="muted">Loading…</p>
      } @else {
        <header class="head">
          <div>
            <h1>{{ vehicle() ? 'Edit ' + vehicle()!.registrationNo : 'New vehicle' }}</h1>
            @if (vehicle(); as v) { <div class="meta"><span class="mono">{{ v.vehicleCode }}</span> <vms-vehicle-status [status]="v.status" /></div> }
          </div>
          @if (isNew() && canCreate()) { <p-button label="Copy from an existing vehicle" icon="pi pi-copy" severity="secondary" [outlined]="true" (onClick)="openCopy()" /> }
        </header>

        @if (conflict(); as message) { <div class="alert warning conflict" role="alert"><span>{{ message }}</span><p-button label="Reload their version" size="small" (onClick)="reload()" /></div> }
        @if (problems().length > 0) { <div class="alert error" role="alert"><strong>This could not be saved.</strong><ul>@for (m of problems(); track m) { <li>{{ m }}</li> }</ul></div> }

        @if (steps().length > 1) { <vms-wizard-steps [steps]="steps()" [active]="active()" (chosen)="go($event)" /> }

        <div class="card body">
          @switch (active()) {
            @case (0) { <app-vehicle-details-step [form]="form" [mayEnterAcquisition]="mayEnterAcquisition() && !draftLike()" /> }
            @case (1) { <app-vehicle-ownership-step [form]="ownership" [mayEnterFinance]="auth.hasPermission('VEH.FIELD.FINANCE.VIEW')" /> }
            @case (2) {
              @if (!vehicle()) {
                <div class="pending"><h2>{{ stepDef().label }}</h2><p class="muted">Save this vehicle as a draft first. Its acquisition and finance are entered once it has a code.</p></div>
              } @else if (!canEnterFinance()) {
                <div class="pending"><h2>{{ stepDef().label }}</h2><p class="muted">The acquisition and finance of a vehicle are entered by someone who can see what it costs and what is financed. Ask your finance team to complete this step; the vehicle can be saved as a draft meanwhile.</p></div>
              } @else {
                <app-vehicle-finance-step [vehicleId]="vehicle()!.id" (saved)="acquisitionSaved($event)" />
              }
            }
            @case (3) {
              <app-vehicle-items-step [(items)]="draftItems" [mayEnterCost]="auth.hasPermission('VEH.FIELD.COST.VIEW')" (edited)="itemsDirty.set(true)" />
              @if (vehicle(); as v) {
                <h2 class="charges-head">Recurring charges</h2>
                <p class="muted lead">Insurance, tracker fees, rent and anything else this vehicle owes on a schedule. Bank Installment is not set up here: it follows the finance agreement automatically once the vehicle is activated.</p>
                <app-recurring-charges-tab [vehicleId]="v.id" />
              } @else {
                <h2 class="charges-head">Recurring charges</h2>
                <p class="muted">Save this vehicle as a draft first; recurring charges are configured once it has a code.</p>
              }
            }
            @case (4) {
              @if (!vehicle()) {
                <div class="pending"><h2>{{ stepDef().label }}</h2><p class="muted">Save this vehicle as a draft first. It is activated from here once its details, ownership, acquisition and finance are in.</p></div>
              } @else if (!canActivateVehicle()) {
                <div class="pending"><h2>{{ stepDef().label }}</h2><p class="muted">Activating a vehicle needs permission to activate. The vehicle is saved as a draft until someone with that permission activates it.</p></div>
              } @else {
                <app-vehicle-review-step [vehicle]="vehicle()!" [ownership]="ownership" [items]="draftItems()" [unsaved]="unsaved()" (activated)="activated($event)" />
              }
            }
            @default {
              <div class="pending">
                <h2>{{ stepDef().label }}</h2>
                <p class="muted">{{ stepDef().pending }}</p>
                <p class="muted">Save this vehicle as a draft now; its ownership and finance are entered when those steps arrive.</p>
              </div>
            }
          }
        </div>

        <div class="savebar">
          <span class="muted">{{ unsaved() ? 'You have unsaved changes.' : isNew() ? 'Fields marked * are required.' : 'All changes are saved.' }}</span>
          <span class="buttons">
            <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="cancel()" />
            @if (active() > 0) { <p-button label="Back" icon="pi pi-arrow-left" severity="secondary" [outlined]="true" (onClick)="go(active() - 1)" /> }
            @if (canContinue()) { <p-button label="Next" icon="pi pi-arrow-right" iconPos="right" severity="secondary" [outlined]="true" (onClick)="next()" /> }
            @if (canSave() && activeKey() !== 'acquisition' && activeKey() !== 'review') { <p-button [label]="isNew() ? 'Save draft' : 'Save changes'" icon="pi pi-check" [loading]="busy()" [disabled]="!isNew() && !dirty()" (onClick)="save()" /> }
          </span>
        </div>
      }
    </div>

    <p-dialog header="Copy from an existing vehicle" [(visible)]="copyOpen" [modal]="true" [style]="{ width: '560px' }">
      <p class="muted">Type, make, model, capacity, axle and body are copied. Registration, chassis and engine are left blank.</p>
      <input pInputText class="search" [ngModel]="copyTerm()" (ngModelChange)="searchCopy($event)" placeholder="Search registration, chassis, engine or code" aria-label="Search vehicles" autocomplete="off" />
      @if (copyTerm().length > 0 && copyTerm().length < 3) { <div class="hint">Type at least 3 characters.</div> }
      <ul class="results">
        @for (r of copyResults(); track r.id) {
          <li><button type="button" (click)="copyFrom(r)"><span class="mono">{{ r.registrationNo }}</span> <span class="muted">{{ r.vehicleType }} · {{ r.make }} {{ r.model }}</span></button></li>
        }
        @if (copyTerm().length >= 3 && copyResults().length === 0 && !copyBusy()) { <li class="muted">No vehicle matches.</li> }
      </ul>
    </p-dialog>
  `,
  styles: [
    `
      .crumbs { color: var(--vms-muted); margin-bottom: .75rem; font-size: .9rem; }
      .head { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1rem; }
      .head h1 { font-size: 1.5rem; font-weight: 600; }
      .meta { display: flex; align-items: center; gap: .6rem; margin-top: .35rem; }
      .alert { margin-bottom: 1rem; } .alert ul { margin: .35rem 0 0; padding-left: 1.25rem; }
      .conflict { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
      .body { padding-bottom: 1.5rem; }
      .charges-head { font-size: 1.05rem; margin: 2rem 0 .25rem; } .lead { margin: 0 0 .75rem; color: var(--vms-muted); }
      .pending h2 { font-size: 1.1rem; margin-bottom: .5rem; }
      .savebar { position: sticky; bottom: 0; z-index: 5; display: flex; align-items: center; justify-content: space-between; gap: 1rem; flex-wrap: wrap; margin: 1rem -2rem -1.5rem; padding: .85rem 2rem; background: var(--vms-surface); border-top: 1px solid var(--vms-border); }
      .buttons { display: inline-flex; gap: .5rem; }
      .search { width: 100%; margin: .5rem 0; } .hint { color: var(--vms-muted); font-size: .8rem; }
      .results { list-style: none; margin: .5rem 0 0; padding: 0; max-height: 18rem; overflow: auto; }
      .results button { width: 100%; text-align: left; border: 0; background: none; padding: .55rem .5rem; font: inherit; cursor: pointer; border-bottom: 1px solid var(--vms-border); color: inherit; }
      .results button:hover { background: var(--vms-neutral-bg); }
    `,
  ],
})
export class VehicleWizardComponent implements HasUnsavedChanges {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(VehiclesApi);
  protected readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);

  @ViewChild(VehicleDetailsStepComponent) private details?: VehicleDetailsStepComponent;
  @ViewChild(VehicleFinanceStepComponent) private financeStep?: VehicleFinanceStepComponent;

  readonly form = buildVehicleForm(this.fb);
  /** Step 2. Kept with the draft (`draftData`) and saved by the same Save as step 1. */
  readonly ownership = buildOwnershipForm(this.fb);
  /** Step 4. Kept with the draft (`draftData`) and saved by the same Save; attached when the vehicle is activated. */
  readonly draftItems = signal<DraftItem[]>([]);
  readonly itemsDirty = signal(false);
  readonly vehicle = signal<Vehicle | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);
  readonly conflict = signal<string | null>(null);
  readonly active = signal(0);

  private readonly id = toSignal(this.route.paramMap.pipe(map((p) => (p.get('id') ? Number(p.get('id')) : null))), { initialValue: null as number | null });
  readonly isNew = computed(() => this.id() === null);
  readonly canCreate = computed(() => this.auth.hasPermission('VEH.CREATE'));
  readonly mayEnterAcquisition = computed(() => this.auth.hasPermission('VEH.ACQUISITION.EDIT'));
  readonly canActivateVehicle = computed(() => this.auth.hasPermission('VEH.ACTIVATE'));
  readonly canEnterFinance = computed(() => mayEnterFinance((p) => this.auth.hasPermission(p)));
  /** A new vehicle or a Draft is still being entered; the acquisition is then set on step 3, not on step 1. */
  readonly draftLike = computed(() => this.isNew() || isDraft(this.vehicle()?.status ?? 'Draft'));
  readonly canSave = computed(() => (this.isNew() ? this.canCreate() : this.auth.hasPermission('VEH.EDIT')));

  private readonly tick = signal(0);
  readonly dirty = computed(() => (this.tick(), this.form.dirty || this.ownership.dirty || this.itemsDirty()));
  readonly stepDef = computed(() => WIZARD_STEPS[this.active()]);
  readonly activeKey = computed(() => this.steps()[this.active()]?.key ?? 'details');

  /** A vehicle already in the fleet has only its details to edit; the other steps belong to a Draft being entered. */
  readonly steps = computed<WizardStep[]>(() => {
    this.tick();
    const draftLike = this.draftLike();
    const valid = this.form.valid;
    return WIZARD_STEPS.filter((_, i) => draftLike || i === 0).map((s, i) => ({
      key: s.key, label: s.label, errors: i === 0 ? problemCount(this.form) : 0, reachable: stepReachable(i, valid), pending: !!s.pending,
    }));
  });
  readonly canContinue = computed(() => this.steps().length > 1 && this.active() < this.steps().length - 1);

  // Copy from an existing vehicle (FR-VH-014).
  copyOpen = false;
  readonly copyTerm = signal('');
  readonly copyResults = signal<VehicleListItem[]>([]);
  readonly copyBusy = signal(false);
  private copyTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.form.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.tick.update((n) => n + 1));
    this.ownership.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.tick.update((n) => n + 1));
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
  }

  /** Step 1's form, or step 3's blocks (which it saves itself), have edits that are not saved. */
  protected unsaved(): boolean {
    return this.dirty() || (this.financeStep?.dirty() ?? false);
  }

  hasUnsavedChanges(): boolean {
    return ((this.form.dirty || this.ownership.dirty || this.itemsDirty()) && !this.busy()) || (this.financeStep?.dirty() ?? false);
  }

  @HostListener('window:beforeunload', ['$event'])
  protected warnOnUnload(event: BeforeUnloadEvent): void {
    if (this.form.dirty || this.ownership.dirty || this.itemsDirty() || this.financeStep?.dirty()) event.preventDefault();
  }

  // ── Loading ─────────────────────────────────────────────────────────────────────

  load(): void {
    this.problems.set([]);
    this.conflict.set(null);
    const id = this.id();
    if (id === null) {
      this.vehicle.set(null);
      this.loadError.set(null);
      this.form.reset({ registrationNo: '' });
      this.form.markAsPristine();
      patchOwnership(this.ownership, null);
      this.ownership.markAsPristine();
      this.draftItems.set([]);
      this.itemsDirty.set(false);
      this.active.set(0);
      queueMicrotask(() => this.details?.sync());
      return;
    }
    this.loading.set(true);
    this.loadError.set(null);
    this.api.get(id).subscribe({
      next: (v) => {
        this.loading.set(false);
        if (isDisposed(v.status)) {
          this.notify.info('This vehicle has left the fleet and can no longer be edited.');
          void this.router.navigate(['/vehicles', v.id], { replaceUrl: true });
          return;
        }
        this.vehicle.set(v);
        patchVehicle(this.form, v);
        this.form.markAsPristine();
        this.form.markAsUntouched();
        const draft = parseDraftData(v.draftData);
        patchOwnership(this.ownership, draft.ownership);
        this.ownership.markAsPristine();
        this.draftItems.set(draft.items);
        this.itemsDirty.set(false);
        queueMicrotask(() => this.details?.sync());
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.loadError.set(err instanceof HttpErrorResponse && err.status === 404 ? 'This vehicle was not found. It may belong to another company, or the link is wrong.' : errorMessage(err, 'The vehicle could not be loaded.'));
      },
    });
  }

  reload(): void {
    this.form.markAsPristine();
    this.load();
  }

  // ── Moving between steps ────────────────────────────────────────────────────────

  async go(index: number): Promise<void> {
    if (index !== this.active() && this.activeKey() === 'acquisition' && this.financeStep?.dirty()) {
      const leave = await this.confirm.ask({ title: 'Leave without saving?', message: 'You have unsaved changes to the acquisition and finance. They will be lost.', confirmLabel: 'Leave', cancelLabel: 'Stay', icon: 'pi pi-exclamation-triangle' });
      if (!leave) return;
    }
    if (index > 0 && !this.form.valid) {
      this.form.markAllAsTouched();
      this.tick.update((n) => n + 1);
      this.notify.warn('Finish the vehicle details first: steps 2 to 5 open once step 1 is valid.', 'Not yet');
      return;
    }
    this.active.set(Math.max(0, Math.min(index, this.steps().length - 1)));
  }

  next(): void {
    void this.go(this.active() + 1);
  }

  /** The vehicle is in the fleet: nothing here is unsaved any more, so leave the wizard for its own screen. */
  activated(v: Vehicle): void {
    this.form.markAsPristine();
    this.ownership.markAsPristine();
    this.itemsDirty.set(false);
    void this.router.navigate(['/vehicles', v.id]);
  }

  /** The vehicle row changed under step 1 (its acquisition date, type and row version): follow it, so saving step 1 next does not undo it or be refused. */
  acquisitionSaved(a: Acquisition): void {
    const v = this.vehicle();
    if (!v) return;
    this.vehicle.set({ ...v, acquisitionDate: a.acquisitionDate ?? null, acquisitionType: a.acquisitionType ?? null, rowVersion: a.rowVersion });
    this.form.controls.acquisitionDate.setValue(a.acquisitionDate ?? null, { emitEvent: false });
    this.form.controls.acquisitionType.setValue(a.acquisitionType ?? null, { emitEvent: false });
  }

  cancel(): void {
    void this.router.navigate(this.vehicle() ? ['/vehicles', this.vehicle()!.id] : ['/vehicles']);
  }

  // ── Saving ──────────────────────────────────────────────────────────────────────

  async save(reassignFuelCard = false): Promise<void> {
    if (this.busy()) return;
    this.problems.set([]);
    this.conflict.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.tick.update((n) => n + 1);
      this.active.set(0);
      this.notify.warn('Please correct the highlighted fields.', 'Not saved');
      return;
    }

    const current = this.vehicle();
    const request$ = current
      ? this.api.update(current.id, toUpdateRequest(this.form, this.mayEnterAcquisition(), current.rowVersion, reassignFuelCard, this.draftData()))
      : this.api.create(toVehicleInput(this.form, this.mayEnterAcquisition(), reassignFuelCard, this.draftData()));
    this.busy.set(true);
    request$.subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.form.markAsPristine();
        this.ownership.markAsPristine();
        this.itemsDirty.set(false);
        if (!current) {
          this.notify.success(`Draft saved as ${saved.vehicleCode}`);
          void this.router.navigate(['/vehicles', saved.id, 'edit'], { replaceUrl: true });
          return;
        }
        this.notify.success('Changes saved');
        this.vehicle.set(saved);
        patchVehicle(this.form, saved);
        this.form.markAsPristine();
      },
      error: (err: unknown) => void this.refused(err),
    });
  }

  /** What a Draft keeps for step 2. A vehicle already in the fleet keeps nothing: its ownership is a relation, changed on its own screen. */
  private draftData(): string | null | undefined {
    return this.draftLike() ? serializeDraftData(toOwnership(this.ownership), this.draftItems()) : undefined;
  }

  private async refused(err: unknown): Promise<void> {
    this.busy.set(false);
    if (err instanceof HttpErrorResponse && err.status === 409) {
      this.conflict.set(errorMessage(err, 'This vehicle was changed by someone else while you were editing.'));
      return;
    }
    const found = apiErrors(err);
    if (found.length === 0) return this.notify.error(err);

    // The fuel card is on another vehicle: ask, and if the person agrees, send the same save again with the yes (VAL-VH-018).
    const card = found.find((e) => e.code === FUEL_CARD_IN_USE);
    if (card && found.length === 1) {
      const go = await this.confirm.ask({ title: 'Fuel card in use', message: this.messages.describe(card), confirmLabel: 'Reassign it', cancelLabel: 'Keep it there', icon: 'pi pi-credit-card' });
      if (go) await this.save(true);
      return;
    }
    const unplaced = applyServerErrors(this.form, found, this.messages);
    this.problems.set(unplaced.map((e) => this.messages.describe(e)));
    this.active.set(0);
    this.tick.update((n) => n + 1);
  }

  // ── Copy from an existing vehicle ───────────────────────────────────────────────

  openCopy(): void {
    this.copyTerm.set('');
    this.copyResults.set([]);
    this.copyOpen = true;
  }

  searchCopy(term: string): void {
    this.copyTerm.set(term);
    clearTimeout(this.copyTimer);
    if (term.trim().length < 3) {
      this.copyResults.set([]);
      return;
    }
    this.copyTimer = setTimeout(() => {
      this.copyBusy.set(true);
      this.api.list({ page: 1, pageSize: 8, search: term.trim(), filters: {} }).subscribe({
        next: (page) => { this.copyBusy.set(false); this.copyResults.set(page.items); },
        error: () => { this.copyBusy.set(false); this.copyResults.set([]); },
      });
    }, 300);
  }

  copyFrom(row: VehicleListItem): void {
    this.api.get(row.id).subscribe({
      next: (v) => {
        const c = this.form.controls;
        c.vehicleTypeId.setValue(v.vehicleTypeId);
        c.makeId.setValue(v.makeId);
        c.model.setValue(v.model);
        c.loadCapacity.setValue(v.loadCapacity ?? null);
        c.capacityUnit.setValue(v.capacityUnit ?? null);
        c.axleConfigurationId.setValue(v.axleConfigurationId ?? null);
        c.bodyTypeId.setValue(v.bodyTypeId ?? null);
        this.form.markAsDirty();
        this.copyOpen = false;
        this.details?.sync();
        this.notify.success(`Copied from ${v.registrationNo}. Now enter this vehicle's registration, chassis and engine numbers.`);
      },
      error: (err) => this.notify.error(err),
    });
  }
}
