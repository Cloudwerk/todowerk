import { useSyncExternalStore } from 'react';
import { createDarkTheme, createLightTheme } from '@fluentui/react-components';
import type { BrandVariants, Theme } from '@fluentui/react-components';

// CloudWerk brand ramp around primary teal #006970 (ADR-0004).
// Slot 80 is the light theme's colorBrandBackground.
const cloudwerkBrand: BrandVariants = {
  10: '#001011',
  20: '#001B1D',
  30: '#002729',
  40: '#003336',
  50: '#004044',
  60: '#004D52',
  70: '#005B61',
  80: '#006970',
  90: '#007F87',
  100: '#00959E',
  110: '#0AACB5',
  120: '#33BAC3',
  130: '#59C8D0',
  140: '#7ED6DC',
  150: '#A5E4E8',
  160: '#CCF1F4',
};

export const cloudwerkLightTheme: Theme = createLightTheme(cloudwerkBrand);

/**
 * The CloudWerk ramp in the dark, worn by the browser Workbench whenever the operating system asks
 * for it ([ADR-0004](../../../../docs/adr/0004-fluent-ui-v9.md), the amendment on the browser's
 * theme). The Teams tab never reaches for it: a tab follows the theme its host reports.
 *
 * Links are moved one slot up the ramp from where `createDarkTheme` puts them. The generator maps
 * the resting link colour to slot 100 whatever the ramp is, and CloudWerk's teal is darker there
 * than Microsoft's blue: slot 100 lands at 4.01:1 against the panel behind it, under the 4.5:1 a
 * body-sized link has to clear. Slot 110 lands at 5.25:1 — within a whisker of the 5.18:1 Fluent's
 * own dark theme gives its links — and hover and pressed keep their positions either side of it.
 * A shift rather than a chosen colour: the ramp stays the authority, and nothing here is a second
 * hard-coded palette.
 */
export const cloudwerkDarkTheme: Theme = {
  ...createDarkTheme(cloudwerkBrand),
  colorBrandForegroundLink: cloudwerkBrand[110],
  colorBrandForegroundLinkHover: cloudwerkBrand[120],
  // Left below the line, where Fluent's own dark theme leaves it (3.85:1): pressed is the state
  // under a held mouse button, and buying a rung here would cost the ladder its direction.
  colorBrandForegroundLinkPressed: cloudwerkBrand[100],
  colorBrandForegroundLinkSelected: cloudwerkBrand[110],
};

/**
 * The one question the browser asks about the palette, and the whole of its dark-mode input. There
 * is deliberately no toggle and nothing stored: an operating system that already knows the answer
 * is a better authority than a preference a person has to find, set and then keep in step.
 */
const prefersDarkQuery = '(prefers-color-scheme: dark)';

/** The ramp for a colour scheme. The mapping, without the machinery for discovering it. */
export function browserThemeFor(prefersDark: boolean): Theme {
  return prefersDark ? cloudwerkDarkTheme : cloudwerkLightTheme;
}

/**
 * The browser SPA's theme: dark when the operating system is, from the first paint rather than
 * after a correcting flash, and re-picked in place when somebody changes the setting with the page
 * open.
 *
 * Subscribed to through `useSyncExternalStore` rather than an effect over `useState`, because the
 * setting can change between the render and the effect that would have started listening — and the
 * change that fell into that gap would never arrive.
 */
export function useBrowserTheme(): Theme {
  return browserThemeFor(
    useSyncExternalStore(subscribeToColourScheme, osPrefersDark, () => false),
  );
}

function subscribeToColourScheme(onChanged: () => void): () => void {
  const query = colourSchemeQuery();

  query?.addEventListener('change', onChanged);

  return () => query?.removeEventListener('change', onChanged);
}

function osPrefersDark(): boolean {
  return colourSchemeQuery()?.matches ?? false;
}

/**
 * `undefined` where the question cannot be asked at all. Every browser this client supports answers
 * it; a test environment need not, and a page that threw rather than rendering because it could not
 * learn the palette would be a broken product over a blemish.
 */
function colourSchemeQuery(): MediaQueryList | undefined {
  return typeof window.matchMedia === 'function' ? window.matchMedia(prefersDarkQuery) : undefined;
}
