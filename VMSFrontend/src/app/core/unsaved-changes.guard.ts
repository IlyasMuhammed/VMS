import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { ConfirmService } from '../shared/confirm.service';

/** A screen that knows whether the person has typed something they have not saved. */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/**
 * Asks before a screen with unsaved changes is left (FSD FR-BP-011): the person can stay and save, or leave and lose them.
 * Attach with `canDeactivate: [unsavedChangesGuard]`. Closing or reloading the tab is covered by the screen itself, with
 * `beforeunload`, because the router does not see that.
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) => {
  if (!component.hasUnsavedChanges()) return true;
  return inject(ConfirmService).ask({
    title: 'Leave without saving?',
    message: 'You have changes that are not saved. If you leave now, they are lost.',
    confirmLabel: 'Leave',
    cancelLabel: 'Stay and keep editing',
    danger: true,
    icon: 'pi pi-exclamation-triangle',
  });
};
