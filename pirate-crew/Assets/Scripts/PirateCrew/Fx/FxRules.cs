using PirateCrew.Battle;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Fx
{
    /// <summary>伤害数字分档（决定字号与颜色）。</summary>
    public enum DamageTier
    {
        /// <summary>轻伤（占比 &lt; 15%）。</summary>
        Light = 0,

        /// <summary>普通（15% – 35%）。</summary>
        Normal = 1,

        /// <summary>重伤（35% – 60%）。</summary>
        Heavy = 2,

        /// <summary>致命档（≥ 60%）。</summary>
        Critical = 3,
    }

    /// <summary>
    /// 特效的**纯逻辑规则层**：把「爆炸半径 / 伤害量 / 武器 id」映射成粒子数量、寿命、速度、颜色、尺寸。
    ///
    /// 【为什么单独一层】<c>Assets/Scripts/PirateCrew/Fx/</c> 的 MonoBehaviour 胶水层要 <c>new GameObject</c> /
    /// 建 ParticleSystem，脱离 Unity 运行时必然抛 <c>SecurityException</c>（ECall 边界，见
    /// <c>external/harness/README.md</c>）。把"多少颗、飞多快、活多久、什么颜色"这类规则全部抽到这里，
    /// 就能在无头验证台直接断言，胶水层只负责把返回值写进 ParticleSystem 模块。
    ///
    /// 【数值性质】本文件所有映射常量均为 **【AI 提案】**（美术风格指南 §1.1「不假」的可读性诉求，
    /// 没有原版 Flash 反编译依据——原版特效是 AS2 逐帧绘制，逆向文档未导出参数）。
    /// 颜色例外：标注【依据】的取自 `docs/美术风格指南.md` 调色板，可核对。
    /// </summary>
    public static class FxRules
    {
        // ==================================================================
        // 全局限额（性能预算，【AI 提案】）
        // ==================================================================

        /// <summary>
        /// 全场同时存活粒子上限（含所有特效）。目标 1080p / 60fps（`docs/场景设计-战斗竞技场.md:397`）。
        /// 游戏同时只有一次爆炸/一两个命中，实测远低于此值；此值作为 ParticleSystem 的 hard cap 用。
        /// </summary>
        public const int MaxLiveParticles = 1200;

        /// <summary>
        /// 单次爆炸最大粒子数（核心 + 火球 + 火花 + 木屑 + 沙尘 + 烟 六个系统的总和，不含冲击波环；
        /// r6 前为七系统，星屑已从爆炸组合移除）。
        /// 由 <see cref="ExplosionTotalParticles"/> 断言；超出说明某个系统的数量映射被改坏了。
        /// </summary>
        public const int MaxExplosionParticles = 200;

        /// <summary>粒子系统对象池上限（每种特效各一池，超额时回收最旧）。</summary>
        public const int MaxPooledSystems = 32;

        /// <summary>Sprite 特效（环/冲击波/伤害数字）对象池上限。</summary>
        public const int MaxPooledSprites = 48;

        /// <summary>基准爆炸半径（世界单位）：cannonball size=100 → radius=70px → 70/16=4.375u
        /// （`WeaponCatalog.cs:130` size=100，`ExplosionResolver.Radius`）。其余武器按比例缩放。</summary>
        public const float ReferenceRadiusWorld = 70f / LevelGeometry.PixelsPerUnit;

        // ==================================================================
        // 爆炸（ExplosionFx）—— r6 三层结构
        // ==================================================================
        //
        // 【三层是什么】r5 复验（size=160）读到的是"白雾 + 硬边半透明白卡片 + 绿白四角星"，
        // 没有火球：34 颗火球（起始尺寸 1.56 世界单位，见旧 FireballStartSize=半径×0.5）
        // 与 11 颗核心在加法混合下大面积重叠，峰值叠加远超 Bloom threshold 直接饱和成白。
        // r6 把爆炸组合压成**三层**，层与层靠"尺寸/寿命/透明度"拉开，不再互相糊：
        //   ① 亮核（ExplosionCore 材质）：**小而实**——半径×0.18、不再膨胀（EndSize=1.0）、
        //      数量少（火球的 1/4），只做"炸点高光"；
        //   ② 橙色中透体（ExplosionFire 材质，**#FF7A1A 系**）：**中等大小、半透明**——
        //      起始尺寸砍半（半径×0.26），40% 后开始淡出（EndAlpha 0.25），读作"火球"；
        //   ③ 暗烟低透（Smoke 材质）：**大而慢、低透**——半径×0.42、上浮慢、EndAlpha 0.14，
        //      且材质 Tint 由白改为烟灰（见 FxMaterials.Specs 的 Fx_Smoke）。
        // 粒子重叠密度：**主杠杆是尺寸**——火球起始尺寸砍半（半径×0.5 → ×0.26），单颗覆盖面积降到 1/4，
        // 加法叠加的饱和面随之大幅收缩，比单纯减 count 更直接治"叠成白"；count 按三层结构另行重定
        // （火球上限 48→26、核 16→8、烟 36→30，见各自的映射函数）。
        //
        // 【r6 删除】星屑（Star4 绿白四角星）**已从爆炸组合移除**：ExplosionStarParticles 已删，
        // ExplosionFx.Play 不再起该粒子系统。FxMaterial.Star4 与 FxTextureKind.Star4 保留不动
        // （其它特效若要用仍可用，r6 未删资产）。爆炸粒子系统数 7 → 6。
        //
        // 【亮度】材质侧 core/fire 的 _Intensity 在 r5 已砍（×0.60 / ×0.70）的基础上
        // **再乘 0.60**（最终值见 FxMaterials.Specs：core 0.4284、fire 0.3864）。

        /// <summary>爆炸半径（世界单位）= (size/2 + 20) / 32。出处：`ExplosionResolver.Radius` + `LevelGeometry.PixelsPerUnit`。</summary>
        public static float ExplosionRadiusWorld(float explosionSize)
        {
            return LevelGeometry.PixelsToUnits(ExplosionResolver.Radius(explosionSize));
        }

        /// <summary>爆炸视觉缩放（1 = cannonball 基准）。用于粒子尺寸/速度/冲击波环直径。</summary>
        public static float ExplosionVisualScale(float explosionSize)
        {
            float r = ExplosionRadiusWorld(explosionSize);
            return Mathf.Clamp(r / ReferenceRadiusWorld, 0.65f, 2.20f);
        }

        /// <summary>橙色中透体（火球）粒子数。r6：密度减半（原 14 + scale×14，现 10 + scale×8）。</summary>
        public static int FireballParticles(float explosionSize)
        {
            return Mathf.Clamp(10 + Mathf.RoundToInt(ExplosionVisualScale(explosionSize) * 8f), 12, 26);
        }

        /// <summary>火花粒子数（硬边亮线，四散）。</summary>
        public static int SparkParticles(float explosionSize)
        {
            return Mathf.Clamp(10 + Mathf.RoundToInt(ExplosionVisualScale(explosionSize) * 14f), 12, 44);
        }

        /// <summary>木屑/沙尘粒子数（受重力，抛向地面）。</summary>
        public static int DebrisParticles(float explosionSize)
        {
            return Mathf.Clamp(6 + Mathf.RoundToInt(ExplosionVisualScale(explosionSize) * 10f), 8, 30);
        }

        /// <summary>余留烟雾粒子数（低饱和、上浮）。r6：随三层结构略降（scale 系数 12→10）。</summary>
        public static int SmokeParticles(float explosionSize)
        {
            return Mathf.Clamp(8 + Mathf.RoundToInt(ExplosionVisualScale(explosionSize) * 10f), 10, 30);
        }

        /// <summary>
        /// 亮核粒子数（三层之①：数量少、尺寸小、寿命短，只做炸点高光）。
        /// r6：原为火球的 1/3（4~16），现为 1/4（3~8）——核心不再参与大范围叠加。
        /// </summary>
        public static int ExplosionCoreParticles(float explosionSize)
        {
            return Mathf.Clamp(Mathf.RoundToInt(FireballParticles(explosionSize) / 4f), 3, 8);
        }

        /// <summary>
        /// 单次爆炸总粒子数（六个粒子系统之和；预算断言与交付报告用这个数）。
        /// r6：星屑已从爆炸组合移除，本式不再计入。
        /// </summary>
        public static int ExplosionTotalParticles(float explosionSize)
        {
            return FireballParticles(explosionSize)
                 + ExplosionCoreParticles(explosionSize)
                 + SparkParticles(explosionSize)
                 + DebrisParticles(explosionSize)
                 + SmokeParticles(explosionSize);
        }

        /// <summary>火球粒子寿命（秒）。</summary>
        public static float FireballLifetime(float explosionSize)
        {
            return 0.16f + 0.06f * ExplosionVisualScale(explosionSize);
        }

        /// <summary>火花寿命（秒）。</summary>
        public static float SparkLifetime(float explosionSize)
        {
            return 0.28f + 0.10f * ExplosionVisualScale(explosionSize);
        }

        /// <summary>木屑寿命（秒，含落地淡出）。</summary>
        public static float DebrisLifetime(float explosionSize)
        {
            return 0.60f + 0.25f * ExplosionVisualScale(explosionSize);
        }

        /// <summary>烟雾寿命（秒）。</summary>
        public static float SmokeLifetime(float explosionSize)
        {
            return 0.90f + 0.50f * ExplosionVisualScale(explosionSize);
        }

        /// <summary>火球粒子初速（世界单位/秒）。速度类 ×2（格 1→2 单位）。</summary>
        public static float FireballSpeed(float explosionSize)
        {
            return 7.0f + 4.0f * ExplosionVisualScale(explosionSize);
        }

        /// <summary>火花初速（世界单位/秒；比火球快，读作"迸射"）。速度类 ×2。</summary>
        public static float SparkSpeed(float explosionSize)
        {
            return 14.0f + 8.0f * ExplosionVisualScale(explosionSize);
        }

        /// <summary>木屑初速（世界单位/秒）。速度类 ×2。</summary>
        public static float DebrisSpeed(float explosionSize)
        {
            return 8.0f + 5.0f * ExplosionVisualScale(explosionSize);
        }

        /// <summary>烟雾上浮速度（世界单位/秒）。速度类 ×2。</summary>
        public static float SmokeRiseSpeed(float explosionSize)
        {
            return 1.2f + 0.8f * ExplosionVisualScale(explosionSize);
        }

        /// <summary>
        /// 橙色中透体（火球）粒子起始尺寸（世界单位）= 半径 × 0.26。
        /// r6：由 半径×0.5 砍到 0.26（≈半）；尺寸减半让加法叠加面积降到 1/4，
        /// size=160 时由 1.56 世界单位降到 ≈0.81（复验判据：火球簇直径 ≈ 爆心半径的 1.5~2 倍，不再是整块白）。
        /// </summary>
        public static float FireballStartSize(float explosionSize)
        {
            return ExplosionRadiusWorld(explosionSize) * 0.26f;
        }

        /// <summary>
        /// 亮核粒子起始尺寸（世界单位）= 半径 × 0.18（三层之①：小而实）。
        /// r6 新增，替换原先直接复用 <see cref="FireballStartSize"/> 的做法（那时核心比火球还大 1.10×）。
        /// </summary>
        public static float ExplosionCoreStartSize(float explosionSize)
        {
            return ExplosionRadiusWorld(explosionSize) * 0.18f;
        }

        /// <summary>火花粒子尺寸（世界单位）。比例不变；半径本身已 ×2 → 输出随之 ×2，上下限同步 ×2。</summary>
        public static float SparkSize(float explosionSize)
        {
            return Mathf.Clamp(ExplosionRadiusWorld(explosionSize) * 0.06f, 0.10f, 0.28f);
        }

        /// <summary>木屑粒子尺寸（世界单位）。比例不变；半径已 ×2 → 输出 ×2，上下限同步 ×2。</summary>
        public static float DebrisSize(float explosionSize)
        {
            return Mathf.Clamp(ExplosionRadiusWorld(explosionSize) * 0.045f, 0.10f, 0.22f);
        }

        /// <summary>
        /// 暗烟粒子起始尺寸（世界单位，随时间长大）= 半径 × 0.42（三层之③：三者中最大）。
        /// r6：由 0.35 提到 0.42——火球尺寸砍半后，烟保持"大而慢"才有层次，否则烟与火球同尺寸会再糊成一团。
        /// </summary>
        public static float SmokeStartSize(float explosionSize)
        {
            return ExplosionRadiusWorld(explosionSize) * 0.42f;
        }

        /// <summary>冲击波环动画时长（秒）。</summary>
        public static float ShockwaveLifetime(float explosionSize)
        {
            return 0.30f + 0.06f * ExplosionVisualScale(explosionSize);
        }

        /// <summary>冲击波环最大直径（世界单位）= 爆炸直径 × 1.1（略溢出危险区，读作"推出去"）。</summary>
        public static float ShockwaveDiameter(float explosionSize)
        {
            return ExplosionRadiusWorld(explosionSize) * 2f * 1.1f;
        }

        // ==================================================================
        // 水花（WaterSplashFx）
        // ==================================================================

        /// <summary>水花飞沫粒子数：随入水竖直速度增大（更快 = 更大水花）。
        /// 【输入量纲变了】<paramref name="fallSpeed"/> 是世界速度，格 1→2 单位后同样场景下翻倍，
        /// 故系数减半（1.5 → 0.75）以**保持粒子数与旧口径逐值相同**。</summary>
        public static int SplashDropletCount(float fallSpeed)
        {
            float s = Mathf.Max(0f, fallSpeed);
            return Mathf.Clamp(8 + Mathf.RoundToInt(s * 0.75f), 10, 40);
        }

        /// <summary>水花泡沫粒子数（系数减半，见 <see cref="SplashDropletCount"/>）。</summary>
        public static int SplashFoamCount(float fallSpeed)
        {
            return Mathf.Clamp(4 + Mathf.RoundToInt(Mathf.Max(0f, fallSpeed) * 0.25f), 6, 14);
        }

        /// <summary>水花飞沫寿命（秒）。时刻类不乘；速度项系数减半以保持输出（见 <see cref="SplashDropletCount"/>）。</summary>
        public static float SplashLifetime(float fallSpeed)
        {
            return Mathf.Clamp(0.35f + Mathf.Max(0f, fallSpeed) * 0.01f, 0.35f, 0.60f);
        }

        /// <summary>水花飞沫尺寸（世界单位）。常数项 ×2、上下限 ×2；速度项系数不变
        /// （<paramref name="fallSpeed"/> 本身已 ×2，故该项自动 ×2）。</summary>
        public static float SplashDropletSize(float fallSpeed)
        {
            return Mathf.Clamp(0.12f + Mathf.Max(0f, fallSpeed) * 0.004f, 0.12f, 0.28f);
        }

        /// <summary>涟漪最大直径（世界单位）。常数项 ×2、上下限 ×2；速度项系数不变（输入已 ×2）。</summary>
        public static float RippleDiameter(float fallSpeed)
        {
            return Mathf.Clamp(2.4f + Mathf.Max(0f, fallSpeed) * 0.06f, 2.4f, 6.0f);
        }

        /// <summary>涟漪动画时长（秒）。时刻类不乘；速度项系数减半以保持输出。</summary>
        public static float RippleLifetime(float fallSpeed)
        {
            return Mathf.Clamp(0.55f + Mathf.Max(0f, fallSpeed) * 0.01f, 0.55f, 1.00f);
        }

        // ==================================================================
        // 命中（HitFx）
        // ==================================================================

        /// <summary>命中火花粒子数（伤害越高越多）。</summary>
        public static int HitSparkCount(float damage, int maxHealth)
        {
            float ratio = maxHealth > 0 ? Mathf.Clamp01(damage / maxHealth) : 0f;
            return Mathf.Clamp(6 + Mathf.RoundToInt(ratio * 22f), 6, 18);
        }

        /// <summary>命中尘土粒子数。</summary>
        public static int HitDustCount(float damage, int maxHealth)
        {
            float ratio = maxHealth > 0 ? Mathf.Clamp01(damage / maxHealth) : 0f;
            return Mathf.Clamp(3 + Mathf.RoundToInt(ratio * 8f), 3, 8);
        }

        /// <summary>命中火花寿命（秒）。</summary>
        public static float HitSparkLifetime(float damage, int maxHealth)
        {
            float ratio = maxHealth > 0 ? Mathf.Clamp01(damage / maxHealth) : 0f;
            return 0.18f + ratio * 0.16f;
        }

        /// <summary>伤害数字分档：按「本次伤害 / 最大生命」的占比切四档（【AI 提案】）。</summary>
        public static DamageTier TierFor(float damage, int maxHealth)
        {
            if (maxHealth <= 0)
                return DamageTier.Light;

            float ratio = damage / maxHealth;
            if (ratio < 0.15f)
                return DamageTier.Light;
            if (ratio < 0.35f)
                return DamageTier.Normal;
            if (ratio < 0.60f)
                return DamageTier.Heavy;
            return DamageTier.Critical;
        }

        /// <summary>伤害数字颜色（【依据】`docs/美术风格指南.md` §2.1/§2.2/§2.3 调色板，可核对）。</summary>
        public static Color32 TierColor(DamageTier tier)
        {
            switch (tier)
            {
                case DamageTier.Light: return FromHex(0xF3E9D2);    // UI 主文字（Art Bible §2.3）
                case DamageTier.Normal: return FromHex(0xFFC24B);   // 强调金（Art Bible §2.2）
                case DamageTier.Heavy: return FromHex(0xFF7A1A);    // 岩浆中间调（Art Bible §2.1）
                default: return FromHex(0xCC2222);                  // 危险色（Art Bible §2.2）
            }
        }

        /// <summary>伤害数字字号倍率（越大越醒目）。</summary>
        public static float TierScale(DamageTier tier)
        {
            switch (tier)
            {
                case DamageTier.Light: return 0.90f;
                case DamageTier.Normal: return 1.00f;
                case DamageTier.Heavy: return 1.15f;
                default: return 1.35f;
            }
        }

        /// <summary>伤害数字基础世界高度（单位）。
        /// 【AI 提案】取 0.60（格 1→2 单位 ×2）：单位 AABB 总高 1.0（Art Bible §5.2），数字约为单位高的 3/5，
        /// 在默认 45° 相机（距离 18）下屏上约 15-20px，压过"单位竖高 ≥25px"判据里的可读性门槛。</summary>
        public const float DamageNumberBaseHeight = 0.60f;

        /// <summary>伤害数字上浮距离（世界单位，0.9s 内）。距离类 ×2。</summary>
        public const float DamageNumberRise = 1.70f;

        /// <summary>伤害数字存活时长（秒）。</summary>
        public const float DamageNumberLifetime = 0.90f;

        // ==================================================================
        // 拖尾（ProjectileTrailFx）
        // ==================================================================

        /// <summary>
        /// 实弹拖尾基础宽度（世界单位）。**与预览线区分**：预览线是
        /// `BattleSceneSetup.cs:399-419` 的**单色不发光细线（线宽 0.06）**，本拖尾是
        /// 带宽度衰减 + 暖色 additive 的**发光尾迹**，语义是"已经飞出去的实体"，不是"将要飞去哪"。
        /// </summary>
        public const float TrailBaseWidth = 0.10f;

        /// <summary>爆炸类武器的拖尾增宽（读作"危险物"）。宽度类 ×2。</summary>
        public const float TrailExplosiveWidthBonus = 0.04f;

        /// <summary>拖尾残留时长（秒）。越短越"利落"，避免拖影盖住命中反馈。</summary>
        public const float TrailTime = 0.18f;

        /// <summary>拖尾最小顶点间距（世界单位）。距离类 ×2。</summary>
        public const float TrailMinVertexDistance = 0.16f;

        /// <summary>按武器取拖尾宽度。</summary>
        public static float TrailWidth(WeaponId weapon)
        {
            WeaponStats stats = WeaponCatalog.Get(weapon);
            return TrailBaseWidth + (stats.HasExplosion ? TrailExplosiveWidthBonus : 0f);
        }

        /// <summary>拖尾颜色：爆炸类偏暖橙，其余偏选中青（与"我在操作的东西"视觉统一，Art Bible §2.2）。</summary>
        public static Color32 TrailColor(WeaponId weapon)
        {
            WeaponStats stats = WeaponCatalog.Get(weapon);
            return stats.HasExplosion ? FromHex(0xFF9A3C) : FromHex(0x49D9D6);
        }

        // ==================================================================
        // 回合/选中标记（TurnMarkerFx）
        // ==================================================================

        /// <summary>地面光环直径（世界单位）。单位 AABB 总高 1.0（Art Bible §5.2），环略大于脚底，读作"站在这里"。
        /// 直径类 ×2（格 1→2 单位）。</summary>
        public const float TurnMarkerDiameter = 1.80f;

        /// <summary>光环脉动频率（Hz）。</summary>
        public const float TurnMarkerPulseHz = 1.6f;

        /// <summary>光环基础不透明度。</summary>
        public const float TurnMarkerAlpha = 0.55f;

        /// <summary>队伍光环色（【依据】Art Bible §2.2 阵营色）。</summary>
        public static Color32 TeamMarkerColor(int teamNumber)
        {
            return teamNumber == 1 ? FromHex(0xFF3A29) : FromHex(0x3366FF);
        }

        /// <summary>被聚焦/选中单位的光环色（【依据】Art Bible §2.2 选中描边青 #49D9D6，与描边链路同源）。</summary>
        public static Color32 SelectedMarkerColor()
        {
            return FromHex(0x49D9D6);
        }

        // ==================================================================
        // 颜色常量（爆炸/水花；【依据】Art Bible 调色板 + 【AI 提案】明度微调）
        // ==================================================================

        /// <summary>爆炸核心（黄昏亮面暖白，Art Bible §2.1 天空-黄昏亮面 #F2B27A 提亮）。</summary>
        public static Color32 ExplosionCoreColor() => FromHex(0xFFD9A8);

        /// <summary>爆炸外焰（岩浆中间调 #FF7A1A，Art Bible §2.1）。</summary>
        public static Color32 ExplosionFireColor() => FromHex(0xFF7A1A);

        /// <summary>火花（强调金 #FFC24B，Art Bible §2.2）。</summary>
        public static Color32 SparkColor() => FromHex(0xFFC24B);

        /// <summary>烟雾（岩石中间调去饱和，【AI 提案】）。</summary>
        public static Color32 SmokeColor() => FromHex(0x8C8A86);

        /// <summary>木屑（木材中间调 #A67B42，Art Bible §2.1）。</summary>
        public static Color32 WoodDebrisColor() => FromHex(0xA67B42);

        /// <summary>沙尘（沙地中间调 #C4A76A，Art Bible §2.1）。</summary>
        public static Color32 SandColor() => FromHex(0xC4A76A);

        /// <summary>水花飞沫（冷白，【AI 提案】；海水亮面 #4DA6D9 提亮到近白）。</summary>
        public static Color32 SplashDropletColor() => FromHex(0xDFF2FF);

        /// <summary>水沫（浪花白，Art Bible §2.1 海水组提亮，【AI 提案】）。</summary>
        public static Color32 SplashFoamColor() => FromHex(0xEAF6FF);

        /// <summary>涟漪（= 正午天空地平线色 #BFE3F5，Art Bible §2.1，【依据】）。</summary>
        public static Color32 RippleColor() => FromHex(0xBFE3F5);

        /// <summary>冲击波（暖白，与爆炸核心同源）。</summary>
        public static Color32 ShockwaveColor() => FromHex(0xFFD9A8);

        // ==================================================================
        // 小工具
        // ==================================================================

        /// <summary>0xRRGGBB → Color32（工程为 Gamma 色彩空间，sRGB 十六进制直接归一化即可，
        /// 见 `PirateSurface.shader` 头注释的色空间说明）。</summary>
        public static Color32 FromHex(int rgb, byte alpha = 255)
        {
            return new Color32(
                (byte)((rgb >> 16) & 0xFF),
                (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF),
                alpha);
        }

        /// <summary>确定性哈希（不依赖 <c>Random</c>，保证同参数每次生成同样的粒子分布，便于测试与复现）。</summary>
        public static int Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 2147483647;
                h = (h ^ (h >> 13)) * 1274126177;
                return h ^ (h >> 16);
            }
        }

        /// <summary>把哈希值映射到 [0,1)。</summary>
        public static float Hash01(int x, int y, int seed)
        {
            return (Hash(x, y, seed) & 0x7FFFFFFF) / 2147483648f;
        }
    }
}
