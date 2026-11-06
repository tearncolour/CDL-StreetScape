// Offline layout review of the dock (0.12.61): the real component tree of CDLC.TS.mjs, drawn with stubbed game components, becomes HTML with the mod's own stylesheet (1 rem = 1 px, the game's
// panel colours and its right-hand column's round buttons imitated). Not a game capture: it shows the proportions and the order of things, nothing of the game's own look.
// Usage: node Tests/preview-dock.mjs <repo root> <output folder>; then open out/<folder>/*.html in a browser (or: msedge --headless --screenshot=x.png --window-size=1100,820 file:///...)
import { readFileSync, writeFileSync, mkdirSync, existsSync } from "node:fs";
import { join, resolve } from "node:path";
import { uiTarget } from "./ui-target.mjs";

const root = resolve(process.argv[2] || ".");
const target = uiTarget(root);
const out = resolve(process.argv[3] || join(root, "out/dock-preview"));
mkdirSync(out, { recursive: true });
const values = {};
// The game's slider as a track and a thumb (TS_PREVIEW_SLIDER=1; without it the module is "not found" and the rows are the buttons' fallback, as in a game whose slider moved).
const stubSlider = props => ({ type: "div", props: { className: "g-slider", style: { position: "relative", height: "6px", margin: "10px 12px", background: "rgba(255,255,255,.25)", borderRadius: "3px" }, children: [
    { type: "div", props: { style: { position: "absolute", left: (100 * (props.value - props.start) / (props.end - props.start)) + "%", top: "-5px", width: "16px", height: "16px", marginLeft: "-8px", borderRadius: "50%", background: "#1e83aa" } } }] } });
const flat =c => c.flat(Infinity).filter(x => x !== null && x !== undefined && x !== false);
const element = (type, props, ...children) => { const p = Object.assign({}, props || {}); if (children.length) p.children = flat(children); return { type, props: p }; };
const host = name => { const f = props => ({ type: "ui." + name, props, host: true }); Object.defineProperty(f, "name", { value: name }); return f; };
globalThis.window = {
    React: { createElement: element, Fragment: "Fragment", useState: i => [i, () => {}], useEffect: () => {}, useRef: i => ({ current: i }), Component: class { constructor(p) { this.props = p; } } },
    "cs2/api": { bindValue: (g, n, f) => ({ n, f }), useValue: b => (b.n in values ? values[b.n] : b.f), trigger: () => {} },
    "cs2/l10n": { useLocalization: () => ({ translate: (k, f) => f }) },
    "cs2/ui": Object.fromEntries(["Panel", "PanelSection", "PanelSectionRow", "PanelFoldout", "Button", "Tooltip", "Scrollable", "Dropdown", "DropdownToggle", "DropdownItem", "ConfirmationDialog"].map(n => [n, host(n)])),
    "cs2/modding": { getModule: (path, name) => (name === "classes" ? { item: "rm-item", toggleStates: "rm-btn", itemInner: "", thumbnail: "", row: "", pageIndicator: "pi" } : name === "Slider" && process.env.TS_PREVIEW_SLIDER ? stubSlider : undefined) },
    innerHeight: 1080
};
globalThis.document = { body: {}, activeElement: null, documentElement: {} };
globalThis.getComputedStyle = () => ({ fontSize: "1px" });
const mod = await import(target.url());

