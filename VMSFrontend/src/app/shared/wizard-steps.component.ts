import { Component, computed, input, output } from '@angular/core';

export interface WizardStep {
  key: string;
  label: string;
  /** How many fields on this step have a problem. Shown as a red badge. */
  errors?: number;
  /** False while an earlier step must be finished first: the step is shown but cannot be opened. */
  reachable?: boolean;
  /** A step that is not built yet: shown dimmed, with a note in place of its content. */
  pending?: boolean;
}

/**
 * The steps of a wizard as a progress bar (FSD §21, FR-VH-009): each step numbered, the current one marked, a red count on a
 * step with problems, and steps that cannot be reached yet greyed out. It only draws the steps and says which was chosen;
 * what each step shows is up to the screen, so the same bar can lie along the top or run down the side.
 */
@Component({
  selector: 'vms-wizard-steps',
  standalone: true,
  template: `
    <ol class="steps" aria-label="Steps">
      @for (s of steps(); track s.key; let i = $index) {
        <li [class.current]="i === active()" [class.done]="i < active()" [class.locked]="s.reachable === false" [class.pending]="s.pending">
          <button type="button" [disabled]="s.reachable === false" [attr.aria-current]="i === active() ? 'step' : null" (click)="chosen.emit(i)">
            <span class="num">{{ i + 1 }}</span>
            <span class="label">{{ s.label }}</span>
            @if (s.errors) { <span class="bad" [attr.aria-label]="s.errors + ' problems'">{{ s.errors }}</span> }
          </button>
        </li>
      }
    </ol>
    <div class="bar" role="progressbar" [attr.aria-valuemin]="0" [attr.aria-valuemax]="steps().length" [attr.aria-valuenow]="active() + 1"><span [style.width.%]="percent()"></span></div>
  `,
  styles: [
    `
      .steps { display: flex; flex-wrap: wrap; gap: .25rem 1.25rem; list-style: none; margin: 0 0 .5rem; padding: 0; }
      button { display: inline-flex; align-items: center; gap: .5rem; border: 0; background: none; padding: .4rem 0; font: inherit; color: var(--vms-muted); cursor: pointer; }
      button:disabled { cursor: not-allowed; opacity: .6; }
      .num { display: inline-grid; place-items: center; width: 1.6rem; height: 1.6rem; border-radius: 50%; border: 1px solid var(--vms-border); font-size: .8rem; font-weight: 700; background: var(--vms-surface); }
      li.current button { color: var(--vms-text); font-weight: 600; }
      li.current .num { background: var(--vms-brand); border-color: var(--vms-brand); color: var(--vms-on-brand); }
      li.done .num { border-color: var(--vms-brand); color: var(--vms-brand-text); }
      li.pending .label { font-style: italic; }
      .bad { min-width: 1.25rem; padding: 0 .35rem; border-radius: var(--vms-radius-pill); background: var(--vms-danger-bg); color: var(--vms-danger-text); font-size: .7rem; font-weight: 700; line-height: 1.25rem; text-align: center; }
      .bar { height: .3rem; border-radius: var(--vms-radius-pill); background: var(--vms-neutral-bg); overflow: hidden; margin-bottom: 1.25rem; }
      .bar span { display: block; height: 100%; background: var(--vms-brand); transition: width .2s; }
    `,
  ],
})
export class WizardStepsComponent {
  readonly steps = input.required<readonly WizardStep[]>();
  /** The index of the step being shown. */
  readonly active = input(0);
  readonly chosen = output<number>();
  protected readonly percent = computed(() => ((this.active() + 1) / Math.max(1, this.steps().length)) * 100);
}
