@echo off
rem BatteryTray 一键编译 — csc 来自系统自带 .NET Framework 4.x, 无需安装任何 SDK
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [错误] 未找到 csc.exe, 请确认已安装 .NET Framework 4.x
    exit /b 1
)
"%CSC%" /nologo /optimize+ /platform:x64 /target:winexe /win32manifest:src\batterytray.manifest /out:BatteryTray.exe src\BatteryTray.cs /r:System.Windows.Forms.dll /r:System.Drawing.dll
if errorlevel 1 exit /b 1
"%CSC%" /nologo /optimize+ /platform:x64 /out:HidScan.exe src\HidScan.cs
echo 完成: BatteryTray.exe / HidScan.exe
