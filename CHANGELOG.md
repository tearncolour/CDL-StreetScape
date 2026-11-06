# 更新日志 · Changelog

每条先中文、后英文。
Each line is Chinese first, then English.

## 0.12.108

- 首个公开版本。功能与 0.12.107 相同，模组介绍和封面重新编写。 / First public release. The functions are those of 0.12.107; the introduction and the cover are rewritten.

## 0.12.107

- 设置页“显存与显示距离”重新排了版：正在用的那一档不再整行刷成亮青色，而是淡淡的底色加左边一小条标记；“建议”改成距离旁边的小标签；档位之间用更细的行距，表头和说明文字分出大小和深浅，进度条变细。数字和判断没有变。 / The "Memory and distance" section of the settings page is laid out again: the step in use is no longer flooded with bright cyan, it has a faint fill and a small marker at its left; "Suggested" is a small chip beside the distance; the steps are tighter, the title and the notes are told apart by size and shade, and the meter is thinner. The figures and the advice are the same.
- 收费站专用的东西（道闸、收费亭、收费岛、橡胶减速带）只在“收费站”标签里，不再混进“护栏”标签；绿化专用的（树池、绿化带、高架花槽、花钵）只在“绿化”标签里。锥桶、防撞桶、水马、警示柱这类别处也用得上的护栏仍然两边都有，带栏杆的花槽、花箱、草坪围栏和垂直绿化墙也仍在护栏里。放置和已放的东西不变。 / What belongs to a toll plaza alone (the lifting-arm gate, the booth, the island, the rubber hump) is only in the toll tab and no longer in the barrier tab; what belongs to the greening alone (the tree pit, the green belt, the viaduct planter, the flower bowl) is only in the greening tab. Cones, drums, water barriers and bollards, which are used elsewhere too, are still in both, and so are the planters with a railing or a box, the lawn fence and the living wall. Placing, and what is already placed, are unchanged.

## 0.12.106

