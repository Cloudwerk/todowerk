import { describe, expect, it } from 'vitest';
import { needsDesktopScreen } from './screen';

describe('needsDesktopScreen', () => {
  it('names the three phone and tablet hosts', () => {
    expect(needsDesktopScreen('android')).toBe(true);
    expect(needsDesktopScreen('ios')).toBe(true);
    expect(needsDesktopScreen('ipados')).toBe(true);
  });

  // A host that did not say is treated as a desktop: a wide table is a smaller mistake than a
  // closed door in front of somebody on a laptop whose Teams answered late.
  it('treats desktop, web and unknown hosts as a desktop screen', () => {
    expect(needsDesktopScreen('desktop')).toBe(false);
    expect(needsDesktopScreen('web')).toBe(false);
    expect(needsDesktopScreen('macos')).toBe(false);
    expect(needsDesktopScreen(undefined)).toBe(false);
  });
});
