// Offline checks of CDLC.TS.mjs (0.12.3: the panels are built of the game's own components). The script is loaded with stubbed game globals and rendered the way
// React renders it (component by component, each one's hooks counted on its own), twice over: with the game's internal modules found, and without them (what a
// game update would leave). Checked: the search, the kinds and the pages; the catalogue panel, the editor and the restore dialog in every state, and every trigger
// they send; that every component calls the same hooks on every render (React error #310 otherwise); that the panels are made of the game's components (every
// cs2/ui component is one the game's published types export, every module looked up through cs2/modding is in the installed game's UI bundle with that export,
// every icon is a packaged file or one of the game's files: when the game is installed here); the C# <-> JS contract (bindings, triggers, JSON fields), the texts in both
// languages, and the stylesheet (a rule for every class, none of what the game's UI engine does not have, the sizes of the grid).
// Usage: node Tests/ui-smoke.mjs <repo root>
import { readFileSync, existsSync } from "node:fs";
import { join } from "node:path";
import { uiTarget } from "./ui-target.mjs";

const root = process.argv[2] || process.cwd();
const target = uiTarget(root);
let failures = 0;
let checks = 0;
const check = (ok, message) => {
    checks++;
    if (!ok) {
        failures++;
        console.log("  FAIL " + message);
    }
};

const languages = {
    "en-US": JSON.parse(readFileSync(join(root, "lang", "en-US.json"), "utf8")),
    "zh-HANS": JSON.parse(readFileSync(join(root, "lang", "zh-HANS.json"), "utf8"))
};

// ------------------------------------------------------------------ the stubs

const triggers = [];
const effects = [];            // what useEffect was given, in order (a test runs one and its clean-up by hand)
const values = {};
const used = new Set();        // cs2/ui components rendered
const looked = [];             // [path, name] looked up through cs2/modding
let hooks = 0;
const hookCounts = new Map();  // component name -> the set of hook counts seen

const flat = children => children.flat(Infinity).filter(c => c !== null && c !== undefined && c !== false);
const element = (type, props, ...children) => {
    const all = Object.assign({}, props || {});
    if (children.length > 0) {
        all.children = flat(children);
    }
    return { type, props: all };
};

function native(name) {
    const component = props => {
        used.add(name);
        return { type: "ui." + name, props, host: true };
    };
    Object.defineProperty(component, "name", { value: "native " + name });
    return component;
}

const uiNames = ["Panel", "PanelSection", "PanelSectionRow", "PanelFoldout", "Button", "Tooltip", "Scrollable", "Dropdown", "DropdownToggle", "DropdownItem", "ConfirmationDialog"];
function makeUi(full) {
    const ui = {};
    for (const n of uiNames) {
        if (full || !(n.startsWith("Dropdown") || n === "ConfirmationDialog")) {
            ui[n] = native(n);
        }
    }
    return ui;
}

const gameClassesStub = { item: "item_x", itemInner: "inner_x", thumbnail: "thumb_x", row: "row_x", pageIndicator: "pi_x", toggleStates: "toggle_x", dropdownToggle: "dt_x" };

function setGlobals(found, full) {
    globalThis.window = {
        React: {
            createElement: element,
            Fragment: "Fragment",
            useState: initial => { hooks++; return [initial, () => {}]; },
            useEffect: fn => { hooks++; effects.push(fn); },
            useRef: initial => { hooks++; return { current: initial }; },
            Component: class { constructor(props) { this.props = props; } }
        },
        "cs2/api": {
            bindValue: (group, name, fallback) => ({ group, name, fallback }),
            useValue: binding => { hooks++; return binding.name in values ? values[binding.name] : binding.fallback; },
            trigger: (group, name, ...args) => triggers.push([group, name, ...args])
        },
        "cs2/l10n": { useLocalization: () => { hooks++; return { translate: (key, fallback) => fallback }; } },
        "cs2/ui": makeUi(full),
        "cs2/modding": {
            getModule: (path, name) => {
                looked.push([path, name]);
                if (!found) {
                    return undefined;
                }
                return name === "classes" ? gameClassesStub : native("game:" + name);
            }
        },
        innerHeight: 1080
    };
    globalThis.document = { body: {}, activeElement: null, documentElement: {} };
    globalThis.getComputedStyle = () => ({ fontSize: "1px" });
}

// Renders an element the way React would: a function component is called with its props and its hooks are counted on their own; a class component is made and
// rendered; the props that carry elements (children, header, footer, left, right, tooltip, content) are rendered too.
function render(node) {
    if (Array.isArray(node)) {
        return node.map(render);
    }
    if (!node || typeof node !== "object") {
        return node;
    }
    if (typeof node.type === "function") {
        if (node.type.prototype && typeof node.type.prototype.render === "function") {
            const instance = new node.type(node.props);
            instance.state = instance.state || {};
            return render(instance.render());
        }
        const before = hooks;
        const out = node.type(node.props);
        if (!(out && out.host)) {
            const name = node.type.name || "anonymous";
            if (!hookCounts.has(name)) {
                hookCounts.set(name, new Set());
            }
            hookCounts.get(name).add(hooks - before);
        }
        return render(out);
    }
    const props = Object.assign({}, node.props);
    for (const key of ["children", "header", "footer", "left", "right", "tooltip", "content"]) {
        if (props[key] !== undefined) {
            props[key] = render(props[key]);
        }
    }
    return { type: node.type, props, children: props.children === undefined ? [] : [].concat(props.children) };
}

function walk(node, visit) {
    if (Array.isArray(node)) {
        node.forEach(n => walk(n, visit));
        return;
    }
    if (!node || typeof node !== "object") {
        return;
    }
    visit(node);
    walk(node.children, visit);
    for (const key of ["header", "footer", "left", "right", "tooltip", "content"]) {
        if (node.props && node.props[key] !== undefined) {
            walk(node.props[key], visit);
        }
    }
}

function find(node, test) {
    const found = [];
    walk(node, n => { if (n.props && test(n)) found.push(n); });
    return found;
}

const byType = (node, type) => find(node, n => n.type === type);
const withClass = (node, name) => find(node, n => typeof n.props.className === "string" && n.props.className.split(/\s+/).includes(name));
const textOf = node => {
    let text = "";
    walk(node, n => { for (const c of n.children || []) if (typeof c === "string" || typeof c === "number") text += c + " "; });
    walk(node, n => {
        for (const key of ["left", "right", "header", "label"]) {
            const v = n.props && n.props[key];
            if (typeof v === "string" || typeof v === "number") text += v + " ";
        }
    });
    return text;
};
// The game's focus rule: a PanelSectionRow (also with disableFocus: it is a passive div with the same controller) and a Tooltip (a FocusBoundary) host ONE focusable child. Several controls side by side
// go under the game's AutoNavigationScope, which counts as one child and takes any number; a PanelSection, a Panel and a PanelFoldout have one inside. A focusable counts once (a button; a dropdown with
// its toggle and items), a scope or a section once if there is any in it.
const focusable = n => n.type === "ui.Button" || n.type === "ui.Dropdown" || n.type === "ui.game:ToolButton" || n.type === "ui.game:InfoButton" || n.type === "ui.game:Checkbox";
const scopes = new Set(["ui.game:AutoNavigationScope", "ui.PanelSection", "ui.Panel", "ui.PanelFoldout"]);
function leaves(node) {
    if (Array.isArray(node)) {
        return node.reduce((sum, n) => sum + leaves(n), 0);
    }
    if (!node || typeof node !== "object" || !node.props) {
        return 0;
    }
    if (focusable(node)) {
        return 1;
    }
    let inside = leaves(node.children);
    for (const key of ["left", "right", "header"]) {
        inside += leaves(node.props[key]);
    }
    return scopes.has(node.type) ? Math.min(1, inside) : inside;
}
const crowded = new Map();
let focusLabel = "";
function crowdedRows(tree) {
    // The rule is checked where the game's modules are found (the scope is theirs): without them the rows are as the game's rows are, and the game logs the errors.
    if (focusLabel !== "[game modules] ") {
        return;
    }
    for (const host of find(tree, n => n.type === "ui.PanelSectionRow" || n.type === "ui.Tooltip")) {
        const inside = leaves(host.children) + leaves(host.props.left) + leaves(host.props.right);
        if (inside > 1) {
            // The same row drawn again is one finding: the key's number part (the editor's id) is left out.
            const name = host.type.slice(3) + " " + String(host.props.key || host.props.tooltip || host.props.left || "").replace(/^\d+:/, "");
            crowded.set(focusLabel + name, (crowded.get(focusLabel + name) || 0) + 1);
            crowdedSizes.set(focusLabel + name, inside);
        }
    }
}
const crowdedSizes = new Map();
const draw = component => {
    const tree = render(element(component, null));
    crowdedRows(tree);
    return tree;
};
const take = () => { const all = triggers.slice(); triggers.length = 0; return JSON.stringify(all.map(x => x.slice(1))); };
const wideAction = (tree, found, text) => found
    ? byType(tree, "ui.game:InfoButton").find(b => String(b.props.label).includes(text))
    : byType(find(tree, n => n.props.className === "cdlts-wide" && textOf(n).includes(text))[0], "ui.Button")[0];

// ------------------------------------------------------------------ the game's published types and its UI bundle (when the game is installed here)

const gameRoot = process.env.CSII_INSTALLATIONPATH || "c:/Program Files (x86)/Steam/steamapps/common/Cities Skylines II";
const typesPath = join(gameRoot, "Cities2_Data", "Content", "Game", ".ModdingToolchain", "npx-create-csii-ui-mod", "template", "types", "ui.d.ts");
const uiRoot = join(gameRoot, "Cities2_Data", "Content", "Game", "UI");
let published = null;
if (existsSync(typesPath)) {
    const d = readFileSync(typesPath, "utf8");
    published = new Set();
    for (const m of d.matchAll(/export (?:export )?(?:const|function|class) (\w+)/g)) published.add(m[1].replace(/\$1$/, ""));
    for (const m of d.matchAll(/(\w+(?:\$1)?) as (\w+)/g)) published.add(m[2]);
} else {
    console.log("note: the game's published types are not here; the native component list is not checked");
}
const bundle = existsSync(join(uiRoot, "index.js")) ? readFileSync(join(uiRoot, "index.js"), "utf8") : null;
const gameCss = existsSync(join(uiRoot, "index.css")) ? readFileSync(join(uiRoot, "index.css"), "utf8") : null;
if (!bundle) {
    console.log("note: the game's UI bundle is not here; the internal modules and icons are not checked");
}

// ------------------------------------------------------------------ the data the C# side would send

const thumbUrl = id => `coui://cdlcts/s-${id}-12345678.png`;
const catalogue = Array.from({ length: 40 }, (_, i) => ({
    id: "t" + i, code: "禁" + (i + 1), zh: "牌" + i, en: "Sign " + i, kw: [], py: "p", grp: i < 30 ? "prohibition" : "warning", thumb: i % 5 === 0 ? "" : thumbUrl(i)
}));
const supportsJson = [
    { id: "pole.single", zh: "单柱式", en: "Single post", fam: "post", thumb: "coui://cdlcts/p-pole.single-1.png", mid: true },
    { id: "pole.double", zh: "双柱式", en: "Double post", fam: "post", thumb: "", mid: true },
    { id: "cantilever.single", zh: "悬臂式", en: "Cantilever", fam: "cantilever", thumb: "coui://cdlcts/p-cantilever.single-1.png" },
    { id: "cantilever.octa", zh: "单悬臂式（八角锥杆）", en: "Octagonal", fam: "cantilever", thumb: "" },
    { id: "gantry.portal", zh: "门架式", en: "Gantry", fam: "gantry", thumb: "" },
    { id: "gantry.space", zh: "门架（空间桁架，前后双层）", en: "Gantry, space truss", fam: "gantry", thumb: "coui://cdlcts/p-gantry.space-1.png" },
    { id: "gantry.limit", zh: "限高架（黄黑）", en: "Height-limit frame (yellow-black)", fam: "gantry", thumb: "coui://cdlcts/p-gantry.limit-1.png" }
];
const editorJson = {
    id: 7, mode: "item", code: "路42", title: "出口预告标志", preview: "coui://cdlcts/edit-3.png", pw: 676, ph: 768, plan: "pole.double",
    fields: [
        { k: "road", l: "道路", t: "choice", v: "g", max: 0, ch: [{ v: "g", l: "国道" }, { v: "s", l: "省道" }, { v: "name", l: "无编号" }] },
        { k: "code", l: "编号", t: "text", v: "G204", max: 5 },
        { k: "lane", l: "直出车道", t: "flag", v: "0", max: 0 },
        { k: "free", l: "自由文本", t: "text", v: "😀👨‍👩", max: 0 }
    ]
};
const boardEditor = Object.assign({}, editorJson, {
    fields: [{ k: "dir", l: "转弯方向", t: "choice", v: "l", max: 0, ch: [{ v: "l", l: "向左" }, { v: "r", l: "向右" }] }],
    boards: [{ m: 0, code: "示26", title: "小型客车车道", lane: 0, x: 6 }, { m: 1, code: "禁5", title: "禁止驶入", lane: 2, x: 9 }],
    sel: 0, lanes: [9, 6, 3], laneState: "ok", lane: 0, warn: []
});

const customJson = { add: "coui://cdlcts/add-r3.png", folder: "C:/Users/p/ModsData/CDLC.TS/Custom", files: [{ n: "公司标志.png", w: 800, h: 600, kb: 120 }, { n: "b.png", w: 0, h: 0, kb: 2 }], note: "", bad: false };

function reset() {
    for (const k of Object.keys(values)) delete values[k];
    values.Catalog = JSON.stringify(catalogue);
    values.Supports = JSON.stringify(supportsJson);
    values.Selected = "t3";
    values.Support = "pole.double";
    values.Mode = 0;
    values.ToolActive = true;
    values.Hint = JSON.stringify({ t: "单击放置。", bad: false });
    values.Custom = JSON.stringify(customJson);
    triggers.length = 0;
}

// ------------------------------------------------------------------ run: with the game's modules, then without them

