# ============================================================================
#  SpendWise Worker - tray control panel for the bank sync agent
#  by Hananel Sabag
#
#  A small always-available window that runs the sync agent on an interval,
#  shows live status + run history, and can launch itself on Windows startup.
#  Minimises to the system tray - no taskbar clutter.
#
#  Reliability features:
#   - True single-instance enforcement (named Mutex) - a second launch shows
#     a clear message and exits instead of creating a confusing second GUI.
#   - Hang watchdog - a run that exceeds MaxRunMinutes is force-killed
#     (whole process tree: node.exe + any spawned chrome.exe) and reported,
#     instead of the UI showing "Syncing..." forever.
#   - "Clean up stuck processes" - on demand, kills every node.exe/chrome.exe
#     that belongs to this agent (matched by command line), anywhere on the
#     system, plus clears a stale lock file. For when something got left
#     behind by a crash, a force-quit, or a previous session.
# ============================================================================

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

# -- Single-instance guard (must happen before any UI is created) -----------
$MutexName = 'Global\SpendWiseWorkerSingleton'
$createdNew = $false
$mutex = New-Object System.Threading.Mutex($true, $MutexName, [ref]$createdNew)
if (-not $createdNew) {
  [System.Windows.Forms.MessageBox]::Show(
    "SpendWise Worker is already running." + [Environment]::NewLine + "Check your system tray (bottom-right, near the clock).",
    'SpendWise Worker',
    [System.Windows.Forms.MessageBoxButtons]::OK,
    [System.Windows.Forms.MessageBoxIcon]::Information
  ) | Out-Null
  exit 0
}

# -- Paths -------------------------------------------------------------------
$WorkerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$AgentDir  = Split-Path -Parent $WorkerDir
$AgentJs   = Join-Path $AgentDir 'src\agent.js'
$LogFile   = Join-Path $AgentDir 'agent.log'
$StateFile = Join-Path $AgentDir '.worker-state.json'
$LockFile  = Join-Path $AgentDir '.agent.lock'
$IconFile  = Join-Path $WorkerDir 'spendwise.ico'
$LogoPng   = Join-Path $WorkerDir 'logo-source.png'   # 1024x1024 source logo
$IntervalMinutes = 30
$MaxRunMinutes   = 6   # a scrape+report never legitimately takes this long

# -- Palette (SpendWise) -----------------------------------------------------
$cBg      = [System.Drawing.Color]::FromArgb(15, 23, 42)     # slate-900
$cCard    = [System.Drawing.Color]::FromArgb(30, 41, 59)     # slate-800
$cCard2   = [System.Drawing.Color]::FromArgb(51, 65, 85)     # slate-700
$cText    = [System.Drawing.Color]::FromArgb(241, 245, 249)  # slate-100
$cMuted   = [System.Drawing.Color]::FromArgb(148, 163, 184)  # slate-400
$cIndigo  = [System.Drawing.Color]::FromArgb(99, 102, 241)   # indigo-500
$cIndigoDeep = [System.Drawing.Color]::FromArgb(79, 70, 229) # indigo-600 (header gradient)
$cGreen   = [System.Drawing.Color]::FromArgb(16, 185, 129)
$cGray    = [System.Drawing.Color]::FromArgb(100, 116, 139)
$cRed     = [System.Drawing.Color]::FromArgb(239, 68, 68)
$cBlue    = [System.Drawing.Color]::FromArgb(59, 130, 246)
$cAmber   = [System.Drawing.Color]::FromArgb(245, 158, 11)

# Fonts — Windows 11 "Segoe UI Variable" is softer and rounder than the plain
# "Segoe UI" that made the panel feel blocky. Fall back gracefully on older
# Windows. "Display" is tuned for big text (title/numbers), "Text" for body.
$script:installedFonts = ([System.Drawing.Text.InstalledFontCollection]::new()).Families.Name
function New-AppFont([single]$size, [System.Drawing.FontStyle]$style, [string[]]$prefer) {
  foreach ($name in $prefer) {
    if ($script:installedFonts -contains $name) {
      try { return New-Object System.Drawing.Font($name, $size, $style) } catch { }
    }
  }
  return New-Object System.Drawing.Font('Segoe UI', $size, $style)
}
$famDisplay = @('Segoe UI Variable Display', 'Segoe UI')
$famText    = @('Segoe UI Variable Text', 'Segoe UI')

