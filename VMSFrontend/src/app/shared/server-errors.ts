import { AbstractControl } from '@angular/forms';
import { ApiFieldError } from '../core/message-format';
import { MessagesService } from '../core/messages.service';

/**
 * Puts the problems the API found next to the fields they belong to (FSD §12.2): each error with a `field`
 * marks that control, in the screen's own wording, and clears itself as soon as the person edits the field.
 * Returns what could not be placed (the form as a whole, or a field this form does not have) so the caller can
 * show it once, with `NotifyService.error`. Also touches the controls so the messages appear.
 *
 * ```ts
 * error: (err) => { const unplaced = applyServerErrors(this.form, apiErrors(err), this.messages); if (unplaced.length) this.notify.error(err); }
 * ```
 */
export function applyServerErrors(form: AbstractControl, errors: readonly ApiFieldError[], messages: MessagesService): ApiFieldError[] {
  const unplaced: ApiFieldError[] = [];

  for (const error of errors) {
    const control = error.field ? form.get(error.field) : null;
    if (!control) {
      unplaced.push(error);
      continue;
    }

    control.setErrors({ ...control.errors, server: messages.describe(error) });
    control.markAsTouched();
    // The person's next edit answers it. Only that one error goes; the checks the form itself makes stay.
    const subscription = control.valueChanges.subscribe(() => {
      subscription.unsubscribe();
      if (control.errors && 'server' in control.errors) {
        const { server, ...rest } = control.errors;
        void server;
        control.setErrors(Object.keys(rest).length ? rest : null);
        control.updateValueAndValidity({ emitEvent: false });
      }
    });
  }
  return unplaced;
}
