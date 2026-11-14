namespace CouchWorkstation.UI;

/// <summary>
/// Short notice on the TV. Never takes focus and ignores the mouse, so the focused text field
/// (e.g. the one being dictated into) is not disturbed.
/// </summary>
public sealed class Toast : Form
{
    private static Toast? _current;

    private Toast(Rectangle screen, string text)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;
        Opacity = 0.92;
        var label = new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 20f, FontStyle.Bold), Padding = new Padding(24, 14, 24, 14) };
        Controls.Add(label);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Load += (_, _) => Location = new Point(screen.X + (screen.Width - Width) / 2, screen.Y + screen.Height / 12);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x80 /* WS_EX_TOOLWINDOW */ | 0x20 /* WS_EX_TRANSPARENT */;
            return cp;
        }
    }

    /// <summary>Shows a notice on the given screen for a while (replaces a visible one). UI thread only.</summary>
    public static void ShowOn(Rectangle screen, string text, int ms = 2500)
    {
        _current?.Close();
        var t = new Toast(screen, text);
        _current = t;
        var timer = new System.Windows.Forms.Timer { Interval = ms };
        timer.Tick += (_, _) => { timer.Dispose(); t.Close(); };
        t.FormClosed += (_, _) => { if (_current == t) _current = null; };
        t.Show();
        timer.Start();
    }
}