$fRegular = New-AppFont 10.0 ([System.Drawing.FontStyle]::Regular) $famText
$fBold    = New-AppFont 10.5 ([System.Drawing.FontStyle]::Bold)    $famText
$fTitle   = New-AppFont 15.0 ([System.Drawing.FontStyle]::Bold)    $famDisplay
$fStat    = New-AppFont 21.0 ([System.Drawing.FontStyle]::Bold)    $famDisplay
$fSmall   = New-AppFont 8.75 ([System.Drawing.FontStyle]::Regular) $famText
$fPill    = New-AppFont 7.75 ([System.Drawing.FontStyle]::Bold)    $famText

# -- State -------------------------------------------------------------------
$script:running       = $false
$script:sessionRuns   = 0
$script:busy          = $false
$script:userQuit      = $false
$script:nextRunAt     = $null
$script:currentProc   = $null
$script:currentPid    = $null
$script:runStartedAt  = $null

function Load-State {
  if (Test-Path $StateFile) {
    try { return Get-Content $StateFile -Raw | ConvertFrom-Json } catch { }
  }
  return [pscustomobject]@{ totalRuns = 0 }
}

# Lifetime traffic totals, recomputed from the whole agent log (cheap, and
# avoids drift/double-counting a running counter would suffer). Every run is
# one server check; a "DONE" line means a bank was actually synced.
function Get-SyncTotals {
  $newTxns = 0; $syncs = 0
  if (Test-Path $LogFile) {
    try {
      Get-Content $LogFile -ErrorAction Stop | ForEach-Object {
        if ($_ -match 'DONE .* (\d+) new, (\d+) skipped') { $newTxns += [int]$Matches[1]; $syncs++ }
      }
    } catch { }
  }
  return @{ newTxns = $newTxns; syncs = $syncs }
}
function Save-State($s) {
  try { $s | ConvertTo-Json | Set-Content $StateFile -Encoding utf8 } catch { }
}
$script:state = Load-State

# -- Logo / icon helpers ------------------------------------------------------
# High-quality downscale of the 1024px source PNG to a square bitmap. Rendering
# straight from the PNG (instead of Icon.ToBitmap(), which upsizes a tiny 16/32
# frame) is what makes the in-window logo crisp instead of a noisy square.
function Get-LogoBitmap([int]$size) {
  $img = [System.Drawing.Image]::FromFile($LogoPng)
  try {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($img, 0, 0, $size, $size)
    $g.Dispose()
    return $bmp
  } finally { $img.Dispose() }
}

# Rounded-square version of the logo for the window header (soft, modern).
function Get-LogoRounded([int]$size, [int]$radius) {
  try {
    $src = Get-LogoBitmap $size
    $out = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($out)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $d = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.SetClip($path)
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose(); $path.Dispose(); $src.Dispose()
    return $out
  } catch { return $null }
}

# App icon: build a real HICON from the source PNG. GDI+ can't always hand
# WinForms a taskbar-size (32px) frame out of a PNG-compressed .ico — the tray's
# 16px loads, but the taskbar/title-bar falls back to the generic Windows icon.
# An HICON rasterized from a bitmap always shows correctly everywhere.
function Get-AppIcon {
  try {
    if (Test-Path $LogoPng) {
      $bmp = Get-LogoBitmap 64
      $hicon = $bmp.GetHicon()
      $bmp.Dispose()
      return [System.Drawing.Icon]::FromHandle($hicon)
    }
  } catch { }
  try {
    if (Test-Path $IconFile) {
      $bytes = [System.IO.File]::ReadAllBytes($IconFile)
      $ms = New-Object System.IO.MemoryStream(,$bytes)
      return New-Object System.Drawing.Icon($ms)
    }
  } catch { }
  return [System.Drawing.SystemIcons]::Application
}
$appIcon = Get-AppIcon

