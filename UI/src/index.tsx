import type { ModRegistrar } from "cs2/modding";
import extension from "./ts-ui";
import "./CDLC.TS.css";

// The game calls the default export with its module registry and loads CDLC.TS.css beside this file when hasCSS is exported (tools/css-presence.js adds it).
const register: ModRegistrar = extension;

export default register;

// What the offline tests (Tests/ui-*.mjs) take hold of: the names the hand-written module exported before this build; the game ignores them.
export * from "./ts-ui";
