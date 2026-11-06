// Offline artwork review; the button backgrounds below are samples, not a game capture.
import { readFileSync, writeFileSync, mkdirSync, existsSync } from "node:fs";
import { resolve, join } from "node:path";

const root = resolve(process.argv[2] || ".");
const out = resolve(process.argv[3] || join(root, "out/tab-icons-0.12.60"));
const names = ["streetscape", "all", "prohibition", "warning", "mandatory", "guide", "auxiliary", "notice", "tourist", "placename", "wayfinding", "variable", "brt", "toll", "cabinet", "lampad", "marking", "barrier", "green", "custom"];
const labels = JSON.parse(readFileSync(join(root, "lang/zh-HANS.json"), "utf8"));
const uri = path => "data:image/png;base64," + readFileSync(path).toString("base64");
const label = name => name === "streetscape" ? "街景设施 · 入口" : labels["CDLC.TS.UI.Group." + name];
const image = (src, x, y, size) => `<image href="${src}" x="${x}" y="${y}" width="${size}" height="${size}"/>`;
const text = (s, x, y, size = 14, fill = "#dce8ef") => `<text x="${x}" y="${y}" font-family="Microsoft YaHei, sans-serif" font-size="${size}" fill="${fill}">${s}</text>`;
const rect = (x, y, w, h, fill, rx = 0) => `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${rx}" fill="${fill}"/>`;
const sources = Object.fromEntries(names.map(name => [name, uri(join(root, "Icons", name + ".png"))]));
let sheet = rect(0, 0, 1200, 1060, "#172630") + text("CDL : StreetScape", 32, 43, 27) + text("0.12.60  /  标签页图标重设计", 32, 74, 17, "#9fb8c8")
    + text("离线矢量预览 · 64 px 原图 / 32 px 默认 / 32 px 选中 / 24 px 浅底", 32, 104, 14, "#9fb8c8");
names.forEach((name, i) => {
    const x = 24 + (i % 5) * 232, y = 126 + Math.floor(i / 5) * 181;
    sheet += rect(x, y, 220, 169, "#253641", 9) + image(sources[name], x + 78, y + 8, 64)
        + text(label(name), x + 15, y + 94, 15) + text(name, x + 15, y + 113, 11, "#9fb8c8")
        + rect(x + 14, y + 123, 36, 36, "#344b59", 5) + image(sources[name], x + 16, y + 125, 32)
        + rect(x + 62, y + 123, 36, 36, "#2186a9", 5) + image(sources[name], x + 64, y + 125, 32)
        + rect(x + 110, y + 123, 36, 36, "#e1e8e9", 5) + image(sources[name], x + 116, y + 129, 24);
});
sheet += text("原版对照 · 本机游戏资源 · 不随模组分发", 32, 886, 16);
const game = process.env.CSII_INSTALLATIONPATH;
const vanilla = ["AllWayStop", "TrafficLights", "RoadsServices", "SmallRoads", "Trees", "Bus", "Electricity", "ParksAndRecreation"];
if (game) vanilla.forEach((name, i) => {
    const path = join(game, "Cities2_Data/Content/Game/UI/Media/Game/Icons", name + ".svg");
    if (!existsSync(path)) return;
    const x = 32 + i * 144;
    sheet += image(uri(path), x + 28, 905, 64) + text(name, x, 991, 11, "#9fb8c8");
});
sheet += text("透明背景 · 色块明暗 · 简化物体透视 · 原版按钮负责悬停与选中状态", 32, 1033, 14, "#9fb8c8");
mkdirSync(out, { recursive: true });
writeFileSync(join(out, "review.svg"), `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="1060" viewBox="0 0 1200 1060">${sheet}</svg>`);
const cards = names.map(name => `<button title="${label(name)}" data-name="${name}"><img src="${sources[name]}" alt="${label(name)}"></button>`).join("");
writeFileSync(join(out, "index.html"), `<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>CDLC.TS 0.12.60 标签图标</title>
<style>body{margin:0;padding:28px;background:#172630;color:#dce8ef;font:15px system-ui,sans-serif}main{max-width:1200px;margin:auto}h1{font-size:25px}p{color:#9fb8c8}nav{display:flex;flex-wrap:wrap;margin:20px 0;max-width:446px}button{margin:3px;padding:2px;width:38px;height:38px;border:0;border-radius:5px;background:#344b59;cursor:pointer}button:hover{background:#4c6878}button.selected{background:#2186a9}button img{width:32px;height:32px}.sheet{width:100%;height:auto}</style>
<main><h1>CDLC.TS 0.12.60 标签图标</h1><p>离线预览；可点击下方图标查看选中状态。按钮背景为审阅示意，非游戏截图。</p><nav>${cards}</nav><p id="chosen">街景设施 · 入口</p><img class="sheet" src="review.svg" alt="20 枚图标及原版风格对照"></main>
<script>document.querySelectorAll('button').forEach(b=>b.onclick=()=>{document.querySelectorAll('button').forEach(x=>x.classList.toggle('selected',x===b));document.getElementById('chosen').textContent=b.title});document.querySelector('button').click();</script></html>`);
console.log(out);
