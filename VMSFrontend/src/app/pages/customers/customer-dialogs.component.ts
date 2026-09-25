import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { TextareaModule } from 'primeng/textarea';
import { Observable } from 'rxjs';
import { CustomersApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { ApiFieldError } from '../../core/message-format';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { CustomerModel } from '../../core/customer.models';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { statusMoves, words } from './customer-logic';

/** What every dialog on the Customer screens does: send the request, put the API's field errors beside their
 * fields, list the rest at the top. The same small base class every other feature's own dialogs already keep as
 * their own local copy (`ActionDialog` in `pages/vehicles/vehicle-dialogs.component.ts`) — not shared across
 * features, matching that established convention. */
export abstract class ActionDialog {
  protected readonly notify = inject(NotifyService);
  protected readonly messages = inject(MessagesService);
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);

  protected run<T>(request: Observable<T>, form: AbstractControl, done: string, finished: (result: T) => void, onError?: (error: ApiFieldError) => boolean): void {
    this.busy.set(true);
    this.problems.set([]);
    request.subscribe({
      next: (result) => {
        this.busy.set(false);
        this.notify.success(done);
        finished(result);
      },
      error: (err) => {
        this.busy.set(false);
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        if (onError && found.length === 1 && onError(found[0])) return;
        this.problems.set(applyServerErrors(form, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }

  protected invalid(form: AbstractControl): boolean {
    if (!form.invalid) return false;
    form.markAllAsTouched();
    return true;
  }
}

/** Activate / Deactivate / Reactivate (FSD §10, §48.1: "Confirmation... financial ones require a reason"). One
 * dialog for all three moves, since they differ only in which reason is required and what happens next. */
@Component({
  selector: 'app-customer-status-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, TextareaModule, FieldComponent],
  template: `
    <p-dialog [visible]="!!customer()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '480px' }" [header]="title()" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      @if (missingItems().length > 0) {
        <div class="alert warning">
          This customer's activation checklist is not complete:
          <ul>@for (m of missingItems(); track m) { <li>{{ m }}</li> }</ul>
          An override reason lets you activate anyway.
        </div>
      }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Reason" [control]="form.controls.reason" for="cs-reason" [hint]="reasonHint()">
          <textarea pTextarea id="cs-reason" formControlName="reason" rows="3" [fluid]="true"></textarea>
        </vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="title()" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; } .alert.warning { margin-bottom: 1rem; }`],
})
export class CustomerStatusDialogComponent extends ActionDialog {
  private readonly api = inject(CustomersApi);
  private readonly fb = inject(FormBuilder);

  readonly customer = input<CustomerModel | null>(null);
  /** Which move this dialog performs; the caller decides (there is more than one possible move from Active/Inactive). */
  readonly move = input<'Active' | 'Inactive'>('Active');
  readonly closed = output<void>();
  readonly changed = output<CustomerModel>();

  readonly form = this.fb.nonNullable.group({ reason: [''] });
  readonly missingItems = signal<string[]>([]);

  protected readonly title = computed(() => {
    const c = this.customer();
    if (!c) return '';
    if (c.status === 'Draft') return 'Activate customer';
    return this.move() === 'Active' ? 'Reactivate customer' : 'Deactivate customer';
  });
  protected readonly reasonHint = computed(() => (this.customer()?.status === 'Inactive' || this.move() === 'Inactive' ? 'Required.' : 'Optional, unless the activation checklist is incomplete.'));

  constructor() {
    super();
    effect(() => {
      const c = this.customer();
      if (!c) return;
      untracked(() => {
        this.form.reset({ reason: '' });
        this.problems.set([]);
        this.missingItems.set([]);
        if (c.status === 'Draft') {
          this.api.activationCheck(c.customerId).subscribe({ next: (check) => this.missingItems.set(check.canActivate ? [] : check.missingItems), error: () => undefined });
        }
      });
    });
  }

  save(): void {
    const c = this.customer();
    if (!c) return;
    const move = c.status === 'Inactive' ? 'Active' : this.move();
    const needsReason = move === 'Inactive' || this.missingItems().length > 0;
    if (needsReason && !this.form.controls.reason.value.trim()) {
      this.form.controls.reason.setErrors({ required: true });
      this.form.controls.reason.markAsTouched();
      return;
    }
    if (this.busy()) return;
    const reason = this.form.controls.reason.value.trim() || null;
    const body = { reason, rowVersion: c.rowVersion };
    const request = c.status === 'Draft' ? this.api.activate(c.customerId, body) : move === 'Active' ? this.api.reactivate(c.customerId, body) : this.api.deactivate(c.customerId, body);
    this.run(request, this.form, `Customer is now ${words(move)}`, (r) => this.changed.emit(r));
  }

  protected readonly statusMoves = statusMoves;
}
