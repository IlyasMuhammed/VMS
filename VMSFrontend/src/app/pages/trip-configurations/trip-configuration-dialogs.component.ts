import { Component, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { Observable } from 'rxjs';
import { TripConfigurationsApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { ApiFieldError } from '../../core/message-format';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { TripConfigurationModel } from '../../core/trip-configuration.models';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { TripCustomerPickerComponent } from '../../shared/trip-customer-picker.component';

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

/** "Copy configuration" (FSD §18): a new Draft with the same route, stops and vehicles, to another customer or
 * the same one, with a new trip code — rates are not copied (there is no "Copy rates" tick yet; none exist to
 * copy for a Draft's usual case of a brand-new customer). */
@Component({
  selector: 'app-trip-configuration-copy-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputTextModule, FieldComponent, TripCustomerPickerComponent],
  template: `
    <p-dialog [visible]="!!source()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '480px' }" header="Copy configuration" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Customer" [control]="form.controls.customerId" for="cp-customer" hint="Leave blank to copy within the same customer.">
          <vms-trip-customer-picker formControlName="customerId" />
        </vms-field>
        <vms-field label="Name" [control]="form.controls.name" for="cp-name" [hint]="'Leave blank for &quot;' + (source()?.name ?? '') + ' (copy)&quot;.'"><input pInputText id="cp-name" formControlName="name" /></vms-field>
        <vms-field label="Trip code" [control]="form.controls.tripCode" for="cp-code" hint="Leave blank to auto-generate."><input pInputText id="cp-code" formControlName="tripCode" /></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Copy" icon="pi pi-copy" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class TripConfigurationCopyDialogComponent extends ActionDialog {
  private readonly api = inject(TripConfigurationsApi);
  private readonly fb = inject(FormBuilder);

  readonly source = input<TripConfigurationModel | null>(null);
  readonly closed = output<void>();
  readonly copied = output<TripConfigurationModel>();

  readonly form = this.fb.group({
    customerId: this.fb.control<number | null>(null),
    name: this.fb.nonNullable.control(''),
    tripCode: this.fb.nonNullable.control(''),
  });

  save(): void {
    const s = this.source();
    if (!s || this.busy()) return;
    const f = this.form.getRawValue();
    this.run(
      this.api.copy(s.tripConfigurationId, { customerId: f.customerId, name: f.name.trim() || null, tripCode: f.tripCode.trim() || null }),
      this.form,
      'Configuration copied',
      (r) => this.copied.emit(r),
    );
  }
}