function render(node) {
    if (Array.isArray(node)) return node.map(render);
    if (!node || typeof node !== "object") return node;
    if (typeof node.type === "function") return render(node.type(node.props));
    const p = Object.assign({}, node.props);
    for (const k of ["children", "header", "footer", "left", "right", "content"]) if (p[k] !== undefined) p[k] = render(p[k]);
    return { type: node.type, props: p };
}
// The game's own glyph files (Media/Mouse/*.svg) from the install named by CSII_INSTALLATIONPATH, as a file URL.
const rawInstall = process.env.CSII_INSTALLATIONPATH || "";
const install = rawInstall[0] === "/" && rawInstall[2] === "/" ? rawInstall[1].toUpperCase() + ":" + rawInstall.slice(2) : rawInstall;
const glyphs = "file:///" + encodeURI(install) + "/Cities2_Data/Content/Game/UI/";
const src = s => s.startsWith("file:///") ? s : s.startsWith("coui://cdlcts-icons/") ? "file:///" + join(root, "Icons", s.slice(20)).replace(/\\/g, "/") : s.startsWith("Media/") ? glyphs + s : "";
const esc = s => String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;");
function html(n) {
    if (Array.isArray(n)) return n.map(html).join("");
    if (n === null || n === undefined || n === false) return "";
    if (typeof n !== "object") return esc(n);
    const p = n.props || {}, cls = p.className ? ` class="${esc(p.className)}"` : "";
    const style = p.style ? ` style="${Object.entries(p.style).map(([k, v]) => k.replace(/[A-Z]/g, c => "-" + c.toLowerCase()) + ":" + String(v).split("rem").join("px")).join(";")}"` : "";
    const kids = html(p.children);
    switch (n.type) {
        case "ui.Panel": return `<div class="g-panel${p.className ? " " + esc(p.className) : ""}"${style}><div class="g-head">${html(p.header)}</div><div class="g-body">${kids}</div></div>`;
        case "ui.Button": return `<button${cls.replace('class="', 'class="' + (p.selected ? "sel " : ""))}>${p.src ? `<img src="${src(p.src)}">` : ""}${kids}</button>`;
        case "ui.Tooltip": return kids;
        case "ui.Scrollable": return `<div class="g-scroll"${style}>${kids}</div>`;
        case "ui.PanelSectionRow": return `<div class="g-row"><span>${html(p.left)}</span><span>${html(p.right)}</span></div>`;
        case "ui.PanelSection": return `<div${cls}>${kids}</div>`;
        case "ui.PanelFoldout": return `<div class="g-row"><b>${html(p.header)}</b></div>${kids}`;
        case "Fragment": return kids;
        case "img": return `<img${cls}${style} src="${src(p.src || "")}">`;
        case "input": return `<input${cls} placeholder="${esc(p.placeholder || "")}" value="${esc(p.defaultValue || p.value || "")}">`;
        default: return typeof n.type === "string" && !n.type.startsWith("ui.") ? `<${n.type}${cls}${style}>${kids}</${n.type}>` : `<div${cls}>${kids}</div>`;
    }
}
const page = (body, stage = [1100, 820]) => `<!doctype html><meta charset="utf-8"><style>
html,body{margin:0;background:#3a5a42;font:13px "Microsoft YaHei",sans-serif;color:#eee} .stage{position:relative;width:${stage[0]}px;height:${stage[1]}px;overflow:hidden}
:root{--panelColorDark:rgba(24,33,51,.92);--panelColorNormal:rgba(42,55,83,.92)}
.rm-item{width:46px;height:46px;border-radius:50%;background:var(--panelColorNormal)} .rm-item+.rm-item{margin-top:10px}
.rm-btn{width:46px;height:46px;border:0;border-radius:50%;background:transparent;display:flex;align-items:center;justify-content:center;padding:0} .rm-btn img{width:36px;height:36px} .rm-btn.sel,button.sel{background:#1e83aa}
.g-panel{background:rgba(35,43,61,.94);border-radius:6px;box-shadow:0 2px 8px rgba(0,0,0,.5)} .g-head{padding:10px 12px;font-weight:bold;border-bottom:1px solid rgba(255,255,255,.1);display:flex;align-items:center}
.g-row{display:flex;justify-content:space-between;padding:6px 12px} .g-scroll{overflow:hidden} button{color:#eee;border:0;background:rgba(255,255,255,.08);cursor:default}
${readFileSync(target.style, "utf8").replace(/(-?\d+(?:\.\d+)?)rem/g, "$1px")}
.cdlts-tile{border-radius:4px;background:rgba(255,255,255,.08)} .cdlts-tile.sel{background:#1e83aa} .cdlts-tile img{display:block} .cdlts-text{color:#fff;border:0}
</style><div class="stage">${body}</div>`;

