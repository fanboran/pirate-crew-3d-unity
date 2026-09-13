# 程序化音频资产（Sfx / Ambient / Music）

> wav 落在构建资产目录 `Assets/Resources/PirateCrewAudio/`（平铺，分类只体现在文件名前缀），
> 由 `Assets/Editor/AudioAssetBuilder.cs` **程序化合成**生成，
> 不是下载或外部素材：波形 100% 来自 `Assets/Scripts/PirateCrew/Audio/Synth/` 的
> 纯函数（正弦/三角/噪声 + ADSR + 滤波 + Schroeder 混响 + 和弦工具），零版权风险。
> 本 README 留在 `Assets/Art/Audio/`（文档不进构建）。
> 重新生成：菜单 `PirateCrew/音频/生成程序化音频资产（幂等）`，或
> `-executeMethod PirateCrew.EditorTools.AudioAssetBuilder.BuildAll`。

## 导入设置（脚本自动写入，可复核）

| 分类 | Load Type | Compression | Quality | Preload | 理由 |
| --- | --- | --- | --- | --- | --- |
| Sfx | Decompress On Load | Vorbis | 0.90 | 是 | 短音效随时触发，需零解码延迟；单条体积小，常驻内存代价可忽略 |
| Ambient | Streaming | Vorbis | 0.75 | 否 | 6–8 秒循环长期播放，流式解码使内存占用最小 |
| Music | Compressed In Memory | Vorbis | 0.80 | 是 | 3.6–4 秒，结算瞬间必须立即响，不能因建流延迟 |

统一：`forceToMono = true`（3D 空间音必须单声道）、`PreserveSampleRate`（合成本就是 44100 Hz，避免重采样失真）。

**循环点**：Unity 导入器没有循环开关，循环由播放侧 `AudioSource.loop` 控制（见 `AudioService.StartLoop`）。循环音在合成阶段已用交叉淡化折叠成首尾无缝体，无需手工对齐循环点。

## 资产清单

