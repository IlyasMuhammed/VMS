import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { LookupsApi } from '../../core/api.services';
import { LookupAttribute, LookupItem, LookupType } from '../../core/models';
import { NotifyService } from '../../core/notify.service';

const CODE_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_]{0,29}$/;

/** What the dialog is editing. Attribute values are text, exactly as the API stores them. */
interface Draft {
  code: string;
  description: string;
  sortOrder: number | null;
  isActive: boolean;
  attributes: Record<string, string>;
}

/**
 * Administration → Master data (FSD §24.2): the tenant's own lists behind every dropdown. A value is never
 * deleted, because records that used it keep pointing at it; it is retired, and stops being offered.
 */
@Component({
  selector: 'app-lookups',
  standalone: true,
  imports: [FormsModule, TableModule, ButtonModule, DialogModule, InputTextModule, SelectModule, CheckboxModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1>Master data</h1>
          <div class="sub">The lists behind every dropdown. Retire a value instead of deleting it: records that used it keep it, and it stops being offered.</div>
        </div>
      </div>

      <div class="layout">
        <nav class="card types" aria-label="Lists">
          @for (t of types(); track t.code) {
            <button type="button" class="type" [class.active]="t.code === selected()?.code" [attr.aria-current]="t.code === selected()?.code ? 'true' : null" (click)="select(t)">
              <span class="name">{{ t.name }}</span>
              <span class="count muted">{{ t.activeCount }}<span class="of">/{{ t.totalCount }}</span></span>
            </button>
          } @empty {
            <div class="muted">@if (loadingTypes()) { Loading… } @else { No lists. }</div>
          }
        </nav>

        <section class="card values" [attr.aria-busy]="loading()">
          @if (selected(); as type) {
            <div class="toolbar">
              <div class="grow">
                <h2>{{ type.name }}</h2>
                <div class="muted">{{ type.description }}</div>
              </div>
              <label class="check"><p-checkbox [ngModel]="showRetired()" (ngModelChange)="showRetired.set($event)" [binary]="true" inputId="showRetired" /> Show retired</label>
              <p-button label="Add value" icon="pi pi-plus" (onClick)="openCreate()" />
            </div>

            <p-table [value]="visibleValues()" [loading]="loading()">
              <ng-template pTemplate="header">
                <tr>
                  <th class="num">Order</th>
                  <th>Code</th>
                  <th>Description</th>
                  @for (a of type.attributes; track a.key) { <th>{{ a.label }}</th> }
                  <th>Status</th>
                  <th></th>
                </tr>
              </ng-template>
              <ng-template pTemplate="body" let-v>
                <tr [class.retired]="!v.isActive">
                  <td class="num">{{ v.sortOrder }}</td>
                  <td class="mono">{{ v.code }}</td>
                  <td>{{ v.description }}</td>
                  @for (a of type.attributes; track a.key) { <td>{{ attributeText(v, a) }}</td> }
                  <td><span class="chip" [class.chip--success]="v.isActive">{{ v.isActive ? 'Active' : 'Retired' }}</span></td>
                  <td class="actions">
                    <p-button icon="pi pi-pencil" [text]="true" size="small" [ariaLabel]="'Edit ' + v.description" (onClick)="openEdit(v)" />
                    <p-button [icon]="v.isActive ? 'pi pi-eye-slash' : 'pi pi-eye'" [text]="true" size="small" severity="secondary"
                              [ariaLabel]="(v.isActive ? 'Retire ' : 'Restore ') + v.description" (onClick)="toggle(v)" />
                  </td>
                </tr>
              </ng-template>
              <ng-template pTemplate="emptymessage">
                <tr><td [attr.colspan]="type.attributes.length + 5" class="muted">Nothing here yet. Add the first value.</td></tr>
              </ng-template>
            </p-table>
          } @else {
            <div class="muted">Choose a list.</div>
          }
        </section>
      </div>
    </div>

    <p-dialog [header]="editing ? 'Edit value' : 'Add value'" [(visible)]="dialogOpen" [modal]="true" [style]="{ width: '520px' }">
      @if (selected(); as type) {
        <div class="form-grid">
          <div class="field">
            <label for="lookup-code">Code *</label>
            <input id="lookup-code" pInputText [(ngModel)]="draft.code" [disabled]="!!editing" style="text-transform: uppercase" />
            <span class="hint">{{ editing ? 'A code never changes: rules and reports may rely on it.' : 'Capital letters, digits and underscores.' }}</span>
          </div>
          <div class="field">
            <label for="lookup-order">Order</label>
            <input id="lookup-order" pInputText type="number" min="0" max="9999" [(ngModel)]="draft.sortOrder" [placeholder]="editing ? '' : 'End of the list'" />
            <span class="hint">Lower numbers show first.</span>
          </div>
          <div class="field full">
            <label for="lookup-description">Description *</label>
            <input id="lookup-description" pInputText [(ngModel)]="draft.description" maxlength="200" />
          </div>

          @for (a of type.attributes; track a.key) {
            <div class="field full">
              @switch (a.kind) {
                @case ('Flag') {
                  <label class="check"><p-checkbox [ngModel]="draft.attributes[a.key] === 'true'" (ngModelChange)="draft.attributes[a.key] = $event ? 'true' : 'false'" [binary]="true" [inputId]="'attr-' + a.key" /> {{ a.label }}</label>
                }
                @case ('Number') {
                  <label [for]="'attr-' + a.key">{{ a.label }}{{ a.required ? ' *' : '' }}</label>
                  <input [id]="'attr-' + a.key" pInputText type="number" min="0" [(ngModel)]="draft.attributes[a.key]" />
                }
                @case ('Lookup') {
                  <label [for]="'attr-' + a.key">{{ a.label }}{{ a.required ? ' *' : '' }}</label>
                  <p-select [inputId]="'attr-' + a.key" [options]="optionsFor(a)" optionLabel="description" optionValue="code" [(ngModel)]="draft.attributes[a.key]"
                            [filter]="true" [showClear]="!a.required" placeholder="Choose…" [fluid]="true" appendTo="body" />
                }
                @default {
                  <label [for]="'attr-' + a.key">{{ a.label }}{{ a.required ? ' *' : '' }}</label>
                  <input [id]="'attr-' + a.key" pInputText maxlength="200" [(ngModel)]="draft.attributes[a.key]" />
                }
              }
            </div>
          }

          @if (editing) {
            <div class="field full">
              <label class="check"><p-checkbox [(ngModel)]="draft.isActive" [binary]="true" inputId="lookup-active" /> Active (offered for new records)</label>
            </div>
          }
        </div>
      }
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="dialogOpen = false" />
        <p-button [label]="editing ? 'Save' : 'Add'" [loading]="busy()" [disabled]="!canSave()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .layout { display: grid; grid-template-columns: 17rem 1fr; gap: 1rem; align-items: start; }
      @media (max-width: 900px) { .layout { grid-template-columns: 1fr; } }

      .types { display: flex; flex-direction: column; gap: .15rem; padding: .5rem; }
      .type { display: flex; justify-content: space-between; align-items: center; gap: .5rem; width: 100%; text-align: left; cursor: pointer;
              padding: .55rem .75rem; border: 0; border-radius: var(--vms-radius-sm); background: transparent; color: var(--vms-text); font: inherit; }
      .type:hover { background: var(--vms-surface-soft); }
      .type.active { background: var(--vms-brand-tint); color: var(--vms-brand-text); font-weight: 600; }
      .type .of { opacity: .7; }

      .values h2 { font-size: 1.15rem; font-weight: 600; }
      .toolbar .grow { flex: 1 1 14rem; }
      .check { display: inline-flex; align-items: center; gap: .5rem; font-weight: 400; }
      .num { text-align: right; width: 5rem; }
      tr.retired td { color: var(--vms-muted); }
    `,
  ],
})
export class LookupsComponent implements OnInit {
  private readonly api = inject(LookupsApi);
  private readonly notify = inject(NotifyService);

  readonly types = signal<LookupType[]>([]);
  readonly selected = signal<LookupType | null>(null);
  private readonly values = signal<LookupItem[]>([]);
  private readonly lookupOptions = signal<Record<string, LookupItem[]>>({});

  readonly loadingTypes = signal(false);
  readonly loading = signal(false);
  readonly busy = signal(false);

  readonly showRetired = signal(true);
  dialogOpen = false;
  editing: LookupItem | null = null;
  draft: Draft = this.blank();

  readonly visibleValues = computed(() => (this.showRetired() ? this.values() : this.values().filter((v) => v.isActive)));

  ngOnInit(): void {
    this.loadTypes();
  }

  private loadTypes(selectCode?: string): void {
    this.loadingTypes.set(true);
    this.api.types().subscribe({
      next: (types) => {
        this.types.set(types);
        this.loadingTypes.set(false);
        const keep = types.find((t) => t.code === (selectCode ?? this.selected()?.code)) ?? types[0];
        if (keep) this.select(keep);
      },
      error: (err) => this.fail(err, () => this.loadingTypes.set(false)),
    });
  }

  select(type: LookupType): void {
    this.selected.set(type);
    this.loading.set(true);
    for (const a of type.attributes.filter((x) => x.kind === 'Lookup' && x.lookupType)) this.loadOptions(a.lookupType!);
    this.api.values(type.code).subscribe({
      next: (values) => {
        this.values.set(values);
        this.loading.set(false);
      },
      error: (err) => this.fail(err, () => this.loading.set(false)),
    });
  }

  private loadOptions(type: string): void {
    if (this.lookupOptions()[type]) return;
    this.api.active(type).subscribe((items) => this.lookupOptions.update((o) => ({ ...o, [type]: items })));
  }

  optionsFor(attribute: LookupAttribute): LookupItem[] {
    return this.lookupOptions()[attribute.lookupType ?? ''] ?? [];
  }

  /** How a value's extra field reads in the table: yes/no for a flag, the description for a reference, else as stored. */
  attributeText(item: LookupItem, attribute: LookupAttribute): string {
    const raw = item.attributes[attribute.key];
    if (raw === null || raw === undefined || raw === '') return '—';
    if (attribute.kind === 'Flag') return raw === 'true' ? 'Yes' : 'No';
    if (attribute.kind === 'Lookup') return this.optionsFor(attribute).find((o) => o.code === raw)?.description ?? raw;
    return raw;
  }

  // ── Dialog ────────────────────────────────────────────────────────────────────

  private blank(): Draft {
    return { code: '', description: '', sortOrder: null, isActive: true, attributes: {} };
  }

  openCreate(): void {
    this.editing = null;
    this.draft = this.blank();
    for (const a of this.selected()?.attributes ?? []) if (a.kind === 'Flag') this.draft.attributes[a.key] = 'false';
    this.dialogOpen = true;
  }

  openEdit(item: LookupItem): void {
    this.editing = item;
    this.draft = {
      code: item.code,
      description: item.description,
      sortOrder: item.sortOrder,
      isActive: item.isActive,
      attributes: Object.fromEntries(Object.entries(item.attributes).map(([k, v]) => [k, v ?? ''])),
    };
    this.dialogOpen = true;
  }

  canSave(): boolean {
    const type = this.selected();
    if (!type || this.busy()) return false;
    if (!this.draft.description.trim()) return false;
    if (!this.editing && !CODE_PATTERN.test(this.draft.code.trim())) return false;
    return type.attributes.every((a) => !a.required || !!this.draft.attributes[a.key]?.toString().trim());
  }

  save(): void {
    const type = this.selected();
    if (!type || !this.canSave()) return;
    const attributes = Object.fromEntries(Object.entries(this.draft.attributes).map(([k, v]) => [k, v === '' ? null : String(v)]));
    const sortOrder = this.draft.sortOrder === null || (this.draft.sortOrder as unknown) === '' ? null : Number(this.draft.sortOrder);
    this.busy.set(true);

    const request = this.editing
      ? this.api.update(type.code, this.editing.id, { description: this.draft.description, sortOrder, isActive: this.draft.isActive, attributes })
      : this.api.create(type.code, { code: this.draft.code.trim().toUpperCase(), description: this.draft.description, sortOrder, attributes });

    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.dialogOpen = false;
        this.notify.success(this.editing ? 'Value updated.' : 'Value added.', 'Saved');
        this.loadTypes(type.code); // the counts change too
      },
      error: (err) => this.fail(err, () => this.busy.set(false)),
    });
  }

  /** Retire or restore in one click, keeping everything else as it is. */
  toggle(item: LookupItem): void {
    const type = this.selected();
    if (!type) return;
    this.api.update(type.code, item.id, { description: item.description, sortOrder: item.sortOrder, isActive: !item.isActive, attributes: item.attributes }).subscribe({
      next: () => {
        this.notify.success(item.description, item.isActive ? 'Retired' : 'Restored');
        this.loadTypes(type.code);
      },
      error: (err) => this.fail(err),
    });
  }

  private fail(err: unknown, done?: () => void): void {
    done?.();
    this.notify.error(err);
  }
}
