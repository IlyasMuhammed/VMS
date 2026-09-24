import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { AbstractControl, Validators } from '@angular/forms';
import { classify } from '../core/form-errors';
import { MessagesService } from '../core/messages.service';

/**
 * A form field with its label, hint and validation message, so every form shows problems the same way (FSD §12.2):
 * the message appears under the field once the person has touched it or the form was submitted, is worded from the
 * message catalogue, names the field, and disappears as soon as it is fixed. A problem the server found is shown
 * the same way (see `applyServerErrors`).
 *
 * ```html
 * <vms-field label="Legal name" [control]="form.controls.legalName" hint="As on the CNIC or NTN certificate" for="legalName">
 *   <input pInputText id="legalName" formControlName="legalName" />
 * </vms-field>
 * ```
 * A required control is starred automatically. Call `form.markAllAsTouched()` on submit so every message shows.
 */
@Component({
  selector: 'vms-field',
  standalone: true,
  template: `
    <div class="field" [class.invalid]="text() !== null">
      @if (label()) {
        <label [attr.for]="for()">{{ label() }}@if (required()) { <span class="req" aria-hidden="true"> *</span> }</label>
      }
      <ng-content />
      @if (text(); as message) {
        <span class="error" role="alert">{{ message }}</span>
      } @else if (hint()) {
        <span class="hint">{{ hint() }}</span>
      }
    </div>
  `,
})
export class FieldComponent {
  private readonly messages = inject(MessagesService);

  readonly label = input('');
  readonly control = input<AbstractControl | null>(null);
  readonly hint = input('');
  /** The id of the input inside, so clicking the label focuses it. */
  readonly for = input<string | null>(null);
  /** Wording for a custom validator's error key, for example `{ weakPassword: 'Use 8 or more characters.' }`. */
  readonly messagesFor = input<Record<string, string>>({});

  /** Angular's controls are not signals, so their events bump this to make the message recompute. */
  private readonly changes = signal(0);

  protected readonly required = computed(() => {
    this.changes();
    return this.control()?.hasValidator(Validators.required) ?? false;
  });

  /** The message to show, or null: only once touched or edited, and only while the control is invalid. */
  protected readonly text = computed(() => {
    this.changes();
    const control = this.control();
    if (!control || !control.invalid || !(control.touched || control.dirty)) return null;

    const error = classify(control.errors, this.label() || 'This field', this.messagesFor());
    if (!error) return null;
    return error.code === 'server' || error.code === 'custom' ? (error.message ?? null) : this.messages.text(error.code, error.params);
  });

  constructor() {
    effect((onCleanup) => {
      const control = this.control();
      if (!control) return;
      const subscription = control.events.subscribe(() => untracked(() => this.changes.update((n) => n + 1)));
      onCleanup(() => subscription.unsubscribe());
    });
  }
}