function shot(name, setup, component = mod.Dock, stage = [1100, 820]) {
    for (const k of Object.keys(values)) delete values[k];
    const cat = Array.from({ length: 40 }, (_, i) => ({ id: "t" + i, code: "禁" + (i + 1), zh: "牌" + i, en: "", kw: [], py: "", grp: ["prohibition", "warning", "mandatory", "guide", "auxiliary", "notice", "tourist", "placename", "wayfinding", "variable", "brt", "toll"][i % 12], thumb: "" }));
    values.Catalog = JSON.stringify(cat);
    values.Selected = "t3";
    values.Mode = 0;
    values.ToolActive = true;
    values.Supports = "[]";
    Object.assign(mod.ui, { open: true, flyout: false, settings: false, showList: false, seenEditor: "", editor: null, top: 0, kind: "all", page: 0, query: "", editorTab: "sign" });
    setup(cat);
    const tree = [].concat(component).map(c => render(element(c, null)));
    writeFileSync(join(out, name + ".html"), page(html(tree), stage));
}
shot("1-opened", () => {});
shot("2-entries", () => { mod.ui.flyout = true; mod.ui.kind = "warning"; mod.ui.showList = true; });
shot("3-settings", () => {
    values.Editor = JSON.stringify({ id: 7, mode: "draft", code: "禁4", title: "禁止驶入", preview: "", pw: 100, ph: 100, plan: "pole.single", fields: [{ k: "speed", l: "限速", t: "choice", v: "60", max: 0, ch: [{ v: "40", l: "40" }, { v: "60", l: "60" }, { v: "80", l: "80" }] }, { k: "t", l: "文字", t: "text", v: "限速", max: 8 }] });
});
shot("4-keys", () => {
    values.Hint = JSON.stringify({ t: "单击放置", bad: false, k: [["左键", "放置 / 并入立架"], ["← →", "靠近 / 远离（Shift 0.5 m）"], ["[  ]", "旋转"], ["按住 Alt", "另立新架"], ["按住 Ctrl 单击", "吸取样式"], ["Ctrl+Z / Ctrl+Y", "撤销 / 重做"], ["右键 / Esc", "退出"]] });
}, mod.KeysPanel);
// 0.12.90: the card in two columns, with the keys a post on a road can have at most (HintText.Keys, kind "place": 15 rows).
shot("4b-keys-many", () => {
    values.Hint = JSON.stringify({ t: "单击放置", bad: false, k: [["左键", "放置 / 并入"], ["← → ↑ ↓", "平移（Shift）"], ["↑ ↓", "升降（Shift）"], ["按住 Alt", "另立新架"], ["[  ]", "旋转"], ["Shift/Alt  [  ]", "步进 5°/1°"],
        ["\\", "掉头"], [";  '", "换立架"], ["<  >", "离路缘"], ["/", "路中"], ["-  =", "倾角"], ["Shift  -  =", "步进 5°"], ["Ctrl+Z / Ctrl+Y", "撤销 / 重做"], ["右键 / Esc", "退出"], ["按住 Ctrl 单击", "多选"]] });
}, [mod.Dock, mod.KeysPanel], [1920, 1080]);
const text = (k, l, v, max = 20) => ({ k, l, t: "text", v, max });
shot("5-exit", () => {
    values.Editor = JSON.stringify({
        id: 9, mode: "draft", code: "路50", title: "双出口枢纽式互通立体交叉的出口预告", preview: "coui://cdlcts-icons/place.png", pw: 333, ph: 264, plan: "pole.single",
        info: ["牌面 3.33 × 2.64 m，连编号牌总高 3.24 m", "字高：按所在道路限速取档"],
        fields: [
            text("r1", "第一出口：道路编号（空为不设）", "G55"), text("n1", "第一出口：地名", "永州"), text("m1", "第一出口：地名二（可空）", ""),
            text("r2", "第二出口：道路编号（空为不设）", "G55"), text("n2", "第二出口：地名", "广州"), text("m2", "第二出口：地名二（可空）", ""),
            text("d", "距离（空为不设）", "3"),
            { k: "u", l: "单位", t: "choice", v: "km", max: 0, ch: [{ v: "km", l: "km" }, { v: "m", l: "m" }] },
            { k: "p", l: "出口位置", t: "choice", v: "r", max: 0, ch: [{ v: "r", l: "右侧出口（如图 271）" }, { v: "l", l: "左侧出口（如图 272）" }, { v: "b", l: "两侧出口（如图 273）" }] },
            text("no", "出口编号（空为不设）", "133"), text("sf", "编号后缀（A、B、A-B）", "")]
    });
    values.Hint = JSON.stringify({ t: "单击放置", bad: false, k: [["左键", "放置 / 并入立架"], ["← →", "靠近 / 远离（Shift 0.5 m）"], ["[  ]", "旋转"], ["按住 Alt", "另立新架"], ["按住 Ctrl 单击", "吸取样式"], ["Ctrl+Z / Ctrl+Y", "撤销 / 重做"], ["右键 / Esc", "退出"]] });
}, [mod.Dock, mod.KeysPanel], [1920, 1080]);
// 0.12.75: the editor of the text light box (the owner's screenshot of 2 Nov: the text box ran over the next label, a black picture, three lines of notes). The real stylesheet, a plain <input> for the
// game's text input; the game's own engine (Coherent) has no automatic minimum size for a flex item: the shot cannot show that, the stacked block's structure is tested in ui-smoke.mjs.
shot("6-textbox", () => {
    values.Editor = JSON.stringify({
        id: 14, mode: "draft", code: "文字灯箱", title: "文字灯箱（发光字）", preview: process.env.TS_PREVIEW_PNG ? "file:///" + process.env.TS_PREVIEW_PNG : "", pw: 512, ph: 128, plan: "pole.single",
        info: ["牌面 2.00 × 0.50 m"],
        fields: [
            { k: "text", l: "文字（\\n 换行）", t: "text", v: "文字灯箱", max: 40, w: true },
            { k: "h", l: "字高（m）", t: "text", v: "0.5", max: 5 }, { k: "sp", l: "字距（%）", t: "text", v: "0", max: 3 }, { k: "ls", l: "行距（%）", t: "text", v: "115", max: 3 },
            { k: "align", l: "对齐", t: "choice", v: "centre", max: 0, ch: [{ v: "centre", l: "居中" }, { v: "left", l: "靠左" }, { v: "right", l: "靠右" }] },
            { k: "glow", l: "发光颜色", t: "choice", v: "warm", max: 0, ch: [{ v: "warm", l: "暖白" }, { v: "white", l: "冷白" }, { v: "red", l: "红" }, { v: "gold", l: "金黄" }, { v: "blue", l: "蓝" }, { v: "pink", l: "粉" }, { v: "green", l: "绿" }] },
            { k: "depth", l: "厚度（cm）", t: "text", v: "6", max: 3 },
            { k: "tilt", l: "仰角（°）", t: "text", v: "12", max: 2, r: [0, 30, 1] }, { k: "tri", l: "三角支架", t: "flag", v: "0", max: 0 }]
    });
    values.Hint = JSON.stringify({ t: "单击放置", bad: false, k: [] });
}, [mod.Dock], [760, 900]);
console.log("written to " + out);
// 0.12.78: the support tab of an edited text light box on a single cantilever (the owner's screenshot of 2 Nov, "精简文本"): the summary in one line, no instruction paragraph, no numpad in the labels, the key card in a few words.
shot("7-support-tab", () => {
    mod.ui.editorTab = "support";
    values.Editor = JSON.stringify({
        id: 15, mode: "item", code: "文字灯箱", title: "文字灯箱（发光字）", preview: "", pw: 512, ph: 128, plan: "pole.single", hung: true, onArm: false, laneState: "ok", lanes: [9, 6, 3, 0, -3, -6], lane: 0, max: 4,
        supportInfo: ["单悬臂式 · 立柱 Φ426 · 离地 5.5 m · 臂 12.0 m"],
        fields: [], boards: [{ m: 0, code: "文字灯箱", title: "文字灯箱（发光字）", lane: 255, x: 8.1 }], sel: 0, warn: [],
        painted: true, finish: 0, blackBack: false, root: 0, rootable: true, leans: true, tilt: 5, reachKind: "arm", reach: 12, reachMin: 1, reachMax: 16, reachStep: 0.5, reachHand: false, clear: 5.5, clearMin: 3, clearMax: 9, clearStep: 0.1, clearHand: false
    });
    values.Hint = JSON.stringify({ t: "编辑中：选中的牌子沿横梁移动，自动避开其他牌子。", bad: false, k: [["左键", "选牌 / 立架"], ["← →", "沿梁移动（Shift 加大）"], ["↑ ↓", "升降（Shift 0.5 m）"], ["Ctrl+Z / Ctrl+Y", "撤销 / 重做"], ["右键 / Esc", "退出"]] });
}, [mod.Dock, mod.KeysPanel], [1100, 900]);
// 0.12.78: the span of a gantry, edited (set by hand: 22 m, the button not marked) and about to be placed (the planner's 13.7 m, the button marked). TS_PREVIEW_SLIDER=1 draws the game's slider as a track.
const gantry = { hung: true, onArm: false, laneState: "ok", lanes: [9, 6, 3, 0, -3, -6], lane: 0, max: 4, plan: "gantry.portal", pw: 512, ph: 128, preview: "", fields: [], warn: [], sel: 0, painted: true, finish: 0, blackBack: false, root: 0, rootable: true, leans: false, tilt: 0, reachKind: "span", reachMin: 6, reachMax: 40, reachStep: 0.5, clear: 5.5, clearMin: 3, clearMax: 9, clearStep: 0.1, clearHand: false };
const gantryHint = () => { values.Hint = JSON.stringify({ t: "编辑中：选中的牌子沿横梁移动，自动避开其他牌子。", bad: false, k: [["左键", "选牌 / 立架"], ["Ctrl+Z / Ctrl+Y", "撤销 / 重做"], ["右键 / Esc", "退出"]] }); };
shot("8-gantry-edit", () => {
    mod.ui.editorTab = "support";
    values.Editor = JSON.stringify(Object.assign({}, gantry, { id: 16, mode: "item", code: "路42", title: "出口预告标志", supportInfo: ["门架式 · 跨度 22.0 m"], boards: [{ m: 0, code: "路42", title: "出口预告标志", lane: 255, x: 8.1 }], reach: 22, reachHand: true, clear: 4.5, clearHand: true }));
    gantryHint();
}, [mod.Dock], [760, 900]);
shot("9-gantry-place", () => {
    mod.ui.editorTab = "support";
    values.Editor = JSON.stringify(Object.assign({}, gantry, { id: 17, mode: "draft", code: "路42", title: "出口预告标志", supportInfo: ["门架式 · 跨度 13.7 m"], boards: [], reach: 13.7, reachHand: false }));
    gantryHint();
}, [mod.Dock], [760, 900]);