for (const [found, full] of [[true, true], [false, false]]) {
    const label = found ? "[game modules] " : "[fallback] ";
    focusLabel = label;
    setGlobals(found, full);
    looked.length = 0;
    const module = await import(target.url(found ? "?found" : "?fallback"));
    const { ui, setUi, perPage, columns, MainPanel, EditorPanel, RestoreDialog, KeysPanel, keyParts, capsWidth, GameWindow, TsButton, Guard, Dock, SettingsPanel, FootprintPanel, bytesText, countText, ActionPill, back, onEscape, stripEntered, panelLeft, stripSlots } = module;
    reset();

    // ---- the dock: the strip of the tabs, the strip of actions, and the panel that slides out beside them (0.12.61)
    const fresh = { open: true, page: 0, kind: "all", query: "", editorTab: "sign", confirming: false, flyout: false, settings: false, showList: false, seenEditor: "", editor: null, top: 0, leftAt: 0, importing: false };
    setUi(fresh);
    let dock = draw(Dock);
    check(dock.type === "div" && dock.props.className === "cdlts-dock", label + "the dock");
    check(byType(dock, "ui.Panel").length === 0 && withClass(dock, "cdlts-fly").length === 1 && !withClass(dock, "cdlts-fly")[0].props.className.includes("cdlts-fly-open"), label + "opened, there are the two strips and the panel is not out");
    const tabButtons = tr => byType(withClass(tr, "cdlts-strip")[0], "ui.Button");
    const tabTips = tr => byType(withClass(tr, "cdlts-strip")[0], "ui.Tooltip");
    let kindButtons = tabButtons(dock);
    check(kindButtons.length === 5, label + "the strip: all, each of the two kinds, the player's own signs and the settings: " + kindButtons.length);
    check(kindButtons[0].props.src === module.icons.all && kindButtons[3].props.src === "coui://cdlcts-icons/custom.png" && kindButtons[4].props.src === module.icons.settings, label + "all, the custom tab and the settings have their icons: " + kindButtons.map(b => b.props.src).join(" "));
    check(kindButtons[1].props.src === "coui://cdlcts-icons/prohibition.png" && kindButtons[2].props.src === "coui://cdlcts-icons/warning.png", label + "categories use dedicated art: " + kindButtons[1].props.src);
    check(tabTips(dock)[1].props.tooltip === "prohibition" && tabTips(dock)[4].props.tooltip === "Save data and display", label + "each with its name in the tooltip");
    check(kindButtons.every(b => !b.props.selected), label + "no tab is on while the panel is in");
    if (found) {
        check(kindButtons.every(b => b.props.theme && b.props.className === "toggle_x") && withClass(withClass(dock, "cdlts-strip")[0], "item_x").length === 5, label + "the tabs are round buttons of the game's right-hand column");
    }
    kindButtons[2].props.onSelect();
    check(ui.kind === "warning" && ui.page === 0 && ui.flyout === true && ui.settings === false, label + "a tab: its kind filters and the panel slides out");
    dock = draw(Dock);
    const panel = byType(dock, "ui.Panel")[0];
    check(!!panel && panel.props.className === "cdlts-flypanel" && typeof panel.props.onClose === "function" && withClass(dock, "cdlts-fly")[0].props.className.includes("cdlts-fly-open") && !withClass(dock, "cdlts-fly")[0].props.className.includes("cdlts-fly-wide") && panel.props.style.width === "470rem", label + "the panel is the game's, out, with a close, and the list's window is the narrow one");
    check(textOf(withClass(panel, "cdlts-fly-title")[0]).includes("warning") && withClass(panel, "cdlts-back").length === 0 && byType(panel, "ui.game:ToolButton").length === 0, label + "headed with the tab's name and no back button on the entries");
    check(tabButtons(dock).filter(b => b.props.selected).length === 1 && tabButtons(dock)[2].props.selected === true, label + "the tab that is out is on");
    check(withClass(dock, "cdlts-tile").length === 10, label + "ten warning signs: " + withClass(dock, "cdlts-tile").length);
    tabButtons(dock)[2].props.onSelect();
    check(ui.flyout === false, label + "the same tab again folds the panel away");
    tabButtons(draw(Dock))[4].props.onSelect();
    check(ui.settings === true && ui.flyout === true, label + "the settings tab slides the settings out");
    dock = draw(Dock);
    check(byType(dock, "ui.PanelFoldout").length === 3 && tabButtons(dock)[4].props.selected === true && textOf(withClass(dock, "cdlts-fly-title")[0]).includes("Save data and display"), label + "the save data, the memory and the display settings, in the panel the settings tab opens");
    tabButtons(dock)[0].props.onSelect();
    check(ui.settings === false && ui.kind === "all" && ui.flyout === true, label + "a kind tab after it is the entries again");
    setUi(fresh);

    // the pointer coming back from the panel to the strip folds the panel away
    setUi({ flyout: true, leftAt: 0 });
    const leaveAt = x => ({ currentTarget: { getBoundingClientRect: () => ({ left: 100 }) }, clientX: x });
    panelLeft(leaveAt(96));
    check(ui.leftAt > 0, label + "the pointer leaving the panel to its left is noted");
    stripEntered();
    check(ui.flyout === false && ui.leftAt === 0, label + "and the strip's mouse enter folds the panel away");
    setUi({ flyout: true, leftAt: 0 });
    panelLeft(leaveAt(700));
    check(ui.leftAt === 0, label + "leaving to the right is not a way back to the strip");
    stripEntered();
    check(ui.flyout === true, label + "the strip entered from elsewhere does not fold it");
    setUi({ flyout: true, leftAt: Date.now() - 5000 });
    stripEntered();
    check(ui.flyout === true && ui.leftAt === 0, label + "a way back that was long ago does not either");
    panelLeft({});
    check(ui.leftAt === 0, label + "an event without a box is no way back");
    setUi(fresh);

    // more tabs than the strip shows: the wheel brings the others
    {
        const kinds = ["prohibition", "warning", "mandatory", "guide", "auxiliary", "notice", "tourist", "placename", "wayfinding", "variable", "brt", "toll"];
        const many = kinds.map((k, i) => ({ id: "k" + i, code: "x" + i, zh: "牌" + i, en: "S" + i, kw: [], py: "", grp: k, thumb: "" }));
        const saved = values.Catalog;
        values.Catalog = JSON.stringify(many);
        setUi({ top: 0 });
        let strip = withClass(draw(Dock), "cdlts-strip")[0];
        check(tabButtons(draw(Dock)).length === stripSlots && stripSlots === 8, label + "eight tabs show of the fifteen");
        check(withClass(strip, "cdlts-strip-bar").length === 0 && !find(strip, n => n.props && n.props.style && n.props.style.height !== undefined).length, label + "no scroll bar on the strip, the owner took it off");
        strip.props.onWheel({ deltaY: 120 });
        check(ui.top === 1 && tabButtons(draw(Dock))[0].props.src === "coui://cdlcts-icons/prohibition.png", label + "the wheel down brings the next tab in: the first one in view is " + tabButtons(draw(Dock))[0].props.src);
        strip.props.onWheel({ deltaY: 120 });
        strip.props.onWheel({ deltaY: 120 });
        for (let i = 0; i < 20; i++) strip.props.onWheel({ deltaY: 120 });
        check(ui.top === 15 - stripSlots && tabButtons(draw(Dock)).length === 8 && tabButtons(draw(Dock))[7].props.src === module.icons.settings, label + "it stops with the settings at the bottom: " + ui.top);
        for (let i = 0; i < 30; i++) strip.props.onWheel({ deltaY: -120 });
        check(ui.top === 0, label + "and back to the top");
        strip.props.onWheel({ deltaY: 0 });
        strip.props.onWheel(undefined);
        check(ui.top === 0, label + "a wheel without a delta does nothing");
        setUi({ top: 99 });
        check(tabButtons(draw(Dock)).length === 8 && tabButtons(draw(Dock))[7].props.src === module.icons.settings, label + "a top past the end is the last view");

        // the arrows at the strip's ends (0.12.94): faint where no tab is out of view that way, bright where there are, a click steps a tab
        const stripNow = () => withClass(draw(Dock), "cdlts-strip")[0];
        const arrowsNow = () => withClass(stripNow(), "cdlts-more");
        const litOf = arrow => String(arrow.props.className).split(" ").includes("cdlts-more-lit");
        setUi({ top: 0 });
        let arrows = arrowsNow();
        check(arrows.length === 2 && String(stripNow().props.className).split(" ").includes("cdlts-strip-more"), label + "with more tabs than the strip shows it has an arrow at each end and the margins for them");
        check(String(arrows[0].props.className).includes("cdlts-more-top") && String(arrows[1].props.className).includes("cdlts-more-foot"), label + "the first is at the top, the second at the foot");
        check(!litOf(arrows[0]) && litOf(arrows[1]), label + "at the top only the arrow down is bright");
        arrows[1].props.onClick();
        arrows = arrowsNow();
        check(ui.top === 1 && litOf(arrows[0]) && litOf(arrows[1]), label + "a click on it steps a tab, and both are bright in between");
        arrows[0].props.onClick();
        check(ui.top === 0, label + "a click on the one at the top steps back");
        setUi({ top: 15 - stripSlots });
        arrows = arrowsNow();
        check(litOf(arrows[0]) && !litOf(arrows[1]), label + "at the foot only the arrow up is bright");
        arrows[1].props.onClick();
        check(ui.top === 15 - stripSlots, label + "a click on a faint one steps nowhere past the end");
        setUi({ top: 0, flyout: true });
        const tall = draw(MainPanel).props.style;
        check(tall.minHeight === Math.min(314, parseInt(tall.maxHeight, 10)) + "rem", label + "the list is as tall as the strip of the fifteen tabs (353.6 rem) less its header: " + tall.minHeight + " of at most " + tall.maxHeight);
        values.Catalog = saved;
        setUi({ top: 0, flyout: true });
        check(arrowsNow().length === 0 && !String(stripNow().props.className).includes("cdlts-strip-more"), label + "with few tabs there are no arrows and no margins");
        const short = draw(MainPanel).props.style;
        check(short.minHeight === Math.min(180, parseInt(short.maxHeight, 10)) + "rem" && parseInt(short.minHeight, 10) < parseInt(tall.minHeight, 10), label + "and a list beside a short strip (the 5 tabs of the plain catalogue, 219.5 rem) is as short: " + short.minHeight);
        setUi(fresh);
    }

    // the search box and the kinds of the entries (the panel's list)
    setUi({ flyout: true });
    let tree = draw(MainPanel);
    check(tree.type === "ui.Scrollable" && byType(tree, "ui.Scrollable").length === 1, label + "the entries scroll in the game's Scrollable");
    const search = found ? byType(tree, "ui.game:TextInput") : byType(tree, "input");
    check(search.length === 1 && (found ? search[0].props.value === "" : search[0].props.defaultValue === ""), label + "the search box is " + (found ? "the game's text input" : "a plain input"));
    search[0].props.onChange({ target: { value: "牌7" } });
    check(ui.query === "牌7" && ui.page === 0, label + "typing searches");
    tree = draw(MainPanel);
    check(withClass(tree, "cdlts-tile").length === 1, label + "the query narrows the grid to the one sign");
    setUi({ query: "没有这个" });
    tree = draw(MainPanel);
    check(withClass(tree, "cdlts-tile").length === 0 && textOf(withClass(tree, "cdlts-grid")[0]).includes("Nothing matches"), label + "a query that finds nothing says so");
    setUi({ query: "" });
    setUi({ kind: "warning" });
    tree = draw(MainPanel);
    check(withClass(tree, "cdlts-tile").length === 10, label + "ten warning signs: " + withClass(tree, "cdlts-tile").length);
    setUi({ kind: "no-such-kind" });
    tree = draw(MainPanel);
    check(withClass(tree, "cdlts-tile").length === perPage, label + "a kind the catalogue does not have is all");
    setUi({ kind: "all" });

    // the grid and its pages
    tree = draw(MainPanel);
    const tiles = withClass(tree, "cdlts-tile");
    check(perPage === 18 && columns === 6 && tiles.length === perPage, label + "a page holds three rows of six: " + tiles.length);
    check(withClass(tree, "cdlts-grid-row").length === 3 && withClass(tree, "cdlts-grid-row").every(r => withClass(r, "cdlts-tile").length === 6), label + "in rows of six");
    check(tiles.every(n => n.type === "ui.Button" && n.props.theme && n.props.theme.button === (found ? "item_x" : "cdlts-tile-plain")), label + "a tile is the game's button with " + (found ? "the asset menu's class" : "a class of its own"));
    check(find(withClass(tree, "cdlts-grid")[0], n => n.type === "img" && /cdlts-tile-img/.test(n.props.className)).length === 14 && withClass(withClass(tree, "cdlts-grid")[0], "cdlts-tile-code").length === 4, label + "fourteen tiles with a thumbnail, four (every fifth sign has none yet) with their code instead");
    check(byType(tree, "ui.Tooltip").some(n => textOf({ props: {}, children: [n.props.tooltip] }).includes("禁4") && textOf({ props: {}, children: [n.props.tooltip] }).includes("Sign 3")), label + "a tile's tooltip names its sign");
    check(tiles.filter(n => n.props.selected).length === 1 && tiles[3].props.selected, label + "the chosen sign's tile is on while the tool places");
    tiles[2].props.onSelect();
    check(take() === JSON.stringify([["Select", "t2"]]), label + "a tile chooses its sign");
    values.ToolActive = false;
    check(withClass(draw(MainPanel), "cdlts-tile").every(n => !n.props.selected), label + "no tile is on when the tool is not running");
    values.ToolActive = true;
    const marks = withClass(tree, "cdlts-page");
    check(marks.length === 3 && marks[0].props.className.includes("selected") && (!found || marks[0].props.className.includes("pi_x")), label + "three page marks, the first on" + (found ? ", with the game's class" : ""));
    marks[2].props.onClick();
    check(ui.page === 2, label + "a mark turns to its page");
    tree = draw(MainPanel);
    check(withClass(tree, "cdlts-tile").length === 4, label + "the last page holds the rest");
    const grid = withClass(tree, "cdlts-grid")[0];
    grid.props.onWheel({ deltaY: 120 });
    check(ui.page === 2, label + "the wheel at the last page stays there");
    grid.props.onWheel({ deltaY: -120 });
    check(ui.page === 1, label + "the wheel up turns back");
    grid.props.onWheel({ deltaY: 0 });
    grid.props.onWheel(undefined);
    check(ui.page === 1, label + "a wheel without a delta does nothing");
    setUi({ page: 9 });
    check(withClass(draw(MainPanel), "cdlts-tile").length === 4, label + "a page past the end is the last");
    setUi({ page: 0 });

    // 0.12.29: the player's own signs: their tab lists the saved ones and the add tile; the tile opens the import of the folder's files
    {
        const saved = { id: "custom.c1", code: "自定义", zh: "公司标志", en: "公司标志", kw: ["自定义"], py: "", grp: "custom", thumb: "coui://cdlcts/s-custom.c1-1.png" };
        values.Catalog = JSON.stringify(catalogue.concat([saved]));
        setUi({ kind: "custom", page: 0, importing: false });
        tree = draw(MainPanel);
        const own = withClass(tree, "cdlts-tile");
        check(own.length === 2, label + "the custom tab: the saved sign and the add tile: " + own.length);
        check(byType(tree, "ui.Tooltip").some(n => textOf({ props: {}, children: [n.props.tooltip] }).includes("Import a PNG picture")), label + "the add tile says what it does");
        check(find(own[1], n => n.type === "img" && n.props.src === customJson.add).length === 1, label + "the add tile is its picture (a dashed square with a plus)");
        take();
        own[1].props.onSelect();
        check(ui.importing === true && take() === JSON.stringify([["ListCustomFiles"]]), label + "the add tile opens the import and lists the folder");
        tree = draw(MainPanel);
        check(textOf(tree).includes(customJson.folder) && textOf(tree).includes("公司标志.png") && textOf(tree).includes("800×600") && textOf(tree).includes("? "), label + "the folder and its files with their sizes (unknown: ?)");
        check(withClass(draw(MainPanel), "cdlts-tile")[1].props.selected === true, label + "the add tile is on while the import is open");
        const importButtons = find(tree, n => n.type === "ui.Button" && textOf(n).includes("Import") && !textOf(n).includes("PNG"));
        check(importButtons.length === 2, label + "an import button for each file: " + importButtons.length);
        importButtons[0].props.onSelect();
        check(take() === JSON.stringify([["ImportCustom", "公司标志.png"]]) && ui.importing === false, label + "importing a file names it, and the import closes");
        setUi({ importing: true });
        tree = draw(MainPanel);
        wideAction(tree, found, "Open the folder").props[found ? "onSelect" : "onSelect"]();
        wideAction(tree, found, "List again").props.onSelect();
        check(take() === JSON.stringify([["OpenCustomFolder"], ["ListCustomFiles"]]), label + "the folder is opened and listed again");
        values.Custom = JSON.stringify(Object.assign({}, customJson, { files: [], note: "b.png 不是 PNG 图片。", bad: true }));
        tree = draw(MainPanel);
        check(textOf(tree).includes("There is no PNG picture") && withClass(tree, "cdlts-bad").some(n => textOf(n).includes("不是 PNG")), label + "an empty folder says so; a refusal is shown as one");
        wideAction(tree, found, "Close the import").props.onSelect();
        check(ui.importing === false && withClass(draw(MainPanel), "cdlts-tile").length === 2, label + "the import closes");
        setUi({ kind: "all", importing: false });
        check(withClass(draw(MainPanel), "cdlts-tile").every(n => !String(textOf(n)).includes("+")), label + "the add tile is only in the custom tab");
        values.Catalog = JSON.stringify(catalogue);
        values.Custom = JSON.stringify(customJson);
    }

    // the supports: on the support page of a sign about to be placed (0.12.61: they were under the grid)
    const draft = Object.assign({}, editorJson, { mode: "draft" });
    values.Editor = JSON.stringify(draft);
    setUi({ editorTab: "support", flyout: true, showList: false });
    tree = draw(EditorPanel);
    check(!!byType(tree, "ui.Scrollable")[0] && withClass(tree, "cdlts-tabs").length + withClass(tree, "cdlts-tabs-flat").length === 1, label + "a sign about to be placed: the tabs and the page");
    // 0.12.58: the supports by form: the automatic one and the three forms of the fixture in a row, and under it the variants of the form chosen (the double post: the two posts)
    const supportTiles = withClass(tree, "cdlts-support");
    check(supportTiles.length === 6 && supportTiles.filter(n => n.props.selected).length === 2 && supportTiles[1].props.selected && supportTiles[5].props.selected, label + "the automatic support, three forms and the two variants of the form chosen: the form and the variant on");
    check(byType(tree, "ui.PanelSectionRow").some(n => n.props.right === "Double post"), label + "the chosen support is named");
    const rows = withClass(tree, "cdlts-supports");
    check(rows.length === 2 && withClass(rows[0], "cdlts-support").length === 4 && withClass(rows[1], "cdlts-support").length === 2, label + "two rows: the forms, and the variants of the chosen form");
    supportTiles[2].props.onSelect();
    supportTiles[0].props.onSelect();
    check(take() === JSON.stringify([["SetSupport", "cantilever.single"], ["SetSupport", ""]]), label + "a form's tile chooses its first variant, the first tile is automatic");
    supportTiles[4].props.onSelect();
    check(take() === JSON.stringify([["SetSupport", "pole.single"]]), label + "a variant's tile chooses it");
    values.Support = "cantilever.octa";
    tree = draw(EditorPanel);
    check(withClass(tree, "cdlts-support").length === 6 && withClass(tree, "cdlts-supports").length === 2, label + "the cantilevers' form: its two variants under the row");
    withClass(tree, "cdlts-support")[1].props.onSelect();
    withClass(tree, "cdlts-support")[2].props.onSelect();
    check(take() === JSON.stringify([["SetSupport", "pole.double"], ["SetSupport", "cantilever.octa"]]), label + "a form comes back to the variant chosen last in it (the double post; the octagonal cantilever)");
    values.Support = "gantry.portal";
    tree = draw(EditorPanel);
    check(withClass(tree, "cdlts-support").length === 7 && withClass(tree, "cdlts-supports").length === 2, label + "the gantry form: its three variants under the row (0.12.78: the portal gantry, the space truss, the height-limit frame)");
    check(withClass(tree, "cdlts-support")[4].props.selected && !withClass(tree, "cdlts-support")[5].props.selected && !withClass(tree, "cdlts-support")[6].props.selected, label + "the portal gantry is the one on");
    withClass(tree, "cdlts-support")[6].props.onSelect();
    withClass(tree, "cdlts-support")[5].props.onSelect();
    check(take() === JSON.stringify([["SetSupport", "gantry.limit"], ["SetSupport", "gantry.space"]]), label + "a variant's tile of the gantry form chooses it");
    values.Support = "gantry.limit";
    tree = draw(EditorPanel);
    check(withClass(tree, "cdlts-support")[6].props.selected && withClass(tree, "cdlts-support").filter(n => n.props.selected).length === 2, label + "the height-limit frame on: its form and itself");
    check(byType(tree, "ui.PanelSectionRow").some(n => n.props.right === "Height-limit frame (yellow-black)" || n.props.right === "限高架（黄黑）"), label + "the chosen support is named");
    withClass(tree, "cdlts-support")[1].props.onSelect();
    values.Support = "pole.double";
    tree = draw(EditorPanel);
    withClass(tree, "cdlts-support")[3].props.onSelect();
    check(take() === JSON.stringify([["SetSupport", "pole.double"], ["SetSupport", "gantry.limit"]]), label + "the gantry form comes back to the variant chosen last in it (the height-limit frame)");
    // 0.12.74: the distance from the kerb on the support page (the owner has no numpad): the value in the head row, the game's slider committed on release (two buttons where it is not found), and a way back to the default
    values.Margin = -0.6;
    tree = draw(EditorPanel);
    const kerbHead = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "Distance from the kerb");
    check(!!kerbHead && kerbHead.props.right === "-0.6 m", label + "the distance from the kerb is on the support page, with its sign and its unit");
    const kerbSliders = byType(tree, "ui.game:Slider");
    const kerbButtons = byType(byType(tree, "ui.PanelSectionRow").find(n => n.props.key === "k-steps"), "ui.Button");
    check(kerbSliders.length === (found ? 1 : 0), label + "the game's slider where its module is found, none where it is not");
    if (found) {
        const slider = kerbSliders[0].props;
        check(slider.start === -8 && slider.end === 6 && slider.gamepadStep === 0.1 && slider.value === -0.6, label + "the slider's range is the tool's: -8 to 6 m, tenths, at the value it is bound to");
        slider.onChange(-2.3400000000000003);
        slider.onDragStart();
        slider.onChange(1.23);
        slider.onChange(1.26);
        check(take() === JSON.stringify([["SetMargin", -2.3]]), label + "a change outside a drag is sent at once, in tenths; during a drag nothing is");
        slider.onDragEnd();
        check(take() === JSON.stringify([["SetMargin", 1.3]]), label + "the drag's last value is sent on release");
        slider.onDragEnd();
        check(take() === "[]", label + "a release with nothing dragged sends nothing");
        check(kerbButtons.length === 1, label + "with the slider only the default button is beside it");
    } else {
        check(kerbButtons.length === 3, label + "without the slider two step buttons and the default button");
        kerbButtons[0].props.onSelect();
        kerbButtons[1].props.onSelect();
        check(take() === JSON.stringify([["SetMargin", -0.7], ["SetMargin", -0.5]]), label + "the buttons step 0.1 m each way");
    }
    values.Margin = 5.95;
    kerbButtons[kerbButtons.length - 1].props.onSelect();
    check(take() === JSON.stringify([["SetMargin", -0.6]]), label + "the default button is -0.6 m");
    delete values.Margin;
    // 0.12.84: the post in the road: a switch on the support page for a post (the planner's choice counts), the same one the / key turns; none for a cantilever or a gantry
    const midRow = node => byType(node, "ui.PanelSectionRow").find(n => n.props.key === "s-mid");
    values.Mid = false;
    tree = draw(EditorPanel);
    const midSwitch = midRow(tree);
    check(!!midSwitch && midSwitch.props.left === "Post in the road", label + "a post: the in-road switch is on the support page");
    check(!!midSwitch && typeof midSwitch.props.tooltip === "string" && midSwitch.props.tooltip.includes("/"), label + "its tooltip names the / key");
    const midBox = !midSwitch ? null : found ? byType(midSwitch, "ui.game:Checkbox")[0] : byType(midSwitch, "ui.Button")[0];
    check(!!midBox && (found ? midBox.props.checked === false : midBox.props.selected === false), label + "off: the switch shows it");
    if (found) {
        midBox.props.onChange(true);
    } else {
        midBox.props.onSelect();
    }
    check(take() === JSON.stringify([["SetMid", true]]), label + "a click sends the state it asks for");
    values.Mid = true;
    tree = draw(EditorPanel);
    const midBoxOn = found ? byType(midRow(tree), "ui.game:Checkbox")[0] : byType(midRow(tree), "ui.Button")[0];
    check(found ? midBoxOn.props.checked === true : midBoxOn.props.selected === true, label + "on (a key press of the tool turned it): the switch follows the tool's state");
    if (found) {
        midBoxOn.props.onChange(false);
    } else {
        midBoxOn.props.onSelect();
    }
    check(take() === JSON.stringify([["SetMid", false]]), label + "and turns it off");
    values.Support = "cantilever.octa";
    tree = draw(EditorPanel);
    check(!midRow(tree), label + "a cantilever: no switch (an arm is not put across a road)");
    values.Support = "gantry.portal";
    tree = draw(EditorPanel);
    check(!midRow(tree), label + "a gantry: none either");
    values.Support = "";
    tree = draw(EditorPanel);
    check(!!midRow(tree), label + "the automatic support planned as a post (the fixture's plan): the switch is there");
    values.Editor = JSON.stringify(Object.assign({}, draft, { plan: "gantry.portal" }));
    tree = draw(EditorPanel);
    check(!midRow(tree), label + "the automatic support planned as a gantry: none");
    values.Editor = JSON.stringify(Object.assign({}, draft, { plan: undefined }));
    tree = draw(EditorPanel);
    check(!!midRow(tree), label + "the automatic support with no plan read yet: offered (the road decides)");
    values.Editor = JSON.stringify(draft);
    values.Support = "pole.double";
    delete values.Mid;
    // 0.12.75: the support style on the support page: the game's dropdown of the profiles (buttons where it is not found), how far the chosen one is backed under it, nothing when the list is empty (the feature is off)
    const profileList = [
        { id: "plain", zh: "通用", en: "Plain", pe: "", re: "", fe: "" },
        { id: "shanghai", zh: "上海", en: "Shanghai", pe: "std", re: "", fe: "supplier" },
        { id: "xuzhou", zh: "徐州", en: "Xuzhou", pe: "photo", re: "estimate", fe: "std" }
    ];
    const profileRows = (node, prefix) => ({ head: byType(node, "ui.PanelSectionRow").find(n => n.props.key === prefix + "-profile"), why: byType(node, "ui.PanelSectionRow").find(n => n.props.key === prefix + "-profile-why") });
    values.Profiles = "[]";
    values.Profile = "plain";
    tree = draw(EditorPanel);
    check(!profileRows(tree, "s").head && !profileRows(tree, "s").why, label + "the feature off (no profiles listed): no row");
    values.Profiles = JSON.stringify(profileList);
    values.Profile = "shanghai";
    tree = draw(EditorPanel);
    let profile = profileRows(tree, "s");
    check(!!profile.head && profile.head.props.left === "Support style" && profile.head.props.disableFocus === true, label + "the support style is a row of the support page");
    check(textOf(profile.why).includes("Paint std") && textOf(profile.why).includes("Forms supplier") && !textOf(profile.why).includes("Root"), label + "under it, how far the chosen profile's paint and forms are backed");
    if (full) {
        const drops = byType(profile.head, "ui.Dropdown");
        const items = byType({ props: {}, children: [drops[0].props.content] }, "ui.DropdownItem");
        check(drops.length === 1 && items.length === 3 && items[1].props.selected && !items[0].props.selected, label + "the profiles in a dropdown, the chosen one selected");
        check(textOf(items[1]).includes("Shanghai") && textOf(items[1]).includes("std") && textOf(items[0]).trim() === "Plain", label + "each option has its name and, where it has one, how far it is backed");
        items[2].props.onChange("xuzhou");
        check(take() === JSON.stringify([["SetProfile", "xuzhou"]]), label + "an item chooses its profile");
        check(textOf(byType(drops[0], "ui.DropdownToggle")[0]).includes("Shanghai"), label + "the toggle shows the profile in force");
    } else {
        const buttons = byType(profile.head, "ui.Button");
        check(buttons.length === 3, label + "without the game's dropdown a button for each profile");
        buttons[2].props.onSelect();
        check(take() === JSON.stringify([["SetProfile", "xuzhou"]]), label + "a button chooses its profile");
    }
    values.Profile = "xuzhou";
    tree = draw(EditorPanel);
    check(textOf(profileRows(tree, "s").why).includes("Root paint estimate"), label + "a profile with a root paint says how far that is backed");
    values.Profile = "plain";
    tree = draw(EditorPanel);
    check(!profileRows(tree, "s").why, label + "the plain profile claims no evidence: nothing is said under it");
    values.Profile = "no-such-profile";
    tree = draw(EditorPanel);
    check(!!profileRows(tree, "s").head && !profileRows(tree, "s").why, label + "a profile id the list does not have shows the first one (plain: nothing under it), and does not break the page");
    delete values.Profile;
    delete values.Profiles;
    values.Support = "";
    tree = draw(EditorPanel);
    check(byType(tree, "ui.PanelSectionRow").some(n => n.props.right === "Automatic (Double post)"), label + "automatic says what the planner chose for the board");
    values.Support = "pole.double";
    // a sign being edited has its support's settings and no choice of another form (the support is the item's)
    values.Editor = JSON.stringify(editorJson);
    check(withClass(draw(EditorPanel), "cdlts-support").length === 0, label + "a sign being edited has no support tiles");
    // a barrier of the catalogue is placed as a marking is: no support to choose
    values.Editor = JSON.stringify(draft);
    values.Catalog = JSON.stringify(catalogue.concat([{ id: "bar.noise", code: "护", zh: "声屏障", en: "Noise wall", kw: [], py: "", grp: "barrier", thumb: "" }]));
    values.Selected = "bar.noise";
    check(withClass(draw(EditorPanel), "cdlts-support").length === 0, label + "a barrier has no support to choose either");
    values.Catalog = JSON.stringify(catalogue);
    values.Selected = "t3";
    delete values.Editor;
    setUi({ editorTab: "sign" });

    // place, edit, delete and the eyedropper; the hint
    check(draw(ActionPill).props.className === "cdlts-pill", label + "the small strip under the tabs");
    const modeButtons = () => byType(draw(ActionPill), "ui.Button");
    let mb = modeButtons();
    check(mb.length === 4, label + "four actions: place, edit, delete, pick");
    check(byType(draw(ActionPill), "ui.Tooltip").map(b => b.props.tooltip).join("|") === "Place|Edit|Delete|Pick style (Shift: whole pole)" && mb.every(b => !!b.props.src), label + "round buttons with an icon and a name each");
    check(mb.map(b => b.props.src).join(" ") === "coui://cdlcts-icons/place.png coui://cdlcts-icons/edit.png coui://cdlcts-icons/delete.png coui://cdlcts-icons/pick.png", label + "the dock's own coloured pictures, not the game's black glyph files: " + mb.map(b => b.props.src).join(" "));
    check(mb[0].props.selected && !mb[1].props.selected && !mb[2].props.selected && !mb[3].props.selected, label + "placing is on");
    mb[0].props.onSelect();
    mb[1].props.onSelect();
    mb[2].props.onSelect();
    mb[3].props.onSelect();
    check(take() === JSON.stringify([["Leave"], ["SetMode", 2], ["SetMode", 1], ["SetMode", 3]]), label + "the mode that is on leaves the tool, as the game's tools do; the others are chosen");
    values.Mode = 3;
    mb = modeButtons();
    check(mb[3].props.selected && !mb[0].props.selected && !mb[1].props.selected && !mb[2].props.selected, label + "the eyedropper is on in its own mode, placing is not");
    mb[3].props.onSelect();
    mb[0].props.onSelect();
    check(take() === JSON.stringify([["Leave"], ["SetMode", 0]]), label + "pressed again the eyedropper leaves the tool; placing starts it");
    values.Mode = 0;
    values.ToolActive = false;
    mb = modeButtons();
    check(mb.every(b => !b.props.selected), label + "nothing is on while the tool is not running");
    mb[0].props.onSelect();
    check(take() === JSON.stringify([["SetMode", 0]]), label + "placing starts the tool");
    values.Selected = "";
    modeButtons()[0].props.onSelect();
    check(take() === "[]", label + "placing with no sign chosen does nothing");
    modeButtons()[3].props.onSelect();
    check(take() === JSON.stringify([["SetMode", 3]]), label + "the eyedropper needs no sign chosen");
    values.Selected = "t3";
    values.ToolActive = true;
    tree = draw(MainPanel);
    check(withClass(tree, "cdlts-note").some(n => textOf(n).includes("单击放置")) && withClass(tree, "cdlts-bad").length === 0, label + "the tool's line of help is shown");
    values.Hint = JSON.stringify({ t: "立杆上只能放小牌子", bad: true });
    check(withClass(draw(MainPanel), "cdlts-bad").some(n => textOf(n).includes("小牌子")), label + "a refusal in the colour of a refusal");
    values.Hint = "not json";
    check(!withClass(draw(MainPanel), "cdlts-note").some(n => textOf(n).includes("小牌子")), label + "a hint that is not JSON is no line");
    // 0.12.47: the keys of the tool in a card of their own, a row for each; none without keys. 0.12.67: in Core's style (caps and the game's mouse glyphs; the card takes no clicks)
    values.Hint = JSON.stringify({ t: "单击放置", bad: false, k: [["左键", "放置"], ["[  ]", "旋转"], ["右键 / Esc", "退出"], ["Ctrl+Z / Ctrl+Y", "撤销 / 重做"], ["按住 Ctrl 单击", "多选"], ["Hold Alt", "另立新架"], ["← →", "移动"]] });
    const keys = draw(KeysPanel);
    check(keys && keys.type === "div" && keys.props.className === "cdlts-hints" && textOf(withClass(keys, "cdlts-hints-title")[0]).trim() === "Keys", label + "the keys' own card, Core's style, with a title");
    const keyRows = withClass(keys, "cdlts-hint");
    check(keyRows.length === 7 && keyRows.every(r => withClass(r, "cdlts-hint-keys").length === 1 && withClass(r, "cdlts-hint-label").length === 1), label + "a row for each key: the keys, then the label");
    check(keyRows.map(r => textOf(withClass(r, "cdlts-hint-label")[0]).trim()).join("|") === "放置|旋转|退出|撤销 / 重做|多选|另立新架|移动", label + "the labels are the hint's own text");
    const capsOf = r => withClass(r, "cdlts-key").map(k => textOf(k).trim()).join(",");
    const mouseOf = r => withClass(r, "cdlts-mouse").map(m => m.props.src).join(",");
    check(mouseOf(keyRows[0]) === "Media/Mouse/LMB.svg" && capsOf(keyRows[0]) === "", label + "a left click is the game's glyph, no cap: " + mouseOf(keyRows[0]));
    check(capsOf(keyRows[1]) === "[,]" && mouseOf(keyRows[1]) === "", label + "[  ] is two caps, one for each bracket");
    check(mouseOf(keyRows[2]) === "Media/Mouse/RMB.svg" && capsOf(keyRows[2]) === "Esc" && withClass(keyRows[2], "cdlts-key-or").length === 1, label + "a right click or Esc: the glyph, a slash, a cap");
    check(capsOf(keyRows[3]) === "Ctrl,Z,Ctrl,Y" && withClass(keyRows[3], "cdlts-key-plus").length === 2 && withClass(keyRows[3], "cdlts-key-or").length === 1, label + "Ctrl+Z / Ctrl+Y: four caps, two plus signs, one slash");
    check(capsOf(keyRows[4]) === "Ctrl" && mouseOf(keyRows[4]) === "Media/Mouse/LMB.svg" && withClass(keyRows[4], "cdlts-key-word").map(w => textOf(w).trim()).join() === "按住", label + "Hold Ctrl and click: the word, a cap and the left button");
    check(capsOf(keyRows[5]) === "Alt" && withClass(keyRows[5], "cdlts-key-word").length === 1 && capsOf(keyRows[6]) === "←,→", label + "Hold Alt: the word and a cap; the arrows are two caps");
    check(JSON.stringify(keyParts("Ctrl + click")) === JSON.stringify([[[{ cap: "Ctrl" }], [{ mouse: "lmb" }]]]), label + "Ctrl + click: a cap, a plus sign and the left button");
    check(JSON.stringify(keyParts("<  >")) === JSON.stringify([[[{ cap: "<" }, { cap: ">" }]]]) && JSON.stringify(keyParts(";  '")) === JSON.stringify([[[{ cap: ";" }, { cap: "'" }]]]), label + "the keys of the distance from the kerb and of the support are caps of their own: < >, ; '");
    // 0.12.90: two columns, the larger half in the first; the rows go down the first and on in the second, in the order they were sent, and each column's labels start at one width
    const width = row => withClass(row, "cdlts-hint-keys")[0].props.style.minWidth;
    const keyCols = withClass(keys, "cdlts-hints-col");
    check(withClass(keys, "cdlts-hints-cols").length === 1 && keyCols.length === 2 && JSON.stringify(keyCols.map(c => withClass(c, "cdlts-hint").length)) === "[4,3]", label + "two columns of 4 and 3 rows: " + keyCols.map(c => withClass(c, "cdlts-hint").length));
    check(keyCols.every(c => withClass(c, "cdlts-hint").every(r => width(r) === width(withClass(c, "cdlts-hint")[0]))), label + "the labels of a column start at one width: " + keyCols.map(c => width(withClass(c, "cdlts-hint")[0])));
    const widthOfFirst = c => parseFloat(width(withClass(c, "cdlts-hint")[0]));
    check(widthOfFirst(keyCols[0]) >= 92 && widthOfFirst(keyCols[1]) >= 92 && widthOfFirst(keyCols[1]) !== widthOfFirst(keyCols[0]), label + "each column is as wide as its own widest keys, 92 rem at least (the Ctrl+Z column is wider than the Hold Alt one)");
    const oneRow = (() => { values.Hint = JSON.stringify({ t: "x", bad: false, k: [["左键", "放置"]] }); return draw(KeysPanel); })();
    check(withClass(oneRow, "cdlts-hints-col").length === 1 && withClass(oneRow, "cdlts-hint").length === 1, label + "one key: one column, no empty second one");
    const oddRows = (() => { values.Hint = JSON.stringify({ t: "x", bad: false, k: Array.from({ length: 15 }, (_, i) => ["Alt", "行" + i]) }); return draw(KeysPanel); })();
    check(JSON.stringify(withClass(oddRows, "cdlts-hints-col").map(c => withClass(c, "cdlts-hint").length)) === "[8,7]"
        && withClass(oddRows, "cdlts-hint-label").map(l => textOf(l).trim()).join("|") === Array.from({ length: 15 }, (_, i) => "行" + i).join("|"), label + "15 keys: 8 and 7, in the order sent");
    values.Hint = JSON.stringify({ t: "单击放置", bad: false, k: [["左键", "放置"], ["[  ]", "旋转"], ["右键 / Esc", "退出"], ["Ctrl+Z / Ctrl+Y", "撤销 / 重做"], ["按住 Ctrl 单击", "多选"], ["Hold Alt", "另立新架"], ["← →", "移动"]] });
    check(!withClass(draw(MainPanel), "cdlts-note").some(n => textOf(n).includes("旋转")), label + "the keys are not in the catalogue's line of help");
    check(JSON.stringify(keyParts("Ctrl+Z / Ctrl+Y")) === JSON.stringify([[[{ cap: "Ctrl" }], [{ cap: "Z" }]], [[{ cap: "Ctrl" }], [{ cap: "Y" }]]]) && keyParts("").length === 0 && keyParts(" / ").length === 0, label + "the parser: alternatives of groups of caps, nothing for no text");
    check(capsWidth("左键") === 22 && capsWidth("Ctrl+Z") > capsWidth("Z"), label + "the width of the caps grows with them");
    // 0.12.68: at the bottom left until it is dragged (the stylesheet places it), then where it was put down, kept for the next time
    check(keys.props.style === undefined && typeof keys.props.onMouseDown === "function", label + "no place of its own until it is dragged; the card is the grip");
    const listening = {};
    document.addEventListener = (name, fn) => { listening[name] = fn; };
    document.removeEventListener = (name, fn) => { if (listening[name] === fn) delete listening[name]; };
    const card = { style: {}, getBoundingClientRect: () => ({ left: 20, top: 900 }), offsetParent: { getBoundingClientRect: () => ({ left: 0, top: 0 }) } };
    const grabEvent = (button, x, y) => ({ button, clientX: x, clientY: y, currentTarget: card });
    keys.props.onMouseDown(grabEvent(2, 100, 100));
    check(!listening.mousemove, label + "the right button does not drag");
    keys.props.onMouseDown(grabEvent(0, 100, 100));
    check(typeof listening.mousemove === "function" && typeof listening.mouseup === "function", label + "the left button listens for the move and the release");
    listening.mousemove({ clientX: 150, clientY: 80 });
    check(card.style.left === "70rem" && card.style.top === "880rem" && card.style.bottom === "auto", label + "the card is moved on its own element while the mouse moves: " + JSON.stringify(card.style));
    let place = draw(KeysPanel).props.style;
    check(place && place.left === "70rem" && place.top === "880rem" && place.bottom === "auto", label + "and a render of it puts it where it was put: " + JSON.stringify(place));
    listening.mousemove({ clientX: -5000, clientY: 99999 });
    place = draw(KeysPanel).props.style;
    check(card.style.left === "0rem" && card.style.top === "1040rem" && place.left === "0rem" && place.top === "1040rem", label + "kept on the screen at its edges: " + JSON.stringify(place));
    listening.mouseup();
    check(!listening.mousemove && !listening.mouseup, label + "letting go stops listening");
    keys.props.onMouseDown(grabEvent(0, 0, 0));
    listening.mousemove({ clientX: 30, clientY: -20 });
    check(JSON.stringify(draw(KeysPanel).props.style) === JSON.stringify({ left: "50rem", top: "880rem", bottom: "auto" }), label + "a new drag is measured from the grip's own box: " + JSON.stringify(draw(KeysPanel).props.style));
    listening.mouseup();
    values.Hint = JSON.stringify({ t: "单击放置", bad: false });
    check(draw(KeysPanel) === null, label + "no keys, no panel");
    values.Hint = "";
    check(draw(KeysPanel) === null, label + "the tool left, no panel");

    // a marking: no support
    const markEntry = { id: "mk.stop", code: "线52", zh: "停止线", en: "Stop line", kw: [], py: "tzx", grp: "marking", thumb: "" };
    const clearEntry = { id: "mk.clear", code: "清除", zh: "清除原版标线", en: "Clear the game's markings", kw: [], py: "qc", grp: "marking", thumb: "" };
    values.Catalog = JSON.stringify(catalogue.concat([markEntry, clearEntry]));
    values.Selected = "mk.stop";
    values.Editor = JSON.stringify(Object.assign({}, editorJson, { mode: "draft", marking: true, code: "线52", title: "停止线" }));
    tree = draw(EditorPanel);
    check(withClass(tree, "cdlts-support").length === 0 && withClass(tree, "cdlts-tabs").length + withClass(tree, "cdlts-tabs-flat").length === 0 && !withClass(tree, "cdlts-note").some(n => textOf(n).includes("no support")), label + "a marking has no supports and no second page (0.12.47: and no note about it)");
    dock = draw(Dock);
    check(textOf(withClass(dock, "cdlts-fly-title")[0]).includes("线52  停止线") && withClass(dock, "cdlts-fly-title").length === 1, label + "the dock names the entry in the panel's header");
    delete values.Editor;
    check(tabButtons(draw(Dock)).length === 6, label + "the markings are a kind of their own: " + tabButtons(draw(Dock)).length);
    check(tabButtons(draw(Dock)).some(b => b.props.src === "coui://cdlcts-icons/marking.png"), label + "the marking tab has an icon before its asset thumbnails are ready");

    // clearing the game's markings: the counts, and restoring all after the game's dialog (or a second click)
    values.Selected = "mk.clear";
    values.ClearAreas = 3;
    values.ClearHidden = 41;
    values.Editor = JSON.stringify(Object.assign({}, editorJson, { mode: "draft", marking: true, fields: [], code: "清除", title: "清除原版标线" }));
    setUi({ editorTab: "sign" });
    tree = draw(EditorPanel);
    check(withClass(tree, "cdlts-note").some(n => textOf(n).includes("3 area(s)") && textOf(n).includes("41 game lines")), label + "the areas and the hidden markings are counted");
    const restore = () => wideAction(draw(EditorPanel), found, "Restore") || wideAction(draw(EditorPanel), found, "Click again");
    restore().props.onSelect();
    check(take() === "[]" && ui.confirming === true, label + "the first press only asks");
    if (full) {
        const dialog = draw(RestoreDialog);
        check(dialog && dialog.type === "ui.ConfirmationDialog" && typeof dialog.props.onConfirm === "function", label + "with the game's confirmation dialog");
        dialog.props.onCancel();
        check(take() === "[]" && ui.confirming === false, label + "cancelled, nothing is restored");
        restore().props.onSelect();
        draw(RestoreDialog).props.onConfirm(false);
        check(take() === JSON.stringify([["RestoreMarkings"]]) && ui.confirming === false, label + "confirmed, everything is restored once");
    } else {
        check(draw(RestoreDialog) === null, label + "no dialog without the game's");
        check(textOf(restore()).includes("Click again"), label + "the button says to press again");
        restore().props.onSelect();
        check(take() === JSON.stringify([["RestoreMarkings"]]) && ui.confirming === false, label + "the second press restores, once");
    }
    delete values.ClearAreas;
    delete values.ClearHidden;
    values.Catalog = JSON.stringify(catalogue);
    values.Selected = "t3";

    // the save data and the display settings, folded away (0.12.47: the controls are the keys' panel, the test switches are gone)
    tree = draw(SettingsPanel);
    let foldouts = byType(tree, "ui.PanelFoldout");
    check(foldouts.length === 3 && foldouts[0].props.initialExpanded === true && foldouts[1].props.initialExpanded === true && foldouts[1].props.header === "Memory and distance" && foldouts[2].props.initialExpanded === false && foldouts[2].props.header === "Display", label + "three parts of the settings tab: the save data and the memory open, the display folded");
    check(!textOf(tree).includes("Render queue") && !textOf(tree).includes("test build"), label + "nothing of the test switches is left");
    // 0.12.38: the save data: nothing loaded, then a record restored with two roads put away, then records offered.
    check(textOf(foldouts[0]).includes("Load a city"), label + "the save data before a city is loaded");
    values.SaveData = JSON.stringify({ status: "restored:120:5:2", offers: [], away: [{ i: 0, why: "upgraded", items: 3, kerb: false, prefab: "Medium Road", x: 1, z: 2 }, { i: 4, why: "gone", items: 2, kerb: true, prefab: "Small Road", x: 3, z: 4 }] });
    tree = draw(SettingsPanel);
    let saveData = byType(tree, "ui.PanelFoldout")[0];
    check(textOf(saveData).includes("Restored: 120 placed, 5 hidden on 2 changed road(s)."), label + "what was restored and put away");
    const awayRows = byType(saveData, "ui.PanelSectionRow").filter(n => String(n.props.left).includes("item(s)"));
    check(awayRows.length === 2 && String(awayRows[0].props.left).includes("upgraded") && String(awayRows[1].props.left).includes("kerb style"), label + "a row for each road put away, with why and a kerb style");
    const buttonOf = (node, text) => byType(node, "ui.Button").find(b => textOf(b).trim() === text);
    buttonOf(awayRows[1], "Show").props.onSelect();
    buttonOf(awayRows[1], "Place").props.onSelect();
    buttonOf(awayRows[0], "Delete").props.onSelect();
    buttonOf(saveData, "Place all").props.onSelect();
    buttonOf(saveData, "Delete all").props.onSelect();
    check(take() === JSON.stringify([["SaveDataLocate", 4], ["SaveDataPlace", 4], ["SaveDataDelete", 0], ["SaveDataPlace", -1], ["SaveDataDelete", -1]]), label + "a road shown, placed, deleted, and all of them");
    values.SaveData = JSON.stringify({ status: "offers", offers: [{ name: "My city", time: "2026-10-28 09:00", score: 80, items: 40 }], away: [] });
    tree = draw(SettingsPanel);
    saveData = byType(tree, "ui.PanelFoldout")[0];
    check(textOf(saveData).includes("My city") && textOf(saveData).includes("80%"), label + "a record offered, with its fit");
    buttonOf(saveData, "Restore").props.onSelect();
    check(take() === JSON.stringify([["SaveDataRestore", 0]]), label + "the record offered restored");
    // 0.12.51: what the game's saving or loading did that the store does not expect, a warning each.
    check(!byType(saveData, "div").some(n => String(n.props.className).includes("cdlts-warn")), label + "no warning when there is none");
    values.SaveData = JSON.stringify({ status: "restored:7:0:0", warn: { leak: 3, plants: 12, other: 1 }, offers: [], away: [] });
    tree = draw(SettingsPanel);
    saveData = byType(tree, "ui.PanelFoldout")[0];
    const warned = byType(saveData, "div").filter(n => String(n.props.className).includes("cdlts-warn")).map(n => textOf(n).trim());
    check(warned.length === 3 && warned[0].includes("3 item(s) were in the save") && warned[1].includes("12 greening plant(s)") && warned[2] === "other",
        label + "a warning for each code, with its count (an unknown code as it is)");
    delete values.SaveData;
    values.NightGain = 20; values.Night = 0; values.LedDay = 60; values.LedNight = 15;
    tree = draw(SettingsPanel);
    const stepperOf = labelText => {
        const r = byType(tree, "ui.PanelSectionRow").find(n => String(n.props.left).includes(labelText));
        return r ? byType(r, "ui.Button") : [];
    };
    stepperOf("Night brightness")[1].props.onSelect();
    stepperOf("Night glow")[0].props.onSelect();
    stepperOf("LED by day")[1].props.onSelect();
    stepperOf("LED at night")[0].props.onSelect();
    check(take() === JSON.stringify([["SetNightGain", 30], ["SetNight", 0], ["SetLedDay", 120], ["SetLedNight", 10]]), label + "the steppers go to the next step and stop at the ends");
    // 0.12.104: the draw distance leaves the display settings for the memory fp (with what each step costs); the fp's own figures are below.
    check(stepperOf("Sign and marking distance").length === 0, label + "the display settings do not hold the draw distance any more");
    const memorySection = byType(tree, "ui.PanelFoldout").find(n => String(n.props.header).includes("Memory and distance"));
    check(!!memorySection && memorySection.props.initialExpanded === true && byType(memorySection, "ui.Button").length >= 7, label + "the settings page has the memory and distance section, open, with the steps in it");
    const MB = 1048576;
    const tiersOf = node => byType(node, "ui.Button").filter(b => String(b.props.className).includes("cdlts-tier"));
    const tierName = b => textOf(b).trim().split(/\s+/)[0];
    const hasClass = (node, name) => String(node.props.className).split(/\s+/).includes(name);
    const meterWidth = node => parseFloat(find(node, n => String(n.props.className).includes("cdlts-meter-fill") && n.props.style && String(n.props.style.width).endsWith("%") && !String(n.props.className).includes("cdlts-tier"))[0].props.style.width);
    // Nothing from the mod yet: reading, and the seven steps with no figure; the step in use (700 by default) is the one selected.
    let fp = draw(FootprintPanel);
    check(textOf(fp).includes("Reading...") && tiersOf(fp).length === 7, label + "before the first figures: reading, and the seven steps");
    check(tiersOf(fp).filter(b => hasClass(b, "cdlts-tier-now")).map(tierName).join() === "700" && tiersOf(fp).every(b => textOf(b).includes("—")), label + "700 is the step in use and the only one marked as such, none has a figure yet");
    check(textOf(fp).includes("Wait for the tiles to be built.") && byType(fp, "ui.Button").filter(b => textOf(b).startsWith("Use ")).length === 0, label + "no advice yet and no button to use one");
    values.Footprint = "{not json";
    fp = draw(FootprintPanel);
    check(textOf(fp).includes("Reading...") && tiersOf(fp).length === 7, label + "a text that is not JSON is read as nothing");
    // The mod's figures: 170 MB of 384 MB (light), 2000 m is the farthest that fits.
    const sample = {
        v: 170 * MB, r: 4 * MB, b: 384 * MB, t: 8192 * MB, lv: 0, mesh: 92 * MB, face: 58 * MB, det: 20 * MB, item: 3 * MB, pic: 1 * MB, rend: 1240, tiles: 61, items: 1860, ok: true, rec: 2000,
        steps: [{ d: 300, m: 60 * MB, l: 0 }, { d: 500, m: 110 * MB, l: 0 }, { d: 700, m: 170 * MB, l: 0 }, { d: 1000, m: 230 * MB, l: 1 }, { d: 1500, m: 330 * MB, l: 1 }, { d: 2000, m: 380 * MB, l: 1 }, { d: 3000, m: 520 * MB, l: 2 }]
    };
    values.Footprint = JSON.stringify(sample);
    fp = draw(FootprintPanel);
    const rowOf = text => byType(fp, "ui.PanelSectionRow").find(n => String(n.props.left).includes(text));
    check(textOf(rowOf("Graphics memory").props.right).trim() === "170 MB / 384 MB" && rowOf("Graphics memory").props.tooltip.includes("5 %"), label + "the graphics memory against the budget, with how the budget is set in its tooltip");
    check(Math.abs(meterWidth(fp) - 170 / (384 * 1.5) * 100) < 0.01, label + "the meter is as long as 150 % of the budget: " + meterWidth(fp));
    check(textOf(fp).includes("Meshes 92 MB · Pictures 58 MB · Detail 20 MB"), label + "what the graphics memory is made of");
    check(String(rowOf("Memory (RAM)").props.right) === "4.0 MB" && textOf(fp).includes("1,240 renderers · 61 tiles · 1,860 items"), label + "the memory, and the counts with their thousands");
    check(find(fp, n => String(n.props.className) === "cdlts-meter-tick").map(n => parseFloat(n.props.style.left)).sort((a, b) => a - b).map(v => v.toFixed(2)).join() === "33.33,66.67", label + "two ticks on the meter, at the half and the whole of the budget (a third and two thirds of its length)");
    check(tiersOf(fp).length === 7 && tiersOf(fp).map(tierName).join() === "300,500,700,1000,1500,2000,3000", label + "a button for each step, in order");
    check(tiersOf(fp).filter(b => hasClass(b, "cdlts-tier-now")).map(tierName).join() === "700", label + "the step in use is marked");
    check(tiersOf(fp).every(b => !b.props.selected), label + "and none is the button's selected: the game's look for it floods the row with its accent colour");
    const rec = tiersOf(fp).filter(b => find(b, n => hasClass(n, "cdlts-tier-chip")).length > 0);
    check(rec.length === 1 && tierName(rec[0]) === "2000" && textOf(rec[0]).includes("Suggested"), label + "the suggested step is marked, once, with its word");
    check(tiersOf(fp).filter(b => textOf(b).includes("Suggested")).length === 1, label + "and only that step has the word");
    const classed = name => find(fp, n => hasClass(n, name)).map(n => textOf(n).trim());
    check(classed("cdlts-fp-title").join("|") === "Sign and marking distance (m)" && classed("cdlts-fp-sub").join("|") === "Meshes 92 MB · Pictures 58 MB · Detail 20 MB|1,240 renderers · 61 tiles · 1,860 items|This mod's meshes and pictures only. Steps are estimates for the camera's place.", label + "the list has its title, and the lines under the figures and at the foot are the small dim ones");
    check(find(rec[0], n => hasClass(n, "cdlts-tier-chip")).length === 1 && tiersOf(fp).filter(b => find(b, n => hasClass(n, "cdlts-tier-chip")).length > 0).length === 1, label + "the suggested step's word is a chip, and only one step has it");
    check(tiersOf(fp).every(b => find(b, n => hasClass(n, "cdlts-tier-mark")).length === 1), label + "every step has its marker (shown for the one in use by its class)");
    check(textOf(tiersOf(fp)[5]).includes("380 MB") && textOf(tiersOf(fp)[5]).includes("Fine") && textOf(tiersOf(fp)[0]).includes("Light") && textOf(tiersOf(fp)[6]).includes("Heavy"), label + "each step's figure and how heavy it is");
    const barOf = b => find(b, n => String(n.props.className).includes("cdlts-meter-fill"))[0];
    check(Math.abs(parseFloat(barOf(tiersOf(fp)[6]).props.style.width) - 520 / (384 * 1.5) * 100) < 0.01 && String(barOf(tiersOf(fp)[6]).props.className).includes("cdlts-bg2") && String(barOf(tiersOf(fp)[0]).props.className).includes("cdlts-bg0"), label + "a step's bar is on the meter's scale, in the colour of its level");
    check(textOf(fp).includes("Room to go to 2000 m."), label + "in use 700, suggested 2000: room to go farther");
    const use = byType(fp, "ui.Button").filter(b => textOf(b).startsWith("Use "));
    check(use.length === 1 && textOf(use[0]).trim() === "Use 2000 m", label + "a button to use it");
    use[0].props.onSelect();
    tiersOf(fp)[4].props.onSelect();
    tiersOf(fp)[0].props.onSelect();
    check(take() === JSON.stringify([["SetViewDistance", 2000], ["SetViewDistance", 1500], ["SetViewDistance", 300]]), label + "the button and the steps send the distance");
    // In use is the suggested one: no button. In use is beyond it: heavy here, and the button goes back.
    values.ViewDistance = 2000;
    fp = draw(FootprintPanel);
    check(textOf(fp).includes("This is the farthest that fits.") && byType(fp, "ui.Button").filter(b => textOf(b).startsWith("Use ")).length === 0 && tiersOf(fp).filter(b => hasClass(b, "cdlts-tier-now")).map(tierName).join() === "2000", label + "at the suggested step: nothing to change");
    values.ViewDistance = 3000;
    values.Footprint = JSON.stringify(Object.assign({}, sample, { v: 520 * MB, lv: 2 }));
    fp = draw(FootprintPanel);
    check(textOf(fp).includes("Heavy here: 2000 m fits better.") && textOf(byType(fp, "ui.Button").find(b => textOf(b).startsWith("Use "))).trim() === "Use 2000 m", label + "beyond it: heavy here, and the way back");
    check(meterWidth(fp) > 90 && textOf(rowOf("Graphics memory").props.right).trim() === "520 MB / 384 MB" && String(find(fp, n => String(n.props.className).includes("cdlts-fp-val"))[0].props.className).includes("cdlts-fc2"), label + "over the budget: the meter is nearly full, the figure red");
    values.Footprint = JSON.stringify(Object.assign({}, sample, { v: 2000 * MB, lv: 2 }));
    check(meterWidth(draw(FootprintPanel)) === 100, label + "a meter never runs past its end");
    // A level the script does not know is the heavy colour, on the meter, on a step's bar and on the figure.
    values.ViewDistance = 700;
    values.Footprint = JSON.stringify(Object.assign({}, sample, { lv: 9, steps: sample.steps.map(s => Object.assign({}, s, { l: 9 })) }));
    fp = draw(FootprintPanel);
    check(String(find(fp, n => String(n.props.className).includes("cdlts-meter-fill") && !String(n.props.className).includes("cdlts-tier"))[0].props.className).includes("cdlts-bg2") && String(find(fp, n => String(n.props.className).includes("cdlts-fp-val"))[0].props.className).includes("cdlts-fc2")
        && tiersOf(fp).every(b => String(barOf(b).props.className).includes("cdlts-bg2")), label + "a level that is not one is painted as heavy");
    // Figures that do not agree: no model but a suggestion, or a model and none: no advice and no button.
    for (const odd of [{ ok: false, rec: 2000 }, { ok: true, rec: 0 }]) {
        values.Footprint = JSON.stringify(Object.assign({}, sample, odd));
        fp = draw(FootprintPanel);
        check(textOf(fp).includes("Wait for the tiles to be built.") && byType(fp, "ui.Button").filter(b => textOf(b).startsWith("Use ")).length === 0 && !textOf(fp).includes("Room to go"), label + "figures that do not agree (ok " + odd.ok + ", suggested " + odd.rec + "): no advice");
    }
    // The tiles are not built yet: the card's figures, no advice.
    values.ViewDistance = 700;
    values.Footprint = JSON.stringify({ v: 30 * MB, r: MB, b: 384 * MB, t: 0, lv: 0, mesh: 0, face: 0, det: 30 * MB, item: MB, pic: 0, rend: 0, tiles: 0, items: 5, ok: false, rec: 0,
        steps: sample.steps.map(s => ({ d: s.d, m: -1, l: 2 })) });
    fp = draw(FootprintPanel);
    check(textOf(fp).includes("Wait for the tiles to be built.") && byType(fp, "ui.Button").filter(b => textOf(b).startsWith("Use ")).length === 0 && tiersOf(fp).every(b => textOf(b).includes("—") && !textOf(b).includes("Light") && !textOf(b).includes("Heavy")), label + "tiles not built: no advice, no figure for the steps, no level word");
    check(tiersOf(fp).every(b => find(b, n => String(n.props.className).includes("cdlts-meter-fill")).length === 0), label + "and no bar is drawn for a step with no figure");
    // The fp asks for its figures while it is there and says when it is gone.
    values.Footprint = JSON.stringify(sample);
    effects.length = 0;
    draw(FootprintPanel);
    check(effects.length === 1 && typeof effects[0] === "function", label + "the fp gives one effect");
    const gone = effects[0]();
    check(take() === JSON.stringify([["WatchFootprint", true]]) && typeof gone === "function", label + "it asks for the figures when it is there");
    gone();
    check(take() === JSON.stringify([["WatchFootprint", false]]), label + "and says it is gone when it goes");
    delete values.Footprint;
    delete values.ViewDistance;
    // The figures as text.
    check(bytesText(0) === "0.0 MB" && bytesText(MB * 0.96) === "1.0 MB" && bytesText(9.94 * MB) === "9.9 MB" && bytesText(10 * MB) === "10 MB" && bytesText(170.4 * MB) === "170 MB" && bytesText(1024 * MB) === "1.0 GB" && bytesText(1536 * MB) === "1.5 GB" && bytesText(-1) === "—" && bytesText(NaN) === "—", label + "bytes as MB or GB, one decimal under 10 MB, a dash for none");
    check(countText(0) === "0" && countText(999) === "999" && countText(1000) === "1,000" && countText(1860) === "1,860" && countText(1234567) === "1,234,567", label + "counts with thousands separators");
    // 0.12.49: the lamp-post ads' light boxes and lanterns at night
    values.LampLit = 3; values.LampGlow = 6;
    tree = draw(SettingsPanel);
    stepperOf("Light boxes at night")[1].props.onSelect();
    stepperOf("Lanterns and neon at night")[0].props.onSelect();
    const lampSent = take();
    check(lampSent === JSON.stringify([["SetLampLit", 4], ["SetLampGlow", 4]]), label + "the lamp-post ads' brightness at night, a step each way: " + lampSent);
    delete values.LampLit; delete values.LampGlow;
    // 0.12.75: the same row on the display page, with the same trigger (and none while the feature is off)
    tree = draw(SettingsPanel);
    check(!profileRows(tree, "d").head, label + "the display page has no support style row while no profile is listed");
    values.Profiles = JSON.stringify(profileList);
    values.Profile = "shanghai";
    tree = draw(SettingsPanel);
    profile = profileRows(tree, "d");
    check(!!profile.head && profile.head.props.left === "Support style" && !!profile.why, label + "the display page has the support style row, with its evidence line under it");
    if (full) {
        const drops = byType(profile.head, "ui.Dropdown");
        const items = byType({ props: {}, children: [drops[0].props.content] }, "ui.DropdownItem");
        items[1].props.onChange("shanghai");
        check(take() === JSON.stringify([["SetProfile", "shanghai"]]), label + "the display page's item sends the same trigger");
    }
    delete values.Profile;
    delete values.Profiles;
    tree = draw(SettingsPanel);
    check(textOf(byType(tree, "ui.PanelSectionRow").find(n => String(n.props.left).includes("Night brightness"))).includes("×2"), label + "the brightness shows its factor");
    // 0.12.5: the items with no road near them, and deleting them after a confirmation
    values.Orphans = 0;
    tree = draw(SettingsPanel);
    const orphanRow = byType(byType(tree, "ui.PanelFoldout")[0], "ui.PanelSectionRow").find(n => n.props.left === "Items with no road near them");
    check(!!orphanRow && orphanRow.props.right === "0" && !wideAction(tree, found, "Delete the items"), label + "no item without a road: the count, no button (in the save data)");
    values.Orphans = 986;
    tree = draw(SettingsPanel);
    check(byType(tree, "ui.PanelSectionRow").some(n => n.props.right === "986"), label + "986 items without a road are counted");
    wideAction(tree, found, "Delete the items").props.onSelect();
    check(take() === "[]" && ui.confirmingOrphans === true, label + "the first press only asks");
    if (full) {
        const dialog = draw(RestoreDialog);
        check(dialog && dialog.type === "ui.ConfirmationDialog" && String(dialog.props.message).includes("986"), label + "the game's dialog says how many");
        dialog.props.onCancel();
        check(take() === "[]" && ui.confirmingOrphans === false, label + "cancelled, nothing is deleted");
        wideAction(draw(SettingsPanel), found, "Delete the items").props.onSelect();
        draw(RestoreDialog).props.onConfirm(false);
        check(take() === JSON.stringify([["DeleteOrphans"]]) && ui.confirmingOrphans === false, label + "confirmed, they are deleted once");
    } else {
        const again = wideAction(draw(SettingsPanel), found, "Press again");
        check(!!again, label + "without the game's dialog the button says to press again");
        again.props.onSelect();
        check(take() === JSON.stringify([["DeleteOrphans"]]) && ui.confirmingOrphans === false, label + "the second press deletes them, once");
    }
    delete values.Orphans;

    // the height of what scrolls: the panel starts 110 rem down, has a header of 56 and the game's toolbar is 120 at the bottom (0.12.63: it ran into the toolbar)
    {
        const heightOf = tr => Number(String(byType(tr, "ui.Scrollable")[0].props.style.maxHeight).replace("rem", ""));
        for (const screen of [768, 1080, 1440, 2160]) {
            globalThis.window.innerHeight = screen;
            globalThis.getComputedStyle = () => ({ fontSize: (screen / 1080) + "px" });
            const entries = heightOf(draw(MainPanel));
            check(entries >= 240 && entries <= 760 && 110 + 56 + entries + 120 <= Math.max(1080, 110 + 56 + 240 + 120), label + `a screen of ${screen} px: the entries scroll within ${entries} rem, clear of the toolbar`);
        }

        globalThis.window.innerHeight = 1080;
        globalThis.getComputedStyle = () => ({ fontSize: "1px" });
        values.Editor = JSON.stringify(editorJson);
        const settings = heightOf(draw(EditorPanel)), entriesNow = heightOf(draw(MainPanel));
        // 0.12.69 (the owner's red box: "限制在这个区域内就行"): the list's panel ends where the strips beside it do, 490 rem under their top: the header 40, what scrolls the rest less a margin of 12. 0.12.90 (the owner:
        // "调整一下UI窗口大小"): the editor's window is not held to the strips: it scrolls within 600 rem, wider than the list's, and still ends clear of the toolbar (110 down, header 56, tabs 44, 120 of toolbar)
        check(entriesNow === 438 && 40 + entriesNow <= 490, label + `the list's entries are ${entriesNow} rem: the panel ends within the strips' 490`);
        check(settings === 600 && 110 + 56 + 44 + settings + 120 <= 1080, label + `the editor's settings scroll within ${settings} rem, clear of the toolbar`);
        for (const screen of [768, 1080, 1440, 2160]) {
            globalThis.window.innerHeight = screen;
            globalThis.getComputedStyle = () => ({ fontSize: (screen / 1080) + "px" });
            const body = heightOf(draw(EditorPanel));
            check(body >= 240 && body <= 600 && 110 + 56 + 44 + body + 120 <= Math.max(1080, 110 + 56 + 44 + 240 + 120), label + `a screen of ${screen} px: the editor scrolls within ${body} rem, clear of the toolbar`);
        }

        globalThis.window.innerHeight = 1080;
        globalThis.getComputedStyle = () => ({ fontSize: "1px" });
        // a text box takes the rest of the row: a label of two characters is in the game's row; one of up to 200 rem (15 CJK characters, a Latin phrase) is beside its box in a block of its own
        // (0.12.69; the game's row wrapped it in two lines, and a label row with the box under it took 90 rem); a longer one is a small line over its box
        values.Editor = JSON.stringify(Object.assign({}, editorJson, { fields: [
            { k: "a", l: "左前方信息一", t: "text", v: "", max: 8 }, { k: "b", l: "编号", t: "text", v: "", max: 8 }, { k: "c", l: "Left front, first line", t: "text", v: "", max: 8 },
            { k: "d", l: "第一出口：地名二（可空）再加一些很长的说明", t: "text", v: "", max: 8 }] }));
        const fieldTree = draw(EditorPanel);
        const blocks = withClass(fieldTree, "cdlts-field"), stacked = withClass(fieldTree, "cdlts-full");
        check(blocks.length === 3 && withClass(fieldTree, "cdlts-field-stack").length === 1 && stacked.length === 1 && byType(stacked[0], found ? "ui.game:TextInput" : "input").length === 1,
            label + "the labels of 6 CJK characters and of a Latin phrase are beside their boxes, the long one over its box, in blocks: " + blocks.length + " blocks, " + stacked.length + " over");
        check(blocks.every(b => withClass(b, "cdlts-field-label").length === 1 && withClass(b, "cdlts-field-control").length === 1 && byType(b, found ? "ui.game:TextInput" : "input").length === 1), label + "each block has its label and its box");
        check(byType(fieldTree, "ui.PanelSectionRow").some(n => n.props.left === "编号") && !byType(fieldTree, "ui.PanelSectionRow").some(n => n.props.left === "左前方信息一"), label + "the label of 2 characters stays in the game's row");
        values.Editor = "";
    }

    // closing: the panel's cross folds it away; the dock itself is closed by the way in (the button on the right) and draws nothing then
    values.Editor = "";
    setUi({ flyout: true, showList: true });
    dock = draw(Dock);
    take();
    byType(dock, "ui.Panel")[0].props.onClose();
    check(ui.flyout === false && take() === "[]", label + "the panel's cross folds the entries away and leaves the tool alone");
    setUi({ open: false });
    check(draw(GameWindow) === null, label + "closed panels draw nothing");
    setUi({ open: true });

    // ---- the editor
    values.Editor = "";
    check(draw(EditorPanel) === null, label + "no editor while the C# side sends none");
    values.Editor = JSON.stringify(editorJson);
    setUi({ editorTab: "sign" });
    tree = draw(EditorPanel);
    check(tree.type === "Fragment" && byType(tree, "ui.Scrollable").length === 1, label + "the editor is the body of the dock's panel: its tabs and a page that scrolls");
    // the dock's panel with a sign's settings in it: titled with the board, a back button at the top left
    setUi(fresh);
    dock = draw(Dock);
    const editing = byType(dock, "ui.Panel")[0];
    const fhead = withClass(editing, "cdlts-fly-head")[0];
    check(!!editing && textOf(fhead).includes("路42") && textOf(fhead).includes("出口预告标志") && textOf(fhead).includes("Editing"), label + "a sign picked to edit is out at once, titled with the board");
    // 0.12.90: with the editor in it the window is the wide one (the fly-out's class and the panel's inline width); the list's stays as it was
    check(withClass(dock, "cdlts-fly")[0].props.className.includes("cdlts-fly-wide") && editing.props.style.width === "560rem", label + "the editor's window is the wide one: " + editing.props.style.width);
    const backArrows = found ? byType(fhead, "ui.game:ToolButton") : byType(fhead, "ui.Button");
    check(backArrows.length === 1 && backArrows[0].props.src === module.icons.back && true, label + "with the back button at its left");
    check(tabButtons(dock).every(b => !b.props.selected), label + "no tab is on while the settings are shown");
    check(byType(editing, "ui.Scrollable").length === 1 && withClass(editing, "cdlts-tile").length === 0, label + "the entries are not in the panel then");
    // the back button: a sign being edited goes back to the entries and stays as it is; a sign about to be placed is let go of
    take();
    backArrows[0].props.onSelect();
    check(ui.showList === true && take() === "[]", label + "back from an edited sign shows the entries and sends nothing");
    dock = draw(Dock);
    check(withClass(dock, "cdlts-tile").length > 0 && byType(dock, "ui.Panel")[0].props.onClose !== undefined && textOf(withClass(dock, "cdlts-fly-title")[0]).includes("路42") === false, label + "the entries, headed with the tab");
    // the settings of the sign again, then a tab: its entries come in their place
    setUi({ showList: false });
    dock = draw(Dock);
    tabButtons(dock)[0].props.onSelect();
    check(ui.flyout === true && ui.showList === true, label + "a tab while a sign is edited shows its entries too");
    dock = draw(Dock);
    withClass(dock, "cdlts-tile")[2].props.onSelect();
    check(ui.showList === false && take() === JSON.stringify([["Select", "t2"]]), label + "an entry chosen there goes back to the settings");
    const draftNow = Object.assign({}, editorJson, { mode: "draft", id: 8 });
    values.Editor = JSON.stringify(draftNow);
    dock = draw(Dock);
    check(textOf(withClass(dock, "cdlts-fly-title")[0]).includes("Settings") && !textOf(withClass(dock, "cdlts-fly-title")[0]).includes("Editing"), label + "a sign about to be placed is headed Settings");
    back();
    check(take() === JSON.stringify([["Leave"]]) && ui.showList === false, label + "back from a sign about to be placed leaves the tool");
    // Esc is the back button's (not in a text field, not when another handler took it)
    values.Editor = JSON.stringify(editorJson);
    setUi({ showList: false });
    draw(Dock);
    onEscape({ key: "Escape", target: { tagName: "INPUT" } });
    onEscape({ key: "Enter", target: { tagName: "DIV" } });
    onEscape({ key: "Escape", target: { tagName: "DIV" }, defaultPrevented: true });
    check(ui.showList === false && take() === "[]", label + "Esc in a text field, another key, or an Esc that was handled: nothing");
    onEscape({ key: "Escape", target: { tagName: "DIV" } });
    check(ui.showList === true, label + "Esc anywhere else goes back to the entries");
    onEscape(undefined);
    values.Editor = "";
    dock = draw(Dock);
    check(withClass(dock, "cdlts-fly-title").length === 0 || ui.flyout === true || byType(dock, "ui.Panel").length === 0, label + "no editor: the panel is out only if a tab was chosen");
    values.Editor = JSON.stringify(editorJson);
    setUi({ showList: false });
    const tabsOf = t => found ? byType(t, "ui.game:Tab") : (withClass(t, "cdlts-tabs-flat").length ? byType(withClass(t, "cdlts-tabs-flat")[0], "ui.Button") : []);
    const tabs = tabsOf(tree);
    check(tabs.length === 2 && (found ? tabs[0].props.selectedId === "sign" : tabs[0].props.selected), label + "an edited sign has two tabs, the sign's on");
    check(!found || (byType(tree, "ui.game:TabBar").length === 1 && byType(tree, "ui.game:TabBar")[0].props.className === "cdlts-tabs"), label + "the game's tab bar with the class that lets two tabs share the width");
    found ? tabs[1].props.onSelect() : tabs[1].props.onSelect();
    check(ui.editorTab === "support" && take() === "[]", label + "a tab turns the page and sends nothing to the game");
    setUi({ editorTab: "sign" });
    const img = withClass(tree, "cdlts-preview-img")[0];
    check(img && img.props.src === "coui://cdlcts/edit-3.png" && img.props.style.width === "176rem" && img.props.style.height === "200rem", label + "the board's picture, in proportion inside its box (400 by 200 rem since 0.12.90, 300 by 140 before): " + (img ? JSON.stringify(img.props.style) : "none"));
    check(byType(tree, "ui.Scrollable").length === 1, label + "the editor's body scrolls in the game's Scrollable");
    const roadRow = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "道路");
    const roadButtons = roadRow ? byType(roadRow, "ui.Button") : [];
    check(roadButtons.length === 3 && roadButtons[0].props.selected && roadRow.props.disableFocus === true, label + "a choice of three short options is three buttons beside its label (a passive row)");
    roadButtons[1].props.onSelect();
    check(take() === JSON.stringify([["EditField", "7|road=s"]]), label + "a choice sends its editor target and key=value");
    const codeRow = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "编号");
    const box = codeRow ? (found ? byType(codeRow, "ui.game:TextInput")[0] : byType(codeRow, "input")[0]) : null;
    check(!!box && (found ? box.props.value === "G204" : box.props.defaultValue === "G204") && box.props.maxLength === 5, label + "a text setting is a box with its value and length");
    box.props.onChange({ target: { value: "G1" }, isComposing: true });
    box.props.onBlur({ target: { value: "G15" } });
    check(take() === JSON.stringify([["EditField", "7|code=G15"]]), label + "a box sends what was typed when it loses the focus, once");
    box.props.onChange({ target: { value: "你" }, nativeEvent: { isComposing: true } });
    await new Promise(resolve => setTimeout(resolve, 420));
    check(take() === "[]", label + "nothing is sent while a word is being composed");
    box.props.onChange({ target: { value: "G3" } });
    await new Promise(resolve => setTimeout(resolve, 420));
    check(take() === JSON.stringify([["EditField", "7|code=G3"]]), label + "and a moment after the last key otherwise");
    // A text that counts clusters on the C# side comes with no length: the box never cuts a pair or a sequence by UTF-16 units, and shows and sends what was typed or pasted whole.
    const freeRow = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "自由文本");
    const freeBox = freeRow ? (found ? byType(freeRow, "ui.game:TextInput")[0] : byType(freeRow, "input")[0]) : null;
    check(!!freeBox && freeBox.props.maxLength === undefined && (found ? freeBox.props.value : freeBox.props.defaultValue) === "😀👨‍👩", label + "a text of clusters is a box with no length limit and its emoji whole");
    freeBox.props.onChange({ target: { value: "😀👍🏽" } });
    freeBox.props.onBlur({ target: { value: "😀👍🏽" } });
    check(take() === JSON.stringify([["EditField", "7|free=😀👍🏽"]]), label + "a pasted emoji with a skin tone is sent whole");
    const flagRow = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "直出车道");
    if (found) {
        const box2 = byType(flagRow, "ui.game:Checkbox")[0];
        check(!!box2 && box2.props.checked === false, label + "a switch is the game's checkbox");
        box2.props.onChange(true);
    } else {
        byType(flagRow, "ui.Button")[0].props.onSelect();
    }
    check(take() === JSON.stringify([["EditField", "7|lane=1"]]), label + "a switch sends 1 when it is turned on");

    // a number with a slider (0.12.76, a standing light box's tilt): the value in the head row, the game's slider sends the field on release (a draft is shown while it is dragged), two step buttons where the
    // game has no slider; a number with no slider stays a text box
    values.Editor = JSON.stringify(Object.assign({}, editorJson, { fields: [
        { k: "tilt", l: "Elevation angle", t: "text", v: "12", max: 2, r: [0, 30, 1] },
        { k: "lift", l: "Height", t: "text", v: "30", max: 3 }
    ] }));
    tree = draw(EditorPanel);
    const tiltHead = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "Elevation angle");
    check(!!tiltHead && tiltHead.props.right === "12", label + "a number with a range shows its value in the head row");
    check(!byType(tree, "ui.PanelSectionRow").some(n => n.props.left === "Elevation angle" && (byType(n, "ui.game:TextInput").length > 0 || byType(n, "input").length > 0)), label + "and has no text box beside it");
    check(byType(tree, "ui.PanelSectionRow").some(n => n.props.left === "Height") && byType(tree, "ui.game:Slider").length === (found ? 1 : 0), label + "a number with no range is as before, and only the ranged one has the slider");
    if (found) {
        const slider = byType(tree, "ui.game:Slider")[0].props;
        check(slider.start === 0 && slider.end === 30 && slider.value === 12 && slider.gamepadStep === 1, label + "the slider has the field's range, step and value");
        slider.onDragStart();
        slider.onChange(17.4);
        check(take() === "[]", label + "a drag sends nothing until it is let go");
        slider.onDragEnd();
        check(take() === JSON.stringify([["EditField", "7|tilt=17"]]), label + "and then the value at the field's step");
        byType(tree, "ui.game:Slider")[0].props.onChange(99);
        check(take() === JSON.stringify([["EditField", "7|tilt=30"]]), label + "a value over the range is held to it");
    } else {
        const stepRow = byType(tree, "ui.PanelSectionRow").find(n => n.props.key === "7:tilt-steps");
        const buttons = stepRow ? byType(stepRow, "ui.Button") : [];
        check(buttons.length === 2, label + "without the slider two step buttons");
        buttons[0].props.onSelect();
        buttons[1].props.onSelect();
        check(take() === JSON.stringify([["EditField", "7|tilt=11"], ["EditField", "7|tilt=13"]]), label + "step the value down and up");
    }

    values.Editor = JSON.stringify(Object.assign({}, editorJson, { fields: [{ k: "tilt", l: "Elevation angle", t: "text", v: "0", max: 2, r: [0, 30, 1] }] }));
    tree = draw(EditorPanel);
    if (!found) {
        byType(byType(tree, "ui.PanelSectionRow").find(n => n.props.key === "7:tilt-steps"), "ui.Button")[0].props.onSelect();
        check(take() === JSON.stringify([["EditField", "7|tilt=0"]]), label + "the least is not stepped below");
    }

    // long choices: the game's dropdown (or wrapping buttons); a long label puts the control under it
    values.Editor = JSON.stringify(Object.assign({}, editorJson, { fields: [
        { k: "speed", l: "设计速度（字高档）", t: "choice", v: "auto", max: 0, ch: [{ v: "auto", l: "自动（按所在道路的限速）" }, { v: "100", l: "100–120 km/h（字高 65 cm）" }, { v: "80", l: "71–99 km/h（字高 55 cm）" }] },
        { k: "long", l: "A label of a text field that is far too long to stand beside its box", t: "text", v: "", max: 5 },
        { k: "en", l: "Automatic speed band", t: "choice", v: "a", max: 0, ch: [{ v: "a", l: "Automatic (from the road's speed limit)" }, { v: "b", l: "100–120 km/h (char height 65 cm)" }] }
    ] }));
    tree = draw(EditorPanel);
    if (full) {
        const drops = byType(tree, "ui.Dropdown");
        check(drops.length === 2, label + "long choices are the game's dropdowns: " + drops.length);
        const items = byType({ props: {}, children: [drops[0].props.content] }, "ui.DropdownItem");
        check(items.length === 3 && items[0].props.selected && items.every(i => i.props.closeOnSelect), label + "with an item for each option, the current one selected");
        items[1].props.onChange("100");
        check(take() === JSON.stringify([["EditField", "7|speed=100"]]), label + "an item sends its value");
        check(textOf(byType(drops[0], "ui.DropdownToggle")[0]).includes("自动"), label + "the toggle shows the current option");
        check(byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "设计速度（字高档）").props.disableFocus === true, label + "the design speed stays beside its label");
        check(withClass(tree, "cdlts-toggle-wide").length === 1, label + "the English options are too long for that: the dropdown goes under the label, at the whole width");
    } else {
        check(withClass(tree, "cdlts-seg-wrap").length === 2, label + "without the game's dropdown the options are buttons that wrap");
    }
    check(withClass(tree, "cdlts-full").length >= 2, label + "a long label puts its box under it");
    const layout = module.choiceLayout;
    check(layout({ l: "方向", ch: [{ l: "向左" }, { l: "向右" }] }).kind === "buttons" && layout({ l: "方向", ch: [{ l: "1" }, { l: "2" }, { l: "3" }, { l: "4" }, { l: "5" }] }).kind === "dropdown", label + "five options are a dropdown, two short ones buttons");

    // the support's page: the boards, their lanes, moving them
    values.Selected = "";
    values.Editor = JSON.stringify(boardEditor);
    setUi({ editorTab: "support" });
    tree = draw(EditorPanel);
    const boardButtons = withClass(tree, "cdlts-board");
    check(boardButtons.length === 2 && boardButtons[0].props.selected && !boardButtons[1].props.selected, label + "a row for each board, the one being edited on");
    check(!byType(tree, "ui.PanelSectionRow").some(n => n.props.left === "转弯方向"), label + "and not the sign's settings");
    check(textOf(tree).includes("auto") && textOf(tree).includes("Lane 2") && byType(tree, "ui.PanelSectionRow").some(n => n.props.right === "2 / 4"), label + "where each board is, and how many of four");
    const iconButtons = t => found ? byType(t, "ui.game:ToolButton") : withClass(t, "cdlts-icon-btn");
    const removes = iconButtons(tree).filter(b => b.props.src === module.icons.trash);
    check(removes.length === 2 && (!found || removes[0].props.tooltip === "Remove"), label + "with two boards each can be taken off");
    boardButtons[1].props.onSelect();
    removes[1].props.onSelect();
    check(take() === JSON.stringify([["SelectBoard", 1], ["RemoveBoard", 1]]), label + "a row selects its board, the bin takes it off");
    check(!textOf(tree).includes("Choose a sign in the catalogue") && !textOf(tree).includes("Chosen:"), label + "with no sign chosen nothing is said about it");
    values.Selected = "t3";
    tree = draw(EditorPanel);
    const replaces = iconButtons(tree).filter(b => b.props.src === module.icons.replace);
    check(replaces.length === 2 && textOf(tree).includes("禁4 Sign 3"), label + "a sign chosen in the catalogue: a replace button on each board, and its name");
    replaces[0].props.onSelect();
    wideAction(tree, found, "Add to this support").props.onSelect();
    check(take() === JSON.stringify([["ReplaceBoard", 0], ["AddBoardHere"]]), label + "replace and add");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { boards: [0, 1, 2, 3].map(i => ({ m: i, code: "禁" + i, title: "t", lane: 0, x: 0 })) }));
    check(!textOf(draw(EditorPanel)).includes("Add the chosen"), label + "four of four: no more");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: true, boards: Array.from({ length: 6 }, (_, i) => ({ m: i, code: "示" + i, title: "t", lane: 0, x: i })), max: 12 }));
    tree = draw(EditorPanel);
    check(byType(tree, "ui.PanelSectionRow").some(n => n.props.right === "6 / 12") && textOf(tree).includes("Add to this support"), label + "a gantry takes twelve");

    // lanes
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: true, posts: true }));
    tree = draw(EditorPanel);
    const laneRow = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "Lane (from the left)");
    check(!!laneRow, label + "a lane row");
    if (full) {
        const items = byType({ props: {}, children: [byType(laneRow, "ui.Dropdown")[0].props.content] }, "ui.DropdownItem");
        check(items.map(i => textOf(i).trim()).join("|") === "Auto|Post|1|2|3" && items[0].props.selected, label + "automatic, the post and the lanes: " + items.map(i => textOf(i).trim()).join("|"));
        items[1].props.onChange("254");
        items[3].props.onChange("2");
    } else {
        const lb = byType(laneRow, "ui.Button");
        check(lb.map(b => textOf(b).trim()).join("|") === "Auto|Post|1|2|3", label + "automatic, the post and the lanes as buttons");
        lb[1].props.onSelect();
        lb[3].props.onSelect();
    }
    check(take() === JSON.stringify([["SetLane", 254], ["SetLane", 2]]), label + "the post is lane 254, a lane its number");
    check(!textOf(tree).includes("lane(s) face this support"), label + "the number of lanes is not said in a note: the dropdown lists them");
    wideAction(tree, found, "One lane").props.onSelect();
    check(take() === JSON.stringify([["SpreadLanes"]]), label + "one lane for each board");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: true, posts: true, laneState: "noroad", lanes: [] }));
    tree = draw(EditorPanel);
    const where = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "Where the board is");
    check(!!where && byType(where, "ui.Button").map(b => textOf(b).trim()).join("|") === "Auto|Post" && textOf(tree).includes("no lanes to read"), label + "no lanes read: automatic or the post, and a note");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { laneState: "post", lanes: [] }));
    tree = draw(EditorPanel);
    check(!byType(tree, "ui.PanelSectionRow").some(n => String(n.props.left).includes("Lane")), label + "a post has no lane row");

    // moving the board, the reach, moving the support
    const moveButtons = t => iconButtons(byType(t, "ui.PanelSectionRow").find(n => String(n.props.left).startsWith("Move ") || String(n.props.left).startsWith("Raise or lower")));
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: true }));
    let mv = moveButtons(draw(EditorPanel));
    check(mv.length === 2 && mv[0].props.src === module.icons.left && mv[1].props.src === module.icons.right, label + "a beam: left and right");
    mv.forEach(b => b.props.onSelect());
    check(take() === JSON.stringify([["NudgeBoard", -1], ["NudgeBoard", 1]]), label + "left is -1, right 1");
    // 0.12.73: a board hung on a beam also goes up and down from the clearance: a row of its own with the up and down buttons (the keys' codes -2 and 2), the one along the beam as it was
    const beamRows = byType(draw(EditorPanel), "ui.PanelSectionRow");
    const beamUp = iconButtons(beamRows.find(n => String(n.props.left).startsWith("Raise or lower")));
    check(beamRows.filter(n => String(n.props.left).startsWith("Move along the beam")).length === 1 && beamUp.length === 2 && beamUp[0].props.src === module.icons.up && beamUp[1].props.src === module.icons.down, label + "a beam: up and down from the clearance as well");
    beamUp.forEach(b => b.props.onSelect());
    check(take() === JSON.stringify([["NudgeBoard", -2], ["NudgeBoard", 2]]), label + "up is -2, down 2 on a beam too");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: false, laneState: "post", lanes: [] }));
    mv = moveButtons(draw(EditorPanel));
    mv.forEach(b => b.props.onSelect());
    check(mv.length === 2 && mv[0].props.src === module.icons.up && take() === JSON.stringify([["NudgeBoard", -2], ["NudgeBoard", 2]]), label + "a post: up is -2, down 2");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: true, posts: true, lane: 254 }));
    check(!byType(draw(EditorPanel), "ui.PanelSectionRow").some(n => String(n.props.left).startsWith("Move along the beam")), label + "a board on the post of a hung support is not moved along the beam");
    check(!byType(draw(EditorPanel), "ui.PanelSectionRow").some(n => String(n.props.left).startsWith("Raise or lower")), label + "nor raised from the clearance");
    // 0.12.57: a board on another mod's signal arm: left and right along the arm, up and down; the buttons that move a standing support are not there
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { onArm: true, hung: false, laneState: "post", lanes: [] }));
    tree = draw(EditorPanel);
    const armRows = byType(tree, "ui.PanelSectionRow");
    const armAlong = iconButtons(armRows.find(n => String(n.props.left).startsWith("Move along the arm"))), armUp = iconButtons(armRows.find(n => String(n.props.left).startsWith("Raise or lower")));
    check(armAlong.length === 2 && armAlong[0].props.src === module.icons.left && armAlong[1].props.src === module.icons.right && armUp.length === 2 && armUp[0].props.src === module.icons.up && armUp[1].props.src === module.icons.down,
        label + "an arm: left and right along it, up and down");
    armAlong.concat(armUp).forEach(b => b.props.onSelect());
    check(take() === JSON.stringify([["NudgeBoard", -1], ["NudgeBoard", 1], ["NudgeBoard", -2], ["NudgeBoard", 2]]), label + "the arm's four buttons send the keys' codes");
    check(!armRows.some(n => String(n.props.left).startsWith("Along the road")), label + "a board on an arm has no support-move rows (along the road)");

    // turning a board on its post (0.12.8): the row only for a board that turns. 0.12.80: the facing is a number to a tenth in a box, the game's slider sets it on release (two buttons where it is not
    // found), the buttons step it, half a turn and back to the support's facing; the sign about to be placed has the same row
    const turnRow = t => byType(t, "ui.PanelSectionRow").find(n => String(n.props.left).startsWith("Facing"));
    const turnSection = tree => byType(tree, "ui.PanelSection").some(n => n.props.key === "turn");
    const turnBoxOf = row => row ? (found ? byType(row, "ui.game:TextInput")[0] : byType(row, "input")[0]) : null;
    const turnMoreOf = t => byType(byType(t, "ui.PanelSectionRow").find(n => n.props.left === "" && textOf(n).includes("Turn round")), "ui.Button");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: false, laneState: "post", lanes: [] }));
    check(!turnRow(draw(EditorPanel)), label + "a board that does not turn has no turn row");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: false, laneState: "post", lanes: [], turns: true, turn: 30.4 }));
    tree = draw(EditorPanel);
    const turning = turnRow(tree);
    const turnBox = turnBoxOf(turning);
    check(!!turning && !!turnBox && (found ? turnBox.props.value : turnBox.props.defaultValue) === "30.4", label + "the turn row has the facing now, to a tenth, in a box");
    check(String(turnBox.props.className).split(/\s+/).includes("cdlts-num"), label + "the box is a few digits wide");
    turnBox.props.onChange({ target: { value: "-37.5" } });
    await new Promise(resolve => setTimeout(resolve, 420));
    check(take() === "[]", label + "a typed facing is sent when the box loses the focus, not while it is typed");
    turnBox.props.onBlur({ target: { value: "-37.5" } });
    turnBox.props.onBlur({ target: { value: "abc" } });
    turnBox.props.onBlur({ target: { value: "200" } });
    check(take() === JSON.stringify([["SetTurn", -37.5], ["SetTurn", 200]]), label + "typed: the angle as typed (the game wraps 200 to -160); not a number: nothing");
    const turnSliders = byType(tree, "ui.game:Slider");
    check(turnSliders.length === (found ? 1 : 0), label + "the turn has the game's slider where its module is found, none where it is not");
    const turnStepRow = byType(tree, "ui.PanelSectionRow").find(n => n.props.key === "t-steps");
    const turnButtons = turnStepRow ? byType(turnStepRow, "ui.Button") : [];
    if (found) {
        const slider = turnSliders[0].props;
        check(slider.start === -180 && slider.end === 179.9 && slider.gamepadStep === 0.1 && slider.value === 30.4, label + "the turn's slider: half a turn back to 179.9, tenths, at the facing now");
        slider.onChange(41.2300001);
        check(take() === JSON.stringify([["SetTurn", 41.2]]), label + "a change outside a drag is sent at once, in tenths");
        slider.onDragStart();
        slider.onChange(50.04);
        slider.onChange(50.26);
        check(take() === "[]", label + "during a drag nothing is sent");
        slider.onDragEnd();
        check(take() === JSON.stringify([["SetTurn", 50.3]]), label + "the last value of a drag is sent on release, one commit");
        slider.onChange(999);
        slider.onChange(-999);
        check(take() === JSON.stringify([["SetTurn", 179.9], ["SetTurn", -180]]), label + "past the ends: the ends");
        check(turnButtons.length === 4, label + "with the slider the four steps are beside it");
    } else {
        check(turnButtons.length === 6, label + "without the slider two tenth buttons and the four steps");
        turnButtons[0].props.onSelect();
        turnButtons[1].props.onSelect();
        check(take() === JSON.stringify([["SetTurn", 30.3], ["SetTurn", 30.5]]), label + "the buttons step a tenth each way");
    }

    turnButtons.slice(-4).forEach(b => b.props.onSelect());
    check(take() === JSON.stringify([-90, -15, 15, 90].map(d => ["TurnBoard", d])), label + "the steps of the turn in degrees");
    const turnMore = turnMoreOf(tree);
    turnMore.forEach(b => b.props.onSelect());
    check(turnMore.length === 2 && take() === JSON.stringify([["TurnBoard", 180], ["TurnBoard", 0]]), label + "half a turn, and back to the support's facing");
    check(!turnMore[1].props.selected, label + "a turned board is not marked as facing with the support");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: false, laneState: "post", lanes: [], turns: true, turn: 0 }));
    tree = draw(EditorPanel);
    check((found ? turnBoxOf(turnRow(tree)).props.value : turnBoxOf(turnRow(tree)).props.defaultValue) === "0" && turnMoreOf(tree)[1].props.selected === true, label + "unturned: 0 and marked as facing with the support");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: false, laneState: "post", lanes: [], turns: true, turn: -45 }));
    check((found ? turnBoxOf(turnRow(draw(EditorPanel))).props.value : turnBoxOf(turnRow(draw(EditorPanel))).props.defaultValue) === "-45", label + "a turn the other way has its sign");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: false, laneState: "post", lanes: [], turns: true, turn: -180 }));
    tree = draw(EditorPanel);
    check(!found || byType(tree, "ui.game:Slider")[0].props.value === -180, label + "half a turn stands at the left end of the slider");
    // the sign about to be placed (mode "draft") has the row when its support takes a rotation, and none when it does not (a wall, a signal's arm, a marking)
    const placing = Object.assign({}, boardEditor, { mode: "draft", boards: [], hung: false, laneState: "post", lanes: [], turns: true, turn: 15 });
    values.Editor = JSON.stringify(placing);
    tree = draw(EditorPanel);
    check(!!turnRow(tree) && (found ? turnBoxOf(turnRow(tree)).props.value : turnBoxOf(turnRow(tree)).props.defaultValue) === "15", label + "the sign about to be placed has the turn row, at the rotation now");
    check(turnSection(tree), label + "the turn row has its section while placing");
    values.Editor = JSON.stringify(Object.assign({}, placing, { turns: false }));
    tree = draw(EditorPanel);
    check(!turnRow(tree) && !turnSection(tree), label + "a placing that takes no rotation has no turn row, and no empty section for it");
    values.Editor = JSON.stringify(Object.assign({}, placing, { marking: true }));
    tree = draw(EditorPanel);
    check(!turnRow(tree) && !turnSection(tree), label + "a marking has no turn row");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: false, laneState: "post", lanes: [], turns: true, turn: 30 }));
    draw(EditorPanel);
    take();

    // 0.12.31: the F type has its boards on one level (no arm row: 0.12.26 to 0.12.30 had rows of boards)
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: true, rows: 3, row: 2 }));
    check(!byType(draw(EditorPanel), "ui.PanelSectionRow").some(n => String(n.props.left).startsWith("This board is on arm")), label + "no arm row, whatever an old editor text says");
    // 0.12.78: the span of a gantry (the owner: where the road is read wrong the player sets it by hand): the box sends when it loses the focus, the game's slider on release (two buttons where it is not found),
    // and one button gives the planner's length back, marked while the length is the planner's
    const spanEditor = Object.assign({}, boardEditor, { hung: true, reach: 18.4, reachKind: "span", reachMin: 6, reachMax: 40, reachStep: 0.5, reachHand: false });
    values.Editor = JSON.stringify(spanEditor);
    tree = draw(EditorPanel);
    const reachRow = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "Span (m)");
    const reachBox = reachRow ? (found ? byType(reachRow, "ui.game:TextInput")[0] : byType(reachRow, "input")[0]) : null;
    check(!!reachBox && (found ? reachBox.props.value : reachBox.props.defaultValue) === "18.4", label + "the span in metres");
    check(String(reachBox.props.className).split(/\s+/).includes("cdlts-num") && /\.cdlts-text\.cdlts-num\s*\{[^}]*width:\s*\d+rem/.test(readFileSync(target.style, "utf8")), label + "the box is a few digits wide, not the row's width");
    reachBox.props.onChange({ target: { value: "22.5" } });
    await new Promise(resolve => setTimeout(resolve, 420));
    check(take() === "[]", label + "a span is sent when the box loses the focus, not while it is typed");
    reachBox.props.onBlur({ target: { value: "22.5" } });
    reachBox.props.onBlur({ target: { value: "abc" } });
    check(take() === JSON.stringify([["SetSpan", 22.5], ["SetSpan", 0]]), label + "typed: the metres; not a number: 0");
    const spanSliders = byType(tree, "ui.game:Slider");
    const spanFitRow = byType(tree, "ui.PanelSectionRow").find(n => n.props.key === "r-steps");
    const spanButtons = spanFitRow ? byType(spanFitRow, "ui.Button") : [];
    check(spanSliders.length === (found ? 1 : 0), label + "the span has the game's slider where its module is found, none where it is not");
    if (found) {
        const slider = spanSliders[0].props;
        check(slider.start === 6 && slider.end === 40 && slider.gamepadStep === 0.5 && slider.value === 18.4, label + "the span's slider has the layout's range (6 to 40 m), the half metre and the span now");
        slider.onChange(22.500000001);
        check(take() === JSON.stringify([["SetSpan", 22.5]]), label + "a change outside a drag is sent at once, in tenths");
        slider.onDragStart();
        slider.onChange(30.04);
        slider.onChange(30.26);
        check(take() === "[]", label + "during a drag nothing is sent");
        slider.onDragEnd();
        check(take() === JSON.stringify([["SetSpan", 30.3]]), label + "the last value of a drag is sent on release");
        slider.onChange(99);
        slider.onChange(2);
        check(take() === JSON.stringify([["SetSpan", 40], ["SetSpan", 6]]), label + "past the ends of the range: the ends");
        check(spanButtons.length === 1, label + "with the slider only the fit button is beside it");
    } else {
        check(spanButtons.length === 3, label + "without the slider two step buttons and the fit button");
        spanButtons[0].props.onSelect();
        spanButtons[1].props.onSelect();
        check(take() === JSON.stringify([["SetSpan", 17.9], ["SetSpan", 18.9]]), label + "the buttons step 0.5 m each way");
    }
    const spanFit = spanButtons[spanButtons.length - 1];
    check(!!spanFit && textOf(spanFit).includes("Fit the road") && spanFit.props.selected === true, label + "the fit button is marked while the length is the planner's");
    spanFit.props.onSelect();
    check(take() === JSON.stringify([["FitSpan"]]), label + "the fit button gives the planner's length back (FitSpan, no number)");
    values.Editor = JSON.stringify(Object.assign({}, spanEditor, { reachHand: true }));
    tree = draw(EditorPanel);
    const handFit = byType(tree, "ui.Button").filter(b => textOf(b).includes("Fit the road"));
    check(handFit.length === 1 && handFit[0].props.selected === false, label + "a span set by hand is not marked as the planner's");
    // a span far from the road's (the planner read it wrong): the box and the slider show the one set, and the ends of the range are the editor's
    values.Editor = JSON.stringify(Object.assign({}, spanEditor, { reach: 40, reachHand: true }));
    tree = draw(EditorPanel);
    check(!found || byType(tree, "ui.game:Slider")[0].props.value === 40, label + "the slider stands at the end of its range for the longest span");
    values.Editor = JSON.stringify(Object.assign({}, spanEditor, { reach: 0 }));
    tree = draw(EditorPanel);
    check(!found || byType(tree, "ui.game:Slider")[0].props.value === 6, label + "a span not read yet (0) is held to the shortest on the slider");
    // 0.12.78: the clearance under a hung support (the owner: a height slider for the gantry, a height-limit frame at a tunnel): its own row under the span's, the same box and slider, tenths from 3 to 9 m;
    // ONE Auto button (Fit the road) in the clearance's row gives both back, marked while neither is set by hand
    const gantryEditor = Object.assign({}, spanEditor, { clear: 5.5, clearMin: 3, clearMax: 9, clearStep: 0.1, clearHand: false });
    values.Editor = JSON.stringify(gantryEditor);
    tree = draw(EditorPanel);
    const clearHead = byType(tree, "ui.PanelSectionRow").find(n => n.props.key === "c-head");
    const clearBox = clearHead ? (found ? byType(clearHead, "ui.game:TextInput")[0] : byType(clearHead, "input")[0]) : null;
    check(!!clearHead && clearHead.props.left === "Clearance (m)" && !!clearBox && (found ? clearBox.props.value : clearBox.props.defaultValue) === "5.5", label + "the clearance row: its label, the height in metres");
    check(String(clearBox.props.className).split(/\s+/).includes("cdlts-num"), label + "the clearance's box is as narrow as the span's");
    clearBox.props.onBlur({ target: { value: "4.5" } });
    clearBox.props.onBlur({ target: { value: "abc" } });
    check(take() === JSON.stringify([["SetClearance", 4.5], ["SetClearance", 0]]), label + "typed: the metres; not a number: 0 (the support's own)");
    const clearSliders = byType(tree, "ui.game:Slider");
    check(clearSliders.length === (found ? 2 : 0), label + "a slider for the span and one for the clearance where the module is found");
    const autoButtons = byType(tree, "ui.Button").filter(b => textOf(b).includes("Fit the road"));
    const clearSteps = byType(tree, "ui.PanelSectionRow").find(n => n.props.key === "c-steps");
    check(autoButtons.length === 1 && !!clearSteps && byType(clearSteps, "ui.Button").includes(autoButtons[0]), label + "one Auto button for both, in the clearance's row");
    if (found) {
        const slider = clearSliders[1].props;
        check(slider.start === 3 && slider.end === 9 && slider.gamepadStep === 0.1 && slider.value === 5.5, label + "the clearance's slider: 3 to 9 m, tenths, at the height now");
        check(clearSliders[0].props.start === 6 && clearSliders[0].props.end === 40, label + "the first slider is still the span's");
        slider.onChange(4.500000001);
        check(take() === JSON.stringify([["SetClearance", 4.5]]), label + "a change outside a drag is sent at once, in tenths");
        slider.onDragStart();
        slider.onChange(7.04);
        slider.onChange(7.26);
        check(take() === "[]", label + "during a drag nothing is sent");
        slider.onDragEnd();
        check(take() === JSON.stringify([["SetClearance", 7.3]]), label + "the last value of a drag is sent on release");
        slider.onChange(99);
        slider.onChange(0.5);
        check(take() === JSON.stringify([["SetClearance", 9], ["SetClearance", 3]]), label + "past the ends of the range: the ends");
        clearSliders[0].props.onChange(20);
        check(take() === JSON.stringify([["SetSpan", 20]]), label + "the span's slider still sets the span");
    } else {
        const stepButtons = byType(clearSteps, "ui.Button");
        check(stepButtons.length === 3 && byType(byType(tree, "ui.PanelSectionRow").find(n => n.props.key === "r-steps"), "ui.Button").length === 2, label + "without the slider: two step buttons for the span, two and Auto for the clearance");
        stepButtons[0].props.onSelect();
        stepButtons[1].props.onSelect();
        check(take() === JSON.stringify([["SetClearance", 5.4], ["SetClearance", 5.6]]), label + "the clearance's buttons step 0.1 m each way");
    }
    check(autoButtons[0].props.selected === true, label + "Auto is marked while neither the span nor the clearance is set by hand");
    autoButtons[0].props.onSelect();
    check(take() === JSON.stringify([["FitSpan"]]), label + "Auto sends FitSpan, no number: the one trigger gives both back");
    values.Editor = JSON.stringify(Object.assign({}, gantryEditor, { clearHand: true }));
    check(byType(draw(EditorPanel), "ui.Button").filter(b => textOf(b).includes("Fit the road")).every(b => b.props.selected === false), label + "a clearance set by hand: Auto is not marked");
    values.Editor = JSON.stringify(Object.assign({}, gantryEditor, { clear: 3, clearHand: true }));
    tree = draw(EditorPanel);
    check(!found || byType(tree, "ui.game:Slider")[1].props.value === 3, label + "the lowest clearance: the slider at its start");
    values.Editor = JSON.stringify(Object.assign({}, gantryEditor, { clear: 0 }));
    tree = draw(EditorPanel);
    check(!found || byType(tree, "ui.game:Slider")[1].props.value === 3, label + "a clearance not read (0) is held to the lowest on the slider");
    // an arm has it too; the one about to be placed (draft) has Auto marked until something is set; a double cantilever that stands has Auto for the clearance only
    values.Editor = JSON.stringify(Object.assign({}, gantryEditor, { mode: "draft", reachKind: "arm", reachMin: 1, reachMax: 16 }));
    tree = draw(EditorPanel);
    check(byType(tree, "ui.PanelSectionRow").some(n => n.props.key === "c-head") && byType(tree, "ui.Button").filter(b => textOf(b).includes("Fit the road")).length === 1, label + "a cantilever being placed: the clearance row and one Auto");
    values.Editor = JSON.stringify(Object.assign({}, gantryEditor, { mode: "item", reachKind: "arms", reachMin: 1, reachMax: 16 }));
    tree = draw(EditorPanel);
    check(byType(tree, "ui.PanelSectionRow").some(n => n.props.key === "c-head") && byType(tree, "ui.Button").filter(b => textOf(b).includes("Fit the road")).length === 1, label + "a double cantilever that stands: the clearance row and Auto");
    values.Editor = JSON.stringify(Object.assign({}, spanEditor, { clearMax: 0 }));
    check(!byType(draw(EditorPanel), "ui.PanelSectionRow").some(n => n.props.key === "c-head"), label + "a model that has no clearance range has no clearance row");
    // 0.12.55: the lean of an overhead support's boards: a button for each step, the present one marked; none on a support that does not lean
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: true, leans: true, tilt: 10 }));
    tree = draw(EditorPanel);
    const tiltRow = byType(tree, "ui.PanelSectionRow").find(n => String(n.props.left).startsWith("Lean forward"));
    const tiltButtons = tiltRow ? byType(tiltRow, "ui.Button") : [];
    check(tiltButtons.length === 4 && tiltButtons.map(b => !!b.props.selected).join() === "false,false,true,false", label + "the lean's four steps, 10° marked");
    tiltButtons.forEach(b => b.props.onSelect());
    check(take() === JSON.stringify([0, 5, 10, 15].map(d => ["SetTilt", d])), label + "the lean in degrees");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: false, leans: false }));
    check(!byType(draw(EditorPanel), "ui.PanelSectionRow").some(n => String(n.props.left).startsWith("Lean forward")), label + "a support that does not lean has no lean row");
    // 0.12.57: the finish and the black backs
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { painted: true, finish: 1, blackBack: false }));
    tree = draw(EditorPanel);
    const finishRow = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "The support's finish");
    const finishButtons = finishRow ? byType(finishRow, "ui.Button") : [];
    check(finishButtons.length === 4 && finishButtons.map(b => !!b.props.selected).join() === "false,true,false,false", label + "four finishes, graphite marked");
    finishButtons.forEach(b => b.props.onSelect());
    check(take() === JSON.stringify([0, 1, 2, 3].map(f => ["SetFinish", f])), label + "the finish by its number");
    const backRow = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "The boards' backs");
    const backButtons = backRow ? byType(backRow, "ui.Button") : [];
    check(backButtons.length === 2 && backButtons[0].props.selected && !backButtons[1].props.selected, label + "the backs as the steel, marked");
    backButtons.forEach(b => b.props.onSelect());
    check(take() === JSON.stringify([["SetBlackBack", false], ["SetBlackBack", true]]), label + "the backs set as the steel or black");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { painted: true, finish: 0, root: 1 }));
    tree = draw(EditorPanel);
    const rootRow = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "The posts' root");
    const rootButtons = rootRow ? byType(rootRow, "ui.Button") : [];
    check(rootButtons.length === 3 && rootButtons.map(b => !!b.props.selected).join() === "false,true,false", label + "three root paints, the yellow and black marked");
    rootButtons.forEach(b => b.props.onSelect());
    check(take() === JSON.stringify([0, 1, 2].map(r => ["SetRootPaint", r])), label + "the root paint by its number");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { painted: false }));
    check(!byType(draw(EditorPanel), "ui.PanelSectionRow").some(n => n.props.left === "The support's finish"), label + "a marking (nothing painted) has no finish row");
    const armsEditor = Object.assign({}, boardEditor, { hung: true, reach: 5, reachKind: "arms", reachMin: 1, reachMax: 16, reachStep: 0.5, reachHand: true });
    values.Editor = JSON.stringify(armsEditor);
    tree = draw(EditorPanel);
    check(byType(tree, "ui.PanelSectionRow").some(n => n.props.left === "Each arm (m)") && !byType(tree, "ui.Button").some(b => textOf(b).includes("Fit the road")), label + "a double cantilever that stands is not fitted to the road");
    check(!found || (byType(tree, "ui.game:Slider")[0].props.start === 1 && byType(tree, "ui.game:Slider")[0].props.end === 16), label + "an arm's slider is 1 to 16 m");
    // the one being placed has the planner's length to come back to, whatever the kind
    values.Editor = JSON.stringify(Object.assign({}, armsEditor, { mode: "draft" }));
    tree = draw(EditorPanel);
    const draftFit = byType(tree, "ui.Button").filter(b => textOf(b).includes("Fit the road"));
    check(draftFit.length === 1 && draftFit[0].props.selected === false, label + "an arm about to be placed has the button for the planner's length, not marked while it is set by hand");
    draftFit[0].props.onSelect();
    check(take() === JSON.stringify([["FitSpan"]]), label + "and it clears the length set for the next one");
    values.Editor = JSON.stringify(Object.assign({}, armsEditor, { reachKind: "arm", reachHand: false }));
    tree = draw(EditorPanel);
    check(byType(tree, "ui.PanelSectionRow").some(n => n.props.left === "Arm (m)") && byType(tree, "ui.Button").some(b => textOf(b).includes("Fit the road") && b.props.selected === true), label + "a cantilever's arm: its own label, fitted to the road, marked while it is the planner's");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: true, reach: 0, reachKind: "" }));
    tree = draw(EditorPanel);
    check(!byType(tree, "ui.PanelSectionRow").some(n => n.props.key === "r-head") && !byType(tree, "ui.Button").some(b => textOf(b).includes("Fit the road")), label + "a support with no length to set has no span rows");
    const along = byType(byType(tree, "ui.PanelSectionRow").find(n => String(n.props.left).startsWith("Along the road")), "ui.Button");
    const across = byType(byType(tree, "ui.PanelSectionRow").find(n => String(n.props.left).startsWith("Across")), "ui.Button");
    along.concat(across).forEach(b => b.props.onSelect());
    check(take() === JSON.stringify([-3, -1, 1, 3, -4, -2, 2, 4].map(c => ["MoveSupport", c])), label + "the support moves along and across: the codes");
    // a placed post (0.12.74): the same row in the editor's move rows, setting the post that stands (SetKerb), only when the editor says it can
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: false, laneState: "post", lanes: [], kerb: -1.3 }));
    tree = draw(EditorPanel);
    const placedHead = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "Distance from the kerb");
    const placedBox = placedHead ? (found ? byType(placedHead, "ui.game:TextInput")[0] : byType(placedHead, "input")[0]) : null;
    check(!!placedBox && (found ? placedBox.props.value : placedBox.props.defaultValue) === "-1.3", label + "a placed post shows the margin it stands at, in a box (0.12.85)");
    if (placedBox) {
        placedBox.props.onBlur({ target: { value: "-0.8" } });
        placedBox.props.onBlur({ target: { value: "abc" } });
        check(take() === JSON.stringify([["SetKerb", -0.8]]), label + "typed: the margin as typed through SetKerb; not a number: nothing");
    }
    if (found) {
        byType(tree, "ui.game:Slider")[0].props.onChange(0.44);
        check(take() === JSON.stringify([["SetKerb", 0.4]]), label + "the slider of a placed post sets it through SetKerb");
    }
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: false, laneState: "post", lanes: [] }));
    check(!byType(draw(EditorPanel), "ui.PanelSectionRow").some(n => n.props.left === "Distance from the kerb"), label + "a support that is not moved by a margin (no kerb in the editor) has no such row");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: false, laneState: "post", lanes: [], wall: 1, kerb: -1.3 }));
    check(!byType(draw(EditorPanel), "ui.PanelSectionRow").some(n => n.props.left === "Distance from the kerb"), label + "nor a wall's");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { hung: false, laneState: "post", lanes: [], wall: 2 }));
    tree = draw(EditorPanel);
    check(byType(tree, "ui.PanelSectionRow").some(n => String(n.props.left).startsWith("Along the wall")) && byType(tree, "ui.PanelSectionRow").some(n => String(n.props.left).startsWith("Up or down")), label + "on a wall: along it and up or down");
    // 0.12.80: a wall board's height has buttons of 10 m too: a tower of 150 m is climbed in fifteen presses, and nothing else of the row changes (a post's is the four it had)
    take();
    byType(byType(tree, "ui.PanelSectionRow").find(n => String(n.props.left).startsWith("Up or down")), "ui.Button").forEach(b => b.props.onSelect());
    check(take() === JSON.stringify([-6, -4, -2, 2, 4, 6].map(c => ["MoveSupport", c])), label + "a wall board's height: 10, 1 and 0.1 m each way");
    wideAction(tree, found, "other way").props.onSelect();
    check(take() === JSON.stringify([["MoveSupport", 5]]), label + "a bracket's board is turned with MoveSupport 5");

    // 0.12.85: the position of a support that stands, by a slider and a box: along its road or wall (m from where it stood when it was picked), from the road's centre line, the lower edge of a board
    // on a wall; each only when the editor carries the number (a support fixed to a host or a structure has none), the step buttons beside them as they were
    const shiftRow = (tr, start) => byType(tr, "ui.PanelSectionRow").find(n => String(n.props.left).startsWith(start));
    const shiftBox = row => row ? (found ? byType(row, "ui.game:TextInput")[0] : byType(row, "input")[0]) : null;
    const boxValue = box => box ? (found ? box.props.value : box.props.defaultValue) : null;
    const shiftEditor = extra => JSON.stringify(Object.assign({}, boardEditor, { hung: false, laneState: "post", lanes: [] }, extra));
    values.Editor = shiftEditor({ along: 3.5 });
    tree = draw(EditorPanel);
    const alongHead = shiftRow(tree, "Along the road");
    const alongBox = shiftBox(alongHead);
    check(!!alongHead && boxValue(alongBox) === "3.5" && String(alongBox.props.className).split(/\s+/).includes("cdlts-num"), label + "a support that stands has the position along in a box on the row of its label, to a tenth");
    const alongSteps = byType(tree, "ui.PanelSectionRow").find(n => n.props.key === "al-steps");
    take();
    byType(alongSteps, "ui.Button").filter(b => !/^[−+]0\.1$/.test(textOf(b).trim())).forEach(b => b.props.onSelect());
    check(take() === JSON.stringify([-3, -1, 1, 3].map(c => ["MoveSupport", c])) && !!shiftRow(tree, "Across"), label + "the four step buttons are in the same block (the same codes), and the across buttons stay");
    alongBox.props.onChange({ target: { value: "12.4" } });
    await new Promise(resolve => setTimeout(resolve, 420));
    check(take() === "[]", label + "a typed position is sent when the box loses the focus, not while it is typed");
    alongBox.props.onBlur({ target: { value: "12.4" } });
    alongBox.props.onBlur({ target: { value: "abc" } });
    alongBox.props.onBlur({ target: { value: "-80" } });
    check(take() === JSON.stringify([["SetAlong", 12.4], ["SetAlong", -80]]), label + "typed: the position as typed, past the slider's 50 m too (the tool holds it at 500); not a number: nothing");
    if (found) {
        const slider = byType(tree, "ui.game:Slider")[0].props;
        check(slider.start === -50 && slider.end === 50 && slider.gamepadStep === 0.1 && slider.value === 3.5, label + "the slider goes 50 m each way, in tenths, at the position now");
        slider.onChange(8.04);
        slider.onChange(75);
        slider.onChange(-75);
        check(take() === JSON.stringify([["SetAlong", 8], ["SetAlong", 50], ["SetAlong", -50]]), label + "a change outside a drag is sent at once, held at the ends");
        slider.onDragStart();
        slider.onChange(20.04);
        slider.onChange(21.26);
        check(take() === "[]", label + "during a drag nothing is sent");
        slider.onDragEnd();
        check(take() === JSON.stringify([["SetAlong", 21.3]]), label + "the last value of a drag is sent on release, one commit");
    }

    values.Editor = shiftEditor({ along: 3.5, kerb: -1.3 });
    tree = draw(EditorPanel);
    check(!!shiftRow(tree, "Along the road") && !!shiftRow(tree, "Distance from the kerb") && !shiftRow(tree, "From the centre line") && byType(tree, "ui.game:Slider").length === (found ? 2 : 0), label + "a post at the kerb: along and the margin, no distance from the centre line");
    values.Editor = shiftEditor({ along: 3.5, centre: 7.2, centreMax: 14 });
    tree = draw(EditorPanel);
    const centreHead = shiftRow(tree, "From the centre line");
    const centreBox = shiftBox(centreHead);
    check(!!centreHead && boxValue(centreBox) === "7.2" && !shiftRow(tree, "Distance from the kerb"), label + "a support the margin does not describe has the distance from the centre line, and no margin");
    take();
    centreBox.props.onBlur({ target: { value: "5.55" } });
    centreBox.props.onBlur({ target: { value: "x" } });
    check(take() === JSON.stringify([["SetCentre", 5.55]]), label + "typed: the distance as typed (the tool rounds it); not a number: nothing");
    if (found) {
        const slider = byType(tree, "ui.game:Slider")[1].props;
        check(slider.start === 0 && slider.end === 14 && slider.value === 7.2, label + "its slider goes from the centre line to the editor's furthest");
        slider.onChange(-3);
        slider.onChange(99);
        check(take() === JSON.stringify([["SetCentre", 0], ["SetCentre", 14]]), label + "held at the centre line and at the end");
    }

    values.Editor = shiftEditor({ wall: 1, along: 1.2, lower: 3.5 });
    tree = draw(EditorPanel);
    const wallAlongBox = shiftBox(shiftRow(tree, "Along the wall"));
    const lowerBox = shiftBox(shiftRow(tree, "Lower edge height"));
    check(boxValue(wallAlongBox) === "1.2" && boxValue(lowerBox) === "3.5" && !shiftRow(tree, "Along the road") && !shiftRow(tree, "Distance from the kerb"), label + "a board on a wall: the position along the wall and its lower edge, in boxes");
    take();
    wallAlongBox.props.onBlur({ target: { value: "-4" } });
    lowerBox.props.onBlur({ target: { value: "150" } });
    lowerBox.props.onBlur({ target: { value: "" } });
    check(take() === JSON.stringify([["SetAlong", -4], ["SetLower", 150]]), label + "typed: along the wall, and the lower edge as high as a tower (the tool holds it at the ceiling); a box left empty: nothing");
    if (found) {
        const sliders = byType(tree, "ui.game:Slider").map(s => s.props);
        check(sliders.length === 2 && sliders[0].start === -50 && sliders[1].start === 0.2 && sliders[1].end === 60 && sliders[1].value === 3.5, label + "the lower edge's slider goes from 0.2 to 60 m");
        sliders[1].onChange(70);
        check(take() === JSON.stringify([["SetLower", 60]]), label + "held at the slider's end");
    }

    // each row is gated by what its support is, besides the editor carrying its number
    values.Editor = shiftEditor({ wall: 1, along: 1.2, lower: 3.5, centre: 7.2, centreMax: 14 });
    check(!shiftRow(draw(EditorPanel), "From the centre line"), label + "a board on a wall has no distance from the centre line, whatever the editor carries");
    values.Editor = shiftEditor({ along: 3.5, lower: 3.5, kerb: -1.3 });
    check(!shiftRow(draw(EditorPanel), "Lower edge height"), label + "a support off a wall has no lower edge row, whatever the editor carries");
    values.Editor = shiftEditor({});
    tree = draw(EditorPanel);
    check(!shiftBox(shiftRow(tree, "Along the road")) && byType(shiftRow(tree, "Along the road"), "ui.Button").length === 4 && !shiftRow(tree, "From the centre line") && !shiftRow(tree, "Lower edge height"),
        label + "a support the controls do not move (fixed to a host or a structure) has the four step buttons as before and none of the new rows");
    values.Editor = shiftEditor({ onArm: true, along: 3.5 });
    check(!shiftRow(draw(EditorPanel), "Along the road"), label + "a board on another mod's arm keeps its own move buttons only");
    take();
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { mode: "draft" }));
    setUi({ editorTab: "sign" });
    tree = draw(EditorPanel);
    // 0.12.58: a sign about to be placed has the support's page too: the finish, the root paint and the lean are set before it is placed; nothing of the boards or of moving it
    check(!byType(tree, "ui.PanelSectionRow").some(n => String(n.props.left).startsWith("Along")) && tabsOf(tree).length === 2 && textOf(tabsOf(tree)[1]).includes("Support") && !textOf(tabsOf(tree)[1]).includes("boards"), label + "a draft: two pages, its support's and the sign's; nothing to move");
    check(byType(tree, "ui.PanelSectionRow").some(n => n.props.left === "转弯方向"), label + "the sign's settings are on the first page");
    setUi({ editorTab: "support" });
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { mode: "draft", painted: true, finish: 1, blackBack: true, root: 2, leans: true, tilt: 5, boards: [], supportInfo: ["立架 单柱式，牌下缘距地 2.3 m"] }));
    tree = draw(EditorPanel);
    const draftRows = byType(tree, "ui.PanelSectionRow");
    check(draftRows.some(n => n.props.left === "The support's finish") && draftRows.some(n => n.props.left === "The boards' backs") && draftRows.some(n => n.props.left === "The posts' root") && draftRows.some(n => String(n.props.left).startsWith("Lean forward")) && textOf(tree).includes("立架 单柱式"),
        label + "its page: the finish, the backs, the root paint, the lean, and what the support is");
    check(!draftRows.some(n => String(n.props.left).startsWith("Move ")) && !draftRows.some(n => String(n.props.left).startsWith("Lane")) && !draftRows.some(n => String(n.props.left).startsWith("Along")), label + "and nothing of boards, lanes or moving a support that is not there yet");
    iconButtons(draftRows.find(n => n.props.left === "The support's finish"));
    byType(draftRows.find(n => n.props.left === "The support's finish"), "ui.Button").forEach(b => b.props.onSelect());
    byType(draftRows.find(n => n.props.left === "The posts' root"), "ui.Button").forEach(b => b.props.onSelect());
    check(take() === JSON.stringify([["SetFinish", 0], ["SetFinish", 1], ["SetFinish", 2], ["SetFinish", 3], ["SetRootPaint", 0], ["SetRootPaint", 1], ["SetRootPaint", 2]]), label + "they set what the next support will be");
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { mode: "draft", painted: true, rootable: false, boards: [] }));
    check(!byType(draw(EditorPanel), "ui.PanelSectionRow").some(n => n.props.left === "The posts' root") && byType(draw(EditorPanel), "ui.PanelSectionRow").some(n => n.props.left === "The support's finish"), label + "a support with no post of its own (a wall, another mod's pole): no root paint row, the finish stays");
    setUi({ editorTab: "sign" });
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { mode: "draft" }));

    // warnings, the junction, a marking, nothing to set
    setUi({ editorTab: "sign" });
    values.Editor = JSON.stringify(Object.assign({}, boardEditor, { warn: ["禁5 与 示26 在横向上重叠"] }));
    tree = draw(EditorPanel);
    check(textOf(tabsOf(tree)[1]).includes("⚠") && withClass(tree, "cdlts-warn").length === 0, label + "a warning marks the support's tab, and is not on the sign's page");
    setUi({ editorTab: "support" });
    check(withClass(draw(EditorPanel), "cdlts-warn").some(n => textOf(n).includes("重叠")), label + "it is on the support's page");
    setUi({ editorTab: "sign" });
    const junction = Object.assign({}, editorJson, { boards: [], jn: { on: true, note: "已读取前方 300 m 十字路口。", notes: ["「学府大道西延伸段北路」超过 7 个字，已截成「学府大道西延伸」。", "名称已截短。"] } });
    values.Editor = JSON.stringify(junction);
    tree = draw(EditorPanel);
    const jrow = byType(tree, "ui.PanelSectionRow").find(n => n.props.left === "Fill in from the junction when placing");
    check(!!jrow && textOf(tree).includes("已读取前方") && withClass(tree, "cdlts-warn").length === 2, label + "the junction block: the switch, what was read and its notes");
    if (found) {
        byType(jrow, "ui.game:Checkbox")[0].props.onChange(false);
    } else {
        byType(jrow, "ui.Button")[0].props.onSelect();
    }
    wideAction(tree, found, "Read the junction").props.onSelect();
    check(take() === JSON.stringify([["SetReadJunctions", 0], ["ReadJunction"]]), label + "the switch and reading again");
    values.Editor = JSON.stringify(Object.assign({}, junction, { mode: "draft" }));
    check(!textOf(draw(EditorPanel)).includes("Read the junction"), label + "a draft has nothing to read again");
    // A text board on a flat wall: the button that reads the wall's curve again (0.12.77); nothing for a draft or for any other board.
    const onWall = Object.assign({}, editorJson, { boards: [], fw: true });
    values.Editor = JSON.stringify(onWall);
    tree = draw(EditorPanel);
    check(textOf(tree).includes("Fit to the wall"), label + "a text board on a wall has the button to fit it to the wall");
    wideAction(tree, found, "Fit to the wall").props.onSelect();
    check(take() === JSON.stringify([["FitFacade"]]), label + "the button asks the tool to fit the wall");
    values.Editor = JSON.stringify(Object.assign({}, onWall, { mode: "draft" }));
    check(!textOf(draw(EditorPanel)).includes("Fit to the wall"), label + "a draft has nothing to fit");
    values.Editor = JSON.stringify(Object.assign({}, editorJson, { boards: [] }));
    check(!textOf(draw(EditorPanel)).includes("Fit to the wall"), label + "another board has no such button");

    values.Editor = JSON.stringify({ id: 12, mode: "item", code: "线52", title: "停止线", preview: "coui://cdlcts/edit-9.png", pw: 256, ph: 40, marking: true, info: ["漆面范围 12.0 m"], fields: [{ k: "span", l: "宽度", t: "text", v: "8", max: 4 }] });
    setUi({ editorTab: "support" });
    tree = draw(EditorPanel);
    check(tabsOf(tree).length === 0 && byType(tree, "ui.PanelSectionRow").some(n => n.props.left === "宽度") && withClass(tree, "cdlts-preview-img").length === 1 && textOf(tree).includes("漆面范围"), label + "a marking: no tabs, its setting, picture and measures (a left-over support tab does not matter)");
    setUi({ editorTab: "sign" });
    values.Editor = JSON.stringify({ id: 8, mode: "draft", code: "禁1", title: "停车让行", preview: "", plan: "pole.single", fields: [] });
    tree = draw(EditorPanel);
    check(textOf(tree).includes("This sign has no settings") && withClass(tree, "cdlts-preview-img").length === 0, label + "no settings: it says so, and no picture");

    // 0.12.29: a sign of the player's own: a new picture is saved as a sign; a saved one being placed keeps its changes or is deleted (pressed twice)
    const customDraft = { id: 13, mode: "draft", code: "自定义", title: "自定义路牌", preview: "coui://cdlcts/edit-10.png", pw: 400, ph: 300, plan: "pole.single", custom: { key: "" }, fields: [{ k: "name", l: "名称", t: "text", v: "公司标志", max: 16 }] };
    values.Editor = JSON.stringify(customDraft);
    tree = draw(EditorPanel);
    check(!!wideAction(tree, found, "Save as a new sign") && !wideAction(tree, found, "Keep the changes") && !wideAction(tree, found, "Delete this sign") && withClass(tree, "cdlts-preview-img").length === 1, label + "a new picture: its preview and the save, nothing to change or delete");
    take();
    wideAction(tree, found, "Save as a new sign").props.onSelect();
    check(take() === JSON.stringify([["SaveCustom"]]), label + "it is saved as a new sign");
    values.Editor = JSON.stringify(Object.assign({}, customDraft, { custom: { key: "c3" } }));
    values.Custom = JSON.stringify(Object.assign({}, customJson, { note: "已保存为路牌：公司标志。" }));
    tree = draw(EditorPanel);
    check(textOf(tree).includes("已保存为路牌") && !!wideAction(tree, found, "Keep the changes"), label + "a saved sign: what the save said, and its changes can be kept");
    wideAction(tree, found, "Keep the changes").props.onSelect();
    wideAction(tree, found, "Delete this sign").props.onSelect();
    check(take() === JSON.stringify([["UpdateCustom", "c3"]]) && ui.confirmingCustom === "c3", label + "changes are kept in it; the first press of delete asks again");
    wideAction(draw(EditorPanel), found, "Press again").props.onSelect();
    check(take() === JSON.stringify([["DeleteCustom", "c3"]]) && ui.confirmingCustom === "", label + "the second press deletes it");
    values.Custom = JSON.stringify(customJson);
    values.Editor = JSON.stringify(Object.assign({}, editorJson, { boards: [] }));
    check(!wideAction(draw(EditorPanel), found, "Save as a new sign"), label + "another sign has no custom buttons");

    // 0.12.75: the text light box is a template like the others: its settings are the editor's generic rows, and what its font and its frame say is on the sign's page
    const textDraft = {
        id: 14, mode: "draft", code: "文字灯箱", title: "文字灯箱（发光字）", preview: "coui://cdlcts/edit-14.png", pw: 400, ph: 100, plan: "pole.single",
        info: ["牌面 2.00 × 0.50 m"],
        fields: [
            { k: "text", l: "文字（\\n 换行）", t: "text", v: "文字灯箱", max: 40, w: true },
            { k: "h", l: "字高（m）", t: "text", v: "0.5", max: 5 },
            { k: "glow", l: "发光颜色", t: "choice", v: "warm", max: 0, ch: [{ v: "warm", l: "暖白" }, { v: "white", l: "冷白" }, { v: "red", l: "红" }, { v: "gold", l: "金黄" }, { v: "blue", l: "蓝" }, { v: "pink", l: "粉" }, { v: "green", l: "绿" }] }
        ]
    };
    values.Editor = JSON.stringify(textDraft);
    tree = draw(EditorPanel);
    const blockOf = start => withClass(tree, "cdlts-field").find(n => textOf(n).startsWith(start)) || byType(tree, "ui.PanelSectionRow").find(n => n.props.left === start);
    const textRow = blockOf("文字（");
    // The owner's screenshot of 2 Nov: the text box ran over the next row. A text over the whole width is a block of its own (label, then the box, in the flow, no height of its own), and the
    // stylesheet gives the control of such a block a basis that is its content's: a flex basis of 0 in a column is a height of 0, which the game's engine does not lift to the content.
    check(!!textRow && textRow.props.className === "cdlts-field cdlts-field-stack", label + "a text of the whole width is a stacked block: " + (textRow && textRow.props.className));
    const stackKids = textRow ? textRow.props.children.filter(c => c && c.props) : [];
    check(stackKids.length === 2 && stackKids[0].props.className === "cdlts-field-label" && /cdlts-full/.test(stackKids[1].props.className || ""), label + "the stacked block is the label and then the control, both in the flow");
    check(!!textRow && textRow.props.style === undefined && stackKids.every(k => k.props.style === undefined), label + "no inline height or position on the block");
    const css = readFileSync(target.style, "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
    const rulesOf = selector => [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)].filter(m => m[1].split(",").map(s => s.trim()).includes(selector)).map(m => m[2]);
    const stackControl = rulesOf(".cdlts-field-stack .cdlts-field-control").join(";");
    check(/flex:\s*0 0 auto/.test(stackControl), label + "the control of a stacked block has the basis of its content (the cause of the overlap: flex 1 1 0 in a column)");
    check(!rulesOf(".cdlts-field-stack").concat(rulesOf(".cdlts-field")).some(r => /(^|;)\s*(height|max-height|position)\s*:/.test(r)), label + "no rule gives a field block a height of its own or takes it out of the flow");
    check(!rulesOf(".cdlts-field-control").concat(rulesOf(".cdlts-field-stack .cdlts-field-control")).some(r => /flex:\s*1 1 0/.test(r) && !/flex:\s*0 0 auto/.test(stackControl)), label + "a basis of 0 never reaches the stacked control");
    const textBox = textRow ? (found ? byType(textRow, "ui.game:TextInput")[0] : byType(textRow, "input")[0]) : null;
    check(!!textBox && textBox.props.maxLength === 40, label + "the text of a light box is a box of 40 characters (36, and room for two typed line breaks)");
    textBox.props.onBlur({ target: { value: "上行\\n下行" } });
    check(take() === JSON.stringify([["EditField", "14|text=上行\\n下行"]]), label + "a typed backslash and n travel as they are: the C# side makes them a line");
    const glowRow = blockOf("发光颜色");
    check(!!glowRow && textOf(glowRow).includes("暖白"), label + "the glow colour is a choice, and the one that is on is shown");
    check(textOf(tree).includes("牌面 2.00 × 0.50 m") && !textOf(tree).includes("支撑架") && !textOf(tree).includes("想换字体") && withClass(tree, "cdlts-warn").length === 0, label + "the size is the one line on the sign's page: no frame figures, no font note, no warning of the support");
    check(withClass(tree, "cdlts-preview-img").length === 1, label + "the picture of the board is shown (the letters lit, made by the C# side)");
    const labelsOf = withClass(tree, "cdlts-field-label").concat(byType(tree, "ui.PanelSectionRow").map(r => ({ props: { children: r.props.left } })));
    check(!/到 \d|0\.15|最多 3 行/.test(textOf(tree)), label + "no label carries a range or an explanation");
    values.Editor = JSON.stringify(Object.assign({}, textDraft, { info: [] }));
    check(!textOf(draw(EditorPanel)).includes("牌面"), label + "with nothing to say there is no note");
    values.Editor = JSON.stringify(Object.assign({}, editorJson, { boards: [] }));

    // 0.12.49: a lamp-post ad takes a picture: the folder's files each with a button that puts it on, and one that takes it off when it has one
    const lampDraft = { id: 31, mode: "draft", code: "道旗", title: "道旗·导入图片", preview: "coui://cdlcts/edit-31.png", pw: 400, ph: 300, plan: "attached.pole", pic: { has: false }, fields: [] };
    values.Editor = JSON.stringify(lampDraft);
    tree = draw(EditorPanel);
    const useRows = byType(tree, "ui.PanelSectionRow").filter(n => n.props.left === "公司标志.png" || n.props.left === "b.png");
    check(useRows.length === 2 && textOf(tree).includes("Picture (PNG)") && !wideAction(tree, found, "Take the picture off"), label + "a lamp-post ad: its picture rows, a row for each file");
    byType(useRows[0], "ui.Button")[0].props.onSelect();
    check(take() === JSON.stringify([["UsePicture", "公司标志.png"]]), label + "a file is put on the ad");
    values.Editor = JSON.stringify(Object.assign({}, lampDraft, { pic: { has: true } }));
    tree = draw(EditorPanel);
    wideAction(tree, found, "Take the picture off").props.onSelect();
    check(take() === JSON.stringify([["UsePicture", ""]]), label + "and taken off");
    values.Editor = JSON.stringify(Object.assign({}, editorJson, { boards: [] }));
    check(!textOf(draw(EditorPanel)).includes("Picture (PNG)"), label + "another sign has no picture rows");

    // 0.12.44: the automatic marking's chosen stretch: how to choose one; chosen, its size, letting it go and undoing it (pressed twice)
    const autoDraft = { id: 21, mode: "draft", code: "重标", title: "按国标自动重标这一段", preview: "", marking: true, auto: { on: false, len: 0, segs: 0, appr: 0, pending: false }, fields: [] };
    values.Editor = JSON.stringify(autoDraft);
    tree = draw(EditorPanel);
    check(textOf(tree).includes("Click a marked stretch") && !wideAction(tree, found, "Undo its marking"), label + "the automatic marking with nothing chosen: how to choose a stretch");
    values.Editor = JSON.stringify(Object.assign({}, autoDraft, { auto: { on: true, len: 182.4, segs: 3, appr: 2, pending: false, ends: ["进口1（绿圈）：导向车道线 60 m（按道路状态），箭头 3 组；限速 50 km/h，排队 47 m"] } }));
    tree = draw(EditorPanel);
    check(textOf(tree).includes("排队 47 m"), label + "what was drawn at each approach, and by what state of the road (0.12.45)");
    const autoHead = byType(tree, "ui.PanelSectionRow").find(n => String(n.props.left).startsWith("Chosen stretch"));
    check(!!autoHead && String(autoHead.props.left).includes("3 segment(s), 182 m") && !textOf(tree).includes("ringed") && !textOf(tree).includes("drawn again in a moment"), label + "a chosen stretch: its size; 0.12.47: no note while nothing waits to be drawn");
    byType(autoHead, "ui.Button")[0].props.onSelect();
    wideAction(tree, found, "Undo its marking").props.onSelect();
    check(take() === JSON.stringify([["AutoForget"]]) && ui.confirmingAutoUndo === 21, label + "let go at once; the first press of undo asks again");
    wideAction(draw(EditorPanel), found, "Press again: undo").props.onSelect();
    check(take() === JSON.stringify([["AutoUndo"]]) && ui.confirmingAutoUndo === null, label + "the second press undoes it, once");
    // 0.12.46: the lines along on the stretch that are no automatic markings: a note and a delete pressed twice
    values.Editor = JSON.stringify(Object.assign({}, autoDraft, { id: 22, auto: { on: true, len: 182.4, segs: 3, appr: 2, pending: false, old: 10 } }));
    tree = draw(EditorPanel);
    check(withClass(tree, "cdlts-warn").some(n => textOf(n).includes("10 manual line(s)")) && !!wideAction(tree, found, "Delete those 10"), label + "old lines on the stretch: said, with a button");
    wideAction(tree, found, "Delete those 10").props.onSelect();
    check(take() === "[]" && ui.confirmingAutoOld === 22, label + "the first press only asks");
    wideAction(draw(EditorPanel), found, "Press again: delete them").props.onSelect();
    check(take() === JSON.stringify([["AutoRemoveOld"]]) && ui.confirmingAutoOld === null, label + "the second press deletes them, once");
    values.Editor = JSON.stringify(Object.assign({}, autoDraft, { auto: { on: true, len: 182.4, segs: 3, appr: 2, pending: false, old: 0 } }));
    check(!wideAction(draw(EditorPanel), found, "Delete those"), label + "none: no button");
    values.Editor = JSON.stringify(Object.assign({}, autoDraft, { fields: [{ k: "range", l: "路段", t: "choice", v: "s", max: 0, sec: "范围", ch: [{ v: "s", l: "这一段" }, { v: "e", l: "只这个路段" }] }, { k: "keep", l: "清除原版的", t: "choice", v: "lines", max: 0, ch: [{ v: "lines", l: "沿路的线" }, { v: "all", l: "全部" }] }] }));
    const sectioned = byType(draw(EditorPanel), "ui.PanelSectionRow");
    const heading = sectioned.findIndex(n => n.props.left === "范围" && n.props.uppercase === true);
    check(heading >= 0 && sectioned.findIndex(n => n.props.left === "路段") > heading && sectioned.filter(n => n.props.uppercase === true && n.props.left === "清除原版的").length === 0, label + "0.12.47: a section's heading above the setting that starts it, none for the others");
    values.Editor = JSON.stringify(Object.assign({}, autoDraft, { auto: { on: true, len: 182.4, segs: 3, appr: 2, pending: true } }));
    check(textOf(draw(EditorPanel)).includes("drawn again in a moment"), label + "settings still to be drawn say so");
    values.Editor = JSON.stringify(Object.assign({}, editorJson, { boards: [] }));
    check(!textOf(draw(EditorPanel)).includes("Chosen stretch") && !textOf(draw(EditorPanel)).includes("Click a stretch marked before"), label + "another entry has no stretch rows");
    values.Editor = "";

    // ---- the window, the button, the extension
    const gw = module.GameWindow();
    check(gw && gw.props.children.length === 3 && gw.props.children.every(c => c.type === Guard), label + "open: three guarded parts, the dock, the dialog and the keys");
    check(byType(draw(GameWindow), "ui.Panel").length === 1, label + "the catalogue's panel is drawn (the editor has nothing to show)");
    const guard = new Guard({ name: "x", children: "kept" });
    guard.state = { failed: false };
    check(guard.render() === "kept", label + "a guard shows its children");
    guard.state = Guard.getDerivedStateFromError(new Error("x"));
    check(guard.render() === null, label + "and nothing once it caught an error");

    setUi({ open: false });
    let button = draw(TsButton);
    const gameButton = byType(button, "ui.Button")[0];
    check(!!gameButton && gameButton.props.src === module.icons.button && gameButton.props.selected === false, label + "the right-hand button, not selected while the panels are closed");
    check(byType(button, "ui.Tooltip").length === 1, label + "with a tooltip");
    if (found) {
        check(button.props.className === "item_x" && gameButton.props.className === "toggle_x" && gameButton.props.variant === undefined, label + "with the game's right-menu classes it is one of the column's items");
    } else {
        check(gameButton.props.variant === "floating", label + "without them a floating button");
    }
    gameButton.props.onSelect();
    check(ui.open === true, label + "pressing it opens the panels");
    const generation = ui.generation;
    gameButton.props.onSelect();
    check(ui.open === true, label + "a second press within 180 ms is the same press");
    const realNow = Date.now;
    Date.now = () => realNow() + 1000;
    byType(draw(TsButton), "ui.Button")[0].props.onSelect();
    check(ui.open === false && take() === JSON.stringify([["Leave"]]), label + "a later press closes them and leaves the tool");
    Date.now = () => realNow() + 2000;
    byType(draw(TsButton), "ui.Button")[0].props.onSelect();
    Date.now = realNow;
    check(ui.open === true && ui.generation > generation, label + "and the next opens them again, as a new generation");

    const extended = [], appended = [];
    module.default({ extend: (path, name, wrapper) => extended.push([path, name, wrapper]), append: (point, component) => appended.push([point, component]) });
    check(extended.length === 1 && extended[0][0] === "game-ui/game/components/asset-menu/asset-menu.tsx" && extended[0][1] === "AssetMenu", label + "the game's AssetMenu is wrapped");
    check(appended.length === 2 && appended[0][0] === "GameBottomRight" && appended[0][1] === TsButton && appended[1][0] === "Game" && appended[1][1] === GameWindow, label + "the button to GameBottomRight, the panels to the game's root");
    const Original = () => "original";
    const wrapped = extended[0][2](Original)({ a: 1 });
    check(wrapped.type === Original && wrapped.props.a === 1, label + "the wrapped menu is the original with its props");
    if (!found) {
        module.default({ extend: () => {}, append: () => {}, registry: new Map([
            [module.modules.rightMenuButton, { classes: { toggleStates: "toggle_r" } }],
            [module.modules.rightMenu, { classes: { item: "item_r" } }]
        ]) });
        button = draw(TsButton);
        check(button.props.className === "item_r" && byType(button, "ui.Button")[0].props.className === "toggle_r", label + "the registry's classes when the modules cannot be looked up");
    }

    // ---- every component calls the same hooks on every render
    for (const [name, counts] of hookCounts) {
        check(counts.size === 1, label + `${name} calls the same hooks on every render: ${[...counts].join(", ")}`);
    }
    check(hookCounts.has("MainPanel") && hookCounts.has("EditorPanel") && hookCounts.has("TextField") && hookCounts.has("Dock") && hookCounts.has("ActionPill") && hookCounts.has("SettingsPanel"), label + "the hooks of the dock, its panels and the text box were counted");
    hookCounts.clear();

    if (found) {
        // ---- the game's components: published, in the bundle, the icons there
        if (published) {
            for (const n of [...used].filter(x => !x.startsWith("game:"))) {
                check(published.has(n), `cs2/ui.${n} is one of the game's published components`);
            }
        }
        const pairs = [...new Set(looked.map(([p, n]) => p + "|" + n))].map(s => s.split("|"));
        check(pairs.length >= 8, "the script looks up the game's modules: " + pairs.length);
        if (bundle) {
            for (const [path, name] of pairs) {
                const at = bundle.indexOf(`add("${path}",{`);
                check(at >= 0, `the game's bundle has the module ${path}`);
                if (at >= 0) {
                    check(bundle.slice(at, at + 4000).includes(`get ${name}(){`), `${path} exports ${name}`);
                }
            }
            for (const [name, path] of Object.entries(module.icons)) {
                const packaged = path.startsWith("coui://cdlcts-icons/");
                check(existsSync(packaged ? join(root, "Icons", path.slice("coui://cdlcts-icons/".length)) : join(uiRoot, path)), `the icon ${name} is available: ${path}`);
            }
        }
        if (gameCss) {
            check(/\.tab-bar_[A-Za-z0-9]+ button\{[^}]*min-width:var\(--minTabWidth\)/.test(gameCss), "the game's tab bar still sizes its tabs by --minTabWidth (the variable this sheet sets)");
            check(/width:var\(--assetMenuItemSize\)/.test(gameCss), "the game's asset tiles still take their size from --assetMenuItemSize");
        }
        for (const name of ["Panel", "PanelSection", "PanelSectionRow", "PanelFoldout", "Button", "Tooltip", "Scrollable", "Dropdown", "DropdownToggle", "DropdownItem", "ConfirmationDialog"]) {
            check(used.has(name), `the panels use cs2/ui.${name}`);
        }
        for (const name of ["TabBar", "Tab", "ToolButton", "InfoButton", "TextInput", "Checkbox"]) {
            check(used.has("game:" + name), `the panels use the game's ${name}`);
        }
    }
}

