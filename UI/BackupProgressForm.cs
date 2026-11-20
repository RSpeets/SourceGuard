using System.Text.RegularExpressions;
using System.Windows.Forms;
using SourceGuard.Models;

namespace SourceGuard.UI;

public sealed class BackupProgressForm : Form
{
    // ── Palette (Catppuccin Mocha) ────────────────────────────────────────────
    private static readonly Color BgDark    = Color.FromArgb(30,  30,  46);
    private static readonly Color BgDarker  = Color.FromArgb(24,  24,  37);
    private static readonly Color SurfaceEl = Color.FromArgb(49,  50,  68);
    private static readonly Color TextMain  = Color.FromArgb(205, 214, 244);
    private static readonly Color TextMuted = Color.FromArgb(108, 112, 134);
    private static readonly Color AccBlue   = Color.FromArgb(137, 180, 250);
    private static readonly Color ColOk     = Color.FromArgb(166, 227, 161);
    private static readonly Color ColErr    = Color.FromArgb(243, 139, 168);

    // ── Layout constants ──────────────────────────────────────────────────────
    private const int FormW   = 460;
    private const int LPad    = 12;
    private const int InnerW  = FormW - LPad * 2;
    private const int HeaderH = 44;
    private const int TaskH   = 62;
    private const int FooterH = 38;

    // ── Fields ────────────────────────────────────────────────────────────────
    private readonly Panel  _tasksArea;
    private readonly Panel  _footer;
    private readonly Label  _statusLabel;
    private readonly System.Windows.Forms.Timer _closeTimer;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly List<WinFormsProgress> _progressItems = [];
    private int _taskCount;

    // ── Constructor ───────────────────────────────────────────────────────────
    public BackupProgressForm()
    {
        SuspendLayout();

        Text            = "SourceGuard";
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox     = false;
        MinimizeBox     = false;
        TopMost         = true;
        ControlBox      = false;
        StartPosition   = FormStartPosition.Manual;
        BackColor       = BgDark;
        Font            = new Font("Segoe UI", 9f);
        ClientSize      = new Size(FormW, HeaderH + FooterH);

        // Header
        var header = new Panel
        {
            Location  = new Point(0, 0),
            Size      = new Size(FormW, HeaderH),
            BackColor = BgDarker,
        };
        header.Controls.Add(new Label
        {
            Text      = "🔒  SourceGuard",
            Font      = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = AccBlue,
            AutoSize  = true,
            Location  = new Point(LPad, 12),
        });

        // Task area (height grows as tasks are added)
        _tasksArea = new Panel
        {
            Location  = new Point(0, HeaderH),
            Size      = new Size(FormW, 0),
            BackColor = BgDark,
        };

        // Footer / status bar
        _footer = new Panel
        {
            Location  = new Point(0, HeaderH),
            Size      = new Size(FormW, FooterH),
            BackColor = BgDarker,
        };
        _statusLabel = new Label
        {
            Text      = "Starting backup…",
            ForeColor = TextMuted,
            AutoSize  = true,
            Location  = new Point(LPad, 11),
        };
        _footer.Controls.Add(_statusLabel);

        Controls.AddRange(new Control[] { header, _tasksArea, _footer });
        ResumeLayout(false);

        _closeTimer          = new System.Windows.Forms.Timer { Interval = 3000 };
        _closeTimer.Tick    += (_, _) => Close();

        _refreshTimer        = new System.Windows.Forms.Timer { Interval = 100 };
        _refreshTimer.Tick  += (_, _) => { foreach (var p in _progressItems) p.Flush(); };
        _refreshTimer.Start();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Add a task row and return a progress handle for it.
    /// Must be called from the UI thread before the backup starts.</summary>
    public ITaskProgress AddTask(string name)
    {
        var y = _taskCount * TaskH + 6;

        var nameLbl = new Label
        {
            Text      = name,
            ForeColor = TextMain,
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            Location  = new Point(LPad, y),
            Size      = new Size(InnerW - 50, 14),
            AutoSize  = false,
        };

        var pctLbl = new Label
        {
            Text      = "0%",
            ForeColor = TextMuted,
            Location  = new Point(LPad + InnerW - 44, y),
            Size      = new Size(44, 14),
            TextAlign = ContentAlignment.MiddleRight,
            AutoSize  = false,
        };

        var barBg = new Panel
        {
            Location  = new Point(LPad, y + 20),
            Size      = new Size(InnerW, 8),
            BackColor = SurfaceEl,
        };
        var barFill = new Panel
        {
            Location  = new Point(0, 0),
            Size      = new Size(0, 8),
            BackColor = AccBlue,
        };
        barBg.Controls.Add(barFill);

        var descLbl = new Label
        {
            Text      = "Waiting…",
            ForeColor = TextMuted,
            Location  = new Point(LPad, y + 32),
            Size      = new Size(InnerW, 24),
            AutoSize  = false,
        };

        _tasksArea.Controls.AddRange(new Control[] { nameLbl, pctLbl, barBg, descLbl });
        _taskCount++;

        // Grow the tasks area and push the footer down
        var tasksH         = _taskCount * TaskH + 8;
        _tasksArea.Size    = new Size(FormW, tasksH);
        _footer.Location   = new Point(0, HeaderH + tasksH);
        ClientSize         = new Size(FormW, HeaderH + tasksH + FooterH);
        PositionWindow();

        var progress = new WinFormsProgress(barBg, barFill, pctLbl, descLbl);
        _progressItems.Add(progress);
        return progress;
    }

    /// <summary>Set the final status message. On success the window closes after 3 s;
    /// on error it stays open until the user dismisses it.</summary>
    public void SetStatus(string message, bool isError = false)
    {
        if (InvokeRequired) { Invoke(() => SetStatus(message, isError)); return; }

        // Final flush so bars all reach their last reported value before we stop the timer
        foreach (var p in _progressItems) p.Flush();
        _refreshTimer.Stop();

        _statusLabel.Text      = message;
        _statusLabel.ForeColor = isError ? ColErr : ColOk;

        if (!isError) _closeTimer.Start();

        PositionWindow();
    }

    // ── Positioning ───────────────────────────────────────────────────────────
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        PositionWindow();
    }

