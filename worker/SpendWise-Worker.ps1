# ============================================================================
#  SpendWise Worker — tray control panel for the bank sync agent
#  by Hananel Sabag
#
#  A small always-available window that runs the sync agent on an interval,
#  shows live status + run history, and can launch itself on Windows startup.
#  Minimises to the system tray — no taskbar clutter.
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
$IntervalMinutes = 30

# ── Palette (SpendWise dark theme) ──────────────────────────────────────────
$cBg      = [System.Drawing.Color]::FromArgb(15, 23, 42)     # slate-900
$cCard    = [System.Drawing.Color]::FromArgb(30, 41, 59)     # slate-800
$cCard2   = [System.Drawing.Color]::FromArgb(51, 65, 85)     # slate-700
$cText    = [System.Drawing.Color]::FromArgb(241, 245, 249)  # slate-100
$cMuted   = [System.Drawing.Color]::FromArgb(148, 163, 184)  # slate-400
$cIndigo  = [System.Drawing.Color]::FromArgb(99, 102, 241)   # indigo-500
$cIndigoH = [System.Drawing.Color]::FromArgb(79, 70, 229)    # indigo-600
$cGreen   = [System.Drawing.Color]::FromArgb(16, 185, 129)
$cGray    = [System.Drawing.Color]::FromArgb(100, 116, 139)
$cRed     = [System.Drawing.Color]::FromArgb(239, 68, 68)
$cBlue    = [System.Drawing.Color]::FromArgb(59, 130, 246)

$fRegular = New-Object System.Drawing.Font('Segoe UI', 9)
$fBold    = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
$fTitle   = New-Object System.Drawing.Font('Segoe UI Semibold', 13)
$fBig     = New-Object System.Drawing.Font('Segoe UI', 20, [System.Drawing.FontStyle]::Bold)
$fSmall   = New-Object System.Drawing.Font('Segoe UI', 8)

# ── State ───────────────────────────────────────────────────────────────────
$script:running   = $false
$script:sessionRuns = 0
$script:busy      = $false
$script:userQuit  = $false

function Load-State {
  if (Test-Path $StateFile) {
    try { return Get-Content $StateFile -Raw | ConvertFrom-Json } catch { }
  }
  return [pscustomobject]@{ totalRuns = 0; lastResult = ''; lastRun = '' }
}
function Save-State($s) {
  try { $s | ConvertTo-Json | Set-Content $StateFile -Encoding utf8 } catch { }
}
$script:state = Load-State

# ── Rounded-panel helper ────────────────────────────────────────────────────
function New-Card($x, $y, $w, $h, $color) {
  $p = New-Object System.Windows.Forms.Panel
  $p.Location = New-Object System.Drawing.Point($x, $y)
  $p.Size     = New-Object System.Drawing.Size($w, $h)
  $p.BackColor = $color
  return $p
}

# ── Form ────────────────────────────────────────────────────────────────────
$form = New-Object System.Windows.Forms.Form
$form.Text = 'SpendWise Worker'
$form.Size = New-Object System.Drawing.Size(380, 500)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedSingle'
$form.MaximizeBox = $false
$form.BackColor = $cBg
$form.ForeColor = $cText
$form.Font = $fRegular

# Header
$hdrIcon = New-Object System.Windows.Forms.Label
$hdrIcon.Text = 'S'
$hdrIcon.Font = $fBig
$hdrIcon.ForeColor = [System.Drawing.Color]::White
$hdrIcon.BackColor = $cIndigo
$hdrIcon.TextAlign = 'MiddleCenter'
$hdrIcon.Size = New-Object System.Drawing.Size(44, 44)
$hdrIcon.Location = New-Object System.Drawing.Point(20, 18)
$form.Controls.Add($hdrIcon)

$hdrTitle = New-Object System.Windows.Forms.Label
$hdrTitle.Text = 'SpendWise Worker'
$hdrTitle.Font = $fTitle
$hdrTitle.ForeColor = $cText
$hdrTitle.AutoSize = $true
$hdrTitle.Location = New-Object System.Drawing.Point(74, 22)
$form.Controls.Add($hdrTitle)

$hdrSub = New-Object System.Windows.Forms.Label
$hdrSub.Text = 'Bank sync agent'
$hdrSub.Font = $fSmall
$hdrSub.ForeColor = $cMuted
$hdrSub.AutoSize = $true
$hdrSub.Location = New-Object System.Drawing.Point(76, 46)
$form.Controls.Add($hdrSub)

# Status card
$statusCard = New-Card 20 78 336 92 $cCard
$form.Controls.Add($statusCard)

$dot = New-Object System.Windows.Forms.Label
$dot.Text = [char]0x25CF
$dot.Font = New-Object System.Drawing.Font('Segoe UI', 16)
$dot.ForeColor = $cGray
$dot.AutoSize = $true
$dot.Location = New-Object System.Drawing.Point(16, 14)
$statusCard.Controls.Add($dot)

