import { Component, effect, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { TextareaModule } from 'primeng/textarea';

/**
 * A confirm dialog that also collects a reason (FSD §48.1: "financial [confirmations] require a reason, min 10
 * chars") — void, reject, cancel, hold and similar actions across the Trip screens all need this same small
 * shape, so it lives here rather than being rebuilt per feature.
 *
 * ```html
 * <vms-reason-dialog [open]="voiding() !== null" title="Void fuel entry" [minLength]="10" [busy]="busy()"
 *                    (closed)="voiding.set(null)" (confirmed)="doVoid($event)" />
 * ```
 */
@Component({
  selector: 'vms-reason-dialog',
  standalone: true,
  imports: [FormsModule, ButtonModule, DialogModule, TextareaModule],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '440px' }" [header]="title()" [closable]="!busy()">
      @if (message()) { <p class="message">{{ message() }}</p> }
      <textarea pTextarea [ngModel]="reason()" (ngModelChange)="reason.set($event)" rows="3" [fluid]="true" [placeholder]="placeholder()" [disabled]="busy()"></textarea>
      @if (minLength() > 0) { <div class="hint">At least {{ minLength() }} characters.</div> }
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="confirmLabel()" [loading]="busy()" [disabled]="reason().trim().length < minLength()" (onClick)="confirm()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`.message { margin-top: 0; } .hint { font-size: .8rem; color: var(--vms-muted); margin-top: .35rem; }`],
})
export class ReasonDialogComponent {
  readonly open = input(false);
  readonly title = input('Reason');
  readonly message = input('');
  readonly placeholder = input('Reason…');
  readonly confirmLabel = input('Confirm');
  readonly minLength = input(0);
  readonly busy = input(false);

  readonly closed = output<void>();
  readonly confirmed = output<string>();

  protected readonly reason = signal('');

  constructor() {
    effect(() => {
      if (!this.open()) return;
      untracked(() => this.reason.set(''));
    });
  }

  protected confirm(): void {
    if (this.reason().trim().length < this.minLength()) return;
    this.confirmed.emit(this.reason().trim());
  }
}
