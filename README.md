# PointCursor · 离线划词发音

在 Windows 中用鼠标选中一个英文单词，松开后自动播放英文读音。针对 Obsidian 笔记做了兼容处理，也可用于支持选区读取的其他软件。

## 开始使用

1. 解压 `PointCursor-Windows-x64.zip`，保留整个 `PointCursor` 文件夹。
2. 双击 `PointCursor.exe`。默认启用划词发音；可使用 Microsoft Zira 等 Windows 系统语音，也可在设置中选择内置 Kokoro 神经语音。
3. 回到 Obsidian，**双击或拖选** `hello`、`English` 等英文单词，松开鼠标即可听到读音。
4. 未能自动取词时，保持选中，按 **Ctrl+C**。关闭设置窗口后，程序继续在系统托盘运行；托盘图标可能在任务栏右侧的隐藏图标区域。

只选中单个词；整句、中文、数字和网址会被忽略。`don't`、`well-known` 可读。单击放置光标、鼠标悬停、文档里原有的黄色标记不会触发发音。重新选中同一个词可以重听。

## 设置

- 双击托盘图标：打开中文设置窗口。
- **暂停／恢复**：同时暂停／恢复自动取词和复制发音；暂停时仍可手动试听。
- **语音、语速、音量**：即时生效并保存在本机。语音列表包含本机 Windows 英文语音，以及 20 个美式、8 个英式 Kokoro 声线。默认语速略慢，音量 85%。
- **复制后发音**：默认开启，可单独关闭。只在你按 Ctrl+C 后读取新复制的短文本，不模拟复制、不替换剪贴板。同一次选词已经自动发音后，再复制不会重复读。
- **退出程序**：彻底退出并移除托盘图标。没有设置开机自启动；启动时使用 `PointCursor.exe --quiet` 可直接进入托盘。

## 运行要求与边界

- Windows 11 x64、.NET Framework 4.8 和可用音频设备。Windows 系统语音是可选项；便携包已经包含 Kokoro 英文语音。
- 运行不需要网络、账号、API 密钥、Python、另行安装 Node.js、浏览器扩展或 Obsidian 插件；无管理员权限要求。Kokoro 使用随程序发布的 Node.js、ONNX Runtime、量化模型和声线文件，运行时明确禁止远程模型下载。
- 默认 Zira 不可用时，可选择 Kokoro 或其他已安装的英文系统语音。安装新的 Windows 系统语音后需要重启 PointCursor 才会刷新列表。
- 选择 Kokoro 后会在后台加载约 88 MiB 模型并静默预热；刚启动时可能需要约 1～3 秒，之后复用模型。新选词立即使旧音频失效，只保留最新待合成词；普通旧推理完成后丢弃结果，取消后仍卡住超过约 1 秒才终止工作进程。
- 无需改变 Obsidian 的启动参数。浏览器第一次建立辅助功能信息时可能稍慢，未成功时可再选一次或按 Ctrl+C。
- 应用对辅助功能接口的支持决定兼容性。管理员权限软件、扫描图片、PDF、特殊插件视图和多屏不同缩放比例未作完整适配；详见 `COMPATIBILITY.md`。
- 发音由所选 Windows 或 Kokoro 后端按独立单词合成；同形异音词不会结合上下文判断读音。跨多个格式片段的选区会保守跳过，避免把整句里的某一部分当作单词。

## 语音故障排查

在解压后的程序目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\diagnose.ps1
```

诊断会校验 290 个运行时文件的大小和 SHA-256，并用固定的 `hello` 测试 Windows 与 Kokoro 合成，不播放声音、不修改设置。缺失或损坏会指出具体文件。源码环境先执行 `git lfs pull`，再重新构建；便携包用户重新完整解压，避免只替换 EXE。

如果仍听到词头缺失，先在 Windows 中确认默认播放设备，再分别试听 Windows 和 Kokoro。程序的系统输出回录已经验证词头完整，但回录不能证明蓝牙耳机、显示器音箱等硬件最终发出的声音；设备类型和容易复现的词有助于进一步定位。

## 本地数据

设置文件：`%LOCALAPPDATA%\PointCursor\settings.xml`。仅保存语音、语速、音量和复制发音开关。

不保存选中文字、阅读历史或按键记录，不访问笔记文件，不联网。设置窗口暂时显示最近一次发音的单词，退出后清除。密码控件会被跳过。

取词辅助进程与主程序隔离。第三方软件取词卡住时，约 900 毫秒后终止该次读取，后续请求会重新启动辅助进程，不阻塞鼠标或设置窗口。

## 从源码构建

在项目目录执行 Windows PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test -Package
```

也可直接双击仓库根目录的 `buildStart.cmd`，默认重新构建并生成下列两个发布产物；从命令行运行 `buildStart.cmd -Test` 时会额外执行完整自动测试。

使用 Windows 自带的 .NET Framework C# 编译器和系统程序集，不下载 NuGet 或 npm 包。Kokoro 运行时位于 `third_party\kokoro-runtime`，模型、声线、Node.js 和原生 ONNX Runtime 由 Git LFS 管理，JavaScript 编译产物也随仓库提交；因此首次克隆源码前需安装 Git LFS 并执行 `git lfs pull`，再确保整个运行时目录完整。构建会对照 `assets.tsv` 检查全部文件；运行时文本固定 LF 换行，避免换机检出后的哈希变化。维护者有意更新运行时后，运行 `python tools/runtime_manifest.py` 刷新清单并一同提交。

- 可运行目录：`dist\PointCursor`
- 便携压缩包：`dist\PointCursor-Windows-x64.zip`
- 自动测试与桌面测试程序：`build\tests`
- 测试截图和验证结果：`build\qa`

桌面测试说明见源码中的 `tests\README.md`。自动测试不操作已有笔记；桌面集成测试使用独立测试窗口或测试库。

## 实现概要

C# 5 / .NET Framework 4.8 / WinForms；UI Automation 优先读取选区，Obsidian 等 Electron 软件补充 MSAA / IAccessible2。Windows 语音由 `System.Speech` 在后台合成为内存 PCM；Kokoro 由独立本地工作进程执行音素转换和 ONNX 推理，生成 PCM16 临时 WAV，读入内存后立即删除。两者共用 24 kHz 单声道 WASAPI 播放通道，保留全部原始采样，不裁剪弱辅音或词头静音。启用期间通道持续输出静音待命；新开设备时先输出约 150 毫秒静音，普通选词不额外添加此等待。暂停会释放播放设备，恢复时重新准备。全局输入监听只识别选择动作与 Ctrl+C，所有耗时取词在独立进程中完成。

Kokoro 模型、声线和运行时的来源、版本、许可证及哈希见 `third_party\kokoro-runtime\THIRD-PARTY-NOTICES.md`。

接口参考：[微软 UI Automation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.textpattern.getselection)、[IAccessible2 文本接口](https://accessibility.linuxfoundation.org/a11yspecs/ia2/docs/html/interface_i_accessible_text.html)。
