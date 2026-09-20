# PointCursor · 离线划词发音

在 Windows 中双击或拖选一个英文单词，松开鼠标后使用 **Microsoft Zira Desktop** 发音。主要适配 Obsidian，也可用于暴露辅助功能选区的文本控件和浏览器。

## 开始使用

1. 把 `dist/PointCursor.zip` 解压到空文件夹，双击 `PointCursor.exe`。ZIP 根层就是程序目录。
2. 回到文档，双击或拖选 `hello`、`English` 等英文单词。
3. 自动取词失败时，可保持选中并明确按 **Ctrl+C** 补充发音。
4. 关闭设置窗口后程序仍在托盘运行；双击托盘图标打开设置，选择“退出程序”才会彻底退出。

仅接受单个英文词，支持 `don't`、`well-known`；整句、中文、数字、网址和密码控件会跳过。单击或悬停不会触发。同词重新选择可以重听，每次明确 Ctrl+C 也可重听。完成划词后立即切窗、点击别处或在 Obsidian 高亮，不会主动撤销已提交请求。

## 语音与设置

- 语音固定为 **Microsoft Zira Desktop**，不再提供声线选择或其他语音后端。
- 语速、音量和“复制后发音”即时生效；默认语速 -1、音量 85%、复制发音开启。
- 暂停同时停用自动取词和复制发音；仍可手动试听 `hello`。
- 使用 `PointCursor.exe --quiet` 可直接进入托盘；不自动设置开机启动。
- 旧设置文件可直接沿用：忽略旧的 `Voice` 字段，保留语速、音量和复制开关；下次保存时移除旧字段。

## 运行要求与边界

Windows x64、.NET Framework 4.8、已安装并启用的 Microsoft Zira Desktop、可用音频设备。没有 Zira 时会明确提示，不自动改用其他声音。程序不会卸载或修改系统中的其他语音。

运行完全离线，无账号、密钥、Python、Node.js、模型、浏览器扩展或 Obsidian 插件依赖。辅助功能支持取决于目标应用；PDF、图片、管理员权限窗口、特殊插件和混合 DPI 尚未完整验证，见 `COMPATIBILITY.md`。独立单词合成不能结合上下文区分同形异音词。

设置仅存于 `%LOCALAPPDATA%\PointCursor\settings.xml`。不保存单词历史或按键，不访问笔记文件，不主动复制或改写剪贴板；设置页临时显示最近发音词，退出后清除。复制发音只读取用户真实 Ctrl+C 后产生的新剪贴板短文本，并检查来源和密码状态。

## 故障排查

先打开设置，试听 `hello`：

- 试听也无声：确认 Zira 已安装、音量不为 0、Windows 默认播放设备正常。安装语音后重启 PointCursor。
- 试听正常但自动取词失败：检查是否暂停、是否只选了一个词；可尝试明确 Ctrl+C。目标控件不提供选区时仍可能无法读取。
- 运行以下诊断，只在内存中合成固定 `hello`，不播放、不联网、不改设置：

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
| `InputMonitor` / `SelectionGesture` | 专用消息线程监听输入；独立状态机识别双击和拖选，按事件时间计时，仅保留最新完成手势 |
| `SelectionClient` | 后台启动及串行访问辅助进程，约 900ms 读取超时；取消、崩溃、坏响应后自动重建 |
| `SelectionReader` / `LegacySelection` | Chromium/Electron 优先 IA2，其余 UIA；以来源窗口和手势类型读取真实选区或重建范围，分别检查密码与进程边界 |
| `Core` | 单词过滤、请求代次、复制门控和设置 |
| `SpeechService` / `SapiSpeechEngine` | 固定 Zira，在后台合成为内存 PCM，新请求取消旧请求 |
| `AudioOutput` / `PcmAudio` | WASAPI 播放完整采样；设备首次打开有 150ms 静音准备，普通词不附加此延迟 |
| `App` / `SettingsForm` | 托盘、界面和事件编排 |

双击类型随请求传递，读取器不再用“坐标相差不超过 1 像素”猜测双击。来源窗口在鼠标按下时锁定，避免松开后立即切窗的竞态。输入钩子不承担界面、磁盘、合成或进程操作；无结果只做有限重试，仍受辅助进程超时约束。

接口参考：[Windows 低级鼠标钩子](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)、[UI Automation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.textpattern.getselection)、[IAccessible2](https://accessibility.linuxfoundation.org/a11yspecs/ia2/docs/html/interface_i_accessible_text.html)。
