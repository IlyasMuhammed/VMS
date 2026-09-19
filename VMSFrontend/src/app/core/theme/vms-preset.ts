import { definePreset } from '@primeng/themes';
import Aura from '@primeng/themes/aura';

/**
 * PrimeNG preset that contains NO colours. Every value is a var(--vms-*) token, so PrimeNG's
 * components (tables, selects, dialogs, tags, toasts...) change with the theme and mode exactly
 * like our own markup. The actual colours live in src/styles/tokens/.
 */

const STEPS = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950] as const;

/** {50: 'var(--vms-<name>-50)', ...} */
const ramp = (name: string, withZero = false): Record<number, string> => {
  const out: Record<number, string> = {};
  if (withZero) out[0] = `var(--vms-${name}-0)`;
  for (const s of STEPS) out[s] = `var(--vms-${name}-${s})`;
  return out;
};

const token = (name: string) => `var(--vms-${name})`;

// The same mapping serves light and dark: the tokens themselves change with data-mode.
const scheme = {
  surface: ramp('neutral', true),
  primary: {
    color: token('brand'),
    contrastColor: token('on-brand'),
    hoverColor: token('brand-hover'),
    activeColor: token('brand-active'),
  },
  highlight: {
    background: token('brand-tint'),
    focusBackground: token('brand-tint-strong'),
    color: token('brand-text'),
    focusColor: token('brand-text'),
  },
  mask: { background: token('overlay'), color: token('text') },
  formField: {
    background: token('surface'),
    disabledBackground: token('surface-soft'),
    filledBackground: token('surface-soft'),
    filledHoverBackground: token('surface-soft'),
    filledFocusBackground: token('surface-soft'),
    borderColor: token('border-strong'),
    hoverBorderColor: token('muted'),
    focusBorderColor: token('brand'),
    invalidBorderColor: token('danger'),
    color: token('text'),
    disabledColor: token('muted'),
    placeholderColor: token('muted'),
    invalidPlaceholderColor: token('danger'),
    floatLabelColor: token('muted'),
    floatLabelFocusColor: token('brand-text'),
    floatLabelActiveColor: token('muted'),
    floatLabelInvalidColor: token('danger'),
    iconColor: token('muted'),
    shadow: 'none',
  },
  text: {
    color: token('text'),
    hoverColor: token('text'),
    mutedColor: token('muted'),
    hoverMutedColor: token('text'),
  },
  content: {
    background: token('surface'),
    hoverBackground: token('surface-soft'),
    borderColor: token('border'),
    color: token('text'),
    hoverColor: token('text'),
  },
  overlay: {
    select: { background: token('surface'), borderColor: token('border'), color: token('text') },
    popover: { background: token('surface'), borderColor: token('border'), color: token('text') },
    modal: { background: token('surface'), borderColor: token('border'), color: token('text') },
  },
  list: {
    option: {
      focusBackground: token('surface-soft'),
      selectedBackground: token('brand-tint'),
      selectedFocusBackground: token('brand-tint-strong'),
      color: token('text'),
      focusColor: token('text'),
      selectedColor: token('brand-text'),
      selectedFocusColor: token('brand-text'),
      icon: { color: token('muted'), focusColor: token('text') },
    },
    optionGroup: { background: 'transparent', color: token('muted') },
  },
  navigation: {
    item: {
      focusBackground: token('surface-soft'),
      activeBackground: token('surface-soft'),
      color: token('text'),
      focusColor: token('text'),
      activeColor: token('text'),
      icon: { color: token('muted'), focusColor: token('text'), activeColor: token('text') },
    },
    submenuLabel: { background: 'transparent', color: token('muted') },
    submenuIcon: { color: token('muted'), focusColor: token('text'), activeColor: token('text') },
  },
};

export const VmsPreset = definePreset(Aura, {
  primitive: {
    // Corners follow the theme (theme A is softer, theme B rounder).
    borderRadius: {
      none: '0',
      xs: 'calc(var(--vms-radius-sm) / 3)',
      sm: 'calc(var(--vms-radius-sm) * 0.66)',
      md: 'var(--vms-radius-sm)',
      lg: 'var(--vms-radius)',
      xl: 'calc(var(--vms-radius) * 1.2)',
    },
    // Hues PrimeNG uses for severities (success, warn, danger, info, help).
    red: ramp('red'),
    orange: ramp('orange'),
    yellow: ramp('yellow'),
    green: ramp('green'),
    sky: ramp('sky'),
    blue: ramp('blue'),
    purple: ramp('purple'),
  },
  semantic: {
    primary: ramp('primary'),
    focusRing: { color: token('focus-ring') },
    overlay: {
      select: { shadow: token('shadow-md') },
      popover: { shadow: token('shadow-md') },
      modal: { shadow: token('shadow-lg') },
      navigation: { shadow: token('shadow-md') },
    },
    colorScheme: { light: scheme, dark: scheme },
  },
});