// ------------------------------------------------------------------ the pure parts

setGlobals(true, true);
const module = await import(target.url("?pure"));
const { matches, search, filterEntries, groupsOf, kindIcon, paginate, normalize, sameEntity, panelVisible, parseCatalog, parseEditor, parseHint, memoParse, previewSize, textWidth, stepFrom, actions, reportError } = module;

const standardSigns = readFileSync(join(root, "Geo", "Signs", "StandardSigns.cs"), "utf8");
const kindNames = [...standardSigns.matchAll(/public const string \w+ = "([a-z]+)";/g)].map(m => m[1]);
check(kindNames.length >= 4 && kindNames.includes("prohibition") && kindNames.includes("warning"), "the C# side names its kinds of sign: " + kindNames.join(","));
const demo = [
    { id: "a", code: "禁1", zh: "停车让行", en: "Stop", kw: ["停", "让行"], py: "tcrx", grp: "prohibition" },
    { id: "b", code: "禁2", zh: "减速让行", en: "Yield", kw: ["减速", "让行"], py: "jsrx", grp: "prohibition" },
    { id: "c", code: "禁39", zh: "限制速度", en: "Speed limit", kw: ["限速"], py: "xzsd", grp: "prohibition" },
    { id: "d", code: "警24", zh: "隧道", en: "Tunnel", kw: ["隧道口"], py: "sd", grp: "warning", thumb: "coui://x/d.png" },
    { id: "e", code: "示13", zh: "开车灯", en: "Headlights on", kw: ["隧道", "车灯"], py: "kcd", grp: "mandatory" }
];
const found = query => search(demo, query).map(e => e.id).sort().join(",");
check(search(demo, "").length === demo.length, "an empty query shows everything");
check(found("停车") === "a" && found("stop") === "a" && found("STOP") === "a", "Chinese and English names, any case");
check(found("禁1") === "a" && found("禁１") === "a" && found("禁39") === "c", "a code finds only itself, full-width digits as half-width");
check(found("禁") === "a,b,c" && found("隧道") === "d,e" && found("隧道 灯") === "e" && found("jsrx") === "b" && found("没有这个") === "", "prefixes, keywords, two words, pinyin initials, nothing");
check(normalize("ＡＢＣ１") === "abc1" && matches({ code: "", zh: "", en: "" }, "x") === false, "normalize; an entry without text matches nothing");
check(parseCatalog("[{\"id\":\"x\"}]").length === 1 && parseCatalog("not json").length === 0 && parseCatalog("{}").length === 0 && parseCatalog("").length === 0, "the catalogue is parsed safely");
check(parseEditor("") === null && parseEditor("not json") === null && parseEditor("{}") === null && parseEditor("[]") === null, "the editor is parsed safely");
check(parseHint("") === null && parseHint("x") === null && parseHint(JSON.stringify({ t: "a", bad: 1 })).bad === true, "the hint is parsed safely");
check(JSON.stringify(parseHint(JSON.stringify({ t: "a", k: [["Esc", "leave"], ["bad"], 3, [1, 2]] })).keys) === JSON.stringify([["Esc", "leave"], ["1", "2"]]) && parseHint(JSON.stringify({ t: "a" })).keys.length === 0,
    "the keys are pairs, made text; anything else is dropped");
