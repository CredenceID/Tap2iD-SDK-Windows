param(
    [Parameter(Mandatory = $true)]
    [string]$Json
)

# Deserialize JSON into an object
try {
    $mdl = $Json | ConvertFrom-Json
    Write-Host "✅ Parsed mDL JSON successfully"
} catch {
    Write-Host "❌ Failed to parse JSON input: $_"
    exit 1
}

Write-Host "Received JSON: $($mdl | ConvertTo-Json -Compress)"

<# 
  PS1.ps1 — Automate form filling in "Citation Manager"
  -----------------------------------------------------
  Run from CMD:
    powershell -NoProfile -STA -ExecutionPolicy Bypass -File "C:\Users\DMV\Downloads\PS1.ps1"
#>



# ===========================
# CONFIG
# ===========================
# Optional: auto-launch if not running (uncomment and set path)
# $exePath = 'C:\Program Files\CitationManager\CitationManager.exe'
$procName = 'CitationManager'     # Process name as shown by Get-Process




# ===========================
# Load dependencies
# ===========================
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms | Out-Null

# Minimal user32 wrapper (restore + foreground)
$src = @'
using System;
using System.Runtime.InteropServices;
public static class WinApi2 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
'@
try { Add-Type -TypeDefinition $src -Language CSharp -ErrorAction Stop } catch { }

# ===========================
# Ensure app is running
# ===========================
$p = Get-Process -Name $procName -ErrorAction SilentlyContinue
if (-not $p) {
  if (-not $exePath) { throw "App not running and $exePath not set. Set $exePath or open the app first." }
  $p = Start-Process -FilePath $exePath -PassThru
}

# ===========================
# Wait for main window
# ===========================
$timeoutSec = 30
$sw = [System.Diagnostics.Stopwatch]::StartNew()
do {
  Start-Sleep -Milliseconds 250
  $p = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
  if (-not $p) { throw "Process exited unexpectedly." }
  $hWnd = [IntPtr]$p.MainWindowHandle
} while (($hWnd -eq [IntPtr]::Zero) -and ($sw.Elapsed.TotalSeconds -lt $timeoutSec))
if ($hWnd -eq [IntPtr]::Zero) { throw "Timed out waiting for main window." }

# ===========================
# Foreground + focus window
# ===========================
if ([WinApi2]::IsIconic($hWnd)) { [void][WinApi2]::ShowWindow($hWnd,9) }  # SW_RESTORE
[void][WinApi2]::SetForegroundWindow($hWnd)
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('%')  # tiny ALT tap
Start-Sleep -Milliseconds 150

# ===========================
# UIA root and helpers
# ===========================
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hWnd)
if (-not $root) { throw "Failed to get AutomationElement for main window." }

function Find-ById([System.Windows.Automation.AutomationElement]$scopeEl, [string]$autoId) {
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $autoId
  )
  $scopeEl.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Get-Scrollable([System.Windows.Automation.AutomationElement]$scopeEl) {
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $q = New-Object System.Collections.Generic.Queue[System.Windows.Automation.AutomationElement]
  $q.Enqueue($scopeEl)
  while ($q.Count) {
    $n = $q.Dequeue()
    try {
      $obj = $null
      if ($n.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern,[ref]$obj)) { return $n }
    } catch {}
    $c = $walker.GetFirstChild($n)
    while ($c) { $q.Enqueue($c); $c = $walker.GetNextSibling($c) }
  }
  return $null
}

# ===========================
# Locate 'First Name' (txtFirstName)
# ===========================
$firstName = Find-ById $root 'txtFirstName'
if (-not $firstName) {
  $scrollHost = Get-Scrollable $root
  if ($scrollHost) {
    $sp = [System.Windows.Automation.ScrollPattern]$scrollHost.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    for ($i=0; $i -lt 60 -and -not $firstName; $i++) {
      try { $sp.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount,[System.Windows.Automation.ScrollAmount]::SmallIncrement) } catch {}
      Start-Sleep -Milliseconds 120
      $firstName = Find-ById $root 'txtFirstName'
    }
  }
}
if (-not $firstName) { throw "Could not find AutomationId 'txtFirstName'. Make sure the correct section/tab is visible." }

# ===========================
# Typing helpers
# ===========================
function Type-Here([string]$text) {
  [System.Windows.Forms.SendKeys]::SendWait("^{a}{DEL}")
  Start-Sleep -Milliseconds 120
  [System.Windows.Forms.SendKeys]::SendWait($text)
}

