import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TagModule } from 'primeng/tag';
import { Observable, delay, of, timer, map, take } from 'rxjs';
import { InstantPipe } from '../../core/datetime/datetime.pipes';
import { notFutureDate } from '../../core/datetime/datetime.validators';
import { MessagesService } from '../../core/messages.service';
import { Paged } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { UploadEvent } from '../../core/upload';
import { ConfirmService } from '../../shared/confirm.service';
import { DataGridComponent, GridActionsDirective, GridCellDirective, GridColumn } from '../../shared/data-grid.component';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { FileUploadComponent } from '../../shared/file-upload.component';
import { FilterBarComponent, FilterDef } from '../../shared/filter-bar.component';
import { FilterValue, ListQuery, isFiltering, noFilters } from '../../shared/list-query';
import { LookupPickerComponent } from '../../shared/lookup-picker.component';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { applyServerErrors } from '../../shared/server-errors';

interface DemoVehicle {
  regNo: string;
  type: string;
  make: string;
  status: 'Active' | 'Draft' | 'Sold';
  expiring: boolean;
  modifiedOn: string;
}

const TYPES = ['Truck', 'Trailer', 'Tanker', 'Pickup', 'Bus'];
const MAKES = ['Hino', 'Isuzu', 'Nissan', 'Mercedes', 'Toyota'];
const STATUSES: DemoVehicle['status'][] = ['Active', 'Active', 'Active', 'Draft', 'Sold'];

/** 137 pretend vehicles, so paging, sorting and filtering have something to work on. */
const VEHICLES: DemoVehicle[] = Array.from({ length: 137 }, (_, i) => ({
  regNo: `LEA-${String(1000 + ((i * 37) % 9000)).padStart(4, '0')}`,
  type: TYPES[i % TYPES.length],
  make: MAKES[(i * 3) % MAKES.length],
  status: STATUSES[i % STATUSES.length],
  expiring: i % 4 === 0,
  modifiedOn: new Date(Date.UTC(2026, 8, 20, 9, 30) - i * 3_600_000 * 5).toISOString(),
}));

/**
 * Reference for the shared components (FND-17): each one working, with the code to use it in the page's own
 * source. Super Admin only. The grid here pages an in-memory list through the same `load` contract a real
 * screen gets from the server, slowed down a little so the loading state and "only the newest answer shows"
 * can be seen.
 */
