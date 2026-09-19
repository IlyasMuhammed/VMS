import { DOCUMENT } from '@angular/common';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import {
  DEFAULT_MODE,
  DEFAULT_THEME,
  THEMES,
  THEME_STORAGE_KEY,
  ThemeId,
  ThemeMode,
  isThemeId,
  isThemeMode,
} from './theme.model';

/**
 * Owns the two independent choices: which theme (a or b) and which mode (light, dark or system).
 * It writes them to <html data-theme data-mode>, which is what src/styles/tokens/ and the PrimeNG
 * preset key off, and remembers them in localStorage.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly doc = inject(DOCUMENT);
  private readonly media = this.doc.defaultView?.matchMedia?.('(prefers-color-scheme: dark)') ?? null;

  readonly themeId = signal<ThemeId>(DEFAULT_THEME);
  readonly mode = signal<ThemeMode>(DEFAULT_MODE);
  private readonly systemDark = signal(this.media?.matches ?? false);

  readonly theme = computed(() => THEMES[this.themeId()]);
  readonly layout = computed(() => this.theme().layout);
  /** The mode actually applied: "system" resolves to the device's current setting. */
  readonly resolvedMode = computed<'light' | 'dark'>(() => {
    const m = this.mode();
    if (m === 'system') return this.systemDark() ? 'dark' : 'light';
    return m;
  });

  constructor() {
    this.restore();
    this.media?.addEventListener('change', (e) => this.systemDark.set(e.matches));
    effect(() => {
      const root = this.doc.documentElement;
      root.setAttribute('data-theme', this.themeId());
      root.setAttribute('data-mode', this.resolvedMode());
      this.persist(this.themeId(), this.mode());
    });
  }

  setTheme(id: ThemeId): void {
    this.themeId.set(id);
  }

  setMode(mode: ThemeMode): void {
    this.mode.set(mode);
  }

  /** Light <-> dark. Until this is used the mode follows the device; after it, the choice is explicit. */
  toggleMode(): void {
    this.mode.set(this.resolvedMode() === 'dark' ? 'light' : 'dark');
  }

  private restore(): void {
    try {
      const saved = JSON.parse(this.doc.defaultView?.localStorage.getItem(THEME_STORAGE_KEY) ?? 'null');
      if (isThemeId(saved?.theme)) this.themeId.set(saved.theme);
      if (isThemeMode(saved?.mode)) this.mode.set(saved.mode);
    } catch {
      // Storage blocked or corrupt: keep the defaults.
    }
  }

  private persist(theme: ThemeId, mode: ThemeMode): void {
    try {
      this.doc.defaultView?.localStorage.setItem(THEME_STORAGE_KEY, JSON.stringify({ theme, mode }));
    } catch {
      // Storage blocked: the choice simply won't survive a reload.
    }
  }
}
