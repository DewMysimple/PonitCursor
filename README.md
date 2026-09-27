# PointCursor · 离线划词发音

在 Windows 中双击或拖选一个英文单词，松开鼠标后使用所选的美式或英式系统语音发音，默认 **Microsoft Zira Desktop**。主要适配 Obsidian，也可用于暴露辅助功能选区的文本控件和浏览器。

## 开始使用

1. 把 `dist/PointCursor.zip` 解压到空文件夹，双击 `PointCursor.exe`。ZIP 根层就是程序目录。
2. 回到文档，双击或拖选 `hello`、`English` 等英文单词。
3. 自动取词失败时，可保持选中并明确按 **Ctrl+C** 补充发音。
4. 关闭设置窗口后程序仍在托盘运行；再次双击 `PointCursor.exe` 或托盘图标，会直接打开并聚焦已有设置窗口。最小化的窗口也会恢复，不再弹出“已在运行”提示；选择“退出程序”才会彻底退出。

仅接受单个英文词，支持 `don't`、`well-known`；整句、中文、数字、网址和密码控件会跳过。单击或悬停不会触发。同词重新选择可以重听，每次明确 Ctrl+C 也可重听。完成划词后立即切窗、点击别处或在 Obsidian 高亮，不会主动撤销已提交请求。

## 语音与设置

- 设置中的语音列表只显示已安装、启用且可供 SAPI 使用的 **美式（en-US）**、**英式（en-GB）** 声音；不显示中文、澳洲、加拿大、印度等其他语言或口音。
- 默认优先 **Microsoft Zira Desktop**。也可选择 Microsoft David Desktop（美式男声）、Microsoft Hazel Desktop（英式女声）等已安装的兼容声音，再按“试听 hello”试听。选择即时生效并保存，切换会取消旧声音的待播请求。
- 语速、音量和“复制后发音”即时生效；默认语速 -1、音量 85%、复制发音开启。
- 暂停同时停用自动取词和复制发音；仍可手动试听 `hello`。
- 首次使用 `PointCursor.exe --quiet` 可直接进入托盘；已有实例时重复启动会唤出设置。不自动设置开机启动。
- 旧设置文件可直接沿用：缺少 `Voice` 时默认 Zira；旧声音已移除或不符合口音范围时，提示并优先改用 Zira，再选其他可用美式/英式声音。语速、音量和复制开关保留。

## 运行要求与边界

Windows x64、.NET Framework 4.8、至少一个已安装并启用的美式或英式 SAPI 5 语音、可用音频设备。没有符合条件的声音时明确提示，并禁用语音选择和试听。

增加系统声音：在 Windows 的“时间和语言 → 语音 → 添加语音”中安装 English (United States) 或 English (United Kingdom)，再重启 PointCursor。只有 `diagnose.ps1` 能枚举到的兼容语音才会出现在列表中；讲述人自然语音不保证可供此接口使用。PointCursor 本身不自动下载、安装或卸载系统组件，不改变系统显示语言。

运行完全离线，无账号、密钥、Python、Node.js、模型、浏览器扩展或 Obsidian 插件依赖。辅助功能支持取决于目标应用；PDF、图片、管理员权限窗口、特殊插件和混合 DPI 尚未完整验证，见 `COMPATIBILITY.md`。独立单词合成不能结合上下文区分同形异音词。

设置仅存于 `%LOCALAPPDATA%\PointCursor\settings.xml`。不保存单词历史或按键，不访问笔记文件，不主动复制或改写剪贴板；设置页临时显示最近发音词，退出后清除。复制发音只读取用户真实 Ctrl+C 后产生的新剪贴板短文本，并检查来源和密码状态。

## 故障排查

先打开设置，试听 `hello`：

- 试听也无声：确认所选系统语音已安装、音量不为 0、Windows 默认播放设备正常。安装或移除语音后重启 PointCursor。
- 试听正常但自动取词失败：检查是否暂停、是否只选了一个词；可尝试明确 Ctrl+C。目标控件不提供选区时仍可能无法读取。
- 运行以下诊断，逐个检查可用美式/英式声音，只在内存中合成固定 `hello`，不播放、不联网、不改设置；加 `-RequireBothAccents` 可要求两类声音都已安装：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\diagnose.ps1
```

程序与 `PointCursor.Reader.exe`、`PointCursor.Core.dll` 和两个 `.config` 文件必须放在一起。升级推荐退出旧版后解压到空目录；本地重新构建会删除输出目录中旧的 Kokoro 资产。

## 构建与验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test -Package
```

也可双击 `buildStart.cmd` 构建打包，或执行 `buildStart.cmd -Test` 加跑自动检查。仅使用系统 C# 编译器和系统程序集，不下载 NuGet、npm 或模型，不再需要 Git LFS 资产。

- 程序目录：`dist/PointCursor`
- 便携包：`dist/PointCursor.zip`
- 测试程序：`build/tests`
- 测试结果与截图：`build/qa`（不提交）

桌面和 Obsidian 测试见 `tests/README.md`，只操作独立合成内容。发布包采用明确文件清单，旧模型或测试数据不会混入 ZIP。

## 维护入口

| 模块 | 职责 |
| --- | --- |
| `InputMonitor` / `SelectionGesture` | 专用消息线程用 Raw Input 接收后台鼠标事件；独立状态机识别双击和拖选，按事件时间计时，仅保留最新完成手势 |
| `SelectionClient` | 后台启动及串行访问辅助进程，约 900ms 读取超时；取消、崩溃、坏响应后自动重建 |
| `SelectionReader` / `LegacySelection` | Chromium/Electron 优先 IA2，其余 UIA；以来源窗口和手势类型读取真实选区或重建范围，分别检查密码与进程边界 |
| `Core` | 单词过滤、请求代次、复制门控和设置 |
| `VoiceCatalog` / `SpeechService` / `SapiSpeechEngine` | 筛选美式/英式语音、恢复选择、后台合成为内存 PCM；请求保存声音快照，新请求取消旧请求 |
| `AudioOutput` / `PcmAudio` | WASAPI 播放完整采样；检测设备时钟停滞并重开，保留尚未播放完的 PCM；设备打开有 150ms 静音准备，普通词不附加此延迟 |
| `App` / `SettingsForm` | 托盘、界面和事件编排 |

双击类型随请求传递，读取器不再用“坐标相差不超过 1 像素”猜测双击。来源窗口在处理鼠标按下事件时锁定，避免松开后立即切窗的竞态。鼠标监听不依赖会被 Windows 静默移除的低级钩子；超过 500ms 的积压输入丢弃，避免恢复后误读旧坐标处的其他窗口，下一次新手势继续生效。Ctrl+C 单独保留轻量键盘钩子，在目标程序复制前取得剪贴板序列号，并每 30 秒更新注册。

输入线程不承担界面、磁盘、合成或进程操作；Obsidian 辅助功能的临时错误与确认的密码控件分别处理，临时错误允许原有的有限重试，仍受约 900ms 的辅助进程超时约束，密码控件继续跳过。音频时钟停滞 750ms 会重开设备，完成的单词不会在设备重开时重复播放。

接口参考：[Windows Raw Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input)、[低级鼠标钩子的超时限制](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)、[UI Automation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.textpattern.getselection)、[IAccessible2](https://accessibility.linuxfoundation.org/a11yspecs/ia2/docs/html/interface_i_accessible_text.html)。
