import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { AccountApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { PASSWORD_HINT, passwordsMatch, strongPassword } from '../../core/validators';

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ButtonModule, InputTextModule, PasswordModule],
  template: `
    <div class="auth-page">
      <form class="auth-card card" [formGroup]="form" (ngSubmit)="submit()">
        <div class="auth-brand"><span class="logo">VMS</span><h1>Reset password</h1><p class="muted">Enter the code we emailed you.</p></div>

        @if (error()) { <div class="alert error">{{ error() }}</div> }

        <div class="field">
          <label for="email">Email</label>
          <input pInputText id="email" type="email" formControlName="email" autocomplete="username" />
        </div>
        <div class="field">
          <label for="code">6-digit code</label>
          <input pInputText id="code" formControlName="code" inputmode="numeric" maxlength="6" autocomplete="one-time-code" />
        </div>
        <div class="field">
          <label for="password">New password</label>
          <p-password inputId="password" formControlName="password" [feedback]="false" [toggleMask]="true" [fluid]="true" autocomplete="new-password" />
          <span class="hint">{{ hint }}</span>
          @if (form.controls.password.touched && form.controls.password.errors?.['weakPassword']) { <span class="error">Password is too weak.</span> }
        </div>
        <div class="field">
          <label for="confirm">Confirm password</label>
          <p-password inputId="confirm" formControlName="confirm" [feedback]="false" [toggleMask]="true" [fluid]="true" autocomplete="new-password" />
          @if (form.controls.confirm.touched && form.errors?.['mismatch']) { <span class="error">Passwords do not match.</span> }
        </div>

        <p-button type="submit" label="Set new password" [loading]="busy()" [disabled]="form.invalid" [fluid]="true" />
        <a class="center" routerLink="/auth/login">Back to sign in</a>
      </form>
    </div>
  `,
})
export class ResetPasswordComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(AccountApi);
  private readonly router = inject(Router);

  @Input() email = '';

  readonly hint = PASSWORD_HINT;
  readonly busy = signal(false);
  readonly error = signal('');

  readonly form = this.fb.nonNullable.group(
    {
      email: ['', [Validators.required, Validators.email]],
      code: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]],
      password: ['', [Validators.required, strongPassword]],
      confirm: ['', Validators.required],
    },
    { validators: passwordsMatch() },
  );

  ngOnInit(): void {
    if (this.email) this.form.patchValue({ email: this.email });
  }

  submit(): void {
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    const { email, code, password } = this.form.getRawValue();
    this.api.resetPassword(email, code, password).subscribe({
      next: () => this.router.navigate(['/auth/login']),
      error: (err) => {
        this.error.set(errorMessage(err));
        this.busy.set(false);
      },
    });
  }
}
