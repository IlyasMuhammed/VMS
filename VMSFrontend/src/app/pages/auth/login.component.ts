import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/api-error';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ButtonModule, InputTextModule, PasswordModule],
  template: `
    <div class="auth-page">
      <form class="auth-card card" [formGroup]="form" (ngSubmit)="submit()">
        <div class="auth-brand"><span class="logo">VMS</span><h1>Sign in</h1><p class="muted">Vehicle Management System</p></div>

        @if (error()) { <div class="alert error">{{ error() }}</div> }

        <div class="field">
          <label for="email">Email</label>
          <input pInputText id="email" type="email" formControlName="email" autocomplete="username" />
        </div>
        <div class="field">
          <label for="password">Password</label>
          <p-password inputId="password" formControlName="password" [feedback]="false" [toggleMask]="true" [fluid]="true" autocomplete="current-password" />
        </div>

        <p-button type="submit" label="Sign in" [loading]="busy()" [disabled]="form.invalid" styleClass="w-full" [fluid]="true" />
        <a class="center" routerLink="/auth/forgot-password">Forgot your password?</a>
      </form>
    </div>
  `,
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly busy = signal(false);
  readonly error = signal('');

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  submit(): void {
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password).subscribe({
      next: () => this.router.navigate(['/']),
      error: (err) => {
        this.error.set(errorMessage(err, 'Sign-in failed.'));
        this.busy.set(false);
      },
    });
  }
}
