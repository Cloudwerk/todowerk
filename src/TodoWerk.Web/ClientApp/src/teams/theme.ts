import {
  teamsDarkTheme,
  teamsHighContrastTheme,
  teamsLightTheme,
} from '@fluentui/react-components';
import type { Theme } from '@fluentui/react-components';

/**
 * The themes a Teams client reports. `glass` arrives on Apple Vision Pro, which nothing here can
 * be tested against — so it is named rather than left to the default, and falls back deliberately
 * ([ADR-0004 amendment](../../../../../docs/adr/0004-fluent-ui-v9.md)).
 */
export type TeamsThemeName = 'default' | 'dark' | 'contrast' | 'glass';

/**
 * Fluent's Teams ramps, not CloudWerk's. A tab is a panel inside somebody else's window, and one
 * that stays cream while the window around it goes black reads as broken rather than as branded.
 * High contrast is not a brand ramp at all and cannot be one.
 *
 * Anything unrecognised — `glass`, an unsubstituted manifest placeholder, a theme Microsoft adds
 * after this was written — falls back to light rather than throwing. A tab that renders in the
 * wrong palette is a blemish; a tab that does not render is a broken product.
 */
export function teamsThemeFor(name: string | null | undefined): Theme {
  switch (name) {
    case 'dark':
      return teamsDarkTheme;
    case 'contrast':
      return teamsHighContrastTheme;
    default:
      return teamsLightTheme;
  }
}

/**
 * The theme from the tab's own URL, so the first paint is already right rather than flashing light
 * and correcting a moment later.
 *
 * Two parameters because the manifest carries two placeholders. `{app.theme}` is the v2 spelling
 * and `{theme}` the v1 one, and mobile Teams substitutes only the v1 placeholders — so on a phone
 * the first arrives as the literal text `{app.theme}` and the second is the real answer. Whichever
 * one names a theme wins; a value still wearing braces is not one.
 */
export function themeFromSearch(search: string): TeamsThemeName | null {
  const parameters = new URLSearchParams(search);

  for (const name of ['theme', 'v1theme']) {
    const value = parameters.get(name);

    if (value !== null && isThemeName(value)) {
      return value;
    }
  }

  return null;
}

function isThemeName(value: string): value is TeamsThemeName {
  return value === 'default' || value === 'dark' || value === 'contrast' || value === 'glass';
}
