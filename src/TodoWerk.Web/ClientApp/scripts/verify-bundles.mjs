import { readFileSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Checks the three built documents against the build output rather than against the import graph.
 *
 * The claim being checked is that the browser SPA gained no TeamsJS. It is easy to believe from
 * reading the imports and easy to break without noticing — one shared component reaching for
 * `app.openLink()` puts a 47 kB library and a Teams handshake into every browser page load, and
 * nothing on screen would say so. The same check keeps the consent popup's landing document tiny,
 * which is a correctness requirement rather than a size one: Teams gives an authentication popup a
 * bounded time to report back, and a document that spends it parsing React reports the flow as
 * cancelled by a person who completed it.
 *
 * Runs as part of `npm run build`, so it runs everywhere the build does — including CI.
 */

const clientRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const webRoot = join(clientRoot, '..', 'wwwroot');

/** Distinctive TeamsJS runtime text. Two of them, because either alone could be minified away. */
const teamsJsMarkers = ['registerOnThemeChangeHandler', 'teams-js/validDomains'];

/** Distinctive React and Fluent runtime text, for the document that must contain neither. */
const frameworkMarkers = ['react-dom', 'useInsertionEffect', '@griffel'];

const documents = {
  browser: 'index.html',
  tab: join('teams', 'index.html'),
  authEnd: join('teams', 'auth-end.html'),
};

const failures = [];

for (const [name, relativePath] of Object.entries(documents)) {
  if (!existsSync(join(webRoot, relativePath))) {
    failures.push(`${name}: ${relativePath} was not built.`);
  }
}

if (failures.length === 0) {
  const browser = chunksReachableFrom(documents.browser);
  const tab = chunksReachableFrom(documents.tab);
  const authEnd = chunksReachableFrom(documents.authEnd);

  for (const marker of teamsJsMarkers) {
    const found = [...browser].filter((chunk) => contentOf(chunk).includes(marker));

    if (found.length > 0) {
      failures.push(
        `The browser bundle contains TeamsJS ("${marker}" in ${found.join(', ')}). `
        + 'Something reachable from index.html imports @microsoft/teams-js — only the tab may.',
      );
    }
  }

  // The other direction, so a passing run cannot mean "the markers stopped matching". If TeamsJS
  // is absent from the tab too, the check above proves nothing and this says so.
  if (!teamsJsMarkers.some((marker) => [...tab].some((chunk) => contentOf(chunk).includes(marker)))) {
    failures.push(
      'The Teams tab bundle contains none of the TeamsJS markers either, so the check above passed '
      + 'for the wrong reason. Update the markers in this script.',
    );
  }

  for (const marker of frameworkMarkers) {
    const found = [...authEnd].filter((chunk) => contentOf(chunk).includes(marker));

    if (found.length > 0) {
      failures.push(
        `The consent popup's landing document pulls in a framework ("${marker}" in ${found.join(', ')}). `
        + 'It must initialize TeamsJS, notify success, and import nothing else.',
      );
    }
  }
}

if (failures.length > 0) {
  console.error('Bundle boundaries are wrong:\n');

  for (const failure of failures) {
    console.error(`  - ${failure}`);
  }

  process.exit(1);
}

console.log('Bundle boundaries hold: no TeamsJS in the browser bundle, no framework in auth-end.');

/**
 * Every asset a document ends up executing: the ones its markup names, plus anything those chunks
 * name in turn — a lazily imported chunk is still part of what the document can run.
 */
function chunksReachableFrom(documentPath) {
  const markup = contentOf(documentPath);
  const reachable = new Set();
  const pending = [...referencedAssets(markup)];

  while (pending.length > 0) {
    const asset = pending.pop();

    if (reachable.has(asset) || !existsSync(join(webRoot, asset))) {
      continue;
    }

    reachable.add(asset);
    pending.push(...referencedAssets(contentOf(asset)));
  }

  return reachable;
}

function referencedAssets(text) {
  return [...text.matchAll(/assets\/[A-Za-z0-9_.-]+\.js/g)].map((match) => match[0]);
}

function contentOf(relativePath) {
  return readFileSync(join(webRoot, relativePath), 'utf8');
}
