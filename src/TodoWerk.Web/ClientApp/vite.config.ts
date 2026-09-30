import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Backend dev URL; matches src/TodoWerk.Web/Properties/launchSettings.json.
const backend = 'https://localhost:7080';

export default defineConfig({
  plugins: [react()],
  build: {
    // The MSBuild publish target ships this folder as the app's static content.
    outDir: '../wwwroot',
    emptyOutDir: true,
    rolldownOptions: {
      // Three documents, three bundles. `index.html` is the browser SPA; `teams/index.html` is the
      // tab and the only one that pulls in TeamsJS; `teams/auth-end.html` is the popup's last stop
      // and must stay tiny, because Teams gives a popup a bounded time to report back and a slow
      // document spends it. Their paths here are their paths under the web root, which is what the
      // server serves them from and the manifest points at.
      input: {
        browser: fileURLToPath(new URL('./index.html', import.meta.url)),
        teams: fileURLToPath(new URL('./teams/index.html', import.meta.url)),
        'teams-auth-end': fileURLToPath(new URL('./teams/auth-end.html', import.meta.url)),
      },
      output: {
        // The framework baseline (React + Fluent) dwarfs our own code and changes
        // only on dependency bumps, so keep it in chunks the browser can cache
        // across deploys instead of one bundle invalidated by every app change.
        codeSplitting: {
          groups: [
            { name: 'react', test: /node_modules[\\/](react|react-dom|scheduler|react-router)[\\/]/ },
            { name: 'fluent', test: /node_modules[\\/](@fluentui|@griffel|keyborg|tabster)[\\/]/ },
            // Its own group, and not for caching: keeping TeamsJS out of the shared chunks is what
            // makes "the browser bundle contains no TeamsJS" checkable at all, rather than a claim
            // about an import graph nobody re-reads.
            { name: 'teams-js', test: /node_modules[\\/]@microsoft[\\/]teams-js[\\/]/ },
          ],
        },
      },
    },
  },
  server: {
    proxy: {
      '/api': { target: backend, secure: false },
      '/auth': { target: backend, secure: false },
      '/health': { target: backend, secure: false },
    },
  },
});
