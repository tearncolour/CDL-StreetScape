// Before a build: takes out of the output folder the files this build makes (the folder may be shared with other files: nothing else is touched).
const fs = require("fs");
const path = require("path");
const MOD = require("../../mod.json");
const out = path.resolve(__dirname, "..", process.env.CDLC_UI_OUT || "build");
for (const name of [`${MOD.id}.mjs`, `${MOD.id}.css`]) {
  fs.rmSync(path.join(out, name), { force: true });
}
