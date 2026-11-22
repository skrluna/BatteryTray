$ErrorActionPreference = 'Stop'
$p = Start-Process -FilePath 'powershell.exe' -Verb RunAs -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'D:\battery\scripts\UpdateSwap2.ps1')
"LAUNCHED pid=" + $p.Id
