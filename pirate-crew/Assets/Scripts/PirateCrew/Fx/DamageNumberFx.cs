using UnityEngine;

namespace PirateCrew.Fx
{
    /// <summary>
    /// 伤害数字：程序化点阵字形 + 上浮淡出（Art Bible §7.2「伤害数字 粗体无衬线，弹出后上飘淡出」）。
    ///
    /// 【中文字体无关】字形由 <see cref="FxDigitFont"/>（3×5 点阵）生成，只用到 0-9 与 '-'/'+'/'.'，
    /// 不依赖 TMP / legacy 字体 / `Assets/Scripts/UI` 波次的任何字体资产（版权与耦合双重纪律）。
    ///
    /// 【分档】颜色与字号由 <see cref="FxRules.TierFor"/> 按"伤害 / 最大生命"占比切四档：
    /// 淡白 → 金 → 橙 → 危险红（Art Bible §2.1/§2.2/§2.3 调色板）。
    /// 描边固定 `#2A2A2A`（Art Bible §2.5：场景叠加文字必须带 2px 深色描边——本实现用 1 个字体像素
    /// 的扩张，在 4× 放大后等效约 4px，留足可读性）。
    ///
    /// 【预算】每个数字 1 个 Quad / 1 个 DrawCall；贴图按"文本+颜色"缓存（上限 64 张，见 FxTextures）。
    /// </summary>
    public static class DamageNumberFx
    {
        /// <summary>每个字模像素放大的屏幕像素数（贴图分辨率；4 → 字形高 5×4=20px + 描边）。</summary>
        const int PixelsPerGlyphPixel = 4;

        /// <summary>在指定世界坐标弹出一个伤害数字。</summary>
        public static void Play(Vector3 worldPosition, float damage, int maxHealth)
        {
            if (!Application.isPlaying)
                return;

            int rounded = Mathf.Max(0, Mathf.RoundToInt(damage));
            string text = rounded.ToString();

            DamageTier tier = FxRules.TierFor(damage, maxHealth);
            Color32 fill = FxRules.TierColor(tier);
            Color32 outline = FxRules.FromHex(0x2A2A2A);   // Art Bible §2.5 场景文字描边

            Texture2D texture = FxTextures.GetDamageNumber(text, PixelsPerGlyphPixel, fill, outline);
            if (texture == null)
                return;

            float height = FxRules.DamageNumberBaseHeight * FxRules.TierScale(tier);
            float width = height * texture.width / Mathf.Max(1, texture.height);

            FxSpriteFx sprite = FxPool.RentSprite(additive: false, billboard: true);
            sprite.SetTexture(texture);
            // 起点放大 25% 再回到 1.0：读作"蹦出来"，比线性放大更有打击感。
            sprite.PlayOnce(
                worldPosition,
                new Vector2(width * 1.25f, height * 1.25f),
                new Vector2(width, height),
                new Color(1f, 1f, 1f, 1f),
                new Color(1f, 1f, 1f, 0f),
                FxRules.DamageNumberLifetime,
                FxRules.DamageNumberRise);
        }
    }
}
