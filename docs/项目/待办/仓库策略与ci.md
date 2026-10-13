# 仓库策略与 CI

> 三项基建已落成为口径：行尾策略、CI、目录归位与命名空间折叠，以仓库现状为准。
> 仍挂着：PlayMode 测试副作用的提交前还原，及其与防回归门禁建设的衔接。

## 详情

### 已落三项（口径，不再挂账）

- **行尾策略**——根目录 [.gitattributes](../../../.gitattributes) 把 Unity 序列化资产
  （.meta/.unity/.prefab/.asset/.mat/.controller）与数据/脚本（.json/.py/.sh）钉为 LF，
  export 产物目录在 .gitignore 逐条锚定，配套 .editorconfig；
  改动该文件前须跑的验证见其头注释与
  [构建与发布手册](../构建与发布手册.md)「仓库卫生」。
- **CI**——[.github/workflows/ci.yml](../../../.github/workflows/ci.yml)：
  只手动触发（创始人裁决），repo-gates（纯文本门禁，不需要 Unity）/ unity-tests
  （EditMode + PlayMode）/ build-windows 三 job 分工；尚未在 GitHub 实跑过，
  首次推送后按该文件头清单核对（步骤在构建与发布手册「CI 配置与 secrets」）。
- **目录归位 + 命名空间折叠（P1-4/P1-5）**——`Assets/Scripts/` 已重排为
  UI / CrewManagement / Campaign / Core / PirateCrew/{Battle, ArtReview, …}，
  命名空间全仓去重；以仓库现状为准。

### PlayMode 测试副作用（仍悬置）

跑 PlayMode 测试会把 `ProjectSettings/EditorSettings.asset` 的
`m_EnterPlayModeOptionsEnabled` 置 1，忘还原就混进提交；当前无常设还原机制，
靠提交前 `git checkout` 该文件兜底。收口方式（门禁自动还原 / 测试侧隔离）待定，
与 [海图退场余项](海图退场余项.md) 的「PlayMode 防回归门禁用例」同线搭车——
仓库级门禁建设时一并收口。