check(filterEntries(demo, "", "all").length === 5 && filterEntries(demo, "让行", "prohibition").map(e => e.id).join(",") === "a,b" && filterEntries(demo, "隧道", "warning").map(e => e.id).join(",") === "d", "query and kind together");
check(groupsOf(demo).join(",") === "prohibition,warning,mandatory,custom" && groupsOf([{ grp: "zzz" }, { grp: "marking" }, { grp: "variable" }, {}]).join(",") === "variable,marking,custom,zzz", "the kinds in the order of the standard, the player's own last of them, unknown ones after");
{
    // 0.12.22: a series (BRT) is a tab of its own that lists its entries of other kinds too; they stay in their own kind's tab.
    const series = [{ id: "m28", grp: "mandatory", ser: "brt", zh: "BRT专用车道" }, { id: "s", grp: "brt", ser: "brt", zh: "BRT站点" }, { id: "w", grp: "marking", ser: "brt", zh: "快速公交" }, { id: "m27", grp: "mandatory", ser: "", zh: "公交专用车道" }];
    check(groupsOf(series).join(",") === "mandatory,brt,marking,custom", "the series is a tab, between the screens and the markings: " + groupsOf(series).join(","));
    check(filterEntries(series, "", "brt").map(e => e.id).join(",") === "m28,s,w" && filterEntries(series, "", "mandatory").map(e => e.id).join(",") === "m28,m27", "its tab lists the series, the kind's tab keeps its own");
}
{
    // 0.12.107: an entry of a series alone (so) is in the series' tab and not in its kind's: a toll booth is not a barrier. The others of the series are in both.
    const toll = [{ id: "gate", grp: "barrier", ser: "toll", so: true, zh: "道闸" }, { id: "cone", grp: "barrier", ser: "toll", zh: "锥桶" }, { id: "rail", grp: "barrier", ser: "", zh: "护栏" },
        { id: "lane", grp: "toll", ser: "toll", zh: "车道牌" }, { id: "g70", grp: "guide", ser: "toll", zh: "收费站" }];
    check(filterEntries(toll, "", "barrier").map(e => e.id).join(",") === "cone,rail", "the barrier tab does not list what is the toll plaza's alone: " + filterEntries(toll, "", "barrier").map(e => e.id).join(","));
    check(filterEntries(toll, "", "toll").map(e => e.id).join(",") === "gate,cone,lane,g70", "the toll tab lists it, with the series' others");
    check(filterEntries(toll, "", "all").length === 5 && filterEntries(toll, "道闸", "all").map(e => e.id).join(",") === "gate" && filterEntries(toll, "道闸", "barrier").length === 0, "all lists it, and a search in the barrier tab does not find it");
    check(groupsOf(toll).join(",") === "guide,toll,barrier,custom", "the tabs: " + groupsOf(toll).join(","));
    check(groupsOf([{ id: "gate", grp: "barrier", ser: "toll", so: true }]).join(",") === "toll,custom", "an entry of a series alone makes no tab of its kind: " + groupsOf([{ id: "gate", grp: "barrier", ser: "toll", so: true }]).join(","));
    check(filterEntries([{ id: "x", grp: "barrier", ser: "toll", so: false }], "", "barrier").length === 1 && filterEntries([{ id: "x", grp: "barrier", ser: "toll" }], "", "barrier").length === 1, "without the flag, or with it false, an entry is in both");
}
check(kindIcon("warning") === "coui://cdlcts-icons/warning.png" && kindIcon("prohibition") === "coui://cdlcts-icons/prohibition.png", "tab icons are independent of generated asset thumbnails");
check(kindIcon("unknown") === module.icons.all, "an extension category has a visible fallback icon");
const groupOrderForTest = Object.keys(languages["en-US"]).filter(k => k.startsWith("CDLC.TS.UI.Group.")).map(k => k.slice("CDLC.TS.UI.Group.".length)).filter(k => k !== "all");
for (const key of Object.keys(languages["en-US"]).filter(k => k.startsWith("CDLC.TS.UI.Group."))) {
    const kind = key.slice("CDLC.TS.UI.Group.".length);
    const uri = kind === "all" ? module.icons.all : kindIcon(kind);
    check(uri === `coui://cdlcts-icons/${kind}.png` && existsSync(join(root, "Icons", `${kind}.png`)), `the ${kind} tab has packaged artwork`);
}
// The icons are shown at 32 to 56 rem, 110 px at most on a 4K screen: packaged at 128 px and a palette (0.12.61, 739 KB of 256 px files became 59 KB), none of the 256 px candidates.
for (const name of ["streetscape", "all", ...groupOrderForTest, "place", "edit", "delete", "settings", "back"]) {
    const png = readFileSync(join(root, "Icons", name + ".png"));
    const width = png.readUInt32BE(16), height = png.readUInt32BE(20);
    check(width <= 128 && height <= 128 && png.length <= 8192, `the ${name} icon is packaged small: ${width}x${height}, ${png.length} bytes`);
    check(png[25] === 3 || png[25] === 6, `and it is a palette or RGBA picture (the transparent sides keep their alpha): colour type ${png[25]}`);
}
const thirty = Array.from({ length: 30 }, (_, i) => ({ id: "x" + i }));
check(paginate(thirty, 0, 18).pages === 2 && paginate(thirty, 1, 18).items.length === 12 && paginate(thirty, 9, 18).page === 1 && paginate(thirty, -4, 18).page === 0 && paginate([], 3, 18).pages === 1, "pages are kept inside the list");
check(sameEntity({ index: 5, version: 1 }, { index: 5, version: 1 }) && !sameEntity({ index: 5, version: 1 }, { index: 5, version: 2 }) && !sameEntity({ index: 0, version: 0 }, { index: 0, version: 0 }) && !sameEntity(null, {}), "entities by index and version, never the null one");
check(panelVisible(true) && !panelVisible(false) && !panelVisible(undefined), "the panels are shown exactly while they are open");
const size = previewSize(100, 200, 360, 220);
check(size.w === 110 && size.h === 220 && previewSize(0, 0, 360, 220).w === 360, "previewSize fits by the limiting side");
check(stepFrom([10, 15, 20, 30], 20, 1) === 30 && stepFrom([10, 15, 20, 30], 30, 1) === 30 && stepFrom([10, 15, 20, 30], 10, -1) === 10 && stepFrom([10, 15, 20, 30], 22, -1) === 15, "a stepper moves by a step and stops at the ends; a value between steps counts from the nearest");
const listA = module.filteredOf(demo, "", "all");
check(module.filteredOf(demo, "", "all") === listA && module.filteredOf(demo, "让行", "all") !== listA && module.filteredOf(demo, "让行", "all").list.length === 2 && module.filteredOf(demo, "", "nope").current === "all" && module.filteredOf(demo, "", "warning").list.length === 1, "the matching signs are worked out again only for another catalogue, query or kind");
let parses = 0;
const memo = memoParse(text => { parses++; return { text }; });
const first = memo("a");
check(memo("a") === first && parses === 1 && memo("b") !== first && parses === 2 && memo("b").text === "b" && parses === 2, "a text is parsed again only when it changes");
check(textWidth("向左") === 26 && Math.abs(textWidth("km/h") - 28.8) < 1e-9 && textWidth("") === 0 && textWidth(null) === 0, "the width of a text");
const uiText = readFileSync(join(root, "Tests", "CDLC.TS.Tests", "UiTextTests.cs"), "utf8");
check(/Cjk = 13f, Latin = 7\.2f/.test(uiText) && /Inside = 380f/.test(uiText) && /ItemExtra = 40f/.test(uiText), "the C# test of the labels uses the script's figures (13 and 7.2 rem, 380 rem, 40 rem a dropdown item)");

