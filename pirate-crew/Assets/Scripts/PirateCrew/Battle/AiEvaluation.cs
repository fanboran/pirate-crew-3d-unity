using System;
using System.Collections.Generic;
using PirateCrew.PirateCrew.Combat;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// AI 决定的动作种类（§3.4 三路径 / §6.1 汇总执行）。
    /// </summary>
    public enum AiActionKind
    {
        /// <summary>抛自己（§6.1 <c>weapon == -1</c> → 直接把 <c>vx/vy</c> 赋给角色）。</summary>
        ThrowSelf = 0,

        /// <summary>使用某件武器（§6.1 <c>weapon &gt;= 0</c> → <c>equip + aiPerform</c>）。</summary>
        UseWeapon = 1,

        /// <summary>放弃/结束本回合（§6.1 无正收益且允许放弃，或没有任何合法动作）。</summary>
        EndGo = 2,
    }

    /// <summary>
    /// AI 评估使用的可播种随机源。
    ///
    /// 【为什么不用 <see cref="System.Random"/>】
    /// <c>System.Random</c> 的算法在 .NET Framework/Mono（Unity 2022.3 运行时）与
    /// .NET 8（无头验证台）之间<b>并不相同</b>：同一 seed 在两个运行时会给出不同序列。
    /// 本评估器要求「同一 seed → 同一决策」在<b>两个运行时都成立</b>（否则无头测试
    /// 校验的行为与编辑器里跑出来的行为会分叉），因此自写一个算法固定、跨运行时逐位一致的
    /// PRNG。需要复用外部随机源时用 <see cref="AiRandom.FromSystemRandom"/> 适配。
    /// </summary>
    public interface IAiRandom
    {
        /// <summary>[0,1) 均匀分布。</summary>
        double NextDouble();

        /// <summary>[minInclusive, maxExclusive) 均匀整数；max &lt;= min 时返回 min。</summary>
        int NextInt(int minInclusive, int maxExclusive);
    }

    /// <summary>
    /// xorshift32 实现的确定性 PRNG。算法固定、跨 .NET 8 / Unity Mono 输出完全一致。
    /// 周期 2^32-1，对本用途（几百次采样）绰绰有余；统计质量足够 AI 随机投掷使用。
    /// </summary>
    public sealed class AiRandom : IAiRandom
    {
        // seed == 0 会让 xorshift 锁死在 0，故替换为一个非零常量。
        const uint ZeroSeedSubstitute = 0x9E3779B9u;

        uint _state;

        public AiRandom(int seed)
        {
            _state = seed == 0 ? ZeroSeedSubstitute : unchecked((uint)seed);
        }

        /// <summary>当前内部状态（调试/回放用）。</summary>
        public uint State => _state;

        uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public double NextDouble()
        {
            // 取高 32 位映射到 [0,1)：除以 2^32。
            return NextUInt() * (1.0 / 4294967296.0);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        /// <summary>
        /// 用外部 <see cref="System.Random"/> 作随机源的适配器。
        /// 注意：跨运行时（.NET 8 vs Unity Mono）不保证逐位一致，仅供不需要跨运行时复现的场景。
        /// </summary>
        public static IAiRandom FromSystemRandom(System.Random random)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            return new SystemRandomSource(random);
        }

        sealed class SystemRandomSource : IAiRandom
        {
            readonly System.Random _random;

            public SystemRandomSource(System.Random random)
            {
                _random = random;
            }

            public double NextDouble() => _random.NextDouble();

            public int NextInt(int minInclusive, int maxExclusive)
            {
                if (maxExclusive <= minInclusive)
                    return minInclusive;
                return _random.Next(minInclusive, maxExclusive);
            }
        }
    }

    /// <summary>
    /// 战场单位快照（纯 C#，不含 MonoBehaviour 引用）。
    /// 坐标为 <b>Flash 平面像素域</b>：<see cref="X"/> = 世界 X × 32、<see cref="Y"/> = 世界 <b>Z</b>（纵深）× 32，
    /// 由调用方复用 <see cref="LevelGeometry.ArenaToPixel"/> 完成。高度不参与 AI 的位置启发式评分
    /// （射程/落点比较全在 XZ 平面内），故本结构不携带高度。
    /// </summary>
    public readonly struct AiUnit
    {
        /// <summary>稳定 id（= <c>PirateBase.PirateId</c>）。</summary>
        public readonly int Id;

        /// <summary>队伍索引：0 = 红队，1 = 蓝队（§4.3）。</summary>
        public readonly int TeamIndex;

        /// <summary>Flash 平面像素 x（= 世界 X × 32）。</summary>
        public readonly float X;

        /// <summary>Flash 平面像素 y（= 世界 <b>Z 纵深</b> × 32）。</summary>
        public readonly float Y;

        public readonly int Health;
        public readonly int MaxHealth;

        /// <summary>是否存活（§4.4）。</summary>
        public readonly bool Alive;

        /// <summary>邪恶度（§4.1 / §5.3；AI 打分的 <c>(1+evilness)</c> 乘子）。</summary>
        public readonly int Evilness;

        /// <summary>该单位的 luck（§4.1；AI 武器随机投掷次数基数）。</summary>
        public readonly int Luck;

        public AiUnit(
            int id, int teamIndex, float x, float y,
            int health, int maxHealth, bool alive, int evilness, int luck)
        {
            Id = id;
            TeamIndex = teamIndex;
            X = x;
            Y = y;
            Health = health;
            MaxHealth = maxHealth;
            Alive = alive;
            Evilness = evilness;
            Luck = luck;
        }

        /// <summary>血量比例（§6.3 tidalWave 打分用 health/maxHealth）。</summary>
        public float HealthFraction => MaxHealth > 0 ? (float)Health / MaxHealth : 0f;

        /// <summary>便捷构造（默认满血 100 / 存活 / evilness=0 / luck=5，§4.1 原版统一值）。</summary>
        public static AiUnit Simple(int id, int teamIndex, float x, float y, int evilness = 0, int luck = 5)
        {
            return new AiUnit(id, teamIndex, x, y, CrewCatalog.MaxHealth, CrewCatalog.MaxHealth, true, evilness, luck);
        }
    }

    /// <summary>
    /// 角色武器背包的一个槽位快照。索引与 <see cref="WeaponInventory"/> 的槽位索引一一对应，
    /// 便于把 AI 决定（<c>WeaponSlotIndex</c>）直接交回 <c>WeaponInventory.Equip(index)</c>。
    /// </summary>
    public readonly struct AiWeaponSlot
    {
        public readonly int SlotIndex;
        public readonly WeaponId Id;

        public AiWeaponSlot(int slotIndex, WeaponId id)
        {
            SlotIndex = slotIndex;
            Id = id;
        }
    }

    /// <summary>未开启的空投宝箱快照（§6.2 顺路捡箱子加分用；M2 可传空列表）。</summary>
    public readonly struct AiChest
    {
        public readonly float X;
        public readonly float Y;
        public readonly bool Opened;

        public AiChest(float x, float y, bool opened)
        {
            X = x;
            Y = y;
            Opened = opened;
        }
    }

    /// <summary>
    /// AI 模拟用的地形模型（纯 C#）。
    ///
    /// 【3D 化】竞技场是一块 XZ 水平面（地面顶面恒为世界 <see cref="LevelGeometry.GroundTopY"/>），
    /// 边界之外即水面 —— 所以地形模型就是<b>平面像素域的一块矩形</b>：
    ///   · 落点平面像素落在矩形内 = 落地；
    ///   · 落在矩形外 = 掉出岛外（继续下落到 <see cref="LevelGeometry.WaterSurfaceY"/> 以下）→ 落水（§4.4）。
    /// 高度不再是地形数据（地面是常量平面），故原 <c>GroundPixelY</c> / <c>GroundY</c> 已移除。
    ///
    /// 【M2 取舍】不模拟瓦片/悬挑/侧墙反弹（原版可借墙弹），仍是已知降级。
    /// </summary>
    public sealed class AiTerrain
    {
        /// <summary>竞技场横向（世界 X）像素下/上界。</summary>
        public readonly float MinX;
        public readonly float MaxX;

        /// <summary>竞技场纵深（世界 Z）像素下/上界。</summary>
        public readonly float MinY;
        public readonly float MaxY;

        public AiTerrain(float minX, float maxX, float minY, float maxY)
        {
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }

        /// <summary>平面像素点是否落在竞技场地面矩形内（决定落地还是落水）。</summary>
        public bool IsInside(float pixelX, float pixelY)
            => pixelX >= MinX && pixelX <= MaxX && pixelY >= MinY && pixelY <= MaxY;

        /// <summary>
        /// 放置类武器（woodenCrate / gunpowderBarrel，§6.3 BoxWeapon.canPlace 的近似）能否放在此处。
        /// 判定：AABB 的平面足迹完全落在竞技场矩形内（Flash 的 2D AABB 重投影为 XZ 足迹）。
        /// 【TODO】缺瓦片实体查询，未做「与已有箱体/角色重叠」检测；场景层补上后可替换本方法。
        /// </summary>
        public bool CanPlace(float pixelX, float pixelY, float halfWidth, float halfDepth)
        {
            return pixelX - halfWidth >= MinX && pixelX + halfWidth <= MaxX
                && pixelY - halfDepth >= MinY && pixelY + halfDepth <= MaxY;
        }
    }

    /// <summary>
    /// 一次 AI 评估的完整输入快照（纯 C#）。不持有任何可变全局状态。
    /// 坐标为 Flash 平面像素域（x = 世界 X、y = 世界 Z 纵深），与 §6 的打分阈值同域。
    /// </summary>
    public sealed class AiBattlefield
    {
        /// <summary>全部单位（两队，含死亡；§6.2 的 <c>team.characters.length</c> 需要含死亡的分母）。</summary>
        public readonly IReadOnlyList<AiUnit> Units;

        /// <summary>本回合要评估的角色 id。</summary>
        public readonly int ActingUnitId;

        /// <summary>该角色当前是否可抛自己（§3.4 <c>canThrow</c>）。</summary>
        public readonly bool CanThrow;

        /// <summary>该角色当前是否可用武器（§3.2 <c>canShoot</c>，continueTurn 时只有已选角色为 true）。</summary>
        public readonly bool CanShoot;

        /// <summary>该角色的武器槽位（索引与 WeaponInventory 对齐；顺序 = hasWeapons 顺序）。</summary>
        public readonly IReadOnlyList<AiWeaponSlot> Weapons;

        public readonly AiTerrain Terrain;

        /// <summary>
        /// 水面参考像素值，仅供 §6.3 tidalWave 的「近水带」判据
        /// <c>y &gt;= waterY - 300</c> 使用（该判据在 2D 里是竖直带；重投影后平面 y 即纵深）。
        /// 3D 的<b>落水</b>不再由它与平面 y 直接比较得出 —— 落水是 3D 事实，
        /// 由 <see cref="AiThrowSample.Drowned"/>（掉出地面矩形后下落到水面以下）承载。
        /// </summary>
        public readonly float WaterPixelY;

        /// <summary>未完成宝箱（§6.2 顺路捡箱加分；可空 → 视为无）。</summary>
        public readonly IReadOnlyList<AiChest> Chests;

        public AiBattlefield(
            IReadOnlyList<AiUnit> units,
            int actingUnitId,
            bool canThrow,
            bool canShoot,
            IReadOnlyList<AiWeaponSlot> weapons,
            AiTerrain terrain,
            float waterPixelY,
            IReadOnlyList<AiChest> chests = null)
        {
            Units = units ?? new List<AiUnit>();
            ActingUnitId = actingUnitId;
            CanThrow = canThrow;
            CanShoot = canShoot;
            Weapons = weapons ?? new List<AiWeaponSlot>();
            Terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            WaterPixelY = waterPixelY;
            Chests = chests ?? new List<AiChest>();
        }

        /// <summary>按 id 找单位；不存在返回 false。</summary>
        public bool TryGetUnit(int id, out AiUnit unit)
        {
            for (int i = 0; i < Units.Count; i++)
            {
                if (Units[i].Id == id)
                {
                    unit = Units[i];
                    return true;
                }
            }
            unit = default;
            return false;
        }

        /// <summary>该队的报名人数（含死亡；§6.3 <c>team.characters.length</c>）。</summary>
        public int TeamTotalCount(int teamIndex)
        {
            int n = 0;
            for (int i = 0; i < Units.Count; i++)
            {
                if (Units[i].TeamIndex == teamIndex)
                    n++;
            }
            return n;
        }

        /// <summary>该队的存活人数（§6.3 <c>team.countAlive()</c>）。</summary>
        public int TeamAliveCount(int teamIndex)
        {
            int n = 0;
            for (int i = 0; i < Units.Count; i++)
            {
                AiUnit u = Units[i];
                if (u.TeamIndex == teamIndex && u.Alive)
                    n++;
            }
            return n;
        }

        /// <summary>敌方存活单位质心（无则回落到 actor 自身位置；§6.2 enemyAvgX/Y）。</summary>
        public void EnemyCentroid(int actorTeamIndex, out float averageX, out float averageY)
        {
            float sx = 0f, sy = 0f;
            int n = 0;
            for (int i = 0; i < Units.Count; i++)
            {
                AiUnit u = Units[i];
                if (u.Alive && u.TeamIndex != actorTeamIndex)
                {
                    sx += u.X;
                    sy += u.Y;
                    n++;
                }
            }

            if (n == 0)
            {
                AiUnit actor = default;
                bool has = TryGetUnit(ActingUnitId, out actor);
                averageX = has ? actor.X : 0f;
                averageY = has ? actor.Y : 0f;
                return;
            }

            averageX = sx / n;
            averageY = sy / n;
        }
    }

    /// <summary>
    /// 一次模拟投掷的结果（Flash 平面像素域）。
    /// <see cref="Vx"/>/<see cref="Vy"/> 是<b>发射瞬间</b>的 Flash 平面速度（px/帧，§6.1 执行时直接赋给角色/武器；
    /// 3D 仰角由 <see cref="LevelGeometry.FlashLaunchVelocityToWorld"/> 在执行端统一施加）。
    /// <see cref="Ex"/>/<see cref="Ey"/> 是落点的<b>平面像素</b>（世界落点经 <see cref="LevelGeometry.ArenaToPixel"/>
    /// 取 XZ 分量）。<see cref="Drowned"/> 是 3D 落水事实（掉出地面矩形 → 下落到水面以下）。
    /// </summary>
    public readonly struct AiThrowSample
    {
        public readonly float Vx;
        public readonly float Vy;
        public readonly float Ex;
        public readonly float Ey;

        /// <summary>模拟结束时是否已落水（3D：掉出地面矩形后落到 <see cref="LevelGeometry.WaterSurfaceY"/> 之下）。</summary>
        public readonly bool Drowned;

        public AiThrowSample(float vx, float vy, float ex, float ey, bool drowned)
        {
            Vx = vx;
            Vy = vy;
            Ex = ex;
            Ey = ey;
            Drowned = drowned;
        }
    }

    /// <summary>
    /// 一个候选动作（§6.1 <c>aiMoveList</c> 的元素）。
    ///
    /// <see cref="FlashSuccess"/> = 逆向文档 §6.2/§6.3 的原始 <c>success</c>（逐行转写）。
    /// <see cref="TotalScore"/> = <see cref="FlashSuccess"/> + 伤害增强项（见
    /// <see cref="AiEvaluationOptions.DamageScoreWeight"/> 的说明），是最终排序依据。
    /// </summary>
    public readonly struct AiMoveCandidate
    {
        /// <summary>候选角色 id（§6.1 <c>t.player</c>）。</summary>
        public readonly int ActorUnitId;

        /// <summary>武器槽位索引；-1 = 抛自己（§6.1 <c>weapon</c>）。</summary>
        public readonly int WeaponSlotIndex;

        /// <summary>武器 id（抛自己时为 <see cref="WeaponId.Cannonball"/> 占位，需配合 <see cref="WeaponSlotIndex"/> 判断）。</summary>
        public readonly WeaponId WeaponId;

        /// <summary>发射速度（Flash px/帧）。</summary>
        public readonly float Vx;
        public readonly float Vy;

        /// <summary>voodooDoll 的锁定目标 id；无则 -1（§6.3 voodoo 分支）。</summary>
        public readonly int TargetUnitId;

        /// <summary>特殊武器的落点/高度（seagull 的飞行高度、箱体的放置点等）；无则 0。</summary>
        public readonly float AimX;
        public readonly float AimY;

        /// <summary>§6 原始打分。</summary>
        public readonly float FlashSuccess;

        /// <summary>命中敌方的预期伤害（归一化为「血条份数」；M2 增强项，见 <see cref="AiEvaluationOptions"/>）。</summary>
        public readonly float ExpectedDamage;

        public AiMoveCandidate(
            int actorUnitId, int weaponSlotIndex, WeaponId weaponId,
            float vx, float vy, int targetUnitId, float aimX, float aimY,
            float flashSuccess, float expectedDamage)
        {
            ActorUnitId = actorUnitId;
            WeaponSlotIndex = weaponSlotIndex;
            WeaponId = weaponId;
            Vx = vx;
            Vy = vy;
            TargetUnitId = targetUnitId;
            AimX = aimX;
            AimY = aimY;
            FlashSuccess = flashSuccess;
            ExpectedDamage = expectedDamage;
        }

        /// <summary>
        /// 最终排序分（含伤害增强项）。权重由评估参数给出，故需显式传入——
        /// 默认 0.5 与 <see cref="AiEvaluationOptions.DamageScoreWeight"/> 一致。
        /// 统一走 <see cref="AiEvaluation.TotalScore"/>，避免权重出现第二份默认值。
        /// </summary>
        public float TotalScore(float damageScoreWeight = 0.5f)
            => FlashSuccess + damageScoreWeight * ExpectedDamage;

        /// <summary>是否至少能命中一个敌人（预期伤害 &gt; 0）。</summary>
        public bool CanHitAnyEnemy => ExpectedDamage > 0f;
    }

    /// <summary>
    /// 评估参数（全部有原版依据或显式标注的 M2 增强项）。
    /// </summary>
    public sealed class AiEvaluationOptions
    {
        /// <summary>自抛候选数（§6.2 <c>randomThrows(50)</c>）。</summary>
        public int SelfThrowSamples = AiEvaluation.SelfThrowSampleCount;

        /// <summary>自抛每个落点的 cherryBomb 复核采样数（§6.2 <c>randomThrows(10)</c>）。</summary>
        public int CherryBombRecheckSamples = AiEvaluation.CherryBombRecheckSampleCount;

        /// <summary>单次投掷模拟的最大步数（§6.4「通用武器最多模拟 100 步」）。</summary>
        public int MaxSimulationSteps = AiEvaluation.MaxSimulationSteps;

        /// <summary>
        /// ★ M2 增强项（原版没有）：预期伤害在最终排序中的权重。
        ///
        /// 原版 §6.2/§6.3 的打分是<b>位置启发式</b>，同一落点下 cherryBomb(40) 与 dynamite(70)
        /// 得分完全相同——即原版 AI 不知道「哪件武器伤害更高」。但反过来看，原版 AI 手里几乎总有
        /// 保底 cannonball，而武器选择本身是随机的（靠大量采样撞运气），并未显式比较伤害。
        /// 本工程要求「能命中时优先选更高伤害的动作」，因此补一项显式的预期伤害项：
        ///
        ///   TotalScore = FlashSuccess + DamageScoreWeight × Σ_敌(伤害/最大生命) − 同权重×Σ_友(...)
        ///
        /// 取 <c>0.5</c> 的理由：与 §6.2 里同量纲的「宝箱奖励 +0.5」对齐，量级不超过原始
        /// 位置分的常见幅度（~1），因此只在位置分接近时起决定作用，不推翻原版启发式。
        /// 设为 0 即恢复纯原版行为（测试中有覆盖）。
        /// </summary>
        public float DamageScoreWeight = 0.5f;
    }

    /// <summary>
    /// AI 的最终决定（§6.1 <c>aiMoveDetails</c> 的可执行投影）。
    /// </summary>
    public readonly struct AiDecision
    {
        public readonly AiActionKind Kind;

        /// <summary>行动角色 id。</summary>
        public readonly int ActorUnitId;

        /// <summary>武器槽位索引；无则 -1。</summary>
        public readonly int WeaponSlotIndex;

        public readonly WeaponId WeaponId;

        /// <summary>voodooDoll 锁定目标；无则 -1。</summary>
        public readonly int TargetUnitId;

        /// <summary>发射速度（Flash px/帧；ThrowSelf 时交给角色，UseWeapon 时交给武器）。</summary>
        public readonly float Vx;
        public readonly float Vy;

        /// <summary>特殊武器落点/高度（seagull 高度、箱体放置点）。</summary>
        public readonly float AimX;
        public readonly float AimY;

        /// <summary>最终排序分（= 候选的 TotalScore）。</summary>
        public readonly float Success;

        /// <summary>§6 原始打分（未含伤害增强项）。</summary>
        public readonly float FlashSuccess;

        /// <summary>§6.1：无明显收益且允许放弃 → 跳过本回合。</summary>
        public readonly bool ShouldBailOut;

        /// <summary>本次评估实际执行的投掷模拟次数（观测量；随 luck / 武器数变化）。</summary>
        public readonly int EvaluationCount;

        public AiDecision(
            AiActionKind kind, int actorUnitId, int weaponSlotIndex, WeaponId weaponId,
            int targetUnitId, float vx, float vy, float aimX, float aimY,
            float success, float flashSuccess, bool shouldBailOut, int evaluationCount)
        {
            Kind = kind;
            ActorUnitId = actorUnitId;
            WeaponSlotIndex = weaponSlotIndex;
            WeaponId = weaponId;
            TargetUnitId = targetUnitId;
            Vx = vx;
            Vy = vy;
            AimX = aimX;
            AimY = aimY;
            Success = success;
            FlashSuccess = flashSuccess;
            ShouldBailOut = shouldBailOut;
            EvaluationCount = evaluationCount;
        }
    }

    /// <summary>
    /// 敌方 AI 评估器（纯 C#，不继承 MonoBehaviour、不 new GameObject，可在无头验证台运行）。
    ///
    /// 【对应章节】§6.1（30ms 时间片 + 汇总取 max + bailout）、§6.2（50 次随机自抛打分）、
    ///             §6.3（luck 次武器随机投掷 + 通用打分 + 7 种特殊武器专门评分）、
    ///             §6.4（随机数使用点）、§4.1（luck / evilness）、§5.2 表末注（特殊武器清单）、
    ///             §3.2/§3.4（canThrow/canShoot 决定评估范围）。
    ///
    /// 【坐标域决策】打分公式仍在 <b>Flash 平面像素域</b>（x = 世界 X、y = 世界 Z 纵深；§6 的
    ///   200px/70px/40px 阈值、evilness 距离项逐行转写无换算），但<b>轨迹模拟改为纯 3D 世界域</b>：
    ///   1) 初速 <see cref="LevelGeometry.FlashLaunchVelocityToWorld"/>（含 <c>ThrowLift</c> 抬升）、
    ///      积分 <see cref="ThrowTrajectory.Predict"/>（与 PhysX 实弹相同的半隐式欧拉 / dt / 重力），
    ///      与实弹、预览同源，不再自写第二套积分；
    ///   2) 落点 = 轨迹最先与水平面 <c>y = <see cref="LevelGeometry.GroundTopY"/></c> 相交的那一步；
    ///      落水 = 平面落点掉出竞技场矩形后下落到 <see cref="LevelGeometry.WaterSurfaceY"/> 以下；
    ///   3) 世界落点经 <see cref="LevelGeometry.ArenaToPixel"/> 取 XZ 分量回到平面像素域，供打分比较；
    ///      输入快照由 <c>AiController</c> 用 <see cref="LevelGeometry.ArenaToPixel"/> 组装。
    /// </summary>
    ///
    /// 【时间片】<see cref="AiEvaluationSession"/> 把 §6.1 的 <c>do { aiThink() } while(...)</c>
    /// 拆成可单步的「工作单元」（一次投掷采样 / 一件武器），由 <c>AiController</c> 在 30ms 预算内
    /// 循环调用，避免一帧内跑完 50+500+… 次物理模拟造成掉帧。
    ///
    /// 【随机数】只接受注入的 <see cref="IAiRandom"/>；本类内部绝不调用 <c>UnityEngine.Random</c>，
    /// 因此同 seed 可完全复现（见 <see cref="AiRandom"/> 的跨运行时说明）。
    /// </summary>
    public static class AiEvaluation
    {
        // ------------------------------------------------------------------
        // 原版常量（§6）
        // ------------------------------------------------------------------

        /// <summary>自抛采样数（§6.2 <c>randomThrows(50)</c>）。</summary>
        public const int SelfThrowSampleCount = 50;

        /// <summary>自抛 cherryBomb 复核采样数（§6.2 <c>randomThrows(10)</c>）。</summary>
        public const int CherryBombRecheckSampleCount = 10;

        /// <summary>投掷模拟最大步数（§6.4「最多模拟 100 步」）。</summary>
        public const int MaxSimulationSteps = 100;

        /// <summary>投掷角度下限（§6.2：<c>angle = 180 + int(rand*180)</c>，永远向上抛）。</summary>
        public const int ThrowAngleMin = 180;

        /// <summary>投掷角度范围（180°–360°，含 180 不含 360）。</summary>
        public const int ThrowAngleRange = 180;

        /// <summary>投掷力度下限（§6.2：<c>force = 5 + random*15</c>）。</summary>
        public const float ThrowForceMin = 5f;

        /// <summary>角色自抛力度增量（§6.2：<c>+ random*15</c>，= twangMaxForce(20) − 5）。</summary>
        public const float CharacterThrowForceRange = 15f;

        /// <summary>落水扣分（§6.2：<c>if t.ey &gt;= water.y: s -= 2</c>）。</summary>
        public const float DrownPenalty = 2f;

        /// <summary>通用武器基础分（§6.3：<c>s = -0.01</c>）。</summary>
        public const float WeaponBaseScore = -0.01f;

        /// <summary>tidalWave 的敌人收益系数（§6.3：<c>health/maxHealth * 0.5</c>）。</summary>
        public const float TidalWaveEnemyWeight = 0.5f;

        /// <summary>tidalWave 的队友惩罚系数（§6.3：<c>−1.5 × 受影响队友数</c>）。</summary>
        public const float TidalWaveAllyPenalty = 1.5f;

        /// <summary>tidalWave 的垂直影响带（§6.3：<c>y &gt;= waterY − 300</c>）。</summary>
        public const float TidalWaveVerticalBand = 300f;

        /// <summary>anchor 收益减半系数（§6.3：<c>s *= 0.5</c>）。</summary>
        public const float AnchorScoreScale = 0.5f;

        /// <summary>piecesOfEight 更激进变换的偏移（§6.3：<c>s = (s − 0.5) * 1.2</c>）。</summary>
        public const float PiecesOfEightOffset = 0.5f;

        /// <summary>piecesOfEight 更激进变换的倍率（§6.3）。</summary>
        public const float PiecesOfEightScale = 1.2f;

        /// <summary>seagull 海鸥飞行高度的随机下探量（§6.3：<c>− random*100</c>）。</summary>
        public const float SeagullHeightRandomDrop = 100f;

        /// <summary>seagull 相对「单位顶端」的最小飞行高度（§6.3 的固定 <c>+100</c>，单位 px）。</summary>
        public const float SeagullHeightAboveUnitTop = 100f;

        /// <summary>seagull 随机投弹落点数（§6.3：10 个）。</summary>
        public const int SeagullShotCount = 10;

        /// <summary>箱体武器候选放置点数（§6.3：10 个）。</summary>
        public const int BoxCandidateCount = 10;

        /// <summary>箱体放置点相对目标 x 的偏移范围（§6.3：16–64px）。</summary>
        public const float BoxOffsetMin = 16f;
        public const float BoxOffsetMax = 64f;

        /// <summary>箱体放置点相对地面的 y 偏移幅度（§6.3：±50）。</summary>
        public const float BoxVerticalOffset = 50f;

        /// <summary>箱体武器判定可行点的最少数量（§6.3：要求 ≥3 个）。</summary>
        public const int BoxMinFeasiblePoints = 3;

        /// <summary>voodooDoll 对每个敌人的模拟投掷次数（§6.3：<c>randomThrows(2)</c>）。</summary>
        public const int VoodooSimulationsPerEnemy = 2;

        /// <summary>voodooDoll 命中落水的成功值基数（§6.3：<c>1 + rand*0.2</c>）。</summary>
        public const float VoodooDrownBase = 1f;

        /// <summary>voodooDoll 未落水的成功值区间（§6.3：<c>rand*0.2 − 0.5</c>）。</summary>
        public const float VoodooMissPenalty = 0.5f;

        // ------------------------------------------------------------------
        // 距离阈值（§6.2 / §6.3，单位 px）
        // ------------------------------------------------------------------

        /// <summary>自抛：敌人「近距」阈值 200px（d² &lt; 40000）。</summary>
        public const float SelfNearEnemyRadius = 200f;

        /// <summary>自抛：敌人「贴脸」阈值 100px（d² &lt; 10000）。</summary>
        public const float SelfTooCloseEnemyRadius = 100f;

        /// <summary>自抛：自己落点过近阈值 80px（d² &lt; 6400）。</summary>
        public const float SelfSelfCloseRadius = 80f;

        /// <summary>自抛：队友误伤阈值 40px（d² &lt; 1600）。</summary>
        public const float SelfAllyRadius = 40f;

        /// <summary>通用武器：敌人命中阈值 70px（d² &lt; 4900）。</summary>
        public const float WeaponEnemyRadius = 70f;

        /// <summary>通用武器：队友误伤阈值 40px（d² &lt; 1600）。</summary>
        public const float WeaponAllyRadius = 40f;

        /// <summary>seagull 炸弹命中阈值 40px（§6.3）。</summary>
        public const float SeagullHitRadius = 40f;

        /// <summary>seagull / 箱体：队友误伤阈值 40px（§6.3）。</summary>
        public const float SeagullAllyRadius = 40f;

        /// <summary>anchor 水平命中半宽（§5.2：|x − anchorX| &lt; 48）。</summary>
        public const float AnchorHalfWidth = 48f;

        /// <summary>anchor 垂直命中带（§5.2：anchorY − 64 &lt; y &lt; anchorY）。</summary>
        public const float AnchorVerticalBand = 64f;

        /// <summary>createsession 用最大步数的默认参数对象（无状态，可共享）。</summary>
        static readonly AiEvaluationOptions DefaultOptions = new AiEvaluationOptions();

        // ------------------------------------------------------------------
        // 公共辅助：比较与选择
        // ------------------------------------------------------------------

        /// <summary>候选最终排序分（供测试与调试直接调用；公式见 <see cref="AiEvaluationOptions.DamageScoreWeight"/>）。</summary>
        public static float TotalScore(in AiMoveCandidate candidate, float damageScoreWeight = 0.5f)
        {
            return candidate.FlashSuccess + damageScoreWeight * candidate.ExpectedDamage;
        }

        /// <summary>
        /// §6.1 <c>argmax(all, m =&gt; m.success)</c>：取排序分最高的候选。
        /// 并列时保留<b>先出现</b>的候选（候选顺序由固定的角色/武器顺序决定 → 确定性）。
        /// </summary>
        public static AiMoveCandidate? PickBest(IReadOnlyList<AiMoveCandidate> candidates, float damageScoreWeight = 0.5f)
        {
            if (candidates == null || candidates.Count == 0)
                return null;

            AiMoveCandidate best = candidates[0];
            float bestScore = TotalScore(best, damageScoreWeight);

            for (int i = 1; i < candidates.Count; i++)
            {
                float score = TotalScore(candidates[i], damageScoreWeight);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidates[i];
                }
            }

            return best;
        }

        // ------------------------------------------------------------------
        // 投掷模拟（§6.2 randomThrows / §6.4，复用 ThrowTrajectory 的 3D 半隐式欧拉）
        // ------------------------------------------------------------------

        /// <summary>
        /// 生成一个随机投掷并模拟到落地/落水（§6.2 角色版 / §6.3 通用武器版）。
        /// 角度 <c>180 + int(rand*180)</c>（决定 XZ 平面内的方向）；力度 <c>5 + rand*(twangMax − 5)</c>；
        /// 重力由 <paramref name="weight"/> 决定（角色来自 <see cref="CrewCatalog"/>，武器来自
        /// <see cref="WeaponCatalog"/>）。3D 仰角由 <see cref="LevelGeometry.ThrowLift"/> 统一施加。
        /// </summary>
        public static AiThrowSample RandomThrow(
            float startX, float startY, float twangMax, float weight,
            AiTerrain terrain, IAiRandom random, int maxSteps = MaxSimulationSteps)
        {
            int angle = ThrowAngleMin + random.NextInt(0, ThrowAngleRange);
            float forceRange = MathF.Max(0f, twangMax - ThrowForceMin);
            float force = ThrowForceMin + (float)random.NextDouble() * forceRange;

            double rad = angle * Math.PI / 180.0;
            float vx = (float)Math.Cos(rad) * force;
            float vy = (float)Math.Sin(rad) * force;

            return SimulateShot(startX, startY, vx, vy, weight, terrain, maxSteps);
        }

        /// <summary>
        /// 以给定 Flash 平面初速模拟一发，直到落点或超步数（§6.2 <c>advanceMotion()</c>）。
        /// <b>与 3D 实弹严格同源</b>：初速走 <see cref="LevelGeometry.FlashLaunchVelocityToWorld"/>（含抬升），
        /// 积分走 <see cref="ThrowTrajectory.Predict"/>（与 PhysX 相同的半隐式欧拉、同一 dt 与重力）。
        /// 落点 = 轨迹<b>最先</b>与水平面 <c>y = <see cref="LevelGeometry.GroundTopY"/></c> 相交、
        /// 且平面落点在 <see cref="AiTerrain"/> 矩形内的那一步；掉出矩形则继续下落到
        /// <see cref="LevelGeometry.WaterSurfaceY"/> 以下 → 落水。
        /// 【M2 降级】不模拟侧墙反弹（无瓦片查询，见 <see cref="AiTerrain"/> 头注）。
        /// </summary>
        public static AiThrowSample SimulateShot(
            float startX, float startY, float vx, float vy,
            float weight, AiTerrain terrain, int maxSteps = MaxSimulationSteps)
        {
            // 站立枢轴高度发射（脚底贴 y=0），与 PirateBase / 弹体的生成点一致。
            Vector3 origin = LevelGeometry.PixelToArena(startX, startY);
            origin.y = LevelGeometry.GroundTopY + LevelGeometry.UnitPivotHeight;
            return SimulateFromWorld(origin, vx, vy, weight, terrain, maxSteps);
        }

        /// <summary>
        /// 世界坐标出发的投掷模拟内核（供 seagull 从空中投弹等复用）；参数与 <see cref="SimulateShot"/> 同口径。
        /// </summary>
        static AiThrowSample SimulateFromWorld(
            Vector3 origin, float vx, float vy, float weight,
            AiTerrain terrain, int maxSteps)
        {
            float launchVx = vx;
            float launchVy = vy;

            if (maxSteps < 0)
                maxSteps = 0;

            Vector3 initialVelocity = LevelGeometry.FlashLaunchVelocityToWorld(vx, vy);
            float gravityY = LevelGeometry.WorldGravityY(weight);

            var points = new Vector3[maxSteps];
            ThrowTrajectory.Predict(origin, initialVelocity, gravityY, points, maxSteps, LevelGeometry.FrameSeconds);

            Vector3 end = origin;
            bool drowned = false;

            for (int step = 0; step < maxSteps; step++)
            {
                Vector3 p = points[step];
                end = p;

                // 最先与地面水平面相交、且平面落点仍在地面矩形内 = 落点。
                if (p.y <= LevelGeometry.GroundTopY)
                {
                    Vector2 planar = LevelGeometry.ArenaToPixel(p);
                    if (terrain == null || terrain.IsInside(planar.x, planar.y))
                        break;
                }

                // 掉出地面矩形后会继续下落到水面以下（§4.4 落水即死）。
                if (p.y <= LevelGeometry.WaterSurfaceY)
                {
                    drowned = true;
                    break;
                }
            }

            Vector2 landing = LevelGeometry.ArenaToPixel(end);
            return new AiThrowSample(launchVx, launchVy, landing.x, landing.y, drowned);
        }

        // ------------------------------------------------------------------
        // §6.2 角色自身投掷打分
        // ------------------------------------------------------------------

        /// <summary>
        /// §6.2 自抛打分（逐行转写）。注意原版把 <c>s *= (1 + e.evilness)</c> 写在敌人循环内，
        /// 会对<b>已累计的整份分数</b>连乘——这是原版事实行为，此处忠实保留（并因此让 evilness
        /// 的影响被放大）。落水额外 −2；顺路宝箱 +0.5。
        /// </summary>
        public static float ScoreSelfThrowSample(in AiThrowSample t, AiBattlefield field, int actorUnitId)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            if (!field.TryGetUnit(actorUnitId, out AiUnit actor))
                return 0f;

            float s = 0f;

            // 落点越高越好（y 向下，故取负系数）。
            s += (t.Ey - actor.Y) * -0.003f;

            field.EnemyCentroid(actor.TeamIndex, out float enemyAvgX, out float enemyAvgY);
            s += (MathF.Abs(t.Ex - enemyAvgX) - MathF.Abs(actor.X - enemyAvgX)) / -500f;
            s += (MathF.Abs(t.Ey - enemyAvgY) - MathF.Abs(actor.Y - enemyAvgY)) / -500f;

            // 落水 −2（§6.2）。3D 下「落水」由样本的 Drowned 事实承载（掉出地面矩形），
            // 不再用平面 y 与水位比较（水位是高度，与 XZ 平面正交）。
            if (t.Drowned)
                s -= DrownPenalty;

            for (int i = 0; i < field.Units.Count; i++)
            {
                AiUnit e = field.Units[i];
                if (!e.Alive || e.TeamIndex == actor.TeamIndex)
                    continue;

                float dx = t.Ex - e.X;
                float dy = t.Ey - e.Y;
                float d2 = dx * dx + dy * dy;
                float d = MathF.Sqrt(d2);

                float k;
                if (d2 < SelfNearEnemyRadius * SelfNearEnemyRadius)
                {
                    k = 0.2f * (1f - d / SelfNearEnemyRadius);
                    if (d2 < SelfTooCloseEnemyRadius * SelfTooCloseEnemyRadius)
                    {
                        k *= d / SelfTooCloseEnemyRadius;
                        s -= (1f - d / SelfTooCloseEnemyRadius) * 3f;   // 太近会自伤/暴露
                    }
                }
                else
                {
                    k = 0.3f * MathF.Pow(0.75f, d / SelfNearEnemyRadius);
                }

                s += k;
                s *= (1f + e.Evilness);   // §6.2 原文：乘子写在敌人循环内，连乘
            }

            for (int i = 0; i < field.Units.Count; i++)
            {
                AiUnit ally = field.Units[i];
                if (!ally.Alive || ally.TeamIndex != actor.TeamIndex)
                    continue;

                float dx = t.Ex - ally.X;
                float dy = t.Ey - ally.Y;
                float d2 = dx * dx + dy * dy;
                float d = MathF.Sqrt(d2);

                if (ally.Id == actorUnitId)
                {
                    if (d2 < SelfSelfCloseRadius * SelfSelfCloseRadius)
                        s -= 1f * (1f - d / SelfSelfCloseRadius);
                }
                else if (d2 < SelfAllyRadius * SelfAllyRadius)
                {
                    s -= 0.1f * (1f - d / SelfAllyRadius);
                }
            }

            // 顺路捡箱子（§6.2；M2 可传空列表）。
            for (int i = 0; i < field.Chests.Count; i++)
            {
                AiChest chest = field.Chests[i];
                if (chest.Opened)
                    continue;
                float dx = t.Ex - chest.X;
                float dy = t.Ey - chest.Y;
                if (dx * dx + dy * dy < 1600f)
                    s += 0.5f;
            }

            // 落点别离自己太远（§6.2 末段；d² < 90000 → d < 300px）。
            float sdx = t.Ex - actor.X;
            float sdy = t.Ey - actor.Y;
            float sd2 = sdx * sdx + sdy * sdy;
            float sd = MathF.Sqrt(sd2);
            s += sd2 < 90000f ? 0.3f * sd / 300f - 0.3f : -0.3f;

            return s;
        }

        // ------------------------------------------------------------------
        // §6.3 通用武器打分
        // ------------------------------------------------------------------

        /// <summary>
        /// §6.3 通用武器单次投掷打分（逐行转写）：
        /// <c>s = −0.01</c>；敌人 70px 内 <c>+= 1.5 − d/70</c> 并 <c>s *= (1+evilness)</c>；
        /// 队友 40px 内 <c>−= 1.5 − d/40</c>；最后套用武器专属修正
        /// （anchor ×0.5 / piecesOfEight <c>(s−0.5)×1.2</c>）。
        /// </summary>
        public static float ScoreWeaponSample(WeaponId weaponId, in AiThrowSample t, AiBattlefield field, int actorUnitId)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            if (!field.TryGetUnit(actorUnitId, out AiUnit actor))
                return 0f;

            float s = WeaponBaseScore;

            for (int i = 0; i < field.Units.Count; i++)
            {
                AiUnit e = field.Units[i];
                if (!e.Alive || e.TeamIndex == actor.TeamIndex)
                    continue;

                float dx = t.Ex - e.X;
                float dy = t.Ey - e.Y;
                float d2 = dx * dx + dy * dy;
                if (d2 < WeaponEnemyRadius * WeaponEnemyRadius)
                {
                    float d = MathF.Sqrt(d2);
                    s += 1.5f - d / WeaponEnemyRadius;
                    s *= (1f + e.Evilness);   // §6.3 原文：乘子同样写在敌人循环内
                }
            }

            for (int i = 0; i < field.Units.Count; i++)
            {
                AiUnit ally = field.Units[i];
                if (!ally.Alive || ally.TeamIndex != actor.TeamIndex)
                    continue;

                float dx = t.Ex - ally.X;
                float dy = t.Ey - ally.Y;
                float d2 = dx * dx + dy * dy;
                if (d2 < WeaponAllyRadius * WeaponAllyRadius)
                {
                    float d = MathF.Sqrt(d2);
                    s -= 1.5f - d / WeaponAllyRadius;
                }
            }

            return ApplyWeaponModifier(weaponId, s);
        }

        /// <summary>武器专属修正：anchor / piecesOfEight 走专门公式，其余原样返回（§6.3）。</summary>
        public static float ApplyWeaponModifier(WeaponId weaponId, float score)
        {
            switch (weaponId)
            {
                case WeaponId.Anchor:
                    return ScoreAnchor(score);
                case WeaponId.PiecesOfEight:
                    return ScorePiecesOfEight(score);
                default:
                    return score;
            }
        }

        /// <summary>§6.3 anchor 专门修正：收益减半（不好控）。</summary>
        public static float ScoreAnchor(float score) => score * AnchorScoreScale;

        /// <summary>§6.3 piecesOfEight 专门修正：更激进 <c>(s − 0.5) × 1.2</c>。</summary>
        public static float ScorePiecesOfEight(float score) => (score - PiecesOfEightOffset) * PiecesOfEightScale;

        // ------------------------------------------------------------------
        // §6.3 特殊武器：tidalWave / voodooDoll / seagull / 箱体 / anchor 落点
        // ------------------------------------------------------------------

        /// <summary>
        /// §6.3 tidalWave 专门评分（确定性、无随机）：
        /// <c>s = Σ_受浪敌人 health/maxHealth × 0.5 − 1.5 × 受浪队友数</c>；
        /// 受浪条件 <c>y &gt;= waterY − 300</c>。
        /// </summary>
        public static float ScoreTidalWave(AiBattlefield field, int actorUnitId)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            if (!field.TryGetUnit(actorUnitId, out AiUnit actor))
                return 0f;

            float threshold = field.WaterPixelY - TidalWaveVerticalBand;
            float s = 0f;

            for (int i = 0; i < field.Units.Count; i++)
            {
                AiUnit u = field.Units[i];
                if (!u.Alive || u.Y < threshold)
                    continue;

                if (u.TeamIndex == actor.TeamIndex)
                    s -= TidalWaveAllyPenalty;   // 浪不分敌我，优先别淹自己人
                else
                    s += u.HealthFraction * TidalWaveEnemyWeight;
            }

            return s;
        }

        /// <summary>
        /// §6.3 seagull 专门评估（<c>Seagull.aiSimulation</c>）：
        /// 高度 = 单位顶端 + 100 + rand×100（3D：世界高度，相对地面，单位 px）；随机 10 个 x 落点，
        /// 逐点模拟坠弹（从该高度垂直下落到地面平面），计算坠弹收益
        /// （敌 40px 内 <c>1 − d/40</c>，友 <c>−1.5 + d/40</c>）；
        /// 只保留正收益落点，要求 ≥2 个（<c>shots.length &gt; 1</c>），success 取最大单点收益。
        /// 原版「敌方最高 y」在平坦 3D 竞技场里所有单位等高（= 站立枢轴），故以单位顶端为基准。
        /// </summary>
        public static bool TryPlanSeagull(
            AiBattlefield field, int actorUnitId, IAiRandom random,
            out float success, out float height, out IReadOnlyList<float> shotXs)
        {
            success = 0f;
            height = 0f;
            shotXs = null;

            if (field == null || random == null || field.Terrain == null)
                return false;
            if (!field.TryGetUnit(actorUnitId, out AiUnit actor))
                return false;

            // 敌方存活数与其纵深质心（坠弹的平面纵深落点；原版只有 1D x，3D 需要 Z 分量）。
            int enemyCount = 0;
            float depthSum = 0f;
            for (int i = 0; i < field.Units.Count; i++)
            {
                AiUnit u = field.Units[i];
                if (u.Alive && u.TeamIndex != actor.TeamIndex)
                {
                    enemyCount++;
                    depthSum += u.Y;
                }
            }

            if (enemyCount == 0)
                return false;

            float dropDepth = depthSum / enemyCount;

            // 3D：海鸥在世界高度飞行（单位顶端 + 100..200px），不再是 2D 的「屏幕 y 更小」。
            height = LevelGeometry.UnitsToPixels(LevelGeometry.UnitPivotHeight)
                + SeagullHeightAboveUnitTop
                + (float)random.NextDouble() * SeagullHeightRandomDrop;

            var positive = new List<float>();
            float best = 0f;
            float span = field.Terrain.MaxX - field.Terrain.MinX;

            for (int i = 0; i < SeagullShotCount; i++)
            {
                float x = field.Terrain.MinX + (float)random.NextDouble() * span;

                // 坠弹：从海鸥高度垂直下落（零初速，仅受重力），落在地面平面上。
                Vector3 origin = LevelGeometry.PixelToArena(x, dropDepth);
                origin.y = LevelGeometry.GroundTopY + LevelGeometry.PixelsToUnits(height);
                AiThrowSample drop = SimulateFromWorld(
                    origin, 0f, 0f, CrewCatalog.Weight, field.Terrain, MaxSimulationSteps);
                if (drop.Drowned)
                    continue;

                float g = 0f;
                for (int u = 0; u < field.Units.Count; u++)
                {
                    AiUnit unit = field.Units[u];
                    if (!unit.Alive)
                        continue;
                    float dx = x - unit.X;
                    float dy = drop.Ey - unit.Y;
                    float d = MathF.Sqrt(dx * dx + dy * dy);

                    if (unit.TeamIndex != actor.TeamIndex)
                    {
                        if (d < SeagullHitRadius)
                            g += 1f - d / SeagullHitRadius;
                    }
                    else if (d < SeagullAllyRadius)
                    {
                        g -= 1.5f - d / SeagullAllyRadius;
                    }
                }

                if (g > 0f)
                {
                    positive.Add(x);
                    if (g > best)
                        best = g;
                }
            }

            // §6.3 要求 shots.length > 1，否则该方案不成立。
            if (positive.Count <= 1)
                return false;

            success = best;
            shotXs = positive;
            return true;
        }

        /// <summary>
        /// §6.3 箱体武器（woodenCrate / gunpowderBarrel）专门评估（<c>BoxWeapon.aiSimulation</c>）：
        /// 在优先目标附近取 10 个候选放置点（沿「本队质心 → 目标」方向偏 16–64px、
        /// 纵深（平面 y）以目标为心偏 ±50px），过滤 <see cref="AiTerrain.CanPlace"/>；
        /// 要求 ≥3 个可行点；<c>success = random</c>（原版即纯随机）。
        /// 3D：放置点落在世界地面平面 y=<see cref="LevelGeometry.GroundTopY"/> 上，平面 y 即纵深。
        /// </summary>
        public static bool TryPlanBoxPlacement(
            AiBattlefield field, int actorUnitId, IAiRandom random,
            out float success, out IReadOnlyList<(float x, float y)> points)
        {
            success = 0f;
            points = null;

            if (field == null || random == null || field.Terrain == null)
                return false;
            if (!field.TryGetUnit(actorUnitId, out AiUnit actor))
                return false;

            int targetId = PickPriorityTarget(field, actorUnitId);
            if (targetId < 0 || !field.TryGetUnit(targetId, out AiUnit target))
                return false;

            // 本队存活质心。
            float cx = 0f;
            int n = 0;
            for (int i = 0; i < field.Units.Count; i++)
            {
                AiUnit u = field.Units[i];
                if (u.Alive && u.TeamIndex == actor.TeamIndex)
                {
                    cx += u.X;
                    n++;
                }
            }
            if (n > 0)
                cx /= n;
            else
                cx = actor.X;

            float direction = target.X >= cx ? 1f : -1f;

            WeaponCatalog.TryGet(WeaponId.WoodenCrate, out WeaponStats crate);
            float halfWidth = crate.AabbRadius > 0f ? crate.AabbRadius : 16f;
            // Flash 的「竖直」AABB 半径在平面重投影后 = XZ 足迹的纵深半宽。
            float halfDepth = crate.AabbVerticalRadius > 0f ? crate.AabbVerticalRadius : 15f;

            var feasible = new List<(float x, float y)>(BoxCandidateCount);
            float span = BoxOffsetMax - BoxOffsetMin;

            for (int i = 0; i < BoxCandidateCount; i++)
            {
                float offset = BoxOffsetMin + (float)random.NextDouble() * span;      // 16–64px
                float yOffset = ((float)random.NextDouble() * 2f - 1f) * BoxVerticalOffset;  // ±50px 纵深
                float x = target.X - direction * offset;   // 放在本队与目标之间，偏向己方半侧
                float y = target.Y + yOffset;             // 平面 y = 世界 Z 纵深

                if (field.Terrain.CanPlace(x, y, halfWidth, halfDepth))
                    feasible.Add((x, y));
            }

            if (feasible.Count < BoxMinFeasiblePoints)
                return false;

            // ★ 原版：success = Math.random()（AI 对箱体基本不做评估），此处忠实保留。
            success = (float)random.NextDouble();
            points = feasible;
            return true;
        }

        /// <summary>
        /// §6.3 voodooDoll 专门评估：对每个存活敌人模拟它的 2 次随机投掷；
        /// 若该敌人被抛出后落水 → <c>s = 1 + rand×0.2</c>（最高收益：把敌人扔下水），
        /// 否则 <c>s = rand×0.2 − 0.5</c>。每个采样产出一个候选（携带目标 id 与期望速度）。
        /// </summary>
        public static void PlanVoodoo(
            AiBattlefield field, int actorUnitId, int weaponSlotIndex, IAiRandom random,
            List<AiMoveCandidate> output, ref int evaluationCount)
        {
            if (field == null || random == null || output == null)
                return;
            if (!field.TryGetUnit(actorUnitId, out AiUnit actor))
                return;
            if (field.Terrain == null)
                return;

            for (int i = 0; i < field.Units.Count; i++)
            {
                AiUnit enemy = field.Units[i];
                if (!enemy.Alive || enemy.TeamIndex == actor.TeamIndex)
                    continue;

                for (int k = 0; k < VoodooSimulationsPerEnemy; k++)
                {
                    // 「对每个存活敌人调它的 randomThrows(2)」——即模拟敌人自己被抛出后的落点。
                    AiThrowSample sample = RandomThrow(
                        enemy.X, enemy.Y, CrewCatalog.TwangMaxForce,
                        CrewCatalog.Weight, field.Terrain, random);

                    evaluationCount++;

                    float s = sample.Drowned
                        ? VoodooDrownBase + (float)random.NextDouble() * 0.2f
                        : (float)random.NextDouble() * 0.2f - VoodooMissPenalty;

                    output.Add(new AiMoveCandidate(
                        actorUnitId, weaponSlotIndex, WeaponId.VoodooDoll,
                        sample.Vx, sample.Vy,
                        enemy.Id, sample.Ex, sample.Ey,
                        s, 0f));
                }
            }
        }

        // ------------------------------------------------------------------
        // 目标选择（§6.3 敌人吸引力 = 距离项 × evilness 项）
        // ------------------------------------------------------------------

        /// <summary>
        /// 单个敌人的吸引力（§6.3 通用打分里的首个敌人项，抽成显式目标选择函数）：
        /// 70px 内 <c>(1.5 − d/70) × (1 + evilness)</c>，范围外 0。
        /// 距离越近、evilness 越高 → 越优先。
        /// </summary>
        public static float ScoreEnemyAttractiveness(AiBattlefield field, int actorUnitId, int enemyUnitId)
        {
            if (field == null)
                return 0f;
            if (!field.TryGetUnit(actorUnitId, out AiUnit actor))
                return 0f;
            if (!field.TryGetUnit(enemyUnitId, out AiUnit enemy))
                return 0f;
            if (!enemy.Alive || enemy.TeamIndex == actor.TeamIndex)
                return 0f;

            float dx = enemy.X - actor.X;
            float dy = enemy.Y - actor.Y;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            if (d >= WeaponEnemyRadius)
                return 0f;

            return (1.5f - d / WeaponEnemyRadius) * (1f + enemy.Evilness);
        }

        /// <summary>按吸引力选优先目标；并列取更近者，再并列取 id 更小者（确定性）。</summary>
        public static int PickPriorityTarget(AiBattlefield field, int actorUnitId)
        {
            if (field == null || !field.TryGetUnit(actorUnitId, out AiUnit actor))
                return -1;

            int bestId = -1;
            float bestScore = 0f;
            float bestDist = float.MaxValue;

            for (int i = 0; i < field.Units.Count; i++)
            {
                AiUnit e = field.Units[i];
                if (!e.Alive || e.TeamIndex == actor.TeamIndex)
                    continue;

                float score = ScoreEnemyAttractiveness(field, actorUnitId, e.Id);
                float dx = e.X - actor.X;
                float dy = e.Y - actor.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                bool better;
                if (bestId < 0)
                    better = true;
                else if (score > bestScore)
                    better = true;
                else
                    better = score == bestScore && dist < bestDist;

                if (better)
                {
                    bestId = e.Id;
                    bestScore = score;
                    bestDist = dist;
                }
            }

            return bestId;
        }

        // ------------------------------------------------------------------
        // 期望伤害（M2 增强项，复用 ExplosionResolver）
        // ------------------------------------------------------------------

        /// <summary>
        /// 在落点 (x,y) 使用某武器时，对敌方的预期伤害（归一化为「血条份数」：
        /// Σ 实际伤害 / 目标最大生命；队友误伤记为负）。爆炸伤害复用
        /// <see cref="ExplosionResolver"/>（§5.3 同一公式），不另写一份。
        /// 非爆炸武器：anchor 按 §5.2 固定 60 伤害的命中带判定，其余（boulder 碾压等）返回 0
        /// 并留 TODO。
        /// </summary>
        public static float ExpectedDamage(WeaponId weaponId, float x, float y, AiBattlefield field, int actorUnitId)
        {
            if (field == null || !field.TryGetUnit(actorUnitId, out AiUnit actor))
                return 0f;

            if (weaponId == WeaponId.Anchor)
                return ExpectedAnchorDamage(x, y, field, actor);

            if (!WeaponCatalog.TryGet(weaponId, out WeaponStats stats) || !stats.HasExplosion)
                return 0f;   // 【TODO】boulder 的 |vx|×1.5 碾压伤害与速度相关，需执行时才知道，先不计入。

            var enemyTargets = new List<ExplosionTarget>();
            var enemyNorm = new List<float>();
            var allyTargets = new List<ExplosionTarget>();
            var allyNorm = new List<float>();

            for (int i = 0; i < field.Units.Count; i++)
            {
                AiUnit u = field.Units[i];
                if (!u.Alive)
                    continue;

                ExplosionTarget target = new ExplosionTarget(u.X, u.Y, u.Alive);
                float norm = u.MaxHealth > 0 ? 1f / u.MaxHealth : 0f;
                if (u.TeamIndex == actor.TeamIndex)
                {
                    allyTargets.Add(target);
                    allyNorm.Add(norm);
                }
                else
                {
                    enemyTargets.Add(target);
                    enemyNorm.Add(norm);
                }
            }

            float total = 0f;

            ExplosionResult enemyResult = ExplosionResolver.Resolve(
                stats.ExplosionSize, stats.ExplosionMaxDamage, x, y, enemyTargets);
            for (int i = 0; i < enemyResult.Hits.Length; i++)
                total += enemyResult.Hits[i].Damage * enemyNorm[enemyResult.Hits[i].Index];

            ExplosionResult allyResult = ExplosionResolver.Resolve(
                stats.ExplosionSize, stats.ExplosionMaxDamage, x, y, allyTargets);
            for (int i = 0; i < allyResult.Hits.Length; i++)
                total -= allyResult.Hits[i].Damage * allyNorm[allyResult.Hits[i].Index];

            return total;
        }

        /// <summary>anchor 预期伤害（§5.2 固定 60 伤害，命中带 |x−anchorX| &lt; 48 且 anchorY−64 &lt; y &lt; anchorY）。</summary>
        static float ExpectedAnchorDamage(float x, float y, AiBattlefield field, in AiUnit actor)
        {
            if (!WeaponCatalog.TryGet(WeaponId.Anchor, out WeaponStats stats) || stats.DirectDamage <= 0f)
                return 0f;

            float total = 0f;
            for (int i = 0; i < field.Units.Count; i++)
            {
                AiUnit u = field.Units[i];
                if (!u.Alive || u.TeamIndex == actor.TeamIndex)
                    continue;

                if (MathF.Abs(u.X - x) >= AnchorHalfWidth)
                    continue;
                if (u.Y <= y - AnchorVerticalBand || u.Y >= y)
                    continue;

                total += stats.DirectDamage / (u.MaxHealth > 0 ? u.MaxHealth : CrewCatalog.MaxHealth);
            }

            return total;
        }

        // ------------------------------------------------------------------
        // 顶层入口
        // ------------------------------------------------------------------

        /// <summary>创建一个可单步驱动的评估会话（供 <c>AiController</c> 做 30ms 时间片）。</summary>
        public static AiEvaluationSession CreateSession(
            AiBattlefield field, IAiRandom random, AiEvaluationOptions options = null)
        {
            return new AiEvaluationSession(field, random, options ?? DefaultOptions);
        }

        /// <summary>
        /// 一次跑完的评估入口（测试与「不需要时间片」的调用方用）。
        /// 输入战场快照 + 可播种随机源 → 输出最优动作；同一 seed 结果完全一致。
        /// </summary>
        /// <param name="canBailOut">§6.1 <c>aiCanBailOut</c>：true 表示无正收益时允许跳过回合。</param>
        public static AiDecision Evaluate(
            AiBattlefield field, IAiRandom random, AiEvaluationOptions options = null, bool canBailOut = false)
        {
            AiEvaluationSession session = CreateSession(field, random, options);
            while (!session.IsFinished)
            {
                if (!session.StepOnce())
                    break;
            }

            return session.BuildDecision(canBailOut);
        }

        /// <summary>
        /// 把一个（可能是跨角色汇总出来的）最优候选投影为可执行决定（§6.1 <c>aiMoveDetails</c>）。
        /// 汇总多个角色的会话时由 <c>AiController</c> 调用；单角色时
        /// <see cref="AiEvaluationSession.BuildDecision"/> 也委托到这里，保证判据只有一份。
        /// 没有任何候选时返回 EndGo（合法动作，绝不返回非法的「用武器但无武器」）。
        /// </summary>
        public static AiDecision BuildDecisionFromBest(
            int actingUnitId, AiMoveCandidate? bestOpt, bool canBailOut, int evaluationCount,
            float damageScoreWeight = 0.5f)
        {
            if (bestOpt == null)
            {
                return new AiDecision(
                    AiActionKind.EndGo, actingUnitId, -1, WeaponId.Cannonball,
                    -1, 0f, 0f, 0f, 0f, 0f, 0f, canBailOut, evaluationCount);
            }

            AiMoveCandidate best = bestOpt.Value;
            float total = TotalScore(best, damageScoreWeight);
            bool bail = best.FlashSuccess <= 0f;   // §6.1 的 best.success > 0 判据用原始 success

            AiActionKind kind = best.WeaponSlotIndex < 0
                ? AiActionKind.ThrowSelf
                : AiActionKind.UseWeapon;

            return new AiDecision(
                kind, best.ActorUnitId, best.WeaponSlotIndex, best.WeaponId,
                best.TargetUnitId, best.Vx, best.Vy, best.AimX, best.AimY,
                total, best.FlashSuccess, bail && canBailOut, evaluationCount);
        }
    }

    /// <summary>
    /// 可单步驱动的 AI 评估会话（对应 §6.1 <c>do { c.aiThink() } while(!aiFinished &amp;&amp; elapsed &lt; 30ms)</c>）。
    ///
    /// 工作单元粒度：
    ///   · 自抛阶段：一次采样（1 个投掷模拟 + 该落点的 cherryBomb 复核 10 次模拟）；
    ///   · 武器阶段：一件不重复武器（内部按其采样数模拟，特殊武器按各自专门方案）。
    ///
    /// 【与原文的可见差异】原文先 <c>randomThrows(50)</c> 一次性抽完 50 个样本再逐个打分，
    /// 本实现边抽边打分（每次只抽 1 个样本），因此随机数消耗的<b>交错顺序</b>不同——
    /// 这只影响 AI 的随机序列，不影响任何规则/数值；文档 §6.4 只规定随机性的来源与用途。
    /// 同一 seed 在本实现内仍然完全可复现。
    /// </summary>
    public sealed class AiEvaluationSession
    {
        const int PhaseSelfThrow = 0;
        const int PhaseWeapons = 1;

        readonly AiBattlefield _field;
        readonly IAiRandom _random;
        readonly AiEvaluationOptions _options;
        readonly List<AiMoveCandidate> _candidates = new List<AiMoveCandidate>();

        readonly List<(int slot, WeaponId id)> _distinctWeapons;

        int _phase = PhaseSelfThrow;
        int _selfIndex;
        bool _selfPhaseEntered;
        int _weaponCursor;
        int _evaluationCount;
        bool _finished;

        // aiRemember：§6.2 cherryBomb 复核中收益为正且刷新最佳时记住的落点，供对应武器槽复用。
        bool _hasRemember;
        int _rememberSlot = -1;
        AiThrowSample _rememberSample;

        float _bestSelfFlash = float.NegativeInfinity;

        public AiEvaluationSession(AiBattlefield field, IAiRandom random, AiEvaluationOptions options)
        {
            _field = field ?? throw new ArgumentNullException(nameof(field));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _options = options ?? new AiEvaluationOptions();

            _distinctWeapons = BuildDistinctWeapons(_field.Weapons);
        }

        /// <summary>是否已完成全部评估。</summary>
        public bool IsFinished => _finished;

        /// <summary>已产生的候选（§6.1 <c>aiMoveList</c>）。</summary>
        public IReadOnlyList<AiMoveCandidate> Candidates => _candidates;

        /// <summary>累计投掷模拟次数（观测量）。</summary>
        public int EvaluationCount => _evaluationCount;

        /// <summary>当前最佳候选；无候选返回 null。</summary>
        public AiMoveCandidate? BestCandidate
        {
            get
            {
                if (_candidates.Count == 0)
                    return null;
                return AiEvaluation.PickBest(_candidates, _options.DamageScoreWeight);
            }
        }

        static List<(int slot, WeaponId id)> BuildDistinctWeapons(IReadOnlyList<AiWeaponSlot> weapons)
        {
            var list = new List<(int slot, WeaponId id)>();
            if (weapons == null)
                return list;

            for (int i = 0; i < weapons.Count; i++)
            {
                WeaponId id = weapons[i].Id;
                bool seen = false;
                for (int j = 0; j < list.Count; j++)
                {
                    if (list[j].id == id)
                    {
                        seen = true;
                        break;
                    }
                }

                if (!seen)
                    list.Add((weapons[i].SlotIndex, id));   // 保留首个槽位（原版 firstIndexOfWeapon）
            }

            return list;
        }

        /// <summary>推进一个工作单元。返回 false 表示已结束（等价于 <see cref="IsFinished"/> 变 true）。</summary>
        public bool StepOnce()
        {
            if (_finished)
                return false;

            if (_phase == PhaseSelfThrow)
                return StepSelfThrow();

            return StepWeapon();
        }

        /// <summary>在预算内连续推进最多 <paramref name="maxWorkUnits"/> 个工作单元，返回实际推进数。</summary>
        public int Step(int maxWorkUnits)
        {
            int done = 0;
            while (done < maxWorkUnits && !_finished)
            {
                if (!StepOnce())
                    break;
                done++;
            }
            return done;
        }

        // ------------------------------------------------------------------
        // §6.2 自抛阶段
        // ------------------------------------------------------------------

        bool StepSelfThrow()
        {
            if (!_selfPhaseEntered)
            {
                _selfPhaseEntered = true;
                if (!_field.CanThrow)
                {
                    // §6.2 else 分支：不能抛自己 → 清空候选，本阶段无事可做。
                    _candidates.Clear();
                    _phase = PhaseWeapons;
                    return true;
                }
            }

            if (_selfIndex >= _options.SelfThrowSamples)
            {
                _phase = PhaseWeapons;
                return true;   // 本步只做阶段切换，下一单位进入武器评估
            }

            if (_field.Terrain == null || !_field.TryGetUnit(_field.ActingUnitId, out AiUnit actor))
            {
                _finished = true;
                return false;
            }

            AiThrowSample sample = AiEvaluation.RandomThrow(
                actor.X, actor.Y, CrewCatalog.TwangMaxForce,
                CrewCatalog.Weight, _field.Terrain, _random, _options.MaxSimulationSteps);
            _evaluationCount++;

            float flash = AiEvaluation.ScoreSelfThrowSample(sample, _field, _field.ActingUnitId);

            // §6.2 cherryBomb 落点收益复核（仅当背包里有 cherryBomb 才有意义）。
            int cherrySlot = FirstSlotOf(WeaponId.CherryBomb);
            if (cherrySlot >= 0)
            {
                for (int i = 0; i < _options.CherryBombRecheckSamples; i++)
                {
                    AiThrowSample m = AiEvaluation.RandomThrow(
                        actor.X, actor.Y, CrewCatalog.TwangMaxForce,
                        CrewCatalog.Weight, _field.Terrain, _random, _options.MaxSimulationSteps);
                    _evaluationCount++;

                    float g = CherryBombRecheckGain(m, actor);
                    if (g > 0f)
                    {
                        flash += g;
                        if (flash > _bestSelfFlash)
                        {
                            _bestSelfFlash = flash;
                            _hasRemember = true;
                            _rememberSlot = cherrySlot;
                            _rememberSample = m;
                        }
                    }
                }
            }

            float expected = AiEvaluation.ExpectedDamage(
                WeaponId.CherryBomb, sample.Ex, sample.Ey, _field, _field.ActingUnitId);

            _candidates.Add(new AiMoveCandidate(
                _field.ActingUnitId, -1, WeaponId.Cannonball,
                sample.Vx, sample.Vy, -1, sample.Ex, sample.Ey,
                flash, expected));

            _selfIndex++;
            if (_selfIndex >= _options.SelfThrowSamples)
                _phase = PhaseWeapons;

            return true;
        }

        /// <summary>§6.2 cherryBomb 复核的单点收益 g（复用 §6.3 的距离/evilness 结构）。</summary>
        float CherryBombRecheckGain(in AiThrowSample m, in AiUnit actor)
        {
            float g = 0f;

            for (int i = 0; i < _field.Units.Count; i++)
            {
                AiUnit e = _field.Units[i];
                if (!e.Alive || e.TeamIndex == actor.TeamIndex)
                    continue;

                float dx = m.Ex - e.X;
                float dy = m.Ey - e.Y;
                float d2 = dx * dx + dy * dy;
                if (d2 < AiEvaluation.SelfTooCloseEnemyRadius * AiEvaluation.SelfTooCloseEnemyRadius)
                {
                    float d = MathF.Sqrt(d2);
                    g += (1f - d / AiEvaluation.SelfTooCloseEnemyRadius) * (1f + e.Evilness) * 0.5f;
                }
            }

            float sdx = m.Ex - actor.X;
            float sdy = m.Ey - actor.Y;
            float sd2 = sdx * sdx + sdy * sdy;
            if (sd2 < AiEvaluation.SelfSelfCloseRadius * AiEvaluation.SelfSelfCloseRadius)
            {
                float sd = MathF.Sqrt(sd2);
                g -= 1f - sd / AiEvaluation.SelfSelfCloseRadius;
            }

            return g;
        }

        int FirstSlotOf(WeaponId id)
        {
            for (int i = 0; i < _field.Weapons.Count; i++)
            {
                if (_field.Weapons[i].Id == id)
                    return _field.Weapons[i].SlotIndex;
            }
            return -1;
        }

        // ------------------------------------------------------------------
        // §6.3 武器阶段
        // ------------------------------------------------------------------

        bool StepWeapon()
        {
            if (!_field.CanShoot || _weaponCursor >= _distinctWeapons.Count)
            {
                _finished = true;
                return false;
            }

            if (_field.Terrain == null || !_field.TryGetUnit(_field.ActingUnitId, out AiUnit actor))
            {
                _finished = true;
                return false;
            }

            (int slot, WeaponId id) weapon = _distinctWeapons[_weaponCursor];
            _weaponCursor++;
            if (_weaponCursor >= _distinctWeapons.Count)
                _finished = true;

            switch (weapon.id)
            {
                case WeaponId.TidalWave:
                    AddDeterministicCandidate(
                        weapon.slot, weapon.id,
                        AiEvaluation.ScoreTidalWave(_field, _field.ActingUnitId),
                        0f, 0f, 0f, _field.WaterPixelY);
                    break;

                case WeaponId.VoodooDoll:
                    AiEvaluation.PlanVoodoo(_field, _field.ActingUnitId, weapon.slot, _random,
                        _candidates, ref _evaluationCount);
                    break;

                case WeaponId.Seagull:
                    StepSeagull(weapon.slot);
                    break;

                case WeaponId.WoodenCrate:
                case WeaponId.GunpowderBarrel:
                    StepBoxWeapon(weapon.slot, weapon.id);
                    break;

                case WeaponId.Cannon:
                    // 【TODO】§6.3 的 cannon 分支需要「炮位 + 角度 + dragRange」，而 §5.2 的
                    // WeaponCatalog 未给 cannon 的 dragRange（记 0），且 §5.2 表末的 AI 特判清单
                    // 不含 cannon。M2 暂不产出 cannon 候选（AI 不会选它），不臆造角度/炮位公式。
                    break;

                case WeaponId.Anchor:
                    StepAnchor(actor, weapon.slot);
                    break;

                default:
                    StepGenericWeapon(actor, weapon.slot, weapon.id);
                    break;
            }

            return true;
        }

        /// <summary>§6.3 通用武器：<c>count = floor(luck × 队伍人数 / 存活人数)</c> 次随机投掷。</summary>
        void StepGenericWeapon(in AiUnit actor, int slot, WeaponId id)
        {
            WeaponCatalog.TryGet(id, out WeaponStats stats);
            float twangMax = stats.TwangMax;
            float weight = stats.Weight;

            if (twangMax <= 0f)
                return;   // 无法弹弓发射（原表为「—」）→ 不产出候选；cannon 另有 TODO 说明

            int count = WeaponSampleCount(actor);

            for (int i = 0; i < count; i++)
            {
                AiThrowSample t = AiEvaluation.RandomThrow(
                    actor.X, actor.Y, twangMax, weight,
                    _field.Terrain, _random, _options.MaxSimulationSteps);
                _evaluationCount++;
                AddWeaponSampleCandidate(actor, slot, id, t);
            }

            // §6.3：aiRemember 复用到对应槽位。
            if (_hasRemember && _rememberSlot == slot)
            {
                AddWeaponSampleCandidate(actor, slot, id, _rememberSample);
            }
        }

        /// <summary>§6.3 anchor：点击直落，没有弹弓初速；用「敌方附近列 + 命中带」生成候选，再套 ×0.5 修正。</summary>
        void StepAnchor(in AiUnit actor, int slot)
        {
            if (!WeaponCatalog.TryGet(WeaponId.Anchor, out WeaponStats stats) || stats.DirectDamage <= 0f)
                return;

            // 候选列：每个存活敌人的 x，以及 ±32/±64（覆盖 48px 半宽）。
            float[] offsets = { 0f, 32f, -32f, 64f, -64f };

            for (int i = 0; i < _field.Units.Count; i++)
            {
                AiUnit enemy = _field.Units[i];
                if (!enemy.Alive || enemy.TeamIndex == actor.TeamIndex)
                    continue;

                for (int k = 0; k < offsets.Length; k++)
                {
                    float x = enemy.X + offsets[k];
                    // 3D：锚落在世界地面平面上；平面 y（纵深）取敌人纵深 + 半个命中带，
                    // 使敌人在 §5.2 的 `anchorY-64 < y < anchorY` 命中带内。
                    float y = enemy.Y + AiEvaluation.AnchorVerticalBand * 0.5f;
                    if (x < _field.Terrain.MinX || x > _field.Terrain.MaxX)
                        continue;
                    if (y < _field.Terrain.MinY || y > _field.Terrain.MaxY)
                        continue;

                    AiThrowSample sample = new AiThrowSample(0f, 40f, x, y, false);
                    AddWeaponSampleCandidate(actor, slot, WeaponId.Anchor, sample);
                }
            }
        }

        void StepSeagull(int slot)
        {
            if (!AiEvaluation.TryPlanSeagull(_field, _field.ActingUnitId, _random,
                    out float success, out float height, out IReadOnlyList<float> shotXs))
                return;

            _evaluationCount += AiEvaluation.SeagullShotCount;

            // §6.3 shots[] 供执行阶段逐个投弹；M2 用首个落点作为 AimX，
            // 其余落点由 §6.3 的 seagull aiPerform 在执行阶段自行按高度投弹（原版 Seagull 自管 multi-shot）。
            float firstShotX = shotXs != null && shotXs.Count > 0 ? shotXs[0] : 0f;

            _candidates.Add(new AiMoveCandidate(
                _field.ActingUnitId, slot, WeaponId.Seagull,
                0f, 0f, -1, firstShotX, height,
                success, 0f));
        }

        void StepBoxWeapon(int slot, WeaponId id)
        {
            if (!AiEvaluation.TryPlanBoxPlacement(_field, _field.ActingUnitId, _random,
                    out float success, out IReadOnlyList<(float x, float y)> points))
                return;

            _evaluationCount += AiEvaluation.BoxCandidateCount;

            float px = points[0].x;
            float py = points[0].y;
            float expected = AiEvaluation.ExpectedDamage(id, px, py, _field, _field.ActingUnitId);

            _candidates.Add(new AiMoveCandidate(
                _field.ActingUnitId, slot, id,
                0f, 0f, -1, px, py,
                success, expected));
        }

        void AddDeterministicCandidate(
            int slot, WeaponId id, float flash, float vx, float vy, float aimX, float aimY)
        {
            _candidates.Add(new AiMoveCandidate(
                _field.ActingUnitId, slot, id, vx, vy, -1, aimX, aimY, flash, 0f));
        }

        void AddWeaponSampleCandidate(in AiUnit actor, int slot, WeaponId id, in AiThrowSample t)
        {
            float flash = AiEvaluation.ScoreWeaponSample(id, t, _field, _field.ActingUnitId);
            float expected = AiEvaluation.ExpectedDamage(id, t.Ex, t.Ey, _field, _field.ActingUnitId);

            _candidates.Add(new AiMoveCandidate(
                _field.ActingUnitId, slot, id, t.Vx, t.Vy, -1, t.Ex, t.Ey, flash, expected));
        }

        /// <summary>§6.3 <c>count = floor(luck × team.characters.length / team.countAlive())</c>。</summary>
        int WeaponSampleCount(in AiUnit actor)
        {
            if (!_field.TryGetUnit(actor.Id, out AiUnit self))
                self = actor;

            int luck = self.Luck;
            int total = _field.TeamTotalCount(self.TeamIndex);
            int alive = _field.TeamAliveCount(self.TeamIndex);
            if (alive <= 0)
                alive = 1;

            int count = (int)MathF.Floor((float)luck * total / alive);
            return count < 0 ? 0 : count;
        }

        // ------------------------------------------------------------------
        // 输出
        // ------------------------------------------------------------------

        /// <summary>
        /// 汇总为可执行决定（§6.1：取 max；<c>best.success &gt; 0 or !aiCanBailOut</c> 才执行）。
        /// 判据统一委托 <see cref="AiEvaluation.BuildDecisionFromBest"/>。
        /// </summary>
        public AiDecision BuildDecision(bool canBailOut)
        {
            return AiEvaluation.BuildDecisionFromBest(
                _field.ActingUnitId, BestCandidate, canBailOut, _evaluationCount, _options.DamageScoreWeight);
        }
    }
}
