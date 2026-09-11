# 验证方法

先运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test`。99 项自动检查涵盖词规则、Obsidian 高亮重写校正、设置、两种语音、Zira 同词重复请求、PCM 词头保留、预加载、快速换词、卡死恢复与退出清理；会播放几个固定的测试单词。测试版托盘程序单独编译为 `POINTCURSOR_QA`，使用独立单实例互斥，设置仅保存到 `build/tests/qa/settings.xml`，不会覆盖或要求退出正在使用的正式版 PointCursor。

以下集成测试会临时展示测试窗口、移动鼠标并播放英文。运行时保持桌面空闲，避免同时操作鼠标键盘。仅操作合成测试内容。脚本退出时恢复原来的前台窗口和光标位置；复制测试仅在可完整快照剪贴板时执行，结束时在剪贴板未被外部改变的情况下恢复其内容。

```powershell
python .\tests\desktop_qa.py
python .\tests\browser_qa.py
```

桌面脚本使用独立 RichTextBox / 密码框测试窗口，并验证划词完成后的普通输入、立即切换窗口和立即点击别处都不会撤销请求。Ctrl+C 测试只在当前剪贴板全部格式都可无损快照时执行，否则明确跳过。浏览器脚本需要 Chrome（默认常见安装路径）和 Node.js 22+，只打开项目中的本地 HTML，并使用独立浏览器配置。它不操作日常使用的浏览器窗口。

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

所有配置和测试库在 `build\qa`，不会写入日常 Obsidian 库。启动脚本只在该隔离库中把 Ctrl+Shift+H 绑定为 Obsidian 的高亮命令；实时交互测试覆盖“拖选后立即高亮”，规则测试覆盖高亮重写后只读到 `hell`／`h` 时恢复完整 `hello`。第一次由 Obsidian 打开测试 Markdown 时可能统一文件换行；正式选区回归测试记录的是打开后的文件哈希。

渲染设置窗口供检查：

```powershell
.\build\tests\PointCursor.Tests.exe --render "$PWD\build\qa\settings.png"
```

测试 JSON 结果保存在 `build\qa`。测试只记录合成词；正式程序不写取词日志。

可选的离线验证：在管理员 PowerShell 中运行 `tests\OfflineQa.ps1`。它临时为四个测试可执行文件（含 Kokoro/node.exe）添加出站阻止规则，执行音频及桌面测试后在 `finally` 中删除规则。只有这项测试需要管理员权限，正式程序不需要。

## 音频词头回录

先暂停其他软件的音频。以下测试只使用 Windows 默认输出的 loopback，不打开麦克风；回录可能包含其他软件声音，文件留在忽略的 build 目录，勿提交或分享。分析脚本需要开发机已有的 numpy，正式程序不依赖 Python。

```powershell
.\build\tests\PointCursor.AudioProbe.exe build/qa/sapi
python tests/analyze_loopback.py build/qa/sapi
.\build\tests\PointCursor.AudioProbe.exe build/qa/kokoro build/qa/kokoro-smoke.wav
python tests/analyze_loopback.py build/qa/kokoro
```

每轮约 6.5 秒，比较首次和间隔后的两次播放，分别检查整词和首个有效信号起 100 毫秒的波形相关性。它验证 Windows 输出端，不能代替物理音箱或耳机试听。

线程对比：`third_party/kokoro-runtime/node.exe tests/kokoro_benchmark.mjs 4`，更换末尾线程数可重测。该计时从模块导入后开始，不包含 Node 启动；完整冷启动请运行 `diagnose.ps1`。
