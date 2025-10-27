param(
    [string]$FirstName,
    [string]$LastName
)

# Add Win32 focus helpers
$winApi = @'
using System;
using System.Runtime.InteropServices;
public static class WinApi {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
'@
Add-Type -TypeDefinition $winApi -Language CSharp -ErrorAction SilentlyContinue

# Load SendKeys
Add-Type -AssemblyName System.Windows.Forms

# Start Notepad
$proc = Start-Process notepad -PassThru
Start-Sleep -Seconds 1.2  # give it time to start

# Bring it forward
$hWnd = $proc.MainWindowHandle
if ($hWnd -ne 0) {
    [WinApi]::ShowWindow($hWnd, 9)        # restore if minimized
    [WinApi]::SetForegroundWindow($hWnd)  # bring to front
    Start-Sleep -Milliseconds 200
}

# Tap ALT once to confirm focus
# [System.Windows.Forms.SendKeys]::SendWait('%')
Start-Sleep -Milliseconds 100

# Type your text
$text = "Hello $FirstName $LastName! This text came from MDL transfer test."
[System.Windows.Forms.SendKeys]::SendWait($text)
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
