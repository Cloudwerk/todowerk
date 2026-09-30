import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// Separate from vite.config.ts rather than a `test` key inside it: Vite's own config type does
// not know that key, so folding the two together fails `tsc -b` — and the typecheck is the gate
// CI runs on the client.
export default defineConfig({
  plugins: [react()],
  // tabster, which @fluentui/react-components pulls in, declares `"type": "module"` but points
  // `main` at a CommonJS bundle and ships no `exports` map. Node's ESM loader therefore hands that
  // .cjs file to Fluent's ESM build and the named imports fail; Vite reads `module` and resolves
  // the ESM build instead, which is why the browser bundle is fine and only the test run breaks —
  // anything Vitest leaves external is loaded by Node itself. That happens on the Linux runner but
  // not on a Windows workstation, so this looks like dead config locally and is not.
  resolve: {
    // Skip tabster's CommonJS bundle even once Vite is doing the resolving: its sourcemap comment
    // names a file the package does not ship, and Vite warns about that on every run.
    alias: { tabster: 'tabster/dist/esm/index.js' },
  },
  test: {
    // A DOM, because what this suite is for is the state the client holds before the server has
    // confirmed it, and half of that only exists once something has rendered.
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['src/test-setup.ts'],
    // Globals stay off: every test names what it imports, which is the same rule the app code
    // follows and the reason `types` is empty in tsconfig.app.json.
    globals: false,
    restoreMocks: true,
    // Above the five seconds `src/test-setup.ts` gives Testing Library, so a wait that runs out
    // says which query it was waiting on rather than which test the runner gave up on.
    testTimeout: 15_000,
    // Keeps the Fluent import chain inside Vite rather than Node, so the tabster resolution above
    // applies to it.
    server: { deps: { inline: [/@fluentui\//] } },
  },
});
