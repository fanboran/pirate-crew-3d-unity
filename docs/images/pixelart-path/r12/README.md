# r12 · 中机位统一到 14 m + 游戏内俯角固定 30°（2026-09-22）

> 创始人原话：「**比当前的 mid 略微近一点的距离最好**，要不我在游戏内调整一个我看着最顺眼的距离当做基准，
> 还有就是**游戏内应该俯仰角固定 30 度，但是左右可以随便旋转**。」
> 本轮就这两件事：取景表的中机位 16 → 14 m；游戏内相机俯角从"真等距 35.264°"改成 **30° 固定 + 方位自由旋转**。

## 1. 十关的中机位（看这十张）

| 关卡 | 中机位 |
| --- | --- |
| L01 云场（样板关） | [pl1-mid](pl1-mid.jpg) |
| L03 空岛（样板关） | [pl3-mid](pl3-mid.jpg) |
| 101 搁浅圣母号 | [pl101-mid](pl101-mid.jpg) |
| 102 环礁 | [pl102-mid](pl102-mid.jpg) |
| 103 鬼火港 | [pl103-mid](pl103-mid.jpg) |
| 104 巨龟环脊 | [pl104-mid](pl104-mid.jpg) |
| 105 红树帷幔 | [pl105-mid](pl105-mid.jpg) |
| 106 螺旋王座 | [pl106-mid](pl106-mid.jpg) |
| 107 雷暴岬 | [pl107-mid](pl107-mid.jpg) |
| 108 沉都之门 | [pl108-mid](pl108-mid.jpg) |

## 2. 取景口径（本轮改动）

| 档位 | 可见高度 | 说明 |
| --- | --- | --- |
| `wide` / `mid` / `close` | **32 / 14 / 7 m**（十关同一组数，不随地图跨度缩放） | 人物大小是锚（r11 定下的规矩）；mid 由 16 → **14 m**（创始人：略近一点更好） |
| `overview`（仅海图） | 0.85 × 跨度 | 整张地图进画面，只读布局不读观感（r11 §2 已说明） |

**14 m 这个数是挑过的，不是随手取**：游戏内滚轮缩放是**整数档 OrthoSize**，而可见高度 = 2 × OrthoSize，
所以游戏内能滚到的距离是偶数米（2/4/…/120）。14 m 正好 = **游戏内正交档 7**——"图里的距离"与
"游戏内能滚到的距离"是同一个档，创始人可以在游戏里滚到顺眼的档位、照着改表。
为此 HUD 提示条会显示当前档的米数（`BattleCameraController.RuntimeVisibleMeters`，同一单位）。

**内容占比（判据读数，`场地占比`）**：10 关里有 9 关随取景变近而上升，**101 是唯一微降的**
（51.0% → 49.0%）——它的中机位对准第一个出生点，取景位置随米数一起变，出入属于正常抖动。

| 关卡 | L01 | L03 | 101 | 102 | 103 | 104 | 105 | 106 | 107 | 108 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 16 m | 54.2% | 82.5% | 51.0% | 58.4% | 35.8% | 71.1% | 39.4% | 33.2% | 65.5% | 83.7% |
| 14 m | 61.6% | 89.6% | 49.0% | 60.5% | 36.3% | 73.6% | 44.1% | 35.4% | 65.9% | 88.3% |

## 3. 判据

十关全绿（块边长 3、色数、墨线在写缓冲、**光照未旁路 100%**、场地在场），结论逐关都是「全部核心判据通过」。

**一条读数要解释清楚，免得下次被它吓到**：`pl1-mid` 的**单图缺线**从 6.50% 跳到 43.90%。它不是墨线丢了——
同一批图里**近黑像素数**是 34434（16 m）→ 35928（14 m），**没有减少**；跳的是那条读数的**分母**
（"剪影边界"按颜色猜，取景变近后画面里岛块占得更多、猜出来的边界集大幅变大）。
这条读数在判据脚本里本来就是**只报不判**（脚本自己的注释：单图测法要靠颜色猜背景与墨线，三种口径都会翻面，
真正的描边判据是两张中间缓冲的对测）。**结论：本轮没有引入墨线回归。**

**对照是干净的**：L01/L03 的 `-wide`（32 m）与 `-close`（7 m）两档取景本轮没动，
把它们与 r10 的同名图逐像素比对，**差异 0.000%**——说明渲染与场景内容在两轮之间没有变，
中机位那点差异只能归给取景这一项。

## 4. 同一轮的相机改动（游戏内 30°）

- `BattleCameraController.OrthoPitchDegrees` 由真等距 **35.264° → 30°**，取值**直接引用**
  `PixelartPilotScene.PitchDegrees`（出图口径）——出图与游戏内必须是同一个投影，
  否则"拿宣传图比观感"这件事本身不成立。
- 方位角**可自由旋转**（右键拖拽，现役玩法）；俯角没有任何输入路径。
  已知取舍：只有方位 45° 及其对称位给出两组地面线斜率相等的对称菱形，别的方位下阶梯长短不一——
  这是裁决明确接受的，不做 snap。
- Battle 场景的烘焙机位（FollowOffset ≈ (18.371, 15, 18.371)，距离恒 30）走
  `BattleScenePipeline` 重烘；`BattleSceneWiringTests` / `CameraFeelRulesTests` 的相机断言已改写并全绿。

## 5. 复现

```bash
U="F:/Unity/2022.3.62f1c1/Editor/Unity.exe"; P="F:/VSCode/pirate-crew-3d-unity/pirate-crew"
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.BuildSystem.BuildScript.BuildFromCommandLineArgs \
  -buildFlavor development -buildScenes development -logFile -
for L in 1 3 101 102 103 104 105 106 107 108; do
  "external/build/0.1.0/win64/PirateCrew3D.exe" -pixelartOut "export/pixelart-r12-pl$L" -pixelartLevel $L
  python tools/pixel-review/judge_pixelart_pilot.py "export/pixelart-r12-pl$L"
done
```

> ⚠ 跑完 `BattleScenePipeline` 记得查 `ProjectSettings/EditorBuildSettings.asset`：装配链里的
> `BattleSceneSetup.RegisterBuildSettings()` 会把 17 个场景覆盖成 3 个（入口/主菜单/战斗），
> 必须恢复（本轮实测又踩了一次）。
