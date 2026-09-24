import { Component, computed, input, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CheckboxModule } from 'primeng/checkbox';
import { PartnerStatusComponent, RoleChipsComponent } from './partner-bits.component';
import { DuplicateMatch, blockingMatches, matchReason, matchesToAcknowledge } from './partner-logic';

/**
 * Partners that look like the one being entered (FSD §12.1), side by side with what was typed. A CNIC or NTN already on file
 * stops the save and says whose it is; a shared mobile or a like name is shown with a tick the person must give to go on,
 * and giving it is recorded in the audit trail (BR-BP-021).
 */
@Component({
  selector: 'app-duplicate-panel',
  standalone: true,
  imports: [FormsModule, CheckboxModule, PartnerStatusComponent, RoleChipsComponent],
  template: `
    @if (matches().length > 0) {
      <section class="dup" [class.hard]="blocking().length > 0" role="region" aria-label="Possible duplicates">
        @if (blocking().length > 0) {
          <h2>This partner is already on file</h2>
          <p>A CNIC or NTN can belong to one partner only. Open the existing record instead of creating another.</p>
        } @else {
          <h2>{{ matches().length === 1 ? 'A partner like this already exists' : matches().length + ' partners like this already exist' }}</h2>
          <p>Check that this is really someone new.</p>
        }

        <table>
          <thead><tr><th>Code</th><th>Name</th><th>Roles</th><th>City</th><th>Status</th><th>Why listed</th></tr></thead>
          <tbody>
            @for (m of matches(); track m.id) {
              <tr [class.is-hard]="m.isHard">
                <td><a class="mono" [href]="'/partners/' + m.id" target="_blank" rel="noopener">{{ m.bpCode }}</a></td>
                <td>{{ m.legalName }}</td>
                <td><vms-role-chips [roles]="m.roles" /></td>
                <td>{{ m.city || '—' }}</td>
                <td><vms-partner-status [status]="m.status" /></td>
                <td>{{ reason(m) }}</td>
              </tr>
            }
          </tbody>
        </table>

        @if (blocking().length === 0 && needAnswer().length > 0) {
          <label class="ack">
            <p-checkbox [(ngModel)]="acknowledged" [binary]="true" inputId="dup-ack" />
            I have looked at {{ needAnswer().length === 1 ? 'this partner' : 'these partners' }} and this is a different partner. Save anyway.
          </label>
        }
      </section>
    }
  `,
  styles: [
    `
      .dup { border: 1px solid var(--vms-warning-border); background: var(--vms-warning-bg); color: var(--vms-warning-text); border-radius: var(--vms-radius); padding: 1rem 1.25rem; margin-bottom: 1rem; }
      .dup.hard { border-color: var(--vms-danger-border); background: var(--vms-danger-bg); color: var(--vms-danger-text); }
      h2 { font-size: 1rem; margin: 0 0 .25rem; }
      p { margin: 0 0 .75rem; }
      table { width: 100%; border-collapse: collapse; background: var(--vms-surface); color: var(--vms-text); border-radius: var(--vms-radius-sm); overflow: hidden; }
      th, td { text-align: left; padding: .45rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .85rem; vertical-align: middle; }
      th { color: var(--vms-muted); font-weight: 600; }
      tr.is-hard td { font-weight: 600; }
      .ack { display: flex; align-items: center; gap: .6rem; margin-top: .85rem; font-weight: 600; cursor: pointer; }
    `,
  ],
})
export class DuplicatePanelComponent {
  readonly matches = input.required<readonly DuplicateMatch[]>();
  /** Two-way: whether the person has ticked "this is a different partner". */
  readonly acknowledged = model(false);

  protected readonly blocking = computed(() => blockingMatches(this.matches()));
  protected readonly needAnswer = computed(() => matchesToAcknowledge(this.matches()));
  protected readonly reason = matchReason;
}
