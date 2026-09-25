import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { map } from 'rxjs';
import { CitiesApi, RoutesApi } from '../../core/api.services';
import { apiErrors, errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { CityModel } from '../../core/city.models';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { RouteModel, RouteStopInput, RouteStopType } from '../../core/route.models';
import { FieldComponent } from '../../shared/field.component';
import { TripCityPickerComponent } from '../../shared/trip-city-picker.component';
import { applyServerErrors } from '../../shared/server-errors';
import { moveStop, previewString, statusSeverity, stopProblems } from './route-logic';
import { cityDisplay } from '../cities/city-logic';

let nextRowId = 1;
interface StopRow {
  id: number;
  cityId: number | null;
  stopType: RouteStopType;
  plannedDurationMin: number | null;
  remarks: string;
}

/** Middle stops choose freely among these; the first and last rows are fixed to Origin/Destination by this
 * screen itself, never offered as a choice, so the server's own "first must be Origin" rule can never be broken
 * from here. */
const MIDDLE_STOP_TYPES: RouteStopType[] = ['Pickup', 'Via', 'Delivery'];

/**
 * Add or edit a route (FSD §17, §48.3 screen 9): code, name and distance, plus its ordered stops. The FSD's own
 * wireframe calls for drag-and-drop reordering; this screen offers the same capability — add, remove, reorder,
 * choose a city and a stop type per row — through up/down buttons instead, since the codebase has no drag-and-drop
 * library yet and one row swap needs none (see `moveStop` in `route-logic.ts`).
 */
@Component({
  selector: 'app-route-form',
  standalone: true,
  imports: [ReactiveFormsModule, FormsModule, RouterLink, ButtonModule, InputTextModule, InputNumberModule, CheckboxModule, SelectModule, TextareaModule, TagModule, FieldComponent, TripCityPickerComponent],
  template: `
    <div class="page">
      <div class="crumbs"><a routerLink="/routes">Routes</a> <span aria-hidden="true">/</span> <span>{{ route()?.routeCode ?? 'New route' }}</span></div>

      @if (loadError(); as message) {
        <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div>
      } @else if (loading()) {
        <p class="muted">Loading…</p>
      } @else {
        <header class="head">
          <div>
            <h1>{{ route()?.routeName || 'New route' }}</h1>
            @if (route(); as r) { <div class="meta"><span class="mono">{{ r.routeCode }}</span> <p-tag [value]="r.status" [severity]="severity(r.status)" /></div> }
          </div>
        </header>

        @if (conflict(); as message) { <div class="alert warning conflict" role="alert"><span>{{ message }}</span> <p-button label="Reload their version" size="small" (onClick)="reload()" /></div> }
        @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (m of problems(); track m) { <li>{{ m }}</li> }</ul></div> }

        <div class="card">
          <h2>Details</h2>
          <form [formGroup]="form" class="form-grid" novalidate>
            @if (isNew()) {
              <vms-field label="Route code" [control]="form.controls.routeCode" for="rt-code" hint="Leave blank to auto-generate from the origin and destination."><input pInputText id="rt-code" formControlName="routeCode" /></vms-field>
            }
            <vms-field label="Route name" [control]="form.controls.routeName" for="rt-name"><input pInputText id="rt-name" formControlName="routeName" /></vms-field>
            <vms-field label="Distance (km)" [control]="form.controls.distanceKm" for="rt-km"><p-inputnumber inputId="rt-km" formControlName="distanceKm" mode="decimal" [minFractionDigits]="1" [maxFractionDigits]="1" [fluid]="true" /></vms-field>
            <vms-field label="Standard duration (minutes)" [control]="form.controls.standardDurationMin" for="rt-min"><p-inputnumber inputId="rt-min" formControlName="standardDurationMin" [useGrouping]="false" [fluid]="true" /></vms-field>
            <div><p-checkbox formControlName="isRoundTrip" [binary]="true" inputId="rt-round" /> <label for="rt-round">Round trip</label></div>
            @if (!isNew()) {
              <vms-field label="Status" [control]="form.controls.status" for="rt-status"><p-select inputId="rt-status" formControlName="status" [options]="statusOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
            }
            <vms-field class="full" label="Remarks" [control]="form.controls.remarks" for="rt-remarks"><textarea pTextarea id="rt-remarks" formControlName="remarks" rows="2" [fluid]="true"></textarea></vms-field>
          </form>
        </div>

        <div class="card">
          <h2>Stops</h2>
          @if (preview()) { <p class="preview">{{ preview() }}</p> }
          @for (p of stopProblemsList(); track p) { <div class="alert warning small">{{ p }}</div> }

          <table>
            <thead><tr><th style="width:3rem">#</th><th>City</th><th>Stop type</th><th>Planned (min)</th><th>Remarks</th><th></th></tr></thead>
            <tbody>
              @for (s of stops(); track s.id; let i = $index, first = $first, last = $last) {
                <tr>
                  <td>{{ i + 1 }}</td>
                  <td><vms-trip-city-picker [ngModel]="s.cityId" (ngModelChange)="setCity(i, $event)" [ngModelOptions]="{ standalone: true }" /></td>
                  <td>
                    @if (first) { Origin } @else if (last) { Destination } @else {
                      <p-select [ngModel]="s.stopType" (ngModelChange)="setType(i, $event)" [ngModelOptions]="{ standalone: true }" [options]="middleTypeOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
                    }
                  </td>
                  <td><input pInputText type="number" min="0" [ngModel]="s.plannedDurationMin" (ngModelChange)="setDuration(i, $event)" [ngModelOptions]="{ standalone: true }" style="width: 6rem" /></td>
                  <td><input pInputText [ngModel]="s.remarks" (ngModelChange)="setRemarks(i, $event)" [ngModelOptions]="{ standalone: true }" /></td>
                  <td class="row-actions">
                    <p-button icon="pi pi-chevron-up" [text]="true" size="small" [disabled]="first" (onClick)="move(i, -1)" ariaLabel="Move up" />
                    <p-button icon="pi pi-chevron-down" [text]="true" size="small" [disabled]="last" (onClick)="move(i, 1)" ariaLabel="Move down" />
                    <p-button icon="pi pi-trash" [text]="true" size="small" severity="danger" [disabled]="first || last" (onClick)="removeStop(i)" ariaLabel="Remove stop" />
                  </td>
                </tr>
              }
            </tbody>
          </table>
          <p-button label="Add stop" icon="pi pi-plus" [text]="true" size="small" (onClick)="addStop()" />
          @if (canEdit()) { <div class="savebar-inline"><p-button label="Save stops" icon="pi pi-check" [loading]="savingStops()" (onClick)="saveStops()" /></div> }
        </div>

        @if (canEdit() && isNew()) {
          <div class="savebar">
            <span class="buttons">
              <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="cancel()" />
              <p-button label="Save" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
            </span>
          </div>
        } @else if (canEdit()) {
          <div class="savebar">
            <span class="buttons"><p-button label="Save details" icon="pi pi-check" [loading]="busy()" (onClick)="save()" /></span>
          </div>
        }
      }
    </div>
  `,
  styles: [
    `
      .crumbs { color: var(--vms-muted); margin-bottom: .75rem; font-size: .9rem; }
      .head { margin-bottom: 1rem; } .head h1 { font-size: 1.5rem; font-weight: 600; } .meta { display: flex; gap: .6rem; align-items: center; margin-top: .35rem; }
      .card { margin-bottom: 1.5rem; } h2 { font-size: 1.05rem; margin-bottom: .75rem; }
      .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; align-items: end; } .full { grid-column: 1 / -1; }
      table { width: 100%; border-collapse: collapse; margin-bottom: .75rem; } th, td { text-align: left; padding: .4rem .5rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: middle; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { white-space: nowrap; text-align: right; }
      .preview { font-weight: 600; margin-bottom: .5rem; }
      .conflict { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
      .alert { margin-bottom: 1rem; } .alert.small { padding: .4rem .65rem; font-size: .85rem; }
      .savebar { margin-top: 1rem; display: flex; justify-content: flex-end; } .savebar-inline { margin-top: .5rem; }
    `,
  ],
})
export class RouteFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(RoutesApi);
  private readonly cities = inject(CitiesApi);
  private readonly auth = inject(AuthService);
  private readonly route$ = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);

  readonly route = signal<RouteModel | null>(null);
  readonly cityMap = signal<Map<number, CityModel>>(new Map());
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly busy = signal(false);
  readonly savingStops = signal(false);
  readonly problems = signal<string[]>([]);
  readonly conflict = signal<string | null>(null);
  readonly stops = signal<StopRow[]>([]);

  private readonly id = toSignal(this.route$.paramMap.pipe(map((p) => (p.get('id') ? Number(p.get('id')) : null))), { initialValue: null as number | null });
  readonly isNew = computed(() => this.id() === null);
  readonly canEdit = computed(() => this.auth.hasPermission('TRP.ROUTE.EDIT'));
  protected readonly severity = statusSeverity;
  protected readonly statusOptions = [{ label: 'Active', value: 'Active' }, { label: 'Inactive', value: 'Inactive' }];
  protected readonly middleTypeOptions = MIDDLE_STOP_TYPES.map((t) => ({ label: t, value: t }));

  readonly form = this.fb.group({
    routeCode: this.fb.nonNullable.control(''),
    routeName: this.fb.nonNullable.control('', Validators.required),
    distanceKm: this.fb.control<number | null>(null, Validators.min(0.01)),
    standardDurationMin: this.fb.control<number | null>(null, Validators.min(1)),
    isRoundTrip: this.fb.nonNullable.control(false),
    status: this.fb.nonNullable.control<'Active' | 'Inactive'>('Active'),
    remarks: this.fb.nonNullable.control(''),
  });

  protected readonly preview = computed(() => {
    const map = this.cityMap();
    const rows = this.stops().filter((s) => s.cityId !== null && s.cityId !== undefined && map.has(s.cityId));
    if (rows.length === 0) return '';
    return previewString(rows.map((s) => ({ cityAbbreviation: map.get(s.cityId!)!.abbreviation })));
  });

  protected readonly stopProblemsList = computed(() => stopProblems(this.normalizedStopTypes(), this.form.controls.isRoundTrip.value, this.sameEnds()));

  /** The stop types as they will actually be submitted (position-based — see `stopInputs`), so the warnings shown
   * here always agree with what Save is about to send. */
  private normalizedStopTypes(): { stopType: string }[] {
    const rows = this.stops();
    return rows.map((s, i) => ({ stopType: i === 0 ? 'Origin' : i === rows.length - 1 ? 'Destination' : s.stopType === 'Origin' || s.stopType === 'Destination' ? 'Via' : s.stopType }));
  }

  private sameEnds(): boolean {
    const rows = this.stops();
    if (rows.length < 2) return false;
    return rows[0].cityId !== null && rows[0].cityId === rows[rows.length - 1].cityId;
  }

  constructor() {
    this.cities.list(true).subscribe({ next: (rows) => this.cityMap.set(new Map(rows.map((c) => [c.cityId, c]))), error: () => undefined });
    this.route$.paramMap.subscribe(() => this.load());
  }

  load(): void {
    this.problems.set([]);
    this.conflict.set(null);
    const id = this.id();
    if (id === null) {
      this.route.set(null);
      this.form.reset({ routeCode: '', routeName: '', distanceKm: null, standardDurationMin: null, isRoundTrip: false, status: 'Active', remarks: '' });
      this.stops.set([this.blankStop('Origin'), this.blankStop('Destination')]);
      return;
    }

    this.loading.set(true);
    this.loadError.set(null);
    this.api.get(id).subscribe({
      next: (r) => { this.loading.set(false); this.show(r); },
      error: (err: unknown) => { this.loading.set(false); this.loadError.set(err instanceof HttpErrorResponse && err.status === 404 ? 'This route was not found.' : errorMessage(err, 'The route could not be loaded.')); },
    });
  }

  private show(r: RouteModel): void {
    this.route.set(r);
    this.form.reset({ routeCode: r.routeCode, routeName: r.routeName, distanceKm: r.distanceKm ?? null, standardDurationMin: r.standardDurationMin ?? null, isRoundTrip: r.isRoundTrip, status: r.status, remarks: r.remarks ?? '' });
    this.stops.set(r.stops.map((s) => ({ id: nextRowId++, cityId: s.cityId, stopType: s.stopType, plannedDurationMin: s.plannedDurationMin ?? null, remarks: s.remarks ?? '' })));
  }

  private blankStop(type: RouteStopType): StopRow {
    return { id: nextRowId++, cityId: null, stopType: type, plannedDurationMin: null, remarks: '' };
  }

  reload(): void {
    this.load();
  }

  // ── Stops editing ────────────────────────────────────────────────────────────────

  addStop(): void {
    this.stops.update((rows) => {
      const withoutLast = rows.slice(0, -1);
      const last = rows[rows.length - 1];
      return [...withoutLast, this.blankStop('Via'), last];
    });
  }

  removeStop(index: number): void {
    this.stops.update((rows) => rows.filter((_, i) => i !== index));
  }

  move(index: number, by: -1 | 1): void {
    this.stops.update((rows) => moveStop(rows, index, by));
  }

  setCity(index: number, cityId: number | null): void {
    this.stops.update((rows) => rows.map((r, i) => (i === index ? { ...r, cityId } : r)));
  }

  setType(index: number, stopType: RouteStopType): void {
    this.stops.update((rows) => rows.map((r, i) => (i === index ? { ...r, stopType } : r)));
  }

  setDuration(index: number, value: number | null): void {
    this.stops.update((rows) => rows.map((r, i) => (i === index ? { ...r, plannedDurationMin: value } : r)));
  }

  setRemarks(index: number, value: string): void {
    this.stops.update((rows) => rows.map((r, i) => (i === index ? { ...r, remarks: value } : r)));
  }

  /** The stop type sent to the server always follows the row's final position, not whatever it happened to hold
   * before a reorder — a row that used to be Origin/Destination and was moved into the middle becomes Via, and
   * whichever row now sits first/last becomes Origin/Destination, regardless of what it displayed before. */
  private stopInputs(): RouteStopInput[] {
    const rows = this.stops();
    return rows.map((s, i) => ({
      cityId: s.cityId!,
      stopType: i === 0 ? 'Origin' : i === rows.length - 1 ? 'Destination' : s.stopType === 'Origin' || s.stopType === 'Destination' ? 'Via' : s.stopType,
      plannedDurationMin: s.plannedDurationMin,
      remarks: s.remarks?.trim() || null,
    }));
  }

  // ── Saving ───────────────────────────────────────────────────────────────────────

  cancel(): void {
    void this.router.navigate(['/routes']);
  }

  save(): void {
    if (this.busy() || this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.problems.set([]);
    this.conflict.set(null);
    const f = this.form.getRawValue();

    if (this.isNew()) {
      if (this.stopProblemsList().length > 0 || this.stops().some((s) => !s.cityId)) { this.notify.warn('Fix the stops before saving.', 'Not saved'); return; }
      this.busy.set(true);
      this.api.create({
        routeCode: f.routeCode.trim() || null, routeName: f.routeName.trim(), isRoundTrip: f.isRoundTrip, distanceKm: f.distanceKm,
        standardDurationMin: f.standardDurationMin, remarks: f.remarks.trim() || null, stops: this.stopInputs(),
      }).subscribe({
        next: (r) => { this.busy.set(false); this.notify.success(`${r.routeName} saved as ${r.routeCode}`); void this.router.navigate(['/routes', r.routeId], { replaceUrl: true }); },
        error: (err) => this.refused(err),
      });
      return;
    }

    const r = this.route();
    if (!r) return;
    this.busy.set(true);
    this.api.update(r.routeId, { routeName: f.routeName.trim(), isRoundTrip: f.isRoundTrip, distanceKm: f.distanceKm, standardDurationMin: f.standardDurationMin, status: f.status, remarks: f.remarks.trim() || null }).subscribe({
      next: (saved) => { this.busy.set(false); this.notify.success('Route details saved'); this.show(saved); },
      error: (err) => this.refused(err),
    });
  }

  saveStops(): void {
    const r = this.route();
    if (!r || this.savingStops()) return;
    if (this.stopProblemsList().length > 0 || this.stops().some((s) => !s.cityId)) { this.notify.warn('Fix the stops before saving.', 'Not saved'); return; }
    this.savingStops.set(true);
    this.api.updateStops(r.routeId, { stops: this.stopInputs() }).subscribe({
      next: (saved) => { this.savingStops.set(false); this.notify.success('Stops saved'); this.show(saved); },
      error: (err: unknown) => {
        this.savingStops.set(false);
        if (err instanceof HttpErrorResponse && err.status === 409) { this.notify.error(err); return; }
        this.refused(err);
      },
    });
  }

  private refused(err: unknown): void {
    this.busy.set(false);
    if (err instanceof HttpErrorResponse && err.status === 409) { this.conflict.set(errorMessage(err, 'This route was changed by someone else while you were editing.')); return; }
    const found = apiErrors(err);
    if (found.length === 0) { this.notify.error(err); return; }
    this.problems.set(applyServerErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
  }

  protected readonly cityDisplay = cityDisplay;
}
