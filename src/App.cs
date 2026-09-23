using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PointCursor
{
    internal sealed class MessageWindow : NativeWindow, IDisposable
    {
        private const int ActivateMessage = 0x8003;
        public event Action ClipboardChanged;
        public event Action InputReady;
        public event Func<IntPtr> ActivateRequested;
        public MessageWindow()
        {
            CreateHandle(new CreateParams { Caption = "PointCursor message receiver", Parent = new IntPtr(-3) });
            if (!Native.AddClipboardFormatListener(Handle)) { DestroyHandle(); throw new System.ComponentModel.Win32Exception(); }
        }
        public void EnableActivation(string instanceName)
        {
            // Publish only after TrayApp is ready to handle ShowSettings.
            if (!Native.SetWindowText(Handle, instanceName)) throw new System.ComponentModel.Win32Exception();
        }
        public static bool ActivateExisting(string instanceName)
        {
            // The first process can still be constructing its tray and speech service.
            // Search only message windows belonging to this user's product/QA instance.
            for (int attempt = 0; attempt < 60; attempt++)
            {
                IntPtr receiver = Native.FindWindowEx(new IntPtr(-3), IntPtr.Zero, null, instanceName);
                if (receiver != IntPtr.Zero)
                {
                    uint pid = Native.ProcessOf(receiver);
                    if (pid != 0)
                    {
                        Native.AllowSetForegroundWindow(pid);
                        UIntPtr result;
                        // Ask the resident to restore its UI, then use the launcher's
                        // foreground permission. A hung resident must not block launch.
                        if (Native.SendMessageTimeout(receiver, ActivateMessage, IntPtr.Zero, IntPtr.Zero, 0x0002, 1000, out result) == IntPtr.Zero) return false;
                        IntPtr settingsWindow = new IntPtr(unchecked((long)result.ToUInt64()));
                        if (Native.ProcessOf(settingsWindow) == pid) Native.SetForegroundWindow(settingsWindow);
                        return true;
                    }
                }
                Thread.Sleep(50);
            }
            return false;
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x31D && ClipboardChanged != null) ClipboardChanged();
            if (message.Msg == InputMonitor.NoticeMessage && InputReady != null) InputReady();
            if (message.Msg == ActivateMessage && ActivateRequested != null)
            {
                message.Result = ActivateRequested();
                return;
            }
            base.WndProc(ref message);
        }
        public void Dispose() { Native.RemoveClipboardFormatListener(Handle); DestroyHandle(); }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private readonly AppSettings settings;
#if POINTCURSOR_QA
        private readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qa", "settings.xml");
#else
        private readonly string settingsPath = AppSettings.FilePath;
#endif
        private readonly SpeechService speech;
        private readonly SelectionClient reader;
        private readonly MessageWindow messages;
        private readonly InputMonitor input;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem toggle;
        private readonly SelectionGate selection = new SelectionGate();
        private readonly CopyGate copy = new CopyGate();
        private readonly System.Windows.Forms.Timer selectionTimer, clipboardTimer;
        private CancellationTokenSource request = new CancellationTokenSource();
        private SettingsForm form;
        private InputNotice pendingSelection;
        private IntPtr copyWindow;
        private long copyTicket, copyTime;
        private bool copying, paused, exiting;
        private string status = "准备好了，选中一个英文单词试试。";
#if POINTCURSOR_QA
        private int qaPronunciations;
#endif
        private readonly uint processId = (uint)Process.GetCurrentProcess().Id;
        public TrayApp(bool quiet, string instanceName)
        {
            bool reset;
            settings = AppSettings.Load(settingsPath, out reset);
            speech = new SpeechService(settings);
            reader = new SelectionClient(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PointCursor.Reader.exe"));
            messages = new MessageWindow();
            // Ensure async continuations run on the WinForms message thread, including --quiet startup.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            SynchronizationContext ui = SynchronizationContext.Current;
            speech.Failed += delegate(string error) { ui.Post(delegate { if (!exiting) SetStatus(error); }, null); };
            speech.Started += delegate(long ticket, string word) { ui.Post(delegate { if (!exiting && ticket == speech.Generation) SetStatus(Pronounced(word)); }, null); };
            input = new InputMonitor(messages.Handle);
            // Start almost immediately. The reader can reconstruct the gesture range
            // from both mouse endpoints, so a later focus/caret change is no longer
            // allowed to erase an already completed selection intent.
            selectionTimer = new System.Windows.Forms.Timer { Interval = 10 };
            selectionTimer.Tick += async delegate { selectionTimer.Stop(); await ReadSelection(); };
            clipboardTimer = new System.Windows.Forms.Timer { Interval = 40 };
            clipboardTimer.Tick += delegate { OnClipboard(); };
            messages.InputReady += ProcessInput;
            messages.ClipboardChanged += OnClipboard;
            var menu = new ContextMenuStrip();
            toggle = new ToolStripMenuItem("暂停划词发音", null, delegate { SetPaused(!paused); });
            menu.Items.Add("打开设置", null, delegate { ShowSettings(); });
            menu.Items.Add(toggle);
            menu.Items.Add("试听 hello", null, delegate { Preview(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出 PointCursor", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = Brand.CreateIcon(), Text = "PointCursor · 划词发音已开启", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowSettings(); };
            speech.Prepare(settings);
            if (speech.Error != null) SetStatus(speech.Error);
            else if (reset) SetStatus("设置文件无法读取，已使用默认设置。");
            messages.ActivateRequested += ShowSettings;
            messages.EnableActivation(instanceName);
            if (!quiet) ShowSettings();
        }
        private bool Eligible(IntPtr window) { return window != IntPtr.Zero && Native.ProcessOf(window) != processId; }
        private void Invalidate(bool stop)
        {
            selection.Invalidate(); selectionTimer.Stop(); pendingSelection = null;
            copy.Reset(); clipboardTimer.Stop(); copying = false;
            request.Cancel(); request.Dispose(); request = new CancellationTokenSource();
            if (stop) speech.Stop();
        }
        private void ProcessInput()
        {
            InputNotice notice;
            while (input.TryTake(out notice))
            {
                if (paused || !Eligible(notice.Window)) continue;
                if (notice.Kind == "selection")
                {
                    request.Cancel(); request.Dispose(); request = new CancellationTokenSource();
                    selection.Invalidate();
                    copy.Reset(); clipboardTimer.Stop(); copying = false;
                    pendingSelection = notice; selectionTimer.Stop(); selectionTimer.Start();
                }
                else if (notice.Kind == "copy" && settings.CopyToSpeak && Native.GetForegroundWindow() == notice.Window)
                {
                    selectionTimer.Stop(); pendingSelection = null;
                    request.Cancel(); request.Dispose(); request = new CancellationTokenSource();
                    // Every explicit Ctrl+C is a new pronunciation request, even when
                    // the selected word and clipboard contents are unchanged.
                    copyWindow = notice.Window; copyTicket = selection.Invalidate(); copyTime = notice.Time;
                    copy.Arm(notice.Window, notice.Sequence, notice.Time);
                    clipboardTimer.Start();
                }
            }
        }
        private async Task ReadSelection()
        {
            InputNotice notice = pendingSelection;
            pendingSelection = null;
            if (notice == null || paused) return;
            long ticket = selection.Current;
            CancellationToken token = request.Token;
            try
            {
                SelectionResult result = await reader.QueryAsync(notice.Window, notice.StartX, notice.StartY, notice.X, notice.Y, false, token, notice.Gesture == GestureKind.DoubleClick);
                // The completed gesture owns this request. Later focus, caret and
                // foreground changes neither revoke its result nor stop its playback.
                if (token.IsCancellationRequested || exiting || paused || ticket != selection.Current) return;
#if POINTCURSOR_QA
                if (form != null && !form.IsDisposed) form.Text = "PointCursor · QA reader: " + result.Status + " / " + notice.Gesture;
#endif
                if (result.Word != null) SpeakOnce(result.Word, ticket);
                else if (result.Status == "unavailable" || result.Status == "timeout") SetStatus(settings.CopyToSpeak ? "此处未能自动取词，选中单词后按 Ctrl+C 可发音。" : "此处未能自动取词，可在设置中开启复制后发音。");
            }
            catch (OperationCanceledException) { }
        }
        private async void OnClipboard()
        {
            if (exiting || paused || !settings.CopyToSpeak || copying) return;
            if (Native.Now - copyTime > 1200) { clipboardTimer.Stop(); copy.Reset(); return; }
            IntPtr foreground = Native.GetForegroundWindow();
            if (!Eligible(foreground)) { clipboardTimer.Stop(); copy.Reset(); return; }
            uint sequence = Native.GetClipboardSequenceNumber();
            if (!copy.TryConsume(foreground, sequence, Native.Now)) return;
            clipboardTimer.Stop();
            if (Native.ProcessOf(Native.GetClipboardOwner()) != Native.ProcessOf(copyWindow)) return;
            copying = true;
            long ticket = copyTicket;
            IntPtr window = copyWindow;
            CancellationToken token = request.Token;
            try
            {
                SelectionResult guard = await reader.QueryAsync(window, 0, 0, true, token);
                if (guard.Status != "safe") { if (!token.IsCancellationRequested) SetStatus("无法确认当前控件状态，已跳过复制发音。"); return; }
                for (int i = 0; i < 4; i++)
                {
                    if (token.IsCancellationRequested || exiting || paused || ticket != selection.Current || Native.GetForegroundWindow() != window || Native.GetClipboardSequenceNumber() != sequence) return;
                    bool busy;
                    string word = Native.ReadShortClipboard(messages.Handle, out busy);
                    if (!busy) { if (word != null) SpeakOnce(word, ticket); return; }
                    await Task.Delay(40, token);
                }
                SetStatus("剪贴板正被占用，请再按一次 Ctrl+C。");
            }
            catch (OperationCanceledException) { }
            // A newer Ctrl+C may cancel this operation while leaving its clipboard timer
            // armed. Always release the single-reader guard so that timer can consume it.
            finally { copying = false; }
        }
        private void SpeakOnce(string word, long ticket)
        {
            if (!selection.TryAccept(ticket)) return;
            if (speech.Speak(word, settings)) SetStatus("正在准备发音 · " + word);
            else SetStatus(speech.Error ?? "英文语音不可用。");
        }
        private void Preview()
        { Invalidate(true); if (speech.Speak("hello", settings)) SetStatus("试听 · hello"); else SetStatus(speech.Error ?? "英文语音不可用。"); }
        private void SetStatus(string value)
        { status = value; if (form != null && !form.IsDisposed) form.UpdateStatus(status, paused); }
        private string Pronounced(string word)
        {
#if POINTCURSOR_QA
            return "已发音 · " + word + " · #" + (++qaPronunciations).ToString(System.Globalization.CultureInfo.InvariantCulture);
#else
            return "已发音 · " + word;
#endif
        }
        private void SetPaused(bool value)
        {
            paused = value; Invalidate(true);
            if (paused) speech.Suspend(); else speech.Prepare(settings);
            toggle.Text = paused ? "恢复划词发音" : "暂停划词发音";
            tray.Text = paused ? "PointCursor · 已暂停" : "PointCursor · 划词发音已开启";
            SetStatus(paused ? "已暂停，选词和复制都不会自动发音。" : "准备好了，选中一个英文单词试试。");
        }
        private IntPtr ShowSettings()
        {
            if (exiting) return IntPtr.Zero;
            if (form == null || form.IsDisposed)
            {
                form = new SettingsForm(settings, speech.Voices);
                form.SettingsChanged += delegate { Invalidate(true); if (!paused) speech.Prepare(settings); if (!settings.Save(settingsPath)) SetStatus("设置无法保存，本次运行仍然有效。"); };
                form.ToggleRequested += delegate { SetPaused(!paused); };
                form.PreviewRequested += delegate { Preview(); };
                form.ExitRequested += delegate { ExitThread(); };
            }
            form.UpdateStatus(status, paused);
            form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            // A --quiet process may have inherited a hidden startup window style.
            Native.ShowWindow(form.Handle, 9); // SW_RESTORE
            Native.SetForegroundWindow(form.Handle);
            form.Activate();
            return form.Handle;
        }
        protected override void ExitThreadCore()
        {
            if (exiting) return;
            exiting = true; Invalidate(true);
            input.Dispose(); messages.Dispose(); reader.Dispose(); speech.Dispose();
            selectionTimer.Dispose(); clipboardTimer.Dispose(); request.Dispose();
            tray.Visible = false; tray.Icon.Dispose(); tray.ContextMenuStrip.Dispose(); tray.Dispose();
            if (form != null) { form.AllowClose = true; form.Close(); form.Dispose(); }
            base.ExitThreadCore();
        }
    }
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
#if POINTCURSOR_QA
            string instanceName = "Local\\PointCursor-QA-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
#else
            string instanceName = "Local\\PointCursor-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
#endif
            using (var instance = new Mutex(false, instanceName))
            {
                if (!AcquireInstance(instance))
                {
                    if (MessageWindow.ActivateExisting(instanceName)) return;
                    // Recover if the previous process exited during the bounded wait.
                    if (!AcquireInstance(instance)) return;
                }
                try { Application.Run(new TrayApp(Array.IndexOf(args, "--quiet") >= 0, instanceName)); }
                catch (Exception) { MessageBox.Show("PointCursor 无法启动。请确认程序文件完整，并检查系统语音和桌面会话后重试。", "PointCursor", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { instance.ReleaseMutex(); }
            }
        }
        private static bool AcquireInstance(Mutex instance)
        {
            try { return instance.WaitOne(0); }
            catch (AbandonedMutexException) { return true; }
        }
    }
}
