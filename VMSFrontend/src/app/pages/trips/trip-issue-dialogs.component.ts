import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { Observable } from 'rxjs';
import { TripOperationsApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { ApiFieldError } from '../../core/message-format';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { ISSUE_SEVERITIES, ISSUE_TYPES, optionsOf } from './trip-logic';

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

/** Report an issue (FSD §23): six types, a severity, and the reporter's own choice of whether it puts the trip
 * On Hold — the FSD names no rule tying a type to that choice automatically. */
@Component({
  selector: 'app-trip-issue-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, CheckboxModule, TextareaModule, FieldComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '480px' }" header="Report an issue" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Type" [control]="form.controls.issueType" for="is-type"><p-select inputId="is-type" formControlName="issueType" [options]="typeOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
        <vms-field label="Severity" [control]="form.controls.severity" for="is-severity"><p-select inputId="is-severity" formControlName="severity" [options]="severityOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
        <vms-field label="Description" [control]="form.controls.description" for="is-desc"><textarea pTextarea id="is-desc" formControlName="description" rows="3" [fluid]="true"></textarea></vms-field>
        <div><p-checkbox formControlName="putOnHold" [binary]="true" inputId="is-hold" /> <label for="is-hold">Put the trip on hold</label></div>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Report" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; } .alert { margin-bottom: 1rem; }`],
})
export class TripIssueDialogComponent extends ActionDialog {
  private readonly api = inject(TripOperationsApi);
  private readonly fb = inject(FormBuilder);

  readonly tripId = input<number | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.tripId() !== null);
  protected readonly typeOptions = optionsOf(ISSUE_TYPES);
  protected readonly severityOptions = optionsOf(ISSUE_SEVERITIES);

  readonly form = this.fb.group({
    issueType: this.fb.nonNullable.control<'Breakdown' | 'Accident' | 'Delay' | 'CustomerHold' | 'RouteBlocked' | 'Other'>('Other'),
    severity: this.fb.nonNullable.control<'Low' | 'Medium' | 'High'>('Medium'),
    description: this.fb.nonNullable.control('', Validators.required),
    putOnHold: this.fb.nonNullable.control(false),
  });

  constructor() {
    super();
    effect(() => {
      if (this.tripId() === null) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset({ issueType: 'Other', severity: 'Medium', description: '', putOnHold: false });
      });
    });
  }

  save(): void {
    const tripId = this.tripId();
    if (tripId === null || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.addIssue(tripId, { issueType: f.issueType, severity: f.severity, description: f.description.trim(), putOnHold: f.putOnHold }), this.form, 'Issue reported', () => this.saved.emit());
  }
}
