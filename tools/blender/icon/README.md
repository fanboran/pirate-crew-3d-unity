# Blender 应用图标无头渲染（`render_icon.py`）

用本机 Blender **纯程序化**渲染《海盗军团夺宝 3D》应用图标：低模骷髅 + 交叉骨 + 黄铜圆环徽章，
深海蓝竖直渐变背景。零外部素材、零贴图文件——全部 Blender 图元 + Principled/Emission 纯色节点材质。

## 复现（一行，仓库根执行）

```bash
"F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup -P tools/blender/icon/render_icon.py
```

- 单次约 **15~30 秒**（本机 OPTIX GPU；无 GPU 自动回退 CPU，纯 CPU 约 1~3 分钟）。
- **幂等可重跑**：所有产物直接覆盖；原程序化图标的备份只做一次（`previous_procedural.png` 存在即跳过）。
- 渲染引擎 Cycles（96 采样 + OpenImageDenoise），1024×1024，正交相机。
- 视图变换锁定 `Standard`（`render_icon.py:377`）：Blender 5.x 默认 AgX 会把 `#0A1D33`/
  `#C9A227` 这些品牌色洗灰，别删这行。

## 参数速查（改哪里）

| 要改什么 | 位置 | 说明 |
| --- | --- | --- |
| **① 灯光亮度/方向** | `render_icon.py:318-322` | key（左上主光）/ fill（右侧冷辅光）/ rim（后上轮廓光）/ bounce（低位暖补光）。**最常调**：主光过强会把颅骨大面推成纯白丢失骨白色调 |
| **② 配色** | `render_icon.py:40-45` | 颅骨 `BONE` / 交叉骨 `BONE_AGED` / 暗部 `SOCKET` / 黄铜 `BRASS` / 背景上下端 `BG_CENTER`·`BG_EDGE`，均为 sRGB hex |
| **③ 取景/主体大小** | `render_icon.py:37` | `ORTHO_SCALE`（越小主体越大）；主体占位另见 `BONE_HALF:51`、`RING_MAJ:48` |
| 分辨率 / 采样数 | `render_icon.py:35-36` | 出 512 版时分辨率改 512 即可（缩略图会自动跟随重算） |
| 圆环粗细 | `render_icon.py:49` | `RING_MIN` 管径（16px 下环只剩约 0.3px，加粗可提升小尺寸存在感） |
| 交叉骨角度/长度 | `render_icon.py:50-51` | 与水平线夹角、半长 |
| 渐变方向 | `render_icon.py:201-230`（`make_backdrop`） | 现为竖直线性（上亮下暗，同平面版图标）；**勿改回径向**——亮心会被骷髅+圆环完全挡住，可见背景环带只剩渐变末端，等于纯色（已踩坑） |

调材质/构图想开 GUI 看：用 `external/icon-blender-work/icon_debug.blend`（脚本每次渲染后自动保存）。

## 判色基准（程序化验收，别只靠"看着像"）

用 PIL 直读 PNG 采样（**不要用 Blender `img.pixels` 判色**——其加载路径的解码会误导，
曾把正确的图"判"成全图洗白；PIL 直读字节才是地面真值）。当前基准：

| 点位 (x, y) | 目标 | 含义 |
| --- | --- | --- |
| (512, 8) | ≈ `#14344E` | 背景渐变亮端（顶） |
| (512, 1015) | ≈ `#0A1D33` | 背景渐变暗端（底） |
| (400, 330) | `#E7E2D7`± | 颅骨亮部（骨白偏奶油，不许到 `#FFFFFF`） |
| (445, 425) | ≲ `#22262E` | 左眼窝（深、有凹感） |
| (512, 975) | 金色系（≠背景色） | 圆环底部（低位补光托起，不许隐入背景） |

参考探针：`external/icon-blender-work/probe_pixels.py`（工作目录，不入库）。

## 产物清单（每个文件应看到什么）

| 文件 | 内容 |
| --- | --- |
| `pirate-crew/Assets/Art/Textures/AppIcon.png` | **主图标** 1024×1024，覆盖写入（`.meta` 不动，Unity 引用不变） |
| `docs/images/icon-blender/previous_procedural.png` | 原程序化平面图标备份（仅首次渲染时创建） |
| `docs/images/icon-blender/preview_512.jpg` | 512×512 JPG 预览，快速评审用 |
| `docs/images/icon-blender/icon_16.png` / `icon_32.png` / `icon_48.png` | 小尺寸验收：16px 应仍能认出"金环 + 白骷髅 + 交叉骨"，眼窝为两个暗点 |
| `external/icon-blender-work/icon_debug.blend` | 场景缓存（gitignored），调参时开 GUI 用 |

## 若对成品不满意，最可能要调的三个参数

1. **灯光能量**（`render_icon.py:318-322`）：嫌颅骨太白/太暗、环底太亮太暗都在这四盏灯；
   判色基准见上表，每次只动一盏。
2. **主体占比**（`ORTHO_SCALE:37`，配合 `BONE_HALF:51` / `RING_MAJ:48`）：
   嫌 16px 下主体太小就缩小取景或加长骨头；注意环不要顶出画面。
3. **配色**（`render_icon.py:40-45`）：所有颜色是 sRGB hex 常量，改动只影响对应部件；
   改背景两端的 hex 会同步影响世界环境光底色（`main` 里 `wbg`）。
