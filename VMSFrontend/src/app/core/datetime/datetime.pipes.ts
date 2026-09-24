import { Pipe, PipeTransform } from '@angular/core';
import { DateInput, formatDate, formatInstant, InstantStyle } from './datetime';

/**
 * An instant from the API, in the viewer's time zone and locale (NFR-DT-02):
 * `{{ user.lastLoginAt | vmsInstant }}`, `| vmsInstant: 'date'`, `| vmsInstant: 'datetime' : true` to name the zone.
 */
@Pipe({ name: 'vmsInstant', standalone: true })
export class InstantPipe implements PipeTransform {
  transform(value: DateInput, style: InstantStyle = 'datetime', withZone = false): string {
    return formatInstant(value, { style, withZone });
  }
}

/** A business date (`YYYY-MM-DD`): `{{ vehicle.acquisitionDate | vmsDate }}`. The same day for every viewer. */
@Pipe({ name: 'vmsDate', standalone: true })
export class BusinessDatePipe implements PipeTransform {
  transform(value: DateInput): string {
    return formatDate(value);
  }
}
