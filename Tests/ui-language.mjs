// Offline check that the panel is native in both languages (0.12.80; the owner: "有没有原生英文支持"). CDLC.TS.mjs is loaded with the game's localisation answering from the language file of the
// language under test (en-US or zh-HANS, a key that is not in the file is a failure) and the bindings carrying what the C# side sends for that language: the catalogue, the supports and the styles
// with both names, the Lang binding, the editor and the lines of help in that language. Every panel is rendered the way React renders it (the dock with its tabs and grid, the editor of a sign
// about to be placed and of one being edited, the keys, the restore dialog, the settings), with the game's own components found and without them, and every string the render holds (text and the
// props that carry text) is collected. With the game in English there is no Chinese in any of them except the standard's own designation of a figure (禁7, 示17, 路8, 线5, 旅9); with the
// game in Chinese the names are the Chinese ones. A source scan checks every t("UI....") key against both language files and its fallback against the English one.
// Usage: node Tests/ui-language.mjs <repo root>
import { readFileSync } from "node:fs";
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

const files = {
    en: JSON.parse(readFileSync(join(root, "lang", "en-US.json"), "utf8")),
    zh: JSON.parse(readFileSync(join(root, "lang", "zh-HANS.json"), "utf8"))
};
const cjk = /[\u3000-\u303F\u3400-\u4DBF\u4E00-\u9FFF\uFF00-\uFFEF]/;
// The standard's own designation of a figure: an identifier that stays in an English game.
const designation = /[禁示警路线辅旅]\d+(?:-\d+)?/g;
// Where Chinese stays in an English game, and why. Empty by intent.
const allowed = [];

// ------------------------------------------------------------------ the stubs (as in ui-smoke.mjs, with the localisation answering from the language file)

const triggers = [];
const values = {};
let language = "en";
const missing = new Set();

const flat = children => children.flat(Infinity).filter(c => c !== null && c !== undefined && c !== false);
const element = (type, props, ...children) => {
    const all = Object.assign({}, props || {});
    if (children.length > 0) {
        all.children = flat(children);
    }
    return { type, props: all };
};
const native = name => {
    const component = props => ({ type: "ui." + name, props, host: true });
    Object.defineProperty(component, "name", { value: "native " + name });
    return component;
};
const uiNames = ["Panel", "PanelSection", "PanelSectionRow", "PanelFoldout", "Button", "Tooltip", "Scrollable", "Dropdown", "DropdownToggle", "DropdownItem", "ConfirmationDialog"];
const makeUi = full => {
    const ui = {};
    for (const n of uiNames) {
        if (full || !(n.startsWith("Dropdown") || n === "ConfirmationDialog")) {
            ui[n] = native(n);
        }
    }
    return ui;
};
const gameClassesStub = { item: "item_x", itemInner: "inner_x", thumbnail: "thumb_x", row: "row_x", pageIndicator: "pi_x", toggleStates: "toggle_x", dropdownToggle: "dt_x" };

function setGlobals(found, full) {
    globalThis.window = {
        React: {
            createElement: element,
            Fragment: "Fragment",
            useState: initial => [initial, () => {}],
            useEffect: () => {},
            useRef: initial => ({ current: initial }),
            Component: class { constructor(props) { this.props = props; } }
        },
        "cs2/api": {
            bindValue: (group, name, fallback) => ({ group, name, fallback }),
            useValue: binding => (binding.name in values ? values[binding.name] : binding.fallback),
            trigger: (group, name, ...args) => triggers.push([group, name, ...args])
        },
        "cs2/l10n": {
            useLocalization: () => ({
                translate: (key, fallback) => {
                    if (!(key in files[language])) {
                        missing.add(key);
                    }
                    return files[language][key] ?? fallback;
                }
            })
        },
        "cs2/ui": makeUi(full),
        "cs2/modding": { getModule: (path, name) => (found ? (name === "classes" ? gameClassesStub : native("game:" + name)) : undefined) },
        innerHeight: 1080
    };
    globalThis.document = { body: {}, activeElement: null, documentElement: {} };
    globalThis.getComputedStyle = () => ({ fontSize: "1px" });
}

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
        return render(node.type(node.props));
    }
    const props = Object.assign({}, node.props);
    for (const key of ["children", "header", "footer", "left", "right", "tooltip", "content"]) {
        if (props[key] !== undefined) {
            props[key] = render(props[key]);
        }
    }
    return { type: node.type, props, children: props.children === undefined ? [] : [].concat(props.children) };
}

