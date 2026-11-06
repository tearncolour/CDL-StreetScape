# CDLC.TS UI (the official UI template)

The mod's panels are built with the template of the game's modding toolchain (`npx create-csii-ui-mod`: TypeScript, React, webpack; the template is in
`<game>/Cities2_Data/Content/Game/.ModdingToolchain/npx-create-csii-ui-mod`). `UI/` holds that project; the repository root is the C# project.

## Build

The C# build does it (`BuildUi` in `CDLC.TS.csproj`): `npm ci` when `UI/node_modules` lacks webpack (the first build on a machine), then `npm run build` into `obj\<configuration>\ui`, and
exactly `CDLC.TS.mjs` and `CDLC.TS.css` from there into the content. It needs node >=18 and npm on the PATH (node v24 and npm 11 here) and, for the first `npm ci`, the network. Behind a proxy
give it to that one command, **not** to dotnet (a proxy variable in dotnet's environment breaks the post-processor's local calls; the build says so):

```
dotnet build CDLC.TS.csproj -c Release -p:NpmProxy=http://127.0.0.1:7897     # the first build on a machine
dotnet build CDLC.TS.csproj -c Release                                       # every later one
```

By hand, for the UI tests and for working on the panel:

```
cd UI
HTTPS_PROXY=http://127.0.0.1:7897 npm ci     # once (the lock file gives the same packages and the same bundle everywhere); without a proxy: npm ci
npm run build      # UI/build/CDLC.TS.mjs and UI/build/CDLC.TS.css, nothing else
npm run check      # the built bundle (see "Source and checks")
```

- The output folder is `UI/build`, or the folder named by the environment variable `CDLC_UI_OUT` (the C# build sets it). **The template's own default, `<user data>\Mods\<id>`, is not used:**
  it would put a second copy of the mod next to the subscribed one. The build refuses an output path inside `CSII_USERDATAPATH\Mods`.
- Id, author, version and dependencies are read from the repository's `mod.json` (one copy, `../mod.json`).
- The game loads `<id>.mjs`, calls its default export with its module registry, and loads `<id>.css` beside it when the module exports `hasCSS` (the game's UI bundle does exactly that;
  `tools/css-presence.js` adds `hasCSS`).
- Functions keep their names in the bundle (Terser `keep_fnames`): the offline tests tell a component by its name, and the game's error stacks name it.
- The stylesheet is plain CSS written for the game's CSS engine and is taken as it is (no sass, no url resolving, no minifying, no class-name hashing: the script uses the literal
  `cdlts-*` names).

## Source and checks

`src/ts-ui.js` and `src/CDLC.TS.css` are the source of the panel, edited by hand. They were the hand-written `CDLC.TS.mjs` and `CDLC.TS.css` of the repository root, which were taken out
once the C# build shipped the built files. **The equivalence of the bundle with that legacy module was proven at merge commit `34bd5ea`**: the exports (57 names, kinds, the default's arity,
`hasCSS`), the stylesheet, the 17 pages `Tests/preview-dock.mjs` draws from the real component trees (byte for byte) and the counts of `ui-smoke` (1,726), `ui-edit-sequences` (14) and
`ui-language` (808) were the same on both. The legacy file lives in history: `git show 34bd5ea:CDLC.TS.mjs` and `git show 34bd5ea:CDLC.TS.css`, and the script that proved it:
`git show 34bd5ea:UI/tools/compare-with-legacy.mjs` (with `legacy-transform.mjs` and `sync-from-legacy.mjs` beside it).

`npm run check` (`tools/check-build.mjs`) checks the bundle in `UI/build` (or `--bundle <a .mjs>`) and exits 1 on any difference:

- the exports against `tools/exports.json` (names, kinds, arity and name of each function), `hasCSS` true, the default export a function of one argument. The file is the 57 exports of the
  legacy module and the bundle at `34bd5ea` (equal); after an export is added or taken out on purpose, `node tools/check-build.mjs --write-exports` writes it again from the bundle;
- the stylesheet is `src/CDLC.TS.css` (but for the final line end the stylesheet loader adds);
- the folder of the bundle holds exactly `CDLC.TS.css` and `CDLC.TS.mjs`;
- the bundle is byte for byte what the build makes of `src/` now (a changed constant that no suite reads is found here);
- `Tests/preview-dock.mjs` draws its pages from the real component trees, and `ui-smoke`, `ui-edit-sequences` and `ui-language` pass on the bundle; their result lines are printed.
  `ui-smoke` counts 1,726 only with the game installed (`CSII_INSTALLATIONPATH` of the **user** environment: a shell's own copy can name a missing drive); without it 1,664, and says so.

The offline UI tests (`ui-smoke`, `ui-edit-sequences`, `ui-language`, `preview-dock`) take the bundle `UI/build/CDLC.TS.mjs` (`Tests/ui-target.mjs`); `CDLC_UI_FILE=<a .mjs>` names another file,
for instance the one the C# build made in `obj\<configuration>\ui`. There is no other copy of the script: without the bundle they stop with `run npm run build in UI/` (exit 2).
The code-reading checks (strings, keys, classes, Chinese in literals) read `src/ts-ui.js`; so do the C# tests that look into the script (`KerbTabTests`, `TrimTests`).

**What none of this shows is the game itself:** loading the module, the registrations (`GameBottomRight`, `Game`, the wrapped `AssetMenu`) and the stylesheet.

## Phase 2 (not done)

Take the hand-written code (about 2,000 lines of JavaScript in one file: texts, parsing of what the C# side sends, panels, dock, editor, settings, keys) apart into typed TSX modules,
with the template's typings in `types/` (`cs2/ui`, `cs2/api`, `cs2/bindings`, `cs2/l10n`, `cs2/modding`): `import` instead of `window["cs2/..."]`, real components instead of `h(...)`,
one file per panel. The existing suites and `npm run check` are the oracle for each step.
