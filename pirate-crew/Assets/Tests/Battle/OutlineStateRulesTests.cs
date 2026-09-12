using NUnit.Framework;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="OutlineStateRules"/> 测试：§4.5 悬停/选中描边档位的纯逻辑判定。
    /// </summary>
    [TestFixture]
    public class OutlineStateRulesTests
    {
        [Test]
        public void NoState_And_Dead_ResolveToNone()
        {
            Assert.AreEqual(OutlineStateRules.None, OutlineStateRules.Resolve(alive: true, selected: false, hovered: false));
            Assert.AreEqual(OutlineStateRules.None, OutlineStateRules.Resolve(alive: false, selected: false, hovered: false));
        }

        [Test]
        public void Hover_Wins_WhenNotSelected()
        {
            Assert.AreEqual(OutlineStateRules.Hover, OutlineStateRules.Resolve(alive: true, selected: false, hovered: true));
        }

        [Test]
        public void Selected_Wins_OverHover()
        {
            // 两态合并进同一材质后必须二选一；选中信息量更大（对应 §4.5 corners 的显示条件）。
            Assert.AreEqual(OutlineStateRules.Selected, OutlineStateRules.Resolve(alive: true, selected: true, hovered: false));
            Assert.AreEqual(OutlineStateRules.Selected, OutlineStateRules.Resolve(alive: true, selected: true, hovered: true));
        }

        [Test]
        public void Dead_Unit_HasNoOutlineFeedback()
        {
            // 死亡后 PirateBase.Kill() 会清掉 selected/hovered，这里再兜一层：
            // 即使状态位残留也不应画出描边。
            Assert.AreEqual(OutlineStateRules.None, OutlineStateRules.Resolve(alive: false, selected: true, hovered: false));
            Assert.AreEqual(OutlineStateRules.None, OutlineStateRules.Resolve(alive: false, selected: true, hovered: true));
            Assert.AreEqual(OutlineStateRules.None, OutlineStateRules.Resolve(alive: false, selected: false, hovered: true));
        }

        [Test]
        public void StateValues_MatchShaderContract()
        {
            // PirateOutline.shader 的 OutlineColorForState()/OutlineWidthForState() 按 0/1/2 分支，
            // 改这三个常量等于改 shader 契约，必须同步改 shader 与 docs/描边Shader调试.md。
            Assert.AreEqual(0, OutlineStateRules.None);
            Assert.AreEqual(1, OutlineStateRules.Hover);
            Assert.AreEqual(2, OutlineStateRules.Selected);
        }
    }
}