triggers.length = 0;
actions.select("gb.stop");
actions.setSupport("cantilever.single");
actions.setMode(1);
actions.editField("code", "G15", 21);
actions.leave();
actions.setNight(16);
check(take() === JSON.stringify([["Select", "gb.stop"], ["SetSupport", "cantilever.single"], ["SetMode", 1], ["EditField", "21|code=G15"], ["Leave"], ["SetNight", 16]]), "the triggers carry their names and arguments");
check(actions.setQueue === undefined && actions.setDebug === undefined, "0.12.47: the test switches' triggers are gone");
let blurred = 0;
globalThis.document = { body: {}, activeElement: { blur: () => { blurred++; } } };
actions.select("gb.stop");
actions.setMode(1);
actions.leave();
check(blurred === 3, "choosing a sign, a mode or leaving takes the focus from a text box");
globalThis.document.activeElement = { blur: () => { throw new Error("blur failed"); } };
let threw = false;
try { actions.select("gb.stop"); } catch (error) { threw = true; }
check(!threw, "a failing blur never stops the trigger");
delete globalThis.document;
take();
for (let i = 0; i < 12; i++) {
    reportError("test", new Error("boom " + i));
}
check(triggers.length === 10 && triggers[0][1] === "UiError" && /boom 0/.test(triggers[0][2]), "script errors go to the C# side, ten at most");
triggers.length = 0;

