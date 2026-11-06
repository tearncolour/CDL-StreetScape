// CSL Driving Life : StreetScape - the panels of the mod (0.12.3: rebuilt of the game's own UI components).
//
// The way in is a button in the column of round buttons on the right edge of the game ("GameBottomRight"); the ordinary asset category in the Roads menu
// (ToolbarTabSystem) is a second one: the game's AssetMenu is wrapped only to open the panel when that category is chosen. Appended to the game's root ("Game") so that
// they do not depend on the toolbar, the dock (0.12.61, the owner's design):
// - a vertical rounded strip of the tabs, each a round button of the game's right-hand column with the tab's picture: eight show, the wheel brings the others (no scroll bar on it,
//   the owner took it off); the last one is the save data and the display settings;
// - under it a small rounded strip with place / edit / delete;
// - to the right of them one panel that slides out when a tab is chosen: its entries (a search box, the signs as the asset menu's tiles, pages turned with the wheel), and once
//   an entry is chosen its settings in their place (built from the fields the C# side sends for its template, so a new template needs no new UI; for a sign that stands, its
//   support and the boards on it), with a back button at the top left and Esc to the entries again; the pointer coming back from the panel to the strip folds it away.
// Built of cs2/ui (Panel, PanelSection, PanelSectionRow, PanelFoldout, Button, Tooltip, Scrollable, Dropdown, ConfirmationDialog) and, through cs2/modding, of the
// game's tab bar, tool buttons, info buttons, text input, checkbox and the classes of the asset tiles. Every lookup of an internal module is optional: without it
// the same thing is a flat button or a plain input. Every hook is called on every render before any early return (a hook after a return is React error #310,
// which takes down the game's whole UI). The texts the C# side sends are parsed once for each new text, not at every render (0.12.3).
const hasWindow = typeof window !== "undefined";
const api = hasWindow ? window["cs2/api"] : null;
const l10n = hasWindow ? window["cs2/l10n"] : null;
const React = hasWindow ? window.React : null;
const gameUi = (hasWindow && window["cs2/ui"]) || {};
const modding = hasWindow ? window["cs2/modding"] : null;
const h = React ? React.createElement : () => null;

const group = "cdlcts";
const keyPrefix = "CDLC.TS.";
// The grid of signs: six tiles to a row, three rows to a page.
const columns = 6;
const perPage = 18;

// ------------------------------------------------------------------ the game's internal modules (optional)

function gameModule(path, name) {
    try {
        const value = modding && modding.getModule ? modding.getModule(path, name) : undefined;
        return value === undefined ? null : value;
    } catch (error) {
        return null;
    }
}

const modules = {
    tabs: "game-ui/common/tabs/tabs.tsx",
    toolButton: "game-ui/game/components/tool-options/tool-button/tool-button.tsx",
    infoButton: "game-ui/game/components/selected-info-panel/shared-components/info-button/info-button.tsx",
    textInput: "game-ui/common/input/text/text-input.tsx",
    checkbox: "game-ui/common/input/toggle/checkbox/checkbox.tsx",
    itemGrid: "game-ui/game/components/item-grid/item-grid.module.scss",
    dropdown: "game-ui/game/themes/game-dropdown.module.scss",
    rightMenuButton: "game-ui/game/components/right-menu/right-menu-button.module.scss",
    rightMenu: "game-ui/game/components/right-menu/right-menu.module.scss",
    navigationScope: "game-ui/common/focus/auto-navigation-scope.tsx",
    slider: "game-ui/common/input/slider/slider.tsx",
    routeSlider: "game-ui/game/components/selected-info-panel/selected-info-sections/route-sections/route-slider.module.scss"
};

const TabBar = gameModule(modules.tabs, "TabBar");
const Tab = gameModule(modules.tabs, "Tab");
const ToolButton = gameModule(modules.toolButton, "ToolButton");
// The selected-info panel's full-width button (label, icon, selected, onSelect), as the game's own panel actions.
const InfoButton = gameModule(modules.infoButton, "InfoButton");
// The game's text input: it holds the keyboard away from the game's shortcuts while it has the focus, and lets go of it on Enter and Esc.
const TextInput = gameModule(modules.textInput, "TextInput");
const Checkbox = gameModule(modules.checkbox, "Checkbox");
// Several controls in one row go under this: the game's focus controllers of a row and of a tooltip host ONE child, a scope counts as one and takes any number.
const NavigationScope = gameModule(modules.navigationScope, "AutoNavigationScope");
// The game's slider and the row layout it has in the route panel's ticket price (the sibling Bus Driver panel uses the same); without them two buttons set the distance from the kerb.
const Slider = gameModule(modules.slider, "Slider");
const useStepTransformer = gameModule(modules.slider, "useStepTransformer");
const sliderClasses = gameModule(modules.routeSlider, "classes") || {};
const gridClasses = gameModule(modules.itemGrid, "classes");
const dropdownTheme = gameModule(modules.dropdown, "classes");

const icons = {
    button: "coui://cdlcts-icons/streetscape.png",
    all: "coui://cdlcts-icons/all.png",
    place: "Media/Tools/Object Tool/Create.svg",
    edit: "Media/Glyphs/Pen.svg",
    remove: "Media/Game/Icons/Bulldozer.svg",
    trash: "Media/Glyphs/Trash.svg",
    again: "Media/Glyphs/ArrowCircular.svg",
    add: "Media/Glyphs/Plus.svg",
    replace: "Media/Glyphs/Copy.svg",
    left: "Media/Glyphs/ThickStrokeArrowLeft.svg",
    right: "Media/Glyphs/ThickStrokeArrowRight.svg",
    up: "Media/Glyphs/ThickStrokeArrowUp.svg",
    down: "Media/Glyphs/ThickStrokeArrowDown.svg",
    lanes: "Media/Glyphs/More.svg",
    warning: "Media/Glyphs/Warning.svg",
    // The dock's own pictures (IconSources): the game's glyph files have no fill and are black in a plain picture, on the dark round buttons they could not be seen.
    dockPlace: "coui://cdlcts-icons/place.png",
    dockEdit: "coui://cdlcts-icons/edit.png",
    dockDelete: "coui://cdlcts-icons/delete.png",
    dockPick: "coui://cdlcts-icons/pick.png",
    settings: "coui://cdlcts-icons/settings.png",
    back: "coui://cdlcts-icons/back.png"
};

// ------------------------------------------------------------------ bindings

const bind = (name, fallback) => (api ? api.bindValue(group, name, fallback) : null);
const category = bind("Category", null);
const catalog = bind("Catalog", "[]");
const selected = bind("Selected", "");
const supports = bind("Supports", "[]");
const support = bind("Support", "");
const mode = bind("Mode", 0);
const margin = bind("Margin", -0.6);
// Whether the next post stands in the road (0.12.84: the "/" key and the button on the support page are the one switch).
const mid = bind("Mid", false);
// The support styles (0.12.75): [{id, zh, en, pe, re, fe}] (empty when the feature is off) and the id of the one in force.
const profiles = bind("Profiles", "[]");
const profile = bind("Profile", "plain");
const toolActive = bind("ToolActive", false);
const editorBinding = bind("Editor", "");
const hintBinding = bind("Hint", "");
// "zh" or "en", the language of the game.
const language = bind("Lang", "en");
const night = bind("Night", 0);
const nightGain = bind("NightGain", 10);
// How far the signs and markings are drawn, in metres (0.12.103).
const viewDistance = bind("ViewDistance", 700);
// What the mod holds and what each draw distance would take, as JSON (0.12.104): only while the panel that shows it has asked (WatchFootprint).
const footprintBinding = bind("Footprint", "");
const ledDay = bind("LedDay", 200);
const ledNight = bind("LedNight", 15);
const detail = bind("Detail", true);
// 0.12.49: the lamp-post ads' lit faces and lanterns at full night (nits).
const lampLit = bind("LampLit", 3);
const lampGlow = bind("LampGlow", 6);
const status = bind("Status", "");
const clearAreas = bind("ClearAreas", 0);
const clearHidden = bind("ClearHidden", 0);
// How many items have no road in reach (0.12.5): in a save played before 0.12.5 they may be another city's, carried over by a load in the same session.
const orphans = bind("Orphans", 0);
// The save-safe store (0.12.38): {"status", "offers": [{"name","time","score","items"}], "away": [{"i","why","items","kerb","prefab","x","z"}]}.
const saveDataBinding = bind("SaveData", "");
// The player's own signs (0.12.29): {"add": the add tile's picture, "folder", "files": [{"n","w","h","kb"}], "note", "bad"}.
const customBinding = bind("Custom", "");
const selectedCategory = api ? api.bindValue("toolbar", "selectedAssetCategory", null) : null;

// ------------------------------------------------------------------ actions (the triggers of the C# side)

// A text field keeps the keyboard until something takes the focus from it, and a click on the world does not (it is not a page element). So the search box is
// let go of whenever the player turns to the world: when a sign is chosen, a mode is picked, and when the tool is left.
// A number typed in a box or dragged on a slider goes to the tool; text that is not a number, or a box left empty (which Number reads as 0), is dropped.
function sendNumber(name, value) {
    const metres = typeof value === "string" && value.trim() === "" ? NaN : Number(value);
    if (Number.isFinite(metres)) {
        api.trigger(group, name, metres);
    }
}

function blurActive() {
    try {
        const active = typeof document !== "undefined" ? document.activeElement : null;
        if (active && active !== document.body && typeof active.blur === "function") {
            active.blur();
        }
    } catch (error) {
        // nothing to do: the field keeps the focus, and the tool no longer depends on it
    }
}

const actions = {
    select: id => { finishTextEdits(); api.trigger(group, "Select", id); },
    setSupport: id => { blurActive(); api.trigger(group, "SetSupport", id); },
    // The distance of the support from the kerb (0.12.74): the one the next is placed with, and the one a placed post stands at (m, + away from the road).
    setMargin: metres => { blurActive(); api.trigger(group, "SetMargin", metres); },
    setMid: on => { blurActive(); api.trigger(group, "SetMid", !!on); },
    setKerb: metres => { blurActive(); api.trigger(group, "SetKerb", metres); },
    // The position of a support that stands (0.12.85): a slider and a box each, along its road or wall, from the road's centre line, and the lower edge of a board on a wall.
    setKerbTyped: value => sendNumber("SetKerb", value),
    setAlong: value => sendNumber("SetAlong", value),
    setCentre: value => sendNumber("SetCentre", value),
    setLower: value => sendNumber("SetLower", value),
    setMode: value => { finishTextEdits(); api.trigger(group, "SetMode", value); },
    // The editor target travels with the field: a delayed edit of one board must never edit the next board or draft.
    editField: (key, value, target) => {
        if (Number.isInteger(target)) {
            api.trigger(group, "EditField", target + "|" + key + "=" + value);
        }
    },
    // The span of the gantry or the arm of the cantilever of the sign being edited, in metres.
    setSpan: value => api.trigger(group, "SetSpan", Number(value) || 0),
    // The editor's work on the boards of the support being edited: the member is the board's place in the list the C# side sent. A lane is counted from the
    // driver's left (0: automatic, 254: on the post).
    selectBoard: member => { finishTextEdits(); api.trigger(group, "SelectBoard", member); },
    removeBoard: member => { finishTextEdits(); api.trigger(group, "RemoveBoard", member); },
    replaceBoard: member => { finishTextEdits(); api.trigger(group, "ReplaceBoard", member); },
    addBoardHere: () => { finishTextEdits(); api.trigger(group, "AddBoardHere"); },
    setLane: lane => api.trigger(group, "SetLane", lane),
    // One lane for each board, from the driver's left in the order they hang.
    spreadLanes: () => api.trigger(group, "SpreadLanes"),
    // A junction sign: read the junction ahead into the sign being edited again; and whether placing one reads it (1 on, 0 off).
    readJunction: () => api.trigger(group, "ReadJunction"),
    fitFacade: () => api.trigger(group, "FitFacade"),
    setReadJunctions: value => api.trigger(group, "SetReadJunctions", value),
    // The automatic marking (0.12.44): the chosen stretch let go, or its marking undone (the game's markings are drawn there again).
    autoForget: () => api.trigger(group, "AutoForget"),
    autoUndo: () => api.trigger(group, "AutoUndo"),
    autoRemoveOld: () => api.trigger(group, "AutoRemoveOld"),
    // The move buttons of the board being edited (the same as the arrow keys): 1 right, -1 left, 2 down, -2 up, as the screen shows.
    nudgeBoard: code => api.trigger(group, "NudgeBoard", code),
    turnBoard: degrees => api.trigger(group, "TurnBoard", degrees),
    setTurn: value => {
        const degrees = Number(value);
        if (Number.isFinite(degrees)) {
            api.trigger(group, "SetTurn", degrees);
        }
    },
    setTilt: degrees => api.trigger(group, "SetTilt", degrees),
    setFinish: finish => api.trigger(group, "SetFinish", finish),
    setBlackBack: on => api.trigger(group, "SetBlackBack", on),
    setRootPaint: root => api.trigger(group, "SetRootPaint", root),
    flipBoard: () => api.trigger(group, "FlipBoard"),
    leave: () => { finishTextEdits(); api.trigger(group, "Leave"); },
    // The barriers' baked detail (normal maps) on or off (0.12.32).
    setDetail: value => api.trigger(group, "SetDetail", value),
    setLampLit: value => api.trigger(group, "SetLampLit", value),
    setLampGlow: value => api.trigger(group, "SetLampGlow", value),
    // A picture of the folder on the lamp-post ad the editor shows (0.12.49); an empty name takes it off.
    usePicture: name => { finishTextEdits(); api.trigger(group, "UsePicture", name); },
    setNight: value => api.trigger(group, "SetNight", value),
    // How much brighter the faces are at night, as a factor in tenths (10: not brighter, 20: twice).
    setNightGain: value => api.trigger(group, "SetNightGain", value),
    // How far the signs and markings are drawn (m): kept in the mod's preferences file.
    setViewDistance: value => api.trigger(group, "SetViewDistance", value),
    // The memory panel is open (true) or gone (false): the C# side makes its figures only while it is.
    watchFootprint: on => api.trigger(group, "WatchFootprint", on),
    // The glow of the LED screens (variable-message signs): by day in hundreds of nits, at night in nits.
    setLedDay: value => api.trigger(group, "SetLedDay", value),
    setLedNight: value => api.trigger(group, "SetLedNight", value),
    // Every area that clears the game's markings is deleted and the game's markings are drawn again (what to do before this mod is taken away).
    restoreMarkings: () => api.trigger(group, "RestoreMarkings"),
    // Every item with no road in reach is deleted (after the confirmation).
    deleteOrphans: () => api.trigger(group, "DeleteOrphans"),
    // The save-safe store (0.12.38): a record offered restored, the offers ignored; a road put away (-1: all of them) shown on the map, placed after all, deleted.
    saveDataRestore: index => api.trigger(group, "SaveDataRestore", index),
    saveDataIgnore: () => api.trigger(group, "SaveDataIgnore"),
    saveDataLocate: anchor => api.trigger(group, "SaveDataLocate", anchor),
    saveDataPlace: anchor => api.trigger(group, "SaveDataPlace", anchor),
    saveDataDelete: anchor => api.trigger(group, "SaveDataDelete", anchor),
    // The support that stands: moved along its road (1 / -1: half a metre toward / away from the traffic that sees it, 3 / -3: five metres) or across (2 / -2: 0.1 m
    // away from / toward the road, 4 / -4: one metre); on a wall along it and up or down, 5 turns a bracket's board. Its span or arm half a metre longer (1) or
    // shorter (-1), or as the road asks (0).
    moveSupport: code => api.trigger(group, "MoveSupport", code),
    fitSpan: () => api.trigger(group, "FitSpan"),
    setClearance: value => api.trigger(group, "SetClearance", Number(value) || 0),
    // The player's own signs (0.12.29): the PNG files of the folder listed again, the folder opened, one imported (the tool then places a new sign of it), the sign the editor shows saved
    // as a new one, a saved one changed or deleted (by its key).
    listCustomFiles: () => api.trigger(group, "ListCustomFiles"),
    openCustomFolder: () => api.trigger(group, "OpenCustomFolder"),
    importCustom: name => { finishTextEdits(); api.trigger(group, "ImportCustom", name); },
    saveCustom: () => { finishTextEdits(); api.trigger(group, "SaveCustom"); },
    updateCustom: key => { finishTextEdits(); api.trigger(group, "UpdateCustom", key); },
    deleteCustom: key => { finishTextEdits(); api.trigger(group, "DeleteCustom", key); },
    // The support style (a city's paint and variants) the boards placed next take; the ones placed stay as they are (0.12.75).
    setProfile: id => { blurActive(); api.trigger(group, "SetProfile", id); }
};

