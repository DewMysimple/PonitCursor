---
type: decision
status: active
kind: architecture
importance: high
updated: 2026-09-11
topic: obsidian-highlight-reconciliation-and-flat-package
source_logs:
  - "[[日志/2026-09-11-兼容Obsidian快速高亮并扁平化发布包]]"
supersedes: null
---

# ADR-007｜Obsidian 高亮重写校正与扁平发布包

## 状态

`active`

## 背景

用户在 Obsidian 完成划词后立即按 Ctrl+Shift+H，高亮命令会把文本改写为 `==word==`。辅助功能读取发生在改写之后时，改写前保存的屏幕端点会映射到新的字符位置；实测实时预览会把 `hello` 截成 `hell`，源码模式会截成 `h`。此外，便携 ZIP 不应再包含额外的同名顶层目录。

## 决策

- 自动手势读取继续优先尊重真实单词选区；需要按手势重建时，同时读取原端点所得片段、当前同行高亮标记上下文和端点所在的辅助功能包围词。
- 仅当原片段本身仍是单词或高亮标记片段，且与包围词前缀／后缀一致时才校正为完整包围词；多词、网址和其他不合法范围仍由单词规则拒绝。
- UIA `TextPatternRange` 和 Electron IA2 两条路径执行相同的校正规则；五字段诊断读取和 Ctrl+C 不启用手势校正。
- `dist/PointCursor.zip` 保持文件名不变，但归档根层直接包含 `PointCursor.exe`、辅助进程、文档、工具和 `Kokoro/`，不再包含额外 `PointCursor/` 包装目录。

## 理由

高亮快捷键表达的是对刚完成选区的格式操作，不是撤销发音。保存的屏幕端点仍是可信手势事实，但在文本插入标记后不能再单独解释为稳定字符偏移。用局部词边界核对片段可以恢复原词，又不会把一个多词范围裁成看似合法的单词。扁平 ZIP 则符合用户对解压即程序根目录的明确要求。

## 验证方式

- 隔离 Obsidian 的实时预览和源码模式真实执行“拖选 `hello` → 立即 Ctrl+Shift+H”，验证 Zira 收到完整 `hello`。
- 单元测试覆盖完整标记、端点截短、源码模式单字符片段和多词拒绝；既有 Obsidian 选区回归继续验证测试笔记哈希不变。
- 检查 `PointCursor.zip` 的每个归档条目，确认不存在 `PointCursor/` 顶层前缀，且根层存在 `PointCursor.exe` 和 `Kokoro/`。

## 来源

- [[日志/2026-09-11-兼容Obsidian快速高亮并扁平化发布包|整改日志]]
