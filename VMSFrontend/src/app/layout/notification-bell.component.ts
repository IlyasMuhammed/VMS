import { DatePipe } from '@angular/common';
import { Component, DestroyRef, ElementRef, HostListener, OnInit, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { interval } from 'rxjs';
import { AppNotification } from '../core/notification.models';
import { NotificationsApi } from '../core/api.services';
import { NotifyService } from '../core/notify.service';

/**
 * The bell icon every signed-in user sees (S7-NOT-06, FSD §23.4): an unread count, and a dropdown of what is
 * waiting for them — a document expiring, a charge due, an installment coming up. Opening a row marks it read
 * and takes the person straight to the vehicle or partner it is about, the same "read as you act" pattern the
 * rest of the shell uses for a toast, not a separate inbox screen to visit on its own.
 */
@Component({
  selector: 'app-notification-bell',
  standalone: true,
  host: { '[class.on-chrome]': 'onChrome()' },
  imports: [ButtonModule, DatePipe],
  template: `
    <div class="wrap">
      <button type="button" class="trigger" [attr.aria-expanded]="open()" aria-haspopup="true" aria-label="Notifications" title="Notifications" (click)="toggle()">
        <i class="pi pi-bell" aria-hidden="true"></i>
        @if (unreadCount() > 0) { <span class="badge">{{ unreadCount() > 9 ? '9+' : unreadCount() }}</span> }
      </button>

      @if (open()) {
        <div class="panel" role="dialog" aria-label="Notifications">
          <div class="panel-header">
            <span>Notifications</span>
            @if (unreadCount() > 0) { <button type="button" class="link" (click)="markAllRead()">Mark all read</button> }
          </div>
          <div class="list">
            @for (n of items(); track n.id) {
              <button type="button" class="row" [class.unread]="!n.isRead" (click)="open2(n)">
                <i class="pi" [class.pi-exclamation-circle]="n.isEscalation" [class.pi-info-circle]="!n.isEscalation" aria-hidden="true"></i>
                <span class="body">
                  <span class="title">{{ n.title }}</span>
                  <span class="message">{{ n.message }}</span>
                  <span class="when">{{ n.occurredOn | date: 'medium' }}</span>
                </span>
                @if (!n.isRead) { <span class="dot" aria-hidden="true"></span> }
              </button>
            } @empty {
              <div class="empty muted">@if (loading()) { Loading… } @else { Nothing to tell you about. }</div>
            }
          </div>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .wrap { position: relative; }
      .trigger { position: relative; display: inline-flex; align-items: center; justify-content: center; width: 2.1rem; height: 2.1rem;
                 border: 0; border-radius: 50%; background: transparent; color: var(--vms-muted); cursor: pointer; font-size: 1.1rem; }
      .trigger:hover { background: var(--vms-surface-soft); color: var(--vms-text); }
      :host(.on-chrome) .trigger { color: var(--vms-chrome-muted); }
      :host(.on-chrome) .trigger:hover { background: var(--vms-chrome-hover); color: var(--vms-chrome-strong); }

      .badge { position: absolute; top: 2px; right: 2px; min-width: 1.05rem; height: 1.05rem; padding: 0 .25rem; border-radius: var(--vms-radius-pill);
                background: var(--vms-danger-bg); color: var(--vms-danger-text); border: 1px solid var(--vms-danger-border);
                font-size: .65rem; font-weight: 700; line-height: 1.05rem; text-align: center; }

      .panel { position: absolute; top: calc(100% + .5rem); right: 0; width: 22rem; max-height: 26rem; overflow: hidden;
               background: var(--vms-surface); border: 1px solid var(--vms-border); border-radius: var(--vms-radius-md); box-shadow: var(--vms-shadow-lg);
               display: flex; flex-direction: column; z-index: 1000; color: var(--vms-text); }
      .panel-header { display: flex; align-items: center; justify-content: space-between; padding: .65rem .9rem; border-bottom: 1px solid var(--vms-border); font-weight: 600; }
      .link { border: 0; background: transparent; color: var(--vms-brand); cursor: pointer; font: inherit; font-weight: 500; padding: 0; }
      .link:hover { text-decoration: underline; }

      .list { overflow-y: auto; max-height: 23rem; }
      .row { display: flex; align-items: flex-start; gap: .6rem; width: 100%; text-align: left; padding: .65rem .9rem; border: 0; border-bottom: 1px solid var(--vms-border);
             background: transparent; color: inherit; font: inherit; cursor: pointer; }
      .row:hover { background: var(--vms-surface-soft); }
      .row:last-child { border-bottom: 0; }
      .row.unread { background: var(--vms-brand-tint); }
      .row > .pi { margin-top: .2rem; color: var(--vms-muted); }
      .row .pi-exclamation-circle { color: var(--vms-danger); }
      .body { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: .1rem; }
      .title { font-weight: 600; }
      .message { color: var(--vms-muted); font-size: .85rem; white-space: normal; }
      .when { color: var(--vms-muted); font-size: .75rem; }
      .dot { flex-shrink: 0; width: .5rem; height: .5rem; border-radius: 50%; background: var(--vms-brand); margin-top: .35rem; }
      .empty { padding: 1.5rem .9rem; text-align: center; }
    `,
  ],
})
export class NotificationBellComponent implements OnInit {
  /** True when the bell sits on the dark top bar (theme B), matching `app-theme-switcher`'s own switch. */
  readonly onChrome = input(false);

  private readonly api = inject(NotificationsApi);
  private readonly notify = inject(NotifyService);
  private readonly router = inject(Router);
  private readonly elementRef = inject(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  readonly open = signal(false);
  readonly loading = signal(false);
  readonly items = signal<AppNotification[]>([]);
  readonly unreadCount = signal(0);

  ngOnInit(): void {
    this.refreshCount();
    // A minute is often enough for a lead-day notification; nobody needs second-by-second freshness here.
    interval(60_000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.refreshCount());
  }

  private refreshCount(): void {
    this.api.unreadCount().subscribe({ next: (count) => this.unreadCount.set(count), error: () => undefined });
  }

  toggle(): void {
    this.open.set(!this.open());
    if (this.open()) this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.list().subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.notify.error(err);
      },
    });
  }

  open2(n: AppNotification): void {
    this.open.set(false);
    if (!n.isRead) {
      this.api.markRead(n.id).subscribe({
        next: () => this.unreadCount.set(Math.max(0, this.unreadCount() - 1)),
        error: () => undefined,
      });
    }
    this.router.navigate([n.ownerType === 'Vehicle' ? '/vehicles' : '/partners', n.ownerId]);
  }

  markAllRead(): void {
    this.api.markAllRead().subscribe({
      next: () => {
        this.unreadCount.set(0);
        this.items.update((list) => list.map((n) => ({ ...n, isRead: true })));
      },
      error: (err) => this.notify.error(err),
    });
  }

  /** Closes the panel on a click outside it, the same lightweight pattern a native `<details>` would give for free. */
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.elementRef.nativeElement.contains(event.target)) this.open.set(false);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.open.set(false);
  }
}
