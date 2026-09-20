using PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.Fx
{
    /// <summary>
    /// 「轮到谁」的地面光环（回合/选中反馈）。
    ///
    /// 【为什么需要】回合制战斗最容易"不知道现在轮到谁"。一个跟着当前行动单位的地面光环
    /// 把"轮到它了"钉在战场上（比只在 HUD 上写行字直观），也与"选中描边青"
    /// （Art Bible §2.2 / §6.3）形成颜色上的一致语义。
    ///
    /// 【两种颜色语义】
    ///   · <c>turn_started</c>（team + PanTarget）→ **队伍色**（红 #FF3A29 / 蓝 #3366FF，Art Bible §2.2），
    ///     读作"这一队该动了"；
    ///   · <c>camera_focus_requested</c>（玩家点选某个单位）→ **选中青 #49D9D6**，
    ///     与描边链路的选中色同源（Art Bible §2.2）。
    ///
    /// 【实现】一个 faceUp 的 Quad（<see cref="FxSpriteFx"/>，非池化、常驻），由 <see cref="FxRoot"/>
    /// 每帧 <see cref="Tick"/> 驱动位置与脉动。不池化是因为它整局只存在一个。
    /// </summary>
    internal sealed class TurnMarkerFx
    {
        FxSpriteFx _ring;
        Transform _target;
        Color _color = Color.white;
        float _phase;
        Vector3 _lastPosition;
        bool _hasPosition;

        /// <summary>当前是否可见（有活着的目标）。</summary>
        public bool IsVisible { get; private set; }

        /// <summary>当前跟随的目标（可能为 null）。</summary>
        public Transform Target => _target;

        /// <summary>绑定常驻光环对象（由 FxRoot 创建）。</summary>
        public void Bind(FxSpriteFx ring)
        {
            _ring = ring;
            if (_ring != null)
            {
                _ring.SetTexture(FxTextures.Get(FxTextureKind.Ring));
                _ring.gameObject.SetActive(false);
            }
            IsVisible = false;
        }

        /// <summary>跟随某目标显示光环。</summary>
        public void Show(Transform target, Color color)
        {
            if (_ring == null || target == null)
            {
                Hide();
                return;
            }

            _target = target;
            _color = color;
            IsVisible = true;
            _hasPosition = false;
            _ring.gameObject.SetActive(true);
        }

        /// <summary>隐藏光环（但保留对象，供下一回合复用）。</summary>
        public void Hide()
        {
            _target = null;
            IsVisible = false;
            if (_ring != null)
                _ring.gameObject.SetActive(false);
        }

        /// <summary>每帧刷新位置与脉动。</summary>
        public void Tick(float deltaTime)
        {
            if (!IsVisible || _ring == null)
                return;

            if (_target == null)
            {
                Hide();
                return;
            }

            Vector3 position = _target.position
                               - Vector3.up * LevelGeometry.UnitPivotHeight
                               + Vector3.up * 0.04f;   // 抬高量 ×2（格 1→2 单位）
            _ring.SetPosition(position);
            _hasPosition = true;
            _lastPosition = position;

            _phase += deltaTime;
            float pulse = Mathf.Sin(_phase * FxRules.TurnMarkerPulseHz * Mathf.PI * 2f);
            float diameter = FxRules.TurnMarkerDiameter * (1f + 0.07f * pulse);
            float alpha = FxRules.TurnMarkerAlpha * (0.70f + 0.30f * (pulse * 0.5f + 0.5f));

            _ring.SetSize(new Vector2(diameter, diameter));
            _ring.SetColor(new Color(_color.r, _color.g, _color.b, alpha));
        }

        /// <summary>销毁常驻对象（FxRoot 销毁时调用）。</summary>
        public void Dispose()
        {
            if (_ring != null)
                Object.Destroy(_ring.gameObject);
            _ring = null;
            IsVisible = false;
        }

        /// <summary>最近一次摆放的世界坐标（调试/测试用）。</summary>
        public Vector3 LastPosition => _hasPosition ? _lastPosition : Vector3.zero;
    }
}
