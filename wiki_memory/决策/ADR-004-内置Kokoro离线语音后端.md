---
type: decision
status: active
kind: architecture
importance: high
updated: 2026-09-09
topic: embedded-kokoro-offline-speech
source_logs:
  - "[[日志/2026-09-09-集成Kokoro离线语音]]"
supersedes: "[[决策/ADR-001-本地系统语音与桌面进程架构]]"
---

# ADR-004｜内置 Kokoro 离线语音后端

## 状态

`active`

## 背景

PointCursor 原先只列出 Windows 已安装的英文 SAPI 语音。用户希望把本地 `NVIDIA/kokoro` 仓库的声线加入工程，同时继续满足便携、离线、无需账号、无需管理员权限和不阻塞托盘 UI 的约束。该本地仓库只有声线张量和推理源码，没有可由 `System.Speech` 直接加载的 SAPI 语音，也缺少 ONNX 模型。

## 决策

保留 Windows `System.Speech` 后端，并新增独立的 Kokoro 工作进程。工程随包发布 Node.js x64、裁剪后的 Transformers.js/ONNX Runtime/phonemizer、Kokoro-82M int8 ONNX 模型和原始声线；C# 主进程通过标准输入输出发送单词、声线、语速、音量和临时 WAV 路径。工作进程显式禁止远程模型，C# 播放生成的 WAV 并在后续请求或退出时清理。

设置页只暴露与产品范围一致的 28 个英语声线（20 个美式、8 个英式），同时保留本机 SAPI 英文语音。模型和二进制用 Git LFS 管理，构建只复制已检出的资产，不执行 NuGet、npm 或模型下载。

## 理由

- Kokoro `.bin` 文件只是风格张量，必须与模型、音素转换和 ONNX 推理结合，不能伪装成可直接选择的系统语音。
- 独立工作进程隔离约 88 MiB 模型及原生 ONNX Runtime；未完成的旧推理可通过终止进程取消，不阻塞 WinForms 消息线程。
- 内置运行时维持最终用户零安装和断网可用；保留 SAPI 后端提供低资源、快速启动的兜底路径。

## 影响

- 正面影响：无需安装系统语言包即可获得多种自然英语声线；运行时不上传文本、不下载模型。
- 代价或风险：源码与发布包显著增大；首次 Kokoro 发音需要加载模型，内存和 CPU 占用高于 SAPI；Node.js 与 ONNX Runtime 需要独立跟踪安全和兼容更新。
- 维护要求：发布必须保留完整 `Kokoro/` 目录和第三方许可，Git LFS 上传失败时不得声称远程已同步。

## 验证方式

运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test -Package`，确认 64 项自动检查、C# 桥接的 `af_heart`/`bf_emma` 实际合成与播放、独立工作进程冒烟 WAV、发布目录和 ZIP 均通过；检查工作进程退出后没有残留 Node 进程或临时 WAV。

## 来源

- [[日志/2026-09-09-集成Kokoro离线语音|集成 Kokoro 离线语音日志]]
