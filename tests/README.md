# 验证方法

先运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test`。59 项常规测试不需要操作桌面；使用本机英文语音将 `hello` 合成为内存中的 WAV，不会播放测试 WAV。

以下集成测试会临时展示测试窗口、移动鼠标并播放英文。运行时保持桌面空闲，避免同时操作鼠标键盘。仅操作合成测试内容。脚本退出时恢复原来的前台窗口和光标位置；复制测试仅在可完整快照剪贴板时执行，结束时在剪贴板未被外部改变的情况下恢复其内容。

```powershell
python .\tests\desktop_qa.py
python .\tests\browser_qa.py
```

桌面脚本使用独立 RichTextBox / 密码框测试窗口。浏览器脚本需要 Chrome（默认常见安装路径）和 Node.js 22+，只打开项目中的本地 HTML，并使用独立浏览器配置。它不操作日常使用的浏览器窗口。

Obsidian 测试需要 Python、Node.js 22+ 和本机 Obsidian，先启动独立测试库：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Start-ObsidianQa.ps1 -Executable "D:\SoftWare\Obsidian\Obsidian.exe"
python .\tests\obsidian_qa.py
python .\tests\obsidian_live_qa.py
```

测试使用 localhost 的专用调试端口 9237（Obsidian）或 9238（Chrome）。脚本校验页面标题仅连接测试库／测试页面。DOM 操作只用于制造可重复的合成选区，正式程序的取词不使用调试协议。

关闭 Obsidian 测试实例：

```powershell
"require('electron').remote.app.quit()" | node .\tests\cdp_eval.mjs
```

所有配置和测试库在 `build\qa`，不会写入日常 Obsidian 库。第一次由 Obsidian 打开测试 Markdown 时可能统一文件换行；正式选区回归测试记录的是打开后的文件哈希。

渲染设置窗口供检查：

```powershell
.\build\tests\PointCursor.Tests.exe --render "$PWD\build\qa\settings.png"
```

测试 JSON 结果保存在 `build\qa`。测试只记录合成词；正式程序不写取词日志。

可选的离线验证：在管理员 PowerShell 中运行 `tests\OfflineQa.ps1`。它临时为三个测试可执行文件添加出站阻止规则，执行音频及桌面测试后在 `finally` 中删除规则。只有这项测试需要管理员权限，正式程序不需要。
