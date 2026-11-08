# DeskMeter の自動 UI テスト補助: 画面の一部をキャプチャ / マウスを動かす (SendInput 版)
#   uitest.ps1 capture "p=out.png"
#   uitest.ps1 drag "p=x1,y1,x2,y2[,hold]"
#   uitest.ps1 release
#   uitest.ps1 rclick "p=x,y"
#   uitest.ps1 rect
param([string]$cmd, [string]$argstr)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class UI {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
  public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
  public static uint Mouse(uint flags) { var inp = new INPUT[1]; inp[0].type = 0; inp[0].mi.dwFlags = flags; return SendInput(1, inp, Marshal.SizeOf(typeof(INPUT))); }
  public static void MoveTo(int x, int y) { SetCursorPos(x, y); Mouse(0x0001); }   // MOUSEEVENTF_MOVE (相対 0) で WM_MOUSEMOVE を発生させる
}
"@
[void][UI]::SetProcessDPIAware()
if ($argstr -and $argstr.StartsWith("p=")) { $argstr = $argstr.Substring(2) }
$p = @($argstr -split ',')
function Get-WidgetRect { $h = [UI]::FindWindow([NullString]::Value, "DeskMeter"); if ($h -eq [IntPtr]::Zero) { throw "DeskMeter window not found" }; $r = New-Object UI+RECT; [void][UI]::GetWindowRect($h, [ref]$r); return $r }
switch ($cmd) {
  'rect' { $r = Get-WidgetRect; "$($r.L),$($r.T),$($r.R),$($r.B)" }
  'capture' {
    $r = Get-WidgetRect; $w = $r.R - $r.L; $h = $r.B - $r.T
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen((New-Object System.Drawing.Point($r.L, $r.T)), (New-Object System.Drawing.Point(0, 0)), (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose(); $bmp.Save($p[0], [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); "saved $($p[0]) ($w x $h)"
  }
  'drag' {
    $x1 = [int]$p[0]; $y1 = [int]$p[1]; $x2 = [int]$p[2]; $y2 = [int]$p[3]; $hold = ($p.Count -ge 5 -and $p[4] -eq 'hold'); $steps = 25
    [UI]::MoveTo($x1, $y1); Start-Sleep -Milliseconds 150
    [void][UI]::Mouse(0x0002); Start-Sleep -Milliseconds 150   # left down
    for ($i = 1; $i -le $steps; $i++) { $x = $x1 + ($x2 - $x1) * $i / $steps; $y = $y1 + ($y2 - $y1) * $i / $steps; [UI]::MoveTo([int]$x, [int]$y); Start-Sleep -Milliseconds 25 }
    Start-Sleep -Milliseconds 200
    if ($hold) { "holding" } else { [void][UI]::Mouse(0x0004); "dragged" }   # left up
  }
  'release' { [void][UI]::Mouse(0x0004); "released" }
  'rclick' { [UI]::MoveTo([int]$p[0], [int]$p[1]); Start-Sleep -Milliseconds 100; [void][UI]::Mouse(0x0008); Start-Sleep -Milliseconds 60; [void][UI]::Mouse(0x0010); "right-clicked" }   # 右クリック (メニューを開く)
  default { "unknown command" }
}
