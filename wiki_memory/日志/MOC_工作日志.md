---
type: moc
status: active
kind: process
importance: high
updated: 2026-09-11
topic: work-log-index
source_logs: []
supersedes: null
---

# 工作日志 MOC

> 单一工作日志索引，按更新时间倒序。任务类型通过 `kind` 元数据区分。

| 时间 | 类型 | 目标 | 状态 | 主题 | 日志 |
| --- | --- | --- | --- | --- | --- |
| 2026-09-11 | bug | 解决完成划词后立即切换窗口或点击别处导致 Microsoft Zira Desktop 无反应的问题，并把后续便携包统一命名为 `PointCursor.zip`，解压后直接得到 `PointCursor` 软件目录。 | archived | lock-selection-gesture-and-rename-package | [[日志/2026-09-11-锁定划词手势与统一发布包名称.md|2026-09-11｜锁定划词手势与统一发布包名称]] |
| 2026-09-11 | bug | 只以 Microsoft Zira Desktop 作为本轮验收语音，提高划词成功率，防止普通光标／窗口／输入变化撤销请求或截断读音，允许同一单词重复 Ctrl+C 发音，并降低鼠标松开到开始取词的等待。 | archived | improve-zira-selection-reliability-and-latency | [[日志/2026-09-11-提升Zira划词稳定性与响应速度.md|2026-09-11｜提升 Zira 划词稳定性与响应速度]] |
| 2026-09-10 | maintenance | 补全仓库根目录的 `buildStart.cmd`，一键生成 `dist` 下的可运行目录和 Windows x64 ZIP。 | archived | add-one-click-build-command | [[日志/2026-09-10-新增一键构建脚本.md|2026-09-10｜新增一键构建脚本]] |
| 2026-09-09 | feature | 把用户提供的本地 NVIDIA Kokoro 声线加入 PointCursor，形成可离线发布和选择的 Kokoro 语音后端。 | archived | integrate-kokoro-offline-voices | [[日志/2026-09-09-集成Kokoro离线语音.md|2026-09-09｜集成 Kokoro 离线语音]] |
| 2026-09-09 | test | 在本地构建 PointCursor，并验证自动检查和便携发布包生成。 | archived | local-build-verification | [[日志/2026-09-09-本地构建验证.md|2026-09-09｜本地构建验证]] |
| 2026-09-09 | maintenance | 按工程记忆模板建立 PointCursor 的可持续记忆，并初始化 Git 远程管理。 | archived | repository-and-memory-initialization | [[日志/2026-09-09-初始化项目与工程记忆.md|2026-09-09｜初始化项目与工程记忆]] |
| 2026-09-09 | bug | 处理跨开发环境的语音加载迟缓、首次播报词头缺失，以及运行时维护问题。 | archived | speech-latency-onset-repair | [[日志/2026-09-09-修复语音延迟与词头播放.md|2026-09-09｜修复语音延迟与词头播放]] |
| 2026-09-09 | bug | 解决其他开发环境克隆仓库后 Kokoro 工作进程缺少 `transformers.node.mjs`、导致 `ERR_MODULE_NOT_FOUND` 和发音失败的问题。 | archived | fix-kokoro-runtime-tracked-files | [[日志/2026-09-09-修复Kokoro运行时提交完整性.md|2026-09-09｜修复 Kokoro 运行时提交完整性]] |

## 使用方式

- 由 `python 工具/memory_lint.py index` 生成或刷新。
- 查询时先阅读当前状态，再按关键词定位日志。
- 历史日志是审计记录，不应直接覆盖当前状态。

## 入口

- [[README|工程 Agent 记忆系统]]
- [[AGENTS|记忆维护协议]]
- [[日志/README|工作日志说明]]
- [[当前状态/项目概览|当前项目概览]]
- [[当前状态/系统架构|当前系统架构]]
