[Console]::OutputEncoding=[Text.Encoding]::UTF8
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public class Tok2 {
 [DllImport("advapi32.dll",SetLastError=true)] public static extern bool OpenProcessToken(IntPtr h,uint acc,out IntPtr tok);
 [DllImport("advapi32.dll",SetLastError=true)] public static extern bool GetTokenInformation(IntPtr tok,int cls,IntPtr info,uint len,out uint ret);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern bool ConvertSidToStringSid(IntPtr sid,out IntPtr str);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern IntPtr OpenProcess(uint acc,bool inh,int pid);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
 [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr h);
 public static string IL(int pid){
  IntPtr hp=OpenProcess(0x1000,false,pid); if(hp==IntPtr.Zero) return "openproc_err"+Marshal.GetLastWin32Error();
  IntPtr tk; if(!OpenProcessToken(hp,0x0008,out tk)){ CloseHandle(hp); return "opentoken_err"+Marshal.GetLastWin32Error(); }
  uint ret; GetTokenInformation(tk,25,IntPtr.Zero,0,out ret);
  IntPtr buf=Marshal.AllocHGlobal((int)ret); string s="query_fail";
  if(GetTokenInformation(tk,25,buf,ret,out ret)){ IntPtr sid=Marshal.ReadIntPtr(buf); IntPtr str; if(ConvertSidToStringSid(sid,out str)){ s=Marshal.PtrToStringUni(str); LocalFree(str); } }
  Marshal.FreeHGlobal(buf); CloseHandle(tk); CloseHandle(hp); return s;
 }
}
'@
Get-Process BatteryTray -ErrorAction SilentlyContinue | ForEach-Object { "PROC pid={0} il={1}" -f $_.Id,[Tok2]::IL($_.Id) }
$base='HKCU:\Control Panel\NotifyIconSettings'
Get-ChildItem $base -ErrorAction SilentlyContinue | ForEach-Object {
  $p=Get-ItemProperty $_.PSPath
  if($p.ExecutablePath -like '*BatteryTray*'){
    "REGKEY={0} promoted={1} tooltip={2}" -f $_.PSChildName,$p.IsPromoted,$p.InitialTooltip
    if($p.IsPromoted -ne 1){ Set-ItemProperty $_.PSPath -Name IsPromoted -Value 1 -Type DWord; "PROMOTED_SET" }
  }
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root=[System.Windows.Automation.AutomationElement]::RootElement
$tbCond=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty,"Shell_TrayWnd")
$tb=$root.FindFirst([System.Windows.Automation.TreeScope]::Children,$tbCond)
$names=@()
foreach($e in $tb.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)){ if($e.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $e.Current.Name){ $names+=$e.Current.Name } }
"TASKBAR_BUTTONS: " + ($names -join ' ;; ')
$hidden=-join ([char]0x9690,[char]0x85CF)
$chev=$null
foreach($e in $tb.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)){ if($e.Current.Name -and $e.Current.Name.Contains($hidden) -and $e.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button){ $chev=$e; break } }
if($chev){ try{ $chev.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1500 }catch{} }
$ovCond=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty,"TopLevelWindowForOverflowXamlIsland")
$ov=$root.FindFirst([System.Windows.Automation.TreeScope]::Children,$ovCond)
if($ov){ $on=@(); foreach($e in $ov.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)){ if($e.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $e.Current.Name){ $on+=$e.Current.Name } }; "OVERFLOW_BUTTONS: " + ($on -join ' ;; ') } else { "OVERFLOW_NOT_OPEN" }