// 0.12.78: the editor of the construction hoarding with its nine styles and the flower troughs (the template's fields as the editor gets them: Geo/MarkingTemplates.Hoarding.cs): a dropdown for the style, buttons for
// the few short choices, the gate's width, the plants and the trough side only with what they belong to (here a gate with troughs on the ground, so all of them show).
const choice = (k, l, v, ch) => ({ k, l, t: "choice", v, max: 0, ch: ch.map(([cv, cl]) => ({ v: cv, l: cl })) });
shot("8-hoarding", () => {
    values.Editor = JSON.stringify({
        id: 16, mode: "draft", code: "施工围挡", title: "施工围挡", preview: "", pw: 400, ph: 200, plan: "pole.single",
        fields: [
            choice("style", "样式", "gate", [["flat", "平板"], ["steel", "彩钢板"], ["module", "装配式"], ["pvc", "PVC 板"], ["mesh", "护栏网"], ["brick", "砖墙"], ["warn", "警示色"], ["lamp", "警示灯"], ["gate", "工地大门"]]),
            choice("colour", "颜色", "b", [["b", "蓝色"], ["w", "白色"], ["g", "绿色"]]),
            choice("height", "高度", "200", [["180", "1.8 m"], ["200", "2.0 m"], ["250", "2.5 m"]]),
            choice("post", "立柱间距", "3", [["2", "2 m"], ["3", "3 m"], ["4", "4 m"]]),
            choice("gate", "大门宽度", "6", [["4", "4 m"], ["6", "6 m"], ["8", "8 m"]]),
            choice("flowers", "花箱", "foot", [["none", "无"], ["foot", "地面花箱"], ["top", "顶部挂箱"]]),
            choice("plants", "植物", "flower", [["flower", "开花灌木（月季等）"], ["shrub", "常绿灌木"]]),
            choice("side", "花箱位置", "r", [["r", "右侧"], ["l", "左侧"], ["b", "两侧"]])]
    });
    values.Hint = JSON.stringify({ t: "单击放置", bad: false, k: [] });
}, [mod.Dock], [760, 900]);
// 0.12.80: the turn row of a board on a single post, edited (the facing 37.4 degrees: the box, the slider, the steps, half a turn and back) and of the sign about to be placed (15 degrees).
// TS_PREVIEW_SLIDER=1 draws the game's slider as a track.
const turning = { hung: false, onArm: false, laneState: "post", lanes: [], lane: 0, max: 4, plan: "pole.single", pw: 512, ph: 128, preview: "", fields: [], warn: [], sel: 0, painted: true, finish: 0, blackBack: false, root: 0, rootable: true, leans: false, tilt: 0, turns: true };
const turnKeys = () => { values.Hint = JSON.stringify({ t: "编辑中：选中的牌子在杆上转动。", bad: false, k: [["左键", "选牌 / 立架"], ["[  ]", "旋转（Shift 5°，Alt 1°）"], ["\\", "掉头"], ["Ctrl+Z / Ctrl+Y", "撤销 / 重做"]] }); };
shot("10-turn-edit", () => {
    mod.ui.editorTab = "support";
    values.Editor = JSON.stringify(Object.assign({}, turning, { id: 18, mode: "item", code: "禁5", title: "禁止驶入", supportInfo: ["单立柱"], boards: [{ m: 0, code: "禁5", title: "禁止驶入", lane: 255, x: 0 }], turn: 37.4 }));
    turnKeys();
}, [mod.Dock, mod.KeysPanel], [1100, 900]);
shot("11-turn-place", () => {
    mod.ui.editorTab = "support";
    values.Editor = JSON.stringify(Object.assign({}, turning, { id: 19, mode: "draft", code: "禁5", title: "禁止驶入", supportInfo: [], boards: [], turn: 15 }));
    turnKeys();
}, [mod.Dock, mod.KeysPanel], [1100, 900]);

