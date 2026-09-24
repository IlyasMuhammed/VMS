import { Injectable, inject } from '@angular/core';
import { ConfirmationService } from 'primeng/api';

export interface ConfirmOptions {
  title: string;
  message: string;
  /** The button that goes ahead. Say what it does ("Delete", "Deactivate"), not "OK". */
  confirmLabel?: string;
  cancelLabel?: string;
  /** For something that cannot be undone: a red button, and focus starts on Cancel so Enter does not destroy anything. */
  danger?: boolean;
  /** A PrimeIcons class. */
  icon?: string;
}

/**
 * One way to ask "are you sure?" everywhere, so wording, buttons and keyboard behaviour match:
 * `if (await this.confirm.ask({ title: 'Delete user', message: 'Delete Sara? This cannot be undone.', confirmLabel: 'Delete', danger: true })) …`.
 * The dialog itself is the one `<p-confirmdialog>` in the app shell.
 */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly confirmation = inject(ConfirmationService);

  ask(options: ConfirmOptions): Promise<boolean> {
    return new Promise((resolve) => {
      this.confirmation.confirm({
        header: options.title,
        message: options.message,
        icon: options.icon ?? (options.danger ? 'pi pi-trash' : 'pi pi-question-circle'),
        acceptLabel: options.confirmLabel ?? 'Confirm',
        rejectLabel: options.cancelLabel ?? 'Cancel',
        acceptButtonStyleClass: options.danger ? 'p-button-danger' : undefined,
        rejectButtonStyleClass: 'p-button-secondary p-button-text',
        defaultFocus: options.danger ? 'reject' : 'accept',
        accept: () => resolve(true),
        reject: () => resolve(false), // Cancel, the close button and Escape all end up here
      });
    });
  }
}
