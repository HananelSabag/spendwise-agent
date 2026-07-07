using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SpendWiseWorker;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Global\SpendWiseWorkerSingleton", out var createdNew);
        if (!createdNew)
        {
            LogStartupError("Duplicate launch ignored because SpendWise Worker is already running.");
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LogStartupError(e.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) LogStartupError(ex.ToString());
            else LogStartupError("Unhandled non-Exception error: " + e.ExceptionObject);
        };

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new WorkerForm(args));
    }

    private static void LogStartupError(string message)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "worker-error.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}

internal sealed class WorkerForm : Form
{
    private const int IntervalMinutes = 30;
    private const int MaxRunMinutes = 6;
    private readonly string _workerDir;
    private readonly string _agentDir;
    private readonly string _agentJs;
    private readonly string _logFile;
    private readonly string _stateFile;
    private readonly string _lockFile;
    private readonly string _i18nDir;
    private readonly string _buildVersion;

    private readonly WorkerState _state;
    private readonly WorkerProfile _profile;
    private I18n _i18n;
    private bool _running;
    private bool _busy;
    private bool _userQuit;
    private bool _pulseOn;
    private int _sessionRuns;
    private DateTime? _nextRunAt;
    private DateTime? _runStartedAt;
    private Process? _currentProc;
    private string _statusKey = "status.stopped";

    private readonly Font _regular;
    private readonly Font _bold;
    private readonly Font _title;
    private readonly Font _stat;
    private readonly Font _small;
    private readonly Font _pillFont;

    private readonly Color _bg = Color.FromArgb(15, 23, 42);
    private readonly Color _card = Color.FromArgb(30, 41, 59);
    private readonly Color _card2 = Color.FromArgb(51, 65, 85);
    private readonly Color _panel = Color.FromArgb(24, 32, 49);
    private readonly Color _border = Color.FromArgb(71, 85, 105);
    private readonly Color _text = Color.FromArgb(241, 245, 249);
    private readonly Color _muted = Color.FromArgb(148, 163, 184);
    private readonly Color _gray = Color.FromArgb(100, 116, 139);
    private readonly Color _indigo = Color.FromArgb(99, 102, 241);
    private readonly Color _indigoDeep = Color.FromArgb(79, 70, 229);
    private readonly Color _green = Color.FromArgb(16, 185, 129);
    private readonly Color _red = Color.FromArgb(239, 68, 68);
    private readonly Color _blue = Color.FromArgb(59, 130, 246);
    private readonly Color _amber = Color.FromArgb(245, 158, 11);
    private readonly Color _cyan = Color.FromArgb(6, 182, 212);

    private Label _headerTitle = null!;
    private Label _headerSubtitle = null!;
    private Label _headerPill = null!;
    private Button _languageButton = null!;
    private Label _hostTitle = null!;
    private Label _hostBody = null!;
    private Label _hostNote = null!;
    private Label _hostBadge = null!;
    private Label _dot = null!;
    private Label _statusText = null!;
    private Label _nextRun = null!;
    private Label _lastResult = null!;
    private Label _lastRun = null!;
    private Label _statusHint = null!;
    private Label _checksNumber = null!;
    private Label _checksLabel = null!;
    private Label _transactionsNumber = null!;
    private Label _transactionsLabel = null!;
    private Label _modelTitle = null!;
    private Label _runMark = null!;
    private Label _runTitle = null!;
    private Label _runBody = null!;
    private Label _handoffMark = null!;
    private Label _handoffTitle = null!;
    private Label _handoffBody = null!;
    private Label _keyPill = null!;
    private Label _apiPill = null!;
    private Label _banksPill = null!;
    private Label _freqPill = null!;
    private Button _mainButton = null!;
    private Button _runButton = null!;
    private Button _cleanButton = null!;
    private Button _logButton = null!;
    private Button _folderButton = null!;
    private CheckBox _startupCheck = null!;
    private Label _intervalLabel = null!;
    private Label _footer = null!;
    private NotifyIcon _tray = null!;
    private ToolStripMenuItem _miOpen = null!;
    private ToolStripMenuItem _miRun = null!;
    private ToolStripMenuItem _miClean = null!;
    private ToolStripMenuItem _miQuit = null!;

    private readonly System.Windows.Forms.Timer _loopTimer = new();
    private readonly System.Windows.Forms.Timer _countdownTimer = new();
    private readonly System.Windows.Forms.Timer _watchdogTimer = new();
    private readonly System.Windows.Forms.Timer _pulseTimer = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();