@Component({
  selector: 'app-ui-kit',
  standalone: true,
  imports: [
    ReactiveFormsModule, FormsModule, ButtonModule, InputTextModule, TagModule, InstantPipe, PartnerPickerComponent,
    DataGridComponent, GridCellDirective, GridActionsDirective, FilterBarComponent,
    LookupPickerComponent, DatePickerComponent, FileUploadComponent, FieldComponent,
  ],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>UI kit</h1><div class="sub">The shared components every screen is built from. Use these rather than building a list, a picker or a dialog again.</div></div>
      </div>

      <section class="card block" aria-labelledby="kit-grid">
        <h2 id="kit-grid">List: filter bar and data grid</h2>
        <p class="muted">Server-side paging (25 a row), search from 3 characters after a short pause, filters, sort, an empty state that offers <em>Clear filters</em>. Requests made so far: <strong data-testid="requests">{{ requests() }}</strong>.</p>
        <vms-filter-bar [(value)]="bar" [filters]="filterDefs" searchPlaceholder="Search registration or make" />
        <vms-data-grid label="Vehicles" [columns]="columns" [load]="loadVehicles" [search]="bar().search" [filters]="bar().filters" [filtering]="filtering()"
                       [defaultSort]="{ field: 'modifiedOn', order: -1 }" emptyMessage="No vehicles yet." noMatchMessage="No vehicles match these filters." (clearFilters)="bar.set(none())">
          <ng-template vmsCell="status" let-v><p-tag [value]="v.status" [severity]="v.status === 'Active' ? 'success' : v.status === 'Draft' ? 'warn' : 'secondary'" /></ng-template>
          <ng-template vmsCell="expiring" let-v>{{ v.expiring ? 'Yes' : '—' }}</ng-template>
          <ng-template vmsCell="modifiedOn" let-v>{{ v.modifiedOn | vmsInstant }}</ng-template>
          <ng-template vmsRowActions let-v><p-button icon="pi pi-eye" [text]="true" severity="secondary" [ariaLabel]="'Open ' + v.regNo" (onClick)="opened.set(v.regNo)" /></ng-template>
        </vms-data-grid>
        <p class="muted">Opened: <strong data-testid="opened">{{ opened() || 'nothing yet' }}</strong></p>
      </section>

      <section class="card block" aria-labelledby="kit-lookup">
        <h2 id="kit-lookup">Lookup picker</h2>
        <p class="muted">Filled from a master list; only values that can still be chosen are offered. A city depends on the province.</p>
        <form [formGroup]="form" class="form-grid">
          <div class="field"><label for="kit-type">Vehicle type</label><vms-lookup-picker inputId="kit-type" type="VEHICLE_TYPE" formControlName="typeId" /></div>
          <div class="field"><label for="kit-province">Province</label><vms-lookup-picker inputId="kit-province" type="PROVINCE" valueField="code" formControlName="province" /></div>
          <div class="field"><label for="kit-city">City (in that province)</label>
            <vms-lookup-picker inputId="kit-city" type="CITY" valueField="code" [cascade]="{ province: form.controls.province.value }" formControlName="city" placeholder="Choose a province first" />
          </div>
        </form>
        <p class="muted">Value: <code data-testid="lookup-value">{{ lookupValue() }}</code></p>
      </section>

      <section class="card block" aria-labelledby="kit-partner">
        <h2 id="kit-partner">Partner picker</h2>
        <p class="muted">Choose a business partner by role: only Active partners, searched as you type. <em>New</em> opens a compact partner form with the role chosen, and selects the partner when it is saved.</p>
        <div class="form-grid">
          <div class="field"><label for="kit-bank">Bank</label><vms-partner-picker inputId="kit-bank" role="Bank" [(ngModel)]="bankId" [ngModelOptions]="{ standalone: true }" /></div>
          <div class="field"><label for="kit-workshop">Workshop</label><vms-partner-picker inputId="kit-workshop" role="Workshop" [(ngModel)]="workshopId" [ngModelOptions]="{ standalone: true }" /></div>
        </div>
        <p class="muted">Chosen: <code data-testid="partner-value">bank {{ bankId ?? 'none' }}, workshop {{ workshopId ?? 'none' }}</code></p>
      </section>

      <section class="card block" aria-labelledby="kit-date">
        <h2 id="kit-date">Date picker</h2>
        <p class="muted">A business date is a plain <code>YYYY-MM-DD</code>, never shifted by a time zone. The calendar opens on your own today.</p>
        <form [formGroup]="form" class="form-grid">
          <div class="field"><label for="kit-acq">Acquisition date (not in the future)</label><vms-date-picker inputId="kit-acq" [notFuture]="true" formControlName="acquired" /></div>
          <div class="field"><label for="kit-exp">Expiry date (from today on)</label><vms-date-picker inputId="kit-exp" [min]="today" formControlName="expiry" /></div>
        </form>
        <p class="muted">Values: <code data-testid="date-value">{{ dateValue() }}</code></p>
      </section>

      <section class="card block" aria-labelledby="kit-file">
        <h2 id="kit-file">File upload</h2>
        <p class="muted">Checks kind, size and emptiness in the same words as the server, then hands over the file or uploads it with progress.</p>
        <div class="form-grid">
          <div class="field"><label>Scan (PDF, PNG or JPEG, up to 10 MB)</label><vms-file-upload [upload]="pretendUpload" (uploaded)="uploadResult.set('uploaded')" (cleared)="uploadResult.set('')" /></div>
          <div class="field"><label>Small file only (up to 100 KB)</label><vms-file-upload [kinds]="['pdf']" [maxBytes]="100 * 1024" (picked)="pickedName.set($event.name)" /></div>
        </div>
        <p class="muted">Result: <strong data-testid="upload-result">{{ uploadResult() || pickedName() || 'nothing yet' }}</strong></p>
      </section>

      <section class="card block" aria-labelledby="kit-validation">
        <h2 id="kit-validation">Fields, validation messages and notifications</h2>
        <p class="muted">A problem shows under its field once touched, worded from the message catalogue. Submit shows every problem at once. Something with no field is a notification.</p>
        <form [formGroup]="vform" class="form-grid" novalidate>
          <vms-field label="Legal name" [control]="vform.controls.legalName" for="v-name" hint="As on the CNIC or NTN certificate"><input pInputText id="v-name" formControlName="legalName" /></vms-field>
          <vms-field label="Email" [control]="vform.controls.email" for="v-email"><input pInputText id="v-email" formControlName="email" /></vms-field>
          <vms-field label="Code" [control]="vform.controls.code" for="v-code" hint="Up to 5 characters"><input pInputText id="v-code" formControlName="code" /></vms-field>
          <vms-field label="Share %" [control]="vform.controls.share" for="v-share"><input pInputText id="v-share" type="number" formControlName="share" /></vms-field>
          <vms-field label="Acquisition date" [control]="vform.controls.acquired" for="v-acq"><vms-date-picker inputId="v-acq" [notFuture]="true" formControlName="acquired" /></vms-field>
        </form>
        <div class="row">
          <p-button label="Submit" (onClick)="submitDemo()" />
          <p-button label="Server rejects the name…" severity="secondary" (onClick)="serverRejects()" />
        </div>
        <div class="row spaced">
          <p-button label="Success" severity="success" [outlined]="true" (onClick)="notify.success('User deleted')" />
          <p-button label="Info" severity="info" [outlined]="true" (onClick)="notify.info('Reports refresh every night.')" />
          <p-button label="Warning" severity="warn" [outlined]="true" (onClick)="notify.warn('2 logos were not saved.')" />
          <p-button label="Error" severity="danger" [outlined]="true" (onClick)="notify.error('The vehicle could not be saved.')" />
        </div>
        <p class="muted">Last outcome: <strong data-testid="submit-result">{{ submitResult() || 'none yet' }}</strong></p>
      </section>

      <section class="card block" aria-labelledby="kit-confirm">
        <h2 id="kit-confirm">Confirm</h2>
        <p class="muted">One way to ask "are you sure?". A destructive one is red and starts with focus on Cancel.</p>
        <div class="row">
          <p-button label="Deactivate…" severity="secondary" (onClick)="ask(false)" />
          <p-button label="Delete…" severity="danger" (onClick)="ask(true)" />
        </div>
        <p class="muted">Answer: <strong data-testid="confirm-result">{{ answer() || 'none yet' }}</strong></p>
      </section>
    </div>
  `,
  styles: [
    `
      .block { margin-bottom: 1.25rem; }
      .block h2 { font-size: 1.1rem; font-weight: 600; margin-bottom: .25rem; }
      .row { display: flex; gap: .75rem; }
      form + .row { margin-top: 1rem; }
      .row.spaced { margin-top: .75rem; }
      code { font-family: var(--vms-font-mono); background: var(--vms-surface-soft); padding: .05rem .35rem; border-radius: var(--vms-radius-sm); }
    `,
  ],
})
export class UiKitComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly confirm = inject(ConfirmService);

  // ── Partner picker ──
  bankId: number | null = null;
  workshopId: number | null = null;

  // ── List ──
  readonly requests = signal(0);
  readonly opened = signal('');
  readonly bar = signal<FilterValue>(noFilters());
  readonly none = noFilters;
  readonly filtering = computed(() => isFiltering(this.bar()));
  readonly columns: GridColumn[] = [
    { key: 'regNo', header: 'Reg no', sortable: true },
    { key: 'type', header: 'Type' },
    { key: 'make', header: 'Make', sortable: true },
    { key: 'status', header: 'Status' },
    { key: 'expiring', header: 'Expiring document' },
    { key: 'modifiedOn', header: 'Modified on', sortable: true },
  ];
  readonly filterDefs: FilterDef[] = [
    { key: 'status', label: 'Status', kind: 'select', options: ['Active', 'Draft', 'Sold'].map((s) => ({ label: s, value: s })) },
    { key: 'types', label: 'Type', kind: 'multiselect', options: TYPES.map((t) => ({ label: t, value: t })) },
    { key: 'expiring', label: 'Has expiring document (30 days)', kind: 'toggle' },
  ];

  /** What a server does with a `ListQuery`, done here in memory. */
  readonly loadVehicles = (q: ListQuery): Observable<Paged<DemoVehicle>> => {
    this.requests.update((n) => n + 1);
    const needle = q.search.toLowerCase();
    const types = (q.filters['types'] as string[] | undefined) ?? [];
    let rows = VEHICLES.filter(
      (v) =>
        (!needle || v.regNo.toLowerCase().includes(needle) || v.make.toLowerCase().includes(needle)) &&
        (!q.filters['status'] || v.status === q.filters['status']) &&
        (types.length === 0 || types.includes(v.type)) &&
        (!q.filters['expiring'] || v.expiring),
    );
    if (q.sort) {
      const { field, order } = q.sort;
      rows = [...rows].sort((a, b) => String((a as never)[field]).localeCompare(String((b as never)[field])) * order);
    }
    const start = (q.page - 1) * q.pageSize;
    const page: Paged<DemoVehicle> = { items: rows.slice(start, start + q.pageSize), totalCount: rows.length, page: q.page, pageSize: q.pageSize, totalPages: Math.ceil(rows.length / q.pageSize) };
    return of(page).pipe(delay(250));
  };

  // ── Pickers ──
  readonly form = this.fb.group({
    typeId: this.fb.control<number | null>(null),
    province: this.fb.control<string | null>(null),
    city: this.fb.control<string | null>(null),
    acquired: this.fb.control<string | null>(null),
    expiry: this.fb.control<string | null>(null),
  });
  readonly today = new Date().toISOString().slice(0, 10);
  readonly lookupValue = computed(() => JSON.stringify({ typeId: this.value().typeId, province: this.value().province, city: this.value().city }));
  readonly dateValue = computed(() => JSON.stringify({ acquired: this.value().acquired, expiry: this.value().expiry }));
  private readonly value = signal(this.form.getRawValue());

  // ── Upload ──
  readonly uploadResult = signal('');
  readonly pickedName = signal('');
  readonly pretendUpload = (): Observable<UploadEvent> =>
    timer(0, 150).pipe(
      take(7),
      map((i): UploadEvent => (i < 6 ? { progress: i * 20 } : { progress: 100, result: { ok: true } })),
    );

  // ── Fields and notifications ──
  protected readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);
  readonly submitResult = signal('');
  readonly vform = this.fb.group({
    legalName: this.fb.control<string>('', Validators.required),
    email: this.fb.control<string>('', [Validators.required, Validators.email]),
    code: this.fb.control<string>('', Validators.maxLength(5)),
    share: this.fb.control<number | null>(null, [Validators.min(1), Validators.max(100)]),
    acquired: this.fb.control<string | null>(null, [Validators.required, notFutureDate]),
  });

  submitDemo(): void {
    if (this.vform.invalid) {
      this.vform.markAllAsTouched();
      this.submitResult.set('not sent: fix the highlighted fields');
      return;
    }
    this.submitResult.set('sent');
    this.notify.success('Saved');
  }

  /** What happens when the server refuses a value: the message lands under that field, in the catalogue's wording. */
  serverRejects(): void {
    const unplaced = applyServerErrors(
      this.vform,
      [
        { field: 'legalName', code: 'VAL-BP-003', message: 'x', params: { BPCode: 'BP-26-00147', LegalName: 'Ali Traders' } },
        { field: null, code: 'VAL-GEN-011', message: 'Please correct the highlighted fields.' },
      ],
      this.messages,
    );
    for (const problem of unplaced) this.notify.error(this.messages.describe(problem));
    this.submitResult.set('server refused');
  }

  // ── Confirm ──
  readonly answer = signal('');

  async ask(danger: boolean): Promise<void> {
    const yes = await this.confirm.ask(
      danger
        ? { title: 'Delete vehicle', message: 'Delete LEA-1234? This cannot be undone.', confirmLabel: 'Delete', danger: true }
        : { title: 'Deactivate vehicle', message: 'LEA-1234 will no longer be offered for new trips.', confirmLabel: 'Deactivate' },
    );
    this.answer.set(`${danger ? 'delete' : 'deactivate'}: ${yes ? 'confirmed' : 'cancelled'}`);
  }

  ngOnInit(): void {
    this.form.valueChanges.subscribe(() => this.value.set(this.form.getRawValue()));
  }
}
