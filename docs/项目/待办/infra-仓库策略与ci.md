# 仓库策略与 CI

> 仓库行尾策略、PlayMode 测试副作用、CI 三项基建待办；下一步定 `.gitattributes`、提交前还原 PlayMode 副作用、建 GitHub Actions CI。

## 详情

### 行尾策略（`.gitattributes`）

**行尾策略（`.gitattributes`）**：`core.autocrlf=true` 且无 `.gitattributes`，Unity YAML 行尾噪音仍在；
属仓库级策略变更，留给用户决定。

### PlayMode 测试副作用

**PlayMode 测试副作用**：跑 PlayMode 会把 `EditorSettings.m_EnterPlayModeOptionsEnabled` 置 1，
提交前需 `git checkout` 还原（本会话门禁脚本已内置）。

### CI

**CI**：GitHub Actions + batchmode EditMode/PlayMode（参考 stick-world 的 CI-DI 文档）。

### 代码组织余项（五审统筹 Track 8 遗留）

**P1-4 目录归位 + P1-5 命名空间折叠**：架构审计余项，163 文件扫荡，必须挑安静窗口做
（并行会话期间动目录/命名空间会大面积撞车）。