# -- Process-tree helpers -----------------------------------------------------
# Kill a process and every descendant (node.exe -> chrome.exe -> chrome
# renderer children). WMI gives us ParentProcessId to walk the tree.
function Stop-ProcessTree([int]$ProcessId) {
  $killed = 0
  try {
    $children = Get-CimInstance Win32_Process -Filter "ParentProcessId=$ProcessId" -ErrorAction SilentlyContinue
    foreach ($child in $children) {
      $killed += Stop-ProcessTree -ProcessId $child.ProcessId
    }
  } catch { }
  try {
    Stop-Process -Id $ProcessId -Force -ErrorAction Stop
    $killed++
  } catch { }
  return $killed
}

# Find every node.exe running OUR agent.js, and every chrome.exe running from
# OUR profile dir, regardless of who started them (this session, a previous
# one, or a crashed worker). Matched by command line, not by our own PID
# tracking, so it also catches truly orphaned processes.
function Find-AgentProcesses {
  $matches = @()
  try {
    $procs = Get-CimInstance Win32_Process -Filter "Name='node.exe' OR Name='chrome.exe'" -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
      $cmd = $p.CommandLine
      if (-not $cmd) { continue }
      if ($cmd -like "*$AgentJs*" -or $cmd -like '*.chrome-profile*') {
        $matches += $p
      }
    }
  } catch { }
  return $matches
}

function Invoke-ZombieCleanup {
  $report = @()

  # 1. Whatever THIS worker is currently tracking
  if ($script:currentPid) {
    $n = Stop-ProcessTree -ProcessId $script:currentPid
    if ($n -gt 0) { $report += "$n process(es) from the active run" }
    $script:currentPid = $null
    $script:currentProc = $null
    $script:busy = $false
  }

  # 2. Anything else matching our agent, anywhere on the system
  $orphans = Find-AgentProcesses
  $orphanCount = 0
  foreach ($p in $orphans) {
    try { Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop; $orphanCount++ } catch { }
  }
  if ($orphanCount -gt 0) { $report += "$orphanCount orphaned process(es)" }

  # 3. Stale lock file - agent.js is PID-aware and self-heals this too, but
  #    clear it here for instant feedback instead of waiting for the next run.
  if (Test-Path $LockFile) {
    try { Remove-Item $LockFile -Force; $report += 'stale lock file' } catch { }
  }

  if ($report.Count -eq 0) { return 'Nothing to clean up - all clear.' }
  return "Cleaned up: $($report -join ', ')."
}

# -- Form --------------------------------------------------------------------
$form = New-Object System.Windows.Forms.Form
$form.Text = 'SpendWise Worker'
$form.ClientSize = New-Object System.Drawing.Size(380, 566)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedSingle'
$form.MaximizeBox = $false
$form.BackColor = $cBg
$form.ForeColor = $cText
$form.Font = $fRegular
$form.Icon = $appIcon

# -- Header: gradient strip with logo + title --------------------------------
$header = New-Object System.Windows.Forms.Panel
$header.Location = New-Object System.Drawing.Point(0, 0)
$header.Size = New-Object System.Drawing.Size(380, 76)
$header.Add_Paint({
  $g = $_.Graphics
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $rect = New-Object System.Drawing.Rectangle 0, 0, $header.Width, $header.Height
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $cIndigoDeep, $cBg, 15.0)
  $g.FillRectangle($brush, $rect)
  $brush.Dispose()
  # hairline separator at the bottom edge
  $pen = New-Object System.Drawing.Pen($cCard2, 1)
  $g.DrawLine($pen, 0, ($header.Height - 1), $header.Width, ($header.Height - 1))
  $pen.Dispose()
})
$form.Controls.Add($header)

