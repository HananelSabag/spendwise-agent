using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SpendWiseWorker;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(a => a.Equals("--smoke", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(WorkerSmoke.Run());
        }

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

internal static class WorkerSmoke
{
    public static int Run()
    {
        try
        {
            var workerDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var agentDir = FindAgentDir(workerDir);
            var agentJs = Path.Combine(agentDir, "src", "agent.js");
            if (!File.Exists(agentJs)) throw new FileNotFoundException("agent.js was not found", agentJs);

            var i18nDir = ResolveI18nDir(workerDir, agentDir);
            foreach (var language in new[] { "en", "he" })
            {
                var i18n = I18n.Load(i18nDir, language);
                if (i18n.T("app.title") == "app.title") throw new InvalidOperationException($"{language}.json is missing app.title");
                if (i18n.T("buttons.start") == "buttons.start") throw new InvalidOperationException($"{language}.json is missing buttons.start");
            }

            _ = WorkerProfile.Load(workerDir);
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "worker-error.log");
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] smoke failed: {ex}{Environment.NewLine}");
            }
            catch { }
            return 1;
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
}

internal sealed class WorkerForm : Form
{
    private const int IntervalMinutes = 30;
    private const int MaxRunMinutes = 6;
    private const int LogTailLines = 80;
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

    private readonly Color _bg = Color.FromArgb(18, 18, 20);
    private readonly Color _card = Color.FromArgb(32, 34, 38);
    private readonly Color _card2 = Color.FromArgb(48, 52, 60);
    private readonly Color _panel = Color.FromArgb(25, 27, 31);
    private readonly Color _border = Color.FromArgb(66, 72, 82);
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
    private PictureBox _logo = null!;
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
    private Label _reportMark = null!;
    private Label _reportTitle = null!;
    private Label _reportBody = null!;
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
    private Panel _pairingOverlay = null!;
    private Label _pairingTitle = null!;
    private Label _pairingBody = null!;
    private Label _pairingCodeLabel = null!;
    private TextBox _pairingCodeInput = null!;
    private Button _pairingButton = null!;
    private Label _pairingStatus = null!;
    private bool _pairingBusy;
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
    private readonly ToolTip _tips = new() { AutomaticDelay = 250, AutoPopDelay = 8000, ReshowDelay = 100 };