// ------------------------------------------------------------------ search, filter, pages

// Full-width forms and letter case must not matter: "禁１", "禁1" and "ＪＩＮ1" are the same query.
function normalize(text) {
    return String(text ?? "").normalize("NFKC").toLowerCase();
}

// An entry: { id, code: "禁1", zh, en, kw: [...], py: "tcrx" (pinyin initials), grp: "prohibition", thumb: "coui://..." }. Every word of the query must be found in a field.
function matches(entry, query) {
    const words = normalize(query).split(/\s+/).filter(Boolean);
    if (words.length === 0) {
        return true;
    }

    const codes = [normalize(entry.code), normalize(entry.ce)];
    const fields = [normalize(entry.zh), normalize(entry.en), normalize((entry.kw || []).join(" ")), normalize(entry.py)];
    return words.every(word => {
        const inFields = fields.some(f => f.includes(word));
        // A word made of letters and then digits is a code: "禁1" is 禁1 and not 禁10. The code of an English game (ce) is searched as well.
        return /^\D+\d+$/.test(word) ? codes.some(c => c === word) || inFields : codes.some(c => c.includes(word)) || inFields;
    });
}

function search(entries, query) {
    return entries.filter(entry => matches(entry, query));
}

// The signs that match the query and the chosen kind ("all" is every kind).
function filterEntries(entries, query, kind) {
    return search(entries, query).filter(entry => kind === "all" || !kind || entry.ser === kind || (entry.grp === kind && !entry.so));
}

const groupOrder = ["prohibition", "warning", "mandatory", "guide", "auxiliary", "notice", "tourist", "placename", "wayfinding", "variable", "brt", "toll", "cabinet", "lampad", "marking", "barrier", "green", "custom"];

// An entry with `so` (0.12.107) is listed in its series' tab only, not in the tab of its kind (a toll booth is not a barrier).
// The kinds the catalogue has, in the order of the standard's chapters (unknown kinds after them), with the series that list entries of other kinds too (0.12.22: BRT), and always the
// player's own signs (0.12.29: the tab is where the first one is made).
function groupsOf(entries) {
    const present = [...new Set(entries.flatMap(entry => [entry.so ? "" : entry.grp, entry.ser]).concat(["custom"]).filter(Boolean))];
    return present.sort((a, b) => {
        const ia = groupOrder.indexOf(a), ib = groupOrder.indexOf(b);
        return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib) || a.localeCompare(b);
    });
}

// Stable tab art is available before the asset thumbnails and never changes when a custom sign is saved.
function kindIcon(kind) {
    return groupOrder.includes(kind) ? "coui://cdlcts-icons/" + kind + ".png" : icons.all;
}

// The tile that adds a sign of the player's own, at the end of their tab (0.12.29).
const addId = "cdlts:add";
function addEntry(custom, t) {
    return { id: addId, add: true, code: "+", zh: t("UI.CustomAdd", "Import a PNG picture"), en: "", thumb: custom && custom.add ? custom.add : "" };
}

function parseCustom(text) {
    try {
        const value = JSON.parse(text || "");
        return value && typeof value === "object" && Array.isArray(value.files) ? value : null;
    } catch (error) {
        return null;
    }
}

// One page of a list: the page is kept inside the list (a filter that shortens it never leaves the panel on a page that is not there).
function paginate(list, page, size) {
    const pages = Math.max(1, Math.ceil(list.length / size));
    const current = Math.min(Math.max(0, page | 0), pages - 1);
    return { items: list.slice(current * size, current * size + size), page: current, pages, total: list.length };
}

// The line of help: {"t": text, "bad": true when it is a refusal, "k": [[key, what it does], ...] the keys of the tool now (0.12.47)}. Anything else is no line.
function parseHint(text) {
    try {
        const value = JSON.parse(text || "");
        if (!value || typeof value.t !== "string" || value.t.length === 0) {
            return null;
        }
        const keys = Array.isArray(value.k) ? value.k.filter(pair => Array.isArray(pair) && pair.length === 2).map(pair => [String(pair[0]), String(pair[1])]) : [];
        return { text: value.t, bad: !!value.bad, keys };
    } catch (error) {
        return null;
    }
}

function parseCatalog(text) {
    try {
        const list = JSON.parse(text || "[]");
        return Array.isArray(list) ? list : [];
    } catch (error) {
        return [];
    }
}

// {"id":3,"mode":"item"|"draft","code":"路42","title":"…","preview":"coui://…","pw":676,"ph":768,"plan":"pole.double","fields":[{"k","l","t":"text"|"choice"|"flag","v","max","w"?:true (a text over the whole width),"ch":[{"v","l"}]}]}
function parseEditor(text) {
    try {
        const editor = JSON.parse(text || "");
        return editor && typeof editor === "object" && Array.isArray(editor.fields) ? editor : null;
    } catch (error) {
        return null;
    }
}

// A parse that is done again only for a new text: the catalogue of some six hundred signs was parsed at every render of the panel, and the panel rendered
// several times a second while the tool ran (0.12.3).
function memoParse(parse) {
    let text, value, done = false;
    return input => {
        if (!done || input !== text) {
            done = true;
            text = input;
            value = parse(input);
        }
        return value;
    };
}

// The signs that match, worked out again only for another catalogue, query or kind (the panel also renders when the editor's text changes).
let filtered = { entries: null, query: null, kind: null, list: [], kinds: [] };
function filteredOf(entries, query, kind) {
    if (filtered.entries !== entries || filtered.query !== query || filtered.kind !== kind) {
        const kinds = filtered.entries === entries ? filtered.kinds : groupsOf(entries);
        const current = kinds.includes(kind) ? kind : "all";
        filtered = { entries, query, kind, kinds, current, list: filterEntries(entries, query, current) };
    }
    return filtered;
}

const catalogOf = memoParse(parseCatalog);
const supportsOf = memoParse(parseCatalog);
const editorOf = memoParse(parseEditor);
const hintOf = memoParse(parseHint);
const customOf = memoParse(parseCustom);

// The size of the picture in the editor, in rem, kept in proportion and inside a box (no object-fit in the game's UI engine, so the image is given its own size).
function previewSize(pw, ph, maxW, maxH) {
    if (!(pw > 0) || !(ph > 0)) {
        return { w: maxW, h: maxH };
    }

    const k = Math.min(maxW / pw, maxH / ph);
    return { w: Math.round(pw * k), h: Math.round(ph * k) };
}

// ------------------------------------------------------------------ text boxes

// A text box sends what was typed a moment after the last key (and at once when it loses the focus, or Enter is pressed), and never in the middle of a word that
// is being composed: the game's input method must not be disturbed, and the C# side rebuilds the board for every change.
const pending = new Map();

function cancelPendingEdit(key, target) {
    const token = target + ":" + key;
    const entry = pending.get(token);
    if (entry !== undefined && typeof clearTimeout === "function") {
        clearTimeout(entry.timer);
    }

    pending.delete(token);
}

function cancelPendingEdits(target) {
    for (const [token, entry] of pending) {
        if (target === undefined || entry.target === target) {
            if (typeof clearTimeout === "function") {
                clearTimeout(entry.timer);
            }
            pending.delete(token);
        }
    }
}

// A normal blur commits to the old target before the action changes it. Anything that could not be blurred is cancelled.
function finishTextEdits() {
    blurActive();
    cancelPendingEdits();
}

function commitNow(key, value, target) {
    cancelPendingEdit(key, target);
    actions.editField(key, value, target);
}

function commitSoon(key, value, composing, target) {
    cancelPendingEdit(key, target);
    if (composing || !Number.isInteger(target)) {
        return;
    }

    if (typeof setTimeout !== "function") {
        commitNow(key, value, target);
        return;
    }

    pending.set(target + ":" + key, { target, timer: setTimeout(() => commitNow(key, value, target), 350) });
}

function composing(event) {
    return !!(event && (event.isComposing || (event.nativeEvent && event.nativeEvent.isComposing)));
}

function valueOf(event) {
    return event && event.target ? String(event.target.value ?? "") : "";
}

// The game's text input (controlled), or without it a plain one (uncontrolled: the input method is not disturbed by a value written back while a word is composed).
function textBox(props) {
    if (TextInput) {
        return h(TextInput, props);
    }
    const plain = Object.assign({}, props, { type: "text", defaultValue: props.value, className: "cdlts-text" + (props.className ? " " + props.className : "") });
    delete plain.value;
    plain.onKeyDown = event => {
        if (event && (event.key === "Escape" || event.key === "Enter") && !event.isComposing && event.target && event.target.blur) {
            event.target.blur();
        }
    };
    return h("input", plain);
}

// A setting typed in: kept here while it is typed (the C# side's value comes back changed only when the board was read again, a junction for one: then it is
// taken unless the box has the focus).
function TextField({ field, target, onCommit }) {
    const [value, setValue] = React.useState(field.v);
    const focused = React.useRef(false);
    const composingText = React.useRef(false);
    const mounted = React.useRef(true);
    React.useEffect(() => {
        mounted.current = true;
        return () => {
            mounted.current = false;
            cancelPendingEdit(field.k, target);
        };
    }, [field.k, target]);
    React.useEffect(() => {
        if (!focused.current) {
            setValue(field.v);
        }
    }, [field.v]);
    const commit = onCommit || (v => commitNow(field.k, v, target));
    return textBox({
        value,
        className: field.cls ? "cdlts-text " + field.cls : "cdlts-text",
        maxLength: field.max > 0 ? field.max : undefined,
        onFocus: () => { focused.current = true; },
        onChange: event => {
            const v = valueOf(event);
            setValue(v);
            if (!onCommit) {
                commitSoon(field.k, v, composingText.current || composing(event), target);
            }
        },
        onCompositionStart: () => {
            composingText.current = true;
            cancelPendingEdit(field.k, target);
        },
        onCompositionEnd: event => {
            composingText.current = false;
            const v = valueOf(event);
            setValue(v);
            if (mounted.current && !onCommit) {
                commitSoon(field.k, v, false, target);
            }
        },
        onBlur: event => {
            focused.current = false;
            if (mounted.current && !composingText.current && !composing(event)) {
                commit(event && event.target ? valueOf(event) : value);
            }
        }
    });
}

// ------------------------------------------------------------------ errors

// A script error cannot be seen from outside the game, so it is sent to the C# side, which writes it to the mod's log (the first ten only).
let reported = 0;

function reportError(where, error) {
    try {
        if (reported++ < 10 && api) {
            const text = error && (error.stack || error.message) ? (error.stack || error.message) : String(error);
            api.trigger(group, "UiError", where + ": " + String(text).slice(0, 600));
        }
    } catch (ignored) {
        // nothing more can be done
    }
}

// An error in rendering one panel shows nothing for that panel and is reported, instead of unmounting everything below it. A new generation (the panel opened
// again) mounts it afresh.
const Guard = React && React.Component
    ? class Guard extends React.Component {
        constructor(props) {
            super(props);
            this.state = { failed: false };
        }

        static getDerivedStateFromError() {
            return { failed: true };
        }

        componentDidCatch(error) {
            reportError(this.props.name, error);
        }

        render() {
            return this.state.failed ? null : this.props.children;
        }
    }
    : ({ children }) => children;

// ------------------------------------------------------------------ the panels' own state (outside React: it survives a panel being hidden and shown)

// flyout: the panel beside the strip is out; settings: its tab is the save data and display settings; showList: the entries are shown though an editor is there (a tab was chosen,
// or the back button of a sign being edited); seenEditor: the editor the panel last saw (a new one starts on its settings); top: the first tab the strip shows; leftAt: when the pointer
// last left the panel to its left.
const ui = { open: false, generation: 0, page: 0, kind: "all", query: "", editorTab: "sign", flyout: false, settings: false, showList: false, seenEditor: "", editor: null, top: 0, leftAt: 0, confirming: false, confirmingOrphans: false, importing: false, confirmingCustom: "", confirmingAutoUndo: null, confirmingAutoOld: null, listeners: new Set() };

function setUi(patch) {
    Object.assign(ui, patch);
    ui.listeners.forEach(listener => listener());
}

function useUi() {
    const [, bump] = React.useState(0);
    React.useEffect(() => {
        const listener = () => bump(n => n + 1);
        ui.listeners.add(listener);
        return () => ui.listeners.delete(listener);
    }, []);
    return ui;
}

function sameEntity(a, b) {
    return !!a && !!b && a.index === b.index && a.version === b.version && !(a.index === 0 && a.version === 0);
}

// The panels are shown from the moment they are opened (the button, or choosing the tab) to the moment they are closed (the button, the cross). Leaving the tool
// with a right click or Esc does not close them: the player may want to pick another sign.
function panelVisible(isOpen) {
    return !!isOpen;
}

function useText() {
    const loc = l10n && l10n.useLocalization ? l10n.useLocalization() : null;
    return (key, fallback) => (loc && loc.translate(keyPrefix + key, fallback)) || fallback || key;
}

// The size of a rem in pixels (the game sets it from the screen's height); 0 when it cannot be read.
function remPixels() {
    try {
        const unit = parseFloat(getComputedStyle(document.documentElement).fontSize);
        return unit > 0 ? unit : 0;
    } catch (error) {
        return 0;
    }
}

// The size of the screen in rem: 1920 by 1080, the size of an ordinary screen, where it cannot be read.
function screenSizeRem() {
    const unit = remPixels();
    return {
        w: unit > 0 && window.innerWidth > 0 ? window.innerWidth / unit : 1920,
        h: unit > 0 && window.innerHeight > 0 ? window.innerHeight / unit : 1080
    };
}

function screenRem() {
    return screenSizeRem().h;
}

// ------------------------------------------------------------------ small parts made of the game's components

// The height of what scrolls in the dock's panel (rem): the screen's, less what is above and below it: the panel starts 110 rem down, its header takes 56, the game's own toolbar at the bottom
// 120 and a margin 14 (0.12.63: the panel ran down into the toolbar; the owner: "会溢出屏幕"); extra is what is in the panel besides the header (the tabs of the settings). It is never under
// 240, and a tall screen does not make the panel taller than 760. 0.12.69 (the owner marked the strips and the panel beside them in a screenshot: "限制在这个区域内就行"): the panel does not go
// below the strips either: they are about dockRem tall (653 px of a screenshot in which the 470 rem panel is 626 px, measured, so within a few per cent), the header takes headerRem.
const dockRem = 490, headerRem = 40;
function bodyHeight(extra) {
    return Math.max(240, Math.min(760, Math.round(Math.min(screenRem() - 300, dockRem - headerRem - 12) - extra))) + "rem";
}