$logoBox = New-Object System.Windows.Forms.PictureBox
$logoBox.Size = New-Object System.Drawing.Size(46, 46)
$logoBox.Location = New-Object System.Drawing.Point(20, 15)
$logoBox.SizeMode = 'Zoom'
$logoBox.BackColor = [System.Drawing.Color]::Transparent
$logoImg = Get-LogoRounded 46 11
if ($logoImg) { $logoBox.Image = $logoImg } else { try { $logoBox.Image = Get-LogoBitmap 46 } catch { } }
$header.Controls.Add($logoBox)

$hdrTitle = New-Object System.Windows.Forms.Label
$hdrTitle.Text = 'SpendWise Worker'
$hdrTitle.Font = $fTitle
$hdrTitle.ForeColor = [System.Drawing.Color]::White
$hdrTitle.BackColor = [System.Drawing.Color]::Transparent
$hdrTitle.AutoSize = $true
$hdrTitle.Location = New-Object System.Drawing.Point(78, 17)
$header.Controls.Add($hdrTitle)

$hdrSub = New-Object System.Windows.Forms.Label
$hdrSub.Text = 'Bank sync agent'
$hdrSub.Font = $fSmall
$hdrSub.ForeColor = [System.Drawing.Color]::FromArgb(199, 210, 254)  # indigo-200
$hdrSub.BackColor = [System.Drawing.Color]::Transparent
$hdrSub.AutoSize = $true
$hdrSub.Location = New-Object System.Drawing.Point(80, 45)
$header.Controls.Add($hdrSub)

# -- Status card -------------------------------------------------------------
$statusCard = New-Object System.Windows.Forms.Panel
$statusCard.Location = New-Object System.Drawing.Point(20, 80)
$statusCard.Size = New-Object System.Drawing.Size(340, 108)
$statusCard.BackColor = $cCard
$form.Controls.Add($statusCard)

$dot = New-Object System.Windows.Forms.Label
$dot.Text = [char]0x25CF
$dot.Font = New-Object System.Drawing.Font('Segoe UI', 15)
$dot.ForeColor = $cGray
$dot.AutoSize = $true
$dot.Location = New-Object System.Drawing.Point(14, 12)
$statusCard.Controls.Add($dot)

$statusText = New-Object System.Windows.Forms.Label
$statusText.Text = 'Stopped'
$statusText.Font = $fBold
$statusText.ForeColor = $cText
$statusText.AutoSize = $true
$statusText.Location = New-Object System.Drawing.Point(42, 17)
$statusCard.Controls.Add($statusText)

$nextRunLbl = New-Object System.Windows.Forms.Label
$nextRunLbl.Text = ''
$nextRunLbl.Font = $fSmall
$nextRunLbl.ForeColor = $cMuted
$nextRunLbl.AutoSize = $true
$nextRunLbl.TextAlign = 'TopRight'
$nextRunLbl.Location = New-Object System.Drawing.Point(210, 20)
$statusCard.Controls.Add($nextRunLbl)

$lastResult = New-Object System.Windows.Forms.Label
$lastResult.Text = 'Not run yet'
$lastResult.Font = $fRegular
$lastResult.ForeColor = $cMuted
$lastResult.AutoSize = $false
$lastResult.Size = New-Object System.Drawing.Size(312, 22)
$lastResult.Location = New-Object System.Drawing.Point(44, 48)
$statusCard.Controls.Add($lastResult)

$lastRun = New-Object System.Windows.Forms.Label
$lastRun.Text = ''
$lastRun.Font = $fSmall
$lastRun.ForeColor = $cGray
$lastRun.AutoSize = $true
$lastRun.Location = New-Object System.Drawing.Point(44, 76)
$statusCard.Controls.Add($lastRun)

