# Fluent UI v9 with a custom CloudWerk brand theme

The client uses Fluent UI React v9 as its component library, themed with CloudWerk's brand ramp (primary teal `#006970`, secondary green `#326b24`) via `createLightTheme` over custom `BrandVariants`. The Workbench design — flat cards, a dense data table, pill badges, Segoe UI — maps directly onto Fluent's `DataGrid`, `Toolbar`, `Card`, `Badge`, `TabList`, and `ProgressBar` with token-level theming; no component fights its own design language to match the intended look.

## Considered options

- **Hand-rolled components with design tokens** — viable and dependency-free, but rebuilding a sortable, multi-select, virtualizable data grid (the core of the product) is real work that Fluent already ships. There also turned out to be no design gap for Fluent to bridge.
- **MUI (Material)** — rejected: a heavy dependency whose Material anatomy (state layers, elevation, MD switches) actually would fight the design, which is flat and Segoe-set despite its palette's Material-generator origin.
- **Fluent v8** — rejected: legacy line, v9 is where Teams-adjacent development lives.

## Consequences

- The Teams tab gets a native-feeling component set for free.
- Theme tokens are the only sanctioned styling override point; per-component style overrides in pursuit of pixel-perfection are out of scope.
- Fluent v9's component coverage bounds the design vocabulary; anything the library lacks gets built on its primitives rather than imported from a second library.

## Amendment (M4): the Teams tab follows its host's theme, not CloudWerk's

The CloudWerk brand theme is the browser's, and only the browser's. Inside the Teams tab the client
uses Fluent's own `teamsLightTheme`, `teamsDarkTheme` and `teamsHighContrastTheme`, driven by the
theme the Teams client reports and re-driven whenever the user changes it.

This partly reverses the sentence above, and only slightly: the component library, the token model
and the design vocabulary are unchanged, and only the ramp differs. The reason is that a tab is a
panel inside somebody else's window, not a CloudWerk page. A panel that stays cream while the window
around it goes black looks broken, not branded. Store validation checks for it, including high
contrast, which is not a brand ramp at all and cannot be branded by design.

Two consequences reach beyond the tab. The client gains `createDarkTheme(cloudwerkBrand)`, which the
browser did not use until the M5 amendment below: dark mode for the Workbench is a feature with its
own scope and does not belong in a Teams ticket. And the theme arrives as a manifest placeholder in
the tab's own URL, so the first paint is already correct and does not flash light first. The Teams
client also reports a `glass` theme on Apple Vision Pro, which nothing here can be tested against;
it falls back rather than failing.

## Amendment (M5): the browser follows the operating system, and nothing else

The browser Workbench renders `createDarkTheme(cloudwerkBrand)` when the operating system asks for
it through `prefers-color-scheme`, from the first paint, and re-picks the ramp in place when the
setting changes with the page open. This is the feature the M4 amendment said belonged here.

There is no toggle and nothing stored. A machine that already knows whether its owner wants a dark
screen is a better authority than a preference a person has to find, set, and then keep in step with
every other application they use. A setting would also have to be migrated, synced between the
browser and the tab, and explained, for an answer that is already available. The document also
declares `color-scheme: light dark`, so the surfaces no theme reaches (the canvas behind the app, the
scrollbars, native controls) turn with it instead of staying white around a dark page.

Tokens only, deliberately: the whole application faces a redesign, and component-level dark styling
done now would be done twice. Nearly every colour in this client already came from the theme, so the
ramp inverting is most of the work. Two things needed more than the ramp. A fixed palette orange
stood in for a warning and now uses the status token that inverts with everything else. And links
move one slot up the brand ramp in the dark theme, because `createDarkTheme` maps the resting link
colour to slot 100 whatever the ramp is, and CloudWerk's teal at slot 100 reads at 4.01:1 against the
panel behind it, under the 4.5:1 that a body-sized link needs. Slot 110 gives 5.25:1, close to the
5.18:1 that Fluent's own dark theme gives its links. The test suite checks that arithmetic for every
foreground the client paints on every surface it paints it on, both ways round, instead of trusting
a screenshot somebody once looked at.

The tab still follows the theme its host reports and runs none of this. One line of it does reach
the tab all the same: the warning colour lives in a component both surfaces render, so the tab's
failed-list text moves to the status token too — readable in all three Teams ramps, high contrast
included, where it resolves to the same white it always did.

Windows High Contrast in the browser is not addressed here and is not claimed. The tab has
`teamsHighContrastTheme` because the Teams client names a theme; a browser page meets `forced-colors`
instead, which the browser applies over whatever the page chose, and which nothing here has checked.
