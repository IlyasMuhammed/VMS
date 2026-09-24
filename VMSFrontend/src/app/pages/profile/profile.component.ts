import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { PasswordModule } from 'primeng/password';
import { AuthService } from '../../core/auth.service';
import { AccountApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { NotifyService } from '../../core/notify.service';
import { PASSWORD_HINT, passwordsMatch, strongPassword } from '../../core/validators';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonModule, PasswordModule],
  template: `
    <div class="page">
      <div class="page-header"><div><h1>My profile</h1></div></div>

      <div class="cols">
        <div class="card">
          <h3>Details</h3>
          @if (auth.user(); as u) {
            <dl>
              <dt>Name</dt><dd>{{ u.firstName }} {{ u.lastName }}</dd>
              <dt>Email</dt><dd>{{ u.email }}</dd>
              <dt>Role</dt><dd>{{ u.role.value }}</dd>
              @if (u.department) { <dt>Department</dt><dd>{{ u.department }}</dd> }
              @if (u.phone) { <dt>Phone</dt><dd>{{ u.phone }}</dd> }
            </dl>
          }
        </div>

        <form class="card" [formGroup]="form" (ngSubmit)="submit()">
          <h3>Change password</h3>
          <p class="muted">You will be signed out everywhere and asked to sign in again.</p>
          @if (error()) { <div class="alert error">{{ error() }}</div> }

          <div class="field">
            <label for="current">Current password</label>
            <p-password inputId="current" formControlName="current" [feedback]="false" [toggleMask]="true" [fluid]="true" autocomplete="current-password" />
          </div>
          <div class="field">
            <label for="password">New password</label>
            <p-password inputId="password" formControlName="password" [feedback]="false" [toggleMask]="true" [fluid]="true" autocomplete="new-password" />
            <span class="hint">{{ hint }}</span>
            @if (form.controls.password.touched && form.controls.password.errors?.['weakPassword']) { <span class="error">Password is too weak.</span> }
          </div>
          <div class="field">
            <label for="confirm">Confirm new password</label>
            <p-password inputId="confirm" formControlName="confirm" [feedback]="false" [toggleMask]="true" [fluid]="true" autocomplete="new-password" />
            @if (form.controls.confirm.touched && form.errors?.['mismatch']) { <span class="error">Passwords do not match.</span> }
          </div>
          <p-button type="submit" label="Change password" [loading]="busy()" [disabled]="form.invalid" />
        </form>
      </div>
    </div>
  `,
  styles: [
    `
      .cols { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 1rem; align-items: start; }
      form.card { display: flex; flex-direction: column; gap: 1rem; }
      dl { display: grid; grid-template-columns: 110px 1fr; gap: .5rem 1rem; margin: 0; }
      dt { color: var(--vms-muted); }
      dd { margin: 0; font-weight: 500; }
    `,
  ],
})
export class ProfileComponent {
  readonly auth = inject(AuthService);
  private readonly api = inject(AccountApi);
  private readonly fb = inject(FormBuilder);
  private readonly notify = inject(NotifyService);

  readonly hint = PASSWORD_HINT;
  readonly busy = signal(false);
  readonly error = signal('');

  readonly form = this.fb.nonNullable.group(
    {
      current: ['', Validators.required],
      password: ['', [Validators.required, strongPassword]],
      confirm: ['', Validators.required],
    },
    { validators: passwordsMatch() },
  );

  submit(): void {
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    const { current, password } = this.form.getRawValue();
    this.api.changePassword(current, password).subscribe({
      next: () => {
        this.notify.success('Please sign in again.', 'Password changed');
        // The server has ended every session, including this one.
        this.auth.clearSession();
        location.assign('/auth/login');
      },
      error: (err) => {
        this.error.set(errorMessage(err));
        this.busy.set(false);
      },
    });
  }
}