# -- Stats row (traffic at a glance) -----------------------------------------
# A colored accent bar down the left edge of each card gives the numbers some
# life and ties them to their meaning (indigo = talking to the server,
# green = money data actually pulled in).
function New-StatCard($x, $labelText, $numColor, $accent) {
  $panel = New-Object System.Windows.Forms.Panel
  $panel.Location = New-Object System.Drawing.Point($x, 200)
  $panel.Size = New-Object System.Drawing.Size(164, 68)
  $panel.BackColor = $cCard
  $panel.Add_Paint({
    $b = New-Object System.Drawing.SolidBrush $accent
    $_.Graphics.FillRectangle($b, (New-Object System.Drawing.Rectangle 0, 0, 3, 68))
    $b.Dispose()
  }.GetNewClosure())

  $num = New-Object System.Windows.Forms.Label
  $num.Text = '0'
  $num.Font = $fStat
  $num.ForeColor = $numColor
  $num.AutoSize = $true
  $num.Location = New-Object System.Drawing.Point(14, 6)
  $panel.Controls.Add($num)

  $lbl = New-Object System.Windows.Forms.Label
  $lbl.Text = $labelText
  $lbl.Font = $fSmall
  $lbl.ForeColor = $cMuted
  $lbl.AutoSize = $true
  $lbl.Location = New-Object System.Drawing.Point(14, 44)
  $panel.Controls.Add($lbl)

  return @{ Panel = $panel; Num = $num }
}

$statA = New-StatCard 20  'Server checks'       $cIndigo $cIndigo
$statB = New-StatCard 196 'Transactions synced' $cGreen  $cGreen
$statA.Num.Text = "$($script:state.totalRuns)"
$statB.Num.Text = "$((Get-SyncTotals).newTxns)"
$form.Controls.Add($statA.Panel)
$form.Controls.Add($statB.Panel)

# -- Buttons -----------------------------------------------------------------
$btnMain = New-Object System.Windows.Forms.Button
$btnMain.Text = 'Start Worker'
$btnMain.Font = $fBold
$btnMain.ForeColor = [System.Drawing.Color]::White
$btnMain.BackColor = $cIndigo
$btnMain.FlatStyle = 'Flat'
$btnMain.FlatAppearance.BorderSize = 0
$btnMain.Size = New-Object System.Drawing.Size(340, 44)
$btnMain.Location = New-Object System.Drawing.Point(20, 282)
$btnMain.Cursor = 'Hand'
$form.Controls.Add($btnMain)

$btnRun = New-Object System.Windows.Forms.Button
$btnRun.Text = 'Sync once now'
$btnRun.Font = $fRegular
$btnRun.ForeColor = $cText
$btnRun.BackColor = $cCard2
$btnRun.FlatStyle = 'Flat'
$btnRun.FlatAppearance.BorderSize = 0
$btnRun.Size = New-Object System.Drawing.Size(340, 36)
$btnRun.Location = New-Object System.Drawing.Point(20, 334)
$btnRun.Cursor = 'Hand'
$form.Controls.Add($btnRun)

$btnClean = New-Object System.Windows.Forms.Button
$btnClean.Text = 'Clean up stuck processes'
$btnClean.Font = $fSmall
$btnClean.ForeColor = $cAmber
$btnClean.BackColor = $cBg
$btnClean.FlatStyle = 'Flat'
$btnClean.FlatAppearance.BorderSize = 1
$btnClean.FlatAppearance.BorderColor = $cCard2
$btnClean.Size = New-Object System.Drawing.Size(340, 30)
$btnClean.Location = New-Object System.Drawing.Point(20, 378)
$btnClean.Cursor = 'Hand'
$form.Controls.Add($btnClean)

# -- Startup toggle + info ---------------------------------------------------
$chkStartup = New-Object System.Windows.Forms.CheckBox
$chkStartup.Text = ' Launch automatically when Windows starts'
$chkStartup.Font = $fRegular
$chkStartup.ForeColor = $cMuted
$chkStartup.AutoSize = $true
$chkStartup.Location = New-Object System.Drawing.Point(22, 424)
$form.Controls.Add($chkStartup)