$statusText = New-Object System.Windows.Forms.Label
$statusText.Text = 'Stopped'
$statusText.Font = $fBold
$statusText.ForeColor = $cText
$statusText.AutoSize = $true
$statusText.Location = New-Object System.Drawing.Point(44, 18)
$statusCard.Controls.Add($statusText)

$lastResult = New-Object System.Windows.Forms.Label
$lastResult.Text = 'Not run yet'
$lastResult.Font = $fRegular
$lastResult.ForeColor = $cMuted
$lastResult.AutoSize = $true
$lastResult.Location = New-Object System.Drawing.Point(46, 42)
$statusCard.Controls.Add($lastResult)

$lastRun = New-Object System.Windows.Forms.Label
$lastRun.Text = ''
$lastRun.Font = $fSmall
$lastRun.ForeColor = $cMuted
$lastRun.AutoSize = $true
$lastRun.Location = New-Object System.Drawing.Point(46, 64)
$statusCard.Controls.Add($lastRun)

# Stats row (two mini cards)
$statA = New-Card 20 182 162 64 $cCard
$form.Controls.Add($statA)
$statAnum = New-Object System.Windows.Forms.Label
$statAnum.Text = '0'; $statAnum.Font = $fTitle; $statAnum.ForeColor = $cText
$statAnum.AutoSize = $true; $statAnum.Location = New-Object System.Drawing.Point(14, 10)
$statA.Controls.Add($statAnum)
$statAlbl = New-Object System.Windows.Forms.Label
$statAlbl.Text = 'Runs this session'; $statAlbl.Font = $fSmall; $statAlbl.ForeColor = $cMuted
$statAlbl.AutoSize = $true; $statAlbl.Location = New-Object System.Drawing.Point(14, 40)
$statA.Controls.Add($statAlbl)

$statB = New-Card 194 182 162 64 $cCard
$form.Controls.Add($statB)
$statBnum = New-Object System.Windows.Forms.Label
$statBnum.Text = "$($script:state.totalRuns)"; $statBnum.Font = $fTitle; $statBnum.ForeColor = $cText
$statBnum.AutoSize = $true; $statBnum.Location = New-Object System.Drawing.Point(14, 10)
$statB.Controls.Add($statBnum)
$statBlbl = New-Object System.Windows.Forms.Label
$statBlbl.Text = 'Total runs'; $statBlbl.Font = $fSmall; $statBlbl.ForeColor = $cMuted
$statBlbl.AutoSize = $true; $statBlbl.Location = New-Object System.Drawing.Point(14, 40)
$statB.Controls.Add($statBlbl)

# Start/Stop button
$btnMain = New-Object System.Windows.Forms.Button
$btnMain.Text = 'Start Worker'
$btnMain.Font = $fBold
$btnMain.ForeColor = [System.Drawing.Color]::White
$btnMain.BackColor = $cIndigo
$btnMain.FlatStyle = 'Flat'
$btnMain.FlatAppearance.BorderSize = 0
$btnMain.Size = New-Object System.Drawing.Size(336, 42)
$btnMain.Location = New-Object System.Drawing.Point(20, 258)
$btnMain.Cursor = 'Hand'
$form.Controls.Add($btnMain)

# Run-now (secondary)
$btnRun = New-Object System.Windows.Forms.Button
$btnRun.Text = 'Sync once now'
$btnRun.Font = $fRegular
$btnRun.ForeColor = $cText
$btnRun.BackColor = $cCard2
$btnRun.FlatStyle = 'Flat'
$btnRun.FlatAppearance.BorderSize = 0
$btnRun.Size = New-Object System.Drawing.Size(336, 34)
$btnRun.Location = New-Object System.Drawing.Point(20, 308)
$btnRun.Cursor = 'Hand'
$form.Controls.Add($btnRun)

# Startup toggle
$chkStartup = New-Object System.Windows.Forms.CheckBox
$chkStartup.Text = ' Launch automatically when Windows starts'
$chkStartup.Font = $fRegular
$chkStartup.ForeColor = $cMuted
$chkStartup.AutoSize = $true
$chkStartup.Location = New-Object System.Drawing.Point(22, 356)
$form.Controls.Add($chkStartup)

$intervalLbl = New-Object System.Windows.Forms.Label
$intervalLbl.Text = "Checks for work every $IntervalMinutes minutes. Only contacts your bank when there's a sync to run (max ~2/day)."
$intervalLbl.Font = $fSmall
$intervalLbl.ForeColor = $cMuted
$intervalLbl.Size = New-Object System.Drawing.Size(336, 40)
$intervalLbl.Location = New-Object System.Drawing.Point(22, 384)
$form.Controls.Add($intervalLbl)

