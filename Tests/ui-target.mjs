// Which UI files the offline UI tests take hold of (ui-smoke, ui-edit-sequences, ui-language, preview-dock).
//   module: the script the game would load: the bundle UI/build/CDLC.TS.mjs (npm run build in UI/), or the file named by the environment variable CDLC_UI_FILE (a path to a .mjs,
//           for instance the bundle the C# build made in obj/<configuration>/ui). Without either the test stops: there is no other copy of the panel's script.
//   style:  the stylesheet that belongs to it (the same folder and name with .css).
//   source: the readable script for the checks that read the code as text (a minified bundle has no comments and no names): UI/src/ts-ui.js, which the bundle is built from.
import { existsSync } from "node:fs";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

export function uiTarget(root) {
    const module = process.env.CDLC_UI_FILE ? resolve(process.env.CDLC_UI_FILE) : join(root, "UI", "build", "CDLC.TS.mjs");
    const style = module.replace(/\.mjs$/, ".css");
    for (const file of [module, style]) {
        if (!existsSync(file)) {
            console.log(`missing: ${file}${process.env.CDLC_UI_FILE ? "" : " (run npm run build in UI/)"}`);
            process.exit(2);
        }
    }

    return {
        module,
        style,
        source: join(root, "UI", "src", "ts-ui.js"),
        url: (suffix = "") => pathToFileURL(module).href + suffix,
    };
}