$intervalLbl = New-Object System.Windows.Forms.Label
$intervalLbl.Text = "Checks for work every $IntervalMinutes minutes. Only contacts your bank when a sync is queued (max ~2/day per bank). A stuck sync is auto-cancelled after $MaxRunMinutes minutes."
$intervalLbl.Font = $fSmall
$intervalLbl.ForeColor = $cMuted
$intervalLbl.Size = New-Object System.Drawing.Size(340, 48)
$intervalLbl.Location = New-Object System.Drawing.Point(22, 454)
$form.Controls.Add($intervalLbl)

$footer = New-Object System.Windows.Forms.Label
$footer.Text = 'SpendWise . by Hananel Sabag . build 26.7.4.1732'
$footer.Font = $fSmall
$footer.ForeColor = $cGray
$footer.AutoSize = $true
$footer.Location = New-Object System.Drawing.Point(22, 522)
$form.Controls.Add($footer)

# -- Tray icon ---------------------------------------------------------------
$tray = New-Object System.Windows.Forms.NotifyIcon
$tray.Icon = $appIcon
$tray.Text = 'SpendWise Worker'
$tray.Visible = $true

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$miOpen = $menu.Items.Add('Open')
$miRun  = $menu.Items.Add('Sync once now')
$miClean = $menu.Items.Add('Clean up stuck processes')
$menu.Items.Add('-') | Out-Null
$miQuit = $menu.Items.Add('Quit')
$tray.ContextMenuStrip = $menu

# -- Helpers -----------------------------------------------------------------
function Set-Status($text, $color) {
  $statusText.Text = $text
  $dot.ForeColor = $color
}

function Parse-LastResult {
  if (-not (Test-Path $LogFile)) { return }
  try { $tail = Get-Content $LogFile -Tail 40 -ErrorAction Stop } catch { return }
  $done = $tail | Where-Object { $_ -match 'DONE .* (\d+) new, (\d+) skipped' } | Select-Object -Last 1
  $none = $tail | Where-Object { $_ -match 'no pending jobs' } | Select-Object -Last 1
  $fail = $tail | Where-Object { $_ -match 'FAILED|FATAL' } | Select-Object -Last 1
  if ($done -and ($done -match '(\d+) new, (\d+) skipped')) {
    $lastResult.Text = "Last sync: $($Matches[1]) new transactions, $($Matches[2]) already known"
    $lastResult.ForeColor = $cGreen
  } elseif ($fail) {
    $lastResult.Text = 'Last run had a failure - see agent.log'
    $lastResult.ForeColor = $cRed
  } elseif ($none) {
    $lastResult.Text = 'Up to date - no work was waiting'
    $lastResult.ForeColor = $cMuted
  }
}

function Update-NextRun {
  if ($script:running -and $script:nextRunAt) {
    $mins = [math]::Max(0, [math]::Round(($script:nextRunAt - (Get-Date)).TotalMinutes))
    $nextRunLbl.Text = "next check in ${mins}m"
  } else {
    $nextRunLbl.Text = ''
  }
}

# -- Hang watchdog ------------------------------------------------------------
$watchdogTimer = New-Object System.Windows.Forms.Timer
$watchdogTimer.Interval = 15 * 1000
$watchdogTimer.Add_Tick({
  if (-not $script:busy -or -not $script:runStartedAt) { return }
  $elapsedMin = ((Get-Date) - $script:runStartedAt).TotalMinutes
  if ($elapsedMin -ge $MaxRunMinutes) {
    $killed = if ($script:currentPid) { Stop-ProcessTree -ProcessId $script:currentPid } else { 0 }
    $script:busy = $false
    $script:currentProc = $null
    $script:currentPid = $null
    $lastResult.Text = "Timed out after ${MaxRunMinutes}m - cancelled ($killed process(es) stopped)"
    $lastResult.ForeColor = $cRed
    $lastRun.Text = "Last run $(Get-Date -Format 'dd/MM HH:mm')"
    if ($script:running) { Set-Status 'Running' $cGreen } else { Set-Status 'Idle' $cGray }
    $tray.ShowBalloonTip(4000, 'SpendWise Worker', "A sync got stuck and was cancelled after ${MaxRunMinutes} minutes.", 'Warning')
  }
})
$watchdogTimer.Start()

