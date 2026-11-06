// The icons of the dock that are no tab (0.12.62): place, edit, delete, pick (0.12.88), the settings tab's gear and the back arrow. The game's glyph files (Media/Glyphs) have no fill and are black in a plain
// picture, which the dark round buttons hide (the owner: "颜色重合了，看不清楚"), so these are coloured pictures of our own, flat as the tab icons are. Each is one large shape (the owner:
// "去掉左边的蓝色牌牌": the first drawings had a small blue signboard behind the green plus, the pencil and the bin).
//
// node IconSources/build.mjs <repo root> [names]   (names: only these drawings are drawn and packed, the others stay as they are)
// writes IconSources/<name>.svg (the drawings), draws each with the browser at 256 px on a transparent ground (Edge, headless) and packs it as the tab icons are packed: 128 px, a palette of 256
// colours with transparency (a Python with Pillow: set PYTHON; Desktop/vision-tools/venv has one), into Icons/<name>.png.
import { mkdirSync, writeFileSync, existsSync } from "node:fs";
import { join, resolve } from "node:path";
import { execFileSync } from "node:child_process";

const root = resolve(process.argv[2] || ".");
const work = join(root, "out", "action-icons");
mkdirSync(work, { recursive: true });

// Place: a green disc with a white plus, a darker rim below it.
const place = `<circle cx="64" cy="70" r="54" fill="#21803a"/><circle cx="64" cy="64" r="54" fill="#3fcf5f"/><path d="M64 14a50 50 0 0 1 35 14 50 50 0 0 0-70 70A50 50 0 0 1 64 14z" fill="#6fe58a" opacity=".55"/>
  <rect x="55" y="30" width="18" height="68" rx="5" fill="#ffffff"/><rect x="30" y="55" width="68" height="18" rx="5" fill="#ffffff"/>`;

// Edit: a pencil from the lower left to the upper right.
const edit = `<g transform="translate(64 64) rotate(42) scale(1.12) translate(0 -3)">
  <rect x="-13" y="-47" width="26" height="14" rx="4" fill="#f27d9a"/><rect x="-13" y="-34" width="26" height="6" fill="#aeb9bf"/>
  <rect x="-13" y="-28" width="26" height="55" fill="#ffb300"/><rect x="2" y="-28" width="11" height="55" fill="#e08a00"/>
  <polygon points="-13,27 13,27 0,53" fill="#f6dcae"/><polygon points="0,27 13,27 0,53" fill="#dfc08a" opacity=".6"/><polygon points="-6.5,40 6.5,40 0,53" fill="#3b4048"/>
</g>`;

// Delete: a red bin with its lid and handle.
const remove = `<g transform="translate(64 66) scale(1.2) translate(-89 -80)">
  <polygon points="58,60 120,60 112,122 66,122" fill="#b3342d"/><polygon points="58,60 120,60 118,72 60,72" fill="#8f241f"/>
  <polygon points="62,64 116,64 108,118 70,118" fill="#e8504a"/>
  <rect x="76" y="76" width="6" height="34" rx="3" fill="#ffd5d2"/><rect x="92" y="76" width="6" height="34" rx="3" fill="#ffd5d2"/>
  <rect x="52" y="48" width="74" height="12" rx="5" fill="#f4655e"/><rect x="52" y="55" width="74" height="5" rx="2" fill="#b3342d"/>
  <rect x="78" y="38" width="22" height="12" rx="4" fill="#f4655e"/><rect x="84" y="43" width="10" height="7" rx="2" fill="#7a1814"/>
</g>`;

// Pick: the eyedropper, a pipette from the upper right (its bulb) to the lower left (its tip) with a drop of colour.
const pick = `<g transform="translate(64 64) rotate(42) scale(0.9) translate(0 -12)">
  <rect x="-16" y="-62" width="32" height="40" rx="15" fill="#5b3fc4"/><rect x="-16" y="-62" width="22" height="40" rx="11" fill="#7a5cf0"/><rect x="-10" y="-54" width="5" height="16" rx="2.5" fill="#ffffff" opacity=".55"/>
  <rect x="-20" y="-24" width="40" height="13" rx="4" fill="#aeb9bf"/><rect x="2" y="-24" width="18" height="13" rx="4" fill="#8d9aa2"/>
  <rect x="-12" y="-12" width="24" height="52" fill="#d9f2fb"/><rect x="5" y="-12" width="7" height="52" fill="#b6dcea"/><rect x="-8" y="6" width="16" height="34" fill="#27a9e1"/><rect x="-9" y="-8" width="4" height="44" rx="2" fill="#ffffff" opacity=".75"/>
  <polygon points="-12,40 12,40 4,58 -4,58" fill="#d9f2fb"/><polygon points="3,40 12,40 4,58 0,58" fill="#b6dcea"/><polygon points="-8,40 8,40 3,56 -3,56" fill="#27a9e1"/>
  <path d="M0 62C-3 68-8 73-8 79a8 8 0 0 0 16 0C8 73 3 68 0 62z" fill="#27a9e1"/><ellipse cx="-2.5" cy="79" rx="2" ry="3.4" fill="#ffffff" opacity=".6"/>
</g>`;

