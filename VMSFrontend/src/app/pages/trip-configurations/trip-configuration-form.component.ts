import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TabsModule } from 'primeng/tabs';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { map } from 'rxjs';
import { RoutesApi, TripConfigurationsApi } from '../../core/api.services';
import { apiErrors, errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { RouteModel } from '../../core/route.models';
import { TripConfigurationModel } from '../../core/trip-configuration.models';
import { ConfirmService } from '../../shared/confirm.service';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { TripCustomerPickerComponent } from '../../shared/trip-customer-picker.component';
import { TripConfigurationCopyDialogComponent } from './trip-configuration-dialogs.component';
import { TripConfigurationStopsTabComponent } from './trip-configuration-stops-tab.component';
import { TripConfigurationVehiclesTabComponent } from './trip-configuration-vehicles-tab.component';
import { TripRatesTabComponent } from './trip-rates-tab.component';
import { TRIP_DIRECTION_TYPES, configStatusSeverity, optionsOf } from './trip-configuration-logic';

type Tab = 'general' | 'stops' | 'vehicles' | 'rates';

/** A trip configuration, for a new one and for a saved one (FSD §18, §19, §26, §48.3 screens 10-11). Only the
 * General tab exists before the configuration is saved — Stops are copied from the route the moment it is
 * created, and Vehicles/Rates need a configuration id to hang off, the same "no child tabs until it exists"
 * shape as the Customer and Business Partner screens. */
@Component({
  selector: 'app-trip-configuration-form',
  standalone: true,
  imports: [
    ReactiveFormsModule, RouterLink, ButtonModule, InputTextModule, SelectModule, TabsModule, TagModule, TextareaModule, FieldComponent,
    TripCustomerPickerComponent, TripConfigurationCopyDialogComponent, TripConfigurationStopsTabComponent, TripConfigurationVehiclesTabComponent, TripRatesTabComponent,
  ],
  template: `
    <div class="page">
      <div class="crumbs"><a routerLink="/trip-configurations">Trip configurations</a> <span aria-hidden="true">/</span> <span>{{ config()?.tripCode ?? 'New configuration' }}</span></div>

      @if (loadError(); as message) {
        <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div>
      } @else if (loading()) {
        <p class="muted">Loading…</p>
      } @else {
        <header class="head">
          <div>
            <h1>{{ config()?.name || 'New configuration' }}</h1>
            @if (config(); as c) { <div class="meta"><span class="mono">{{ c.tripCode }}</span> <p-tag [value]="c.status" [severity]="severity(c.status)" /></div> }
          </div>
          @if (config(); as c) {
            <div class="actions-bar">
              @if (canEdit() && moves().length > 0) { @for (m of moves(); track m) { <p-button [label]="m === 'Active' ? 'Activate' : 'Deactivate'" severity="secondary" [outlined]="true" size="small" (onClick)="changeStatus(m)" /> } }
              @if (canEdit()) { <p-button label="Copy" icon="pi pi-copy" severity="secondary" [outlined]="true" size="small" (onClick)="copyOpen.set(true)" /> }
            </div>
          }
        </header>

        @for (w of config()?.warnings ?? []; track w) { <div class="alert warning">{{ w }}</div> }
        @if (conflict(); as message) { <div class="alert warning conflict" role="alert"><span>{{ message }}</span> <p-button label="Reload their version" size="small" (onClick)="reload()" /></div> }
        @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (m of problems(); track m) { <li>{{ m }}</li> }</ul></div> }

        <div class="card body">
          <p-tabs [value]="tab()" (valueChange)="tab.set($any($event))" [scrollable]="true">
            <p-tablist>
              <p-tab value="general">General</p-tab>
              @if (!isNew()) { <p-tab value="stops">Stops</p-tab> }
              @if (!isNew()) { <p-tab value="vehicles">Vehicles</p-tab> }
              @if (!isNew()) { <p-tab value="rates">Rates</p-tab> }
            </p-tablist>
            <p-tabpanels>
              <p-tabpanel value="general">
                <form [formGroup]="form" class="form-grid" novalidate>
                  @if (isNew()) {
                    <vms-field label="Customer" [control]="form.controls.customerId" for="tc-customer"><vms-trip-customer-picker formControlName="customerId" /></vms-field>
                    <vms-field label="Route" [control]="form.controls.routeId" for="tc-route">
                      <p-select inputId="tc-route" formControlName="routeId" [options]="routeOptions()" optionLabel="label" optionValue="value" [filter]="true" placeholder="Choose a route" [fluid]="true" appendTo="body" />
                    </vms-field>
                    <vms-field label="Trip code" [control]="form.controls.tripCode" for="tc-code" hint="Leave blank to auto-generate."><input pInputText id="tc-code" formControlName="tripCode" /></vms-field>
                  }
                  <vms-field label="Name" [control]="form.controls.name" for="tc-name"><input pInputText id="tc-name" formControlName="name" /></vms-field>
                  <vms-field label="Direction" [control]="form.controls.directionType" for="tc-direction">
                    <p-select inputId="tc-direction" formControlName="directionType" [options]="directionOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
                  </vms-field>
                  <vms-field class="full" label="Remarks" [control]="form.controls.remarks" for="tc-remarks"><textarea pTextarea id="tc-remarks" formControlName="remarks" rows="2" [fluid]="true"></textarea></vms-field>
                </form>
                @if (canEdit()) {
                  <div class="savebar-inline"><p-button [label]="isNew() ? 'Save' : 'Save changes'" icon="pi pi-check" [loading]="busy()" (onClick)="save()" /></div>
                }
              </p-tabpanel>
              @if (!isNew()) { <p-tabpanel value="stops">@if (tab() === 'stops') { <app-trip-configuration-stops-tab [configuration]="config()!" /> }</p-tabpanel> }
              @if (!isNew()) { <p-tabpanel value="vehicles">@if (tab() === 'vehicles') { <app-trip-configuration-vehicles-tab [configurationId]="config()!.tripConfigurationId" /> }</p-tabpanel> }
              @if (!isNew()) { <p-tabpanel value="rates">@if (tab() === 'rates') { <app-trip-rates-tab [configurationId]="config()!.tripConfigurationId" [customerId]="config()!.customerId" /> }</p-tabpanel> }
            </p-tabpanels>
          </p-tabs>
        </div>
      }
    </div>

    <app-trip-configuration-copy-dialog [source]="copyOpen() ? config() : null" (closed)="copyOpen.set(false)" (copied)="onCopied($event)" />
  `,
  styles: [
    `
      .crumbs { color: var(--vms-muted); margin-bottom: .75rem; font-size: .9rem; }
      .head { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1rem; }
      .head h1 { font-size: 1.5rem; font-weight: 600; } .meta { display: flex; gap: .6rem; align-items: center; margin-top: .35rem; }
      .actions-bar { display: flex; flex-wrap: wrap; gap: .5rem; } .conflict { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
      .alert { margin-bottom: 1rem; } .body { padding-bottom: 1.5rem; }
      .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; } .full { grid-column: 1 / -1; }
      .savebar-inline { margin-top: 1rem; }
    `,
  ],
})
export class TripConfigurationFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(TripConfigurationsApi);
  private readonly routesApi = inject(RoutesApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);
  private readonly confirm = inject(ConfirmService);

  readonly config = signal<TripConfigurationModel | null>(null);
  readonly routes = signal<RouteModel[]>([]);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);
  readonly conflict = signal<string | null>(null);
  readonly tab = signal<Tab>('general');
  readonly copyOpen = signal(false);

  private readonly id = toSignal(this.route.paramMap.pipe(map((p) => (p.get('id') ? Number(p.get('id')) : null))), { initialValue: null as number | null });
  readonly isNew = computed(() => this.id() === null);
  readonly canEdit = computed(() => this.auth.hasPermission('TRP.TRIPCONFIG.EDIT'));
  readonly moves = computed(() => {
    const status = this.config()?.status;
    if (status === 'Draft' || status === 'Inactive') return ['Active'] as const;
    if (status === 'Active') return ['Inactive'] as const;
    return [] as const;
  });
  protected readonly severity = configStatusSeverity;
  protected readonly directionOptions = optionsOf(TRIP_DIRECTION_TYPES);
  protected readonly routeOptions = computed(() => this.routes().filter((r) => r.status === 'Active').map((r) => ({ label: `${r.routeCode} — ${r.routeName}`, value: r.routeId })));

  readonly form = this.fb.group({
    customerId: this.fb.control<number | null>(null, Validators.required),
    routeId: this.fb.control<number | null>(null, Validators.required),
    tripCode: this.fb.nonNullable.control(''),
    name: this.fb.nonNullable.control('', Validators.required),
    directionType: this.fb.nonNullable.control<'OneWay' | 'Return' | 'RoundTrip'>('OneWay'),
    remarks: this.fb.nonNullable.control(''),
  });

  constructor() {
    this.routesApi.list(true).subscribe({ next: (r) => this.routes.set(r), error: () => undefined });
    this.route.paramMap.subscribe(() => this.load());
  }

  load(): void {
    this.problems.set([]);
    this.conflict.set(null);
    const id = this.id();
    if (id === null) {
      this.config.set(null);
      const fromQuery = this.route.snapshot.queryParamMap.get('customerId');
      this.form.reset({ customerId: fromQuery ? Number(fromQuery) : null, routeId: null, tripCode: '', name: '', directionType: 'OneWay', remarks: '' });
      this.tab.set('general');
      return;
    }

    this.loading.set(true);
    this.loadError.set(null);
    this.api.get(id).subscribe({
      next: (c) => { this.loading.set(false); this.show(c); },
      error: (err: unknown) => { this.loading.set(false); this.loadError.set(err instanceof HttpErrorResponse && err.status === 404 ? 'This configuration was not found.' : errorMessage(err, 'The configuration could not be loaded.')); },
    });
  }

  private show(c: TripConfigurationModel): void {
    this.config.set(c);
    this.form.reset({ customerId: c.customerId, routeId: c.routeId, tripCode: c.tripCode, name: c.name, directionType: c.directionType, remarks: c.remarks ?? '' });
  }

  reload(): void {
    this.load();
  }

  save(): void {
    if (this.busy() || this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.problems.set([]);
    this.conflict.set(null);
    const f = this.form.getRawValue();

    if (this.isNew()) {
      this.busy.set(true);
      this.api.create({ customerId: f.customerId!, tripCode: f.tripCode.trim() || null, name: f.name.trim(), routeId: f.routeId!, directionType: f.directionType, remarks: f.remarks.trim() || null }).subscribe({
        next: (c) => { this.busy.set(false); this.notify.success(`${c.name} saved as ${c.tripCode}`); void this.router.navigate(['/trip-configurations', c.tripConfigurationId], { replaceUrl: true }); },
        error: (err) => this.refused(err),
      });
      return;
    }

    const c = this.config();
    if (!c) return;
    this.busy.set(true);
    this.api.update(c.tripConfigurationId, { name: f.name.trim(), directionType: f.directionType, remarks: f.remarks.trim() || null, rowVersion: c.rowVersion }).subscribe({
      next: (saved) => { this.busy.set(false); this.notify.success('Configuration saved'); this.show(saved); },
      error: (err) => this.refused(err),
    });
  }

  protected changeStatus(move: 'Active' | 'Inactive'): void {
    const c = this.config();
    if (!c) return;
    const verb = move === 'Active' ? 'Activate' : 'Deactivate';
    void this.confirm.ask({ title: `${verb} configuration`, message: `${verb} ${c.name}?`, confirmLabel: verb, cancelLabel: 'Cancel', icon: 'pi pi-flag' }).then((go) => {
      if (!go) return;
      const request = move === 'Active' ? this.api.activate(c.tripConfigurationId, { rowVersion: c.rowVersion }) : this.api.deactivate(c.tripConfigurationId, { rowVersion: c.rowVersion });
      request.subscribe({ next: (r) => { this.config.set(r); this.notify.success(`Configuration is now ${r.status}`); }, error: (err) => this.notify.error(err) });
    });
  }

  protected onCopied(copy: TripConfigurationModel): void {
    this.copyOpen.set(false);
    this.notify.success(`${copy.name} saved as ${copy.tripCode}`);
    void this.router.navigate(['/trip-configurations', copy.tripConfigurationId]);
  }

  private refused(err: unknown): void {
    this.busy.set(false);
    if (err instanceof HttpErrorResponse && err.status === 409) { this.conflict.set(errorMessage(err, 'This configuration was changed by someone else while you were editing.')); return; }
    const found = apiErrors(err);
    if (found.length === 0) { this.notify.error(err); return; }
    this.problems.set(applyServerErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
  }
}
