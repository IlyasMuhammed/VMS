export type ThemeId = 'a' | 'b';
export type ThemeMode = 'light' | 'dark' | 'system';
export type ShellLayout = 'sidebar' | 'topbar';

export interface ThemeDef {
  id: ThemeId;
  label: string;
  description: string;
  /** How the app shell arranges navigation for this theme. */
  layout: ShellLayout;
}

export const THEMES: Record<ThemeId, ThemeDef> = {
  a: { id: 'a', label: 'Console', description: 'Sidebar navigation, blue', layout: 'sidebar' },
  b: { id: 'b', label: 'Workspace', description: 'Top navigation, teal', layout: 'topbar' },
};

export const THEME_LIST: ThemeDef[] = Object.values(THEMES);

export const DEFAULT_THEME: ThemeId = 'a';
export const DEFAULT_MODE: ThemeMode = 'system';

/**
 * Keep in sync with the inline script in src/index.html, which applies the saved choice before
 * Angular starts so the page never flashes the wrong theme.
 */
export const THEME_STORAGE_KEY = 'vms.theme';

export const isThemeId = (v: unknown): v is ThemeId => v === 'a' || v === 'b';
export const isThemeMode = (v: unknown): v is ThemeMode => v === 'light' || v === 'dark' || v === 'system';