    public WorkerForm(string[] args)
    {
        _workerDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _agentDir = FindAgentDir(_workerDir);
        _agentJs = Path.Combine(_agentDir, "src", "agent.js");
        _logFile = Path.Combine(_agentDir, "agent.log");
        _stateFile = Path.Combine(_agentDir, ".worker-state.json");
        _lockFile = Path.Combine(_agentDir, ".agent.lock");
        _i18nDir = ResolveI18nDir(_workerDir, _agentDir);
        _buildVersion = ShortVersion(GetType().Assembly.GetName().Version);

        _state = WorkerState.Load(_stateFile);
        _profile = WorkerProfile.Load(_workerDir);
        _i18n = I18n.Load(_i18nDir, _state.Language);

        _regular = AppFont(10.0f, FontStyle.Regular, "Segoe UI Variable Text", "Segoe UI");
        _bold = AppFont(10.5f, FontStyle.Bold, "Segoe UI Variable Text", "Segoe UI");
        _title = AppFont(15.0f, FontStyle.Bold, "Segoe UI Variable Display", "Segoe UI");
        _stat = AppFont(21.0f, FontStyle.Bold, "Segoe UI Variable Display", "Segoe UI");
        _small = AppFont(8.75f, FontStyle.Regular, "Segoe UI Variable Text", "Segoe UI");
        _pillFont = AppFont(7.8f, FontStyle.Bold, "Segoe UI Variable Text", "Segoe UI");

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;

        BuildUi();
        WireTimers();
        ApplyLanguage();
        ParseLastResult();
        UpdatePairingVisibility();

        if (args.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("-AutoStart", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("autostart", StringComparison.OrdinalIgnoreCase)))
        {
            // Nothing useful to start yet on an un-paired personal install —
            // the user has to open the window and enter a pairing code first.
            if (!RequiresPairing()) StartWorker();
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

    private static string ShortVersion(Version? version)
    {
        if (version is null) return "0.0.0";
        return $"{version.Major}.{version.Minor}.{version.Build}";
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
        ClientSize = new Size(640, 930);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = _bg;
        ForeColor = _text;
        Font = _regular;
        Icon = LoadIcon();

        var header = new GradientHeader(_indigoDeep, _bg, _card2) { Bounds = new Rectangle(0, 0, 640, 96) };
        Controls.Add(header);

        _logo = new PictureBox
        {
            Bounds = new Rectangle(28, 24, 48, 48),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Image = LoadLogo(48)
        };
        header.Controls.Add(_logo);

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
        _statusHint = NewLabel(T("status.hintStopped"), _small, _gray, new Rectangle(44, 97, 500, 18), ContentAlignment.MiddleLeft);
        statusCard.Controls.AddRange(new Control[] { _dot, _statusText, _nextRun, _lastResult, _lastRun, _statusHint });

        var checks = StatCard(new Rectangle(24, 374, 286, 78), _indigo, out _checksNumber, out _checksLabel);
        var txns = StatCard(new Rectangle(330, 374, 286, 78), _green, out _transactionsNumber, out _transactionsLabel);
        Controls.Add(checks);
        Controls.Add(txns);
        _checksNumber.Text = _state.TotalRuns.ToString(CultureInfo.InvariantCulture);
        _transactionsNumber.Text = GetSyncTotals().newTxns.ToString(CultureInfo.InvariantCulture);

        var model = new CardPanel(_panel, _border) { Bounds = new Rectangle(24, 468, 592, 218) };
        Controls.Add(model);
        _modelTitle = NewLabel(T("model.title"), _bold, _text, new Rectangle(22, 16, 340, 24), ContentAlignment.MiddleLeft);
        _keyPill = Pill("", new Rectangle(392, 14, 174, 26), Color.White, _green);
        model.Controls.AddRange(new Control[] { _modelTitle, _keyPill });

        AddInfoLine(model, 52, _green, out _runMark, out _runTitle, out _runBody);
        AddInfoLine(model, 98, _cyan, out _handoffMark, out _handoffTitle, out _handoffBody);
        AddInfoLine(model, 144, _amber, out _reportMark, out _reportTitle, out _reportBody);
        _apiPill = Pill("", new Rectangle(22, 184, 210, 26), Color.White, _cyan);
        _banksPill = Pill(T("model.banksCards"), new Rectangle(244, 184, 156, 26), Color.White, _indigo);
        _freqPill = Pill(T("model.frequency"), new Rectangle(412, 184, 112, 26), Color.White, _amber);
        model.Controls.AddRange(new Control[] { _apiPill, _banksPill, _freqPill });

        _mainButton = NewButton(T("buttons.start"), new Rectangle(24, 704, 592, 48), _indigo, Color.White, _bold);
        _runButton = NewButton(T("buttons.runOnce"), new Rectangle(24, 768, 286, 38), _card2, _text, _regular);
        _cleanButton = NewButton(T("buttons.clean"), new Rectangle(330, 768, 286, 38), _bg, _amber, _small);
        SetButtonBorder(_cleanButton, _card2, 1);
        _logButton = NewButton(T("buttons.openLog"), new Rectangle(24, 818, 286, 34), _panel, _cyan, _small);
        SetButtonBorder(_logButton, _border, 1);
        _folderButton = NewButton(T("buttons.openFolder"), new Rectangle(330, 818, 286, 34), _panel, _text, _small);
        SetButtonBorder(_folderButton, _border, 1);
        Controls.AddRange(new Control[] { _mainButton, _runButton, _cleanButton, _logButton, _folderButton });

        _startupCheck = new CheckBox
        {
            Bounds = new Rectangle(25, 868, 286, 24),
            Font = _small,
            ForeColor = _muted,
            BackColor = _bg,
            Checked = TestStartup(),
            AutoSize = false
        };
        Controls.Add(_startupCheck);

        _intervalLabel = NewLabel("", _small, _muted, new Rectangle(24, 892, 592, 28), ContentAlignment.TopLeft);
        Controls.Add(_intervalLabel);

        _footer = NewLabel("", _small, _gray, new Rectangle(340, 866, 276, 24), ContentAlignment.MiddleRight);
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

        BuildPairingOverlay();
    }

    // ── Pairing (General Worker, before the device is connected) ───────────

    private bool IsPaired => File.Exists(Path.Combine(_agentDir, ".agent-device.json"));

    private bool RequiresPairing() => !_profile.IsDefaultHost && !IsPaired;

    private void BuildPairingOverlay()
    {
        _pairingOverlay = new Panel
        {
            Bounds = new Rectangle(0, 96, 640, 834),
            BackColor = _bg,
        };
        Controls.Add(_pairingOverlay);
        _pairingOverlay.BringToFront();

        var card = new CardPanel(_panel, _border) { Bounds = new Rectangle(24, 40, 592, 300) };
        _pairingOverlay.Controls.Add(card);

        _pairingTitle = NewLabel("", _title, _text, new Rectangle(24, 24, 544, 30), ContentAlignment.MiddleLeft);
        _pairingBody = NewLabel("", _regular, _muted, new Rectangle(24, 62, 544, 44), ContentAlignment.TopLeft);
        _pairingCodeLabel = NewLabel("", _small, _muted, new Rectangle(24, 116, 544, 18), ContentAlignment.MiddleLeft);
        card.Controls.AddRange(new Control[] { _pairingTitle, _pairingBody, _pairingCodeLabel });

        _pairingCodeInput = new TextBox
        {
            Bounds = new Rectangle(24, 138, 544, 34),
            Font = new Font(_regular.FontFamily, 14f, FontStyle.Bold),
            BackColor = _card2,
            ForeColor = _text,
            BorderStyle = BorderStyle.FixedSingle,
            CharacterCasing = CharacterCasing.Upper,
            MaxLength = 8,
            TextAlign = HorizontalAlignment.Center,
        };
        card.Controls.Add(_pairingCodeInput);

        _pairingButton = NewButton("", new Rectangle(24, 182, 544, 44), _indigo, Color.White, _bold);
        card.Controls.Add(_pairingButton);

        _pairingStatus = NewLabel("", _small, _muted, new Rectangle(24, 236, 544, 40), ContentAlignment.TopLeft);
        card.Controls.Add(_pairingStatus);

        _pairingButton.Click += (_, _) => _ = RunPairingAsync();
        _pairingCodeInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = RunPairingAsync(); }
        };
    }