- 中央隔离栏（城市道路隔离栏、京式、锌钢、港式）有一项原来叫“行人断口渐降”的设置，不少人没找到。现在它改名叫“路口前降至 0.7 m”，而且**新放的栏杆一开始就选“两端”**：沿路画栏杆时每一段（两个路口之间）的两端就是路口，栏杆会在进入每个路口前降下来。不想降就把它改成“无”。已经放下的栏杆不变。路口处栏杆断开的方式要用“接入侧断开”或“全部断开”，“连续通过”没有路口断口。 / The median railings (the city road fence, Beijing, zinc-steel and Hong Kong types) have a setting that was called "Lowered at a pedestrians' gap" and that many did not find. It is now called "Lowered to 0.7 m at a junction", and **a railing placed new starts with "At both ends"**: a barrier drawn along a road is one piece for each stretch between two junctions, so its two ends are the junctions, and the railing comes down on its way into each. Set it to None to leave it high. A railing that is already placed does not change. The railing has a gap at a junction only with the junction setting "Cut where a road joins" or "Cut at every junction"; "Go on through" has none.
- 降低是**快速降低**，不是缓缓的长坡：栏杆在路口前 25 米处开始降，用 15 米降到 0.70 米，再保持 0.70 米直到路口（最后 10 米），依据 GA/T 1567-2019 第 4.2.6 条（降低段末端高度不大于 0.7 米、末端长度不小于 10 米、渐变段不小于 15 米）。“低段长度”可选 10 到 70 米（新放的是 10 米；原来的默认 40 米没改，所以已放的栏杆不变），新增的“渐变长度”可选 5（很陡）、10、15（国标）、20、30 米。 / The drop is **quick, not a long slope**: the railing starts to come down 25 m before the junction, takes 15 m to reach 0.70 m, and stays at 0.70 m up to the junction (the last 10 m), after GA/T 1567-2019 clause 4.2.6 (end height not above 0.7 m, end length at least 10 m, transition at least 15 m). "Low section" can be 10 to 70 m (10 m for a new one; the old default of 40 m is not changed, so a railing already placed stays as it is), and a new "Slope length" can be 5 (very steep), 10, 15 (the standard), 20 or 30 m.
- 隔音墙的“淡绿”玻璃太亮、颜色不够深：现在换成更深的绿（色相不变，亮度降到原来的 56 %，饱和度加 5 个点，是照你给的调色读数算的），并且玻璃更不透明一点（0.34 改 0.46），不再被后面被阳光照亮的景物冲淡。吸声板选“绿色”本来就是深绿，没改。蓝色和透明玻璃不变。已经放下的隔音墙重新加载就是新颜色，不用重放。 / The "green tint" glass of the noise barrier was too bright and not deep enough: it is now a deeper green (the same hue, lightness taken down to 56 % and saturation up by 5 points, worked out from the Photoshop reading you sent), and the glass shows a little more (0.46, not 0.34), so the sunlit scene behind it no longer washes it out. The panel colour "Green" was already a deep green and is not changed; the blue and clear glass are as they were. A noise barrier already placed has the new colour when it is loaded: nothing to place again.
- **自动标线（“重标”）暂时下架**，以后交给 ARL 扩展实现：标线列表里不再有它，清除工具里的“清除后按国标重标”也不再显示。已经放下的标线不受影响，“自动栏杆”（按道路类型）没动。 / The **automatic marking ("redraw") is taken down for now** and left to the ARL extension later: it is no longer in the marking list, and the clear tool's "then mark it by the standard" is no longer shown. Markings already placed are not affected; the automatic barrier (by the kind of road) is not touched.
- 在 RIE 里移动、转动信号灯杆后，挂在它竖杆或横杆上的路牌现在会跟着走（位置和朝向一起）：RIE 的杆每秒看 4 次，杆动了牌子就按同样的位移和转角跟上。用移动工具把杆放到 40 米以外，或者杆被拆掉，牌子就留在原地。以前放的牌子读档后第一次遇到杆时自己接上，不用重放；两块牌挂在同一根杆上也一样跟随。这一项不需要改 RIE。 / Boards hung on the post or the arm of a signal pole now go with it when the pole is moved or turned in RIE (place and facing together): the pole is looked at four times a second, and when it has moved the boards follow by the same move and turn. A pole put more than 40 m away with the move tool, or taken away, leaves its boards where they are. Boards placed before attach themselves the first time they meet the pole after a load, so nothing needs to be placed again. Nothing in RIE had to change for this.
- 没有订阅 Realistic Information Extension（RIE）时，和它有关的选项不再出现：支架列表里的“附着式（信号灯横杆）”（以及 `,` `.` 键切换支架）。装上 RIE 之后它几秒内就会出现，不用重启。已经挂在横杆上的牌子照旧显示。 / Without the Realistic Information Extension (RIE) what belongs to it no longer shows: the support "Attached (signal arm)" in the support list (and the `,` `.` keys that go through the supports). With RIE installed it appears within a few seconds, with no restart. Boards already hung on an arm are shown as before.

## 0.12.105