// Every string a rendered tree holds: the text of its children and the props that are text (not class names, keys, addresses or handlers).
const quiet = new Set(["className", "key", "src", "style", "theme", "id", "type", "ref"]);
function strings(node, out = []) {
    if (Array.isArray(node)) {
        node.forEach(n => strings(n, out));
        return out;
    }
    if (typeof node === "string") {
        out.push(node);
        return out;
    }
    if (!node || typeof node !== "object") {
        return out;
    }
    for (const [key, value] of Object.entries(node.props || {})) {
        if (quiet.has(key) || typeof value === "function" || key === "children") {
            continue;
        }
        if (typeof value === "string") {
            out.push(value);
        } else if (value && typeof value === "object") {
            strings(value, out);
        }
    }
    strings(node.children, out);
    return out;
}

const draw = component => render(element(component, null));

// ------------------------------------------------------------------ the data the C# side would send for a language

const designations = ["禁", "示", "警"];
const catalogueOf = lang => Array.from({ length: 40 }, (_, i) => {
    // A standard sign (the designation stays), a sign of a series with a code of words, and a figure of an annex: the three kinds of code.
    const kind = i % 3;
    const zhCode = kind === 0 ? designations[i % 3] + (i + 1) : kind === 1 ? "灯箱" : "图B." + (i + 1);
    const enCode = kind === 0 ? zhCode : kind === 1 ? "Light box" : "Fig. B." + (i + 1);
    return { id: "t" + i, code: zhCode, ce: enCode, zh: "牌" + i, en: "Sign " + i, kw: [], py: "p", grp: i < 30 ? "prohibition" : "warning", thumb: i % 5 === 0 ? "" : `coui://cdlcts/s-${i}-12345678.png` };
});
const supportsJson = [
    { id: "pole.single", zh: "单柱式", en: "Single post", fam: "post", thumb: "", mid: true },
    { id: "pole.double", zh: "双柱式", en: "Double post", fam: "post", thumb: "coui://cdlcts/p-1.png", mid: true },
    { id: "cantilever.single", zh: "悬臂式", en: "Cantilever", fam: "cantilever", thumb: "" },
    { id: "cantilever.octa", zh: "单悬臂式（八角锥杆）", en: "Octagonal cantilever", fam: "cantilever", thumb: "" },
    { id: "gantry.portal", zh: "门架式", en: "Gantry", fam: "gantry", thumb: "" },
    { id: "gantry.limit", zh: "限高架（黄黑）", en: "Height-limit frame (yellow-black)", fam: "gantry", thumb: "" }
];
const profileList = [
    { id: "plain", zh: "通用（GB 5768.2）", en: "Plain (GB 5768.2)", pe: "", re: "", fe: "" },
    { id: "shanghai", zh: "上海", en: "Shanghai", pe: "std", re: "", fe: "supplier" },
    { id: "xuzhou", zh: "徐州 / 江苏", en: "Xuzhou / Jiangsu", pe: "photo", re: "estimate", fe: "std" }
];
// What the C# side writes in each language: the editor's title, the labels of its fields, the boards' names, the line of help and the keys' rows.
const words = {
    en: { title: "Exit advance board", road: "Road", g: "National road", s: "Provincial road", n: "No number", code: "Number", lane: "Exit lane", free: "Free text", board1: "Small passenger vehicle lane", board2: "No entry", hint: "Click to place.", keys: [["Left", "Place"], ["[  ]", "Turn"], ["Right / Esc", "Leave"]] },
    zh: { title: "出口预告标志", road: "道路", g: "国道", s: "省道", n: "无编号", code: "编号", lane: "直出车道", free: "自由文本", board1: "小型客车车道", board2: "禁止驶入", hint: "单击放置。", keys: [["左键", "放置"], ["[  ]", "旋转"], ["右键 / Esc", "退出"]] }
};
const editorOf = (lang, mode, withBoards) => {
    const w = words[lang];
    const base = {
        id: 7, mode, code: lang === "zh" ? "路42" : "路42", title: w.title, preview: "coui://cdlcts/edit-3.png", pw: 676, ph: 768, plan: "pole.double",
        fields: [
            { k: "road", l: w.road, t: "choice", v: "g", max: 0, ch: [{ v: "g", l: w.g }, { v: "s", l: w.s }, { v: "name", l: w.n }] },
            { k: "code", l: w.code, t: "text", v: "G204", max: 5 },
            { k: "lane", l: w.lane, t: "flag", v: "0", max: 0 },
            { k: "free", l: w.free, t: "text", v: "ABC", max: 0 }
        ]
    };
    if (withBoards) {
        base.boards = [{ m: 0, code: lang === "zh" ? "示26" : "示26", title: w.board1, lane: 0, x: 6 }, { m: 1, code: lang === "zh" ? "禁5" : "禁5", title: w.board2, lane: 2, x: 9 }];
        base.sel = 0;
        base.lanes = [9, 6, 3];
        base.laneState = "ok";
        base.lane = 0;
        base.warn = [];
        // 0.12.85: a post at the kerb has the margin and the position along, each with a slider and a box
        base.kerb = -1.3;
        base.along = 3.5;
    }
    return base;
};