// The least the list's body is tall (0.12.94, the owner, with the list of "全部" 22 px short of the strip beside it: "条目列表高度可以高一点，和左侧UI条不对齐"): the panel as tall as the strip of the
// tabs (count of them, the settings tab and "all" included), its header taken off, and never over what bodyHeight allows.
function listMinHeight(count) {
    return Math.min(Math.round(stripRem(count) - headerRem), parseInt(bodyHeight(0), 10)) + "rem";
}

// The editor's window (0.12.90; the owner, looking at the settings of a junction board of which three rows showed under the picture: "调整一下UI窗口大小"): wider than the list's, and no longer held to the
// strips' height (0.12.69 did); what scrolls is up to editorBodyRem tall and ends 480 rem above the screen's foot (its header 56 and tabs 44 start 110 down: the game's toolbar and the keys' card at the
// bottom left stay clear).
const editorPanelRem = 560, editorBodyRem = 600;
function editorBodyHeight() {
    return Math.max(240, Math.min(editorBodyRem, Math.round(screenRem() - 480))) + "rem";
}

// What a line of text takes, in rem: a CJK or full-width character is as wide as the font is high (13rem), anything else about 0.55 of it. The C# test of the
// labels (UiTextTests) uses the same figures; keep them equal.
const cjkWidth = 13, latinWidth = 7.2;
function textWidth(text) {
    let width = 0;
    for (const ch of String(text || "")) {
        width += ch.codePointAt(0) >= 0x2e80 ? cjkWidth : latinWidth;
    }
    return width;
}

// How a choice is shown. A few short options are buttons side by side beside the label (at most 4, taking 230rem with 26rem of padding each); more or longer
// ones are the game's dropdown. A label wider than 130rem, or a dropdown whose longest option is wider than 200rem, puts the label on a line of its own and the
// control under it at the editor's whole width (380rem).
// A text box with a label of up to inlineTextLabel (15 CJK characters) is beside it (0.12.69: the label over the box took two rows of 45 rem each); a longer label is a small line over the box.
const inlineTextLabel = 200;
const sideTextLabel = 56, sideLabel = 130, sideButtons = 230, buttonExtra = 26, sideDropdown = 200, wholeWidth = 380, itemExtra = 40;
function choiceLayout(field) {
    const options = field.ch || [];
    let buttons = 0, longest = 0;
    for (const option of options) {
        buttons += textWidth(option.l) + buttonExtra;
        longest = Math.max(longest, textWidth(option.l));
    }
    const label = textWidth(field.l);
    if (options.length <= 4 && buttons <= sideButtons) {
        return { kind: "buttons", stacked: label > sideLabel };
    }
    return { kind: "dropdown", stacked: label > sideLabel || longest > sideDropdown };
}

function row(key, left, right, extra) {
    return h(gameUi.PanelSectionRow, Object.assign({ key, left, right }, extra || {}));
}

// A row whose right side has several controls. The game's row hosts ONE focusable child, also with disableFocus (that is a passive div with the same controller): a second one logs "Cannot register
// second focus key ... can only host a single child" with a stack trace, and the owner's UI.log of 1 Nov 22:49 to 23:47 had 636 of them, 6 to 12 each time a page of the editor opened (0.12.71; found
// by the RIE session in its own panel, which had the same comment here). The controls go under the game's AutoNavigationScope: one child of the row. Without the scope (a game update that moved it)
// the row is as it was.
function controlsRow(key, left, right, tip) {
    return row(key, left, right == null || !NavigationScope ? right : h(NavigationScope, null, right), tip ? { disableFocus: true, tooltip: tip } : { disableFocus: true });
}

function section(key, children, className) {
    return h(gameUi.PanelSection, { key, className }, children);
}

// A flat button with a text (the game's flat button has no padding of its own outside a row).
function textButton(key, label, onSelect, selectedNow, className) {
    return h(gameUi.Button, { key, variant: "flat", className: "cdlts-btn" + (className ? " " + className : ""), selected: !!selectedNow, onSelect }, label);
}

// A square button with an icon and a tooltip: the game's tool button (bottom right in the game), else an icon button.
function iconButton(key, src, tooltip, onSelect, selectedNow, children) {
    if (ToolButton) {
        return h(ToolButton, { key, src, tooltip, selected: !!selectedNow, onSelect }, children);
    }
    const button = h(gameUi.Button, { variant: "flat", className: "cdlts-icon-btn", src: src || undefined, selected: !!selectedNow, onSelect }, src ? undefined : children);
    return gameUi.Tooltip ? h(gameUi.Tooltip, { key, tooltip }, button) : h("div", { key }, button);
}

// A full-width action with an icon: the game's info button (as the selected-info panel's own actions), else a flat button.
function wideButton(key, label, src, onSelect, selectedNow) {
    if (InfoButton) {
        return h("div", { key, className: "cdlts-wide" }, h(InfoButton, { label, icon: src, selected: !!selectedNow, onSelect }));
    }
    return h("div", { key, className: "cdlts-wide" }, textButton(key + "-b", label, onSelect, selectedNow, "cdlts-fill"));
}

// A switch: the game's checkbox, else a flat button with a tick.
function toggle(key, checked, onChange) {
    if (Checkbox) {
        return h(Checkbox, { key, checked: !!checked, onChange: value => onChange(!!value) });
    }
    return textButton(key, checked ? "✓" : "–", () => onChange(!checked), checked);
}

// A choice of a few as buttons side by side, one selected.
function segments(key, options, value, onChange, wrap) {
    return h("div", { key, className: "cdlts-seg" + (wrap ? " cdlts-seg-wrap" : "") },
        options.map(option => textButton(key + ":" + option.v, option.l, () => onChange(option.v), option.v === value)));
}

// A choice among many: the game's dropdown (with the game's dropdown classes, so the toggle and the items take its theme and arrow), else buttons that wrap.
function dropdown(key, options, value, onChange, wide) {
    if (gameUi.Dropdown && gameUi.DropdownToggle && gameUi.DropdownItem) {
        const current = options.find(option => option.v === value);
        const content = options.map(option => h(gameUi.DropdownItem, {
            key: key + ":" + option.v, value: option.v, selected: option.v === value, closeOnSelect: true, onChange: () => onChange(option.v)
        }, option.l));
        return h(gameUi.Dropdown, { key, theme: dropdownTheme || undefined, content },
            h(gameUi.DropdownToggle, { className: "cdlts-toggle" + (wide ? " cdlts-toggle-wide" : "") }, current ? current.l : "–"));
    }
    return segments(key, options, value, onChange, true);
}

// A number with a step each way (the game's flat buttons).
function stepper(key, value, onMinus, onPlus) {
    return h("div", { key, className: "cdlts-seg" },
        textButton(key + "-", "−", onMinus, false, "cdlts-step"),
        h("div", { className: "cdlts-value" }, String(value)),
        textButton(key + "+", "+", onPlus, false, "cdlts-step"));
}

// The steps a stepper goes through: the next or the one before (it stays at the ends); a value between the steps goes to the nearest.
function stepFrom(steps, value, by) {
    let at = 0;
    for (let i = 0; i < steps.length; i++) {
        if (Math.abs(steps[i] - value) < Math.abs(steps[at] - value)) {
            at = i;
        }
    }
    return steps[Math.min(steps.length - 1, Math.max(0, at + by))];
}

function note(key, text, kind) {
    return h("div", { key, className: kind === "warn" ? "cdlts-note cdlts-warn" : kind === "bad" ? "cdlts-note cdlts-bad" : "cdlts-note" }, text);
}

// ------------------------------------------------------------------ the catalogue

function SearchBox() {
    const t = useText();
    const state = useUi();
    return h("div", { className: "cdlts-search" },
        textBox({
            value: state.query,
            className: "cdlts-text",
            placeholder: t("UI.Search", "Search signs, numbers, keywords"),
            onChange: event => setUi({ query: valueOf(event), page: 0 })
        }));
}

// The tabs of the strip: all, each kind the catalogue has, and the settings.
const stripSlots = 8;
const settingsTab = "settings";

function tabsOf(kinds, t) {
    return [{ id: "all", name: t("UI.Group.all", "All"), icon: icons.all }]
        .concat(kinds.map(k => ({ id: k, name: t("UI.Group." + k, k), icon: kindIcon(k) })))
        .concat([{ id: settingsTab, name: t("UI.SettingsTab", "Save data and display"), icon: icons.settings }]);
}

// The first tab the strip shows, kept so that the last tab is at the bottom at most.
function clampTop(top, count) {
    return Math.max(0, Math.min(Math.max(0, count - stripSlots), top | 0));
}

// A round button of the game's right-hand column (the way in has the same), with its name in the tooltip; the tool button where the game's classes are not there.
function roundButton(key, src, tooltip, onSelect, selectedNow) {
    const theme = gameClasses(modules.rightMenuButton);
    const menu = gameClasses(modules.rightMenu);
    if (!theme || !menu || !menu.item) {
        return iconButton(key, src, tooltip, onSelect, selectedNow);
    }
    const button = h(gameUi.Button, { src, selected: !!selectedNow, theme, className: theme.toggleStates, onSelect, title: tooltip, disableHint: true });
    return h("div", { key, className: menu.item }, gameUi.Tooltip ? h(gameUi.Tooltip, { tooltip, direction: "right", alignment: "center" }, button) : button);
}

// A tab chosen: its entries slide out, or fold away when its entries are out already.
function pickTab(id, listShown) {
    const same = id === settingsTab ? ui.settings : !ui.settings && ui.kind === id;
    if (listShown && same) {
        setUi({ flyout: false });
        return;
    }
    setUi(id === settingsTab ? { settings: true, flyout: true, showList: true }
        : { settings: false, kind: id, page: 0, importing: id === "custom" && ui.importing, flyout: true, showList: true });
}

// The wheel over the strip brings the tabs that are not in view.
function scrollStrip(event, count) {
    if (event && event.deltaY) {
        setUi({ top: clampTop(clampTop(ui.top, count) + (event.deltaY > 0 ? 1 : -1), count) });
    }
}

// The pointer coming back to the strip from the panel (it left the panel to its left a moment ago) folds the entries away.
const returnWithin = 800;
function stripEntered() {
    if (ui.flyout && ui.leftAt > 0 && Date.now() - ui.leftAt < returnWithin) {
        setUi({ flyout: false, leftAt: 0 });
    } else if (ui.leftAt !== 0) {
        ui.leftAt = 0;
    }
}

function panelLeft(event) {
    const box = event && event.currentTarget && event.currentTarget.getBoundingClientRect ? event.currentTarget.getBoundingClientRect() : null;
    ui.leftAt = box && typeof event.clientX === "number" && event.clientX <= box.left + 8 ? Date.now() : 0;
}

// The strip shows stripSlots tabs at a time and the wheel brings the others (0.12.94, the owner: "左边的分类标签加一个滚动提示"): with more tabs than that it keeps a margin at each end
// for an arrow that is bright where there are tabs out of view that way and faint where there are none; a click on one steps a tab. The height of the strip is what the list's panel is
// made as tall as (stripRem), measured from the owner's screenshot: 8 tabs and the padding take 341.6 rem, so a tab 40.7 rem and the padding 16; the margins for the arrows add 12.
const slotRem = 40.7, stripPadRem = 16, stripMoreRem = 12;
const stripRem = count => stripPadRem + (count > stripSlots ? stripMoreRem : 0) + Math.min(count, stripSlots) * slotRem;

function TabStrip({ tabs, active, listShown }) {
    const top = clampTop(ui.top, tabs.length);
    const visible = tabs.slice(top, top + stripSlots);
    const more = tabs.length > stripSlots;
    const arrow = up => {
        const lit = up ? top > 0 : top + stripSlots < tabs.length;
        return h("div", {
            key: up ? "more-up" : "more-down",
            className: "cdlts-more " + (up ? "cdlts-more-top" : "cdlts-more-foot") + (lit ? " cdlts-more-lit" : ""),
            onClick: () => setUi({ top: clampTop(top + (up ? -1 : 1), tabs.length) })
        }, h("div", { className: "cdlts-more-tri " + (up ? "cdlts-more-up" : "cdlts-more-down") }));
    };
    return h("div", { className: "cdlts-strip" + (more ? " cdlts-strip-more" : ""), onWheel: event => scrollStrip(event, tabs.length), onMouseEnter: stripEntered },
        more ? arrow(true) : null,
        visible.map(tab => roundButton("k-" + tab.id, tab.icon, tab.name, () => pickTab(tab.id, listShown), listShown && tab.id === active)),
        more ? arrow(false) : null);
}

// A name in the language of the game (the catalogue, the supports and the styles carry both; the search reads both). A Chinese game keeps the English name as the second line of a tooltip; an English game shows no Chinese.
const nameOf = (entry, lang) => (lang === "zh" ? entry.zh : entry.en) || entry.zh || entry.en || "";
const codeOf = (entry, lang) => (lang === "zh" ? entry.code : entry.ce || entry.code);
const otherOf = (entry, lang) => (lang === "zh" && entry.en && entry.en !== entry.zh ? entry.en : "");

// One sign as a tile of the game's asset menu: its thumbnail (or its code before the thumbnails are made), the code and the names in the tooltip. The add tile opens the import.
function tile(entry, on, lang) {
    const other = otherOf(entry, lang);
    const tooltip = entry.add ? h("div", null, nameOf(entry, lang)) : h("div", null, h("div", { className: "cdlts-tip-title" }, codeOf(entry, lang) + "  " + nameOf(entry, lang)), other ? h("div", null, other) : null);
    const button = h(gameUi.Button, {
        className: "cdlts-tile",
        theme: { button: gridClasses && gridClasses.item ? gridClasses.item : "cdlts-tile-plain" },
        selected: on,
        onSelect: entry.add ? () => { setUi({ importing: true }); actions.listCustomFiles(); } : () => { setUi({ showList: false }); actions.select(entry.id); }
    },
    gridClasses && gridClasses.itemInner ? h("div", { className: gridClasses.itemInner }) : null,
    entry.thumb ? h("img", { className: "cdlts-tile-img" + (gridClasses && gridClasses.thumbnail ? " " + gridClasses.thumbnail : ""), src: entry.thumb }) : h("div", { className: "cdlts-tile-code" }, codeOf(entry, lang)));
    return gameUi.Tooltip ? h(gameUi.Tooltip, { key: entry.id, tooltip }, button) : h("div", { key: entry.id }, button);
}

// The grid: rows of tiles and, as the asset menu has them, the marks of the pages on its right; the wheel turns the pages.
function SignGrid({ page, chosen, active }) {
    const t = useText();
    const lang = api.useValue(language);
    const turn = step => setUi({ page: Math.min(Math.max(0, page.page + step), page.pages - 1) });
    const rows = [];
    for (let i = 0; i < page.items.length; i += columns) {
        rows.push(h("div", { key: "r" + i, className: "cdlts-grid-row" + (gridClasses && gridClasses.row ? " " + gridClasses.row : "") },
            page.items.slice(i, i + columns).map(entry => tile(entry, entry.add ? !!ui.importing : active && entry.id === chosen, lang))));
    }
    const marks = [];
    for (let p = 0; p < page.pages && page.pages > 1; p++) {
        marks.push(h("div", {
            key: "p" + p,
            className: "cdlts-page" + (gridClasses && gridClasses.pageIndicator ? " " + gridClasses.pageIndicator : "") + (p === page.page ? " selected cdlts-page-on" : ""),
            onClick: () => setUi({ page: p })
        }));
    }
    return h("div", { className: "cdlts-grid", onWheel: event => { if (event && event.deltaY) { turn(event.deltaY > 0 ? 1 : -1); } } },
        h("div", { className: "cdlts-grid-rows" }, rows.length > 0 ? rows : note("empty", t("UI.Empty", "Nothing matches."))),
        h("div", { className: "cdlts-pages" }, marks));
}