$d = 900   # pacing delay
function T([string]$text) {
  [System.Windows.Forms.SendKeys]::SendWait("{TAB}")
  Start-Sleep -Milliseconds 150
  [System.Windows.Forms.SendKeys]::SendWait("^{a}{DEL}")
  Start-Sleep -Milliseconds 150
  [System.Windows.Forms.SendKeys]::SendWait($text)
  Start-Sleep -Milliseconds $d
}

function Set-FocusedValue([string]$text) {
  try {
    $el = [System.Windows.Automation.AutomationElement]::FocusedElement
    if ($null -ne $el) {
      $obj = $null
      if ($el.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern,[ref]$obj)) {
        ([System.Windows.Automation.ValuePattern]$obj).SetValue($text)
        return
      }
    }
  } catch {}
  [System.Windows.Forms.SendKeys]::SendWait("^{a}{DEL}")
  Start-Sleep -Milliseconds 120
  [System.Windows.Forms.SendKeys]::SendWait($text)
}

# ===========================
# Fill fields (your order)
# ===========================
$firstName.SetFocus() | Out-Null
$vpObj = $null
if ($firstName.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern,[ref]$vpObj)) {
  ([System.Windows.Automation.ValuePattern]$vpObj).SetValue($mdl.FirstName)
} else {
  Type-Here $mdl.FirstName
}
Start-Sleep -Milliseconds 150

T $mdl.MiddleName                    # Middle Name (if exists)
T $mdl.LastName                      # Last Name
T $mdl.Suffix                        # Suffix
T $mdl.ResidentAddress               # Address Line 1
T $mdl.AddressLine2                  # Address Line 2
T $mdl.Zip                           # ZIP
Start-Sleep -Milliseconds 1200
T $mdl.City                          # City
T $mdl.State                         # State
T $mdl.Status                        # Status
T $mdl.DocumentNumber                # DL#
T $mdl.IssuingAuthority              # Issuing State
T $mdl.Commercial                    # Commercial Yes/No

# --- Fix for extra Yes/No and DOB/AGE ---
[System.Windows.Forms.SendKeys]::SendWait("{TAB}")
Start-Sleep -Milliseconds 200
[System.Windows.Forms.SendKeys]::SendWait("{TAB}")
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait("{RIGHT}")
Start-Sleep -Milliseconds 200
[System.Windows.Forms.SendKeys]::SendWait("{TAB}")
Start-Sleep -Milliseconds 300

# DOB (MMDDYYYY)
if ($mdl.BirthDate) {
  [System.Windows.Forms.SendKeys]::SendWait("^{a}{DEL}")
  Start-Sleep -Milliseconds 200
  [System.Windows.Forms.SendKeys]::SendWait($mdl.BirthDate)
  Start-Sleep -Milliseconds $d
}

# AGE
if ($mdl.Age) {
  [System.Windows.Forms.SendKeys]::SendWait("{TAB}")
  Start-Sleep -Milliseconds 200
  [System.Windows.Forms.SendKeys]::SendWait("^{a}{DEL}")
  [System.Windows.Forms.SendKeys]::SendWait($mdl.Age)
  Start-Sleep -Milliseconds $d
}

# ===========================
# Remaining: Sex, Hair, Eyes, Height, Weight
# ===========================
Start-Sleep -Milliseconds 1500

# Sex = M-Male
[System.Windows.Forms.SendKeys]::SendWait("{TAB}")
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait("^{a}{DEL}")
[System.Windows.Forms.SendKeys]::SendWait($mdl.Sex)
Start-Sleep -Milliseconds $d

# Hair = Black
[System.Windows.Forms.SendKeys]::SendWait("{TAB}")
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait("^{a}{DEL}")
[System.Windows.Forms.SendKeys]::SendWait($mdl.HairColor)
Start-Sleep -Milliseconds $d

# Eyes = BLK-BLACK
[System.Windows.Forms.SendKeys]::SendWait("{TAB}")
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait("^{a}{DEL}")
[System.Windows.Forms.SendKeys]::SendWait($mdl.EyeColor)
Start-Sleep -Milliseconds $d

# Height = 5ft10in
[System.Windows.Forms.SendKeys]::SendWait("{TAB}")
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait("^{a}{DEL}")
[System.Windows.Forms.SendKeys]::SendWait($mdl.Height)
Start-Sleep -Milliseconds $d

# Weight = 170
[System.Windows.Forms.SendKeys]::SendWait("{TAB}")
Start-Sleep -Milliseconds 500
Set-FocusedValue $mdl.Weight
Start-Sleep -Milliseconds $d

Write-Host "✅ Done. Closing PowerShell..."
Start-Sleep -Seconds 2
exit