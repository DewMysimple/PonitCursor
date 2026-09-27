# 验证方法

先运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test`。自动检查涵盖词规则、高亮校正、双击状态机、旧配置迁移、仅美式/英式语音过滤、选择持久化、缺失声音处理、逐个已安装声音切换合成与重听、PCM、Reader 超时/取消/坏响应/崩溃恢复和退出并发；会播放固定的测试单词。至少需要一个可用美式或英式 SAPI 声音。测试版托盘程序单独编译为 `POINTCURSOR_QA`，使用独立单实例互斥，设置仅保存到 `build/tests/qa/settings.xml`，不会覆盖或要求退出正在使用的正式版 PointCursor。

两类真实语音的安装验收使用 `powershell -NoProfile -ExecutionPolicy Bypass -File .\diagnose.ps1 -RequireBothAccents`，必须同时枚举到 en-US 和 en-GB 且逐个合成出非空音频。过滤与缺失声音测试使用合成目录，不卸载系统语音。窗体测试安排在异步 Reader 检查之后，避免 WinForms 自动安装的同步上下文阻塞没有消息循环的测试主线程。

以下集成测试会临时展示测试窗口、移动鼠标并播放英文。运行时保持桌面空闲，避免同时操作鼠标键盘。仅操作合成测试内容。脚本退出时恢复原来的前台窗口和光标位置；复制测试必须额外传入 `--copy` 且可完整快照剪贴板时才执行，结束时在剪贴板未被外部改变的情况下恢复其内容。

```powershell
python .\tests\desktop_qa.py --stress 100 --input-stall
python .\tests\browser_qa.py
python .\tests\single_instance_qa.py
python .\tests\voice_selection_qa.py
```

桌面脚本使用独立 RichTextBox / 密码框测试窗口，并验证划词完成后的普通输入、立即切换窗口和立即点击别处都不会撤销请求。默认不操作剪贴板；显式传入 `--copy` 后还需通过完整快照检查，否则跳过。回归覆盖第二次点击长按、两次 1.5 秒 UI 卡顿，以及连续交替双击。`--input-stall` 另外使输入线程卡住 1.5 秒：过期积压手势不应播报，恢复后的第一次新双击必须成功。故障入口只编译在 QA 版中。浏览器脚本需要 Chrome（默认常见安装路径）和 Node.js 22+，只打开项目中的本地 HTML，并使用独立浏览器配置。它不操作日常使用的浏览器窗口。

单实例脚本约 5 秒：由前台 Fixture 启动第二个 QA 进程，模拟从资源管理器启动程序的前台权限，检查静默驻留唤出、隐藏恢复、最小化恢复、遮挡后聚焦、连续启动、正常退出与两个进程同时冷启动。它临时切换焦点，不移动鼠标、不读写剪贴板；先清除合成 Alt 激活留下的菜单状态，避免 Windows 因活动菜单拒绝焦点交接。结果在 `build/qa/single-instance-results.json`，只终止本轮创建的测试进程。WinForms 隐藏/显示时可能重建 HWND，因此不要求隐藏前后句柄保持不变。

语音选择脚本要求两种口音均已安装：在独立 QA 窗口逐个选择声音、验证实际设置文件和试听，再重启确认选项保留并能继续发音。仅临时覆盖隔离的 QA 设置并在退出时恢复；不操作鼠标、剪贴板或正式设置。运行前退出其他 QA 实例。

Obsidian 测试需要 Python、Node.js 22+ 和本机 Obsidian，先启动独立测试库：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Start-ObsidianQa.ps1 -Executable "D:\SoftWare\Obsidian\Obsidian.exe"
python .\tests\obsidian_qa.py
python .\tests\obsidian_live_qa.py --stress 20
python .\tests\idle_qa.py --idle-seconds 330 --cycles 2
```

测试使用 localhost 的专用调试端口 9237（Obsidian）或 9238（Chrome）。脚本校验页面标题仅连接测试库／测试页面。DOM 操作只用于制造可重复的合成选区，正式程序的取词不使用调试协议。

`idle_qa.py` 隐藏 PointCursor、最小化独立 Obsidian，保持同一进程闲置；默认奇数轮验证首次双击，偶数轮验证首次拖选，随后各做四次重复划词。默认两轮各 330 秒，可用 `--first-gesture drag --cycles 1` 单独验证首次拖选，或用 `--mode source`、`--mode reading` 切换模式；期间不重启、不试听、不暂停再恢复。QA 环境变量 `POINTCURSOR_QA_SOURCE_PID` 将自动取词限制在合成库所在进程，避免闲置期间其他软件的操作触发测试版。脚本等待恢复后的文字布局可读，再取得只读几何坐标，不预先制造选区。结果为 `build/qa/idle-<mode>-results.json`，同时检查合成笔记哈希与手势期间的剪贴板序列号。

同词连续复选间隔超过 Windows 的双击时间，确保每次都是独立双击；快速连续三击/四击可能使 Obsidian 选中整行，整行被产品跳过属于预期的多词保护。

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

可选的离线验证：在管理员 PowerShell 中运行 `tests\OfflineQa.ps1`。它临时为三个测试可执行文件添加出站阻止规则，执行音频及桌面测试后在 `finally` 中删除规则。只有这项测试需要管理员权限，正式程序不需要。

## 音频词头回录

先暂停其他软件的音频。以下测试只使用 Windows 默认输出的 loopback，不打开麦克风；回录可能包含其他软件声音，文件留在忽略的 build 目录，勿提交或分享。分析脚本需要开发机已有的 numpy，正式程序不依赖 Python。

```powershell
.\build\tests\PointCursor.AudioProbe.exe build/qa/sapi
python tests/analyze_loopback.py build/qa/sapi
.\build\tests\PointCursor.AudioProbe.exe build/qa/recovery --idle-seconds 120 --stall
python tests/analyze_loopback.py build/qa/recovery
```

每轮约 6.5 秒，比较首次和间隔后的两次播放，分别检查整词和首个有效信号起 100 毫秒的波形相关性。它验证 Windows 输出端，不能代替物理音箱或耳机试听。

`--idle-seconds` 在打开输出后先闲置指定秒数，闲置期间不回录；`--stall` 在首次播放前强制停止原生音频客户端，验证时钟检测能够自动重开并保留完整单词。系统静音或主音量为 0 时回录不能证明播放，脚本不会修改系统音量。PCM 自动检查另外验证未完成单词可恢复、已完成单词不重播、旧设备完成通知不删除新请求。
