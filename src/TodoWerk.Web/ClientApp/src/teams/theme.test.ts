import {
  teamsDarkTheme,
  teamsHighContrastTheme,
  teamsLightTheme,
} from '@fluentui/react-components';
import { describe, expect, it } from 'vitest';
import { cloudwerkLightTheme } from '../theme';
import { teamsThemeFor, themeFromSearch } from './theme';

describe('teamsThemeFor', () => {
  it('maps the three themes a Teams client can actually be in', () => {
    expect(teamsThemeFor('default')).toBe(teamsLightTheme);
    expect(teamsThemeFor('dark')).toBe(teamsDarkTheme);
    expect(teamsThemeFor('contrast')).toBe(teamsHighContrastTheme);
  });

  /**
   * `glass` arrives on Apple Vision Pro and nothing here can be tested against it. It falls back
   * rather than throwing, because a tab in the wrong palette is a blemish and a tab that does not
   * render is a broken product.
   */
  it.each(['glass', 'something-microsoft-adds-later', '{app.theme}', '', null, undefined])(
    'falls back rather than failing for %p',
    (name) => {
      expect(teamsThemeFor(name)).toBe(teamsLightTheme);
    },
  );

  /** The tab never wears the CloudWerk ramp; the browser never wears Teams'. */
  it('is never the browser theme', () => {
    expect(teamsThemeFor('default')).not.toBe(cloudwerkLightTheme);
  });
});

describe('themeFromSearch', () => {
  it('reads the v2 placeholder the desktop and web clients substitute', () => {
    expect(themeFromSearch('?theme=dark&v1theme=%7Btheme%7D')).toBe('dark');
  });

  /**
   * Mobile Teams substitutes only the v1 placeholders, so `{app.theme}` arrives as literal text
   * and the second parameter is the only real answer. Reading both is what makes the first paint
   * right on a phone.
   */
  it('falls through to the v1 placeholder when the v2 one was not substituted', () => {
    expect(themeFromSearch('?theme=%7Bapp.theme%7D&v1theme=contrast')).toBe('contrast');
  });

  it('reports nothing when neither placeholder was substituted', () => {
    expect(themeFromSearch('?theme=%7Bapp.theme%7D&v1theme=%7Btheme%7D')).toBeNull();
  });

  it('reports nothing when the tab was opened with no theme at all', () => {
    expect(themeFromSearch('')).toBeNull();
  });
});
