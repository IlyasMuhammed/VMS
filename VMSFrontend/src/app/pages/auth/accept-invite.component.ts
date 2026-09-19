import { Component, Input, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { PasswordModule } from 'primeng/password';
import { AccountApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { PASSWORD_HINT, passwordsMatch, strongPassword } from '../../core/validators';

@Component({
  selector: 'app-accept-invite',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ButtonModule, PasswordModule],
  template: `
    <div class="auth-page">
      <form class="auth-card card" [formGroup]="form" (ngSubmit)="submit()">
        <div class="auth-brand"><span class="logo">VMS</span><h1>Set your password</h1><p class="muted">Welcome — choose a password to activate your account.</p></div>

        @if (done()) {
          <div class="alert success">Your password is set. You can now sign in.</div>
          <a class="center" routerLink="/auth/login">Go to sign in</a>
        } @else {
          @if (!token) { <div class="alert error">This invite link is missing its token. Ask your administrator for a new one.</div> }
          @if (error()) { <div class="alert error">{{ error() }}</div> }

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

          <p-button type="submit" label="Activate account" [loading]="busy()" [disabled]="form.invalid || !token" [fluid]="true" />
        }
      </form>
    </div>
  `,
})
export class AcceptInviteComponent {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(AccountApi);

  /** From the `?token=` query string (withComponentInputBinding). */
  @Input() token = '';

  readonly hint = PASSWORD_HINT;
  readonly busy = signal(false);
  readonly done = signal(false);
  readonly error = signal('');

  readonly form = this.fb.nonNullable.group(
    { password: ['', [Validators.required, strongPassword]], confirm: ['', Validators.required] },
    { validators: passwordsMatch() },
  );

  submit(): void {
    if (this.form.invalid || this.busy() || !this.token) return;
    this.busy.set(true);
    this.error.set('');
    this.api.acceptInvite(this.token, this.form.getRawValue().password).subscribe({
      next: () => this.done.set(true),
      error: (err) => {
        this.error.set(errorMessage(err));
        this.busy.set(false);
      },
    });
  }
}