// 0.12.84: the support page of a sign about to be placed on a post: the switch for the post in the road (on), and the keys of the tool with the / key.
shot("12-mid-road", () => {
    mod.ui.editorTab = "support";
    values.Supports = JSON.stringify([
        { id: "pole.single", zh: "单柱式", en: "Single post", fam: "post", thumb: "", mid: true },
        { id: "pole.double", zh: "双柱式", en: "Double post", fam: "post", thumb: "", mid: true },
        { id: "cantilever.single", zh: "悬臂式", en: "Cantilever", fam: "cantilever", thumb: "" },
        { id: "gantry.portal", zh: "门架式", en: "Gantry", fam: "gantry", thumb: "" }
    ]);
    values.Support = "pole.single";
    values.Mid = true;
    values.Margin = -0.6;
    values.Editor = JSON.stringify(Object.assign({}, turning, { id: 20, mode: "draft", code: "示41", title: "允许掉头", supportInfo: [], boards: [], turn: 0, plan: "pole.single" }));
    values.Hint = JSON.stringify({ t: "路中：单击放置。", bad: false, k: [["左键", "放置 / 并入"], ["[  ]", "旋转"], [";  '", "换立架"], ["<  >", "离路缘"], ["/", "路中（再按回路侧）"], ["Ctrl+Z / Ctrl+Y", "撤销 / 重做"], ["右键 / Esc", "退出"]] });
}, [mod.Dock, mod.KeysPanel], [1100, 900]);