# -- Pulsing status dot (a little life while it's alive) ----------------------
# When the worker is running (or mid-sync) the dot breathes between its full
# colour and a dimmed version; when stopped, Set-Status paints it static gray.
$script:pulseOn = $false
$pulseTimer = New-Object System.Windows.Forms.Timer
$pulseTimer.Interval = 560
$pulseTimer.Add_Tick({
  if (-not $script:running -and -not $script:busy) { return }
  $script:pulseOn = -not $script:pulseOn
  $base = if ($script:busy) { $cBlue } else { $cGreen }
  if ($script:pulseOn) {
    $dot.ForeColor = $base
  } else {
    $dot.ForeColor = [System.Drawing.Color]::FromArgb(
      [int]($base.R * 0.45 + $cCard.R * 0.55),
      [int]($base.G * 0.45 + $cCard.G * 0.55),
      [int]($base.B * 0.45 + $cCard.B * 0.55))
  }
})
$pulseTimer.Start()

# -- Run the agent (hidden, non-blocking) ------------------------------------
function Invoke-AgentRun {
  if ($script:busy) { return }
  $script:busy = $true
  $script:runStartedAt = Get-Date
  Set-Status 'Syncing...' $cBlue
  $lastRun.Text = "Started $(Get-Date -Format 'HH:mm:ss')"

  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = 'node'
  $psi.Arguments = '"' + $AgentJs + '"'
  $psi.WorkingDirectory = $AgentDir
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  try {
    $proc = [System.Diagnostics.Process]::Start($psi)
  } catch {
    Set-Status 'Node.js not found' $cRed
    $lastResult.Text = 'Install Node.js, then try again'
    $lastResult.ForeColor = $cRed
    $script:busy = $false
    $script:runStartedAt = $null
    return
  }

  $script:currentProc = $proc
  $script:currentPid = $proc.Id

  # Every run is one server check. Transactions-synced (statB) is recomputed
  # from the log when the run finishes (see the completion handler below).
  $script:sessionRuns++
  $script:state.totalRuns = [int]$script:state.totalRuns + 1
  $statA.Num.Text = "$($script:state.totalRuns)"
  Save-State $script:state

  # NOTE: this Tick handler runs long after Invoke-AgentRun has returned, from
  # a dead call frame - PowerShell script blocks do NOT close over a function's
  # local variables (no lexical capture without .GetNewClosure()), so any bare
  # local var referenced here (e.g. $proc, $waitTimer) resolves to $null at
  # invoke time, not compile time. Use $script:currentProc (real state) and
  # $this (the Timer instance itself, auto-bound by the WinForms event) instead
  # of the locals - both are safely resolvable from any call context.
  $waitTimer = New-Object System.Windows.Forms.Timer
  $waitTimer.Interval = 1500
  $waitTimer.Add_Tick({
    if (-not $script:busy) { $this.Stop(); return }  # watchdog already handled it
    if ($script:currentProc.HasExited) {
      $this.Stop()
      $script:busy = $false
      $script:currentProc = $null
      $script:currentPid = $null
      $script:runStartedAt = $null
      Parse-LastResult
      $statB.Num.Text = "$((Get-SyncTotals).newTxns)"
      $lastRun.Text = "Last contact $(Get-Date -Format 'HH:mm') · $($script:sessionRuns) checks this session"
      if ($script:running) { Set-Status 'Running' $cGreen } else { Set-Status 'Idle' $cGray }
    }
  })
  $waitTimer.Start()
}

# -- Interval loop + countdown -----------------------------------------------
$loopTimer = New-Object System.Windows.Forms.Timer
$loopTimer.Interval = $IntervalMinutes * 60 * 1000
$loopTimer.Add_Tick({
  $script:nextRunAt = (Get-Date).AddMinutes($IntervalMinutes)
  Invoke-AgentRun
})

$countdownTimer = New-Object System.Windows.Forms.Timer
$countdownTimer.Interval = 30 * 1000
$countdownTimer.Add_Tick({ Update-NextRun })
$countdownTimer.Start()