function reset(lang) {
    for (const k of Object.keys(values)) delete values[k];
    values.Lang = lang;
    values.Catalog = JSON.stringify(catalogueOf(lang));
    values.Supports = JSON.stringify(supportsJson);
    values.Profiles = JSON.stringify(profileList);
    values.Profile = "shanghai";
    values.Selected = "t3";
    values.Support = "pole.double";
    values.Mode = 0;
    values.ToolActive = true;
    values.Hint = JSON.stringify({ t: words[lang].hint, bad: false, k: words[lang].keys });
    values.Custom = JSON.stringify({ add: "coui://cdlcts/add-r3.png", folder: "C:/Users/p/ModsData/CDLC.TS/Custom", files: [{ n: "company.png", w: 800, h: 600, kb: 120 }], note: "", bad: false });
    values.ClearAreas = 3;
    values.ClearHidden = 41;
    values.SaveData = JSON.stringify({ status: "restored:120:5:2", offers: [{ name: "My city", time: "2026-10-28 09:00", score: 80, items: 40 }], away: [{ i: 0, why: "upgraded", items: 3, kerb: false, prefab: "Medium Road", x: 1, z: 2 }], warn: { leak: 3, plants: 12 } });
    triggers.length = 0;
}

// ------------------------------------------------------------------ run: both languages, with the game's modules and without them

const fresh = { open: true, page: 0, kind: "all", query: "", editorTab: "sign", confirming: false, flyout: false, settings: false, showList: false, seenEditor: "", editor: null, top: 0, leftAt: 0, importing: false };
const seen = { en: new Set(), zh: new Set() };

