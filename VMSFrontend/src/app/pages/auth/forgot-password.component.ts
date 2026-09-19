import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { AccountApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ButtonModule, InputTextModule],
  template: `
    <div class="auth-page">
      <form class="auth-card card" [formGroup]="form" (ngSubmit)="submit()">
        <div class="auth-brand"><span class="logo">VMS</span><h1>Forgot password</h1><p class="muted">We will email you a 6-digit code.</p></div>

        @if (error()) { <div class="alert error">{{ error() }}</div> }

        <div class="field">
          <label for="email">Email</label>
          <input pInputText id="email" type="email" formControlName="email" autocomplete="username" />
        </div>

        <p-button type="submit" label="Send code" [loading]="busy()" [disabled]="form.invalid" [fluid]="true" />
        <a class="center" routerLink="/auth/login">Back to sign in</a>
      </form>
    </div>
  `,
})
export class ForgotPasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(AccountApi);
  private readonly router = inject(Router);

  readonly busy = signal(false);
  readonly error = signal('');
  readonly form = this.fb.nonNullable.group({ email: ['', [Validators.required, Validators.email]] });

  submit(): void {
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    const { email } = this.form.getRawValue();
    this.api.forgotPassword(email).subscribe({
      // The server answers the same whether or not the email has an account, so we always move on.
      next: () => this.router.navigate(['/auth/reset-password'], { queryParams: { email } }),
      error: (err) => {
        this.error.set(errorMessage(err));
        this.busy.set(false);
      },
    });
  }
}
