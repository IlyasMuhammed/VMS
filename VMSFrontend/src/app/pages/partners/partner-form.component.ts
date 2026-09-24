import { Component, DestroyRef, HostListener, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { ButtonModule } from 'primeng/button';
import { TabsModule } from 'primeng/tabs';
import { map } from 'rxjs';
import { PartnersApi } from '../../core/api.services';
import { apiErrors, errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { Partner } from '../../core/partner.models';
import { HasUnsavedChanges } from '../../core/unsaved-changes.guard';
import { ConfirmService } from '../../shared/confirm.service';
import { DuplicatePanelComponent } from './duplicate-panel.component';
import { DuplicateBasis, DuplicateWatcher, watchDuplicates } from './duplicate-watcher';
import { HistoryTabComponent } from './history-tab.component';
import { LinkedVehiclesTabComponent } from './linked-vehicles-tab.component';
import { DocumentsTabComponent } from '../documents/documents-tab.component';
import { PartnerStatusComponent } from './partner-bits.component';
import { Permissions, buildPartnerForm, invalidCount, patchPartner, placeErrors, syncRequirements, toCreateRequest, toUpdateRequest, wireRules } from './partner-form';
import { PartnerTab, roleLabel, tabOfField } from './partner-logic';
import { AddressesTabComponent, BankAccountsTabComponent, ContactsTabComponent, GeneralTabComponent, RoleDetailsTabComponent } from './partner-tabs.component';
import { PartnerRoleDialogComponent, RoleAction } from './partner-role-dialog.component';
import { PartnerStatusDialogComponent } from './partner-status-dialog.component';

/**
 * The partner screen, for a new partner and for a saved one (FSD §9.2, §10): a header with the code, name, roles and status;
 * tabs for the General details, the role panels, contacts, addresses and bank accounts, and the history; a duplicate warning;
 * and one Save that sends everything at once. The tabs only draw parts of one form, so the tab strip below is the only thing
 * that would change to lay them out another way.
 */
@Component({
  selector: 'app-partner-form',
  standalone: true,
  imports: [
    RouterLink, ButtonModule, TabsModule, PartnerStatusComponent, DuplicatePanelComponent, GeneralTabComponent, RoleDetailsTabComponent, ContactsTabComponent,
    AddressesTabComponent, BankAccountsTabComponent, HistoryTabComponent, LinkedVehiclesTabComponent, PartnerStatusDialogComponent, PartnerRoleDialogComponent, DocumentsTabComponent,
  ],
  template: `
    <div class="page">
      <div class="crumbs"><a routerLink="/partners">Business partners</a> <span aria-hidden="true">/</span> <span>{{ partner()?.bpCode ?? 'New partner' }}</span></div>

      @if (loadError(); as message) {
        <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div>
      } @else if (loading()) {
        <p class="muted">Loading…</p>
      } @else {
        <header class="head">
          <div class="title">
            <h1>{{ partner()?.legalName || (isNew() ? 'New partner' : '') }}</h1>
            @if (partner(); as p) {
              <div class="meta">
                <span class="mono">{{ p.bpCode }}</span>
                <vms-partner-status [status]="p.status" />
                @if (p.statusReason) { <span class="muted">— {{ p.statusReason }}</span> }
              </div>
            }
          </div>

          @if (partner(); as p) {
            <div class="roles" aria-label="Roles">
              @for (role of p.roles; track role) {
                <span class="chip chip--info role">
                  {{ label(role) }}
                  @if (canManageRoles() && p.status !== 'Merged') {
                    <button type="button" class="x" [attr.aria-label]="'Remove role ' + label(role)" [disabled]="dirty()" [title]="dirty() ? 'Save your changes first' : 'Remove this role'" (click)="roleAction.set({ kind: 'remove', role })">×</button>
                  }
                </span>
              }
              @if (canManageRoles() && p.status !== 'Merged') {
                <p-button label="Add role" icon="pi pi-plus" size="small" severity="secondary" [outlined]="true" [disabled]="dirty()" [title]="dirty() ? 'Save your changes first' : 'Give this partner another role'" (onClick)="roleAction.set({ kind: 'add' })" />
              }
              @if (canChangeStatus() && p.status !== 'Merged') {
                <p-button label="Change status" icon="pi pi-flag" size="small" severity="secondary" [outlined]="true" [disabled]="dirty()" [title]="dirty() ? 'Save your changes first' : ''" (onClick)="statusOpen.set(true)" />
              }
            </div>
          }
        </header>

        @if (conflict(); as message) {
          <div class="alert warning conflict" role="alert">
            <span>{{ message }}</span>
            <p-button label="Reload their version" size="small" (onClick)="reload()" />
          </div>
        }
        @if (problems().length > 0) {
          <div class="alert error" role="alert"><strong>This could not be saved.</strong><ul>@for (m of problems(); track m) { <li>{{ m }}</li> }</ul></div>
        }
        @if (partner()?.status === 'Merged') {
          <div class="alert info">This partner was merged into another and can no longer be changed.</div>
        }
        @if (documentWarnings().length > 0) {
          <div class="alert warning" role="alert">
            <strong>Documents to follow up on.</strong>
            <ul>@for (w of documentWarnings(); track w) { <li>{{ w }}</li> }</ul>
            @if (canSeeDocuments()) { <p-button label="Go to Documents" [text]="true" size="small" (onClick)="activeTab.set('documents')" /> }
          </div>
        }

        <app-duplicate-panel [matches]="watcher.matches()" [(acknowledged)]="watcher.acknowledged" />

        <div class="card body">
          <p-tabs [value]="activeTab()" (valueChange)="activeTab.set($any($event))" [scrollable]="true">
            <p-tablist>
              <p-tab value="general">General @if (errors().general) { <span class="badge bad" [attr.aria-label]="errors().general + ' problems'">{{ errors().general }}</span> }</p-tab>
              <p-tab value="roles">Role details @if (errors().roles) { <span class="badge bad" [attr.aria-label]="errors().roles + ' problems'">{{ errors().roles }}</span> }</p-tab>
              <p-tab value="contacts">Contacts @if (errors().contacts) { <span class="badge bad" [attr.aria-label]="errors().contacts + ' problems'">{{ errors().contacts }}</span> } @else if (counts().contacts) { <span class="badge">{{ counts().contacts }}</span> }</p-tab>
              <p-tab value="addresses">Addresses @if (errors().addresses) { <span class="badge bad" [attr.aria-label]="errors().addresses + ' problems'">{{ errors().addresses }}</span> } @else if (counts().addresses) { <span class="badge">{{ counts().addresses }}</span> }</p-tab>
              <p-tab value="bank">Bank accounts @if (errors().bank) { <span class="badge bad" [attr.aria-label]="errors().bank + ' problems'">{{ errors().bank }}</span> } @else if (counts().bank) { <span class="badge">{{ counts().bank }}</span> }</p-tab>
              @if (!isNew() && canSeeVehicles()) { <p-tab value="vehicles">Linked vehicles</p-tab> }
              @if (!isNew() && canSeeDocuments()) { <p-tab value="documents">Documents</p-tab> }
              @if (!isNew()) { <p-tab value="history">History</p-tab> }
            </p-tablist>
            <p-tabpanels>
              <p-tabpanel value="general"><app-general-tab [form]="form" [mode]="isNew() ? 'create' : 'edit'" [perms]="perms()" /></p-tabpanel>
              <p-tabpanel value="roles"><app-role-details-tab [form]="form" [perms]="perms()" [creating]="isNew()" /></p-tabpanel>
              <p-tabpanel value="contacts"><app-contacts-tab [form]="form" [editable]="editable()" /></p-tabpanel>
              <p-tabpanel value="addresses"><app-addresses-tab [form]="form" [editable]="editable()" /></p-tabpanel>
              <p-tabpanel value="bank"><app-bank-accounts-tab [form]="form" [editable]="editable()" /></p-tabpanel>
              @if (!isNew() && canSeeVehicles()) {
                <p-tabpanel value="vehicles">@if (activeTab() === 'vehicles') { <app-linked-vehicles-tab [partnerId]="partner()!.id" /> }</p-tabpanel>
              }
              @if (!isNew() && canSeeDocuments()) {
                <p-tabpanel value="documents">@if (activeTab() === 'documents') { <app-documents-tab ownerType="BusinessPartner" [ownerId]="partner()!.id" /> }</p-tabpanel>
              }
              @if (!isNew()) {
                <p-tabpanel value="history">@if (activeTab() === 'history') { <app-history-tab [partnerId]="partner()!.id" [version]="partner()!.rowVersion" /> }</p-tabpanel>
              }
            </p-tabpanels>
          </p-tabs>
        </div>

        @if (editable()) {
          <div class="savebar">
            <span class="muted">{{ !watcher.canSave() ? 'Review the partners listed above before saving.' : dirty() ? 'You have unsaved changes.' : (isNew() ? 'Fields marked * are required.' : 'All changes are saved.') }}</span>
            <span class="buttons">
              <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="cancel()" />
              @if (isNew()) { <p-button label="Save and new" icon="pi pi-plus" severity="secondary" [outlined]="true" [disabled]="busy()" (onClick)="save(true)" /> }
              <p-button [label]="isNew() ? 'Save' : 'Save changes'" icon="pi pi-check" [loading]="busy()" [disabled]="!watcher.canSave() || (!isNew() && !dirty())" (onClick)="save(false)" />
            </span>
          </div>
        }
      }
    </div>

    <app-partner-status-dialog [partner]="statusOpen() ? partner() : null" (closed)="statusOpen.set(false)" (changed)="onServerChanged()" />
    <app-partner-role-dialog [partner]="partner()" [action]="roleAction()" [salary]="perms().salary" [credit]="perms().credit" (closed)="roleAction.set(null)" (changed)="onRoleChanged($event)" />
  `,
  styles: [
    `
      .crumbs { color: var(--vms-muted); margin-bottom: .75rem; font-size: .9rem; }
      .head { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1rem; }
      .title h1 { font-size: 1.5rem; font-weight: 600; }
      .meta { display: flex; align-items: center; gap: .6rem; margin-top: .35rem; }
      .roles { display: flex; flex-wrap: wrap; align-items: center; gap: .5rem; }
      .role { display: inline-flex; align-items: center; gap: .35rem; }
      .x { border: 0; background: none; color: inherit; cursor: pointer; font-size: 1rem; line-height: 1; padding: 0 .1rem; }
      .x:disabled { opacity: .5; cursor: not-allowed; }
      .badge { display: inline-block; min-width: 1.25rem; padding: 0 .35rem; margin-left: .35rem; border-radius: var(--vms-radius-pill); font-size: .7rem; font-weight: 700; line-height: 1.25rem; text-align: center; background: var(--vms-neutral-bg); color: var(--vms-neutral-text); }
      .badge.bad { background: var(--vms-danger-bg); color: var(--vms-danger-text); }
      .conflict { display: flex; align-items: center; justify-content: space-between; gap: 1rem; margin-bottom: 1rem; }
      .alert { margin-bottom: 1rem; }
      .alert ul { margin: .35rem 0 0; padding-left: 1.25rem; }
      .body { padding-bottom: 1.5rem; }
      .savebar {
        position: sticky; bottom: 0; z-index: 5; display: flex; align-items: center; justify-content: space-between; gap: 1rem; flex-wrap: wrap;
        margin: 1rem -2rem -1.5rem; padding: .85rem 2rem; background: var(--vms-surface); border-top: 1px solid var(--vms-border);
      }
      .buttons { display: inline-flex; gap: .5rem; }
    `,
  ],
})
export class PartnerFormComponent implements HasUnsavedChanges {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(PartnersApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);

  readonly form = buildPartnerForm(this.fb);
  readonly partner = signal<Partner | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);
  readonly conflict = signal<string | null>(null);
  readonly activeTab = signal<PartnerTab>('general');
  readonly statusOpen = signal(false);
  readonly roleAction = signal<RoleAction | null>(null);

  private readonly id = toSignal(this.route.paramMap.pipe(map((p) => (p.get('id') ? Number(p.get('id')) : null))), { initialValue: null as number | null });
  readonly isNew = computed(() => this.id() === null);
  protected readonly label = roleLabel;

  readonly perms = computed<Permissions>(() => ({
    salary: this.auth.hasPermission('BP.FIELD.SALARY.VIEW'),
    credit: this.auth.hasPermission('BP.FIELD.CREDIT.VIEW'),
    opening: this.auth.hasPermission('BP.FIELD.OPENING.VIEW'),
  }));
  readonly canManageRoles = computed(() => this.auth.hasPermission('BP.ROLE.MANAGE'));
  readonly canChangeStatus = computed(() => this.auth.hasPermission('BP.STATUS.CHANGE'));
  /** The Linked vehicles tab shows the vehicles module's data, so it is for someone who may view vehicles. */
  readonly canSeeVehicles = computed(() => this.auth.hasPermission('VEH.VIEW'));
  readonly canSeeDocuments = computed(() => this.auth.hasPermission('DOC.VIEW'));
  readonly documentWarnings = computed(() => this.partner()?.documentWarnings ?? []);
  readonly editable = computed(() => (this.isNew() ? this.auth.hasPermission('BP.CREATE') : this.auth.hasPermission('BP.EDIT') && this.partner()?.status !== 'Merged'));

  /** Angular's controls are not signals: form events bump this so the tab badges and the dirty flag recompute. */
  private readonly tick = signal(0);
  readonly dirty = computed(() => (this.tick(), this.form.dirty));
  readonly errors = computed(() => {
    this.tick();
    const c = this.form.controls;
    return {
      general: [c.partyType, c.legalName, c.displayName, c.cnic, c.ntn, c.strn, c.filerStatus, c.primaryMobile, c.alternatePhone, c.email, c.cityId, c.addressLine, c.branchId, c.openingBalance, c.openingBalanceDate, c.notes, c.roles]
        .reduce((n, x) => n + invalidCount(x), 0),
      roles: invalidCount(c.driver) + invalidCount(c.vendor) + invalidCount(c.customer),
      contacts: invalidCount(c.contacts),
      addresses: invalidCount(c.addresses),
      bank: invalidCount(c.bankAccounts),
    };
  });
  readonly counts = computed(() => (this.tick(), { contacts: this.form.controls.contacts.length, addresses: this.form.controls.addresses.length, bank: this.form.controls.bankAccounts.length }));

  /** What the partner was when loaded: nothing is asked about duplicates until an identifying field changes. */
  private baseline: DuplicateBasis | null = null;
  readonly watcher: DuplicateWatcher = watchDuplicates(this.api, this.form, this.destroyRef, {
    excludeId: () => this.partner()?.id ?? null,
    baseline: () => this.baseline,
  });

  constructor() {
    wireRules(this.form, this.destroyRef);
    this.form.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.tick.update((n) => n + 1));

    // A change to the route (a new partner, another partner) loads the right thing.
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty && !this.busy();
  }

  /** Closing or reloading the tab is not seen by the router, so the browser is asked to warn. */
  @HostListener('window:beforeunload', ['$event'])
  protected warnOnUnload(event: BeforeUnloadEvent): void {
    if (this.form.dirty) event.preventDefault();
  }

  // ── Loading ─────────────────────────────────────────────────────────────────────

  load(): void {
    this.problems.set([]);
    this.conflict.set(null);
    this.watcher.clear();
    const id = this.id();

    if (id === null) {
      this.partner.set(null);
      this.baseline = null;
      this.loadError.set(null);
      this.startNew(this.fromQuery());
      return;
    }

    this.loading.set(true);
    this.loadError.set(null);
    this.api.get(id).subscribe({
      next: (p) => {
        this.loading.set(false);
        this.show(p);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.loadError.set(err instanceof HttpErrorResponse && err.status === 404 ? 'This partner was not found. It may belong to another company, or the link is wrong.' : errorMessage(err, 'The partner could not be loaded.'));
      },
    });
  }

  private show(p: Partner): void {
    this.partner.set(p);
    patchPartner(this.fb, this.form, p);
    if (this.editable()) {
      this.form.enable({ emitEvent: false });
      syncRequirements(this.form);
      // Roles are changed with their own action (so each is logged), not by editing the list here.
      this.form.controls.roles.disable({ emitEvent: false });
    } else {
      this.form.disable({ emitEvent: false });
    }
    this.form.markAsPristine();
    this.form.markAsUntouched();
    const v = this.form.getRawValue();
    this.baseline = { legalName: v.legalName.trim(), cnic: v.cnic.trim(), ntn: v.ntn.trim(), primaryMobile: v.primaryMobile.trim(), cityId: v.cityId };
    this.tick.update((n) => n + 1);
  }

  /** `?partyType=Company&roles=Driver&cityId=3` starts a new partner with those chosen (used by Save and new, and by deep links). */
  private fromQuery(): { partyType?: string; roles?: string[]; cityId?: number | null; branchId?: string | null } {
    const q = this.route.snapshot.queryParamMap;
    return {
      partyType: q.get('partyType') ?? undefined,
      roles: q.get('roles')?.split(',').filter(Boolean),
      cityId: q.get('cityId') ? Number(q.get('cityId')) : undefined,
      branchId: q.get('branchId') ?? undefined,
    };
  }

  private startNew(seed: { partyType?: string; roles?: string[]; cityId?: number | null; branchId?: string | null }): void {
    for (const rows of [this.form.controls.contacts, this.form.controls.addresses, this.form.controls.bankAccounts]) rows.clear({ emitEvent: false });
    this.form.enable({ emitEvent: false });
    this.form.reset({ partyType: seed.partyType === 'Company' ? 'Company' : 'Person', roles: seed.roles ?? [], cityId: seed.cityId ?? null, branchId: seed.branchId ?? null, filerStatus: 'Unknown' }, { emitEvent: false });
    this.form.controls.driver.patchValue({ commissionBasis: 'None' }, { emitEvent: false });
    this.form.controls.vendor.patchValue({ supplyCategories: [] }, { emitEvent: false });
    syncRequirements(this.form);
    this.form.markAsPristine();
    this.form.markAsUntouched();
    this.activeTab.set('general');
    this.watcher.clear();
    this.tick.update((n) => n + 1);
  }

  reload(): void {
    this.form.markAsPristine();
    this.load();
  }

  onServerChanged(): void {
    this.statusOpen.set(false);
    this.reload();
  }

  onRoleChanged(updated: Partner): void {
    this.roleAction.set(null);
    this.show(updated);
  }

  // ── Saving ──────────────────────────────────────────────────────────────────────

  cancel(): void {
    void this.router.navigate(['/partners']);
  }

  save(andNew: boolean): void {
    if (this.busy()) return;
    this.problems.set([]);
    this.conflict.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.tick.update((n) => n + 1);
      this.openFirstProblem();
      this.notify.warn('Please correct the highlighted fields.', 'Not saved');
      return;
    }
    if (!this.watcher.canSave()) {
      this.notify.warn('This partner looks like one that already exists. Review the list above.', 'Not saved');
      return;
    }

    this.busy.set(true);
    const current = this.partner();
    const ack = this.watcher.ackIds();
    const request$ = current
      ? this.api.update(current.id, toUpdateRequest(this.form, this.perms(), ack, current.rowVersion))
      : this.api.create(toCreateRequest(this.form, this.perms(), ack));

    request$.subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.form.markAsPristine(); // so leaving the screen after saving does not ask
        if (!current) return this.afterCreate(saved, andNew);
        this.notify.success('Changes saved');
        this.show(saved);
      },
      error: (err: unknown) => this.refused(err),
    });
  }

  private afterCreate(saved: Partner, andNew: boolean): void {
    this.notify.success(andNew ? `${saved.legalName} saved as ${saved.bpCode}. Ready for the next one.` : `${saved.legalName} saved as ${saved.bpCode}`);
    if (!andNew) {
      void this.router.navigate(['/partners', saved.id], { replaceUrl: true });
      return;
    }
    // Save and new (FR-BP-015): the party type, roles, city and branch are kept, since a run of similar partners is being entered.
    const v = this.form.getRawValue();
    this.startNew({ partyType: v.partyType, roles: v.roles, cityId: v.cityId, branchId: v.branchId });
    setTimeout(() => document.getElementById('gn-legal')?.focus());
  }

  private refused(err: unknown): void {
    this.busy.set(false);

    if (err instanceof HttpErrorResponse && err.status === 409) {
      this.conflict.set(errorMessage(err, 'This partner was changed by someone else while you were editing.'));
      return;
    }
    const found = apiErrors(err);
    if (found.length === 0) {
      this.notify.error(err);
      return;
    }

    const unplaced = placeErrors(this.form, found, this.messages);
    this.problems.set(unplaced.map((e) => this.messages.describe(e)));
    // A duplicate the warning had not shown yet: show it now.
    if (found.some((e) => ['cnic', 'ntn', 'legalName', 'primaryMobile'].includes(e.field ?? ''))) this.watcher.refresh();
    const first = found.find((e) => e.field);
    if (first?.field) this.activeTab.set(tabOfField(first.field));
    this.tick.update((n) => n + 1);
  }

  private openFirstProblem(): void {
    const e = this.errors();
    const order: PartnerTab[] = ['general', 'roles', 'contacts', 'addresses', 'bank'];
    const first = order.find((tab) => e[tab as keyof typeof e] > 0);
    if (first) this.activeTab.set(first);
  }
}