// The supports: automatic (the planner's choice for the board, GB 5768.2 4.8.12, named with what it comes to) and each style, as tiles with their pictures.
// The supports by form (0.12.58, the owner: "整合立杆类型"): a row of the forms (posts, cantilevers, gantry, attached, street-name posts) and, under the one chosen, its variants (what it is made of
// or how it is dressed: a round or an octagonal post, a cranked arm, a city's style). Choosing a form takes the variant chosen last in it, else its first.
const familyOrder = ["post", "cantilever", "gantry", "attached", "plate"];
const lastVariant = {};

function familiesOf(list) {
    const families = [];
    list.forEach(entry => {
        const id = entry.fam || "post";
        let family = families.find(f => f.id === id);
        if (!family) {
            family = { id, items: [] };
            families.push(family);
        }
        family.items.push(entry);
    });
    const rank = id => { const i = familyOrder.indexOf(id); return i < 0 ? familyOrder.length : i; };
    return families.sort((a, b) => rank(a.id) - rank(b.id));
}

// The distance from the kerb (0.12.74; the owner has no numpad: "可以在支架界面加入一个滑条来控制，既可以按键加减，也可以滑条定量"): the < and > keys step it by 0.1 m, the slider sets it, committed
// on release (a draft is shown while it is dragged). Two buttons stand in for a slider the game's UI does not have.
const kerbRange = { min: -8, max: 6, step: 0.1, home: -0.6 };
const kerbTenths = value => Math.round(value * 10) / 10;
// The position of a support that stands (SupportShift): the slider along goes 50 m each way from where the support stood when it was picked, the box takes up to 500; the lower edge of a board on a wall to 60 m
// on the slider (the box up to the building's top).
const alongRange = { min: -50, max: 50, step: 0.1 };
const lowerRange = { min: 0.2, max: 60, step: 0.1 };
const kerbText = value => { const v = kerbTenths(value); return (v >= 0 ? "+" : "-") + Math.abs(v).toFixed(1) + " m"; };

const profilesOf = memoParse(text => {
    try {
        const list = JSON.parse(text || "[]");
        return Array.isArray(list) ? list : [];
    } catch (error) {
        return [];
    }
});

// The support style (0.12.75, DESIGN-support-styles.md): the game's dropdown of the profiles, and under it how far the chosen one is backed (a standard, a photograph, a maker's listing, this mod's
// estimate; the plain profile is the default and has none). Nothing where the list is empty (the feature is off). prefix keeps the keys apart where two pages hold the row.
function ProfileRows({ prefix }) {
    const t = useText();
    const list = profilesOf(api.useValue(profiles));
    const current = api.useValue(profile);
    const lang = api.useValue(language);
    if (list.length === 0) {
        return null;
    }

    const evidence = code => t("UI.Evidence." + code, code);
    const options = list.map(entry => ({ v: entry.id, l: t("UI.Profile." + entry.id, nameOf(entry, lang)) + (entry.pe ? " · " + evidence(entry.pe) : "") }));
    const chosen = list.find(entry => entry.id === current) || list[0];
    const parts = [];
    if (chosen.pe) {
        parts.push(t("UI.ProfilePaint", "Paint") + " " + evidence(chosen.pe));
    }
    if (chosen.re) {
        parts.push(t("UI.ProfileRoot", "Root paint") + " " + evidence(chosen.re));
    }
    if (chosen.fe) {
        parts.push(t("UI.ProfileForms", "Forms") + " " + evidence(chosen.fe));
    }
    return [
        controlsRow(prefix + "-profile", t("UI.Profile", "Support style"), dropdown(prefix + "-profile-d", options, chosen.id, actions.setProfile, false)),
        parts.length > 0 ? row(prefix + "-profile-why", "", parts.join(" · ")) : null
    ];
}

// A number with a range: the head row with its value (a text, or a box to type it in), the game's slider that commits on release (a draft is shown while it is dragged), two step buttons where the
// game has no slider, and the buttons of the caller (a way back to the default or to the planner's own value) in the same row. Used by the kerb's distance, a number field and the span of a support.
function RangeRows({ prefix, label, value, min, max, step, round, head, onSet, extras }) {
    const [draft, setDraft] = React.useState(null);
    const draftRef = React.useRef(null);
    const dragging = React.useRef(false);
    const transformer = useStepTransformer ? useStepTransformer(step) : undefined;
    const current = Math.min(max, Math.max(min, value));
    const shown = draft === null ? value : draft;
    const commit = next => {
        draftRef.current = null;
        setDraft(null);
        onSet(round(Math.min(max, Math.max(min, next))));
    };
    const slider = Slider
        ? h("div", { key: prefix + "-slider", className: sliderClasses.routeSlider },
            h(Slider, {
                value: Math.min(max, Math.max(min, shown)),
                start: min,
                end: max,
                gamepadStep: step,
                valueTransformer: transformer,
                onChange: next => {
                    draftRef.current = next;
                    setDraft(next);
                    if (!dragging.current) {
                        commit(next);
                    }
                },
                onDragStart: () => { dragging.current = true; },
                onDragEnd: () => {
                    dragging.current = false;
                    if (draftRef.current !== null) {
                        commit(draftRef.current);
                    }
                }
            }))
        : null;
    const buttons = [
        Slider ? null : textButton(prefix + "-minus", "−" + step, () => commit(current - step), false, "cdlts-step"),
        Slider ? null : textButton(prefix + "-plus", "+" + step, () => commit(current + step), false, "cdlts-step")
    ].concat(extras ? extras(commit) : []);
    return [
        row(prefix + "-head", label, head(shown)),
        slider,
        buttons.some(Boolean) ? controlsRow(prefix + "-steps", "", h("div", { className: "cdlts-seg" }, ...buttons)) : null
    ];
}

function KerbRow({ value, onSet, label, box }) {
    const t = useText();
    return h(RangeRows, {
        prefix: "k", label, value, min: kerbRange.min, max: kerbRange.max, step: kerbRange.step, round: kerbTenths, head: box || kerbText, onSet,
        extras: commit => [textButton("k-home", t("UI.KerbDefault", "Default"), () => commit(kerbRange.home))]
    });
}

function SupportBar({ list, chosen, planned }) {
    const t = useText();
    const distance = api.useValue(margin);
    const midOn = api.useValue(mid);
    const lang = api.useValue(language);
    const autoName = t("UI.SupportAuto", "Automatic") + (planned ? " (" + nameOf(planned, lang) + ")" : "");
    const families = familiesOf(list);
    const current = list.find(entry => entry.id === chosen);
    const currentFamily = current ? families.find(f => f.id === (current.fam || "post")) : null;
    // The button is for a post (the planner's choice counts: which it picks is the road's): none for an arm, a gantry or a wall.
    const inForce = current || planned;
    const midOffered = inForce ? !!inForce.mid : chosen === "";
    if (current) {
        lastVariant[current.fam || "post"] = current.id;
    }
    const tileOf = (key, name, thumb, selected, onSelect) => {
        const button = h(gameUi.Button, {
            className: "cdlts-support",
            theme: { button: gridClasses && gridClasses.item ? gridClasses.item : "cdlts-tile-plain" },
            selected,
            onSelect
        }, thumb ? h("img", { className: "cdlts-support-img", src: thumb }) : h("div", { className: "cdlts-tile-code" }, name.slice(0, 2)));
        return gameUi.Tooltip ? h(gameUi.Tooltip, { key, tooltip: name }, button) : h("div", { key }, button);
    };
    const formName = family => t("UI.Family." + family.id, nameOf(family.items[0], lang));
    const forms = [tileOf("s-auto", t("UI.SupportAuto", "Automatic"), "", chosen === "", () => actions.setSupport(""))].concat(families.map(family => {
        const shown = family.items.find(entry => entry.id === lastVariant[family.id]) || family.items[0];
        return tileOf("s-f-" + family.id, formName(family), shown.thumb, family === currentFamily, () => actions.setSupport(shown.id));
    }));
    const variants = currentFamily && currentFamily.items.length > 1
        ? currentFamily.items.map(entry => tileOf("s-v-" + entry.id, nameOf(entry, lang), entry.thumb, chosen === entry.id, () => actions.setSupport(entry.id))) : null;
    return section("supports", [
        row("s-head", t("UI.Support", "Support"), current ? nameOf(current, lang) : autoName),
        h("div", { key: "s-tiles", className: "cdlts-supports" }, forms),
        variants ? h("div", { key: "s-variants", className: "cdlts-supports cdlts-variants" }, variants) : null,
        h(KerbRow, { key: "s-kerb", value: distance, onSet: actions.setMargin, label: t("UI.Kerb", "Distance from the kerb") }),
        midOffered ? row("s-mid", t("UI.MidRoad", "Post in the road"), toggle("s-mid-c", midOn, actions.setMid),
            { tooltip: t("UI.MidRoadTip", "Same as the / key: the post stands at the cursor, facing the nearest lane's traffic.") }) : null,
        h(ProfileRows, { key: "s-profile", prefix: "s" })
    ]);
}

// What the tool says about the state it is in (in a colour of its own when it refuses): a part of its own, so that a new line renders only it.
function HintLine() {
    const hint = hintOf(api.useValue(hintBinding));
    return hint ? note("hint", hint.text, hint.bad ? "bad" : "") : null;
}

// Clearing the game's markings: how many areas there are and how many markings they hide, and the button that deletes them all and brings the markings back
// (after the game's confirmation dialog; without it a second click).
function ClearBlock() {
    const t = useText();
    const state = useUi();
    const areas = api.useValue(clearAreas);
    const hidden = api.useValue(clearHidden);
    const dialog = gameUi.ConfirmationDialog;
    const restore = () => {
        if (dialog) {
            setUi({ confirming: true });
        } else if (state.confirming) {
            actions.restoreMarkings();
            setUi({ confirming: false });
        } else {
            setUi({ confirming: true });
        }
    };
    return section("clear", [
        note("count", t("UI.ClearCount", "{0} area(s), {1} game lines hidden.").replace("{0}", String(areas)).replace("{1}", String(hidden))),
        wideButton("restore", !dialog && state.confirming ? t("UI.RestoreConfirm", "Click again: delete every area and restore") : t("UI.Restore", "Restore all of the game's markings"),
            icons.again, restore, !dialog && state.confirming),
        note("keep", t("UI.RestoreNote", "Without this mod the game's lines return."))
    ]);
}

// The game's confirmation dialog of the restore button: beside the panels, not inside them (the game's dialogs cover the screen, and a panel's scrolling box would
// clip it).
function RestoreDialog() {
    const t = useText();
    const state = useUi();
    const count = api.useValue(orphans);
    const dialog = gameUi.ConfirmationDialog;
    if (!dialog || !(state.confirming || state.confirmingOrphans)) {
        return null;
    }
    if (state.confirmingOrphans) {
        return h(dialog, {
            title: t("UI.OrphansDelete", "Delete the items with no road near them"),
            message: t("UI.OrphansAsk", "Delete the {0} sign(s) and marking(s) that have no road near them?").replace("{0}", String(count)),
            onConfirm: () => { setUi({ confirmingOrphans: false }); actions.deleteOrphans(); },
            onCancel: () => setUi({ confirmingOrphans: false })
        });
    }
    return h(dialog, {
        title: t("UI.Restore", "Restore all of the game's markings"),
        message: t("UI.RestoreAsk", "Delete every area that clears the game's markings, and draw them all again?"),
        onConfirm: () => { setUi({ confirming: false }); actions.restoreMarkings(); },
        onCancel: () => setUi({ confirming: false })
    });
}

// The items with no road in reach and the button that deletes them (after the game's confirmation dialog; without it a second press).
function OrphanRows() {
    const t = useText();
    const state = useUi();
    const count = api.useValue(orphans);
    const dialog = gameUi.ConfirmationDialog;
    const press = () => {
        if (dialog || !state.confirmingOrphans) {
            setUi({ confirmingOrphans: true });
        } else {
            actions.deleteOrphans();
            setUi({ confirmingOrphans: false });
        }
    };
    return h("div", { className: "cdlts-orphans" },
        row("orphans", t("UI.Orphans", "Items with no road near them"), String(count)),
        count > 0 ? wideButton("orphans-delete", !dialog && state.confirmingOrphans ? t("UI.OrphansAgain", "Press again: delete them") : t("UI.OrphansDelete", "Delete the items with no road near them"), icons.trash, press, !dialog && state.confirmingOrphans) : null,
        note("orphans-note", t("UI.OrphansNote", "Mostly from changing city before 0.12.5.")));
}

// The save-safe store (0.12.38): what it did with this city, the records of it offered when none fitted well enough to restore on its own, and the roads that changed since
// their record was written, whose items are put away (not drawn) until they are placed or deleted here.
function SaveData() {
    const t = useText();
    const raw = api.useValue(saveDataBinding);
    let data = null;
    try {
        data = raw ? JSON.parse(raw) : null;
    } catch (e) {
        data = null;
    }
    if (!data) {
        return note("savedata-none", t("UI.SaveDataNone", "Load a city to see its data."));
    }
    const parts = (data.status || "").split(":");
    const statusText = {
        "": t("UI.SaveDataIdle", "Nothing restored yet in this city."),
        none: t("UI.SaveDataNoRecord", "No data for this city yet; written at the next save."),
        legacy: t("UI.SaveDataLegacy", "{0} item(s) in the save itself: the next save moves them.").replace("{0}", parts[1] || "0"),
        restored: t("UI.SaveDataRestored", "Restored: {0} placed, {1} hidden on {2} changed road(s).").replace("{0}", parts[1] || "0").replace("{1}", parts[2] || "0").replace("{2}", parts[3] || "0"),
        saved: t("UI.SaveDataSaved", "Saved to the store: {0} item(s), {1} put away.").replace("{0}", parts[1] || "0").replace("{1}", parts[2] || "0"),
        offers: t("UI.SaveDataOffers", "No record for this save; these fit under {0}: choose or ignore.").replace("{0}", "95 %"),
        ignored: t("UI.SaveDataIgnored", "The records offered were ignored.")
    }[parts[0]] || (parts[0] === "error" ? t("UI.SaveDataError", "Error: ") + parts.slice(1).join(":") : data.status);
    // What the game's saving or loading did that the store does not expect (0.12.51): code -> count.
    const warnText = {
        late: t("UI.SaveDataWarnLate", "A save was written before this mod's step: its record followed."),
        nosave: t("UI.SaveDataWarnNoSave", "A save has no record and none could be made: loading matches by road."),
        unreported: t("UI.SaveDataWarnUnreported", "The game did not report a save done: the record was written anyway."),
        leak: t("UI.SaveDataWarnLeak", "{0} item(s) were in the save: removed; the store's record is used."),
        plants: t("UI.SaveDataWarnPlants", "{0} greening plant(s) were in the save: removed and replanted.")
    };
    const warnings = Object.keys(data.warn || {}).map(code => note("savedata-warn-" + code, (warnText[code] || code).replace("{0}", String(data.warn[code])), "warn"));
    const why = {
        gone: t("UI.SaveDataGone", "deleted, moved or split"),
        moved: t("UI.SaveDataMoved", "its curve changed"),
        replaced: t("UI.SaveDataReplaced", "another road type"),
        upgraded: t("UI.SaveDataUpgraded", "upgraded"),
        junction: t("UI.SaveDataJunction", "a road joined or left at its end")
    };
    const offers = (data.offers || []).map((o, n) => controlsRow("offer-" + n, o.name + "  " + o.time + "  " + o.score + "%  (" + o.items + ")",
        textButton("offer-r-" + n, t("UI.SaveDataRestore", "Restore"), () => actions.saveDataRestore(n))));
    const away = data.away || [];
    const roads = away.map((a, n) => controlsRow("away-" + a.i,
        (why[a.why] || a.why) + "  ·  " + t("UI.SaveDataItems", "{0} item(s)").replace("{0}", String(a.items)) + (a.kerb ? "  ·  " + t("UI.SaveDataKerb", "kerb style") : ""),
        h("div", { className: "cdlts-actions" },
            textButton("away-l-" + a.i, t("UI.SaveDataLocate", "Show"), () => actions.saveDataLocate(a.i)),
            textButton("away-p-" + a.i, t("UI.SaveDataPlace", "Place"), () => actions.saveDataPlace(a.i)),
            textButton("away-d-" + a.i, t("UI.SaveDataDelete", "Delete"), () => actions.saveDataDelete(a.i)))));
    return h("div", { className: "cdlts-savedata" },
        note("savedata-status", statusText),
        warnings.length > 0 ? warnings : null,
        offers.length > 0 ? section("savedata-offers", offers.concat([wideButton("offer-ignore", t("UI.SaveDataIgnore", "Ignore them"), icons.remove, () => actions.saveDataIgnore())])) : null,
        away.length > 0 ? section("savedata-away", [note("away-note", t("UI.SaveDataAwayNote", "Items on these roads are hidden: they changed after the save."))]
            .concat(roads)
            .concat(away.length > 1 ? [h("div", { key: "away-all", className: "cdlts-actions" },
                textButton("away-pa", t("UI.SaveDataPlaceAll", "Place all"), () => actions.saveDataPlace(-1)),
                textButton("away-da", t("UI.SaveDataDeleteAll", "Delete all"), () => actions.saveDataDelete(-1)))] : [])) : null,
        note("savedata-where", t("UI.SaveDataWhere", "Kept in ModsData/CDLC.TS/Saves.")));
}

