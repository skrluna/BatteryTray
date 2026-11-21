// Uninstall.cs - 外设电量托盘卸载器 (随安装包释放到安装目录)
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

class Uninst
{
    [STAThread]
    static int Main()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        var r = MessageBox.Show("卸载 外设电量托盘 BatteryTray？\r\n\r\n将删除程序文件、登录自启计划任务和快捷方式。",
            "卸载 外设电量托盘", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (r != DialogResult.OK) return 1;
        foreach (var p in Process.GetProcessesByName("BatteryTray")) { try { p.Kill(); } catch (Exception) {} }
        try { Run("schtasks.exe", "/Delete /TN BatteryTray /F"); } catch (Exception) {}
        try { Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BatteryTray", false); } catch (Exception) {}
        try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "BatteryTray.lnk")); } catch (Exception) {}
        try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BatteryTray.lnk")); } catch (Exception) {}
        try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryTray"), true); } catch (Exception) {}
        // 延迟自删目录(等本进程退出)
        try
        {
            var pi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rd /s /q \"" + dir.TrimEnd('\\') + "\"");
            pi.CreateNoWindow = true; pi.UseShellExecute = false;
            Process.Start(pi);
        }
        catch (Exception) {}
        return 0;
    }
    static void Run(string f, string a)
    {
        var p = Process.Start(new ProcessStartInfo(f, a) { CreateNoWindow = true, UseShellExecute = false });
        if (p != null) p.WaitForExit(8000);
    }
}
