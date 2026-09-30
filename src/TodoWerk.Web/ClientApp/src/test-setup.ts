import { cleanup, configure } from '@testing-library/react';
import { afterEach } from 'vitest';

// Testing Library only registers this itself when Vitest's globals are on, and they are off here
// so that every test names what it imports. Without it each render stacks on the last one's DOM
// and a query for one button finds three.
afterEach(cleanup);

// How long `findBy*` and `waitFor` give an answer to arrive. Testing Library's own default is one
// second of wall clock, and wall clock is the wrong unit for it: nothing in this suite waits for
// anything slower than a resolved promise, so the second is not a budget for the product but for
// the machine's scheduler. A loaded runner — CI's shares one with a SQL Server container — can
// leave a Vitest worker unscheduled for longer than that, and the run then goes red for a reason
// no reader can tell apart from a real one.
//
// Five seconds costs nothing on a green run, because a wait that succeeds returns the moment it
// can. It costs four extra seconds on each wait in a run that was going to fail anyway, which is
// the cheaper half of the trade. Deliberately not fewer workers, which would buy the same headroom
// by making every run slower, red and green alike.
configure({ asyncUtilTimeout: 5_000 });

// jsdom implements no ResizeObserver, and Fluent's MessageBar constructs one to decide whether its
// actions sit beside the text or below it. Without this the banner cannot be rendered at all in
// this environment, which would be a gap in the test host mistaken for a gap in the product.
// A stub rather than a polyfill: nothing here asserts on layout, only on what is on the screen.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}