    public WorkerForm(string[] args)
    {
        _workerDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _agentDir = FindAgentDir(_workerDir);
        _agentJs = Path.Combine(_agentDir, "src", "agent.js");
        _logFile = Path.Combine(_agentDir, "agent.log");
        _stateFile = Path.Combine(_agentDir, ".worker-state.json");
        _lockFile = Path.Combine(_agentDir, ".agent.lock");
        _i18nDir = ResolveI18nDir(_workerDir, _agentDir);
        _buildVersion = GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        _state = WorkerState.Load(_stateFile);
        _profile = WorkerProfile.Load(_workerDir);
        _i18n = I18n.Load(_i18nDir, _state.Language);

        _regular = AppFont(10.0f, FontStyle.Regular, "Segoe UI Variable Text", "Segoe UI");
        _bold = AppFont(10.5f, FontStyle.Bold, "Segoe UI Variable Text", "Segoe UI");
        _title = AppFont(15.0f, FontStyle.Bold, "Segoe UI Variable Display", "Segoe UI");
        _stat = AppFont(21.0f, FontStyle.Bold, "Segoe UI Variable Display", "Segoe UI");
        _small = AppFont(8.75f, FontStyle.Regular, "Segoe UI Variable Text", "Segoe UI");
        _pillFont = AppFont(7.8f, FontStyle.Bold, "Segoe UI Variable Text", "Segoe UI");

        BuildUi();
        WireTimers();
        ApplyLanguage();
        ParseLastResult();

        if (args.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("-AutoStart", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("autostart", StringComparison.OrdinalIgnoreCase)))
        {
            StartWorker();
            WindowState = FormWindowState.Minimized;
            Hide();
        }
    }