function Start-Worker {
  $script:running = $true
  $btnMain.Text = 'Stop Worker'
  $btnMain.BackColor = $cCard2
  Set-Status 'Running' $cGreen
  $script:nextRunAt = (Get-Date).AddMinutes($IntervalMinutes)
  Update-NextRun
  $loopTimer.Start()
  Invoke-AgentRun   # immediate first run
}
function Stop-Worker {
  $script:running = $false
  $loopTimer.Stop()
  $script:nextRunAt = $null
  Update-NextRun
  $btnMain.Text = 'Start Worker'
  $btnMain.BackColor = $cIndigo
  Set-Status 'Stopped' $cGray
}

# -- Windows startup registration --------------------------------------------
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$RunName = 'SpendWiseWorker'
$Launcher = Join-Path $WorkerDir 'SpendWise-Worker.vbs'

function Test-Startup {
  try { return [bool]((Get-ItemProperty -Path $RunKey -Name $RunName -ErrorAction Stop).$RunName) }
  catch { return $false }
}
function Set-Startup($on) {
  if ($on) {
    # "autostart" makes the worker begin its sync loop immediately after a
    # reboot, minimised to the tray - no click needed.
    Set-ItemProperty -Path $RunKey -Name $RunName -Value ('wscript.exe "' + $Launcher + '" autostart')
  } else {
    Remove-ItemProperty -Path $RunKey -Name $RunName -ErrorAction SilentlyContinue
  }
}
$chkStartup.Checked = Test-Startup

# -- Wiring ------------------------------------------------------------------
$btnMain.Add_Click({ if ($script:running) { Stop-Worker } else { Start-Worker } })
$btnRun.Add_Click({ Invoke-AgentRun })
$btnClean.Add_Click({
  $btnClean.Enabled = $false
  $msg = Invoke-ZombieCleanup
  $lastResult.Text = $msg
  $lastResult.ForeColor = $cAmber
  if ($script:running) { Set-Status 'Running' $cGreen } else { Set-Status 'Idle' $cGray }
  $btnClean.Enabled = $true
  [System.Windows.Forms.MessageBox]::Show($msg, 'Clean up stuck processes', 'OK', 'Information') | Out-Null
})
$chkStartup.Add_Click({ Set-Startup $chkStartup.Checked })

$miOpen.Add_Click({ $form.Show(); $form.WindowState = 'Normal'; $form.Activate() })
$miRun.Add_Click({ Invoke-AgentRun })
$miClean.Add_Click({ $btnClean.PerformClick() })
$miQuit.Add_Click({ $script:userQuit = $true; $tray.Visible = $false; $form.Close() })
$tray.Add_MouseDoubleClick({ $form.Show(); $form.WindowState = 'Normal'; $form.Activate() })

# Close (X) hides to tray so the worker keeps running; Quit (tray menu) exits.
$form.Add_FormClosing({
  param($s, $e)
  if ($script:userQuit -ne $true) {
    $e.Cancel = $true
    $form.Hide()
    $tray.ShowBalloonTip(2000, 'SpendWise Worker', 'Still running in the tray.', 'Info')
  } else {
    # Real quit: don't leave a sync running headless with no UI to watch it.
    if ($script:currentPid) { Stop-ProcessTree -ProcessId $script:currentPid | Out-Null }
  }
})

Parse-LastResult

# Auto-start the worker loop when launched at Windows startup (silent path),
# so a reboot resumes syncing without any click.
if ($env:SPENDWISE_WORKER_AUTOSTART -eq '1' -or ($args -contains '-AutoStart')) {
  Start-Worker
  $form.WindowState = 'Minimized'
  $form.Hide()
}

try {
  [System.Windows.Forms.Application]::EnableVisualStyles()
  [System.Windows.Forms.Application]::Run($form)
} finally {
  $tray.Visible = $false
  if ($script:currentPid) { Stop-ProcessTree -ProcessId $script:currentPid | Out-Null }
  $mutex.ReleaseMutex()
  $mutex.Dispose()
}




