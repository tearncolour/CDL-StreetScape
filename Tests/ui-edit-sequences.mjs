// Event-sequence regressions for the editor's real text boxes and debounce timers. No game or UI packages needed.
// Usage: node Tests/ui-edit-sequences.mjs <repo root>
import assert from "node:assert/strict";
import { resolve, join } from "node:path";
import { uiTarget } from "./ui-target.mjs";

const root = resolve(process.argv[2] || process.cwd());
const target = uiTarget(root);
const events = [];
const values = { Catalog: "[]", Selected: "", Editor: "" };
let hooks = null;
const react = {
    Component: class {},
    createElement: (type, props, ...children) => ({ type, props: { ...props, children } }),
    useState: initial => {
        const i = hooks.cursor++;
        if (!(i in hooks.slots)) hooks.slots[i] = initial;
        const instance = hooks;
        return [instance.slots[i], value => { instance.slots[i] = typeof value === "function" ? value(instance.slots[i]) : value; }];
    },
    useRef: initial => {
        const i = hooks.cursor++;
        return hooks.slots[i] || (hooks.slots[i] = { current: initial });
    },
    useEffect: (effect, dependencies) => {
        const i = hooks.cursor++;
        const previous = hooks.slots[i];
        if (!previous || !dependencies || dependencies.some((value, k) => !Object.is(value, previous.dependencies[k]))) {
            hooks.effects.push(() => {
                previous?.cleanup?.();
                hooks.slots[i] = { dependencies, cleanup: effect() };
            });
        }
    }
};

globalThis.window = {
    React: react,
    "cs2/api": {
        bindValue: (_group, name, fallback) => ({ name, fallback }),
        useValue: binding => values[binding.name] ?? binding.fallback,
        trigger: (_group, name, ...args) => events.push([name, ...args])
    },
    "cs2/ui": { Panel: "Panel", Scrollable: "Scrollable", PanelSection: "Section", PanelSectionRow: "Row" }
};
globalThis.document = { body: {}, activeElement: null };
const module = await import(target.url("?edit-sequences"));
const delay = () => new Promise(resolve => setTimeout(resolve, 390));
const event = value => ({ target: { value } });
let checks = 0;
function expect(expected, name) {
    assert.deepEqual(events.splice(0), expected, name);
    checks++;
}

function mount(component, props) {
    const state = { cursor: 0, slots: [], effects: [] };
    return {
        render(next = props) {
            state.cursor = 0;
            state.effects = [];
            hooks = state;
            const node = component(next);
            for (const effect of state.effects) effect();
            hooks = null;
            return node;
        },
        unmount() {
            for (const slot of state.slots) slot?.cleanup?.();
        }
    };
}

function findField(node) {
    if (!node || typeof node !== "object") return null;
    if (node.type?.name === "TextField") return node;
    for (const value of Object.values(node)) {
        if (typeof value === "object") {
            const found = findField(value);
            if (found) return found;
        }
    }
    return null;
}

const panel = mount(module.EditorPanel);
function editor(target, mode = "item") {
    values.Editor = JSON.stringify({ id: target, mode, code: "路42", title: "Test", fields: [{ k: "code", v: "G1", max: 32 }] });
    return panel.render();
}
function textField(target) {
    const node = findField(editor(target));
    assert.ok(node, "the editor renders its real text field");
    const instance = mount(node.type, node.props);
    return { instance, props: instance.render().props };
}

let field = textField(1);
field.props.onChange(event("G2"));
field.props.onChange(event("G3=东口"));
await delay();
expect([["EditField", "1|code=G3=东口"]], "normal typing debounces once and preserves equals signs and Chinese");
field.props.onChange(event("G4"));
field.props.onBlur(event("G4"));
await delay();
expect([["EditField", "1|code=G4"]], "blur commits immediately and cancels the pending duplicate");

field.props.onChange(event("before composition"));
field.props.onCompositionStart();
field.props.onChange({ ...event("组合中"), nativeEvent: { isComposing: true } });
await delay();
expect([], "starting composition cancels a timer created before the IME began");
field.props.onCompositionEnd(event("组合完成"));
await delay();
expect([["EditField", "1|code=组合完成"]], "composition end commits the completed text");

field.props.onChange(event("before composing change"));
field.props.onChange({ ...event("组合中"), isComposing: true });
await delay();
expect([], "a composing change also cancels an older timer when no compositionstart was delivered");

field.props.onChange(event("A must not reach B"));
module.actions.selectBoard(1);
await delay();
expect([["SelectBoard", 1]], "switching boards cancels pending edits even when a world field cannot be blurred");

field.props.onChange(event("A pending during incoming world selection"));
editor(2);
await delay();
expect([], "a backend EditorTarget change cancels the old target's timers");
field.instance.unmount();
field.props.onBlur(event("late unmounted blur"));
expect([], "a late blur from an unmounted field does not commit");

field = textField(3);
field.props.onChange(event("discard on tab unmount"));
field.instance.unmount();
await delay();
expect([], "unmounting a field cancels its timer even while the editor target is unchanged");

field = textField(4);
globalThis.document.activeElement = { blur: () => field.props.onBlur(event("A safe final value")) };
field.props.onChange(event("A safe final value"));
module.actions.selectBoard(0);
await delay();
expect([["EditField", "4|code=A safe final value"], ["SelectBoard", 0]], "a real blur safely commits to A before the board-switch message");
globalThis.document.activeElement = null;

field.props.onChange(event("outstanding edit after close"));
module.actions.leave();
await delay();
expect([["Leave"]], "leaving the tool cancels outstanding edits");
field.instance.unmount();

field = textField(5);
field.props.onChange(event("draft switch"));
editor(6, "draft");
await delay();
expect([], "switching from an item to a new draft cancels the old item's edit");
field.instance.unmount();

// A queued browser message can outlive the field before the new editor JSON arrives. It must still identify A, so the C# target guard can reject it.
module.commitSoon("code", "in-flight A", false, 7);
await delay();
expect([["EditField", "7|code=in-flight A"]], "in-flight messages retain the original target rather than using a newer selection");
module.actions.editField("code", "unaddressed");
expect([], "an edit without a target cannot bypass the protocol");
panel.unmount();
console.log(`${checks} editor sequence checks passed`);