    private static string FindAgentDir(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "agent.js"))) return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string ResolveI18nDir(string workerDir, string agentDir)
    {
        var local = Path.Combine(workerDir, "i18n");
        if (Directory.Exists(local)) return local;
        return Path.Combine(agentDir, "worker", "i18n");
    }

    private static Font AppFont(float size, FontStyle style, params string[] names)
    {
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        var installed = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (installed.Contains(name)) return new Font(name, size, style);
        }

        return new Font("Segoe UI", size, style);
    }

    private void BuildUi()
    {
        Text = T("app.title");
        ClientSize = new Size(640, 880);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = _bg;
        ForeColor = _text;
        Font = _regular;
        Icon = LoadIcon();

        var header = new GradientHeader(_indigoDeep, _bg, _card2) { Bounds = new Rectangle(0, 0, 640, 96) };
        Controls.Add(header);

        var logo = new PictureBox
        {
            Bounds = new Rectangle(28, 24, 48, 48),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Image = LoadLogo(48)
        };
        header.Controls.Add(logo);

        _headerTitle = NewLabel(T("app.title"), _title, Color.White, new Rectangle(94, 24, 300, 28), ContentAlignment.MiddleLeft, true);
        _headerSubtitle = NewLabel(T("header.subtitle"), _small, Color.FromArgb(199, 210, 254), new Rectangle(96, 56, 320, 20), ContentAlignment.MiddleLeft, true);
        header.Controls.Add(_headerTitle);
        header.Controls.Add(_headerSubtitle);

        _languageButton = NewButton(T("meta.toggle"), new Rectangle(536, 18, 76, 28), _panel, Color.White, _pillFont);
        _languageButton.FlatAppearance.BorderSize = 1;
        _languageButton.FlatAppearance.BorderColor = Color.FromArgb(129, 140, 248);
        header.Controls.Add(_languageButton);

        _headerPill = Pill(T("header.pill"), new Rectangle(454, 56, 158, 26), Color.White, Color.FromArgb(34, 197, 94));
        header.Controls.Add(_headerPill);

        var hostCard = new CardPanel(_panel, _border) { Bounds = new Rectangle(24, 112, 592, 112) };
        Controls.Add(hostCard);
        _hostTitle = NewLabel("", _bold, _text, new Rectangle(24, 18, 360, 24), ContentAlignment.MiddleLeft);
        _hostBody = NewLabel("", _regular, _muted, new Rectangle(24, 48, 420, 40), ContentAlignment.TopLeft);
        _hostNote = NewLabel("", _small, _gray, new Rectangle(24, 88, 520, 18), ContentAlignment.MiddleLeft);
        _hostBadge = Pill("", new Rectangle(424, 18, 144, 28), Color.White, _indigo);
        hostCard.Controls.AddRange(new Control[] { _hostTitle, _hostBody, _hostNote, _hostBadge });

        var statusCard = new CardPanel(_card, _border) { Bounds = new Rectangle(24, 240, 592, 118) };
        Controls.Add(statusCard);
        _dot = NewLabel("\u25CF", new Font("Segoe UI", 15, FontStyle.Regular), _gray, new Rectangle(18, 13, 22, 24), ContentAlignment.MiddleCenter);
        _dot.AutoEllipsis = false;
        _statusText = NewLabel(T("status.stopped"), _bold, _text, new Rectangle(44, 17, 172, 22), ContentAlignment.MiddleLeft);
        _nextRun = NewLabel("", _small, _muted, new Rectangle(410, 19, 150, 20), ContentAlignment.MiddleRight);
        _lastResult = NewLabel(T("result.notRunYet"), _regular, _muted, new Rectangle(44, 51, 500, 24), ContentAlignment.MiddleLeft);
        _lastRun = NewLabel("", _small, _gray, new Rectangle(44, 77, 500, 18), ContentAlignment.MiddleLeft);
        _statusHint = NewLabel(T("status.hint"), _small, _gray, new Rectangle(44, 97, 500, 18), ContentAlignment.MiddleLeft);
        statusCard.Controls.AddRange(new Control[] { _dot, _statusText, _nextRun, _lastResult, _lastRun, _statusHint });

        var checks = StatCard(new Rectangle(24, 374, 286, 78), _indigo, out _checksNumber, out _checksLabel);
        var txns = StatCard(new Rectangle(330, 374, 286, 78), _green, out _transactionsNumber, out _transactionsLabel);
        Controls.Add(checks);
        Controls.Add(txns);
        _checksNumber.Text = _state.TotalRuns.ToString(CultureInfo.InvariantCulture);
        _transactionsNumber.Text = GetSyncTotals().newTxns.ToString(CultureInfo.InvariantCulture);

        var model = new CardPanel(_panel, _border) { Bounds = new Rectangle(24, 468, 592, 166) };
        Controls.Add(model);
        _modelTitle = NewLabel(T("model.title"), _bold, _text, new Rectangle(22, 16, 280, 24), ContentAlignment.MiddleLeft);
        _keyPill = Pill("", new Rectangle(410, 14, 156, 26), Color.White, _green);
        model.Controls.AddRange(new Control[] { _modelTitle, _keyPill });

        AddInfoLine(model, 50, _green, out _runMark, out _runTitle, out _runBody);
        AddInfoLine(model, 94, _cyan, out _handoffMark, out _handoffTitle, out _handoffBody);
        _apiPill = Pill("", new Rectangle(22, 132, 170, 26), Color.White, _cyan);
        _banksPill = Pill(T("model.banksCards"), new Rectangle(204, 132, 160, 26), Color.White, _indigo);
        _freqPill = Pill(T("model.frequency"), new Rectangle(376, 132, 120, 26), Color.White, _amber);
        model.Controls.AddRange(new Control[] { _apiPill, _banksPill, _freqPill });

        _mainButton = NewButton(T("buttons.start"), new Rectangle(24, 652, 592, 48), _indigo, Color.White, _bold);
        _runButton = NewButton(T("buttons.runOnce"), new Rectangle(24, 716, 286, 38), _card2, _text, _regular);
        _cleanButton = NewButton(T("buttons.clean"), new Rectangle(330, 716, 286, 38), _bg, _amber, _small);
        SetButtonBorder(_cleanButton, _card2, 1);
        _logButton = NewButton(T("buttons.openLog"), new Rectangle(24, 766, 286, 34), _panel, _cyan, _small);
        SetButtonBorder(_logButton, _border, 1);
        _folderButton = NewButton(T("buttons.openFolder"), new Rectangle(330, 766, 286, 34), _panel, _text, _small);
        SetButtonBorder(_folderButton, _border, 1);
        Controls.AddRange(new Control[] { _mainButton, _runButton, _cleanButton, _logButton, _folderButton });

        _startupCheck = new CheckBox
        {
            Bounds = new Rectangle(25, 816, 286, 24),
            Font = _small,
            ForeColor = _muted,
            BackColor = _bg,
            Checked = TestStartup(),
            AutoSize = false
        };
        Controls.Add(_startupCheck);

        _intervalLabel = NewLabel("", _small, _muted, new Rectangle(24, 838, 592, 22), ContentAlignment.TopLeft);
        Controls.Add(_intervalLabel);

        _footer = NewLabel("", _small, _gray, new Rectangle(390, 816, 226, 24), ContentAlignment.MiddleRight);
        Controls.Add(_footer);

        var menu = new ContextMenuStrip();
        _miOpen = new ToolStripMenuItem();
        _miRun = new ToolStripMenuItem();
        _miClean = new ToolStripMenuItem();
        _miQuit = new ToolStripMenuItem();
        menu.Items.AddRange(new ToolStripItem[] { _miOpen, _miRun, _miClean, new ToolStripSeparator(), _miQuit });

        _tray = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };

        _languageButton.Click += (_, _) => ToggleLanguage();
        _mainButton.Click += (_, _) => { if (_running) StopWorker(); else StartWorker(); };
        _runButton.Click += (_, _) => InvokeAgentRun();
        _cleanButton.Click += (_, _) => CleanStuckProcesses();
        _logButton.Click += (_, _) => OpenLog();
        _folderButton.Click += (_, _) => OpenAgentFolder();
        _startupCheck.Click += (_, _) => SetStartup(_startupCheck.Checked);
        _miOpen.Click += (_, _) => ShowFromTray();
        _miRun.Click += (_, _) => InvokeAgentRun();
        _miClean.Click += (_, _) => CleanStuckProcesses();
        _miQuit.Click += (_, _) => QuitForReal();
        _tray.MouseDoubleClick += (_, _) => ShowFromTray();
        FormClosing += OnFormClosing;
    }

    private Label NewLabel(string text, Font font, Color fore, Rectangle bounds, ContentAlignment align, bool transparent = false)
    {
        return new Label
        {
            Text = text,
            Font = font,
            ForeColor = fore,
            BackColor = transparent ? Color.Transparent : Color.Empty,
            Bounds = bounds,
            TextAlign = align,
            AutoEllipsis = true
        };
    }

    private Button NewButton(string text, Rectangle bounds, Color back, Color fore, Font font)
    {
        var button = new RoundedButton
        {
            Text = text,
            Bounds = bounds,
            BackColor = back,
            ForeColor = fore,
            Font = font,
            Cursor = Cursors.Hand,
            Radius = bounds.Height >= 40 ? 10 : 8,
            HoverBackColor = ControlPaint.Light(back, 0.08f)
        };
        return button;
    }

    private static void SetButtonBorder(Button button, Color color, int size)
    {
        if (button is RoundedButton rounded)
        {
            rounded.BorderColor = color;
            rounded.BorderSize = size;
            return;
        }

        button.FlatAppearance.BorderColor = color;
        button.FlatAppearance.BorderSize = size;
    }

    private Label Pill(string text, Rectangle bounds, Color fore, Color back)
    {
        return new PillLabel
        {
            Text = text,
            Bounds = bounds,
            ForeColor = fore,
            BackColor = back,
            Font = _pillFont,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true
        };
    }

    private CardPanel StatCard(Rectangle bounds, Color accent, out Label number, out Label label)
    {
        var card = new CardPanel(_card, _border) { Bounds = bounds, Accent = accent };
        number = NewLabel("0", _stat, accent, new Rectangle(18, 6, bounds.Width - 36, 38), ContentAlignment.MiddleLeft);
        label = NewLabel("", _small, _muted, new Rectangle(18, 49, bounds.Width - 36, 18), ContentAlignment.MiddleLeft);
        card.Controls.Add(number);
        card.Controls.Add(label);
        return card;
    }

    private void AddInfoLine(Control parent, int y, Color accent, out Label mark, out Label title, out Label body)
    {
        mark = NewLabel("\u25CF", new Font("Segoe UI", 9, FontStyle.Regular), accent, new Rectangle(18, y + 4, 18, 18), ContentAlignment.MiddleCenter);
        mark.AutoEllipsis = false;
        title = NewLabel("", _bold, _text, new Rectangle(42, y, 300, 22), ContentAlignment.MiddleLeft);
        body = NewLabel("", _small, _muted, new Rectangle(42, y + 24, 300, 18), ContentAlignment.MiddleLeft);
        parent.Controls.AddRange(new Control[] { mark, title, body });
    }

    private Icon LoadIcon()
    {
        foreach (var path in AssetCandidates("spendwise.ico"))
        {
            if (File.Exists(path))
            {
                try { return new Icon(path); } catch { }
            }
        }

        return SystemIcons.Application;
    }

    private Image? LoadLogo(int size)
    {
        foreach (var path in AssetCandidates("logo-source.png"))
        {
            if (!File.Exists(path)) continue;
            try
            {
                using var src = Image.FromFile(path);
                var bmp = new Bitmap(size, size);
                using var g = Graphics.FromImage(bmp);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var clip = RoundedPath(new Rectangle(0, 0, size, size), 10);
                g.SetClip(clip);
                g.DrawImage(src, 0, 0, size, size);
                return bmp;
            }
            catch { }
        }

        return null;
    }

    private IEnumerable<string> AssetCandidates(string fileName)
    {
        yield return Path.Combine(_workerDir, fileName);
        yield return Path.Combine(_agentDir, "worker", fileName);
        yield return Path.Combine(_agentDir, "Worker Windows App", fileName);
    }

    private static GraphicsPath RoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void WireTimers()
    {
        _loopTimer.Interval = IntervalMinutes * 60 * 1000;
        _loopTimer.Tick += (_, _) =>
        {
            _nextRunAt = DateTime.Now.AddMinutes(IntervalMinutes);
            InvokeAgentRun();
        };

        _countdownTimer.Interval = 30 * 1000;
        _countdownTimer.Tick += (_, _) => UpdateNextRun();
        _countdownTimer.Start();

        _watchdogTimer.Interval = 15 * 1000;
        _watchdogTimer.Tick += (_, _) => WatchdogTick();
        _watchdogTimer.Start();

        _pulseTimer.Interval = 560;
        _pulseTimer.Tick += (_, _) => PulseTick();
        _pulseTimer.Start();

        _refreshTimer.Interval = 10 * 1000;
        _refreshTimer.Tick += (_, _) => RefreshSnapshot();
        _refreshTimer.Start();
    }

    private void ApplyLanguage()
    {
        var he = _i18n.Language == "he";
        var left = he ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
        var right = he ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight;

        Text = T("app.title");
        _tray.Text = T("app.trayText");
        _headerTitle.Text = T("app.title");
        _headerSubtitle.Text = T("header.subtitle");
        _headerPill.Text = T("header.pill");
        _languageButton.Text = T("meta.toggle");
        ApplyHostCopy(he);

        foreach (var label in new[] {
            _headerSubtitle, _statusText, _lastResult, _lastRun, _statusHint, _checksLabel,
            _transactionsLabel, _modelTitle, _runTitle, _runBody, _handoffTitle, _handoffBody,
            _intervalLabel
        })
        {
            label.TextAlign = left;
            label.RightToLeft = he ? RightToLeft.Yes : RightToLeft.No;
        }
        _footer.TextAlign = ContentAlignment.MiddleLeft;
        _footer.RightToLeft = RightToLeft.No;
        LayoutStatusHeader(he, right);
        _startupCheck.RightToLeft = he ? RightToLeft.Yes : RightToLeft.No;
        foreach (var control in new Control[] { _mainButton, _runButton, _cleanButton, _logButton, _folderButton, _keyPill, _apiPill, _banksPill, _freqPill })
        {
            control.RightToLeft = he ? RightToLeft.Yes : RightToLeft.No;
        }
        LayoutInfoLines(he);

        _checksLabel.Text = T("stats.serverChecks");
        _transactionsLabel.Text = T("stats.transactionsSynced");
        _modelTitle.Text = T("model.title");
        _runTitle.Text = T("model.runsTitle");
        _runBody.Text = T("model.runsBody");
        _handoffTitle.Text = T("model.encryptedTitle");
        _handoffBody.Text = T("model.encryptedBody");
        _banksPill.Text = T("model.banksCards");
        _freqPill.Text = T("model.frequency");

        _statusText.Text = T(_statusKey);
        _mainButton.Text = _running ? T("buttons.stop") : T("buttons.start");
        _runButton.Text = T("buttons.runOnce");
        _cleanButton.Text = T("buttons.clean");
        _logButton.Text = T("buttons.openLog");
        _folderButton.Text = T("buttons.openFolder");
        _startupCheck.Text = T("startup.label");
        _intervalLabel.Text = string.Format(T("startup.interval"), IntervalMinutes, MaxRunMinutes);
        _footer.Text = string.Format(T("footer"), _buildVersion);

        _miOpen.Text = T("tray.open");
        _miRun.Text = T("buttons.runOnce");
        _miClean.Text = T("buttons.clean");
        _miQuit.Text = T("tray.quit");

        UpdateNextRun();
        ParseLastResult();
        RefreshConfigPills();
        UpdateBusyState();
    }

    private void ApplyHostCopy(bool he)
    {
        var key = _profile.IsDefaultHost ? "host.default" : "host.general";
        var owner = string.IsNullOrWhiteSpace(_profile.OwnerName) ? "Hananel" : _profile.OwnerName;
        _hostTitle.Text = T(key + "Title");
        _hostBody.Text = string.Format(T(key + "Body"), owner);
        _hostNote.Text = T(key + "Note");
        _hostBadge.Text = T(key + "Badge");

        var align = he ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
        var bodyAlign = he ? ContentAlignment.TopRight : ContentAlignment.TopLeft;
        _hostTitle.TextAlign = align;
        _hostBody.TextAlign = bodyAlign;
        _hostNote.TextAlign = align;
        _hostTitle.RightToLeft = he ? RightToLeft.Yes : RightToLeft.No;
        _hostBody.RightToLeft = he ? RightToLeft.Yes : RightToLeft.No;
        _hostNote.RightToLeft = he ? RightToLeft.Yes : RightToLeft.No;
        _hostBadge.RightToLeft = he ? RightToLeft.Yes : RightToLeft.No;
    }

    private void LayoutInfoLines(bool he)
    {
        LayoutInfoLine(_runMark, _runTitle, _runBody, 50, he);
        LayoutInfoLine(_handoffMark, _handoffTitle, _handoffBody, 94, he);
    }

    private void LayoutStatusHeader(bool he, ContentAlignment nextRunAlign)
    {
        if (he)
        {
            _dot.Bounds = new Rectangle(544, 13, 22, 24);
            _statusText.Bounds = new Rectangle(44, 17, 488, 22);
            _statusText.TextAlign = ContentAlignment.MiddleRight;
            _statusText.RightToLeft = RightToLeft.Yes;
            _nextRun.Bounds = new Rectangle(44, 19, 170, 20);
            _nextRun.TextAlign = nextRunAlign;
            _nextRun.RightToLeft = RightToLeft.Yes;
            return;
        }

        _dot.Bounds = new Rectangle(18, 13, 22, 24);
        _statusText.Bounds = new Rectangle(44, 17, 260, 22);
        _statusText.TextAlign = ContentAlignment.MiddleLeft;
        _statusText.RightToLeft = RightToLeft.No;
        _nextRun.Bounds = new Rectangle(410, 19, 150, 20);
        _nextRun.TextAlign = nextRunAlign;
        _nextRun.RightToLeft = RightToLeft.No;
    }

    private static void LayoutInfoLine(Label mark, Label title, Label body, int y, bool he)
    {
        if (he)
        {
            mark.Bounds = new Rectangle(548, y + 4, 18, 18);
            title.Bounds = new Rectangle(50, y, 486, 22);
            body.Bounds = new Rectangle(50, y + 24, 486, 18);
            title.TextAlign = ContentAlignment.MiddleRight;
            body.TextAlign = ContentAlignment.MiddleRight;
            title.RightToLeft = RightToLeft.Yes;
            body.RightToLeft = RightToLeft.Yes;
            return;
        }

        mark.Bounds = new Rectangle(22, y + 4, 18, 18);
        title.Bounds = new Rectangle(50, y, 500, 22);
        body.Bounds = new Rectangle(50, y + 24, 500, 18);
        title.TextAlign = ContentAlignment.MiddleLeft;
        body.TextAlign = ContentAlignment.MiddleLeft;
        title.RightToLeft = RightToLeft.No;
        body.RightToLeft = RightToLeft.No;
    }

    private void ToggleLanguage()
    {
        _state.Language = _i18n.Language == "he" ? "en" : "he";
        _state.Save(_stateFile);
        _i18n = I18n.Load(_i18nDir, _state.Language);
        ApplyLanguage();
    }

    private string T(string key) => _i18n.T(key);

    private void RefreshSnapshot()
    {
        _checksNumber.Text = _state.TotalRuns.ToString(CultureInfo.InvariantCulture);
        _transactionsNumber.Text = GetSyncTotals().newTxns.ToString(CultureInfo.InvariantCulture);
        RefreshConfigPills();
        if (!_busy) ParseLastResult();
        _startupCheck.Checked = TestStartup();
        _footer.Text = string.Format(T("footer"), _buildVersion);
    }

    private void RefreshConfigPills()
    {
        var config = GetConfigSummary();
        _keyPill.Text = config.KeyLabel;
        _keyPill.BackColor = config.KeyColor;
        _apiPill.Text = config.ApiLabel;
        _apiPill.BackColor = config.ApiColor;
    }

    private void UpdateBusyState()
    {
        _runButton.Enabled = !_busy;
        _runButton.Cursor = _busy ? Cursors.Default : Cursors.Hand;
    }

    private ConfigSummary GetConfigSummary()
    {
        var apiUrl = "";
        var envFile = Path.Combine(_agentDir, ".env");
        if (File.Exists(envFile))
        {
            foreach (var line in File.ReadLines(envFile))
            {
                var match = Regex.Match(line, @"^\s*API_URL\s*=\s*(.+?)\s*$");
                if (!match.Success) continue;
                apiUrl = match.Groups[1].Value.Trim().Trim('"', '\'');
                break;
            }
        }

        var apiLabel = T("config.apiNotSet");
        var apiColor = _amber;
        if (apiUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            apiLabel = T("config.cloudApi");
            apiColor = _cyan;
        }
        else if (Regex.IsMatch(apiUrl, @"^http://(localhost|127\.0\.0\.1|\[::1\])", RegexOptions.IgnoreCase))
        {
            apiLabel = T("config.localApi");
            apiColor = _blue;
        }
        else if (!string.IsNullOrWhiteSpace(apiUrl))
        {
            apiLabel = T("config.unsupportedApi");
            apiColor = _red;
        }

        var keyReady = File.Exists(Path.Combine(_agentDir, "agent-private.key"));
        return new ConfigSummary(
            apiLabel,
            apiColor,
            keyReady ? T("config.keyReady") : T("config.keyMissing"),
            keyReady ? _green : _amber);
    }

    private void SetStatus(string key, Color color)
    {
        _statusKey = key;
        _statusText.Text = T(key);
        _dot.ForeColor = color;
    }

    private void ParseLastResult()
    {
        if (!File.Exists(_logFile))
        {
            _lastResult.Text = T("result.notRunYet");
            _lastResult.ForeColor = _muted;
            return;
        }

        string[] tail;
        try { tail = File.ReadLines(_logFile).TakeLast(40).ToArray(); }
        catch
        {
            _lastResult.Text = T("result.notRunYet");
            _lastResult.ForeColor = _muted;
            return;
        }

        var done = tail.LastOrDefault(l => Regex.IsMatch(l, @"DONE .* \d+ new, \d+ skipped"));
        var none = tail.LastOrDefault(l => l.Contains("no pending jobs", StringComparison.OrdinalIgnoreCase));
        var fail = tail.LastOrDefault(l => l.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                                           l.Contains("FATAL", StringComparison.OrdinalIgnoreCase));
        if (done is not null)
        {
            var match = Regex.Match(done, @"(\d+) new, (\d+) skipped");
            if (match.Success)
            {
                _lastResult.Text = string.Format(T("result.lastSync"), match.Groups[1].Value, match.Groups[2].Value);
                _lastResult.ForeColor = _green;
                return;
            }
        }

        if (fail is not null)
        {
            _lastResult.Text = T("result.failure");
            _lastResult.ForeColor = _red;
        }
        else if (none is not null)
        {
            _lastResult.Text = T("result.upToDate");
            _lastResult.ForeColor = _muted;
        }
        else
        {
            _lastResult.Text = T("result.notRunYet");
            _lastResult.ForeColor = _muted;
        }
    }

    private (int newTxns, int syncs) GetSyncTotals()
    {
        var newTxns = 0;
        var syncs = 0;
        if (!File.Exists(_logFile)) return (0, 0);

        try
        {
            foreach (var line in File.ReadLines(_logFile))
            {
                var match = Regex.Match(line, @"DONE .* (\d+) new, (\d+) skipped");
                if (!match.Success) continue;
                newTxns += int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                syncs++;
            }
        }
        catch { }

        return (newTxns, syncs);
    }

    private void UpdateNextRun()
    {
        if (_running && _nextRunAt is not null)
        {
            var mins = Math.Max(0, (int)Math.Round((_nextRunAt.Value - DateTime.Now).TotalMinutes));
            _nextRun.Text = string.Format(T("status.nextRun"), mins);
        }
        else
        {
            _nextRun.Text = "";
        }
    }

    private void WatchdogTick()
    {
        if (!_busy || _runStartedAt is null) return;
        if ((DateTime.Now - _runStartedAt.Value).TotalMinutes < MaxRunMinutes) return;

        var killed = KillCurrentTree();
        _busy = false;
        _currentProc = null;
        _runStartedAt = null;
        UpdateBusyState();
        _lastResult.Text = string.Format(T("result.timeout"), MaxRunMinutes, killed);
        _lastResult.ForeColor = _red;
        _lastRun.Text = string.Format(T("result.lastRun"), DateTime.Now.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture));
        SetStatus(_running ? "status.running" : "status.idle", _running ? _green : _gray);
        _tray.ShowBalloonTip(4000, T("tray.stuckTitle"), string.Format(T("tray.stuckBody"), MaxRunMinutes), ToolTipIcon.Warning);
    }

    private void PulseTick()
    {
        if (!_running && !_busy) return;
        _pulseOn = !_pulseOn;
        var baseColor = _busy ? _blue : _green;
        _dot.ForeColor = _pulseOn ? baseColor : Blend(baseColor, _card, 0.45);
    }

    private static Color Blend(Color a, Color b, double amount)
    {
        return Color.FromArgb(
            (int)(a.R * amount + b.R * (1 - amount)),
            (int)(a.G * amount + b.G * (1 - amount)),
            (int)(a.B * amount + b.B * (1 - amount)));
    }

    private void InvokeAgentRun()
    {
        if (_busy) return;
        _busy = true;
        UpdateBusyState();
        _runStartedAt = DateTime.Now;
        SetStatus("status.syncing", _blue);
        _lastRun.Text = string.Format(T("result.started"), DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));

        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = Quote(_agentJs),
                    WorkingDirectory = _agentDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };
            proc.Exited += (_, _) => BeginInvoke(new Action(AgentExited));
            proc.Start();
            _currentProc = proc;
        }
        catch
        {
            SetStatus("status.nodeMissing", _red);
            _lastResult.Text = T("result.installNode");
            _lastResult.ForeColor = _red;
            _busy = false;
            UpdateBusyState();
            _runStartedAt = null;
            return;
        }

        _sessionRuns++;
        _state.TotalRuns++;
        _state.Save(_stateFile);
        _checksNumber.Text = _state.TotalRuns.ToString(CultureInfo.InvariantCulture);
    }

    private void AgentExited()
    {
        if (!_busy) return;
        _busy = false;
        UpdateBusyState();
        _currentProc?.Dispose();
        _currentProc = null;
        _runStartedAt = null;
        ParseLastResult();
        _transactionsNumber.Text = GetSyncTotals().newTxns.ToString(CultureInfo.InvariantCulture);
        _lastRun.Text = string.Format(T("result.lastContact"), DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture), _sessionRuns);
        SetStatus(_running ? "status.running" : "status.idle", _running ? _green : _gray);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private void StartWorker()
    {
        _running = true;
        _mainButton.Text = T("buttons.stop");
        _mainButton.BackColor = _card2;
        SetStatus("status.running", _green);
        _nextRunAt = DateTime.Now.AddMinutes(IntervalMinutes);
        UpdateNextRun();
        _loopTimer.Start();
        InvokeAgentRun();
    }

    private void StopWorker()
    {
        _running = false;
        _loopTimer.Stop();
        _nextRunAt = null;
        UpdateNextRun();
        _mainButton.Text = T("buttons.start");
        _mainButton.BackColor = _indigo;
        SetStatus("status.stopped", _gray);
    }

    private void CleanStuckProcesses()
    {
        _cleanButton.Enabled = false;
        var report = new List<string>();
        var active = KillCurrentTree();
        if (active > 0) report.Add(string.Format(T("cleanup.activeRun"), active));

        var orphaned = KillOrphanedAgentProcesses();
        if (orphaned > 0) report.Add(string.Format(T("cleanup.orphans"), orphaned));

        if (File.Exists(_lockFile))
        {
            try
            {
                File.Delete(_lockFile);
                report.Add(T("cleanup.staleLock"));
            }
            catch { }
        }

        var message = report.Count == 0 ? T("cleanup.none") : string.Format(T("cleanup.cleaned"), string.Join(", ", report));
        _lastResult.Text = message;
        _lastResult.ForeColor = _amber;
        SetStatus(_running ? "status.running" : "status.idle", _running ? _green : _gray);
        _cleanButton.Enabled = true;
        MessageBox.Show(message, T("cleanup.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenLog()
    {
        try
        {
            if (!File.Exists(_logFile)) File.WriteAllText(_logFile, "");
            Process.Start(new ProcessStartInfo { FileName = _logFile, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, T("buttons.openLog"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenAgentFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _agentDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, T("buttons.openFolder"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private int KillCurrentTree()
    {
        if (_currentProc is null) return 0;
        try
        {
            if (!_currentProc.HasExited)
            {
                _currentProc.Kill(entireProcessTree: true);
                _currentProc.WaitForExit(2000);
                return 1;
            }
        }
        catch { }
        finally
        {
            _currentProc?.Dispose();
            _currentProc = null;
        }

        return 0;
    }

    private int KillOrphanedAgentProcesses()
    {
        var agent = _agentJs.Replace("'", "''");
        var script = "$n=0; " +
                     "Get-CimInstance Win32_Process -Filter \"Name='node.exe' OR Name='chrome.exe'\" | " +
                     "Where-Object { $_.CommandLine -like '*" + agent + "*' -or $_.CommandLine -like '*.chrome-profile*' } | " +
                     "ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop; $n++ } catch {} }; " +
                     "Write-Output $n";
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + Quote(script),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            if (proc is null) return 0;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return int.TryParse(output.Trim(), out var n) ? n : 0;
        }
        catch
        {
            return 0;
        }
    }

    private bool TestStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("SpendWiseWorker") is not null;
        }
        catch { return false; }
    }

    private void SetStartup(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;
            if (on) key.SetValue("SpendWiseWorker", Quote(Application.ExecutablePath) + " --autostart");
            else key.DeleteValue("SpendWiseWorker", throwOnMissingValue: false);
        }
        catch { }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void QuitForReal()
    {
        _userQuit = true;
        _tray.Visible = false;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_userQuit)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(2000, T("app.title"), T("tray.stillRunning"), ToolTipIcon.Info);
            return;
        }

        KillCurrentTree();
        _tray.Dispose();
    }
}

internal sealed record ConfigSummary(string ApiLabel, Color ApiColor, string KeyLabel, Color KeyColor);

internal sealed class WorkerProfile
{
    public string Mode { get; set; } = "personal";
    public string OwnerName { get; set; } = "";

    public bool IsDefaultHost => Mode.Equals("default-host", StringComparison.OrdinalIgnoreCase);

    public static WorkerProfile Load(string workerDir)
    {
        var path = Path.Combine(workerDir, "worker-profile.json");
        try
        {
            if (!File.Exists(path)) return new WorkerProfile();
            var profile = JsonSerializer.Deserialize<WorkerProfile>(File.ReadAllText(path), JsonOptions());
            if (profile is null) return new WorkerProfile();
            if (string.IsNullOrWhiteSpace(profile.Mode)) profile.Mode = "personal";
            profile.OwnerName = profile.OwnerName?.Trim() ?? "";
            return profile;
        }
        catch
        {
            return new WorkerProfile();
        }
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
}

internal sealed class WorkerState
{
    public int TotalRuns { get; set; }
    public string Language { get; set; } = "en";

    public static WorkerState Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new WorkerState();
            var state = JsonSerializer.Deserialize<WorkerState>(File.ReadAllText(path), JsonOptions());
            if (state is null) return new WorkerState();
            if (state.Language is not "en" and not "he") state.Language = "en";
            return state;
        }
        catch
        {
            return new WorkerState();
        }
    }

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions()));
        }
        catch { }
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}

