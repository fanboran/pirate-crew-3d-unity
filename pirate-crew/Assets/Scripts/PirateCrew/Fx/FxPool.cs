using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Fx
{
    /// <summary>
    /// 特效对象池（粒子爆发体 + 四边形特效体）。
    ///
    /// 【为什么池化】爆炸/命中/水花在一个回合内可发生多次，每次 <c>new GameObject</c> + 建
    /// ParticleSystem 会产生明显的 GC 尖刺（ParticleSystem 组件创建并不便宜）。池化后热路径
    /// 只有 SetActive / Play，不分配堆内存。
    ///
    /// 【上限与回收】按 `FxRules.MaxPooledSystems` / `MaxPooledSprites` 封顶；超额归还时直接
    /// <c>Destroy</c>，避免场景里堆着几十个空对象。池容器 <c>[FxPool]</c> 挂在
    /// <c>DontDestroyOnLoad</c>，跨场景复用；容器被销毁（退出播放）后，栈里的"假 null"引用
    /// 由 <c>Rent</c> 时的 <c>== null</c> 检查自然清理。
    ///
    /// 【ECall 边界】本类触碰 <c>GameObject</c>，只在 Unity 运行时里用，不进无头断言。
    /// </summary>
    public static class FxPool
    {
        static Transform _root;
        static readonly Stack<FxParticles> ParticlePool = new Stack<FxParticles>();
        static readonly Stack<FxSpriteFx>[,] SpritePool = new Stack<FxSpriteFx>[2, 2];
        static bool _initialized;

        static Transform Root
        {
            get
            {
                if (_root != null)
                    return _root;

                var go = new GameObject("[FxPool]");
                Object.DontDestroyOnLoad(go);
                _root = go.transform;
                return _root;
            }
        }

        static void EnsureInit()
        {
            if (_initialized)
                return;
            _initialized = true;
            for (int blend = 0; blend < 2; blend++)
            {
                for (int billboard = 0; billboard < 2; billboard++)
                    SpritePool[blend, billboard] = new Stack<FxSpriteFx>();
            }
        }

        /// <summary>借一个粒子爆发体。</summary>
        public static FxParticles RentParticles()
        {
            EnsureInit();

            while (ParticlePool.Count > 0)
            {
                FxParticles pooled = ParticlePool.Pop();
                if (pooled != null)
                {
                    pooled.gameObject.SetActive(true);
                    return pooled;
                }
            }

            var go = new GameObject("FxParticles");
            go.transform.SetParent(Root, false);
            return go.AddComponent<FxParticles>();
        }

        /// <summary>借一个四边形特效体（additive = 加法混合；billboard = 面向相机）。</summary>
        public static FxSpriteFx RentSprite(bool additive, bool billboard)
        {
            EnsureInit();
            Stack<FxSpriteFx> stack = SpritePool[additive ? 1 : 0, billboard ? 1 : 0];

            while (stack.Count > 0)
            {
                FxSpriteFx pooled = stack.Pop();
                if (pooled != null)
                {
                    pooled.gameObject.SetActive(true);
                    return pooled;
                }
            }

            FxSpriteFx created = FxSpriteFx.Create("FxSprite", additive, billboard);
            created.transform.SetParent(Root, false);
            return created;
        }

        /// <summary>归还粒子爆发体。</summary>
        public static void Return(FxParticles particles)
        {
            if (particles == null)
                return;

            particles.PrepareForPool();
            if (ParticlePool.Count >= FxRules.MaxPooledSystems)
            {
                Object.Destroy(particles.gameObject);
                return;
            }

            particles.gameObject.SetActive(false);
            particles.transform.SetParent(Root, false);
            ParticlePool.Push(particles);
        }

        /// <summary>归还四边形特效体。</summary>
        public static void Return(FxSpriteFx sprite)
        {
            if (sprite == null)
                return;

            sprite.PrepareForPool();
            Stack<FxSpriteFx> stack = SpritePool[sprite.IsAdditive ? 1 : 0, sprite.IsBillboard ? 1 : 0];

            if (stack.Count >= FxRules.MaxPooledSprites)
            {
                // 池超额：特效体连同其每实例材质一起销毁（DontSave 材质不显式销毁即成孤儿）。
                sprite.ReleaseForDestroy();
                Object.Destroy(sprite.gameObject);
                return;
            }

            sprite.gameObject.SetActive(false);
            sprite.transform.SetParent(Root, false);
            stack.Push(sprite);
        }

        /// <summary>清空池（场景重载/退出时可选调用；不调用也会由假 null 检查自愈）。</summary>
        public static void Clear()
        {
            EnsureInit();

            while (ParticlePool.Count > 0)
            {
                FxParticles p = ParticlePool.Pop();
                if (p != null)
                    Object.Destroy(p.gameObject);
            }

            for (int blend = 0; blend < 2; blend++)
            {
                for (int billboard = 0; billboard < 2; billboard++)
                {
                    while (SpritePool[blend, billboard].Count > 0)
                    {
                        FxSpriteFx s = SpritePool[blend, billboard].Pop();
                        if (s != null)
                        {
                            s.ReleaseForDestroy();
                            Object.Destroy(s.gameObject);
                        }
                    }
                }
            }
        }
    }
}
