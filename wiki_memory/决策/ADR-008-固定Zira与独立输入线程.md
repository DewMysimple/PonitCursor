---
type: decision
status: superseded
kind: architecture
importance: high
updated: 2026-09-23
topic: zira-only-and-isolated-input
source_logs:
  - "[[日志/2026-09-20-收敛Zira语音并修复输入与取词稳定性]]"
supersedes:
  - "[[决策/ADR-004-内置Kokoro离线语音后端]]"
---

# ADR-008｜固定 Zira 与独立输入线程

语音范围已由 [[决策/ADR-009-限定美式与英式系统语音]] 替代；该后续决策继续保留本页的独立输入线程、手势快照、Reader 生命周期与轻量依赖原则。以下保留原始决策。

用户明确只保留 Microsoft Zira Desktop，并要求改善双击稳定性和可维护性。

## 已采用决策

1. 删除 Kokoro 引擎、Node/ONNX/模型/声线、完整性工具、LFS 属性和相关测试；UI 不再选择声音，配置不再保存 Voice。保留 Zira 合成和已验证的 WASAPI 完整采样输出。
2. 低级钩子使用专用 STA 消息循环，回调只识别/投递最新意图；纯 `SelectionGesture` 状态机独立测试。双击按两次按下时间判断，第二下长按仍成立。来源 HWND 在按下时锁定，不依赖稍后前台状态。
3. 双击类型传递至 UIA/IA2 范围重建，替代按 1px 位移猜测。Chromium/Electron 优先经过保护的 IA2；UIA 包装层失败不阻断其他独立受保护路径。
4. Reader 进程操作移出 UI，生命周期串行化。900ms 超时继续有效，坏响应、取消、崩溃可恢复；无结果只有限重试。
5. 新增界面卡顿、长按、连续双击、旧配置、故障恢复回归；当前历史决策 ADR-006/007 的来源范围、高亮与扁平归档原则继续有效，语音及协议扩展以本决策为准。

## 依据与取舍

微软说明低级钩子在安装线程上处理，超时可被静默移除；这与把 UI/磁盘操作放在同线程有冲突。参见 [LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)。独立线程降低此风险，但不承诺操作系统极端调度或所有应用均可读取。

删除多语音后，不再具备缺少 Zira 时的其他声音兜底；明确提示用户安装 Zira。不会卸载系统声音或改写 Git 历史。

短期连续测试不能替代多小时真实驻留；尚未验证的设备和插件环境继续列在当前待办。