# Footer
$footer = New-Object System.Windows.Forms.Label
$footer.Text = 'by Hananel Sabag'
$footer.Font = $fSmall
$footer.ForeColor = $cGray
$footer.AutoSize = $true
$footer.Location = New-Object System.Drawing.Point(22, 430)
$form.Controls.Add($footer)

# ── Tray icon ───────────────────────────────────────────────────────────────
$tray = New-Object System.Windows.Forms.NotifyIcon
$tray.Icon = [System.Drawing.SystemIcons]::Application
$tray.Text = 'SpendWise Worker'
$tray.Visible = $true

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$miOpen = $menu.Items.Add('Open')
$miRun  = $menu.Items.Add('Sync once now')
$menu.Items.Add('-') | Out-Null
$miQuit = $menu.Items.Add('Quit')
$tray.ContextMenuStrip = $menu

# ── Status helpers ──────────────────────────────────────────────────────────
function Set-Status($text, $color) {
  $statusText.Text = $text
  $dot.ForeColor = $color
}

function Parse-LastResult {
  if (-not (Test-Path $LogFile)) { return }
  try {
    $tail = Get-Content $LogFile -Tail 40 -ErrorAction Stop
  } catch { return }
  $done = $tail | Where-Object { $_ -match 'DONE .* (\d+) new, (\d+) skipped' } | Select-Object -Last 1
  $none = $tail | Where-Object { $_ -match 'no pending jobs' } | Select-Object -Last 1
  $fail = $tail | Where-Object { $_ -match 'FAILED|FATAL' } | Select-Object -Last 1
  if ($done) {
    if ($done -match '(\d+) new, (\d+) skipped') {
      $lastResult.Text = "Last sync: $($Matches[1]) new, $($Matches[2]) skipped"
      $lastResult.ForeColor = $cGreen
    }
  } elseif ($fail) {
    $lastResult.Text = 'Last run failed — open the app to check'
    $lastResult.ForeColor = $cRed
  } elseif ($none) {
    $lastResult.Text = 'Up to date — no new work'
    $lastResult.ForeColor = $cMuted
  }
}

# ── Run the agent (fire-and-forget, hidden) ─────────────────────────────────
function Invoke-AgentRun {
  if ($script:busy) { return }
  $script:busy = $true
  Set-Status 'Syncing…' $cBlue
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
    Set-Status 'Error: Node not found' $cRed
    $lastResult.Text = 'Install Node.js, then try again'
    $lastResult.ForeColor = $cRed
    $script:busy = $false
    return
  }

  # Count the run
  $script:sessionRuns++
  $statAnum.Text = "$($script:sessionRuns)"
  $script:state.totalRuns = [int]$script:state.totalRuns + 1
  $statBnum.Text = "$($script:state.totalRuns)"
  $script:state.lastRun = (Get-Date).ToString('s')
  Save-State $script:state

  # Poll for completion without freezing the UI
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

# ── Interval loop ───────────────────────────────────────────────────────────
$loopTimer = New-Object System.Windows.Forms.Timer
$loopTimer.Interval = $IntervalMinutes * 60 * 1000
$loopTimer.Add_Tick({ Invoke-AgentRun })

function Start-Worker {
  $script:running = $true
  $btnMain.Text = 'Stop Worker'
  $btnMain.BackColor = $cCard2
  Set-Status 'Running' $cGreen
  $loopTimer.Start()
  Invoke-AgentRun   # run once immediately
}
function Stop-Worker {
  $script:running = $false
  $loopTimer.Stop()
  $btnMain.Text = 'Start Worker'
  $btnMain.BackColor = $cIndigo
  Set-Status 'Stopped' $cGray
}

# ── Windows startup (Run key → the hidden launcher) ─────────────────────────
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$RunName = 'SpendWiseWorker'
$Launcher = Join-Path $WorkerDir 'SpendWise-Worker.vbs'

function Test-Startup {
  try { return [bool]((Get-ItemProperty -Path $RunKey -Name $RunName -ErrorAction Stop).$RunName) }
  catch { return $false }
}
function Set-Startup($on) {
  if ($on) {
    Set-ItemProperty -Path $RunKey -Name $RunName -Value ('wscript.exe "' + $Launcher + '"')
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

# Close (X) button → hide to tray so the worker keeps running.
# Only a real Quit (tray menu) sets userQuit and lets the form close.
$form.Add_FormClosing({
  param($s, $e)
  if ($script:userQuit -ne $true) {
    $e.Cancel = $true
    $form.Hide()
    $tray.ShowBalloonTip(2000, 'SpendWise Worker', 'Still running in the tray.', 'Info')
  }
})

# Show last known result on open
Parse-LastResult

[System.Windows.Forms.Application]::EnableVisualStyles()
[System.Windows.Forms.Application]::Run($form)
$tray.Visible = $false