internal sealed class I18n
{
    private readonly Dictionary<string, string> _values;
    public string Language { get; }

    private I18n(string language, Dictionary<string, string> values)
    {
        Language = language;
        _values = values;
    }

    public static I18n Load(string dir, string language)
    {
        if (language is not "en" and not "he") language = "en";
        var path = Path.Combine(dir, language + ".json");
        if (!File.Exists(path))
        {
            language = "en";
            path = Path.Combine(dir, "en.json");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Flatten("", doc.RootElement, values);
        }
        catch { }

        return new I18n(language, values);
    }

    public string T(string key) => _values.TryGetValue(key, out var value) ? value : key;

    private static void Flatten(string prefix, JsonElement element, Dictionary<string, string> values)
    {
        foreach (var prop in element.EnumerateObject())
        {
            var key = string.IsNullOrEmpty(prefix) ? prop.Name : prefix + "." + prop.Name;
            if (prop.Value.ValueKind == JsonValueKind.Object) Flatten(key, prop.Value, values);
            else if (prop.Value.ValueKind == JsonValueKind.String) values[key] = prop.Value.GetString() ?? "";
        }
    }
}

internal sealed class RoundedButton : Button
{
    private bool _hover;
    public int Radius { get; set; } = 8;
    public int BorderSize { get; set; }
    public Color BorderColor { get; set; } = Color.Transparent;
    public Color HoverBackColor { get; set; }

