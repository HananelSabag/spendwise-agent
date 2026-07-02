# ============================================================================
#  SpendWise Worker — tray control panel for the bank sync agent
#  by Hananel Sabag
#
#  Runs the sync agent on a 30-minute interval, shows live status, run
#  history, and a next-run countdown. Minimises to the system tray.
#  Optional "launch on Windows startup" so it survives reboots.
# ============================================================================

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

# ── Paths ───────────────────────────────────────────────────────────────────
$WorkerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$AgentDir  = Split-Path -Parent $WorkerDir
$AgentJs   = Join-Path $AgentDir 'src\agent.js'
$LogFile   = Join-Path $AgentDir 'agent.log'
$StateFile = Join-Path $AgentDir '.worker-state.json'
$IconFile  = Join-Path $WorkerDir 'spendwise.ico'
$IntervalMinutes = 30

# ── Palette (SpendWise) ─────────────────────────────────────────────────────
$cBg      = [System.Drawing.Color]::FromArgb(15, 23, 42)     # slate-900
$cCard    = [System.Drawing.Color]::FromArgb(30, 41, 59)     # slate-800
$cCard2   = [System.Drawing.Color]::FromArgb(51, 65, 85)     # slate-700
$cText    = [System.Drawing.Color]::FromArgb(241, 245, 249)  # slate-100
$cMuted   = [System.Drawing.Color]::FromArgb(148, 163, 184)  # slate-400
$cIndigo  = [System.Drawing.Color]::FromArgb(99, 102, 241)   # indigo-500
$cGreen   = [System.Drawing.Color]::FromArgb(16, 185, 129)
$cGray    = [System.Drawing.Color]::FromArgb(100, 116, 139)
$cRed     = [System.Drawing.Color]::FromArgb(239, 68, 68)
$cBlue    = [System.Drawing.Color]::FromArgb(59, 130, 246)
$cAmber   = [System.Drawing.Color]::FromArgb(245, 158, 11)

$fRegular = New-Object System.Drawing.Font('Segoe UI', 9.5)
$fBold    = New-Object System.Drawing.Font('Segoe UI Semibold', 10)
$fTitle   = New-Object System.Drawing.Font('Segoe UI Semibold', 14)
$fStat    = New-Object System.Drawing.Font('Segoe UI', 16, [System.Drawing.FontStyle]::Bold)
$fSmall   = New-Object System.Drawing.Font('Segoe UI', 8.5)

# ── State ───────────────────────────────────────────────────────────────────
$script:running     = $false
$script:sessionRuns = 0
$script:busy        = $false
$script:userQuit    = $false
$script:nextRunAt   = $null

function Load-State {
  if (Test-Path $StateFile) {
    try { return Get-Content $StateFile -Raw | ConvertFrom-Json } catch { }
  }
  return [pscustomobject]@{ totalRuns = 0 }
}
function Save-State($s) {
  try { $s | ConvertTo-Json | Set-Content $StateFile -Encoding utf8 } catch { }
}
$script:state = Load-State

# ── Icon (real SpendWise logo, with graceful fallback) ──────────────────────
$appIcon = $null
try { if (Test-Path $IconFile) { $appIcon = New-Object System.Drawing.Icon($IconFile) } } catch { }
if (-not $appIcon) { $appIcon = [System.Drawing.SystemIcons]::Application }

# ── Form ────────────────────────────────────────────────────────────────────
$form = New-Object System.Windows.Forms.Form
$form.Text = 'SpendWise Worker'
$form.ClientSize = New-Object System.Drawing.Size(380, 508)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedSingle'
$form.MaximizeBox = $false
$form.BackColor = $cBg
$form.ForeColor = $cText
$form.Font = $fRegular
$form.Icon = $appIcon

# ── Header: logo + title ────────────────────────────────────────────────────
$logoBox = New-Object System.Windows.Forms.PictureBox
$logoBox.Size = New-Object System.Drawing.Size(44, 44)
$logoBox.Location = New-Object System.Drawing.Point(20, 18)
$logoBox.SizeMode = 'Zoom'
try { $logoBox.Image = $appIcon.ToBitmap() } catch { }
$form.Controls.Add($logoBox)

$hdrTitle = New-Object System.Windows.Forms.Label
$hdrTitle.Text = 'SpendWise Worker'
$hdrTitle.Font = $fTitle
$hdrTitle.ForeColor = $cText
$hdrTitle.AutoSize = $true
$hdrTitle.Location = New-Object System.Drawing.Point(74, 20)
$form.Controls.Add($hdrTitle)

$hdrSub = New-Object System.Windows.Forms.Label
$hdrSub.Text = 'Bank sync agent'
$hdrSub.Font = $fSmall
$hdrSub.ForeColor = $cMuted
$hdrSub.AutoSize = $true
$hdrSub.Location = New-Object System.Drawing.Point(76, 48)
$form.Controls.Add($hdrSub)

# ── Status card ─────────────────────────────────────────────────────────────
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

