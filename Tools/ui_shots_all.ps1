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
function SendOn($pathName, $type, $method, $v) {
  $o = [ordered]@{ action='send'; path=$pathName; type=$type; method=$method }
  if ($null -ne $v -and $v -ne '') { $o.value = $v }
  return PostJson '/input' ($o | ConvertTo-Json -Compress)
}
function Bring-Front {
  $fg = @'
using System;
using System.Runtime.InteropServices;
public class __FG3 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
'@
  if (-not ('__FG3' -as [type])) { Add-Type -TypeDefinition $fg }
  foreach ($p in (Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 })) {
    if ([__FG3]::IsIconic($p.MainWindowHandle)) { [void][__FG3]::ShowWindow($p.MainWindowHandle, 9) }
    [void][__FG3]::SetForegroundWindow($p.MainWindowHandle)
  }
}
function Shot($name) {
  Bring-Front
  Start-Sleep -Milliseconds 400
  Send 'Capture' $name | Out-Null
  Start-Sleep -Milliseconds 1600
}

# ── 0) 确保在战斗中，并把手牌/血量做成能看清"部分填充"的状态 ──
$seeds = @('starvine','toxic_mushroom','life_spring','moontree')
$ok = $false
for ($try = 1; $try -le 10; $try++) {
  $st = Send 'Status'
  if ($st -match 'phase=Prepare') { Send 'DebugChooseSeed' '0' | Out-Null; Start-Sleep -Milliseconds 1400; $st = Send 'Status' }
  if ($st -match 'phase=Combat') {
    foreach ($x in $seeds) { Send 'GrantSeed' $x | Out-Null }
    Send 'HurtPlayer' '4' | Out-Null
    Start-Sleep -Milliseconds 500
    $ok = $true
    break
  }
  Start-Sleep -Seconds 2
}
if (-not $ok) { "NO COMBAT: $st"; exit 1 }

# ── 1) 正午（默认 t=0）──
SendOn 'NightLighting' 'NightLighting' 'SetTimeOverride' '0'
Start-Sleep -Milliseconds 300
Shot 'ui-after-combat-noon.png'

# ── 2) 夜晚 t=150（昼相 0.5 = 最深夜）──
SendOn 'NightLighting' 'NightLighting' 'SetTimeOverride' '150'
Start-Sleep -Milliseconds 600
Shot 'ui-after-combat-night.png'

# ── 3) 黄昏 t=75（昼相 0.25 = h01 0.5，最半亮半暗）──
SendOn 'NightLighting' 'NightLighting' 'SetTimeOverride' '75'
Start-Sleep -Milliseconds 600
Shot 'ui-after-combat-dusk.png'

# ── 4) 麦田/木栅底色（把机位拉到农田上方）──
SendOn 'NightLighting' 'NightLighting' 'SetTimeOverride' '0'
Send 'FocusFarm' | Out-Null
Start-Sleep -Milliseconds 900
Shot 'ui-after-combat-farm.png'

# ── 5) 恢复自然时间，收尾 ──
SendOn 'NightLighting' 'NightLighting' 'ClearTimeOverride' ''
Start-Sleep -Milliseconds 300
Send 'ArtShotOff' | Out-Null
Send 'HudAudit' | Out-Null
Start-Sleep -Milliseconds 700
Send 'HudAuditReport'
Send 'Status'