// 0.12.85: the position of a support that stands, by slider and box: a post at the kerb (along and the margin), a support the margin does not describe (along and the distance from the centre line),
// a board on a wall (along the wall and its lower edge).
const placed = Object.assign({}, turning, { id: 21, mode: "item", code: "示41", title: "允许掉头", supportInfo: ["单立柱"], boards: [], turn: 0, turns: false, painted: false });
const positionShot = (name, extra) => shot(name, () => {
    mod.ui.editorTab = "support";
    values.Mode = 2;
    values.Support = "pole.single";
    values.Editor = JSON.stringify(Object.assign({}, placed, extra));
}, mod.Dock, [1100, 1100]);
positionShot("13-position-kerb", { along: 3.5, kerb: -1.3 });
positionShot("14-position-centre", { along: -12.4, centre: 7.2, centreMax: 14 });
positionShot("15-position-wall", { wall: 1, along: 1.2, lower: 3.5, turns: false });

// 0.12.80: the editor of a board with the lighting setting, its rows as the template makes them (Preview.exe root out --editor-rows gb.guide8.01, which writes out/rows.gb.guide8.01.json).
const lightingRows = join(root, "out/rows.gb.guide8.01.json");
if (existsSync(lightingRows)) {
    shot("9-lighting", () => {
        values.Editor = JSON.stringify({ id: 16, mode: "draft", code: "路1", title: "交叉路口预告标志", preview: "", pw: 400, ph: 200, plan: "pole.single", fields: JSON.parse(readFileSync(lightingRows, "utf8")) });
        values.Hint = JSON.stringify({ t: "单击放置", bad: false, k: [] });
    }, [mod.Dock], [760, 900]);
    // The same board with only its last rows (the English line and the lighting), to see that section whole: in the full editor it is at the foot of the panel, below the fold.
    shot("9-lighting-foot", () => {
        const rows = JSON.parse(readFileSync(lightingRows, "utf8"));
        values.Editor = JSON.stringify({ id: 16, mode: "draft", code: "路1", title: "交叉路口预告标志", preview: "", pw: 400, ph: 200, plan: "pole.single", fields: rows.slice(-3) });
        values.Hint = JSON.stringify({ t: "单击放置", bad: false, k: [] });
    }, [mod.Dock], [760, 900]);
}