// ------------------------------------------------------------------ the contract with the C# side

const source = readFileSync(target.source, "utf8");
const csharp = readFileSync(join(root, "Systems", "TsUiSystem.cs"), "utf8") + readFileSync(join(root, "Systems", "TsUiSystem.Profiles.cs"), "utf8");
const clearTool = readFileSync(join(root, "Tool", "PlacementTool.Clear.cs"), "utf8");
check(clearTool.includes("ClearByAuto(draft, click") && !clearTool.includes("RedrawEdge("), "0.12.46: the clear tool's redraw is the automatic marking, its own lines-only redraw is gone");
const midTool = readFileSync(join(root, "Tool", "PlacementTool.cs"), "utf8") + readFileSync(join(root, "Tool", "PlacementTool.Mid.cs"), "utf8");
check(/internal static bool Mid \{ get; private set; \}/.test(midTool) && !/\bMid\s*=\s*false/.test(midTool) && !/SetMid\(\s*false\s*\)/.test(midTool) && !midTool.includes("m_Mid"), "0.12.84: the in-road mode is the tool's one static switch, and choosing another sign does not turn it off");
check(midTool.includes("if (!Mid || carriageway == null || !MidRoad.Takes(support.Kind))") && midTool.includes("midOn: Mid, midHere: midActive"), "0.12.84: the placing reads the switch, and the line of help knows whether the post stands in the road now");
check(csharp.includes('[JsonProperty("mid")] public bool Mid;') && csharp.includes("Mid = MidRoad.Takes(def.Kind)") && /TriggerBinding<bool>\(kGroup, "SetMid", PlacementTool\.SetMid\)/.test(csharp) && csharp.includes("m_Mid.Update(PlacementTool.Mid)"), "0.12.84: the C# side offers the switch for the posts, takes its trigger and sends the tool's state each update");
for (const name of ["Debug", "Stats", "Queue"]) {
    check(!source.includes("bind(\"" + name + "\"") && !csharp.includes("kGroup, \"" + name + "\""), "0.12.47: " + name + " is not bound any more");
}
for (const name of ["Category", "Status", "Catalog", "Selected", "Supports", "Support", "Mode", "Margin", "Mid", "ToolActive", "Editor", "Hint", "Night", "NightGain", "ViewDistance", "Footprint", "LedDay", "LedNight", "ClearAreas", "ClearHidden", "Orphans", "Custom"]) {
    check(source.includes(`bind("${name}"`), `the script binds ${name}`);
    check(csharp.includes(`"${name}"`), `the C# side declares ${name}`);
}
const sent = [...new Set([...source.matchAll(/api\.trigger\(group, "([A-Za-z]+)"/g)].map(m => m[1]))];
check(sent.length >= 25, "the script sends its triggers: " + sent.length);
for (const name of sent) {
    check(new RegExp(`TriggerBinding(<[a-z]+>)?\\(kGroup, "${name}"`).test(csharp), `the C# side handles ${name}`);
}
check(/kGroup = "cdlcts"/.test(csharp) && /const group = "cdlcts"/.test(source) && /"toolbar", "selectedAssetCategory"/.test(source), "the group names agree; the game's own selected category is read");
for (const field of ["id", "code", "zh", "en", "kw", "py", "grp", "thumb", "cfg"]) {
    check(csharp.includes(`JsonProperty("${field}")`), `the catalogue carries "${field}"`);
}
for (const field of ["id", "mode", "code", "title", "preview", "pw", "ph", "plan", "info", "reach", "reachKind", "reachMin", "reachMax", "reachStep", "reachHand", "clear", "clearMin", "clearMax", "clearStep", "clearHand", "boards", "sel", "max", "lanes", "laneState", "lane", "warn", "fields", "k", "l", "t", "v", "ch", "posts", "wall", "turns", "turn"]) {
    check(csharp.includes(`JsonProperty("${field}")`), `the editor carries "${field}"`);
}
// 0.12.78: the span and the arm are set by SetSpan (a number) and FitSpan (none); the old two (SetReach, StepReach) are gone from both sides
check(source.includes('api.trigger(group, "SetSpan"') && source.includes('api.trigger(group, "FitSpan")') && /TriggerBinding<float>\(kGroup, "SetSpan"/.test(csharp) && /TriggerBinding\(kGroup, "FitSpan"/.test(csharp), "the script and the C# side agree on SetSpan and FitSpan");
check(source.includes('api.trigger(group, "SetClearance"') && /TriggerBinding<float>\(kGroup, "SetClearance"/.test(csharp), "the script and the C# side agree on SetClearance");
// 0.12.80: the facing is set by SetTurn (an angle, from the slider, the box and the sign about to be placed) besides TurnBoard (a step), and the model carries it as a float, not whole degrees
check(source.includes('api.trigger(group, "SetTurn"') && /TriggerBinding<float>\(kGroup, "SetTurn"/.test(csharp) && /\[JsonProperty\("turn"\)\] public float Turn;/.test(csharp), "the script and the C# side agree on SetTurn, and the model's turn is a float");
check(!source.includes("StepReach") && !source.includes("SetReach") && !csharp.includes('"StepReach"') && !csharp.includes('"SetReach"'), "no trigger of the old span row is left");
for (const field of ["id", "zh", "en", "thumb"]) {
    check(new RegExp(`class SupportEntry[\\s\\S]*JsonProperty\\("${field}"\\)`).test(csharp), `the support list carries "${field}"`);
}
// 0.12.75: the support styles: the bindings and the trigger agree, the list carries what the row reads, and every profile and every evidence class has its name in both languages
check(source.includes('bind("Profiles"') && source.includes('bind("Profile"') && csharp.includes('"Profiles"') && csharp.includes('"Profile"') && csharp.includes('TriggerBinding<string>(kGroup, "SetProfile"'), "the script and the C# side agree on Profiles, Profile and SetProfile");
for (const field of ["id", "zh", "en", "pe", "re", "fe"]) {
    check(new RegExp(`class ProfileEntry[\\s\\S]*JsonProperty\\("${field}"\\)`).test(csharp), `the profile list carries "${field}"`);
}
const profileSource = readFileSync(join(root, "Geo", "StyleProfile.cs"), "utf8");
const profileIds = [...profileSource.matchAll(/Id = "([a-z]+)", Zh = /g)].map(m => m[1]);
check(profileIds.length === 7 && profileIds[0] === "plain", "the seven profiles of Geo/StyleProfile.cs are found: " + profileIds.join(","));
for (const [locale, texts] of Object.entries(languages)) {
    for (const id of profileIds) {
        check(typeof texts["CDLC.TS.UI.Profile." + id] === "string" && texts["CDLC.TS.UI.Profile." + id].length > 0, `${locale}: the profile ${id} has a name`);
    }
    for (const code of ["std", "photo", "supplier", "estimate"]) {
        check(typeof texts["CDLC.TS.UI.Evidence." + code] === "string" && texts["CDLC.TS.UI.Evidence." + code].length > 0, `${locale}: the evidence class ${code} has a name`);
    }
}
for (const field of ["add", "folder", "files", "note", "bad", "n", "w", "h", "kb", "custom", "key"]) {
    check(csharp.includes(`JsonProperty("${field}"`), `the custom signs carry "${field}"`);
}
check(/if \(choice\.Hidden\)\s+continue;/.test(csharp), "the bare custom sign is not listed in the catalogue");
check(csharp.includes("k = HintText.Keys(hint, zh)") && !csharp.includes("TextWanted"), "0.12.47: the keys go with the line of help; the tool's own text is not sent any more");
check(csharp.includes("JsonProperty(\"sec\""), "the editor's settings carry their sections");
check(csharp.includes("JsonProperty(\"r\", NullValueHandling = NullValueHandling.Ignore)") && csharp.includes("f.Range = new[] { field.SliderMin, field.SliderMax, field.SliderStep }"), "0.12.76: a number with a slider carries its range, from, to, step, as \"r\"");
check(csharp.includes("field.DraftOnly && view.IsItem") && csharp.includes("SignLine.Stripped(view.Def, spec.ToString())"), "0.12.69: the settings of a line of signs are the draft's: a placed sign's editor leaves them out and its picture does not depend on them");
const store = readFileSync(join(root, "Render", "ThumbnailStore.cs"), "utf8");
check(/Host = "cdlcts"/.test(store) && store.includes("coui://{Host}/") && store.includes("static void EnsureHost") && csharp.includes("ThumbnailStore.EnsureHost()"), "the thumbnails are served from coui://cdlcts/, registered until the UI system is there");

// ------------------------------------------------------------------ the texts

const tabSource = readFileSync(join(root, "Systems", "ToolbarTabSystem.cs"), "utf8");
const categoryName = /kCategoryName = "([^"]+)"/.exec(tabSource)[1];
const keys = [...new Set([...source.matchAll(/t\("([A-Za-z.]+[A-Za-z])"/g)].map(m => m[1]))];
check(keys.length >= 60, "the script asks for its texts through t(): " + keys.length);
const kindsInCode = [...new Set([...kindNames, "guide", "notice", "tourist", "placename", "wayfinding", "variable", "marking", "all"])];
for (const [locale, texts] of Object.entries(languages)) {
    for (const k of [`Assets.NAME[${categoryName}]`, `Assets.DESCRIPTION[${categoryName}]`, `SubServices.NAME[${categoryName}]`, `SubServices.DESCRIPTION[${categoryName}]`]) {
        check(typeof texts[k] === "string", `${locale}: ${k}`);
    }
    for (const key of keys) {
        check(typeof texts["CDLC.TS." + key] === "string" && texts["CDLC.TS." + key].length > 0, `${locale}: ${key} is translated`);
    }
    for (const kind of kindsInCode) {
        check(typeof texts[`CDLC.TS.UI.Group.${kind}`] === "string", `${locale}: the kind ${kind} has a name`);
    }
}
check(languages["zh-HANS"][`SubServices.NAME[${categoryName}]`] === "CDL街景设施" && languages["en-US"][`Assets.NAME[${categoryName}]`] === "CDL:StreetScape China Pack", "the tab's names");
check(Object.keys(languages["en-US"]).length === Object.keys(languages["zh-HANS"]).length, "both languages have the same keys");
for (const key of ["UI.Done", "UI.On", "UI.Off"]) {
    check(!("CDLC.TS." + key in languages["en-US"]), `the texts of 0.12.2's panel that nothing shows any more are gone: ${key}`);
}

// ------------------------------------------------------------------ the stylesheet

const css = readFileSync(target.style, "utf8");
const cssRules = css.replace(/\/\*[\s\S]*?\*\//g, "");
check(!/var\(--[^)]*,/.test(cssRules), "no var(--x, fallback) (the game's UI engine drops it)");
check(!/object-fit|grid-template|display:\s*grid|\bgap:/.test(cssRules), "no object-fit, grid or gap (the game's UI engine has none of them)");
const classes = new Set([...source.matchAll(/cdlts-[a-z0-9]+(?:-[a-z0-9]+)*/g)].map(m => m[0]));
check(classes.size >= 35, "the script uses its classes: " + classes.size);
for (const name of classes) {
    check(new RegExp("\\." + name + "(?![a-z0-9-])").test(cssRules), `the stylesheet styles .${name}`);
}
const rem = (rule, property) => {
    const m = new RegExp(rule.replace(/[.]/g, "\\.") + "\\s*\\{([^}]*)\\}").exec(cssRules);
    if (!m) return NaN;
    const p = new RegExp("(?:^|[;{\\s])" + property.replace(/[-]/g, "\\-") + ":\\s*([0-9.]+)rem").exec(m[1]);
    return p ? parseFloat(p[1]) : NaN;
};
const tileSize = rem(".cdlts-tile", "width"), gap = 6;
check(tileSize === rem(".cdlts-grid", "--assetMenuItemSize") && rem(".cdlts-tile-img", "width") === rem(".cdlts-grid", "--assetMenuImageSize"), "the tiles are as large as the game's variables say");
check(6 * tileSize + 5 * gap <= rem(".cdlts-grid-rows", "width") && 3 * tileSize + 2 * gap <= rem(".cdlts-grid-rows", "height"), "three rows of six tiles fit the grid");
check(rem(".cdlts-grid-rows", "width") + rem(".cdlts-pages", "width") + rem(".cdlts-pages", "margin-left") + 24 <= rem(".cdlts-flypanel", "width"), "the grid with its page marks fits the panel");
check(source.includes(`"${rem(".cdlts-flypanel", "width")}rem"`) && rem(".cdlts-fly-open", "width") > rem(".cdlts-flypanel", "width"), "the panel is given the stylesheet's widths inline too");
// 0.12.90: the editor's window is wider; its inline width is the script's editorPanelRem and the fly-out around it is 10 rem wider, as the list's is
check(Number((/const editorPanelRem = (\d+)/.exec(source) || [])[1]) === rem(".cdlts-fly-wide", "width") - 10 && rem(".cdlts-fly-wide", "width") > rem(".cdlts-fly-open", "width") && cssRules.indexOf(".cdlts-fly-wide") > cssRules.indexOf(".cdlts-fly-open"), "the editor's window is wider than the list's, its fly-out is 10 rem wider than its panel, and the rule comes after the open one");
check(10 * rem(".cdlts-support", "width") + 9 * 4 + 24 <= rem(".cdlts-flypanel", "width"), "the automatic support and nine styles fit on one line (the forms are six, and the most variants of a form eight)");
check(/\.cdlts-tabs \{[^}]*--minTabWidth: 0rem/.test(cssRules) && /\.cdlts-tabs button \{[^}]*min-width: 0/.test(cssRules), "the tabs share the editor's width");

// The game's focus rule over every tree drawn above
if (process.env.REPORT_FOCUS) {
    for (const [name, times] of crowded) {
        console.log("  focus: " + name + " hosts " + crowdedSizes.get(name) + " focusable children (drawn " + times + "x)");
    }
}
check(crowded.size === 0, "no row or tooltip hosts more than one focusable child (the game's focus rule): " + [...crowded.keys()].slice(0, 8).join("; ") + (crowded.size > 8 ? "; ... " + crowded.size + " in all" : ""));

console.log(`${checks} checks, ${failures} failed`);
process.exit(failures === 0 ? 0 : 1);