    private void ApplyPairingLanguage(bool he)
    {
        _pairingTitle.Text = T("pairing.title");
        _pairingBody.Text = T("pairing.body");
        _pairingCodeLabel.Text = T("pairing.codeLabel");
        _pairingButton.Text = _pairingBusy ? T("pairing.working") : T("pairing.button");
        foreach (var label in new[] { _pairingTitle, _pairingBody, _pairingCodeLabel, _pairingStatus })
        {
            label.TextAlign = he ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
            label.RightToLeft = he ? RightToLeft.Yes : RightToLeft.No;
        }
        _pairingBody.TextAlign = he ? ContentAlignment.TopRight : ContentAlignment.TopLeft;
        _pairingStatus.TextAlign = he ? ContentAlignment.TopRight : ContentAlignment.TopLeft;
        _pairingCodeInput.RightToLeft = he ? RightToLeft.Yes : RightToLeft.No;
    }

    private void UpdatePairingVisibility()
    {
        var show = RequiresPairing();
        _pairingOverlay.Visible = show;
        if (show) _pairingOverlay.BringToFront();
    }

    private async Task RunPairingAsync()
    {
        if (_pairingBusy) return;
        var code = (_pairingCodeInput.Text ?? "").Trim();
        if (code.Length != 8)
        {
            _pairingStatus.Text = T("pairing.errorFormat");
            _pairingStatus.ForeColor = _red;
            return;
        }

        _pairingBusy = true;
        _pairingButton.Enabled = false;
        _pairingButton.Text = T("pairing.working");
        _pairingStatus.Text = T("pairing.working");
        _pairingStatus.ForeColor = _muted;

        var pairingScript = Path.Combine(_agentDir, "src", "pairing.js");
        try
        {
            var (ok, message) = await Task.Run(() => RunPairingScript(pairingScript, _agentDir, code));
            if (ok)
            {
                _pairingStatus.Text = T("pairing.success");
                _pairingStatus.ForeColor = _green;
                UpdatePairingVisibility();
                RefreshSnapshot();
            }
            else
            {
                _pairingStatus.Text = string.IsNullOrWhiteSpace(message) ? T("pairing.errorGeneric") : message;
                _pairingStatus.ForeColor = _red;
            }
        }
        catch (Exception ex)
        {
            AppendWorkerLog("pairing failed: " + ex);
            _pairingStatus.Text = T("pairing.errorGeneric");
            _pairingStatus.ForeColor = _red;
        }
        finally
        {
            _pairingBusy = false;
            _pairingButton.Enabled = true;
            _pairingButton.Text = T("pairing.button");
        }
    }