# ── Stats row ───────────────────────────────────────────────────────────────
function New-StatCard($x, $labelText) {
  $panel = New-Object System.Windows.Forms.Panel
  $panel.Location = New-Object System.Drawing.Point($x, 200)
  $panel.Size = New-Object System.Drawing.Size(164, 68)
  $panel.BackColor = $cCard

  $num = New-Object System.Windows.Forms.Label
  $num.Text = '0'
  $num.Font = $fStat
  $num.ForeColor = $cText
  $num.AutoSize = $true
  $num.Location = New-Object System.Drawing.Point(14, 8)
  $panel.Controls.Add($num)

  $lbl = New-Object System.Windows.Forms.Label
  $lbl.Text = $labelText
  $lbl.Font = $fSmall
  $lbl.ForeColor = $cMuted
  $lbl.AutoSize = $true
  $lbl.Location = New-Object System.Drawing.Point(14, 42)
  $panel.Controls.Add($lbl)

  return @{ Panel = $panel; Num = $num }
}

$statA = New-StatCard 20 'Runs this session'
$statB = New-StatCard 196 'Total runs'
$statB.Num.Text = "$($script:state.totalRuns)"
$form.Controls.Add($statA.Panel)
$form.Controls.Add($statB.Panel)

# ── Buttons ─────────────────────────────────────────────────────────────────
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

# ── Startup toggle + info ───────────────────────────────────────────────────
$chkStartup = New-Object System.Windows.Forms.CheckBox
$chkStartup.Text = ' Launch automatically when Windows starts'
$chkStartup.Font = $fRegular
$chkStartup.ForeColor = $cMuted
$chkStartup.AutoSize = $true
$chkStartup.Location = New-Object System.Drawing.Point(22, 386)
$form.Controls.Add($chkStartup)

$intervalLbl = New-Object System.Windows.Forms.Label
$intervalLbl.Text = "Checks for work every $IntervalMinutes minutes. Only contacts your bank when a sync is queued (max ~2/day per bank)."
$intervalLbl.Font = $fSmall
$intervalLbl.ForeColor = $cMuted
$intervalLbl.Size = New-Object System.Drawing.Size(340, 34)
$intervalLbl.Location = New-Object System.Drawing.Point(22, 416)
$form.Controls.Add($intervalLbl)

$footer = New-Object System.Windows.Forms.Label
$footer.Text = 'SpendWise · by Hananel Sabag'
$footer.Font = $fSmall
$footer.ForeColor = $cGray
$footer.AutoSize = $true
$footer.Location = New-Object System.Drawing.Point(22, 462)
$form.Controls.Add($footer)

# ── Tray icon ───────────────────────────────────────────────────────────────
$tray = New-Object System.Windows.Forms.NotifyIcon
$tray.Icon = $appIcon
$tray.Text = 'SpendWise Worker'
$tray.Visible = $true

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$miOpen = $menu.Items.Add('Open')
$miRun  = $menu.Items.Add('Sync once now')
$menu.Items.Add('-') | Out-Null
$miQuit = $menu.Items.Add('Quit')
$tray.ContextMenuStrip = $menu

# ── Helpers ─────────────────────────────────────────────────────────────────
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

# ── Run the agent (hidden, non-blocking) ────────────────────────────────────
function Invoke-AgentRun {
  if ($script:busy) { return }
  $script:busy = $true
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
    return
  }

  $script:sessionRuns++
  $statA.Num.Text = "$($script:sessionRuns)"
  $script:state.totalRuns = [int]$script:state.totalRuns + 1
  $statB.Num.Text = "$($script:state.totalRuns)"
  Save-State $script:state

  $waitTimer = New-Object System.Windows.Forms.Timer
  $waitTimer.Interval = 1500
  $waitTimer.Add_Tick({
    if ($proc.HasExited) {
      $waitTimer.Stop()
      $script:busy = $false
      Parse-LastResult
      $lastRun.Text = "Last run $(Get-Date -Format 'dd/MM HH:mm')"
      if ($script:running) { Set-Status 'Running' $cGreen } else { Set-Status 'Idle' $cGray }
    }
  })
  $waitTimer.Start()
}

# ── Interval loop + countdown ───────────────────────────────────────────────
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

# ── Windows startup registration ────────────────────────────────────────────
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
    # reboot, minimised to the tray — no click needed.
    Set-ItemProperty -Path $RunKey -Name $RunName -Value ('wscript.exe "' + $Launcher + '" autostart')
  } else {
    Remove-ItemProperty -Path $RunKey -Name $RunName -ErrorAction SilentlyContinue
  }
}
$chkStartup.Checked = Test-Startup

# ── Wiring ──────────────────────────────────────────────────────────────────
$btnMain.Add_Click({ if ($script:running) { Stop-Worker } else { Start-Worker } })
$btnRun.Add_Click({ Invoke-AgentRun })
$chkStartup.Add_Click({ Set-Startup $chkStartup.Checked })

$miOpen.Add_Click({ $form.Show(); $form.WindowState = 'Normal'; $form.Activate() })
$miRun.Add_Click({ Invoke-AgentRun })
$miQuit.Add_Click({ $script:userQuit = $true; $tray.Visible = $false; $form.Close() })
$tray.Add_MouseDoubleClick({ $form.Show(); $form.WindowState = 'Normal'; $form.Activate() })

# Close (X) hides to tray so the worker keeps running; Quit (tray menu) exits.
$form.Add_FormClosing({
  param($s, $e)
  if ($script:userQuit -ne $true) {
    $e.Cancel = $true
    $form.Hide()
    $tray.ShowBalloonTip(2000, 'SpendWise Worker', 'Still running in the tray.', 'Info')
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

[System.Windows.Forms.Application]::EnableVisualStyles()
[System.Windows.Forms.Application]::Run($form)
$tray.Visible = $false