- 文字灯箱（发光字）离远了会变成一条发光条，不管显示距离设成多少：超过 250 米（中距离那一档的界限）每行字就只画成一根条。现在远处画的是发光的字形本身（每个字只画发光的正面，不画字的背面、支撑杆和骨架，也不投影），一直到你设的显示距离。立式灯箱也一样。字特别多又特别繁复的文字（四十个“龘”这样的，发光面超过 4000 个顶点）、弧形排列的文字和没有字体的文字，远处仍然是发光条，因为那样画太重，或者字形要沿弧线转向。仰角的字远处也是字形，跟着一起倾斜。 / A text light box (the glowing letters) turned into one lit bar in the distance, whatever the draw distance was set to: past 250 metres (the middle tier's limit) each line was drawn as a single bar. Now the lit letters themselves are drawn, out to the draw distance you set (only the glowing face of each letter: no backs, studs or frame, and no shadow). The standing light box is the same. A text with very many and very intricate characters (forty of the most intricate ones, over 4,000 vertices of lit faces), a text on an arc, and one whose font is missing are still lit bars in the distance, because the letters would be too heavy or have to be turned along the arc. A leaning text has its letters in the distance too, leaning with it.

## 0.12.104

- 设置页（面板最后一个标签）新增“显存与显示距离”一栏：用一条进度条显示本模组现在占用的显存（网格、牌面图片、细节图）和预算，旁边是内存占用、绘制对象数、地图块数和物件数。预算是显卡显存的 5 %（192 到 768 MB，读不到显存时按 384 MB），一半以内算“轻”，预算以内算“适中”，超过算“偏重”。下面把显示距离的 7 个档位列成按钮，每一档按相机当前位置估算预计占用和轻重，并标出预算内最远的一档（“建议”），点一档就切换，也可以按“采用”键。显示距离原来的那一行从“显示”栏移到了这里。只统计本模组自己的网格和图片，游戏和其他模组的占用算不出来，不在其中；内存一栏是按物件数估的。 / The settings page (the panel's last tab) has a new Memory and distance section: a meter shows the graphics memory this mod holds now (meshes, face pictures, detail maps) against its budget, with the memory, the renderers, the tiles and the items beside it. The budget is 5 % of the card's memory (192 to 768 MB, 384 MB when the card does not say); up to half of it is light, within it fine, over it heavy. Below, the 7 draw distance steps are buttons, each with what it would take for where the camera is and how heavy that is, and the farthest one within the budget marked as suggested; press a step to use it, or the Use button. The draw distance row moved here from the Display section. Only this mod's own meshes and pictures are counted: the game's and the other mods' memory cannot be told from them; the memory figure is estimated from the number of items.

## 0.12.103

- 新增“牌子和标线的显示距离”设置：面板最后一个标签（存档和显示）里的“显示”一栏，可以在 300、500、700、1000、1500、2000、3000 米之间选，默认仍是 700 米。距离越远，要画和保留的网格越多；远处的牌子和标线画得较简略，也不投影，所以不会增加阴影的负担。选择会记在 ModsData/CDLC.TS/preferences.json 里，下次启动还在。路牌近处、中距离的细节范围不变。 / New setting for how far signs and markings are drawn: in the panel's last tab (save data and display), the Display section, with 300, 500, 700, 1000, 1500, 2000 and 3000 m to choose from; the default is still 700 m. The farther, the more meshes are drawn and kept; far signs and markings are drawn in a simpler form and cast no shadow, so shadows cost no more. The choice is kept in ModsData/CDLC.TS/preferences.json and is there at the next start. The near and middle detail ranges are unchanged.

## 0.12.102

- 另存一个新名字的存档、在里面编辑路牌之后，再去加载同一座城市里别的旧存档，那些路牌会跑到旧存档里。原因：旧存档如果没有本模组的记录（比如是在模组存档功能出现之前存的），加载时模组会从这座城市的记录里挑道路最吻合的一份自动恢复，而同一座城市的存档道路几乎一样，挑中的就是最新那份。现在没有自己记录的存档只会从“写入时间不晚于它自己”的记录里找，比它新的记录不可能属于它。已经跑进旧存档并存过盘的路牌，要用删除工具删掉。 / After you saved a city under a new name and edited signs in it, loading another, older save of the same city brought those signs into it. Cause: an older save with no record of this mod (one made before the mod kept records, say) was given, on loading, the city's record whose roads fitted best, and every save of one city has nearly the same roads, so the newest record won. A save with no record of its own is now only given a record written no later than the save itself: a newer record cannot belong to it. Signs that already got into an older save, and were saved there, have to be removed with the delete tool.

## 0.12.101

- 沿路放护栏、声屏障等路侧护栏时，如果点击位置被吸到最外侧车道的白色边线上，护栏现在改为放在路的边缘（和自动护栏一样，在路面外缘内 0.35 米），不再停在白线外侧约 0.8 米、还在行车道上的位置。吸到车道之间的线、中央隔离带上，或者没吸附（直接用鼠标位置）的，都和以前一样。要更靠里，用“外移”调整。已经放下的护栏要重放。 / When a guard rail, noise wall or other roadside barrier was placed along a road and the click snapped to the white edge line of the outermost lane, the barrier now goes to the road's edge (as the automatic barriers do, 0.35 metres inside the road's outer edge) instead of standing about 0.8 metres out on the carriageway beside that line. A click that snapped to a line between lanes or on a median strip, or that took the mouse's own place, is as before. Use the offset (外移) to put it farther in. A barrier that is already placed has to be placed again.

## 0.12.100

- 删除一段道路后，原来放在这段路上的声屏障（和别的沿路物件）有时会留在空地上：它们离旁边那段没删的路的尽头不到二十四米，被当成还在那条路旁边。现在只有站在那条路旁边、或者超出它的尽头不到六米的物件才会留下，其余的随路一起删除（Ctrl+Z 可以撤回）。已经留在空地上的墙请用删除工具删掉。 / After a stretch of road was deleted, a noise wall (or another item along a road) that had stood on it was sometimes left behind on open ground: it was less than twenty-four metres from the end of the neighbouring stretch that was not deleted, and counted as still being beside that road. Now only an item that stands beside that road, or less than six metres past its end, stays; the rest go with the road (Ctrl+Z brings them back). A wall that is already stranded has to be removed with the delete tool.
- 用“连续通过”让声屏障（或别的墙、护栏）跨过岔路口时，如果前后两段路宽度不同、又错开了几米，墙会在节点处拐出一个 Z 形的折角。现在墙在节点两侧用一段平缓的曲线连起来，转角每两米不超过十度，并且不会把墙带进窄路的路面里。已经放下的墙要重放。 / When a noise wall (or another wall or barrier) was drawn straight through a junction with “Go on through” and the road before and after it had different widths and sat a few metres to the side of each other, the wall made a Z-shaped corner at the node. The wall is now joined across the node by one gentle curve, turning at most ten degrees every two metres, and it is not taken into the lanes of the narrower road. A wall that is already placed has to be placed again.

## 0.12.99

- 沿路放很长的声屏障（或别的墙、护栏）时，每隔几段就会少两米，留下一个小缺口：原因是一段的设置文字超出了存档里每件物件能装的字数，末尾的一小截被悄悄截掉了。现在每段按自己设置的长短留够位置，再也不会被截。已经放下的墙不会自己补上，要重放。 / When a very long noise wall (or another wall or barrier) was placed along a road, every few pieces came up two metres short and left a small gap: the settings text of a piece was longer than what an item can hold in a save, and its last bit was cut off without a word. Every piece now gets the room its own settings leave, and nothing is cut any more. A wall that is already placed does not mend itself: place it again.
- 路在节点处向旁边错开一两米并同时变宽（或变窄）时，墙在那里会拐出一个折线，现在平滑地过渡，并且一直在路缘外面。已经放下的墙要重放。 / Where a road shifts a metre or two sideways at a node and at the same time gets wider (or narrower), the wall used to make a kink there; it now goes over smoothly and stays outside the road's edge. A wall that is already placed has to be placed again.

## 0.12.98

- 沿路画声屏障（和别的沿路线）时，如果路在某个节点处由两段拼成，而且两段在节点处侧向错开了一两米，线在那里会先退回去一截再往前，拐出一个钩子。现在不会了。放下来的线要重新放才会变直。 / When a noise wall (or another line along a road) is drawn over a node where two road pieces meet a metre or two to the side of each other, the line used to go back a little and forward again there, making a hook. It does not any more. A line that is already placed has to be placed again to be straight.

## 0.12.97

- 沿路画的线（声屏障、护栏等）在两段路相接的节点处，以前有时会被切出十来米的缺口，现在直路相接就连着画过去。 / Lines drawn along a road (noise walls, barriers and so on) used to be cut over ten metres or so at a node where two road pieces meet; where the road goes straight on they now run across.
- 路被改动、线跟着路走时，节点处不再冒出一段折回去的多余折线（声屏障上那种“迂回折线”）；存档里已经有的这种折线也会自动拉直，不用重画。 / When a road is changed and a line follows it, no stray zigzag that doubles back appears at the node (the switchback you saw on noise walls); the ones already in a save are straightened by themselves, no need to draw them again.

## 0.12.96

- 下沉道路（两边是挡土墙的那种）里牌子和标线吸附不上：上一版的办法没起作用，这一版换了做法。游戏报告的落点是路上方的地面，不是你指的那条路；现在模组直接拿视线去和路面求交，求到的位置就是你指的位置。盖在山下的隧道不受影响。还没在所有下沉路上验证过，如果还是吸不上，请把 `Logs/CDLC.TS.Mod.log` 发给我。 / Signs and markings did not snap in a sunken road (retaining walls on both sides): the last version's method did not work, so this one does it differently. The point the game reports is the ground above the road, not the road you point at; the mod now puts your line of sight through the road surface itself, and the place it meets is the place you point at. A tunnel under a hill is not affected. Not checked on every sunken road; if it still does not snap, please send `Logs/CDLC.TS.Mod.log`.

## 0.12.95

- 下沉道路（两边是挡土墙的那种）里，游戏有时认不出光标下的路，牌子和标线吸附不上。现在模组会自己量一下脚下有没有路，量到就按这条路吸附。这是试着修的，还没在所有下沉路上验证过；如果你那里还是吸不上，请把 `Logs/CDLC.TS.Mod.log` 发给我。 / In a sunken road (the kind with retaining walls on both sides) the game sometimes does not report the road under the cursor, so signs and markings would not snap. The mod now works out for itself whether there is a road under the cursor, and snaps to it if so. This is an attempt that has not been checked on every sunken road; if it still does not snap for you, please send `Logs/CDLC.TS.Mod.log`.

## 0.12.94

- 条目列表的高度和左边的分类条对齐了。 / The list of entries is as tall as the strip of tabs beside it now.
- 分类标签一屏放不下时，条的上下各有一个箭头：亮着说明那个方向还有标签，点它或滚动鼠标滚轮都能翻。 / When the tabs do not all fit, the strip has an arrow at each end: bright means there are more tabs that way; click it or turn the mouse wheel.

## 0.12.93

- 下沉道路和隧道口的路段，以前鼠标指上去找不到路、牌子吸不上；现在能吸附放置了。 / Roads in a cutting or at a tunnel mouth used to be invisible to the cursor, so signs would not snap to them; they snap now.
- 游戏的地下视图打开时（修路时用的那个地下/地上切换），可以在隧道里放。 / With the game's underground view on (the same underground / surface switch the road tools use), you can place inside tunnels too.

## 0.12.92

- 可变信息屏白天不再亮得爆炸：灯点换回原来的材质，白天的亮度重新调过。 / Variable-message screens are no longer blown out by day: the dots are back on the old material and their daytime brightness is set again.
- 白色的墙板、围挡等白天不再那么刺眼：接近白色的表面设了亮度上限。 / White wall panels, hoardings and the like are no longer so glaring by day: near-white surfaces have a brightness ceiling.

## 0.12.91

- 自动标线：标完一段，再标紧挨着的下一段，前一段的线不会再被删掉了。 / Automatic marking: after marking one segment, marking the next one beside it no longer deletes the first one's lines.
- 删除或编辑标线时，鼠标点得稍微偏一点，不会再误选到“清除原版标线”那块区域（以前会让原版标线跑回来，叠在你的线上）。 / When you delete or edit a line and your click is a little off, it no longer picks the "clear the game's markings" area by mistake (that used to bring the game's lines back on top of yours).
- 发光灯箱不再被建筑地块的贴花盖住；发光字夜里稍微暗了一点。 / Glowing light boxes are no longer painted over by a building lot's decal, and their letters are a little dimmer at night.
- 编辑窗口变大了。 / The editor window is bigger.
- 路口牌和多块牌子的支架上，不再冒出引用国标条款的橙色提示。 / The orange notes quoting the standard no longer pop up on junction boards and on supports with several boards.

## 0.12.90

- 按键提示分成两列。 / The key hints are split into two columns.
- 放置更流畅：移动鼠标时不再每次都把预览整个重做。 / Placing is smoother: moving the mouse no longer rebuilds the whole preview every time.

## 0.12.88

- 工具条上多了一个吸管：点一个已经放好的牌子，就把它的设置、支架、倾角和样式吸过来再放；按住 Shift 点，整根杆上的牌子一起吸。 / There is an eyedropper in the tool strip now: click a placed sign to pick up its settings, support, tilt and style and place another like it; hold Shift to take every board on the pole.

## 0.12.87

- 读档之后，原版标线有时会重新冒出来盖在我们的标线上（别的模组在同一帧里把隐藏标记摘掉了），现在处理了。 / After loading a save, the game's markings sometimes showed up again over ours (another mod removed the hiding mark in the same frame). This is handled now.

## 0.12.86

- 修复：用 Move It 移动高架路时，旁边的牌子可能留在原地。 / Fix: when you moved a viaduct with Move It, the signs beside it could stay behind.

## 0.12.85

- 编辑模式：支架的位置（沿路、离路）有了滑块和输入框，悬臂和门架也可以。 / Edit mode: a support's position (along the road and away from it) has a slider and a box now, cantilevers and gantries included.
- 道路菜单里本模组的标签页有了自己的图标，三种路缘石样式也放在里面。 / The mod's tab in the road menu has its own icon, and the three kerb styles are in it.

## 0.12.84

- 存档重新读取之后，路缘线不会再跑到路缘石上面去。 / After a save is loaded again, kerb lines no longer end up on top of the kerb stones.
- 声屏障沿着弯道更顺了；支架页多了一个“立柱放在路中间”的开关（和 `/` 键一样）。 / Noise barriers follow bends better; the support page has a switch for putting a post in the middle of the road (same as the `/` key).

## 0.12.83

- 立柱、横梁、边框和牌面改用游戏自己的着色器，建筑地块的贴花不会再盖到它们上面。 / Posts, beams, frames and sign faces use the game's own shader now, so building decals no longer paint over them.
- 导流线上的斜条会跟着路面高低走；标线的明暗会随坡度变化。 / The diagonal bars of guide strips follow the height of the road, and markings are shaded by the slope.

## 0.12.82

- 路边的牌子、立柱、标线和护栏，现在默认会跟着被 Move It 或游戏改动的道路一起走。 / Signs, posts, markings and barriers beside a road now follow it by default when Move It or the game changes the road.
- 读档后，路缘样式和车道箭头会重新生成。 / Kerb styles and lane arrows are made again after a save is loaded.

## 0.12.81

- 新增两种跨路的附着式支架：把牌子嵌在跨路桥的侧面，或者挂在桥底下。 / Two new over-road supports: a board set into the side of a bridge over the road, or hung under the bridge.

## 0.12.80

- 面板里牌子、支架和样式的名字，会按游戏语言显示。 / The names of signs, supports and styles in the panel follow the game's language.
- 路口预告牌重新画过；牌面可以加照明，也有荧光黄绿可选；`/` 键把立柱放到路中间；旋转有了滑块和输入框；墙上牌子的高度会跟着建筑走。 / The junctions-ahead board is redrawn; boards can be lit and have a fluorescent yellow-green choice; the `/` key puts a post in the middle of the road; turning has a slider and a box; a wall board's height follows the building.

## 0.12.79

- 读档时，首次需要的准备工作改在加载界面下做完；修了一种可能卡在加载界面的情况。 / When a save is loaded, the first-time preparation is now done under the loading screen; fixed a case that could leave the game stuck there.
- 新增公路界碑和道口标柱；里程碑埋进地下的深度按标准图改过了。 / New right-of-way boundary stones and junction posts; the depth of a kilometre stone in the ground now follows the standard's figure.

## 0.12.78

- 新牌子：分车型限速牌、前方路口预告牌、车道选择牌，还有城市曲线牌、立交转弯牌和距离牌。 / New boards: speed limits by vehicle type, junctions ahead, lane choice, and the urban curve, interchange turn and distance boards.
- 新的路侧立柱：柱式轮廓标（附百米牌）、公路里程碑和百米桩。 / New roadside posts: post delineators with 100 m plates, kilometre stones and 100 m posts.
- 支架：门架的跨度、悬臂的长度和净空都有滑块了；新增空间桁架门架和限高门架。 / Supports: a gantry's span, an arm's length and the clearance have sliders now; a new space truss gantry and height-limit frame.
- 护栏：新增九种施工围挡和垂直绿化墙。 / Barriers: nine kinds of construction hoarding and a vertical greening wall.
- 二十种牌面可以在中文下面加一行英文（每块牌子上有开关）。 / Twenty board templates can carry an English line under the Chinese (a switch on each board).

## 更早 · Earlier

0.12.77 及以前是私有测试版，功能请看 README。 / Versions up to 0.12.77 were private test builds; see the README for what the mod does.
