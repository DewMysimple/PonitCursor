# PointCursor 工程 Agent 指令与记忆协议

本文件是本仓库的 Agent 工作入口。每次开始工作时，先读取本文件，再按顺序读取 `wiki_memory/当前状态/项目概览.md`、`wiki_memory/当前状态/系统架构.md`、`wiki_memory/当前状态/当前约束.md`、`wiki_memory/当前状态/当前待办.md`，然后按任务读取相关决策、知识页和最近日志。

## 工程记忆

- `wiki_memory/当前状态/` 只记录当前有效事实；一个主题只能有一个 active 页面。
- `wiki_memory/决策/` 记录已经采用、会影响后续工作的架构和工程决策；被替代的决策保留并标为 `superseded`。
- `wiki_memory/知识/` 记录稳定的模块、流程、规范和运维说明。
- `wiki_memory/日志/` 是追加式任务历史。完成实质任务后新增 `YYYY-MM-DD-任务标题.md`，并运行 `python wiki_memory/工具/memory_lint.py index`。
- 长期页面必须使用 YAML frontmatter，并通过 `source_logs` 或正文链接指向来源日志；不要记录密钥、令牌、完整聊天或内部推理。
- 记忆体检使用 `python wiki_memory/工具/memory_lint.py check`，只报告问题，不批量删除历史或覆盖未经确认的 active 内容。

## PointCursor 修改与 Git 约束

- 用户要求后续每一次完整对话产生的工程修改都必须形成一次 Git 提交，并推送到已配置的远程仓库。
- 完成代码、文档、构建或测试修改后，先执行必要的验证，再运行 `git status`、`git diff --check`，创建清晰的中文或英文提交信息，并执行 `git push`。
- 推送前不得提交设置文件、用户笔记、测试浏览器缓存、账号信息、令牌或其他本机私有数据；构建产物默认由 `.gitignore` 排除，发布包通过构建脚本生成。
- 若推送失败，保留本地提交，记录失败原因和下一步，不伪造“已同步”状态。
- 每次实质任务完成时执行“记忆同步”：更新必要的当前状态或知识页，新增一篇日志，并刷新工作日志 MOC。

## 开发边界

- 目标运行环境为 Windows x64、.NET Framework 4.8、WinForms；不引入需要联网下载的依赖。
- PointCursor 只读前台窗口的辅助功能选区和用户明确复制的新剪贴板文本，不修改笔记、不主动模拟复制、不保存单词历史。
- 选择读取失败时必须快速超时并允许新请求取消旧请求；密码控件、自身窗口和非单个英文词必须保守跳过。
- 修改实现时优先保持与 `README.md`、`COMPATIBILITY.md` 及 `wiki_memory/` 的描述一致。
