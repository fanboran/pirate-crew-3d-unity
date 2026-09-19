using PirateCrew.PirateCrew.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 单位头顶血条（世界空间，图标优先 HUD 的"个人血条"落点）。
    ///
    /// 【为什么从名册挪进世界】左下 12 行名册列表占屏大、信息密度低（UI 审计的"毛坯房"观感）；
    /// 头顶条让"谁残血"在 3D 场景里一眼可辨，名册由顶栏合成血条 + pips 接管后整块退役。
    ///
    /// 【生命周期】由 <see cref="BattleHud"/> 在 <c>battle_started</c> 时对每个单位
    /// <see cref="Attach"/>（UI 模块内自建，不侵入 Battle 的生成路径）；单位死亡淡出销毁。
    /// 【朝向】LateUpdate 只做 yaw 对齐（俯仰不跟随——45° 相机下平贴即可读，
    /// 且避免跟相机的 roll 抖动）；相机缓存 <c>Camera.main</c>，丢失时逐帧重试。
    /// 【滚动】自带指数趋近（<see cref="UiMotionRules.ApproachExponential"/>，
    /// scaled 时间——暂停时血条冻结是正确语义）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OverheadHealthBar : MonoBehaviour
    {
        /// <summary>条宽 / 高（世界单位；相机 30-50 距离档下约 20-30 屏幕像素）。</summary>
        const float Width = 1.15f;
        const float Height = 0.17f;

        /// <summary>条中心相对单位刚体中心的高度（头顶上方，避开帽子）。</summary>
        const float Lift = 1.25f;

        PirateBase _pirate;
        Image _fill;
        float _ratio = 1f;
        float _target = 1f;
        Transform _anchor;
        Camera _camera;

        /// <summary>给单位挂一条头顶血条（重复调用幂等——已挂则只刷颜色）。</summary>
        public static OverheadHealthBar Attach(PirateBase pirate)
        {
            if (pirate == null || pirate.Body == null)
                return null;

            OverheadHealthBar existing = pirate.Body.GetComponentInChildren<OverheadHealthBar>();
            if (existing != null)
            {
                existing.Bind(pirate);
                return existing;
            }

            var root = new GameObject("OverheadHealthBar", typeof(Canvas));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 5;                       // 压过单位本体、不与屏幕 HUD 抢层

            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(Width, Height);
            rect.localScale = Vector3.one;                  // 尺寸由 sizeDelta 直接表达（世界单位）

            var track = root.gameObject.AddComponent<Image>();
            track.sprite = CartoonSpriteFactory.Get(CartoonSpriteFactory.Shape.BarTrack);
            track.type = Image.Type.Sliced;
            track.color = Color.white;
            track.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(rect, false);
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = CartoonSpriteFactory.Get(CartoonSpriteFactory.Shape.Pill);
            fill.type = Image.Type.Sliced;
            fill.color = UiSkin.TeamFill(pirate.TeamIndex);
            fill.raycastTarget = false;
            RectTransform fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var bar = root.AddComponent<OverheadHealthBar>();
            bar._fill = fill;
            bar.Bind(pirate);
            return bar;
        }

        /// <summary>绑定单位（订阅本地事件 + 钉初值）。</summary>
        void Bind(PirateBase pirate)
        {
            if (_pirate != null)
            {
                _pirate.HealthChanged -= OnHealthChanged;
                _pirate.Died -= OnDied;
            }

            _pirate = pirate;
            _anchor = pirate.Body.transform;
            _fill.color = UiSkin.TeamFill(pirate.TeamIndex);
            _target = RatioOf(pirate.Health, pirate.MaxHealth);
            _ratio = _target;
            ApplyRatio();

            pirate.HealthChanged += OnHealthChanged;
            pirate.Died += OnDied;
        }

        void OnHealthChanged(PirateBase pirate, int health, int maxHealth)
        {
            _target = RatioOf(health, maxHealth);
        }

        void OnDied(PirateBase pirate)
        {
            _target = 0f;
            // 死亡后短暂保留滚到 0，再自毁（不留悬空节点）。
            Destroy(gameObject, 1.5f);
        }

        static float RatioOf(int health, int maxHealth)
        {
            if (maxHealth <= 0)
                return 0f;
            return Mathf.Clamp01((float)health / maxHealth);
        }

        void ApplyRatio()
        {
            if (_fill != null)
                _fill.rectTransform.anchorMax = new Vector2(_ratio, 1f);
        }

        void LateUpdate()
        {
            if (_anchor == null)
                return;

            // 跟随单位（含滚动/击退位移），抬到头顶。
            transform.position = _anchor.position + Vector3.up * Lift;

            // 只做 yaw 对齐（见类注释）。
            if (_camera == null)
                _camera = Camera.main;
            if (_camera != null)
            {
                Vector3 dir = _camera.transform.position - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(dir);
            }

            if (!Mathf.Approximately(_ratio, _target))
            {
                _ratio = UiMotionRules.ApproachExponential(
                    _ratio, _target, Time.deltaTime, UiMotionRules.HealthDrainSpeedPerSecond);
                ApplyRatio();
            }
        }

        void OnDestroy()
        {
            if (_pirate != null)
            {
                _pirate.HealthChanged -= OnHealthChanged;
                _pirate.Died -= OnDied;
            }
        }
    }
}
