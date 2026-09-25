import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { Observable } from 'rxjs';
import { CitiesApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { ApiFieldError } from '../../core/message-format';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { CityModel } from '../../core/city.models';
import { FieldComponent } from '../../shared/field.component';
import { LookupPickerComponent } from '../../shared/lookup-picker.component';
import { applyServerErrors } from '../../shared/server-errors';

/** Send the request, put the API's field errors beside their fields, list the rest at the top — the same small
 * base class every feature's own dialogs keep as their own local copy. */
abstract class ActionDialog {
  protected readonly notify = inject(NotifyService);
  protected readonly messages = inject(MessagesService);
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);

  protected run<T>(request: Observable<T>, form: AbstractControl, done: string, finished: (result: T) => void, onError?: (error: ApiFieldError) => boolean): void {
    this.busy.set(true);
    this.problems.set([]);
    request.subscribe({
      next: (result) => { this.busy.set(false); this.notify.success(done); finished(result); },
      error: (err) => {
        this.busy.set(false);
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        if (onError && found.length === 1 && onError(found[0])) return;
        this.problems.set(applyServerErrors(form, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }

  protected invalid(form: AbstractControl): boolean {
    if (!form.invalid) return false;
    form.markAllAsTouched();
    return true;
  }
}

/** Add or edit a city (FSD §16, §48.3 screen 8). */
@Component({
  selector: 'app-city-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputTextModule, CheckboxModule, FieldComponent, LookupPickerComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '480px' }" [header]="city() ? 'Edit city' : 'Add a city'" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="form-grid" novalidate>
        <vms-field label="City name" [control]="form.controls.cityName" for="cty-name"><input pInputText id="cty-name" formControlName="cityName" /></vms-field>
        <vms-field label="Abbreviation" [control]="form.controls.abbreviation" for="cty-abbr" hint="2-5 letters, e.g. LHR.">
          <input pInputText id="cty-abbr" formControlName="abbreviation" maxlength="5" style="text-transform: uppercase" />
        </vms-field>
        <vms-field label="Country" [control]="form.controls.countryId" for="cty-country"><vms-lookup-picker type="COUNTRY" formControlName="countryId" inputId="cty-country" placeholder="Choose a country" /></vms-field>
        <vms-field label="Province/State" [control]="form.controls.provinceState" for="cty-province"><input pInputText id="cty-province" formControlName="provinceState" /></vms-field>
        @if (city()) {
          <div class="full"><p-checkbox formControlName="active" [binary]="true" inputId="cty-active" /> <label for="cty-active">Active</label></div>
        }
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="city() ? 'Save changes' : 'Add city'" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(13rem, 1fr)); gap: 1rem; } .full { grid-column: 1 / -1; }`],
})
export class CityDialogComponent extends ActionDialog {
  private readonly api = inject(CitiesApi);
  private readonly fb = inject(FormBuilder);

  readonly city = input<CityModel | null>(null);
  readonly adding = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.adding() || !!this.city());

  readonly form = this.fb.group({
    cityName: this.fb.nonNullable.control('', Validators.required),
    abbreviation: this.fb.nonNullable.control('', [Validators.required, Validators.pattern(/^[A-Za-z]{2,5}$/)]),
    countryId: this.fb.control<number | null>(null),
    provinceState: this.fb.nonNullable.control(''),
    active: this.fb.nonNullable.control(true),
  });

  constructor() {
    super();
    effect(() => {
      const c = this.city();
      const adding = this.adding();
      if (!adding && !c) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset(c
          ? { cityName: c.cityName, abbreviation: c.abbreviation, countryId: c.countryId, provinceState: c.provinceState ?? '', active: c.status === 'Active' }
          : { cityName: '', abbreviation: '', countryId: null, provinceState: '', active: true });
      });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    const body = { cityName: f.cityName.trim(), abbreviation: f.abbreviation.trim(), countryId: f.countryId, provinceState: f.provinceState.trim() || null, status: (f.active ? 'Active' : 'Inactive') as 'Active' | 'Inactive' };
    const c = this.city();
    const request = c ? this.api.update(c.cityId, body) : this.api.create(body);
    this.run(request, this.form, c ? 'City updated' : 'City added', () => this.saved.emit());
  }
}