for (const lang of ["en", "zh"]) {
    for (const [found, full] of [[true, true], [false, false]]) {
        const label = `[${lang === "en" ? "en-US" : "zh-HANS"}${found ? ", game modules" : ", fallback"}] `;
        language = lang;
        missing.clear();
        setGlobals(found, full);
        const module = await import(target.url(`?${lang}${found ? "f" : "n"}`));
        const { setUi, Dock, EditorPanel, KeysPanel, RestoreDialog, SettingsPanel, MainPanel } = module;
        reset(lang);

        const scenes = [];
        const scene = (name, component, state, before) => {
            setUi(Object.assign({}, fresh, state || {}));
            if (before) before();
            const list = strings(draw(component));
            if (process.env.LANG_DUMP && label.includes(process.env.LANG_DUMP)) console.log(label + name + ": " + JSON.stringify(list));
            scenes.push([name, list]);
        };
        scene("the dock with the grid", Dock, { flyout: true });
        scene("the grid, a page on", MainPanel, { page: 1 });
        scene("the dock, warning tab", Dock, { flyout: true, kind: "warning" });
        scene("the dock, custom tab", Dock, { flyout: true, kind: "custom" });
        scene("the dock, settings", Dock, { flyout: true, settings: true });
        scene("the settings", SettingsPanel, { settings: true });
        scene("a sign about to be placed", EditorPanel, {}, () => { values.Editor = JSON.stringify(editorOf(lang, "draft", false)); });
        scene("its support page, a post", EditorPanel, { editorTab: "support" }, () => { values.Support = "pole.double"; });
        scene("its support page, automatic", EditorPanel, { editorTab: "support" }, () => { values.Support = ""; });
        scene("its support page, a gantry", EditorPanel, { editorTab: "support" }, () => { values.Support = "gantry.limit"; });
        scene("a sign being edited", EditorPanel, {}, () => { values.Editor = JSON.stringify(editorOf(lang, "item", true)); values.Support = "pole.double"; });
        scene("a sign being edited, its boards", EditorPanel, { editorTab: "support" }, () => { values.Selected = "t3"; });
        // 0.12.85: the other position rows of a support that stands: the distance from the centre line, and a board on a wall
        scene("a support the margin does not describe", EditorPanel, { editorTab: "support" }, () => { values.Editor = JSON.stringify(Object.assign(editorOf(lang, "item", true), { kerb: undefined, centre: 7.2, centreMax: 14 })); });
        scene("a board on a wall", EditorPanel, { editorTab: "support" }, () => { values.Editor = JSON.stringify(Object.assign(editorOf(lang, "item", true), { wall: 1, kerb: undefined, along: 1.2, lower: 3.5 })); });
        scene("the keys", KeysPanel, {});
        if (full) scene("the restore dialog", RestoreDialog, { confirming: true });

        const all = [];
        for (const [name, list] of scenes) {
            check(list.length > 0, label + name + " draws something");
            for (const s of list) all.push([name, s]);
        }
        const text = all.map(x => x[1]).join("\n");
        check(missing.size === 0, label + "every key the panels ask the localisation for is in the language file: " + [...missing].slice(0, 5).join(", "));

        if (lang === "en") {
            const leaks = all.filter(([, s]) => cjk.test(s.replace(designation, "")) && !allowed.some(a => a.test(s)));
            for (const [name, s] of leaks.slice(0, 20)) {
                check(false, label + "Chinese in the English panel (" + name + "): " + s);
            }
            check(leaks.length === 0, label + "no Chinese in any string of the English panels (" + all.length + " strings): " + leaks.length);
            check(text.includes("Sign 3") && text.includes("Double post") && text.includes("Shanghai"), label + "the English names of a sign, a support and a style are the ones shown");
            check(text.includes("Light box") && text.includes("Fig. B."), label + "a code that is words is shown in English");
            check(text.includes("Posts") && text.includes("Cantilevers"), label + "the forms of the supports are named in English");
            check(text.includes("Automatic (Double post)"), label + "automatic names what the planner chose, in English");
        } else {
            check(text.includes("牌3") && text.includes("双柱式") && text.includes("上海"), label + "the Chinese names of a sign, a support and a style are the ones shown");
            check(text.includes("Sign 3"), label + "the tooltip keeps the English name as its second line");
            check(text.includes("灯箱") && text.includes("图B."), label + "the codes are the Chinese ones");
            check(!text.includes("Light box") && !text.includes("Fig. B."), label + "no English code in the Chinese panel");
            check(text.includes("立柱式") && text.includes("悬臂式") && text.includes("(双柱式)"), label + "the forms of the supports and the planner's choice are named in Chinese");
        }
        seen[lang].add(label);
    }
}

// ------------------------------------------------------------------ the keys and the fallbacks in the source

const source = readFileSync(target.source, "utf8");
const call = /\bt\(\s*"(UI\.[A-Za-z0-9_.]+)"\s*(?:\+[^,]*)?,\s*("(?:[^"\\]|\\.)*")/g;
let literals = 0;
for (const m of source.matchAll(call)) {
    literals++;
    const key = "CDLC.TS." + m[1];
    const fallback = JSON.parse(m[2]);
    for (const [lang, file] of [["en", "en-US"], ["zh", "zh-HANS"]]) {
        check(key in files[lang], `${m[1]} is in ${file}.json`);
    }
    check(!cjk.test(fallback), `${m[1]}: the fallback has no Chinese: ${fallback}`);
    check(files.en[key] === undefined || files.en[key] === fallback, `${m[1]}: the fallback is the English file's text`);
}
check(literals > 100, "the t() calls with a literal key were found: " + literals);

// A literal Chinese string in the script (outside a comment) would show in an English game.
const lines = source.split("\n");
let literalChinese = 0;
lines.forEach((line, i) => {
    const code = line.replace(/\/\/.*$/, "");
    for (const m of code.matchAll(/"((?:[^"\\]|\\.)*)"|'((?:[^'\\]|\\.)*)'|`((?:[^`\\]|\\.)*)`/g)) {
        const text = m[1] ?? m[2] ?? m[3] ?? "";
        if (cjk.test(text.replace(designation, ""))) {
            literalChinese++;
            console.log(`  FAIL UI/src/ts-ui.js:${i + 1}: Chinese in a string literal: ${text.slice(0, 60)}`);
            failures++;
        }
    }
});
checks++;

console.log(`${checks} checks, ${failures} failed`);
process.exit(failures === 0 ? 0 : 1);