// The steps of the test switches: the night glow of the whole face in tenths of a nit; the night brightness of the faces as a factor in tenths; the LED glow by
// day in hundreds of nits and at night in nits.
const nightSteps = [0, 1, 2, 5, 10, 20];
const gainSteps = [10, 15, 20, 30, 40, 60];
const ledDaySteps = [10, 30, 60, 120, 200, 250];
const ledNightSteps = [5, 10, 15, 25, 40];
const lampSteps = [0, 1, 2, 3, 4, 6, 8, 12, 20];
// The draw distance of the signs and markings (m): the same steps as StreamOptions.FarSteps (700 is the default).
const viewSteps = [300, 500, 700, 1000, 1500, 2000, 3000];

// The memory panel (0.12.104; the owner: "在设置中加入一个当前Mod内存显存占用，并给出档位建议，绘制对应UI/ux"): the graphics memory this mod holds against its budget (a meter), the memory,
// what they are made of, and the draw distance as a list of its steps, each with what it would take, how heavy that is and the one that is suggested. A step is pressed to be used.
// The meter is as long as 150 % of the budget, with a tick at the half and at the whole of it, so that going over shows.
const fpScale = 1.5;

// The colour of a level (light, fine, heavy): a bar's fill and a figure's text; a level that is not one is the heavy colour.
const fillClass = ["cdlts-bg0", "cdlts-bg1", "cdlts-bg2"];
const textClass = ["cdlts-fc0", "cdlts-fc1", "cdlts-fc2"];
const levelFill = level => fillClass[level] || fillClass[2];
const levelText = level => textClass[level] || textClass[2];

function bytesText(bytes) {
    if (!(bytes >= 0)) {
        return "—";
    }
    const mb = bytes / 1048576;
    return mb >= 1024 ? (mb / 1024).toFixed(1) + " GB" : mb >= 10 ? Math.round(mb) + " MB" : mb.toFixed(1) + " MB";
}

function countText(n) {
    return String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}

function meter(key, bytes, budget, level) {
    const pct = budget > 0 ? Math.max(0, Math.min(100, bytes / (budget * fpScale) * 100)) : 0;
    return h("div", { key, className: "cdlts-meter" },
        h("div", { key: "fill", className: "cdlts-meter-fill " + levelFill(level), style: { width: pct + "%" } }),
        h("div", { key: "half", className: "cdlts-meter-tick", style: { left: 100 / fpScale / 2 + "%" } }),
        h("div", { key: "whole", className: "cdlts-meter-tick", style: { left: 100 / fpScale + "%" } }));
}

// One step of the draw distance: its far limit, the word of the suggested one, a small bar of what it would take on the meter's scale, the figure and how heavy it is. The step in use is not the
// button's `selected` (the game's look for it floods the row with its accent colour): it has a quiet fill and a marker of its own (CSS, cdlts-tier-now), and the suggested one a chip.
function tierRow(step, current, rec, budget, levels, suggest) {
    const known = step.m >= 0;
    const pct = known && budget > 0 ? Math.max(0, Math.min(100, step.m / (budget * fpScale) * 100)) : 0;
    return h(gameUi.Button, { key: "tier-" + step.d, variant: "flat", className: "cdlts-btn cdlts-tier" + (step.d === current ? " cdlts-tier-now" : ""), onSelect: () => actions.setViewDistance(step.d) },
        h("div", { key: "mark", className: "cdlts-tier-mark" }),
        h("div", { key: "d", className: "cdlts-tier-d" }, step.d + " m"),
        h("div", { key: "tag", className: "cdlts-tier-tag" }, step.d === rec ? h("span", { className: "cdlts-tier-chip" }, suggest) : null),
        h("div", { key: "track", className: "cdlts-tier-track" }, known ? h("div", { className: "cdlts-meter-fill " + levelFill(step.l), style: { width: pct + "%" } }) : null),
        h("div", { key: "mb", className: "cdlts-tier-mb" }, bytesText(step.m)),
        h("div", { key: "lv", className: "cdlts-tier-lv " + levelText(step.l) }, known ? levels[step.l] || "" : ""));
}

// A line under a figure or a list that says what it is made of or how it is meant: small and dim, so that the figures lead.
function sub(key, text) {
    return h("div", { key, className: "cdlts-fp-sub" }, text);
}

function FootprintPanel() {
    const t = useText();
    const raw = api.useValue(footprintBinding);
    const current = api.useValue(viewDistance);
    React.useEffect(() => {
        actions.watchFootprint(true);
        return () => actions.watchFootprint(false);
    }, []);
    let data = null;
    try {
        data = raw ? JSON.parse(raw) : null;
    } catch (e) {
        data = null;
    }
    const levels = [t("UI.FpLight", "Light"), t("UI.FpFine", "Fine"), t("UI.FpHeavy", "Heavy")];
    const ready = !!data && data.ok === true && data.rec > 0;
    const steps = data && data.steps && data.steps.length > 0 ? data.steps : viewSteps.map(d => ({ d, m: -1, l: 2 }));
    const rec = ready ? data.rec : 0;
    const advice = !ready ? t("UI.FpWait", "Wait for the tiles to be built.")
        : rec === current ? t("UI.FpAtBest", "This is the farthest that fits.")
            : rec > current ? t("UI.FpRaise", "Room to go to {0} m.").replace("{0}", String(rec))
                : t("UI.FpLower", "Heavy here: {0} m fits better.").replace("{0}", String(rec));
    const head = !data ? [sub("fp-wait", t("UI.FpReading", "Reading..."))] : [
        row("fp-vram", t("UI.FpVram", "Graphics memory"), h("div", { className: "cdlts-fp-val " + levelText(data.lv) }, bytesText(data.v) + " / " + bytesText(data.b)), { tooltip: t("UI.FpBudgetTip", "Budget: 5 % of the card's memory, 192 to 768 MB. Light up to half of it.") }),
        meter("fp-meter", data.v, data.b, data.lv),
        sub("fp-parts", t("UI.FpMeshes", "Meshes") + " " + bytesText(data.mesh) + " · " + t("UI.FpPictures", "Pictures") + " " + bytesText(data.face) + " · " + t("UI.FpDetail", "Detail") + " " + bytesText(data.det)),
        row("fp-ram", t("UI.FpRam", "Memory (RAM)"), bytesText(data.r)),
        sub("fp-counts", countText(data.rend) + " " + t("UI.FpRenderers", "renderers") + " · " + countText(data.tiles) + " " + t("UI.FpTiles", "tiles") + " · " + countText(data.items) + " " + t("UI.FpItems", "items"))
    ];
    return h("div", { className: "cdlts-fp" },
        head,
        h("div", { key: "fp-tiers", className: "cdlts-fp-title" }, t("UI.ViewDistance", "Sign and marking distance (m)")),
        h("div", { key: "fp-list", className: "cdlts-tiers" }, steps.map(step => tierRow(step, current, rec, data ? data.b : 0, levels, t("UI.FpSuggest", "Suggested")))),
        controlsRow("fp-advice", advice, ready && rec !== current ? textButton("fp-use", t("UI.FpUse", "Use {0} m").replace("{0}", String(rec)), () => actions.setViewDistance(rec)) : null),
        sub("fp-where", t("UI.FpHelpTip", "This mod's meshes and pictures only. Steps are estimates for the camera's place.")));
}

// How the signs look (0.12.47: what was left of the test switches that a player sets): the barriers' baked detail, the faces at night and the LED screens.
function DisplaySettings() {
    const t = useText();
    const currentNight = api.useValue(night);
    const currentGain = api.useValue(nightGain);
    const currentLedDay = api.useValue(ledDay);
    const currentLedNight = api.useValue(ledNight);
    const currentDetail = api.useValue(detail);
    const currentLampLit = api.useValue(lampLit);
    const currentLampGlow = api.useValue(lampGlow);
    const step = (key, label, steps, value, show, send) => controlsRow(key, label, stepper(key + "-s", show(value), () => send(stepFrom(steps, value, -1)), () => send(stepFrom(steps, value, 1))));
    return h("div", { className: "cdlts-display" },
        controlsRow("detail", t("UI.Detail", "Baked detail"), segments("d", [{ v: true, l: t("UI.DetailOn", "On") }, { v: false, l: t("UI.DetailOff", "Off") }], currentDetail, v => actions.setDetail(v))),
        step("gain", t("UI.NightGain", "Night brightness (x)"), gainSteps, currentGain, v => "×" + v / 10, actions.setNightGain),
        step("glow", t("UI.Night", "Night glow (nits)"), nightSteps, currentNight, v => String(v / 10), actions.setNight),
        step("ledday", t("UI.LedDay", "LED by day (x100 nits)"), ledDaySteps, currentLedDay, v => String(v), actions.setLedDay),
        step("lednight", t("UI.LedNight", "LED at night (nits)"), ledNightSteps, currentLedNight, v => String(v), actions.setLedNight),
        step("lamplit", t("UI.LampLit", "Light boxes at night (nits)"), lampSteps, currentLampLit, v => String(v), actions.setLampLit),
        step("lampglow", t("UI.LampGlow", "Lanterns and neon at night (nits)"), lampSteps, currentLampGlow, v => String(v), actions.setLampGlow),
        h(ProfileRows, { key: "d-profile", prefix: "d" }));
}

// The keys of what the tool does now (0.12.47; the owner: "独立小窗口提示按键"): a small panel of its own, a row for each key, while the tool runs; the line of help in the catalogue says
// the rest.
// The keys as Core draws them (CDLC.mjs HintKeys; the owner: "按键提示面板应该复用Core的风格"): "Ctrl+Z / Ctrl+Y" is the caps [Ctrl] + [Z], "/", [Ctrl] + [Y]; "左键", "Left click" and the like are the
// game's mouse glyphs; "[  ]" and "← →" are two caps. A text is parsed into alternatives (split by " / "), each of groups joined by "+", each of caps or mouse buttons.
const mouseGlyphs = { lmb: "Media/Mouse/LMB.svg", rmb: "Media/Mouse/RMB.svg", mmb: "Media/Mouse/MMB.svg", wheel: "Media/Mouse/Scrollwheel.svg" };
const mouseNames = [
    [/^(左键|left click|left button|lmb)$/i, "lmb"], [/^(右键|right click|right button|rmb)$/i, "rmb"],
    [/^(中键|middle click|middle button|mmb)$/i, "mmb"], [/^(滚轮|wheel|mouse wheel|scroll)$/i, "wheel"]
];

function keyParts(text) {
    return String(text || "").split(" / ").map(alternative => alternative.trim()).filter(alternative => alternative.length > 0).map(alternative =>
        alternative.split("+").map(group => group.trim()).filter(group => group.length > 0).map(group => {
            const named = mouseNames.find(([pattern]) => pattern.test(group));
            if (named) {
                return [{ mouse: named[1] }];
            }
            // "按住 Ctrl 单击", "Hold Alt": "hold" is a word beside the caps, not a key; a click is the left button.
            return group.split(/\s+/).map(word => /^(单击|click)$/i.test(word) ? { mouse: "lmb" } : /^(按住|hold)$/i.test(word) ? { word } : { cap: word });
        }));
}

// How wide the caps of a text are, in rem (Core's caps: 24 rem at least, 12 rem bold type, 6 rem of padding each side, 2 rem borders): for the column the labels start at.
function capsWidth(text) {
    let width = 0;
    keyParts(text).forEach((groups, a) => {
        width += a > 0 ? 16 : 0;
        groups.forEach((items, g) => {
            width += g > 0 ? 13 : 0;
            items.forEach((item, i) => {
                width += (i > 0 ? 4 : 0) + (item.mouse ? 22 : item.word ? 8 * item.word.length + 4 : Math.max(24, 8 * String(item.cap).length + 16));
            });
        });
    });
    return Math.ceil(width);
}

function KeyCaps({ text }) {
    const children = [];
    keyParts(text).forEach((groups, a) => {
        if (a > 0) {
            children.push(h("div", { key: "or" + a, className: "cdlts-key-or" }, "/"));
        }
        groups.forEach((items, g) => {
            if (g > 0) {
                children.push(h("div", { key: "plus" + a + "-" + g, className: "cdlts-key-plus" }, "+"));
            }
            items.forEach((item, i) => children.push(item.mouse
                ? h("img", { key: "m" + a + "-" + g + "-" + i, className: "cdlts-mouse", src: mouseGlyphs[item.mouse] })
                : item.word
                    ? h("div", { key: "w" + a + "-" + g + "-" + i, className: "cdlts-key-word" }, item.word)
                    : h("div", { key: "c" + a + "-" + g + "-" + i, className: "cdlts-key" }, item.cap)));
        });
    });
    return h("div", { className: "cdlts-keys" }, children);
}

// Where the player dragged the key hints to, in rem from the screen's top left; null is the place the stylesheet gives them, at the bottom left as Core's hints are. It is kept here and not in the
// card because the card is gone whenever the tool is left.
let keysAt = null;

// A card dragged to the edge keeps a part of itself on the screen, to be taken back from there (the screen may be smaller than when it was put down).
function clampKeys(x, y) {
    const size = screenSizeRem();
    return { x: Math.max(0, Math.min(x, size.w - 120)), y: Math.max(0, Math.min(y, size.h - 40)) };
}