    private static (bool ok, string message) RunPairingScript(string pairingScript, string agentDir, string code)
    {
        if (!File.Exists(pairingScript)) return (false, "pairing.js not found");
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = Quote(pairingScript) + " " + Quote(code),
                    WorkingDirectory = agentDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                },
            };
            proc.Start();
            // Drain stdout concurrently (not blocking) so a slow/hung network
            // call inside pairing.js can't wedge this on an unbounded
            // ReadToEnd() — WaitForExit's timeout is what actually bounds it.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            if (!proc.WaitForExit(20_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return (false, "Timed out — check your internet connection and try again");
            }
            var stdout = stdoutTask.GetAwaiter().GetResult();

            var line = stdout.Trim().Split('\n').LastOrDefault(l => l.TrimStart().StartsWith("{"));
            if (line is null) return (false, null!);
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            var error = root.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;
            return (ok, error!);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
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
        body = NewLabel("", _small, _muted, new Rectangle(42, y + 24, 300, 20), ContentAlignment.MiddleLeft);
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

        RightToLeft = he ? RightToLeft.Yes : RightToLeft.No;
        Text = T("app.title");
        _tray.Text = T("app.trayText");
        _headerTitle.Text = T("app.title");
        _headerSubtitle.Text = T(_profile.IsDefaultHost ? "header.defaultSubtitle" : "header.generalSubtitle");
        _headerPill.Text = T(_profile.IsDefaultHost ? "header.defaultPill" : "header.generalPill");
        _languageButton.Text = T("meta.toggle");
        LayoutHeader(he);
        ApplyHostCopy(he);

        foreach (var label in new[] {
            _headerSubtitle, _statusText, _lastResult, _lastRun, _statusHint, _checksLabel,
            _transactionsLabel, _modelTitle, _runTitle, _runBody, _handoffTitle, _handoffBody,
            _reportTitle, _reportBody,
            _intervalLabel
        })
        {
            label.TextAlign = left;
            label.RightToLeft = he ? RightToLeft.Yes : RightToLeft.No;
        }
        _checksNumber.TextAlign = left;
        _transactionsNumber.TextAlign = left;
        _footer.TextAlign = ContentAlignment.MiddleLeft;
        _footer.RightToLeft = RightToLeft.No;
        LayoutStatusHeader(he, right);
        _startupCheck.RightToLeft = he ? RightToLeft.Yes : RightToLeft.No;
        foreach (var control in new Control[] { _languageButton, _mainButton, _runButton, _cleanButton, _logButton, _folderButton, _headerPill, _hostBadge, _keyPill, _apiPill, _banksPill, _freqPill })
        {
            control.RightToLeft = he ? RightToLeft.Yes : RightToLeft.No;
        }
        LayoutInfoLines(he);
        LayoutModelPills(he);

        _checksLabel.Text = T("stats.serverChecks");
        _transactionsLabel.Text = T("stats.transactionsSynced");
        _modelTitle.Text = T("model.title");
        _runTitle.Text = T("model.queueTitle");
        _runBody.Text = T("model.queueBody");
        _handoffTitle.Text = T("model.browserTitle");
        _handoffBody.Text = T("model.browserBody");
        _reportTitle.Text = T("model.reportTitle");
        _reportBody.Text = T("model.reportBody");
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
        UpdateStatusHint();
        UpdateActionTooltips();

        _miOpen.Text = T("tray.open");
        _miRun.Text = T("buttons.runOnce");
        _miClean.Text = T("buttons.clean");
        _miQuit.Text = T("tray.quit");

        UpdateNextRun();
        ParseLastResult();
        RefreshConfigPills();
        UpdateBusyState();
        ApplyPairingLanguage(he);
        UpdatePairingVisibility();
    }

    private void LayoutHeader(bool he)
    {
        if (he)
        {
            _logo.Bounds = new Rectangle(564, 24, 48, 48);
            _headerTitle.Bounds = new Rectangle(224, 24, 322, 28);
            _headerSubtitle.Bounds = new Rectangle(204, 56, 342, 20);
            _languageButton.Bounds = new Rectangle(28, 18, 76, 28);
            _headerPill.Bounds = new Rectangle(28, 56, 158, 26);
            _headerTitle.TextAlign = ContentAlignment.MiddleRight;
            _headerSubtitle.TextAlign = ContentAlignment.MiddleRight;
            _headerTitle.RightToLeft = RightToLeft.Yes;
            _headerSubtitle.RightToLeft = RightToLeft.Yes;
            return;
        }

        _logo.Bounds = new Rectangle(28, 24, 48, 48);
        _headerTitle.Bounds = new Rectangle(94, 24, 300, 28);
        _headerSubtitle.Bounds = new Rectangle(96, 56, 320, 20);
        _languageButton.Bounds = new Rectangle(536, 18, 76, 28);
        _headerPill.Bounds = new Rectangle(454, 56, 158, 26);
        _headerTitle.TextAlign = ContentAlignment.MiddleLeft;
        _headerSubtitle.TextAlign = ContentAlignment.MiddleLeft;
        _headerTitle.RightToLeft = RightToLeft.No;
        _headerSubtitle.RightToLeft = RightToLeft.No;
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
        if (he)
        {
            _hostBadge.Bounds = new Rectangle(24, 18, 144, 28);
            _hostTitle.Bounds = new Rectangle(192, 18, 376, 24);
            _hostBody.Bounds = new Rectangle(96, 48, 472, 40);
            _hostNote.Bounds = new Rectangle(96, 88, 472, 18);
        }
        else
        {
            _hostTitle.Bounds = new Rectangle(24, 18, 360, 24);
            _hostBody.Bounds = new Rectangle(24, 48, 420, 40);
            _hostNote.Bounds = new Rectangle(24, 88, 520, 18);
            _hostBadge.Bounds = new Rectangle(424, 18, 144, 28);
        }
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
        LayoutInfoLine(_runMark, _runTitle, _runBody, 52, he);
        LayoutInfoLine(_handoffMark, _handoffTitle, _handoffBody, 98, he);
        LayoutInfoLine(_reportMark, _reportTitle, _reportBody, 144, he);
    }

    private void LayoutModelPills(bool he)
    {
        if (he)
        {
            _modelTitle.Bounds = new Rectangle(214, 16, 352, 24);
            _keyPill.Bounds = new Rectangle(22, 14, 174, 26);
            _apiPill.Bounds = new Rectangle(346, 184, 220, 26);
            _banksPill.Bounds = new Rectangle(178, 184, 156, 26);
            _freqPill.Bounds = new Rectangle(54, 184, 112, 26);
            return;
        }

        _modelTitle.Bounds = new Rectangle(22, 16, 340, 24);
        _keyPill.Bounds = new Rectangle(392, 14, 174, 26);
        _apiPill.Bounds = new Rectangle(22, 184, 210, 26);
        _banksPill.Bounds = new Rectangle(244, 184, 156, 26);
        _freqPill.Bounds = new Rectangle(412, 184, 112, 26);
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
        if (_busy) RefreshRunProgress();
        else ParseLastResult();
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

    private void RunOnUi(Action action)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(action);
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void AppendWorkerLog(string message)
    {
        try
        {
            File.AppendAllText(_logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WORKER {message}{Environment.NewLine}");
        }
        catch
        {
            try
            {
                File.AppendAllText(Path.Combine(_workerDir, "worker-error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }

    private void SoftNotice(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _lastResult.Text = message;
        _lastResult.ForeColor = icon == ToolTipIcon.Error ? _red : _amber;
        try { _tray.ShowBalloonTip(3000, title, message, icon); } catch { }
    }

    private void UpdateBusyState()
    {
        _runButton.Enabled = !_busy;
        _runButton.Cursor = _busy ? Cursors.Default : Cursors.Hand;
        UpdateStatusHint();
        UpdateActionTooltips();
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
        UpdateStatusHint();
        UpdateActionTooltips();
    }

    private void SetRestingStatus()
    {
        SetStatus(_running ? "status.running" : "status.stopped", _running ? _green : _gray);
    }

    private void UpdateStatusHint()
    {
        if (_statusHint is null) return;
        if (_busy)
        {
            _statusHint.Text = T("status.hintBusy");
            return;
        }

        _statusHint.Text = _running ? T("status.hintRunning") : T("status.hintStopped");
    }

    private void UpdateActionTooltips()
    {
        if (_mainButton is null) return;
        _tips.SetToolTip(_mainButton, T(_running ? "tips.stopService" : "tips.startService"));
        _tips.SetToolTip(_runButton, T("tips.checkNow"));
        _tips.SetToolTip(_cleanButton, T("tips.clean"));
        _tips.SetToolTip(_logButton, T("tips.openLog"));
        _tips.SetToolTip(_folderButton, T("tips.openFolder"));
    }

    private void ParseLastResult()
    {
        ApplyLogSnapshot(ReadLogSnapshot(), busy: false);
    }

    private void RefreshRunProgress()
    {
        var snapshot = ReadLogSnapshot();
        if (snapshot.Kind is LogEventKind.Claimed or LogEventKind.Scraping or LogEventKind.Warmup or LogEventKind.Pausing or LogEventKind.Done)
        {
            SetStatus("status.syncing", _blue);
        }
        else if (snapshot.Kind is LogEventKind.Cooldown)
        {
            SetStatus("status.running", _green);
        }
        else if (snapshot.Kind is LogEventKind.Failure or LogEventKind.Timeout)
        {
            SetStatus("status.syncing", _amber);
        }
        else
        {
            SetStatus("status.checking", _blue);
        }

        ApplyLogSnapshot(snapshot, busy: true);
    }

    private LogSnapshot ReadLogSnapshot()
    {
        if (!File.Exists(_logFile)) return new LogSnapshot(LogEventKind.None);

        string[] tail;
        try { tail = File.ReadLines(_logFile).TakeLast(LogTailLines).ToArray(); }
        catch { return new LogSnapshot(LogEventKind.None); }

        var recentCooldown = CountLatestCooldownDeclines(tail);
        if (recentCooldown > 0)
        {
            return new LogSnapshot(LogEventKind.Cooldown, Count: recentCooldown);
        }

        foreach (var line in tail.Reverse())
        {
            var noRunnable = Regex.Match(line, @"no (?:runnable jobs after cooldown|sync requests can run now) \((\d+) (?:declined|skipped)", RegexOptions.IgnoreCase);
            if (noRunnable.Success)
            {
                return new LogSnapshot(
                    LogEventKind.Cooldown,
                    Count: int.Parse(noRunnable.Groups[1].Value, CultureInfo.InvariantCulture));
            }

            var cleanup = Regex.Match(line, @"WORKER cleanup:\s*(.+)$");
            if (cleanup.Success) return new LogSnapshot(LogEventKind.Cleanup, Message: cleanup.Groups[1].Value.Trim());

            if (line.Contains("watchdog cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return new LogSnapshot(LogEventKind.Timeout);
            }

            if (line.Contains("another instance is running", StringComparison.OrdinalIgnoreCase))
            {
                return new LogSnapshot(LogEventKind.Locked);
            }

            if (line.Contains("duplicate sync request", StringComparison.OrdinalIgnoreCase))
            {
                return new LogSnapshot(LogEventKind.Duplicate);
            }

            if (line.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("FATAL", StringComparison.OrdinalIgnoreCase))
            {
                return new LogSnapshot(LogEventKind.Failure);
            }

            var done = Regex.Match(line, @"DONE .*?(\d+) new, (\d+) skipped");
            if (done.Success)
            {
                return new LogSnapshot(
                    LogEventKind.Done,
                    NewCount: int.Parse(done.Groups[1].Value, CultureInfo.InvariantCulture),
                    SkippedCount: int.Parse(done.Groups[2].Value, CultureInfo.InvariantCulture));
            }

            if (line.Contains("no pending jobs", StringComparison.OrdinalIgnoreCase))
            {
                return new LogSnapshot(LogEventKind.NonePending);
            }

            var claimed = Regex.Match(line, @"(?:claimed|received) (\d+) (?:job|sync request)", RegexOptions.IgnoreCase);
            if (claimed.Success)
            {
                return new LogSnapshot(
                    LogEventKind.Claimed,
                    Count: int.Parse(claimed.Groups[1].Value, CultureInfo.InvariantCulture));
            }

            var scraping = Regex.Match(line, @"\[job:(\d+)\]\s+scraping\s+([a-z0-9_-]+)", RegexOptions.IgnoreCase);
            if (scraping.Success)
            {
                return new LogSnapshot(LogEventKind.Scraping, Source: scraping.Groups[2].Value, JobId: scraping.Groups[1].Value);
            }

            var warmup = Regex.Match(line, @"\[([a-z0-9_-]+)\]\s+warming up Cloudflare", RegexOptions.IgnoreCase);
            if (warmup.Success)
            {
                return new LogSnapshot(LogEventKind.Warmup, Source: warmup.Groups[1].Value);
            }

            var pausing = Regex.Match(line, @"pausing (\d+)s before next job", RegexOptions.IgnoreCase);
            if (pausing.Success)
            {
                return new LogSnapshot(
                    LogEventKind.Pausing,
                    Count: int.Parse(pausing.Groups[1].Value, CultureInfo.InvariantCulture));
            }
        }

        return new LogSnapshot(LogEventKind.None);
    }

    private void ApplyLogSnapshot(LogSnapshot snapshot, bool busy)
    {
        switch (snapshot.Kind)
        {
            case LogEventKind.Done:
                _lastResult.Text = string.Format(T("result.lastSync"), snapshot.NewCount, snapshot.SkippedCount);
                _lastResult.ForeColor = _green;
                return;
            case LogEventKind.NonePending:
                _lastResult.Text = T("result.upToDate");
                _lastResult.ForeColor = _muted;
                return;
            case LogEventKind.Failure:
                _lastResult.Text = T("result.failure");
                _lastResult.ForeColor = _red;
                return;
            case LogEventKind.Cleanup:
                _lastResult.Text = string.Format(T("result.cleanup"), snapshot.Message);
                _lastResult.ForeColor = _amber;
                return;
            case LogEventKind.Timeout:
                _lastResult.Text = T("result.timeoutShort");
                _lastResult.ForeColor = _red;
                return;
            case LogEventKind.Locked:
                _lastResult.Text = T("result.locked");
                _lastResult.ForeColor = _amber;
                return;
            case LogEventKind.Duplicate:
                _lastResult.Text = T("result.alreadyRunning");
                _lastResult.ForeColor = _amber;
                return;
            case LogEventKind.Claimed:
                _lastResult.Text = string.Format(T("result.claimed"), snapshot.Count);
                _lastResult.ForeColor = _blue;
                return;
            case LogEventKind.Scraping:
                _lastResult.Text = string.Format(T("result.scraping"), PrettySource(snapshot.Source));
                _lastResult.ForeColor = _blue;
                return;
            case LogEventKind.Warmup:
                _lastResult.Text = string.Format(T("result.openingBrowser"), PrettySource(snapshot.Source));
                _lastResult.ForeColor = _blue;
                return;
            case LogEventKind.Pausing:
                _lastResult.Text = string.Format(T("result.pausing"), snapshot.Count);
                _lastResult.ForeColor = _muted;
                return;
            case LogEventKind.Cooldown:
                _lastResult.Text = string.Format(T("result.cooldown"), snapshot.Count);
                _lastResult.ForeColor = _amber;
                return;
            default:
                _lastResult.Text = busy ? T("result.checking") : T("result.notRunYet");
                _lastResult.ForeColor = _muted;
                return;
        }
    }

    private static int CountLatestCooldownDeclines(string[] tail)
    {
        var count = 0;
        for (var i = tail.Length - 1; i >= 0; i--)
        {
            var line = tail[i];
            if (line.Contains("inside 3h cooldown", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("too soon since last successful sync", StringComparison.OrdinalIgnoreCase))
            {
                count++;
                continue;
            }

            if (count > 0)
            {
                if (Regex.IsMatch(line, @"claimed \d+ job", RegexOptions.IgnoreCase)) return count;
                if (line.Contains("cooldown declined", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("synced recently", StringComparison.OrdinalIgnoreCase)) continue;
                return 0;
            }

            if (line.Contains("run finished", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(line, @"DONE .*?\d+ new, \d+ skipped", RegexOptions.IgnoreCase) ||
                line.Contains("no pending jobs", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }
        }

        return count;
    }

    private static string PrettySource(string source)
    {
        return string.IsNullOrWhiteSpace(source) ? "" : source.Replace('_', ' ');
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
        if (_busy)
        {
            _nextRun.Text = T("status.now");
        }
        else if (_running && _nextRunAt is not null)
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
        RemoveAgentLock();
        AppendWorkerLog($"watchdog cancelled a stuck sync after {MaxRunMinutes} minutes; killed={killed}");
        _busy = false;
        _currentProc = null;
        _runStartedAt = null;
        UpdateBusyState();
        _lastResult.Text = string.Format(T("result.timeout"), MaxRunMinutes, killed);
        _lastResult.ForeColor = _red;
        _lastRun.Text = string.Format(T("result.lastRun"), DateTime.Now.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture));
        SetRestingStatus();
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
        if (_busy)
        {
            AppendWorkerLog("duplicate sync request ignored because a run is already active");
            _lastResult.Text = T("result.alreadyRunning");
            _lastResult.ForeColor = _amber;
            if (_running)
            {
                _nextRunAt = DateTime.Now.AddMinutes(IntervalMinutes);
                UpdateNextRun();
            }
            return;
        }

        if (!File.Exists(_agentJs))
        {
            AppendWorkerLog("agent entry file was not found: " + _agentJs);
            SetStatus(_running ? "status.running" : "status.stopped", _red);
            _lastResult.Text = T("result.launchFailed");
            _lastResult.ForeColor = _red;
            return;
        }

        _busy = true;
        UpdateBusyState();
        _runStartedAt = DateTime.Now;
        SetStatus("status.checking", _blue);
        _lastResult.Text = T("result.checking");
        _lastResult.ForeColor = _blue;
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
            proc.Exited += (_, _) => RunOnUi(AgentExited);
            proc.Start();
            _currentProc = proc;
        }
        catch (Exception ex)
        {
            AppendWorkerLog("failed to start agent: " + ex);
            SetStatus(ex is Win32Exception ? "status.nodeMissing" : (_running ? "status.running" : "status.stopped"), _red);
            _lastResult.Text = ex is Win32Exception ? T("result.installNode") : T("result.launchFailed");
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
        var exitCode = 0;
        try { exitCode = _currentProc?.ExitCode ?? 0; } catch { }
        _busy = false;
        UpdateBusyState();
        _currentProc?.Dispose();
        _currentProc = null;
        _runStartedAt = null;
        ParseLastResult();
        if (exitCode != 0)
        {
            AppendWorkerLog("agent exited with code " + exitCode.ToString(CultureInfo.InvariantCulture));
            if (_lastResult.ForeColor != _red)
            {
                _lastResult.Text = T("result.failure");
                _lastResult.ForeColor = _red;
            }
        }
        _transactionsNumber.Text = GetSyncTotals().newTxns.ToString(CultureInfo.InvariantCulture);
        _lastRun.Text = string.Format(T("result.lastContact"), DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture), _sessionRuns);
        SetRestingStatus();
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
        if (_busy)
        {
            _lastResult.Text = T("result.stoppedAfterCurrent");
            _lastResult.ForeColor = _amber;
            SetStatus("status.syncing", _blue);
        }
        else
        {
            SetStatus("status.stopped", _gray);
        }
    }

    private void CleanStuckProcesses()
    {
        _cleanButton.Enabled = false;
        var report = new List<string>();
        var active = KillCurrentTree();
        if (active > 0) report.Add(string.Format(T("cleanup.activeRun"), active));
        if (active > 0 || _currentProc is null)
        {
            _busy = false;
            _runStartedAt = null;
            UpdateBusyState();
        }

        var orphaned = KillOrphanedAgentProcesses();
        if (orphaned > 0) report.Add(string.Format(T("cleanup.orphans"), orphaned));

        if (RemoveAgentLock()) report.Add(T("cleanup.staleLock"));

        var message = report.Count == 0 ? T("cleanup.none") : string.Format(T("cleanup.cleaned"), string.Join(", ", report));
        _lastResult.Text = message;
        _lastResult.ForeColor = _amber;
        SetRestingStatus();
        _cleanButton.Enabled = true;
        AppendWorkerLog("cleanup: " + message);
        try { _tray.ShowBalloonTip(3000, T("cleanup.title"), message, ToolTipIcon.Info); } catch { }
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
            AppendWorkerLog("open log failed: " + ex);
            SoftNotice(T("buttons.openLog"), ex.Message, ToolTipIcon.Error);
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
            AppendWorkerLog("open folder failed: " + ex);
            SoftNotice(T("buttons.openFolder"), ex.Message, ToolTipIcon.Error);
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

    private bool RemoveAgentLock()
    {
        if (!File.Exists(_lockFile)) return false;
        try
        {
            File.Delete(_lockFile);
            return true;
        }
        catch
        {
            return false;
        }
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
            // Must actually point at THIS install, not just "some value exists" —
            // a stale entry from an old launcher (e.g. a pre-C# PowerShell/VBS
            // version) would otherwise show the checkbox checked while Windows
            // actually launches something else entirely on login.
            var value = key?.GetValue("SpendWiseWorker") as string;
            return value is not null && value.Contains(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
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

        var killed = KillCurrentTree();
        if (killed > 0) RemoveAgentLock();
        _tray.Dispose();
    }
}

internal sealed record ConfigSummary(string ApiLabel, Color ApiColor, string KeyLabel, Color KeyColor);

internal enum LogEventKind
{
    None,
    Done,
    NonePending,
    Failure,
    Cleanup,
    Timeout,
    Locked,
    Duplicate,
    Claimed,
    Scraping,
    Warmup,
    Pausing,
    Cooldown
}

internal sealed record LogSnapshot(
    LogEventKind Kind,
    string Message = "",
    int Count = 0,
    int NewCount = 0,
    int SkippedCount = 0,
    string Source = "",
    string JobId = "");

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
