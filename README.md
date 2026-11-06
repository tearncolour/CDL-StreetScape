<a id="zh"></a>

# CSL Driving Life : StreetScape China Pack（中国街景设施）

版本 0.12.108　·　[English](#english)

本模组用于放置和编辑中国道路设施：国标交通标志、路面标线、护栏、声屏障、绿化，以及灯杆广告。

在目录中选择牌子，指向路边并单击即可放置。支架（单立柱、悬臂或门架）按道路情况自动选择，牌面自动朝向来车方向。牌面上的文字、地名、路名和箭头均可修改，设置旁有预览。支持撤销和重做；按住 Ctrl 单击已放置的对象，可提取其样式。

标线和护栏沿道路绘制，依次单击起点和终点，弯道与路口自动处理。一个支架可以挂多块牌子。

说明：

- 本模组只提供外观设施，不改变限速、让行和车道连接。
- 牌面和标线依据国家标准（GB 5768、GB 17733）重新绘制，未使用标准中的原图；标准未规定的尺寸为估计值，与实物可能有差异。
- 放置的内容保存在存档旁的 ModsData/CDLC.TS/Saves，不写入游戏存档，未安装本模组时存档不受影响。卸载前，请先在面板中点击“恢复全部原版标线”，再保存存档。
- 使用 Move It 移动道路时，沿路的牌子、标线和护栏随之移动。
- 设置页显示本模组的显存占用，并按显卡给出显示距离的建议。
- 界面按游戏语言显示中文或英文。

## 里面有什么

- **交通标志**：GB 5768.2 的 273 种标志、GB 17733 的地名标志、BRT 和收费站的牌面、LED 可变信息屏。支架有单立柱、双立柱、悬臂（单臂、双臂、F 形）和门架（还有空间桁架门架、限高门架），也能装在路灯杆、信号灯杆和墙上；一个支架上可以放好几块牌子。
- **文字灯箱和立式灯箱**：自己写字的发光字，字体和发光颜色可以选，夜里会亮。
- **路面标线**：GB 5768.3 的标线。单击放符号，沿路画曲线，还有多边形的岛、导流线、待行区、路口引导；清除或恢复原版标线；停车位和公交站会跟着弯道走。
- **护栏和声屏障**：波形梁、混凝土护栏、城市隔离栏、仿古栏杆、水马；金属、水泥、砖墙和高架封闭式声屏障；九种施工围挡，还有垂直绿化墙。
- **路侧立柱**：柱式轮廓标、公路里程碑和百米桩、公路界碑、道口标柱。
- **绿化**：树池、分车绿带、高架花箱、花钵，植物全是游戏自带的。
- **灯杆广告**：道旗、灯箱、中国结、LED 造型、灯笼、霓虹造型灯，都可以换成你自己的 PNG 图片。
- **自己的路牌和路缘石**：导入图片做自己的牌子；三种国标花岗岩路缘，当作道路升级来用。

## 怎么用

1. 点屏幕右边圆按钮那一列里本模组的按钮，或者打开道路菜单的最后一个标签页。
2. 在目录里搜索或者按类别找，选好牌子和支架（支架可以选“自动”，让模组帮你挑），指到路边，**左键放置**。标线和护栏是先点起点、再点终点；右键或者 Esc 退出。
3. 工具旁边有个小窗口，会列出现在能用的按键。常用的有：`[` `]` 转动（Shift 每次 5°，Alt 每次 1°），`;` `'` 换支架，`<` `>` 调离路缘的距离，`/` 把立柱放到路中间，Ctrl+Z / Ctrl+Y 撤销和重做。按住 Alt 再点，可以另立一个新支架；按住 Ctrl 点已经放好的东西，或者用工具条上的吸管，可以把它的样式吸过来（Shift 一起吸整根杆）。
4. “编辑”可以改牌子和支架的设置：支架的位置、悬臂长度、净空、转动角度，都有滑块和输入框。“删除”可以选中我们的牌子、立柱、底座或者悬臂。

## 用自己的图片

- **路牌**：把 PNG 图片放进游戏用户数据文件夹里的 `ModsData/CDLC.TS/Custom`，打开“自定义”标签页，点那个虚线的 + 方块导入。在编辑器里给它起名字、选形状和大小、定图片怎么铺满牌面，满意了点“另存为新路牌”。图片的每个像素只分不透明和透明（alpha 250 以上算不透明），可以放大缩小（10% 到 400%）、挪位置，也可以让牌面沿着图片不透明的部分裁出形状。
- **灯杆广告**：道旗、灯箱和画框也能用 `Custom` 里的 PNG（编辑器里的“图片 (PNG)”那几行，点“使用”）。

## 换字体

牌面和路面上的字，用的是模组自带的交通标志专用字体。想换成别的：把字体放进 `ModsData/CDLC.TS/Fonts`（模组会自己建好这个文件夹，里面有个 README.txt），中文用 `cjk.ttf`（或 `中文.ttf`），字母和数字用 `latin.ttf`（或 `西文.ttf`），`.otf` 也行，重启游戏就生效。

## 存档、卸载和设置

- 你放的东西存在存档旁边的 `ModsData/CDLC.TS/Saves`，不在游戏存档里面。没装模组的时候，存档就是一座普通的城市；重新装上，东西会回来。如果某条路后来被改过、找不到原来的位置，那些东西会列在面板的“存档与显示设置”里，你可以把它们放回去或者删掉。
- **卸载之前**，请先在面板里点“恢复全部原版标线”，然后存档。卸载模组也会带走你保存的自定义路牌。
- 想改设置，打开 `ModsData/CDLC.TS/preferences.json`（启动时读取，改完要重启游戏），在 `features` 里有两项：`roadFollow`（默认 `on`，路被 Move It 或游戏改动时，路边的东西跟着走；写 `off` 就关掉）和 `gameShader`（默认 `on`，用游戏自己的着色器画，这样建筑地块的贴花不会盖到牌子上；写 `off` 换回原来的材质）。

## 和别的模组

- 不需要别的模组也能用。
- 可以另外装 Realistic Information Extension（一个独立的模组），装了之后 LED 可变信息屏会跟着路况、事故、限速和车道状态变；没装的话，屏幕显示你手动设的内容。
- 放牌子和画标线只管好看，不会影响车怎么开：限速、让行、车道连接都不会因为你放了牌子而改变。“清除原版标线”只是把原版的线藏起来（路面上的车道箭头会被删掉，恢复时重新生成），不会动道路本身。
- 如果你在 Paradox Mods 上订阅了这个模组，就别再把它拷一份放进本地的 Mods 文件夹，不然会被加载两遍。

## 许可

本模组的源码以 GNU Affero 通用公共许可证第 3 版（AGPL-3.0）发布，全文见 [LICENSE](LICENSE)。字体和国家标准不在这个许可的范围内，见 `NOTICE.md`。

## 反馈和致谢

问题反馈请到 [GitHub Issues](https://github.com/tearncolour/CDL-StreetScape/issues) 提交，并尽量附上 `Logs/CDLC.TS.Mod.log`。源码以 AGPL-3.0 许可发布在 [tearncolour/CDL-StreetScape](https://github.com/tearncolour/CDL-StreetScape)。字体、许可和致谢见 `NOTICE.md`。

---

<a id="english"></a>

# CSL Driving Life : StreetScape China Pack

Version 0.12.108　·　[中文](#zh)

This mod places and edits Chinese road furniture: national-standard traffic signs, road markings, barriers, noise barriers, greening, and lamp-post advertising.

Select a sign in the catalogue, point at the roadside and click to place it. The support (single post, cantilever or gantry) is chosen to suit the road, and the board faces the oncoming traffic. The text, place names, road names and arrows on a board can be edited, with a preview beside the settings. Undo and redo are supported; Ctrl+click on a placed object picks up its style.

Markings and barriers are drawn along the road by clicking the start and then the end; bends and junctions are handled automatically. One support can carry several boards.

Notes:

- The mod provides decoration only; it does not change speed limits, priorities or lane connections.
- Signs and markings are redrawn from the Chinese national standards (GB 5768, GB 17733), not copied from their figures. Sizes that the standards do not specify are estimates, and may differ from the real objects.
- Placed items are stored beside the save (ModsData/CDLC.TS/Saves), not in it, so a save is unaffected without the mod. Before uninstalling, press "Restore all of the game's markings" in the panel and save the game.
- When a road is moved with Move It, the signs, markings and barriers beside it move with it.
- The settings page shows the graphics memory used by the mod and suggests a draw distance for the graphics card.
- The panel shows Chinese or English according to the game's language.

## What is in it

- **Signs:** the 273 signs of GB 5768.2, the place-name signs of GB 17733, BRT and toll-plaza boards, LED message screens. Supports: single and double posts, cantilevers (one arm, two arms, F-type) and gantries (including a space truss gantry and a height-limit frame); you can also fix a board to a lamp post, a signal pole or a wall, and put several boards on one support.
- **Text light boxes and standing light boxes:** glowing letters with your own text, a choice of font and glow colour; they light up at night.
- **Markings:** the markings of GB 5768.3. Click to place a symbol, draw curved runs, polygon islands, gore markings, waiting areas and junction guides; clear or restore the game's markings; parking bays and bus stops follow bends.
- **Barriers and noise barriers:** W-beam, concrete, city and classical railings, water barriers; metal, cement, brick and enclosed viaduct noise barriers; nine kinds of construction hoarding and a vertical greening wall.
- **Roadside posts:** post delineators, kilometre stones and 100 m posts, right-of-way boundary stones, junction posts.
- **Greening:** tree pits, green belts, viaduct planters and flower bowls; every plant is one of the game's own.
- **Lamp-post ads:** banners, light boxes, Chinese knots, LED figures, lanterns and neon figures; any of them can use your own PNG picture.
- **Your own signs and kerbs:** import a picture to make a sign; three granite kerbs from the standard, used as road upgrades.

## How to use it

1. Press the mod's button in the column of round buttons on the right of the screen, or open the last tab of the Roads menu.
2. Search or filter the catalogue, choose a sign and a support (the support can be "automatic" and the mod picks one), point at the roadside and **left-click to place**. For markings and barriers, click the start and then the end; right-click or Esc leaves.
3. A small panel by the tool lists the keys that work right now. The usual ones: `[` `]` turn (Shift 5° a step, Alt 1°), `;` `'` change the support, `<` `>` the distance from the kerb, `/` puts a post in the middle of the road, Ctrl+Z / Ctrl+Y undo and redo. Hold Alt and click for a support of its own; hold Ctrl and click a placed item, or use the eyedropper in the tool strip, to pick up its style (add Shift to take the whole pole).
4. Edit changes the settings of a sign and its support: a support's position, an arm's length, the clearance and the turning angle all have sliders and boxes. Delete picks one of our signs, posts, feet or arms.

## Using your own pictures

- **Signs:** put PNG pictures in `ModsData/CDLC.TS/Custom` in the game's user data folder, open the Custom tab and press the dashed + tile to import one. In the editor give it a name, a shape and a size, choose how the picture fills the board, and press **Save as a new sign**. Each pixel of a picture is either opaque or clear (alpha 250 or more counts as opaque); you can scale it (10 to 400 %), move it, or cut the board to the opaque part of the picture.
- **Lamp-post ads:** banners, light boxes and framed boards can take a PNG from `Custom` too (the "Picture (PNG)" rows in the editor, then **Use**).

## Changing the font

Signs and road words use the traffic sign font that comes with the mod. To use your own, put fonts in `ModsData/CDLC.TS/Fonts` (the mod creates the folder, with a README.txt in it): `cjk.ttf` (or `中文.ttf`) for Chinese characters, `latin.ttf` (or `西文.ttf`) for letters and figures; `.otf` works too. Restart the game and it takes effect.

## Saves, uninstalling and settings

- Your items are kept beside the save, in `ModsData/CDLC.TS/Saves`, not inside it. Without the mod a save is an ordinary city; with the mod installed again, the items come back. If a road was changed later and an item's place can no longer be found, it is listed in the panel under "Save data and display", where you can put it back or delete it.
- **Before you uninstall**, press **Restore all of the game's markings** in the panel and save the city. Removing the mod also removes the custom signs you saved.
- To change settings, open `ModsData/CDLC.TS/preferences.json` (read at start, so restart after editing). Under `features` there are two you may want: `roadFollow` (default `on`: items beside a road follow it when Move It or the game changes the road; write `off` to stop that) and `gameShader` (default `on`: drawn with the game's own shader, so a building lot's decal does not paint over the signs; write `off` to go back to the old material).

## With other mods

- No other mod is needed.
- You can add the Realistic Information Extension (a separate mod): the LED message screens then follow traffic, accidents, speed limits and lane state; without it they show what you set by hand.
- Placing a sign or a marking is only about looks; it does not change how cars drive: speed limits, priorities and lane connections stay as they were. "Clear the game's markings" only hides the game's lines (road arrows on the surface are removed and made again when you restore), and does not touch the road itself.
- If you subscribed to the mod on Paradox Mods, do not also copy it into the local Mods folder, or it will load twice.

## Licence

The source code of this mod is published under the GNU Affero General Public License, version 3 (AGPL-3.0); the full text is in [LICENSE](LICENSE). The fonts and the national standards are not covered by it; see `NOTICE.md`.

## Feedback and credits

Please report problems as an issue on [GitHub](https://github.com/tearncolour/CDL-StreetScape/issues), with `Logs/CDLC.TS.Mod.log` attached if you can. The source code is published under AGPL-3.0 at [tearncolour/CDL-StreetScape](https://github.com/tearncolour/CDL-StreetScape). The font, the licence and the credits are in `NOTICE.md`.
