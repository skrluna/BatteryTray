// Installer.cs - 外设电量托盘 单文件安装器 (嵌入 BatteryTray.exe + Uninstall.exe)
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

class InstallerApp : Form
{
    TextBox dirBox, log; CheckBox deskChk, runChk; Button btn;
    static string DefDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BatteryTray"); } }

    InstallerApp()
    {
        Text = "外设电量托盘 v1.3.1 安装程序";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(480, 300);
        var l1 = new Label(); l1.Text = "安装到："; l1.Location = new Point(14, 16); l1.AutoSize = true; Controls.Add(l1);
        dirBox = new TextBox(); dirBox.Text = DefDir; dirBox.Location = new Point(70, 12); dirBox.Width = 330; Controls.Add(dirBox);
        var br = new Button(); br.Text = "浏览..."; br.Location = new Point(406, 10); br.Width = 60; br.Click += OnBrowse; Controls.Add(br);
        deskChk = new CheckBox(); deskChk.Text = "创建桌面快捷方式"; deskChk.Location = new Point(70, 44); deskChk.AutoSize = true; deskChk.Checked = true; Controls.Add(deskChk);
        runChk = new CheckBox(); runChk.Text = "安装完成后立即启动"; runChk.Location = new Point(240, 44); runChk.AutoSize = true; runChk.Checked = true; Controls.Add(runChk);
        btn = new Button(); btn.Text = "安装"; btn.Location = new Point(70, 74); btn.Width = 120; btn.Click += OnInstall; Controls.Add(btn);
        log = new TextBox(); log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical;
        log.Font = new Font("Consolas", 9F); log.Location = new Point(14, 110); log.Size = new Size(451, 175); Controls.Add(log);
        Log("独立应用 · 系统自带 .NET 4.8 即可运行 · 数据在 %LOCALAPPDATA%\\BatteryTray");
    }
    void Log(string s) { log.AppendText("· " + s + "\r\n"); }
    void Run(string file, string args)
    {
        var pi = new ProcessStartInfo(file, args);
        pi.CreateNoWindow = true; pi.UseShellExecute = false;
        pi.RedirectStandardOutput = true; pi.RedirectStandardError = true;
        var p = Process.Start(pi);
        if (p == null) throw new Exception(file + " 启动失败");
        string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(10000);
        if (p.ExitCode != 0) throw new Exception(file + " 失败 rc=" + p.ExitCode + " " + o.Trim());
        Log(file + " 完成");
    }
    void Extract(string res, string dest)
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
        {
            if (s == null) throw new Exception("资源缺失: " + res);
            using (var f = File.Create(dest)) s.CopyTo(f);
        }
    }
    void Shortcut(string lnkPath, string target, string workDir)
    {
        object sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        object lnk = sh.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
        lnk.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
        lnk.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir });
        lnk.GetType().InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
        lnk.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }
    void OnBrowse(object sender, EventArgs e)
    {
        using (var d = new FolderBrowserDialog())
        {
            d.Description = "选择安装位置（将自动创建 BatteryTray 子文件夹）";
            d.ShowNewFolderButton = true;
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                string p = d.SelectedPath;
                if (!p.EndsWith("BatteryTray", StringComparison.OrdinalIgnoreCase)) p = Path.Combine(p, "BatteryTray");
                dirBox.Text = p;
            }
        }
    }
    void OnInstall(object sender, EventArgs e)
    {
        string dir = dirBox.Text.Trim();
        if (dir.Length < 4 || dir.IndexOf(':') != 1 || !Path.IsPathRooted(dir))
        {
            MessageBox.Show("请输入有效的安装路径，例如 D:\\Apps\\BatteryTray", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex0) { MessageBox.Show("无法创建目录: " + ex0.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        btn.Enabled = false;
        try
        {
            Log("停止正在运行的 BatteryTray...");
            foreach (var p in Process.GetProcessesByName("BatteryTray")) { try { p.Kill(); p.WaitForExit(3000); } catch (Exception) {} }
            Directory.CreateDirectory(dir);
            Log("释放文件...");
            string exe = Path.Combine(dir, "BatteryTray.exe"), un = Path.Combine(dir, "Uninstall.exe");
            Extract("BatteryTray.exe", exe); Extract("Uninstall.exe", un);
            try
            {
                string srcDev = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "devices.txt");
                string dstDev = Path.Combine(dir, "devices.txt");
                if (File.Exists(srcDev) && !File.Exists(dstDev)) { File.Copy(srcDev, dstDev); Log("已迁移设备配置 devices.txt"); }
            }
            catch (Exception) {}
            Log("注册卸载信息(设置-应用)...");
            using (var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BatteryTray"))
            {
                k.SetValue("DisplayName", "外设电量托盘 BatteryTray");
                k.SetValue("DisplayVersion", "1.3.1");
                k.SetValue("Publisher", "neko");
                k.SetValue("DisplayIcon", exe);
                k.SetValue("UninstallString", un);
                k.SetValue("NoModify", 1); k.SetValue("NoRepair", 1);
                k.SetValue("EstimatedSize", 90, Microsoft.Win32.RegistryValueKind.DWord);
            }
            Log("创建开始菜单快捷方式...");
            string sm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "BatteryTray.lnk");
            Shortcut(sm, exe, dir);
            if (deskChk.Checked) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BatteryTray.lnk"), exe, dir);
            Log("注册登录自启计划任务...");
            Run("schtasks.exe", "/Create /TN BatteryTray /TR \\\"" + exe + "\\\"" + " /SC ONLOGON /RL HIGHEST /IT /F");
            if (runChk.Checked) { Log("启动程序..."); Process.Start(exe); }
            Log("√ 安装完成: " + dir);
            MessageBox.Show("安装完成！程序位于:\r\n" + dir + "\r\n\r\n登录自启已注册(计划任务 BatteryTray)，\r\n可在「设置-应用」中卸载。", "外设电量托盘",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            Log("× 失败: " + ex.Message);
            MessageBox.Show("安装失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            btn.Enabled = true;
        }
    }
    [STAThread]
    static void Main() { Application.EnableVisualStyles(); Application.Run(new InstallerApp()); }
}
