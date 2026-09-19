import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-forbidden',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="page">
      <div class="card" style="max-width: 520px">
        <h2>Access denied</h2>
        <p class="muted">Your role does not include permission to open that page.</p>
        <a routerLink="/">Back to the dashboard</a>
      </div>
    </div>
  `,
})
export class ForbiddenComponent {}
