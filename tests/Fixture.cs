using System;
using System.Drawing;
using System.Windows.Forms;

internal static class Fixture
{
    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        var window = new Form { Text = "PointCursor QA fixture", ClientSize = new Size(640, 340), StartPosition = FormStartPosition.Manual, Location = new Point(80, 80) };
        window.Controls.Add(new Label { Text = "Synthetic text for selection / clipboard / password tests", AutoSize = true, Location = new Point(20, 15) });
        var text = new RichTextBox { Text = "hello world\nwell-known don't\nhello world 123 https://example.com", ReadOnly = true, Font = new Font("Consolas", 20), Location = new Point(20, 48), Size = new Size(600, 175), DetectUrls = false, HideSelection = false };
        window.Controls.Add(text);
        window.Controls.Add(new Label { Text = "Password control (must never speak)", Location = new Point(20, 240), AutoSize = true });
        window.Controls.Add(new TextBox { Text = "secret", UseSystemPasswordChar = true, Location = new Point(20, 270), Size = new Size(400, 32) });
        var coordinates = new Label { Location = new Point(20, 305), AutoSize = true };
        window.Controls.Add(coordinates);
        window.Shown += delegate {
            text.Focus();
            var values = new System.Collections.Generic.List<string>();
            foreach(int index in new[] {0,5,6,11}) { Point p=text.GetPositionFromCharIndex(index); values.Add(index+","+p.X+","+p.Y); }
            coordinates.Text = "QA coordinates|" + String.Join("|",values);
        };
        Application.Run(window);
    }
}
