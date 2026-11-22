$ErrorActionPreference = 'Continue'
$src = 'D:\battery\BatteryTray_new.exe'
$dst = 'D:\BatteryTray\BatteryTray.exe'
$st  = 'D:\battery\swap2_status.txt'
function W($m) { Add-Content -Path $st -Value $m -Encoding UTF8 }
Set-Content -Path $st -Value 'ELEVATED_START' -Encoding UTF8
if (-not (Test-Path $src)) { W 'SRC_MISSING'; exit 1 }
Get-Process BatteryTray -ErrorAction SilentlyContinue | Stop-Process -Force
for ($i = 0; $i -lt 80; $i++) {
  if (-not (Get-Process BatteryTray -ErrorAction SilentlyContinue)) { break }
  Start-Sleep -Milliseconds 250
}
if (Get-Process BatteryTray -ErrorAction SilentlyContinue) { W 'KILL_FAIL'; exit 2 }
W 'KILLED'
$ok = $false
for ($i = 0; $i -lt 20; $i++) {
  try { Copy-Item -LiteralPath $src -Destination $dst -Force -ErrorAction Stop; $ok = $true; break }
  catch { Start-Sleep -Milliseconds 500 }
}
if (-not $ok) { W 'MOVE_FAIL'; exit 3 }
W 'MOVE_OK'
Start-Process -FilePath $dst -WorkingDirectory 'D:\BatteryTray'
W 'STARTED'