const gear = (() => {
    const teeth = 8, rOuter = 56, rInner = 43, points = [];
    for (let i = 0; i < teeth; i++) {
        const a = (i / teeth) * Math.PI * 2;
        const w = (Math.PI * 2) / teeth;
        for (const [da, r] of [[-0.30, rInner], [-0.18, rOuter], [0.18, rOuter], [0.30, rInner]]) {
            points.push([64 + Math.cos(a + da * w * 2) * r, 64 + Math.sin(a + da * w * 2) * r]);
        }
    }
    const p = points.map(([x, y]) => x.toFixed(1) + "," + y.toFixed(1)).join(" ");
    return `<polygon points="${p}" fill="#6f7f89" transform="translate(0 6)"/><polygon points="${p}" fill="#d3dde2"/><circle cx="64" cy="64" r="22" fill="#6f7f89"/><circle cx="64" cy="64" r="16" fill="#2a3440"/>`;
})();

const back = `<polygon points="14,64 62,16 62,46 114,46 114,82 62,82 62,112" fill="#17505f" transform="translate(0 7)"/><polygon points="14,64 62,16 62,46 114,46 114,82 62,82 62,112" fill="#e9f3f6"/><polygon points="62,46 114,46 114,52 62,52" fill="#ffffff" opacity=".7"/>`;

const drawings = { place, edit, delete: remove, pick, settings: gear, back };
const only = process.argv.slice(3);
for (const name of only) if (!(name in drawings)) throw new Error("no drawing named " + name);

const edgeIn = base => (base ? join(base, "Microsoft", "Edge", "Application", "msedge.exe") : "");
const edge = process.env.EDGE || [edgeIn(process.env["ProgramFiles(x86)"]), edgeIn(process.env.ProgramFiles)].find(p => p && existsSync(p));
if (!edge) throw new Error("Microsoft Edge is not here (set EDGE): it draws the pictures");
const python = process.env.PYTHON || "python";

const toWindows = p => p.replace(/^\/([a-z])\//, (m, d) => d.toUpperCase() + ":/");
for (const [name, body] of Object.entries(drawings)) {
    if (only.length && !only.includes(name))
        continue;
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="0 0 128 128">${body}</svg>\n`;
    writeFileSync(join(root, "IconSources", name + ".svg"), svg);
    const page = join(work, name + ".html");
    writeFileSync(page, `<!doctype html><meta charset="utf-8"><body style="margin:0;background:transparent;overflow:hidden">${svg.replace('width="128" height="128"', 'width="256" height="256"')}</body>`);
    const big = join(work, name + "-256.png");
    execFileSync(edge, ["--headless", "--disable-gpu", "--hide-scrollbars", "--default-background-color=00000000", "--window-size=256,256", "--screenshot=" + toWindows(big), "file:///" + toWindows(page)], { stdio: "ignore" });
}

const pack = `
import glob, os, sys
from PIL import Image
work, out, only = sys.argv[1], sys.argv[2], sys.argv[3:]
for path in sorted(glob.glob(os.path.join(work, "*-256.png"))):
    name = os.path.basename(path)[:-8]
    if only and name not in only:
        continue
    image = Image.open(path).convert("RGBA")
    if image.getchannel("A").getextrema()[0] != 0:
        raise SystemExit(name + ": no transparent pixel, the ground was not transparent")
    small = image.resize((128, 128), Image.Resampling.LANCZOS)
    small.quantize(colors=256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE).save(os.path.join(out, name + ".png"), optimize=True)
    print(name, os.path.getsize(os.path.join(out, name + ".png")), "bytes")
`;
writeFileSync(join(work, "pack.py"), pack);
console.log(execFileSync(python, ["-I", toWindows(join(work, "pack.py")), toWindows(work), toWindows(join(root, "Icons")), ...only], { encoding: "utf8" }));