function keysPlace() {
    if (!keysAt) {
        return undefined;
    }
    const at = clampKeys(keysAt.x, keysAt.y);
    return { left: at.x + "rem", top: at.y + "rem", bottom: "auto" };
}

// The rows of the key card in two columns (0.12.90; the owner: "把按键提示改成两列"): the first column has the larger half and the rows go down it and on in the second, in the order the tool sends them.
function keyColumns(keys) {
    const half = Math.ceil(keys.length / 2);
    return [keys.slice(0, half), keys.slice(half)].filter(column => column.length > 0);
}

// The keys of what the tool does now (0.12.47; the owner: "独立小窗口提示按键"; 0.12.67: in Core's style; 0.12.68: at the bottom left and to be dragged by hand; 0.12.90: two columns): a row for each key,
// while the tool runs; the line of help in the catalogue says the rest. The card is the grip: the mouse goes down on it, and moves and lets go anywhere on the screen.
function KeysPanel() {
    const t = useText();
    const hint = hintOf(api.useValue(hintBinding));
    const [dragging, setDragging] = React.useState(false);
    const release = React.useRef(null);
    React.useEffect(() => () => {
        if (release.current) {
            release.current();
        }
    }, []);
    if (!hint || hint.keys.length === 0) {
        return null;
    }
    const grab = event => {
        const unit = remPixels();
        if (event.button > 0 || unit <= 0 || typeof document === "undefined" || !document.addEventListener) {
            return;
        }
        const box = event.currentTarget.getBoundingClientRect();
        const parent = event.currentTarget.offsetParent;
        const origin = parent ? parent.getBoundingClientRect() : { left: 0, top: 0 };
        const start = { x: event.clientX, y: event.clientY, left: box.left - origin.left, top: box.top - origin.top };
        // The card is moved on its element while the mouse moves (a render of the panel at every move left it behind the pointer, and a pointer that got ahead of it was over the game's tool: the
        // release then placed a sign); the shield inside it covers the screen while it is held, so the pointer is over the card whatever it does.
        const card = event.currentTarget;
        const move = e => {
            keysAt = clampKeys((start.left + e.clientX - start.x) / unit, (start.top + e.clientY - start.y) / unit);
            const at = keysPlace();
            card.style.left = at.left;
            card.style.top = at.top;
            card.style.bottom = at.bottom;
        };
        const drop = () => {
            document.removeEventListener("mousemove", move);
            document.removeEventListener("mouseup", drop);
            release.current = null;
            setDragging(false);
        };
        document.addEventListener("mousemove", move);
        document.addEventListener("mouseup", drop);
        release.current = drop;
        setDragging(true);
    };
    // Each column's labels start at the width of its own widest keys.
    return h("div", { className: dragging ? "cdlts-hints cdlts-hints-held" : "cdlts-hints", style: keysPlace(), onMouseDown: grab },
        dragging ? h("div", { key: "shield", className: "cdlts-shield" }) : null,
        h("div", { className: "cdlts-hints-title" }, t("UI.Keys", "Keys")),
        h("div", { className: "cdlts-hints-cols" }, keyColumns(hint.keys).map((rows, c) => {
            const column = Math.max(92, ...rows.map(pair => capsWidth(pair[0])));
            return h("div", { key: "c" + c, className: "cdlts-hints-col" }, rows.map((pair, index) => h("div", { key: "k" + index, className: "cdlts-hint" },
                h("div", { className: "cdlts-hint-keys", style: { minWidth: column + "rem" } }, h(KeyCaps, { text: pair[0] })),
                h("div", { className: "cdlts-hint-label" }, pair[1]))));
        })));
}

// The import of a picture (0.12.29): the PNG files of the folder, each with its size and the button that imports it, the buttons that open the folder and list it again, and what the last
// import or save said.
function ImportBlock({ custom }) {
    const t = useText();
    const files = custom ? custom.files : [];
    return section("import", [
        row("im-head", t("UI.CustomImport", "Import a PNG picture"), null, { uppercase: true }),
        note("im-where", t("UI.CustomWhere", "Put PNGs in this folder, then refresh:") + " " + (custom && custom.folder ? custom.folder : "ModsData/CDLC.TS/Custom")),
        h("div", { key: "im-buttons", className: "cdlts-actions" },
            wideButton("im-open", t("UI.CustomOpen", "Open the folder"), icons.add, () => actions.openCustomFolder()),
            wideButton("im-list", t("UI.CustomList", "List again"), icons.again, () => actions.listCustomFiles())),
        files.length === 0 ? note("im-none", t("UI.CustomNone", "There is no PNG picture in the folder yet.")) : files.map((file, index) => controlsRow("im-f" + index, file.n,
            h("div", { className: "cdlts-seg" },
                h("div", { className: "cdlts-value" }, (file.w > 0 ? file.w + "×" + file.h : "?") + "  " + file.kb + " KB"),
                textButton("im-do" + index, t("UI.CustomDo", "Import"), () => { setUi({ importing: false }); actions.importCustom(file.n); }, false, "cdlts-step")))),
        custom && custom.note ? note("im-note", custom.note, custom.bad ? "bad" : "") : null,
        wideButton("im-close", t("UI.CustomClose", "Close the import"), icons.left, () => setUi({ importing: false }))
    ]);
}

// The entries of a tab: the search box, the signs, the import of a picture on the player's own tab, the tool's line of help.
function MainPanel() {
    const t = useText();
    const state = useUi();
    const entries = catalogOf(api.useValue(catalog));
    const chosen = api.useValue(selected);
    const currentMode = api.useValue(mode);
    const running = api.useValue(toolActive);
    const custom = customOf(api.useValue(customBinding));

    const matching = filteredOf(entries, state.query, state.kind);
    const kind = matching.current;
    const page = paginate(kind === "custom" ? matching.list.concat([addEntry(custom, t)]) : matching.list, state.page, perPage);
    return h(gameUi.Scrollable, { vertical: true, className: "cdlts-scroll", style: { maxHeight: bodyHeight(0), minHeight: listMinHeight(matching.kinds.length + 2) } },
        h(SearchBox, { key: "search" }),
        h(SignGrid, { key: "grid", page, chosen, active: running && currentMode === 0 }),
        kind === "custom" && state.importing ? h(ImportBlock, { key: "import", custom }) : null,
        h(HintLine, { key: "hint" }));
}

// The save data and the display settings: the last tab of the strip.
function SettingsPanel() {
    const t = useText();
    return h(gameUi.Scrollable, { vertical: true, className: "cdlts-scroll", style: { maxHeight: bodyHeight(0) } },
        h(gameUi.PanelFoldout, { key: "savedata", header: t("UI.SaveData", "Save data"), initialExpanded: true }, h(SaveData, null), h(OrphanRows, null)),
        h(gameUi.PanelFoldout, { key: "memory", header: t("UI.Memory", "Memory and distance"), initialExpanded: true }, h(FootprintPanel, null)),
        h(gameUi.PanelFoldout, { key: "display", header: t("UI.Display", "Display"), initialExpanded: false }, h(DisplaySettings, null)));
}

// Place, edit, delete and the eyedropper (pick the style and settings of a placed item, then place): a small rounded strip under the tabs. A mode that is on leaves the tool when it is pressed again, as the game's own tools do.
function ActionPill() {
    const t = useText();
    const chosen = api.useValue(selected);
    const currentMode = api.useValue(mode);
    const running = api.useValue(toolActive);
    const modeButton = (key, label, src, value, enabled) => roundButton(key, src, label, () => {
        if (running && currentMode === value) {
            actions.leave();
        } else if (enabled) {
            actions.setMode(value);
        }
    }, running && currentMode === value && (value !== 0 || !!chosen));
    return h("div", { className: "cdlts-pill" },
        modeButton("place", t("UI.Place", "Place"), icons.dockPlace, 0, !!chosen),
        modeButton("edit", t("UI.Edit", "Edit"), icons.dockEdit, 2, true),
        modeButton("delete", t("UI.Delete", "Delete"), icons.dockDelete, 1, true),
        modeButton("pick", t("UI.Pick", "Pick style (Shift: whole pole)"), icons.dockPick, 3, true));
}

// Back from the settings of an entry to the entries (the back button, Esc): a sign that is about to be placed is let go of, a sign being edited stays as it is.
function back() {
    const editor = ui.editor;
    if (editor && editor.mode === "item") {
        setUi({ showList: true, flyout: true });
        return;
    }
    actions.leave();
    setUi({ showList: false });
}

function onEscape(event) {
    const target = event && event.target;
    const tag = target && typeof target.tagName === "string" ? target.tagName.toUpperCase() : "";
    if (event && event.key === "Escape" && !event.defaultPrevented && tag !== "INPUT" && tag !== "TEXTAREA" && tag !== "SELECT") {
        back();
    }
}

// The dock: the strip, the strip of actions, and the panel beside them.
function Dock() {
    const t = useText();
    const state = useUi();
    const entries = catalogOf(api.useValue(catalog));
    const editor = editorOf(api.useValue(editorBinding));
    const matching = filteredOf(entries, state.query, state.kind);
    // The editor now, for the back button and Esc (they are not components: they cannot read the binding).
    ui.editor = editor;
    // A new editor (a sign chosen, a sign picked to edit) starts on its settings.
    const seen = editor ? editor.id + ":" + editor.mode : "";
    if (ui.seenEditor !== seen) {
        ui.seenEditor = seen;
        ui.showList = false;
        if (seen) {
            ui.settings = false;
        }
    }
    const config = !!editor && !ui.showList;
    const settings = ui.settings && !config;
    const open = ui.flyout || config;
    const listShown = open && !config;
    const tabs = tabsOf(matching.kinds, t);
    React.useEffect(() => {
        if (!config || typeof document === "undefined" || !document.addEventListener) {
            return undefined;
        }
        document.addEventListener("keydown", onEscape);
        return () => document.removeEventListener("keydown", onEscape);
    }, [config]);

    const isItem = !!editor && editor.mode === "item";
    const title = config ? (isItem ? t("UI.Editing", "Editing") : t("UI.Settings", "Settings")) + "  " + editor.code + "  " + editor.title
        : settings ? t("UI.SettingsTab", "Save data and display") : (tabs.find(tab => tab.id === matching.current) || tabs[0]).name;
    const header = h("div", { className: "cdlts-fly-head" },
        config ? iconButton("back", icons.back, t("UI.Back", "Back"), back, false) : null,
        h("div", { className: "cdlts-fly-title" }, title));
    const panel = open ? h(gameUi.Panel, { key: "panel", className: "cdlts-flypanel", style: { width: config ? editorPanelRem + "rem" : "470rem" }, header, onClose: config ? back : () => setUi({ flyout: false }) },
        config ? h(EditorPanel, null) : settings ? h(SettingsPanel, null) : h(MainPanel, null)) : null;
    return h("div", { className: "cdlts-dock" },
        h("div", { key: "rail", className: "cdlts-rail" },
            h(TabStrip, { key: "strip", tabs, active: settings ? settingsTab : matching.current, listShown }),
            h(ActionPill, { key: "actions" })),
        h("div", { key: "fly", className: "cdlts-fly" + (open ? " cdlts-fly-open" : "") + (open && config ? " cdlts-fly-wide" : ""), onMouseLeave: panelLeft }, panel));
}

// ------------------------------------------------------------------ the editor

// A number that has a slider as well as keys (0.12.76, GuideField.Slider, field.r = [from, to, step]: the tilt of a standing light box): the value in the head row, the game's slider committed on release
// with a draft shown while it is dragged, two step buttons where the game has no slider.
function FieldSlider({ field, target }) {
    const from = field.r[0], to = field.r[1], step = field.r[2];
    const value = Number(field.v);
    return h(RangeRows, {
        prefix: target + ":" + field.k, label: field.l, value: Number.isFinite(value) ? Math.min(to, Math.max(from, value)) : from, min: from, max: to, step,
        round: next => Math.min(to, Math.max(from, Math.round(next / step) * step)),
        head: shown => String(Math.round(shown * 1000) / 1000),
        onSet: snapped => actions.editField(field.k, String(Math.round(snapped * 1000) / 1000), target)
    });
}

// One setting of the editor: its label and a control of the kind it is.
function fieldRows(editor, field, t, onChoose) {
    const key = editor.id + ":" + field.k;
    const choose = value => (onChoose ? onChoose(value) : actions.editField(field.k, value, editor.id));
    if (field.t === "flag") {
        return [row(key, field.l, toggle(key + "-c", field.v === "1", on => actions.editField(field.k, on ? "1" : "0", editor.id)))];
    }
    if (field.t === "choice") {
        const layout = choiceLayout(field);
        const options = field.ch || [];
        const control = layout.kind === "buttons" ? segments(key + "-s", options, field.v, choose) : dropdown(key + "-d", options, field.v, choose, layout.stacked);
        return layout.stacked ? [fieldBlock(key, field.l, control, true)] : [controlsRow(key, field.l, control)];
    }
    if (Array.isArray(field.r) && field.r.length === 3 && field.r[2] > 0) {
        return [h(FieldSlider, { key: key + "-r", field, target: editor.id })];
    }
    const box = h(TextField, { key: key + "-t", field, target: editor.id });
    // A text box takes the rest of the row: a label of more than four characters is put beside it in a block of its own (the game's row wrapped it in two lines), a long one over it.
    const width = textWidth(field.l);
    if (field.w === true) {
        return [fieldBlock(key, field.l, box, true)];
    }
    return width > sideTextLabel ? [fieldBlock(key, field.l, box, width > inlineTextLabel)] : [row(key, field.l, box)];
}

// A setting's label and its control in one block (0.12.69; a label row and a full-width row under it took 90 rem for a text box): the label beside the control, or over it when it is long.
function fieldBlock(key, label, control, stacked) {
    return h("div", { key, className: stacked ? "cdlts-field cdlts-field-stack" : "cdlts-field" },
        h("div", { className: "cdlts-field-label" }, label),
        h("div", { className: stacked ? "cdlts-field-control cdlts-full" : "cdlts-field-control" }, control));
}

// The junction reading of a junction sign (路1, 路3, 路4, 路6, 路7): whether placing the sign reads the junction ahead into its names, form and lanes, the button
// that reads it again for a sign that stands, and what the last reading said. Nothing for any other sign.
function junctionRows(editor, t) {
    const jn = editor.jn;
    if (!jn || typeof jn !== "object") {
        return null;
    }
    const notes = Array.isArray(jn.notes) ? jn.notes : [];
    return section("junction", [
        row("jn-head", t("UI.JunctionTitle", "Junction ahead"), null, { uppercase: true }),
        row("jn-switch", t("UI.JunctionAuto", "Fill in from the junction when placing"), toggle("jn-c", !!jn.on, on => actions.setReadJunctions(on ? 1 : 0))),
        editor.mode === "item" ? wideButton("jn-read", t("UI.JunctionRead", "Read the junction ahead into this sign"), icons.again, () => actions.readJunction()) : null,
        jn.note ? note("jn-note", jn.note) : null,
        notes.map((line, index) => note("jn-w" + index, line, "warn"))
    ], "cdlts-junction");
}

// A text board on a wall: the button that reads the wall's curve again and bends the letters to it (0.12.77).
function facadeRows(editor, t) {
    return editor.fw === true && editor.mode === "item" ? wideButton("fw-fit", t("UI.FitWall", "Fit to the wall"), icons.again, () => actions.fitFacade()) : null;
}

