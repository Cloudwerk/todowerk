import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { teamsDarkTheme } from '@fluentui/react-components';
import { browserThemeFor, cloudwerkDarkTheme, cloudwerkLightTheme, useBrowserTheme } from './theme';

describe('browserThemeFor', () => {
  it('answers the CloudWerk ramp in whichever direction the operating system asked for', () => {
    expect(browserThemeFor(true)).toBe(cloudwerkDarkTheme);
    expect(browserThemeFor(false)).toBe(cloudwerkLightTheme);
  });

  /** The browser never wears Teams' ramp, the same way the tab never wears CloudWerk's. */
  it('is never a Teams theme', () => {
    expect(browserThemeFor(true)).not.toBe(teamsDarkTheme);
  });
});

/**
 * The acceptance the dark ramp was built against, kept as arithmetic rather than as a memory of
 * having looked at the screen once. Every foreground this client paints, over every surface it
 * paints it on, in both directions of the ramp — because the failure mode of a generated theme is
 * a colour that reads well one way round and disappears the other.
 */
describe('the CloudWerk ramp, read against WCAG AA', () => {
  const surfaces = ['colorNeutralBackground1', 'colorNeutralBackground2'] as const;
  const foregrounds = [
    'colorNeutralForeground1',
    'colorNeutralForeground2',
    'colorNeutralForeground3',
    'colorStatusWarningForeground1',
    'colorBrandForegroundLink',
    'colorBrandForegroundLinkHover',
    'colorBrandForegroundLinkSelected',
  ] as const;

  it.each([
    ['light', cloudwerkLightTheme],
    ['dark', cloudwerkDarkTheme],
  ])('reads on a panel in the %s', (_, theme) => {
    for (const surface of surfaces) {
      for (const foreground of foregrounds) {
        expect({
          pair: `${foreground} on ${surface}`,
          contrast: contrast(theme[foreground], theme[surface]) >= 4.5,
        }).toEqual({ pair: `${foreground} on ${surface}`, contrast: true });
      }
    }
  });

  /** The header is the one surface painted in the brand colour rather than a neutral. */
  it.each([
    ['light', cloudwerkLightTheme],
    ['dark', cloudwerkDarkTheme],
  ])('reads on the brand header in the %s', (_, theme) => {
    expect(
      contrast(theme.colorNeutralForegroundOnBrand, theme.colorBrandBackground),
    ).toBeGreaterThanOrEqual(4.5);
  });

  /** The organisation page's tiles sit one surface deeper than the panel around them. */
  it.each([
    ['light', cloudwerkLightTheme],
    ['dark', cloudwerkDarkTheme],
  ])('reads on a tile in the %s', (_, theme) => {
    expect(
      contrast(theme.colorNeutralForeground1, theme.colorNeutralBackground3),
    ).toBeGreaterThanOrEqual(4.5);
  });

  /** The mark the confirm dialog draws on the Hashtag a Change puts into a title. */
  it.each([
    ['light', cloudwerkLightTheme],
    ['dark', cloudwerkDarkTheme],
  ])('reads on the brand mark in the %s', (_, theme) => {
    expect(
      contrast(theme.colorBrandForeground2, theme.colorBrandBackground2),
    ).toBeGreaterThanOrEqual(4.5);
  });
});

describe('useBrowserTheme', () => {
  /**
   * The first render, not a correction a moment later: the query is asked before anything paints,
   * so a person whose machine is dark never sees the light theme flash past.
   */
  it('is dark on the first render when the operating system is dark', () => {
    stubColourScheme(true);

    expect(renderHook(() => useBrowserTheme()).result.current).toBe(cloudwerkDarkTheme);
  });

  it('is light on the first render when the operating system is light', () => {
    stubColourScheme(false);

    expect(renderHook(() => useBrowserTheme()).result.current).toBe(cloudwerkLightTheme);
  });

  /** Changing the setting re-themes the page in place; nothing here asks for a reload. */
  it('follows the operating system changing while the page is open', () => {
    const scheme = stubColourScheme(false);
    const { result } = renderHook(() => useBrowserTheme());

    act(() => scheme.change(true));
    expect(result.current).toBe(cloudwerkDarkTheme);

    act(() => scheme.change(false));
    expect(result.current).toBe(cloudwerkLightTheme);
  });

  it('stops listening once nothing is rendering it', () => {
    const scheme = stubColourScheme(true);

    renderHook(() => useBrowserTheme()).unmount();

    expect(scheme.listeners()).toBe(0);
  });

  /**
   * A browser that cannot answer the question gets the light theme rather than an exception —
   * the same trade the tab's theme makes, and for the same reason: a page in the wrong palette is
   * a blemish, a page that does not render is a broken product.
   */
  it('falls back to light where the browser cannot be asked', () => {
    vi.stubGlobal('matchMedia', undefined);

    expect(renderHook(() => useBrowserTheme()).result.current).toBe(cloudwerkLightTheme);
  });
});

afterEach(() => vi.unstubAllGlobals());

/**
 * A media query list that answers `(prefers-color-scheme: dark)` and can be told to change its
 * mind, because jsdom's own never changes and the question here is what happens when it does.
 */
function stubColourScheme(dark: boolean) {
  const handlers = new Set<(event: MediaQueryListEvent) => void>();
  const query = {
    matches: dark,
    media: '(prefers-color-scheme: dark)',
    addEventListener: (_: 'change', handler: (event: MediaQueryListEvent) => void) => {
      handlers.add(handler);
    },
    removeEventListener: (_: 'change', handler: (event: MediaQueryListEvent) => void) => {
      handlers.delete(handler);
    },
  };

  vi.stubGlobal('matchMedia', (media: string) => {
    expect(media).toBe('(prefers-color-scheme: dark)');
    return query;
  });

  return {
    change(nowDark: boolean) {
      query.matches = nowDark;
      for (const handler of handlers) {
        handler({ matches: nowDark } as MediaQueryListEvent);
      }
    },
    listeners: () => handlers.size,
  };
}

/**
 * WCAG 2.2's contrast ratio, on the two `#rrggbb` values a Fluent theme holds. Written out rather
 * than pulled in: it is eight lines, and a dependency whose whole job is one formula from a
 * published standard is a dependency to keep updated.
 */
function contrast(foreground: string, background: string): number {
  const [lighter, darker] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);

  return (lighter + 0.05) / (darker + 0.05);
}

function luminance(colour: string): number {
  const channels = [1, 3, 5].map((start) => {
    const value = parseInt(colour.slice(start, start + 2), 16) / 255;

    return value <= 0.03928 ? value / 12.92 : Math.pow((value + 0.055) / 1.055, 2.4);
  });

  return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
}
