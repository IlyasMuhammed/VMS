import { Component, input } from '@angular/core';
import { TagModule } from 'primeng/tag';
import { statusSeverity } from './customer-logic';

/** The customer's status chip (FSD §48.1: "Coloured status chip"), reused wherever a customer is shown. */
@Component({
  selector: 'vms-customer-status',
  standalone: true,
  imports: [TagModule],
  template: `<p-tag [value]="status()" [severity]="severity()" />`,
})
export class CustomerStatusComponent {
  readonly status = input.required<string>();
  protected readonly severity = () => statusSeverity(this.status());
}
