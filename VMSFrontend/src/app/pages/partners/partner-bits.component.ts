import { Component, computed, input } from '@angular/core';
import { TagModule } from 'primeng/tag';
import { roleLabel } from './partner-logic';

type Severity = 'success' | 'secondary' | 'danger' | 'info' | 'warn' | 'contrast';

/** A partner's status as a coloured label: Active, Inactive, Blacklisted, Merged. */
@Component({
  selector: 'vms-partner-status',
  standalone: true,
  imports: [TagModule],
  template: `<p-tag [value]="status()" [severity]="severity()" />`,
})
export class PartnerStatusComponent {
  readonly status = input.required<string>();
  protected readonly severity = computed<Severity>(() => {
    switch (this.status()) {
      case 'Active':
        return 'success';
      case 'Blacklisted':
        return 'danger';
      case 'Merged':
        return 'info';
      default:
        return 'secondary';
    }
  });
}

/** The roles a partner holds, as small chips. */
@Component({
  selector: 'vms-role-chips',
  standalone: true,
  template: `
    <span class="chips">
      @for (role of roles(); track role) { <span class="chip chip--info">{{ label(role) }}</span> }
      @if (roles().length === 0) { <span class="muted">—</span> }
    </span>
  `,
  styles: [`.chips { display: inline-flex; flex-wrap: wrap; gap: .25rem; }`],
})
export class RoleChipsComponent {
  readonly roles = input.required<readonly string[]>();
  protected readonly label = roleLabel;
}
