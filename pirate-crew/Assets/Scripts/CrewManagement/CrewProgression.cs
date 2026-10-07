namespace PirateCrew.CrewManagement
{
    /// <summary>
    /// 船员经验账本（每个船员 id → 累计经验）。纯 C#，无头验证台可断言。
    ///
    /// 【定位】**仅作存档兼容保留**：船员没有经验/等级/成长系统（用户口径），
    ///   本类不再有升级曲线与经验发放；运行时既不写入也不读出等级，
    ///   只在 <see cref="CrewManagementSaveCodec"/> 存/读档时原样搬运（旧档兼容，字段不删）。
    /// </summary>
    public sealed class CrewProgression
    {
        readonly System.Collections.Generic.Dictionary<string, int> _xp =
            new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal);

        /// <summary>该船员的累计经验（存档兼容字段）；未知船员返回 0。</summary>
        public int GetXp(string crewId)
        {
            if (string.IsNullOrEmpty(crewId))
                return 0;

            return _xp.TryGetValue(crewId, out int xp) ? xp : 0;
        }

        /// <summary>直接写入累计经验（读档用）；负值夹到 0。</summary>
        public void SetXp(string crewId, int totalXp)
        {
            if (string.IsNullOrEmpty(crewId))
                return;

            _xp[crewId] = totalXp < 0 ? 0 : totalXp;
        }

        /// <summary>已记录经验的船员 id 列表（存档读写用）。</summary>
        public System.Collections.Generic.IReadOnlyCollection<string> CrewIds => _xp.Keys;

        /// <summary>清空全部经验。</summary>
        public void Reset()
        {
            _xp.Clear();
        }
    }
}
