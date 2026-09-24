import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="page">
      <div class="card" style="max-width: 520px">
        <h2>Page not found</h2>
        <p class="muted">There is nothing at this address. It may have moved, or the link may be wrong.</p>
        <a routerLink="/">Back to the dashboard</a>
      </div>
    </div>
  `,
})
export class NotFoundComponent {}
