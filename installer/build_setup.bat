@echo off
cd /d D:\battery\installer
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize+ /platform:x64 /target:winexe /out:Uninstall.exe Uninstall.cs /r:System.Windows.Forms.dll
if errorlevel 1 exit /b 1
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize+ /platform:x64 /target:winexe /win32manifest:setup.manifest /out:..\BatteryTraySetup.exe Installer.cs /res:..\BatteryTray.exe,BatteryTray.exe /res:Uninstall.exe,Uninstall.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll
if errorlevel 1 exit /b 1
echo BUILD_OK