    public RoundedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        var fill = Enabled ? (_hover ? HoverBackColor : BackColor) : ControlPaint.Dark(BackColor, 0.18f);
        using var path = UiShape.RoundedPath(rect, Radius);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);

        if (BorderSize > 0)
        {
            using var pen = new Pen(BorderColor, BorderSize);
            e.Graphics.DrawPath(pen, path);
        }

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            ClientRectangle,
            Enabled ? ForeColor : ControlPaint.Dark(ForeColor, 0.35f),
            flags);
    }
}

internal sealed class PillLabel : Label
{
    public int Radius { get; set; } = 7;

    public PillLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
        using var path = UiShape.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, flags);
    }
}

internal sealed class GradientHeader : Panel
{
    private readonly Color _left;
    private readonly Color _right;
    private readonly Color _line;

    public GradientHeader(Color left, Color right, Color line)
    {
        _left = left;
        _right = right;
        _line = line;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, _left, _right, 15f);
        e.Graphics.FillRectangle(brush, ClientRectangle);
        using var pen = new Pen(_line);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        base.OnPaint(e);
    }
}

internal sealed class CardPanel : Panel
{
    private readonly Color _back;
    private readonly Color _border;
    public Color? Accent { get; set; }
    public int Radius { get; set; } = 8;

    public CardPanel(Color back, Color border)
    {
        _back = back;
        _border = border;
        BackColor = Color.Transparent;
        DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? _back);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiShape.RoundedPath(rect, Radius);
        using var back = new SolidBrush(_back);
        e.Graphics.FillPath(back, path);
        if (Accent is { } accent)
        {
            using var accentBrush = new SolidBrush(accent);
            var state = e.Graphics.Save();
            e.Graphics.SetClip(path);
            e.Graphics.FillRectangle(accentBrush, 0, 0, 4, Height);
            e.Graphics.Restore(state);
        }

        using var pen = new Pen(_border);
        e.Graphics.DrawPath(pen, path);
        base.OnPaint(e);
    }
}

internal static class UiShape
{
    public static GraphicsPath RoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
