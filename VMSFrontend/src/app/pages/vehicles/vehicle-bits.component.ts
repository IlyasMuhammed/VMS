import { Component, computed, input } from '@angular/core';
import { TagModule } from 'primeng/tag';
import { words } from './vehicle-logic';

type Severity = 'success' | 'secondary' | 'danger' | 'info' | 'warn' | 'contrast';

/** A vehicle's status as a coloured label. */
@Component({
  selector: 'vms-vehicle-status',
  standalone: true,
  imports: [TagModule],
  template: `<p-tag [value]="label()" [severity]="severity()" />`,
})
export class VehicleStatusComponent {
  readonly status = input.required<string>();
  protected readonly label = computed(() => words(this.status()));
  protected readonly severity = computed<Severity>(() => {
    switch (this.status()) {
      case 'Active':
      case 'Assigned':
        return 'success';
      case 'Draft':
        return 'info';
      case 'UnderMaintenance':
      case 'TemporarilyUnavailable':
        return 'warn';
      default:
        return 'secondary';
    }
  });
}
