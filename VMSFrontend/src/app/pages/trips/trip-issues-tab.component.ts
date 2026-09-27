import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { TripOperationsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { InstantPipe } from '../../core/datetime/datetime.pipes';
import { NotifyService } from '../../core/notify.service';
import { TripIssueModel } from '../../core/trip-operations.models';
import { TripIssueDialogComponent } from './trip-issue-dialogs.component';
import { words } from './trip-logic';

/** Issues tab (FSD §23): breakdowns, accidents, delays and the like, each resolvable once dealt with. */
@Component({
  selector: 'app-trip-issues-tab',
  standalone: true,
  imports: [ButtonModule, TagModule, InstantPipe, TripIssueDialogComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
    <div class="head-row">
      @if (canReport()) { <p-button label="Report issue" icon="pi pi-plus" size="small" (onClick)="addOpen.set(true)" /> }
    </div>
    <table>
      <thead><tr><th>Reported</th><th>Type</th><th>Severity</th><th>Description</th><th>Status</th><th></th></tr></thead>
      <tbody>
        @for (i of issues(); track i.tripIssueId) {
          <tr>
            <td class="nowrap">{{ i.reportedAtUtc | vmsInstant }}</td><td>{{ words(i.issueType) }}</td>
            <td><span class="chip" [class.chip--danger]="i.severity === 'High'" [class.chip--warning]="i.severity === 'Medium'">{{ i.severity }}</span></td>
            <td>{{ i.description }}</td>
            <td><p-tag [value]="i.isResolved ? 'Resolved' : 'Open'" [severity]="i.isResolved ? 'success' : 'warn'" /></td>
            <td class="row-actions">@if (canResolve() && !i.isResolved) { <p-button label="Resolve" [text]="true" size="small" (onClick)="resolve(i)" /> }</td>
          </tr>
        }
        @if (issues().length === 0) { <tr><td colspan="6" class="muted">{{ loading() ? 'Loading…' : 'No issues reported.' }}</td></tr> }
      </tbody>
    </table>

    <app-trip-issue-dialog [tripId]="addOpen() ? tripId() : null" (closed)="addOpen.set(false)" (saved)="addOpen.set(false); load()" />
  `,
  styles: [
    `
      .head-row { margin-bottom: .75rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { white-space: nowrap; text-align: right; } .nowrap { white-space: nowrap; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class TripIssuesTabComponent {
  private readonly api = inject(TripOperationsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  readonly tripId = input.required<number>();
  protected readonly issues = signal<TripIssueModel[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canReport = () => this.auth.hasPermission('TRP.TRIP.STATUS');
  protected readonly canResolve = () => this.auth.hasPermission('TRP.TRIP.STATUS');
  protected readonly words = words;

  protected readonly addOpen = signal(false);

  constructor() {
    effect(() => { this.tripId(); untracked(() => this.load()); });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.issues(this.tripId()).subscribe({
      next: (rows) => { this.loading.set(false); this.issues.set(rows); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Issues could not be loaded.')); },
    });
  }

  protected resolve(i: TripIssueModel): void {
    this.api.resolveIssue(i.tripIssueId, {}).subscribe({ next: () => this.load(), error: (err) => this.notify.error(err) });
  }
}
