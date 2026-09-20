using System;
using System.Collections.Generic;
using PirateCrew.Audio.Synth;

namespace PirateCrew.Audio
{
    /// <summary>
    /// 音效渲染调度器：<see cref="SfxId"/> → 波形缓冲（离线一次性生成，确定性）。
    ///
    /// 【两条使用路径共用本类】
    ///   · 编辑器离线路径：<c>AudioAssetBuilder</c> 调 <see cref="Render"/> →
    ///     <see cref="WavCodec.EncodePcm16"/> → 写 wav 资产（推荐，运行时零 CPU）；
    ///   · 运行时回退路径：<c>AudioService</c> 在找不到对应 wav 资产时调
    ///     <see cref="CreateRuntimeClipBody"/> 取 PCM，用 <c>AudioClip.Create</c> 现场生成
    ///     （无资产也能出声，代价是首次生成几十毫秒 CPU）。
    ///
    /// 【出口统一处理】非循环音做 4 ms 首尾淡入淡出（防爆音），循环音跳过（会破坏无缝），
    /// 最后统一归一化到 <see cref="SfxCatalog.PeakTarget"/>，保证任何资产都不削波。
    /// </summary>
    public static class SynthRenderer
    {
        /// <summary>
        /// 该 id 是否有程序化合成实现。
        ///
        /// 【为什么需要它】少数 id 的素材是**外部搬运**的（如
        /// <see cref="SfxId.BedPad"/> 来自隔壁 Game-2 的 ambient_pad.wav），
        /// 工程里没有、也不该有对应的合成配方。调用方（音频资产构建器、配方一致性测试）
        /// 必须先问 <c>CanRender</c>，否则会把「本来就没有合成」误判成「漏登记/渲染失败」。
        ///
        /// 注意：**被搬运覆盖、但仍有合成实现的 id**（爆炸/受击/回合音等）返回 true——
        /// 合成版本是资产缺失时的兜底，仍然要可渲染、可测试。
        /// </summary>
        public static bool CanRender(SfxId id)
        {
            // 目前只有底床垫底是纯外部素材；新增纯外部 id 时在此追加。
            return id != SfxId.BedPad;
        }

        /// <summary>全部**可合成**的 id（顺序与 <see cref="SfxCatalog.All"/> 一致）。</summary>
        public static SfxId[] RenderableIds()
        {
            SfxRecipe[] all = SfxCatalog.All;
            var ids = new List<SfxId>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                if (CanRender(all[i].Id))
                    ids.Add(all[i].Id);
            }

            return ids.ToArray();
        }

        /// <summary>渲染一个音效为浮点缓冲（时长/采样率/声道与配方一致）。</summary>
        public static AudioBuffer Render(SfxId id)
        {
            if (!CanRender(id))
                throw new InvalidOperationException(
                    "该 SfxId 是外部搬运素材，没有程序化合成实现: " + id
                    + "（调用前先查 SynthRenderer.CanRender）");

            SfxRecipe recipe = SfxCatalog.Get(id);
            AudioBuffer buffer = Generate(id, SfxCatalog.SampleRate);

            if (buffer == null)
                buffer = SynthUtil.Create(recipe.DurationSeconds, SfxCatalog.SampleRate);

            if (!recipe.Loop)
                buffer.ApplyDeclick(SynthUtil.DeclickSeconds);

            buffer.NormalizeTo(SfxCatalog.PeakTarget);
            return buffer;
        }

        /// <summary>
        /// 运行时回退入口：返回交错单声道 PCM 数据（供 <c>AudioClip.Create</c> 使用）。
        /// 与离线路径完全同源，保证两条路出声一致。
        /// </summary>
        public static float[] CreateRuntimeClipBody(SfxId id, out int sampleRate, out int channels)
        {
            AudioBuffer buffer = Render(id);
            sampleRate = buffer.SampleRate;
            channels = buffer.Channels;
            return buffer.Samples;
        }

        /// <summary>
        /// 渲染全表（编辑器批量落盘用；按 <see cref="SfxCatalog.All"/> 顺序）。
        /// 只含「可合成」的条目：外部搬运素材由 <c>AudioAssetBuilder.SyncPortedAssets</c>
        /// 负责落盘，不在这里重复。
        /// </summary>
        public static List<KeyValuePair<SfxRecipe, AudioBuffer>> RenderAll()
        {
            SfxRecipe[] all = SfxCatalog.All;
            var result = new List<KeyValuePair<SfxRecipe, AudioBuffer>>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                SfxRecipe recipe = all[i];
                if (!CanRender(recipe.Id))
                    continue;
                result.Add(new KeyValuePair<SfxRecipe, AudioBuffer>(recipe, Render(recipe.Id)));
            }

            return result;
        }

        static AudioBuffer Generate(SfxId id, int sampleRate)
        {
            switch (id)
            {
                // ---- 战斗 ----
                case SfxId.Explosion:
                    return CombatSfx.Explosion(sampleRate);
                case SfxId.WoodCrack:
                    return CombatSfx.WoodCrack(sampleRate);
                case SfxId.FleshHit:
                    return CombatSfx.FleshHit(sampleRate);
                case SfxId.WaterSplash:
                    return CombatSfx.WaterSplash(sampleRate);
                case SfxId.ThrowWhoosh:
                    return CombatSfx.ThrowWhoosh(sampleRate);
                case SfxId.Bounce:
                    return CombatSfx.Bounce(sampleRate);
                case SfxId.StoneRoll:
                    return CombatSfx.StoneRoll(sampleRate);
                case SfxId.MineBeep:
                    return CombatSfx.MineBeep(sampleRate);
                case SfxId.CrewDown:
                    return CombatSfx.CrewDown(sampleRate);

                // ---- 环境 ----
                case SfxId.WavesLoop:
                    return AmbientSfx.WavesLoop(sampleRate);
                case SfxId.SeagullCry1:
                    return AmbientSfx.SeagullCry1(sampleRate);
                case SfxId.SeagullCry2:
                    return AmbientSfx.SeagullCry2(sampleRate);
                case SfxId.SeagullCry3:
                    return AmbientSfx.SeagullCry3(sampleRate);
                case SfxId.WindLoop:
                    return AmbientSfx.WindLoop(sampleRate);

                // ---- 反馈 ----
                case SfxId.UnitSelect:
                    return FeedbackSfx.UnitSelect(sampleRate);
                case SfxId.WeaponSwitch:
                    return FeedbackSfx.WeaponSwitch(sampleRate);
                case SfxId.TurnStart:
                    return FeedbackSfx.TurnStart(sampleRate);
                case SfxId.TurnEnd:
                    return FeedbackSfx.TurnEnd(sampleRate);
                case SfxId.DangerWarning:
                    return FeedbackSfx.DangerWarning(sampleRate);

                // ---- UI ----
                case SfxId.UiClick:
                    return UiSfx.UiClick(sampleRate);
                case SfxId.UiPanelOpen:
                    return UiSfx.UiPanelOpen(sampleRate);
                case SfxId.UiError:
                    return UiSfx.UiError(sampleRate);

                // ---- 结果 ----
                case SfxId.VictoryJingle:
                    return MusicSfx.VictoryJingle(sampleRate);
                case SfxId.DefeatJingle:
                    return MusicSfx.DefeatJingle(sampleRate);

                default:
                    throw new ArgumentOutOfRangeException(nameof(id), "未实现合成器的 SfxId: " + id);
            }
        }
    }
}
