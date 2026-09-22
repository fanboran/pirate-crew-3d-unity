Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Cap {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  public struct RECT { public int L, T, R, B; }
}
'@
$u=(Get-Process Unity | Where-Object { $_.MainWindowTitle -ne '' } | Select-Object -First 1)
if (-not $u) { Write-Output "no unity window"; exit 1 }
[Cap]::ShowWindow($u.MainWindowHandle, 9) | Out-Null    # SW_RESTORE
Start-Sleep -Milliseconds 600
[Cap]::SetForegroundWindow($u.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 900
$rect = New-Object Cap+RECT
[Cap]::GetWindowRect($u.MainWindowHandle, [ref]$rect) | Out-Null
$w = $rect.R - $rect.L; $h = $rect.B - $rect.T
Write-Output "window ${w}x${h}"
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[Cap]::PrintWindow($u.MainWindowHandle, $dc, 2) | Out-Null
$g.ReleaseHdc($dc); $g.Dispose()
$bmp.Save("F:\VSCode\pirate-crew-3d-unity\export\ui-pixel-4a\unity-window.png")
Write-Output "saved"
