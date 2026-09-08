using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PointCursor
{
    internal static class Brand
    {
        public static readonly Color Ink = Color.FromArgb(29, 48, 44);
        public static readonly Color Muted = Color.FromArgb(93, 108, 103);
        public static readonly Color Accent = Color.FromArgb(32, 104, 81);
        public static Icon CreateIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var brush = new SolidBrush(Accent)) g.FillEllipse(brush, 0, 0, 31, 31);
                using (var brush = new SolidBrush(Color.White))
                    g.FillPolygon(brush, new[] { new Point(9, 7), new Point(9, 23), new Point(13, 19), new Point(16, 25), new Point(19, 23), new Point(16, 17), new Point(22, 17) });
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
                finally { Native.DestroyIcon(handle); }
            }
        }
    }

    internal sealed class SettingsForm : Form
    {
        public event Action SettingsChanged, ToggleRequested, PreviewRequested, ExitRequested;
        public bool AllowClose { get; set; }
        private readonly Label status, state;
        private readonly Button toggle;
        public SettingsForm(AppSettings settings, IList<string> voices)
        {
            Text = "PointCursor · 划词发音";
            Icon = Brand.CreateIcon();
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Color.FromArgb(246, 248, 245); ForeColor = Brand.Ink;
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(570, 656); MinimumSize = new Size(570, 650);
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 20), ColumnCount = 1, RowCount = 7, AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 280));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(layout);

            var heading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            heading.Controls.Add(new Label { Text = "PointCursor", Font = new Font("Segoe UI", 25F, FontStyle.Bold), ForeColor = Brand.Ink, AutoSize = true, Location = new Point(-3, 0) });
            heading.Controls.Add(new Label { Text = "选中一个单词，听见它的读法。", AutoSize = true, ForeColor = Brand.Muted, Location = new Point(0, 62) });
            layout.Controls.Add(heading, 0, 0);

            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.FromArgb(229, 239, 230), Padding = new Padding(14, 7, 8, 7), Margin = new Padding(0, 0, 0, 12) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            state = new Label { Text = "●  划词发音已开启", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Brand.Accent, AutoSize = true };
            toggle = Button("暂停", false); toggle.Dock = DockStyle.Fill; toggle.Margin = Padding.Empty;
            toggle.Click += delegate { if (ToggleRequested != null) ToggleRequested(); };
            bar.Controls.Add(state, 0, 0); bar.Controls.Add(toggle, 1, 0); layout.Controls.Add(bar, 0, 1);

            var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(16, 12, 16, 12), BackColor = Color.White, Margin = new Padding(0, 0, 0, 12) };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            options.Controls.Add(Caption("语音"), 0, 0);
            var voice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Margin = new Padding(0, 7, 0, 0), AccessibleName = "英文语音" };
            foreach (string name in voices) voice.Items.Add(name);
            if (voices.Count > 0) voice.SelectedItem = settings.Voice; else { voice.Items.Add("未安装英文语音"); voice.SelectedIndex = 0; voice.Enabled = false; }
            voice.SelectedIndexChanged += delegate { settings.Voice = (string)voice.SelectedItem; Changed(); };
            options.Controls.Add(voice, 1, 0);
            options.Controls.Add(Caption("语速"), 0, 1);
            options.Controls.Add(Slider(-5, 5, settings.Rate, "英文语速", delegate(int value) { settings.Rate = value; Changed(); }, delegate(int value) { return value == 0 ? "标准" : (value < 0 ? "较慢  " : "较快  +") + value; }), 1, 1);
            options.Controls.Add(Caption("音量"), 0, 2);
            options.Controls.Add(Slider(0, 100, settings.Volume, "发音音量", delegate(int value) { settings.Volume = value; Changed(); }, delegate(int value) { return value + "%"; }), 1, 2);
            var copy = new CheckBox { Text = "复制后发音（Ctrl+C）", Checked = settings.CopyToSpeak, Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty, AccessibleName = "复制后发音" };
            copy.CheckedChanged += delegate { settings.CopyToSpeak = copy.Checked; Changed(); };
            options.Controls.Add(copy, 0, 3); options.SetColumnSpan(copy, 2);
            var copyHelp = new Label { Text = "自动取词失败时，复制一个英文单词即可。", ForeColor = Brand.Muted, Dock = DockStyle.Fill, AutoSize = true, Font = new Font(Font.FontFamily, 9F), Margin = Padding.Empty };
            options.Controls.Add(copyHelp, 0, 4); options.SetColumnSpan(copyHelp, 2);
            layout.Controls.Add(options, 0, 2);

            status = new Label { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), ForeColor = Brand.Muted, AutoEllipsis = true, AccessibleName = "发音状态", Margin = Padding.Empty };
            layout.Controls.Add(status, 0, 3);
            var note = new Label { Text = "完全离线  ·  不保存阅读内容\n仅朗读单个英文词；关闭窗口后仍在托盘运行。", ForeColor = Brand.Muted, Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 9F), AutoSize = true, Margin = Padding.Empty };
            layout.Controls.Add(note, 0, 4); layout.SetRowSpan(note, 2);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            var preview = Button("试听 hello", true); preview.Dock = DockStyle.Fill; preview.Enabled = voices.Count > 0; preview.Margin = Padding.Empty;
            preview.Click += delegate { if (PreviewRequested != null) PreviewRequested(); };
            var exit = Button("退出程序", false); exit.Dock = DockStyle.Fill; exit.Margin = Padding.Empty;
            exit.Click += delegate { if (ExitRequested != null) ExitRequested(); };
            actions.Controls.Add(preview, 0, 0); actions.Controls.Add(exit, 2, 0); layout.Controls.Add(actions, 0, 6);
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            FormClosed += delegate { Icon.Dispose(); };
        }
        private void Changed() { if (SettingsChanged != null) SettingsChanged(); }
        private Label Caption(string text) { return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }; }
        private Button Button(string text, bool primary)
        {
            var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = primary ? Brand.Accent : Color.White, ForeColor = primary ? Color.White : Brand.Ink, Cursor = Cursors.Hand, UseVisualStyleBackColor = false, Height = 40 };
            button.FlatAppearance.BorderSize = primary ? 0 : 1; button.FlatAppearance.BorderColor = Color.FromArgb(213, 223, 216);
            return button;
        }
        private Control Slider(int min, int max, int value, string name, Action<int> change, Func<int, string> format)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            var track = new TrackBar { Minimum = min, Maximum = max, Value = value, TickStyle = TickStyle.None, Dock = DockStyle.Fill, AutoSize = false, Margin = new Padding(0, 12, 0, 8), AccessibleName = name, SmallChange = 1, LargeChange = max == 100 ? 10 : 1 };
            var label = new Label { Text = format(value), TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill, ForeColor = Brand.Muted, Margin = Padding.Empty };
            track.ValueChanged += delegate { label.Text = format(track.Value); change(track.Value); };
            row.Controls.Add(track, 0, 0); row.Controls.Add(label, 1, 0); return row;
        }
        public void UpdateStatus(string text, bool paused)
        {
            status.Text = text; state.Text = paused ? "●  划词发音已暂停" : "●  划词发音已开启";
            state.ForeColor = paused ? Brand.Muted : Brand.Accent; toggle.Text = paused ? "恢复" : "暂停";
        }
    }
}
