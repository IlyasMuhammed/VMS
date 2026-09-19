import { Component, computed, inject, input } from '@angular/core';
import { THEME_LIST } from '../core/theme/theme.model';
import { ThemeService } from '../core/theme/theme.service';

/**
 * Two small controls, two states each:
 *   - theme:  Console | Workspace, each with a swatch showing that theme's own colours
 *   - mode:   a light/dark switch
 * Both are remembered.
 */
@Component({
  selector: 'app-theme-switcher',
  standalone: true,
  host: { '[class.on-chrome]': 'onChrome()' },
  template: `
    <div class="themes" role="group" aria-label="Theme">
      @for (t of themes; track t.id) {
        <button type="button" class="theme" [class.on]="theme.themeId() === t.id" [attr.aria-pressed]="theme.themeId() === t.id" [attr.aria-label]="t.label + ' theme'" [title]="t.label + ': ' + t.description" (click)="theme.setTheme(t.id)">
          <!-- Carries the theme's own attributes, so it paints with that theme's colours whichever theme the page uses. -->
          <span class="swatch" [attr.data-theme]="t.id" [attr.data-mode]="theme.resolvedMode()" aria-hidden="true"></span>
          <span class="label">{{ t.label }}</span>
        </button>
      }
    </div>

    <button type="button" class="mode" role="switch" [attr.aria-checked]="isDark()" aria-label="Dark mode" [title]="isDark() ? 'Switch to light mode' : 'Switch to dark mode'" (click)="theme.toggleMode()">
      <i class="pi pi-sun" aria-hidden="true"></i>
      <i class="pi pi-moon" aria-hidden="true"></i>
      <span class="knob"><i class="pi" [class.pi-sun]="!isDark()" [class.pi-moon]="isDark()" aria-hidden="true"></i></span>
    </button>
  `,
  styles: [
    `
      :host { display: inline-flex; align-items: center; gap: .75rem; }

      .themes { display: inline-flex; gap: 2px; padding: 3px; background: var(--vms-surface-soft); border: 1px solid var(--vms-border); border-radius: var(--vms-radius-pill); }
      .theme {
        display: inline-flex; align-items: center; gap: .5rem;
        padding: .3rem .85rem .3rem .4rem;
        border: 0; border-radius: var(--vms-radius-pill);
        background: transparent; color: var(--vms-muted);
        font: inherit; font-weight: 600; cursor: pointer;
        transition: background .18s, color .18s, box-shadow .18s;
      }
      .theme:hover { color: var(--vms-text); }
      .theme.on { background: var(--vms-surface); color: var(--vms-text); box-shadow: 0 0 0 1px var(--vms-border-strong); }
      .swatch {
        width: 18px; height: 18px; flex-shrink: 0; border-radius: 50%;
        background: linear-gradient(135deg, var(--vms-chrome-bg) 50%, var(--vms-brand) 50%);
        box-shadow: 0 0 0 1px var(--vms-border-strong);
      }

      .mode {
        position: relative; display: inline-flex; align-items: center; justify-content: space-between;
        width: 58px; height: 30px; padding: 0 8px;
        border: 1px solid var(--vms-border); border-radius: var(--vms-radius-pill);
        background: var(--vms-surface-soft); color: var(--vms-muted);
        cursor: pointer; transition: border-color .2s;
      }
      .mode:hover { border-color: var(--vms-border-strong); }
      .mode > i { font-size: .8rem; }
      .knob {
        position: absolute; top: 3px; left: 3px;
        display: flex; align-items: center; justify-content: center;
        width: 22px; height: 22px; border-radius: 50%;
        background: var(--vms-brand); color: var(--vms-on-brand); font-size: .75rem;
        box-shadow: var(--vms-shadow-md);
        transition: transform .24s cubic-bezier(.4, 0, .2, 1);
      }
      .mode[aria-checked='true'] .knob { transform: translateX(28px); }

      /* On the dark top bar (theme B) the controls take the bar's colours. */
      :host(.on-chrome) .themes { background: transparent; border-color: var(--vms-chrome-border); }
      :host(.on-chrome) .theme { color: var(--vms-chrome-muted); }
      :host(.on-chrome) .theme:hover { color: var(--vms-chrome-strong); }
      :host(.on-chrome) .theme.on { background: var(--vms-chrome-hover); color: var(--vms-chrome-strong); box-shadow: none; }
      :host(.on-chrome) .swatch { box-shadow: 0 0 0 1px var(--vms-chrome-border); }
      :host(.on-chrome) .mode { background: transparent; border-color: var(--vms-chrome-border); color: var(--vms-chrome-muted); }
      :host(.on-chrome) .mode:hover { border-color: var(--vms-chrome-muted); }

      /* The dark top bar is tight, so there the themes show as swatches only. The names stay in the
         tooltip and the accessible label. */
      @media (max-width: 1600px) {
        :host(.on-chrome) .theme .label { display: none; }
        :host(.on-chrome) .theme { padding: .3rem; }
      }
      @media (max-width: 900px) {
        .theme .label { display: none; }
        .theme { padding: .3rem; }
      }
      @media (prefers-reduced-motion: reduce) {
        .theme, .mode, .knob { transition: none; }
      }
    `,
  ],
})
export class ThemeSwitcherComponent {
  readonly theme = inject(ThemeService);
  /** True when the switcher sits on the dark top bar (theme B). */
  readonly onChrome = input(false);

  readonly themes = THEME_LIST;
  readonly isDark = computed(() => this.theme.resolvedMode() === 'dark');
}
