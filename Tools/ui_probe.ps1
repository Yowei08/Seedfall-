param(
  [Parameter(Mandatory=$true)][string]$Action,
  [string]$Method,
  [string]$Value,
  [string]$Name,
  [int]$Wait = 0
)
$ErrorActionPreference = 'Stop'
$b = 'http://127.0.0.1:17890'

function PostJson($path, $json) {
  try { return (Invoke-WebRequest -Uri "$b$path" -Method POST -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec 60).Content }
  catch { return "ERR: $($_.Exception.Message)" }
}
function GetJson($path) {
  try { return (Invoke-WebRequest -Uri "$b$path" -TimeoutSec 60).Content }
  catch { return "ERR: $($_.Exception.Message)" }
}
function SendMethod($m, $v) {
  $o = [ordered]@{ action='send'; path='DebugApi'; type='DebugApi'; method=$m }
  if ($null -ne $v -and $v -ne '') { $o.value = $v }
  $json = $o | ConvertTo-Json -Compress
  return PostJson '/input' $json
}

switch ($Action) {
  'status'  { return (GetJson '/status') }
  'send'    { $r = SendMethod $Method $Value; if ($Wait -gt 0) { Start-Sleep -Milliseconds $Wait }; return $r }
  'capture' {
    $fg = @'
using System;
using System.Runtime.InteropServices;
public class __FG {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
'@
    if (-not ('__FG' -as [type])) { Add-Type -TypeDefinition $fg }
    foreach ($p in (Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 })) {
      if ([__FG]::IsIconic($p.MainWindowHandle)) { [void][__FG]::ShowWindow($p.MainWindowHandle, 9) }
      [void][__FG]::SetForegroundWindow($p.MainWindowHandle)
    }
    Start-Sleep -Milliseconds 350
    $r = SendMethod 'Capture' $Name
    Start-Sleep -Milliseconds 1200
    return $r
  }
  'log' { return (GetJson '/log') }
  default { throw "unknown action $Action" }
}