// 0.12.88: the eyedropper (the fourth round button, on) with the entries beside it and its line of help and keys.
shot("13-pick", () => {
    mod.ui.flyout = true;
    mod.ui.kind = "warning";
    mod.ui.showList = true;
    values.Mode = 3;
    values.Hint = JSON.stringify({ t: "单击物件吸取样式；Shift 取整杆。", bad: false, k: [["左键", "吸取样式"], ["Shift + 单击", "吸取整杆"], ["Ctrl+Z / Ctrl+Y", "撤销 / 重做"], ["右键 / Esc", "退出"]] });
}, [mod.Dock, mod.KeysPanel]);

// 0.12.104: the memory and distance section of the settings tab: the meter, the memory, what they are made of, and the steps of the draw distance with what each would take. TS_PREVIEW_* none needed.
// The figures are an example (a card of 8 GB, a budget of 384 MB): light and its suggestion, the farthest step in use and over the budget, and the first seconds before the tiles are built.
const MB = 1048576;
const memorySteps = [[300, 60, 0], [500, 110, 0], [700, 170, 0], [1000, 230, 1], [1500, 330, 1], [2000, 380, 1], [3000, 520, 2]].map(([d, m, l]) => ({ d, m: m * MB, l }));
const memoryData = (over = {}) => Object.assign({ v: 170 * MB, r: 4 * MB, b: 384 * MB, t: 8192 * MB, lv: 0, mesh: 92 * MB, face: 58 * MB, det: 20 * MB, item: 3 * MB, pic: 1 * MB, rend: 1240, tiles: 61, items: 1860, ok: true, rec: 2000, steps: memorySteps }, over);
const memoryShot = (name, data, distance) => shot(name, () => {
    mod.ui.settings = true;
    mod.ui.flyout = true;
    mod.ui.showList = true;
    values.Footprint = JSON.stringify(data);
    values.ViewDistance = distance;
    values.SaveData = JSON.stringify({ status: "restored:1860:0:0", warn: {}, offers: [], away: [] });
}, [mod.Dock], [760, 1000]);
memoryShot("16-memory-light", memoryData(), 700);
memoryShot("17-memory-heavy", memoryData({ v: 520 * MB, lv: 2 }), 3000);
memoryShot("18-memory-wait", memoryData({ v: 30 * MB, r: MB, lv: 0, mesh: 0, face: 0, det: 30 * MB, tiles: 0, rend: 0, items: 5, ok: false, rec: 0, steps: memorySteps.map(s => ({ d: s.d, m: -1, l: 2 })) }), 700);
// The section alone in a panel of its own, whole (in the dock the settings tab scrolls and shows a part of it).
const Standalone = () => element(window["cs2/ui"].Panel, { header: "Memory and distance" }, element(mod.FootprintPanel, null));
const sectionShot = (name, data, distance) => shot(name, () => {
    values.Footprint = data ? JSON.stringify(data) : "";
    values.ViewDistance = distance;
}, [Standalone], [440, 700]);
sectionShot("19-section-light", memoryData(), 700);
sectionShot("20-section-heavy", memoryData({ v: 520 * MB, lv: 2 }), 3000);
sectionShot("21-section-wait", memoryData({ v: 30 * MB, r: MB, lv: 0, mesh: 0, face: 0, det: 30 * MB, tiles: 0, rend: 0, items: 5, ok: false, rec: 0, steps: memorySteps.map(s => ({ d: s.d, m: -1, l: 2 })) }), 700);
sectionShot("22-section-reading", null, 700);
