// Checks the built bundle of the panel (UI/build/CDLC.TS.mjs and .css, or another folder with --bundle <a .mjs>), after the legacy hand-written file it was proven against was taken out
// (the equivalence was shown at commit 9a338c3, see UI/README.md).
//   node UI/tools/check-build.mjs [--bundle <a .mjs>]      node UI/tools/check-build.mjs --write-exports   (after an export was added or taken out on purpose)
// It checks: the exports against UI/tools/exports.json (names, kinds, the arity and the name of each function, hasCSS); that the default export takes one argument (the game calls it with its
// module registry); the stylesheet against UI/src/CDLC.TS.css; that the bundle's folder holds exactly the .mjs and the .css; that the bundle is byte for byte what the build makes of UI/src now (a
// changed constant that no suite looks at is found here); that the pages Tests/preview-dock.mjs draws from the real component trees are made; and that ui-smoke, ui-edit-sequences and
// ui-language pass on it. Exit 1 on any difference. The game is not involved: what it does with the bundle (loading it, calling the default export, loading the stylesheet) is not shown here.
import { spawnSync } from "node:child_process";
import { existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const snapshotFile = join(root, "UI", "tools", "exports.json");
const args = process.argv.slice(2);
const bundle = args.includes("--bundle") ? resolve(args[args.indexOf("--bundle") + 1]) : join(root, "UI", "build", "CDLC.TS.mjs");
const sheet = bundle.replace(/\.mjs$/, ".css");
for (const file of [bundle, sheet]) {
    if (!existsSync(file)) {
        console.log(`missing: ${file} (cd UI && npm install && npm run build)`);
        process.exit(2);
    }
}

const module = await import(pathToFileURL(bundle).href + "?bundle");
const exported = {};
for (const name of Object.keys(module).sort()) {
    const value = module[name];
    exported[name] = typeof value === "function" ? { kind: "function", arity: value.length, name: name === "default" ? null : value.name } : { kind: typeof value };
}

if (args.includes("--write-exports")) {
    writeFileSync(snapshotFile, JSON.stringify(exported, null, 2) + "\n");
    console.log(`${Object.keys(exported).length} exports written to ${snapshotFile}`);
    process.exit(0);
}

let failures = 0;
function check(ok, text) {
    if (!ok) {
        failures++;
        console.log("  FAIL " + text);
    }
}

// ---- the exports
const expected = JSON.parse(readFileSync(snapshotFile, "utf8"));
const names = Object.keys(expected);
check(JSON.stringify(names) === JSON.stringify(Object.keys(exported)), `the export names differ: only in UI/tools/exports.json: ${names.filter(n => !(n in exported)).join(", ") || "-"}; only in the bundle: ${Object.keys(exported).filter(n => !(n in expected)).join(", ") || "-"}`);
for (const name of names) {
    if (name in exported) {
        check(JSON.stringify(exported[name]) === JSON.stringify(expected[name]), `${name}: ${JSON.stringify(exported[name])} against ${JSON.stringify(expected[name])} in UI/tools/exports.json`);
    }
}
check(module.hasCSS === true, `hasCSS is ${module.hasCSS} (the game loads the stylesheet only when it is true)`);
check(typeof module.default === "function" && module.default.length === 1, "the default export is a function of one argument (the game calls it with its module registry)");
console.log(`exports: ${names.length} names checked against UI/tools/exports.json`);

// ---- the bundle's folder holds the script and the stylesheet and nothing else (the content takes exactly these two)
const files = readdirSync(dirname(bundle)).sort();
check(JSON.stringify(files) === JSON.stringify(["CDLC.TS.css", "CDLC.TS.mjs"]), `${dirname(bundle)} holds ${files.join(", ")} (expected CDLC.TS.css and CDLC.TS.mjs)`);

// ---- the stylesheet is the source's but for the one line end the stylesheet loader adds
const cssSource = readFileSync(join(root, "UI", "src", "CDLC.TS.css"), "utf8"), cssBuilt = readFileSync(sheet, "utf8");
check(cssSource.trimEnd() === cssBuilt.trimEnd(), "the stylesheet is not UI/src/CDLC.TS.css");
console.log(`stylesheet: ${cssBuilt.length} characters, ${cssSource.trimEnd() === cssBuilt.trimEnd() ? "UI/src/CDLC.TS.css but for the final line end" : "DIFFERENT"}`);

// ---- the bundle is what the build makes of UI/src
const webpackCli = join(root, "UI", "node_modules", "webpack-cli", "bin", "cli.js");
if (existsSync(webpackCli)) {
    const rebuilt = mkdtempSync(join(tmpdir(), "cdlc-ui-rebuild-"));
    try {
        const made = spawnSync(process.execPath, [webpackCli], { cwd: join(root, "UI"), encoding: "utf8", env: { ...process.env, CDLC_UI_OUT: rebuilt }, maxBuffer: 64 * 1024 * 1024 });
        check(made.status === 0, `the build failed: ${(made.stdout || "").split("\n").slice(-6).join(" | ")}`);
        for (const [file, given] of [["CDLC.TS.mjs", bundle], ["CDLC.TS.css", sheet]]) {
            check(existsSync(join(rebuilt, file)) && readFileSync(join(rebuilt, file), "utf8") === readFileSync(given, "utf8"), `${given} is not what the build makes of UI/src now (cd UI && npm run build)`);
        }
        console.log("build: the bundle is what UI/src builds to, byte for byte");
    } finally {
        rmSync(rebuilt, { recursive: true, force: true });
    }
} else {
    console.log("build: NOT CHECKED, UI/node_modules is missing (cd UI && npm install)");
}

// ---- the pages drawn from the real component trees, and the three suites
function run(script, extra = []) {
    const result = spawnSync(process.execPath, [join(root, "Tests", script), root, ...extra], {
        cwd: root, encoding: "utf8", env: { ...process.env, CDLC_UI_FILE: bundle }, maxBuffer: 64 * 1024 * 1024, timeout: 15 * 60 * 1000,
    });
    return { code: result.status, out: (result.stdout || "") + (result.stderr || "") };
}

const work = mkdtempSync(join(tmpdir(), "cdlc-ui-check-"));
try {
    const pages = join(work, "pages");
    const drawn = run("preview-dock.mjs", [pages]);
    check(drawn.code === 0, `preview-dock exited ${drawn.code}: ${drawn.out.split("\n").slice(-3).join(" | ")}`);
    const made = existsSync(pages) ? readdirSync(pages).filter(f => f.endsWith(".html")).length : 0;
    check(made > 0, "preview-dock made no page");
    console.log(`pages: ${made} drawn from the real component trees`);

    for (const script of ["ui-smoke.mjs", "ui-edit-sequences.mjs", "ui-language.mjs"]) {
        const result = run(script);
        const line = result.out.split("\n").map(l => l.trim()).filter(l => /checks?,? |checks passed/.test(l)).pop() || result.out.split("\n").slice(-3).join(" | ");
        check(result.code === 0, `${script} exited ${result.code}: ${line}`);
        console.log(`${script}: "${line}"`);
    }
} finally {
    rmSync(work, { recursive: true, force: true });
}

console.log(failures === 0 ? "the bundle passes every check here" : `${failures} difference(s)`);
process.exit(failures === 0 ? 0 : 1);