// The automatic marking (0.12.44): the stretch chosen on the map, whose settings the fields below are (a change draws it again), with the buttons that let it go and undo
// its marking (pressed twice); with none chosen, how to choose one.
function autoRows(editor, t) {
    const auto = editor.auto;
    if (!auto || typeof auto !== "object") {
        return null;
    }
    if (!auto.on) {
        return section("auto", note("auto-how", t("UI.AutoHow", "Click a marked stretch to change its settings.")), "cdlts-auto");
    }
    const confirming = ui.confirmingAutoUndo === editor.id;
    const confirmingOld = ui.confirmingAutoOld === editor.id;
    const old = auto.old > 0 ? auto.old : 0;
    const size = t("UI.AutoSize", "{0} segment(s), {1} m").replace("{0}", String(auto.segs)).replace("{1}", String(Math.round(auto.len)));
    return section("auto", [
        row("auto-head", t("UI.AutoChosen", "Chosen stretch") + " · " + size, textButton("auto-forget", t("UI.AutoForget", "Let go"), () => actions.autoForget()), { uppercase: true }),
        auto.pending ? note("auto-note", t("UI.AutoPending", "Settings changed: drawn again in a moment.")) : null,
        Array.isArray(auto.ends) ? auto.ends.map((line, index) => note("auto-e" + index, line)) : null,
        old > 0 ? note("auto-old", t("UI.AutoOld", "{0} manual line(s) lie under the new ones.").replace("{0}", String(old)), "warn") : null,
        old > 0 ? wideButton("auto-old-delete", confirmingOld ? t("UI.AutoOldAgain", "Press again: delete them") : t("UI.AutoOldDelete", "Delete those {0} line(s)").replace("{0}", String(old)), icons.trash, () => {
            if (confirmingOld) {
                setUi({ confirmingAutoOld: null });
                actions.autoRemoveOld();
            } else {
                setUi({ confirmingAutoOld: editor.id });
            }
        }, confirmingOld) : null,
        wideButton("auto-undo", confirming ? t("UI.AutoUndoAgain", "Press again: undo its marking") : t("UI.AutoUndo", "Undo its marking (the game's markings come back)"), icons.trash, () => {
            if (confirming) {
                setUi({ confirmingAutoUndo: null });
                actions.autoUndo();
            } else {
                setUi({ confirmingAutoUndo: editor.id });
            }
        }, confirming)
    ], "cdlts-auto");
}

// The picture of a lamp-post ad (0.12.49): the PNG files of the folder, each with the button that puts it on the ad, the one that takes it off, and the folder's buttons. The list is read
// when the editor of such an ad opens.
function pictureRows(editor, custom, t) {
    const pic = editor.pic;
    if (!pic || typeof pic !== "object") {
        return null;
    }
    const files = custom && Array.isArray(custom.files) ? custom.files : [];
    return section("picture", [
        row("pic-head", t("UI.PicTitle", "Picture (PNG)"), null, { uppercase: true }),
        note("pic-where", t("UI.CustomWhere", "Put PNGs in this folder, then refresh:") + " " + (custom && custom.folder ? custom.folder : "ModsData/CDLC.TS/Custom")),
        h("div", { key: "pic-buttons", className: "cdlts-actions" },
            wideButton("pic-open", t("UI.CustomOpen", "Open the folder"), icons.add, () => actions.openCustomFolder()),
            wideButton("pic-list", t("UI.CustomList", "List again"), icons.again, () => actions.listCustomFiles())),
        files.length === 0 ? note("pic-none", t("UI.CustomNone", "There is no PNG picture in the folder yet.")) : files.map((file, index) => controlsRow("pic-f" + index, file.n,
            textButton("pic-use" + index, t("UI.PicUse", "Use"), () => actions.usePicture(file.n), false, "cdlts-step"))),
        pic.has ? wideButton("pic-off", t("UI.PicOff", "Take the picture off"), icons.trash, () => actions.usePicture("")) : null,
        custom && custom.note ? note("pic-note", custom.note, custom.bad ? "bad" : "") : null
    ], "cdlts-picture");
}

function laneName(t, k) {
    return t("UI.LaneN", "Lane {0}").replace("{0}", String(k));
}

// Where a board stands, in a word: automatic (side by side, centred), on the post (254), over a lane (counted from the driver's left), or by hand (put there with
// the arrow keys or by pointing; 255 is what the C# side calls it): then its distance from the first post.
function boardPlace(board, t) {
    if (board.lane === 254) {
        return t("UI.BoardPost", "on the post");
    }
    if (board.lane === 255) {
        return t("UI.BoardHand", "by hand") + " " + (Math.round((board.x || 0) * 10) / 10) + " m";
    }
    return board.lane > 0 ? laneName(t, board.lane) : t("UI.BoardAuto", "auto");
}

// The boards on the support being edited: each one a row (its name selects it for editing; Replace puts the sign chosen in the catalogue in its place; Remove
// takes it off), and a button adds the chosen sign as one more board. Only an edited sign has them.
function boardRows(editor, chosen, t, lang) {
    const boards = editor.boards || [];
    const many = boards.length > 1;
    // The most boards this support carries: 4 on a post or an arm, 12 on a gantry (the C# side says).
    const maxBoards = editor.max > 0 ? editor.max : 4;
    return section("boards", [
        row("b-head", t("UI.Boards", "Boards on this support"), boards.length + " / " + maxBoards, { uppercase: true }),
        // A block of its own and not a row of the game's (0.12.71): a row hosts one focusable child, and a board's has its name button and the buttons beside the place.
        boards.map(board => h("div", { key: "b" + board.m, className: "cdlts-boardrow" },
            textButton("b" + board.m + "-name", board.code + " " + board.title, () => actions.selectBoard(board.m), board.m === editor.sel, "cdlts-board"),
            h("div", { className: "cdlts-seg" },
                h("div", { className: "cdlts-value" }, boardPlace(board, t)),
                chosen ? iconButton("b" + board.m + "-rep", icons.replace, t("UI.Replace", "Replace"), () => actions.replaceBoard(board.m)) : null,
                many ? iconButton("b" + board.m + "-rem", icons.trash, t("UI.Remove", "Remove"), () => actions.removeBoard(board.m)) : null))),
        chosen ? note("b-chosen", t("UI.Chosen", "Chosen") + ": " + codeOf(chosen, lang) + " " + nameOf(chosen, lang)) : null,
        chosen && boards.length < maxBoards ? wideButton("b-add", t("UI.AddHere", "Add to this support"), icons.add, () => actions.addBoardHere()) : null
    ]);
}

// Where the board being edited stands on an arm or a beam: automatic (side by side with the others), on the post (a small board, under the lowest arm) or over a lane,
// counted from the driver's left. Nothing for a post.
function laneRows(editor, t) {
    if (!editor.boards || editor.boards.length === 0) {
        return null;
    }
    const post = editor.posts ? [{ v: "254", l: t("UI.LanePost", "Post") }] : [];
    const auto = { v: "0", l: t("UI.LaneAutoShort", "Auto") };
    const set = value => actions.setLane(Number(value) || 0);
    if (editor.laneState === "noroad") {
        const rows = [];
        if (post.length > 0) {
            rows.push(controlsRow("lane", t("UI.BoardWhere", "Where the board is"), segments("lane-s", [auto].concat(post), String(editor.lane || 0), set)));
        }
        rows.push(note("lane-note", t("UI.LaneNoRoad", "Not on a road: no lanes to read.")));
        return section("lanes", rows);
    }
    if (editor.laneState !== "ok" || !Array.isArray(editor.lanes) || editor.lanes.length === 0) {
        return null;
    }
    const options = [auto].concat(post, editor.lanes.map((x, index) => ({ v: String(index + 1), l: String(index + 1) })));
    return section("lanes", [
        controlsRow("lane", t("UI.Lane", "Lane (from the left)"), dropdown("lane-d", options, String(editor.lane || 0), set, false)),
        editor.boards.length >= 2 ? wideButton("spread", t("UI.Spread", "One lane for each board"), icons.lanes, () => actions.spreadLanes()) : null
    ]);
}

// The buttons that move the board being edited (as the arrow keys do): left and right along a beam, and up and down it from the support's clearance (0.12.73); up and down a post. A board on the post of a hung support
// keeps its place in the stack there (the standard's order): nothing to move.
function moveRow(editor, t) {
    if (!editor.boards || editor.boards.length === 0 || (editor.hung && editor.lane === 254)) {
        return null;
    }
    if (editor.onArm) {
        // A board on another mod's signal arm (0.12.57): along the arm by its free places, and up or down within the room its heads leave.
        return [controlsRow("move", t("UI.MoveArm", "Move along the arm"),
            h("div", { className: "cdlts-seg" }, iconButton("m-left", icons.left, t("UI.Left", "Left"), () => actions.nudgeBoard(-1)), iconButton("m-right", icons.right, t("UI.Right", "Right"), () => actions.nudgeBoard(1)))),
        controlsRow("move-up", t("UI.MoveArmUp", "Raise or lower"),
            h("div", { className: "cdlts-seg" }, iconButton("m-up", icons.up, t("UI.Up", "Up"), () => actions.nudgeBoard(-2)), iconButton("m-down", icons.down, t("UI.Down", "Down"), () => actions.nudgeBoard(2))))];
    }
    const upDown = h("div", { className: "cdlts-seg" }, iconButton("m-up", icons.up, t("UI.Up", "Up"), () => actions.nudgeBoard(-2)), iconButton("m-down", icons.down, t("UI.Down", "Down"), () => actions.nudgeBoard(2)));
    if (!!editor.hung && editor.laneState !== "post") {
        return [controlsRow("move", t("UI.MoveBeam", "Move along the beam"),
            h("div", { className: "cdlts-seg" }, iconButton("m-left", icons.left, t("UI.Left", "Left"), () => actions.nudgeBoard(-1)), iconButton("m-right", icons.right, t("UI.Right", "Right"), () => actions.nudgeBoard(1)))),
        controlsRow("move-up", t("UI.MoveBeamUp", "Raise or lower"), upDown)];
    }

    return controlsRow("move", t("UI.MovePost", "Raise or lower"), upDown);
}

// The way the board being edited faces on its post (0.12.8): a board in the stack on one post (a single post, a lamp post or a signal pole, the post of a hung support)
// turns on its own about the post; the steps, half a turn, and back to the support's facing. A board on a gantry's beam goes to its other face (0.12.22). Nothing for a
// board on two posts, an arm or a wall. 0.12.80 (the owner: "旋转角度调节应该支持滑条无级调节和按键步进调节"): the facing is a number to a tenth of a degree, set by the game's slider (on release),
// by the box, or stepped by the buttons and by [ and ] (15°, Shift 5°, Alt 1°); the sign about to be placed has the same row. -180 to 179.9: the two ends are one facing, half a turn is -180.
const turnRange = { min: -180, max: 179.9, step: 0.1 };
const turnTenths = value => Math.round(value * 10) / 10;
function turnRows(editor, t) {
    if (editor.faces) {
        return [controlsRow("face", editor.back ? t("UI.FaceBack", "On the gantry's back") : t("UI.FaceFront", "On the gantry's front"),
            h("div", { className: "cdlts-seg" }, textButton("t-face", editor.back ? t("UI.ToFront", "To the front") : t("UI.ToBack", "To the back"), () => actions.flipBoard())))];
    }
    if (!editor.turns) {
        return null;
    }
    const turn = Number(editor.turn) || 0;
    const steps = [["−90°", -90], ["−15°", -15], ["+15°", 15], ["+90°", 90]];
    return [
        h(RangeRows, {
            key: "turn", prefix: "t", label: t("UI.BoardTurnAngle", "Facing (°)"), value: turn, min: turnRange.min, max: turnRange.max, step: turnRange.step, round: turnTenths, onSet: actions.setTurn,
            head: shown => h(TextField, { key: editor.id + ":turn", field: { k: "turn", v: String(turnTenths(shown)), max: 6, cls: "cdlts-num" }, target: editor.id, onCommit: actions.setTurn }),
            extras: () => steps.map(([label, degrees]) => textButton("t" + degrees, label, () => actions.turnBoard(degrees), false, "cdlts-step"))
        }),
        controlsRow("turn-more", "", h("div", { className: "cdlts-seg" },
            textButton("t-round", t("UI.TurnRound", "Turn round"), () => actions.turnBoard(180)),
            textButton("t-reset", t("UI.TurnReset", "As the support faces"), () => actions.turnBoard(0), turn === 0)))
    ];
}

// How the support is painted (0.12.57): galvanised, graphite black (RAL 9011), dark or light grey; the boards' backs black whatever the steel is; the paint on the posts' root (none, the
// yellow and black 立面标记 of GB 51038 14.6.2, blue with blue and white rings). While placing, the support placed next.
function finishRows(editor, t) {
    if (!editor.painted) {
        return null;
    }
    // The root paint is for supports that have posts of their own to paint (a wall, another mod's pole or a space truss's legs have not).
    const rootable = editor.rootable !== false;
    const finish = Number(editor.finish) || 0;
    const names = [t("UI.FinishGalv", "Galvanised"), t("UI.FinishGraphite", "Graphite black"), t("UI.FinishDark", "Dark grey"), t("UI.FinishLight", "Light grey")];
    return [
        controlsRow("finish", t("UI.Finish", "The support's finish"), h("div", { className: "cdlts-seg" }, names.map((name, i) => textButton("fin" + i, name, () => actions.setFinish(i), finish === i, "cdlts-step")))),
        controlsRow("back", t("UI.BlackBack", "The boards' backs"), h("div", { className: "cdlts-seg" },
            textButton("back-off", t("UI.BackAsSteel", "As the steel"), () => actions.setBlackBack(false), !editor.blackBack, "cdlts-step"),
            textButton("back-on", t("UI.BackBlack", "Black"), () => actions.setBlackBack(true), !!editor.blackBack, "cdlts-step"))),
        rootable ? controlsRow("root", t("UI.RootPaint", "The posts' root"), h("div", { className: "cdlts-seg" },
            [t("UI.RootNone", "Unpainted"), t("UI.RootMarking", "Yellow and black"), t("UI.RootBlue", "Blue and white")]
                .map((name, i) => textButton("root" + i, name, () => actions.setRootPaint(i), (Number(editor.root) || 0) === i, "cdlts-step")))) : null
    ];
}

// How far the boards of an overhead support lean forward (0.12.55; JTG D82 2.3.5: 0° to 15°): one button for each step, the present one marked; while placing, the boards
// placed next.
function tiltRows(editor, t) {
    if (!editor.leans) {
        return null;
    }
    const tilt = Number(editor.tilt) || 0;
    return [controlsRow("tilt", t("UI.Tilt", "Lean forward"),
        h("div", { className: "cdlts-seg" }, [0, 5, 10, 15].map(degrees => textButton("tilt" + degrees, degrees + "°", () => actions.setTilt(degrees), tilt === degrees, "cdlts-step"))),
        t("UI.TiltTip", "The top leans toward the traffic, 0° to 15° (JTG D82 2.3.5)"))];
}

