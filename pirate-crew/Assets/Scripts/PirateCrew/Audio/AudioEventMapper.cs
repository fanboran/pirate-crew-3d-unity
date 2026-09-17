using PirateCrew.PirateCrew.Combat;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.PirateCrew.Audio
{
    /// <summary>
    /// 事件载荷 → 播放决策的纯映射（可无头测试）。
    ///
    /// 【为什么单独抽出来】「哪种武器引爆放哪个音」「什么结果放哪段乐句」属于**规则**，
    /// 不该埋在 MonoBehaviour 的回调里：抽成纯静态函数后，可以在无头验证台遍历全部
    /// 17 种武器与 4 种对局结果做断言，保证没有武器漏配、没有结果放错音乐。
    ///
    /// 【事件来源】<c>PirateCrew/PirateCrew/Battle/BattleEvents.cs</c> 的现有事件，
    /// 本波次**不新增任何事件、不改任何他人文件**（见交付报告）。
    /// </summary>
    public static class AudioEventMapper
    {
        /// <summary>武器引爆 → 音效（覆盖 §5.2 全部 17 种武器）。</summary>
        public static SfxId SfxForDetonation(WeaponId weapon)
        {
            switch (weapon)
            {
                // 木箱/火药桶：木质碎裂（§5.2 BoxWeapon）
                case WeaponId.WoodenCrate:
                case WeaponId.GunpowderBarrel:
                    return SfxId.WoodCrack;

                // 香蕉/金币：无爆炸，是弹跳/清脆碰撞
                case WeaponId.Banana:
                case WeaponId.PiecesOfEight:
                    return SfxId.Bounce;

                // 船锚/巫毒娃娃：重击/落体碰撞，无爆炸
                case WeaponId.Anchor:
                case WeaponId.VoodooDoll:
                    return SfxId.FleshHit;

                // 海鸥投弹 / 潮汐巨浪：水体相关
                case WeaponId.Seagull:
                case WeaponId.TidalWave:
                    return SfxId.WaterSplash;

                // 其余（cannonball / cherryBomb / dynamite / boulder / mine /
                // parachuteBomb / rumBottle / cannon / SweepingFlame）都是爆炸
                default:
                    return SfxId.Explosion;
            }
        }

        /// <summary>
        /// 对局结果 → 结果乐句裁决（唯一真值）。返回 false 表示这一局**不该放乐句**
        /// （2P 热座下蓝队获胜，对红队玩家既非胜也非败，放任何一段都错），此时
        /// <paramref name="jingle"/> 的值无意义。
        ///
        /// 映射依据：<see cref="MatchOutcome"/>（<c>Combat/TurnRules.cs</c>）与
        /// <c>MatchFinishedPayload.Team1IsAi</c>（1P 模式为 true）。
        /// 【口径消费方】<c>AudioService.OnMatchFinished</c> 与
        /// <c>BattleHud.SettlementJingleFor</c> 都走本方法，两侧测试锁同一致性。
        /// </summary>
        public static bool MusicForMatchOutcome(MatchOutcome outcome, bool team1IsAi, out SfxId jingle)
        {
            switch (outcome)
            {
                case MatchOutcome.Team0Win:
                    jingle = SfxId.VictoryJingle;
                    return true;

                case MatchOutcome.LevelFailed:
                    // 1P 模式玩家失败（含双方全灭）
                    jingle = SfxId.DefeatJingle;
                    return true;

                case MatchOutcome.Draw:
                    jingle = SfxId.DefeatJingle;
                    return true;

                case MatchOutcome.Team1Win:
                    if (team1IsAi)
                    {
                        // 1P：AI（蓝队）获胜 = 玩家失败
                        jingle = SfxId.DefeatJingle;
                        return true;
                    }

                    // 2P 热座：蓝队（玩家 2）获胜，对红队玩家不是失败，不放乐句
                    jingle = SfxId.VictoryJingle;   // 占位：返回值为 false 时调用方不得使用
                    return false;

                default:
                    jingle = SfxId.VictoryJingle;   // 占位：返回值为 false 时调用方不得使用
                    return false;
            }
        }

        /// <summary>海鸥变体选择：把 [0,1) 的随机数映射到 3 个鸣叫变体之一。</summary>
        public static SfxId SeagullVariantForRoll(float roll)
        {
            if (roll < 0f) roll = 0f;
            if (roll >= 1f) roll = 0.999999f;

            int index = (int)(roll * 3f);
            switch (index)
            {
                case 0:
                    return SfxId.SeagullCry1;
                case 1:
                    return SfxId.SeagullCry2;
                default:
                    return SfxId.SeagullCry3;
            }
        }

        /// <summary>该武器引爆是否需要 3D 空间化（爆炸/碎裂是；入水/UI 等视配方而定）。</summary>
        public static SpatialMode SpatialForDetonation(WeaponId weapon)
        {
            return SfxCatalog.Get(SfxForDetonation(weapon)).Spatial;
        }

        /// <summary>
        /// 把爆炸距离映射为「是否需要播放」的快速判定（超出最大距离直接跳过，
        /// 省掉一次剪辑查找与 AudioSource 占用）。
        /// </summary>
        public static bool WithinAudibleRange(WeaponId weapon, float distance)
        {
            SfxRecipe recipe = SfxCatalog.Get(SfxForDetonation(weapon));
            return SpatialAudioRules.IsAudible(distance, recipe.MaxDistance);
        }
    }
}
