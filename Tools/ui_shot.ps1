param([string]$ShotName = 'ui-after-combat-full.png', [string]$ShotNameAlt = '')
$ErrorActionPreference = 'Stop'
$b = 'http://127.0.0.1:17890'

function PostJson($path, $json) {
  try { return (Invoke-WebRequest -Uri "$b$path" -Method POST -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec 60).Content }
  catch { return "ERR: $($_.Exception.Message)" }
}
function Send($m, $v) {
  $o = [ordered]@{ action='send'; path='DebugApi'; type='DebugApi'; method=$m }
  if ($null -ne $v -and $v -ne '') { $o.value = $v }
  return PostJson '/input' ($o | ConvertTo-Json -Compress)
}
function Bring-Front {
  $fg = @'
using System;
using System.Runtime.InteropServices;
public class __FG2 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
'@
  if (-not ('__FG2' -as [type])) { Add-Type -TypeDefinition $fg }
  foreach ($p in (Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 })) {
    if ([__FG2]::IsIconic($p.MainWindowHandle)) { [void][__FG2]::ShowWindow($p.MainWindowHandle, 9) }
    [void][__FG2]::SetForegroundWindow($p.MainWindowHandle)
  }
}

$seeds = @('starvine','toxic_mushroom','life_spring','moontree')
$done = $false
for ($try = 1; $try -le 8; $try++) {
  $st = Send 'Status'
  if ($st -match 'phase=Prepare') {
    Send 'DebugChooseSeed' '0' | Out-Null
    Start-Sleep -Milliseconds 1400
    $st = Send 'Status'
  }
  if ($st -match 'phase=Combat') {
    foreach ($s in $seeds) { Send 'GrantSeed' $s | Out-Null }
    Send 'HurtPlayer' '4' | Out-Null
    Start-Sleep -Milliseconds 400
    $st2 = Send 'Status'
    if ($st2 -match 'phase=Combat') {
      Bring-Front
      Start-Sleep -Milliseconds 400
      Send 'Capture' $ShotName | Out-Null
      Start-Sleep -Milliseconds 1500
      Send 'HudAudit' | Out-Null
      Start-Sleep -Milliseconds 700
      $audit = Send 'HudAuditReport'
      $final = Send 'Status'
      "STATUS: $st2"
      "AUDIT : $audit"
      "AFTER : $final"
      $done = $true
      break
    }
  }
  Start-Sleep -Seconds 2
}
if (-not $done) { "FAILED after 8 tries; last status: $st" }
