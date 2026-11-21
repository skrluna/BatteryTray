$ErrorActionPreference='Continue'
Set-Content -Path 'D:\battery\swap_status.txt' -Value 'ELEVATED_START'
Stop-Process -Name BatteryTray -Force -ErrorAction SilentlyContinue
$deadline=(Get-Date).AddSeconds(30)
while((Get-Date) -lt $deadline){ if(-not (Get-Process BatteryTray -ErrorAction SilentlyContinue)){ break }; Start-Sleep -Milliseconds 500 }
if(Get-Process BatteryTray -ErrorAction SilentlyContinue){ Set-Content -Path 'D:\battery\swap_status.txt' -Value 'KILL_FAIL'; exit 1 }
$ok=$false
for($i=0;$i -lt 20 -and -not $ok;$i++){ try{ Move-Item -Path 'D:\battery\BatteryTray_new.exe' -Destination 'D:\battery\BatteryTray.exe' -Force; $ok=$true }catch{ Start-Sleep -Milliseconds 500 } }
if(-not $ok){ Set-Content -Path 'D:\battery\swap_status.txt' -Value 'MOVE_FAIL'; exit 2 }
Set-Content -Path 'D:\battery\swap_status.txt' -Value 'MOVE_OK'
Start-Process 'D:\battery\BatteryTray.exe'
Set-Content -Path 'D:\battery\swap_status.txt' -Value 'STARTED'