    private void PositionWindow()
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        Left = area.Right  - Width  - 12;
        Top  = Math.Max(area.Top + 12, area.Bottom - Height - 12);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closeTimer.Dispose();
        _refreshTimer.Dispose();
        base.OnFormClosed(e);
    }

    // ── Nested progress implementation ────────────────────────────────────────
    private sealed class WinFormsProgress : ITaskProgress
    {
        private static readonly Regex _markup =
            new(@"\[/?[a-zA-Z][a-zA-Z0-9 #,_]*\]", RegexOptions.Compiled);

        private readonly Panel _barBg, _barFill;
        private readonly Label _pct, _desc;

        // Written by background thread, read by UI timer — double is 64-bit atomic on x64
        private double _max = 100, _val;
        private string _pendingDesc = "Waiting…";

        internal WinFormsProgress(Panel barBg, Panel barFill, Label pct, Label desc)
        {
            (_barBg, _barFill, _pct, _desc) = (barBg, barFill, pct, desc);
        }

        public double MaxValue { get => _max; set => _max = value; }
        public double Value    { get => _val; set => _val = value; }

        public string Description
        {
            get => _pendingDesc;
            set => _pendingDesc = _markup.Replace(value, "");
        }

        public void Increment(double amount = 1.0) =>
            _val = Math.Min(_val + amount, _max);

        /// <summary>Called on the UI thread every 100 ms to push pending state to controls.</summary>
        internal void Flush()
        {
            var pct = _max > 0 ? (int)Math.Clamp(_val / _max * 100.0, 0, 100) : 0;
            _barFill.Width = (int)Math.Round(pct / 100.0 * _barBg.Width);
            _pct.Text      = $"{pct}%";
            _desc.Text     = _pendingDesc;
        }
    }
}
