import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { AuthService } from '../../core/auth.service';
import { TripOperationsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { InstantPipe } from '../../core/datetime/datetime.pipes';
import { NotifyService } from '../../core/notify.service';
import { TripEventModel } from '../../core/trip-operations.models';
import { FieldComponent } from '../../shared/field.component';
import { InstantPickerComponent } from '../../shared/instant-picker.component';
import { words } from './trip-logic';

/** Timeline tab (FSD §25, screen 15): every operational step, newest first, from whichever source logged it —
 * a status transition, a fuel/expense/issue entry, a POD upload, or a manual Note (the only type this tab's own
 * "Add Event" can create; every other type is produced by its own dedicated action). */
@Component({
  selector: 'app-trip-timeline-tab',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonModule, DialogModule, InputTextModule, TextareaModule, InstantPipe, FieldComponent, InstantPickerComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
    <div class="head-row">
      @if (canAdd()) { <p-button label="Add note" icon="pi pi-plus" size="small" (onClick)="addOpen.set(true)" /> }
    </div>
    <ul class="timeline">
      @for (e of events(); track e.tripEventId) {
        <li>
          <span class="icon" [class]="'src-' + e.source.toLowerCase()"><i class="pi" [class.pi-user]="e.source === 'Manual'" [class.pi-mobile]="e.source === 'DriverApp'" [class.pi-cog]="e.source === 'System'" [class.pi-map-marker]="e.source === 'GPS'"></i></span>
          <div class="body">
            <div class="head"><strong>{{ words(e.eventType) }}</strong> <span class="muted">{{ e.eventDateTime | vmsInstant }}</span></div>
            @if (e.locationText || e.cityId) { <div class="muted small">{{ e.locationText || 'City #' + e.cityId }}</div> }
            @if (e.odometer !== null && e.odometer !== undefined) { <div class="muted small">Odometer: {{ e.odometer }} km</div> }
            @if (e.remarks) { <div>{{ e.remarks }}</div> }
          </div>
        </li>
      }
      @if (events().length === 0) { <li class="muted">{{ loading() ? 'Loading…' : 'Nothing recorded yet.' }}</li> }
    </ul>

    <p-dialog [visible]="addOpen()" (visibleChange)="!$event && addOpen.set(false)" [modal]="true" [style]="{ width: '480px' }" header="Add a note" [closable]="!busy()">
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="When" [control]="form.controls.eventDateTime" for="ev-when" hint="Defaults to now."><vms-instant-picker inputId="ev-when" formControlName="eventDateTime" [notFuture]="true" /></vms-field>
        <vms-field label="Location" [control]="form.controls.locationText" for="ev-loc"><input pInputText id="ev-loc" formControlName="locationText" /></vms-field>
        <vms-field label="Remarks" [control]="form.controls.remarks" for="ev-remarks"><textarea pTextarea id="ev-remarks" formControlName="remarks" rows="3" [fluid]="true"></textarea></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="addOpen.set(false)" [disabled]="busy()" />
        <p-button label="Add" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .head-row { margin-bottom: .75rem; }
      .timeline { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 1rem; }
      .timeline li { display: flex; gap: .75rem; }
      .icon { flex: none; width: 2rem; height: 2rem; border-radius: 50%; display: flex; align-items: center; justify-content: center; background: var(--vms-surface-soft); color: var(--vms-muted); }
      .head { display: flex; gap: .5rem; align-items: baseline; } .small { font-size: .8rem; } .stack { display: flex; flex-direction: column; gap: 1rem; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class TripTimelineTabComponent {
  private readonly api = inject(TripOperationsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);
  private readonly fb = inject(FormBuilder);

  readonly tripId = input.required<number>();
  protected readonly events = signal<TripEventModel[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canAdd = () => this.auth.hasPermission('TRP.TRIP.STATUS');
  protected readonly words = words;

  protected readonly addOpen = signal(false);
  protected readonly busy = signal(false);

  readonly form = this.fb.group({
    eventDateTime: this.fb.control<string | null>(null),
    locationText: this.fb.nonNullable.control(''),
    remarks: this.fb.nonNullable.control(''),
  });

  constructor() {
    effect(() => { this.tripId(); untracked(() => this.load()); });
    effect(() => { if (this.addOpen()) untracked(() => this.form.reset({ eventDateTime: null, locationText: '', remarks: '' })); });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.events(this.tripId()).subscribe({
      next: (rows) => { this.loading.set(false); this.events.set([...rows].sort((a, b) => b.eventDateTime.localeCompare(a.eventDateTime))); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'The timeline could not be loaded.')); },
    });
  }

  protected save(): void {
    if (this.busy()) return;
    const f = this.form.getRawValue();
    this.busy.set(true);
    this.api.addEvent(this.tripId(), { eventType: 'Note', eventDateTime: f.eventDateTime, locationText: f.locationText.trim() || null, remarks: f.remarks.trim() || null }).subscribe({
      next: () => { this.busy.set(false); this.addOpen.set(false); this.notify.success('Note added'); this.load(); },
      error: (err) => { this.busy.set(false); this.notify.error(err); },
    });
  }
}
