import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { CustomersApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { CustomerTaxRuleModel } from '../../core/customer.models';
import { CustomerTaxRuleDialogComponent, CustomerTaxRuleEditDialogComponent } from './customer-tax-rule-dialogs.component';
import { words } from './customer-logic';

/** Tax / Deduction Configuration (FSD §14, screen 6): the rules an invoice applies, in sequence, each with its
 * own effective timeline — "New rate" closes the open rule and opens a fresh one, so history is never overwritten. */
@Component({
  selector: 'app-customer-tax-rules-tab',
  standalone: true,
  imports: [ButtonModule, TagModule, BusinessDatePipe, CustomerTaxRuleDialogComponent, CustomerTaxRuleEditDialogComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
    <div class="head-row">
      @if (canEdit()) { <p-button label="Add rule" icon="pi pi-plus" size="small" (onClick)="addOpen.set(true)" /> }
    </div>
    <table>
      <thead><tr><th>Name</th><th>Code</th><th>Rate</th><th>Basis</th><th>Seq</th><th>Applicable</th><th>Effective</th><th>Status</th><th></th></tr></thead>
      <tbody>
        @for (r of rules(); track r.customerTaxRuleId) {
          <tr>
            <td>{{ r.taxName }}</td><td>{{ r.taxCode }}</td>
            <td class="nowrap">{{ r.taxType === 'Percentage' ? r.taxPercentage + '%' : r.fixedAmount }}</td>
            <td>{{ words(r.calculationBasis) }}</td><td>{{ r.sequence }}</td>
            <td>@if (r.applicable) { <p-tag value="Yes" severity="success" /> } @else { <p-tag value="No" severity="secondary" /> }</td>
            <td class="nowrap">{{ r.effectiveFrom | vmsDate }}@if (r.effectiveTo) { – {{ r.effectiveTo | vmsDate }} }</td>
            <td><p-tag [value]="r.status" [severity]="r.status === 'Active' ? 'success' : 'danger'" /></td>
            <td class="row-actions">
              @if (canEdit()) {
                <p-button label="Edit" [text]="true" size="small" (onClick)="editing.set(r)" />
                @if (r.status === 'Active') { <p-button label="New rate" [text]="true" size="small" (onClick)="replacing.set(r)" /> }
              }
            </td>
          </tr>
        }
        @if (rules().length === 0) { <tr><td colspan="9" class="muted">{{ loading() ? 'Loading…' : 'No tax or deduction rules yet.' }}</td></tr> }
      </tbody>
    </table>

    <app-customer-tax-rule-dialog [customerId]="customerId()" [adding]="addOpen()" [replacing]="replacing()" (closed)="addOpen.set(false); replacing.set(null)" (saved)="addOpen.set(false); replacing.set(null); load()" />
    <app-customer-tax-rule-edit-dialog [rule]="editing()" (closed)="editing.set(null)" (saved)="editing.set(null); load()" />
  `,
  styles: [
    `
      .head-row { margin-bottom: .75rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { white-space: nowrap; text-align: right; } .nowrap { white-space: nowrap; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class CustomerTaxRulesTabComponent {
  private readonly api = inject(CustomersApi);
  private readonly auth = inject(AuthService);

  readonly customerId = input.required<number>();
  protected readonly rules = signal<CustomerTaxRuleModel[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canEdit = () => this.auth.hasPermission('TRP.TAXRULE.EDIT');

  protected readonly addOpen = signal(false);
  protected readonly editing = signal<CustomerTaxRuleModel | null>(null);
  protected readonly replacing = signal<CustomerTaxRuleModel | null>(null);
  protected readonly words = words;

  constructor() {
    effect(() => { this.customerId(); untracked(() => this.load()); });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.taxRules(this.customerId(), true).subscribe({
      next: (r) => { this.loading.set(false); this.rules.set(r); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Tax rules could not be loaded.')); },
    });
  }
}
