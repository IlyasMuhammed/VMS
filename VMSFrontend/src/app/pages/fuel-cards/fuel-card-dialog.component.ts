import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { Observable } from 'rxjs';
import { FuelCardsApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { ApiFieldError } from '../../core/message-format';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { FuelCardModel } from '../../core/fuel-card.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { applyServerErrors } from '../../shared/server-errors';

abstract class ActionDialog {
  protected readonly notify = inject(NotifyService);
  protected readonly messages = inject(MessagesService);
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);

  protected run<T>(request: Observable<T>, form: AbstractControl, done: string, finished: (result: T) => void, onError?: (error: ApiFieldError) => boolean): void {
    this.busy.set(true);
    this.problems.set([]);
    request.subscribe({
      next: (result) => { this.busy.set(false); this.notify.success(done); finished(result); },
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

/** Add or edit a fuel card (FSD §28, screen 17). The card number and its issuing company are set once, at
 * creation, and never editable again — the same "identifier, not a field" treatment as a City's abbreviation. */
@Component({
  selector: 'app-fuel-card-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputTextModule, InputNumberModule, SelectModule, TextareaModule, FieldComponent, PartnerPickerComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '480px' }" [header]="card() ? 'Edit fuel card' : 'Add a fuel card'" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        @if (!card()) {
          <vms-field label="Card number" [control]="form.controls.cardNumber" for="fc-number" hint="Shown masked everywhere else."><input pInputText id="fc-number" formControlName="cardNumber" /></vms-field>
          <vms-field label="Fuel card company" [control]="form.controls.fuelCardCompanyId" for="fc-company"><vms-partner-picker role="FuelCardCompany" formControlName="fuelCardCompanyId" inputId="fc-company" /></vms-field>
        }
        <vms-field label="Card holder name" [control]="form.controls.cardHolderName" for="fc-holder"><input pInputText id="fc-holder" formControlName="cardHolderName" /></vms-field>
        <vms-field label="Expiry date" [control]="form.controls.expiryDate" for="fc-expiry"><vms-date-picker inputId="fc-expiry" formControlName="expiryDate" /></vms-field>
        <vms-field label="Monthly limit" [control]="form.controls.monthlyLimit" for="fc-limit"><p-inputnumber inputId="fc-limit" formControlName="monthlyLimit" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        @if (card()) {
          <vms-field label="Status" [control]="form.controls.status" for="fc-status"><p-select inputId="fc-status" formControlName="status" [options]="statusOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
        }
        <vms-field label="Remarks" [control]="form.controls.remarks" for="fc-remarks"><textarea pTextarea id="fc-remarks" formControlName="remarks" rows="2" [fluid]="true"></textarea></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="card() ? 'Save changes' : 'Add card'" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class FuelCardDialogComponent extends ActionDialog {
  private readonly api = inject(FuelCardsApi);
  private readonly fb = inject(FormBuilder);

  readonly card = input<FuelCardModel | null>(null);
  readonly adding = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.adding() || !!this.card());
  protected readonly statusOptions = [{ label: 'Active', value: 'Active' }, { label: 'Inactive', value: 'Inactive' }, { label: 'Blocked', value: 'Blocked' }];

  readonly form = this.fb.group({
    cardNumber: this.fb.nonNullable.control('', Validators.required),
    fuelCardCompanyId: this.fb.control<number | null>(null, Validators.required),
    cardHolderName: this.fb.nonNullable.control(''),
    expiryDate: this.fb.control<string | null>(null, Validators.required),
    monthlyLimit: this.fb.control<number | null>(null),
    status: this.fb.nonNullable.control<'Active' | 'Inactive' | 'Blocked'>('Active'),
    remarks: this.fb.nonNullable.control(''),
  });

  constructor() {
    super();
    effect(() => {
      const c = this.card();
      const adding = this.adding();
      if (!adding && !c) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset(c
          ? { cardNumber: '', fuelCardCompanyId: c.fuelCardCompanyId, cardHolderName: c.cardHolderName ?? '', expiryDate: c.expiryDate, monthlyLimit: c.monthlyLimit ?? null, status: c.status === 'Expired' ? 'Inactive' : c.status, remarks: c.remarks ?? '' }
          : { cardNumber: '', fuelCardCompanyId: null, cardHolderName: '', expiryDate: null, monthlyLimit: null, status: 'Active', remarks: '' });
      });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    const c = this.card();
    if (c) {
      this.run(this.api.save(c.fuelCardId, { cardHolderName: f.cardHolderName.trim() || null, expiryDate: f.expiryDate!, status: f.status, monthlyLimit: f.monthlyLimit, remarks: f.remarks.trim() || null, rowVersion: c.rowVersion }),
        this.form, 'Fuel card saved', () => this.saved.emit());
      return;
    }
    this.run(this.api.create({ cardNumber: f.cardNumber.trim(), fuelCardCompanyId: f.fuelCardCompanyId!, cardHolderName: f.cardHolderName.trim() || null, expiryDate: f.expiryDate!, monthlyLimit: f.monthlyLimit, remarks: f.remarks.trim() || null }),
      this.form, 'Fuel card added', () => this.saved.emit());
  }
}