| 音效 | 文件 | 分类 | 时长(s) | 循环 | 空间 | 默认音量 | 触发来源 | 合成配方要点 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Explosion | SfxExplosion.wav | Sfx | 1.60 | 否 | 3D | 0.90 | battle_projectile_detonated（cannonball / cherryBomb / dynamite / boulder / mine / parachuteBomb / rumBottle / SweepingFlame） | 低频冲击（正弦 95→32 Hz 指数滑落，tau≈0.22s）+ 噪声爆（白噪 → 二阶低通 1500→600 Hz，tau≈0.09s）+ 隆隆尾（低通 220 Hz，tau≈0.55s）+ Schroeder 混响 25% |
| WoodCrack | SfxWoodCrack.wav | Sfx | 0.60 | 否 | 3D | 0.80 | battle_projectile_detonated（gunpowderBarrel / woodenCrate） | 木质腔体（三角 190 Hz + 分音 320/540 Hz，tau 0.10–0.18s）+ 4–6 段木片断裂噪声（带通 900–3500 Hz，5–18 ms 随机错位） |
| FleshHit | SfxFleshHit.wav | Sfx | 0.24 | 否 | 3D | 0.70 | crew_damaged（载荷无世界坐标 → 目前退化为 2D，见交付报告待裁决项） | 低频闷响（正弦 150→65 Hz 指数滑落，tau≈0.05s）+ 拍打噪声（低通 1400 Hz，tau≈0.025s） |
| WaterSplash | SfxWaterSplash.wav | Sfx | 0.70 | 否 | 3D | 0.75 | 无事件（弹体落水消失路径不广播）；公开 API PlaySfx 手动触发 | 水花（白噪 → 高通 700 Hz，起音 4 ms，tau≈0.08s）+ 音高下坠（正弦 700→250 Hz）+ 气泡尾（低通 500 Hz，11 Hz 振幅调制，tau≈0.25s） |
| ThrowWhoosh | SfxThrowWhoosh.wav | Sfx | 0.42 | 否 | 2D | 0.65 | battle_shot_released（载荷为拖拽距离 px → 映射音高/音量） | 带通噪声中心频率 180→2400→420 Hz 两段扫（Q≈1.2）+ 幅度 sin^1.5 包络；拖拽越远音高越高 |
| Bounce | SfxBounce.wav | Sfx | 0.16 | 否 | 3D | 0.60 | 无事件（弹体落地弹跳在 WeaponProjectile 内部结算）；公开 API 手动触发 | 弹性音高下坠（正弦 420→130 Hz，tau≈0.05s）+ 3 ms 高频点击（噪声高通 2000 Hz） |
| StoneRoll | SfxStoneRoll.wav | Sfx | 1.40 | 否 | 3D | 0.55 | 无事件（boulder 滚动在 WeaponProjectile 内部结算）；公开 API 手动触发 | 低频隆隆（低通 420 Hz + 900 Hz 双层噪声）+ 4/7 Hz 摩擦调制 + 70 Hz 次低音抖动 |
| MineBeep | SfxMineBeep.wav | Sfx | 0.09 | 否 | 3D | 0.62 | battle_mine_beep（§5.2 beepTimes：0/15/30/38/45/49/53/55/57/59 帧） | 方波 2093 Hz（C7）60 ms，5 ms 起音/释音，轻微音高下坠；越接近引爆音量略升（由 AudioService 按 ElapsedFrames 缩放） |
| CrewDown | SfxCrewDown.wav | Sfx | 0.70 | 否 | 2D | 0.62 | crew_died（载荷无世界坐标 → 2D 播放） | 下行低音号角（锯齿+谐波 330→165 Hz，低通 1200→400 Hz 扫落）+ 短混响 |
| WavesLoop | AmbientWavesLoop.wav | Ambient | 6.00 | 是 | 2D | 0.50 | battle_started → AudioService.StartAmbient；无战斗场景时由场景控制器手动调用 | 三层：低频隆隆（低通 260 Hz，0.35 基幅）+ 每 2 秒一次涌浪（低通 1800 Hz，sin^1.6 包络）+ 浪花泡沫（高通 3000 Hz，sin^4 包络）；调制频率取 6 秒整除（3/2/1 周期）保证循环无缝，末尾用 FoldSeamlessLoop 交叉淡化 |
| SeagullCry1 | AmbientSeagullCry1.wav | Ambient | 0.90 | 否 | 2D | 0.42 | battle_started 后 AudioService 随机间隔点缀（3 个变体随机取一） | 两声「嘎」：基频 1150→1600→900 Hz 滑音 + 4 个谐波（1/h^1.5）+ 带通 2 kHz 共振峰 + 少量噪声嘶哑 |
| SeagullCry2 | AmbientSeagullCry2.wav | Ambient | 1.10 | 否 | 2D | 0.42 | 同 SeagullCry1（变体 2：基频更低、三声） | 三声「嘎」：基频 980→1380→820 Hz，间距更长，尾音上扬 |
| SeagullCry3 | AmbientSeagullCry3.wav | Ambient | 0.75 | 否 | 2D | 0.42 | 同 SeagullCry1（变体 3：单声短促） | 单声短促「嘎」：基频 1250→1700→1000 Hz，快速收尾 |
| WindLoop | AmbientWindLoop.wav | Ambient | 8.00 | 是 | 2D | 0.38 | battle_started → AudioService.StartAmbient | 带通噪声（250–900 Hz）+ 两层慢速阵风调制（0.125/0.25 Hz，8 秒整除）+ 180 Hz 低频底噪；末尾 FoldSeamlessLoop 交叉淡化 |
| UnitSelect | SfxUnitSelect.wav | Sfx | 0.16 | 否 | 2D | 0.55 | 无事件（选角在 AimThrowController 内部）；公开 API PlayUi 手动触发 | 两音上行：正弦 1046.5 Hz（C6）40 ms → 1567.98 Hz（G6）50 ms，5 ms 起音 |
| WeaponSwitch | SfxWeaponSwitch.wav | Sfx | 0.22 | 否 | 2D | 0.60 | ai_decided（载荷含 WeaponSlotIndex → 槽位变化时播放） | 机械点击（噪声高通 2500 Hz，6 ms）+ 木扣（三角 520 Hz，tau≈0.03s）+ 70 ms 后第二下低音确认 |
| TurnStart | SfxTurnStart.wav | Sfx | 0.70 | 否 | 2D | 0.62 | turn_started | 三音上行琶音 C5-E5-G5-C6（MIDI 72/76/79/84），三角波叠加 3 次谐波，每音 90 ms 错位，尾部混响 20% |
| TurnEnd | SfxTurnEnd.wav | Sfx | 0.55 | 否 | 2D | 0.55 | turn_ended | 两音下行 G4→C4（MIDI 67/60），三角波，慢起音 12 ms，尾部混响 20% |
| DangerWarning | SfxDangerWarning.wav | Sfx | 0.60 | 否 | 2D | 0.66 | 无事件（危险判定在投掷/移动规则内）；公开 API PlayUi 手动触发 | 急促双音警笛：440/660 Hz 方波交替 3 次（每次 90 ms），25 Hz 振幅调制，小幅噪声粗糙化 |
| UiClick | SfxUiClick.wav | Sfx | 0.09 | 否 | 2D | 0.50 | 无事件（UI 按钮在 UI 层）；公开 API PlayUi 手动接线（UI 文件不在本波次白名单，见报告） | 正弦 1200 Hz 25 ms tau + 3 ms 噪声点击（高通 1500 Hz） |
| UiPanelOpen | SfxUiPanelOpen.wav | Sfx | 0.36 | 否 | 2D | 0.50 | 无事件；公开 API PlayUi 手动触发 | 柔和上扫噪声（带通 380→2600 Hz，起音 40 ms）+ 叠加上行三角 300→620 Hz；尾部轻微混响 |
| UiError | SfxUiError.wav | Sfx | 0.32 | 否 | 2D | 0.55 | 无事件；公开 API PlayUi 手动触发 | 低沉粗糙双音：方波 160 Hz + 120 Hz 交替（各 140 ms），5 Hz 调幅，噪声粗糙化 |
| VictoryJingle | MusicVictoryJingle.wav | Music | 4.00 | 否 | 2D | 0.70 | match_finished（Outcome = Team0Win，1P 玩家胜） | A 小调 i-VI-III-VII（Am-F-C-G）进行，每和弦 1 秒：低音 + 琶音（八分）+ 旋律层；每和弦由 MusicTheory.TriadOnDegree 生成，失谐叠加 + 混响 30% |
| DefeatJingle | MusicDefeatJingle.wav | Music | 3.60 | 否 | 2D | 0.66 | match_finished（LevelFailed / Draw / 1P 模式下 AI 胜） | A 和声小调下行 i-VII-VI-V（Am-G-F-E），每和弦 0.9 秒，低音区弦垫（失谐锯齿 + 低通）+ 下行旋律 E4-D4-C4-B3；混响 30% |

## 剪辑解析（资产优先，回退兜底）

`AudioService` 的剪辑解析优先级：`ClipProvider` 委托 → `Resources.Load("PirateCrewAudio/<文件名>")` → 运行时 `AudioClip.Create` 合成回退。本目录（`Assets/Resources/PirateCrewAudio/`）正落在 Resources 约定路径上，因此**无需任何场景接线、无需改 Bootstrapper，进入播放即走 wav 资产**；`ClipProvider` 只是留给特殊场合（如换素材源）的覆盖钩子，回退合成仅在资产缺失时兜底。

> 数值说明：全部合成参数（时长/音量/衰减距离/配方细节）为 **AI 提案/待定**，原版 Flash 逆向只给出机制（如 mine 的 beepTimes），没有音频素材与混音参数，需人耳验收。