// The span of a gantry or the arm of a cantilever (0.12.78; the owner: where the road is read wrong the player sets it by hand): the box and the slider set it, Fit the road gives the planner's back
// (a support that stands: the road's; a double cantilever that stands has no road length to fit). A hung support has the height of its boards' lower edge (clearance, 0.12.78), and the one button
// gives both back (a double cantilever that stands: the clearance only). The button is selected while neither is set by hand.
function SpanRows({ editor }) {
    const t = useText();
    const label = editor.reachKind === "span" ? t("UI.Span", "Span (m)") : editor.reachKind === "arms" ? t("UI.ArmEach", "Each arm (m)") : t("UI.Arm", "Arm (m)");
    const tenths = value => Math.round(value * 10) / 10;
    const clearance = editor.clearMax > 0;
    const fit = () => (!clearance && editor.mode === "item" && editor.reachKind === "arms" ? [] : [textButton("r-fit", t("UI.FitRoad", "Fit the road"), () => actions.fitSpan(), !editor.reachHand && !editor.clearHand)]);
    return [
        h(RangeRows, {
            key: "span", prefix: "r", label, value: editor.reach || 0, min: editor.reachMin, max: editor.reachMax, step: editor.reachStep, round: tenths, onSet: actions.setSpan,
            head: shown => h(TextField, { key: editor.id + ":reach", field: { k: "reach", v: String(tenths(shown)), max: 5, cls: "cdlts-num" }, target: editor.id, onCommit: actions.setSpan }),
            extras: () => (clearance ? [] : fit())
        }),
        clearance ? h(RangeRows, {
            key: "clear", prefix: "c", label: t("UI.Clearance", "Clearance (m)"), value: editor.clear || 0, min: editor.clearMin, max: editor.clearMax, step: editor.clearStep, round: tenths, onSet: actions.setClearance,
            head: shown => h(TextField, { key: editor.id + ":clear", field: { k: "clear", v: String(tenths(shown)), max: 4, cls: "cdlts-num" }, target: editor.id, onCommit: actions.setClearance }),
            extras: fit
        }) : null
    ];
}

// The buttons that move a support that stands: along its road and toward or away from it; on a wall along the wall and up or down, and a bracket's board turned
// the other way along the wall.
function supportMoveRows(editor, t) {
    const steps = (key, codes) => h("div", { className: "cdlts-seg" }, codes.map(([label, code]) => textButton(key + code, label, () => actions.moveSupport(code), false, "cdlts-step")));
    const along = [["−5", -3], ["−0.5", -1], ["+0.5", 1], ["+5", 3]];
    const wall = editor.wall === 1 || editor.wall === 2;
    const across = wall ? [["−10", -6], ["−1", -4], ["−0.1", -2], ["+0.1", 2], ["+1", 4], ["+10", 6]] : [["−1", -4], ["−0.1", -2], ["+0.1", 2], ["+1", 4]];
    const tenths = value => Math.round(value * 10) / 10;
    const box = (name, shown, onCommit) => h(TextField, { key: editor.id + ":" + name, field: { k: name, v: String(tenths(shown)), max: 7, cls: "cdlts-num" }, target: editor.id, onCommit });
    const number = value => typeof value === "number" && Number.isFinite(value);
    const alongLabel = wall ? t("UI.MoveAlongWall", "Along the wall (m, + to the right facing it)") : t("UI.MoveAlong", "Along the road (m, + toward the traffic)");
    return section("smove", [
        // Along is one number (m moved from where the support stood when it was picked): the box, the slider and the step buttons all set it, as the turn's do; across and the lower edge are absolute, with rows of their own.
        number(editor.along) ? h(RangeRows, {
            key: "sm-al", prefix: "al", label: alongLabel, value: editor.along, min: alongRange.min, max: alongRange.max, step: alongRange.step, round: tenths, onSet: actions.setAlong,
            head: shown => box("along", shown, actions.setAlong), extras: () => along.map(([label, code]) => textButton("a" + code, label, () => actions.moveSupport(code), false, "cdlts-step"))
        }) : controlsRow("sm-along", alongLabel, steps("a", along)),
        controlsRow("sm-across", wall ? t("UI.MoveHeight", "Up or down (m, + up)") : t("UI.MoveAcross", "Across (m, + away from the road)"), steps("c", across)),
        wall && number(editor.lower) ? h(RangeRows, {
            key: "sm-lo", prefix: "lo", label: t("UI.LowerEdge", "Lower edge height (m)"), value: editor.lower,
            min: lowerRange.min, max: lowerRange.max, step: lowerRange.step, round: tenths, onSet: actions.setLower, head: shown => box("lower", shown, actions.setLower)
        }) : null,
        !wall && typeof editor.kerb === "number" ? h(KerbRow, { key: "sm-kerb", value: editor.kerb, onSet: actions.setKerb, label: t("UI.Kerb", "Distance from the kerb"), box: shown => box("kerb", shown, actions.setKerbTyped) }) : null,
        !wall && number(editor.centre) && number(editor.centreMax) ? h(RangeRows, {
            key: "sm-ce", prefix: "ce", label: t("UI.FromCentre", "From the centre line (m)"), value: editor.centre,
            min: 0, max: editor.centreMax, step: 0.1, round: tenths, onSet: actions.setCentre, head: shown => box("centre", shown, actions.setCentre)
        }) : null,
        editor.wall === 2 ? wideButton("sm-turn", t("UI.TurnOnWall", "Face the other way along the wall"), icons.again, () => actions.moveSupport(5)) : null
    ], "cdlts-support-move");
}

// A sign of the player's own (0.12.29): save what the editor shows as a new sign; for a saved one being placed, keep the changes in it or delete it (pressed twice). What was said
// last under them.
function customRows(editor, custom, t) {
    const own = editor.custom;
    if (!own || typeof own !== "object") {
        return null;
    }
    const key = own.key || "";
    const confirming = key.length > 0 && ui.confirmingCustom === key;
    return section("custom", [
        row("cu-head", t("UI.CustomTitle", "Custom sign"), null, { uppercase: true }),
        wideButton("cu-save", t("UI.CustomSave", "Save as a new sign"), icons.add, () => actions.saveCustom()),
        key.length > 0 ? wideButton("cu-update", t("UI.CustomUpdate", "Keep the changes in this sign"), icons.edit, () => actions.updateCustom(key)) : null,
        key.length > 0 ? wideButton("cu-delete", confirming ? t("UI.CustomDeleteAgain", "Press again: delete this sign") : t("UI.CustomDelete", "Delete this sign"), icons.trash, () => {
            if (confirming) {
                setUi({ confirmingCustom: "" });
                actions.deleteCustom(key);
            } else {
                setUi({ confirmingCustom: key });
            }
        }, confirming) : null,
        custom && custom.note ? note("cu-note", custom.note, custom.bad ? "bad" : "") : null
    ], "cdlts-custom");
}

// The tabs of an edited sign: its own settings, and its support with the boards on it (a mark when there is a warning there). The game's tab bar, else buttons.
function editorTabs(active, hasWarn, t, draft) {
    const tabs = [["sign", t("UI.TabSign", "Sign")], ["support", (draft ? t("UI.TabSupportDraft", "Support") : t("UI.TabSupport", "Support and boards")) + (hasWarn ? " ⚠" : "")]];
    if (TabBar && Tab) {
        return h(TabBar, { key: "tabs", className: "cdlts-tabs" }, tabs.map(([id, label]) => h(Tab, { key: id, id, selectedId: active, onSelect: () => setUi({ editorTab: id }) }, label)));
    }
    return h("div", { key: "tabs", className: "cdlts-tabs-flat" }, segments("tabs-s", tabs.map(([v, l]) => ({ v, l })), active, id => setUi({ editorTab: id })));
}

function EditorPanel() {
    const t = useText();
    const lang = api.useValue(language);
    const state = useUi();
    const entries = catalogOf(api.useValue(catalog));
    const chosenId = api.useValue(selected);
    const editor = editorOf(api.useValue(editorBinding));
    const custom = customOf(api.useValue(customBinding));
    const supportList = supportsOf(api.useValue(supports));
    const chosenSupport = api.useValue(support);
    const target = editor ? editor.id : null;
    React.useEffect(() => () => cancelPendingEdits(target), [target]);
    const takesPicture = !!(editor && editor.pic);
    React.useEffect(() => {
        if (takesPicture) {
            actions.listCustomFiles();
        }
    }, [target, takesPicture]);
    if (!editor) {
        return null;
    }

    const chosen = entries.find(entry => entry.id === chosenId) || null;
    const isItem = editor.mode === "item";
    // A road marking is paint: it has its settings and its picture, no support and no boards, so no second page.
    const isMarking = editor.marking === true;
    const hasBoards = isItem && !isMarking && Array.isArray(editor.boards) && editor.boards.length > 0;
    const hasWarn = Array.isArray(editor.warn) && editor.warn.length > 0;
    // A sign has two pages, the one about to be placed as an edited one (0.12.58: the support's finish, lean and root paint are set before it is placed): its own settings, and its support (with the
    // boards on it, once it is placed). A marking has the one.
    const tab = !isMarking && state.editorTab === "support" ? "support" : "sign";
    const size = previewSize(editor.pw, editor.ph, 400, 200);

    const signPage = [
        junctionRows(editor, t),
        facadeRows(editor, t),
        autoRows(editor, t),
        customRows(editor, custom, t),
        pictureRows(editor, custom, t),
        editor.preview ? h("div", { key: "preview", className: "cdlts-preview" }, h("img", { className: "cdlts-preview-img", src: editor.preview, style: { width: size.w + "rem", height: size.h + "rem" } })) : null,
        Array.isArray(editor.info) && editor.info.length > 0 ? section("info", editor.info.map((line, index) => note("i" + index, line))) : null,
        // A setting that starts a section has its heading above it (0.12.47).
        section("fields", editor.fields.length === 0 ? note("none", t("UI.NoSettings", "This sign has no settings."))
            : editor.fields.map(field => [field.sec ? row("sec-" + field.k, field.sec, null, { uppercase: true }) : null, fieldRows(editor, field, t)])),
        // The game's markings the areas of this entry hide, and the way to bring them back.
        !isItem && chosen && chosen.id === "mk.clear" ? h(ClearBlock, { key: "clear" }) : null
    ];
    const lanes = hasBoards ? laneRows(editor, t) : null;
    const reach = editor.reachKind ? h(SpanRows, { key: "span", editor }) : null;
    const tilt = tiltRows(editor, t);
    const finishes = finishRows(editor, t);
    // A sign about to be placed has its support chosen here (the planner's, or a form and a variant); a marking or a barrier has none.
    const noSupport = isMarking || !!(chosen && (chosen.grp === "marking" || chosen.grp === "barrier"));
    const planned = editor.plan ? supportList.find(entry => entry.id === editor.plan) : null;
    const supportPage = [
        !isItem && !noSupport ? h(SupportBar, { key: "supports", list: supportList, chosen: chosenSupport, planned }) : null,
        Array.isArray(editor.supportInfo) && editor.supportInfo.length > 0 ? section("sinfo", editor.supportInfo.map((line, index) => note("si" + index, line))) : null,
        hasWarn ? section("warns", editor.warn.map((line, index) => note("w" + index, line, "warn"))) : null,
        hasBoards ? boardRows(editor, chosen, t, lang) : null,
        hasBoards ? section("move", moveRow(editor, t)) : null,
        (hasBoards && (editor.turns || editor.faces)) || (!isItem && editor.turns) ? section("turn", turnRows(editor, t)) : null,
        lanes,
        reach ? section("reach", reach) : null,
        tilt ? section("tilt", tilt) : null,
        finishes ? section("finish", finishes) : null,
        isItem && !editor.onArm ? supportMoveRows(editor, t) : null,
        !isItem && !hasBoards && !reach && !lanes && !tilt && !finishes && noSupport ? section("nosupport", note("ns", t("UI.NoSupportSettings", "This support has nothing to set."))) : null
    ];

    return h(React.Fragment, null,
        !isMarking ? editorTabs(tab, hasWarn, t, !isItem) : null,
        h(gameUi.Scrollable, { key: "body:" + editor.id + ":" + tab, vertical: true, className: "cdlts-scroll", style: { maxHeight: editorBodyHeight() } },
            tab === "support" ? supportPage : signPage));
}

// ------------------------------------------------------------------ the way in

// Wraps the game's AssetMenu. Any failure falls back to the unwrapped menu so that this mod can never break the toolbar. Choosing our tab opens the panel.
const wrapAssetMenu = Original => props => {
    let mine = false;
    try {
        mine = sameEntity(api.useValue(selectedCategory), api.useValue(category));
    } catch (error) {
        mine = false;
    }

    React.useEffect(() => {
        if (mine && !ui.open) {
            setUi({ open: true, generation: ui.generation + 1 });
        }
    }, [mine]);

    return h(Original, props);
};

// The panels, appended to the game's root: shown while they are open, whatever the toolbar does.
function GameWindow() {
    const state = useUi();
    if (!panelVisible(state.open)) {
        return null;
    }

    return h(React.Fragment, null,
        h(Guard, { name: "dock", key: "w" + state.generation }, h(Dock, null)),
        h(Guard, { name: "dialog", key: "d" + state.generation }, h(RestoreDialog, null)),
        h(Guard, { name: "keys", key: "k" + state.generation }, h(KeysPanel, null)));
}

// The classes the game's own right-hand buttons use: from the game's modules, else from the registry the extension was given; null when neither has them (then
// the button is a plain floating one).
let moduleRegistry = null;

function gameClasses(path) {
    const found = gameModule(path, "classes");
    if (found) {
        return found;
    }
    try {
        const entry = moduleRegistry && moduleRegistry.registry && moduleRegistry.registry.get(path);
        return entry && entry.classes ? entry.classes : null;
    } catch (error) {
        return null;
    }
}

let lastToggle = 0;

// The button in the right-hand column. It toggles the panels; a press that comes twice at once (the game sends select twice for one click on some buttons) counts once.
function TsButton() {
    const state = useUi();
    const t = useText();
    const title = t("UI.Title", "CDL:StreetScape");
    const toggleOpen = () => {
        const now = Date.now();
        if (now - lastToggle < 180) {
            return;
        }

        lastToggle = now;
        if (ui.open) {
            actions.leave();
            setUi({ open: false, flyout: false });
        } else {
            setUi({ open: true, generation: ui.generation + 1 });
        }
    };

    const theme = gameClasses(modules.rightMenuButton);
    const rightMenu = gameClasses(modules.rightMenu);
    const tooltip = h("div", null,
        h("div", { className: "cdlts-tip-title" }, title),
        h("div", null, t("UI.ButtonTooltip", "Place and edit road signs, markings and street furniture")));
    const button = h(gameUi.Button, theme
        ? { src: icons.button, selected: !!state.open, theme, className: theme.toggleStates, onSelect: toggleOpen, title, disableHint: true }
        : { src: icons.button, selected: !!state.open, variant: "floating", onSelect: toggleOpen, title });
    const tipped = h(gameUi.Tooltip, { tooltip, direction: "left", alignment: "center" }, button);
    return rightMenu && rightMenu.item ? h("div", { className: rightMenu.item }, tipped) : tipped;
}

function extension(registry) {
    moduleRegistry = registry;
    registry.extend("game-ui/game/components/asset-menu/asset-menu.tsx", "AssetMenu", wrapAssetMenu);
    registry.append("GameBottomRight", TsButton);
    registry.append("Game", GameWindow);
}

// Everything but the extension is exported for the offline tests only; hasCSS is added to the bundle by tools/css-presence.js.
export {
    extension as default, normalize, matches, search, filterEntries, filteredOf, groupsOf, kindIcon, paginate, parseCatalog, parseEditor, parseHint, memoParse, sameEntity,
    keyParts, capsWidth, panelVisible, actions, blurActive, MainPanel, EditorPanel, RestoreDialog, KeysPanel, GameWindow, TsButton, Guard, reportError, ui, setUi, columns, perPage, status, previewSize, commitNow, parseCustom, addId,
    commitSoon, textWidth, choiceLayout, stepFrom, FootprintPanel, bytesText, countText, modules, icons, Dock, TabStrip, ActionPill, SettingsPanel, tabsOf, clampTop, pickTab, scrollStrip, stripEntered, panelLeft, back, onEscape, stripSlots, settingsTab
};
