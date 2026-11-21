# BatteryTray — 外设电量托盘

在 Windows 任务栏托盘**实时显示无线外设（鼠标 / 键盘）电量**的轻量小工具。

单文件 C#，编译后 **约 45 KB**，零第三方依赖（只用系统自带 .NET Framework 4.x 与 Win32 API），运行内存约 **30 MB** —— 用最小的代价，把「电量」这件事做专做透。

平台：Windows 10 / 11 (x64) ｜ 体积：~45 KB ｜ 依赖：无 ｜ 协议：MIT

⬇️ **下载安装包**：[Releases](https://github.com/skrluna/BatteryTray/releases/latest) — `BatteryTraySetup.exe`（61 KB，双击安装）

![图标与电量状态预览](assets/icon_preview.png)

## 特性

- **托盘即电量**：电池造型图标，颜色即电量（绿 >50%% / 黄 20–50%% / 红 ≤20%% / 灰 = 休眠无数据），充电时画黄色闪电 ⚡
- **悬停看明细**：tooltip 列出每台设备；**左键**弹出详情面板（名称 / 电量条 / 连接模式 / 状态 / HID 真实型号 / 最后更新时间）
- **低电量提醒**：≤20%% 弹气泡，充电或回升后自动复位
- **休眠感知**：设备休眠时保留上次电量并标注「休眠」，唤醒后 60 秒内自动刷新；真正拔掉 30 分钟后才从列表移除
- **零配置识别**：内置型号表覆盖迈从 / ATK / VXE / VGN / Dareu 等 Compx / Nordic 芯片方案外设，插上即认
- **添加未知设备**：「＋添加」向导全协议扫描，把内置表没有的设备存入 devices.txt；每行「✎」重命名、「✕」删除
- **蓝牙免添加**：自动读取系统缓存的蓝牙电池属性（DEVPKEY_Bluetooth_Battery）
- **登录自启**：右键开关，内部用登录计划任务静默提权，不弹 UAC
- **可维护**：%LOCALAPPDATA%\BatteryTray\tray.log 记录启动 / 发现 / 唤醒 / 休眠 / 电量变化 / 低电提醒 / 异常，256KB 自动轮转

## 快速开始

### 方式一：安装包（推荐）

下载 Release 里的 BatteryTraySetup.exe（约 61 KB 单文件），双击运行：

- 自动安装到自选目录（支持任意盘符，可浏览选择）
- 创建开始菜单 / 桌面快捷方式
- 注册登录自启（计划任务）与「设置 → 应用」卸载入口

### 方式二：绿色运行

直接双击 BatteryTray.exe —— **需要「以管理员身份运行」**（见下方 FAQ），右键菜单里可勾选开机自启。

### 命令行

```bat
BatteryTray.exe --console   :: 探测一次并打印，便于排查
```

## 已验证设备

| 设备 | VID:PID | 协议 | 模式 |
|---|---|---|---|
| 迈从 K7 Ultra | `3837:1001` | RealTek A7（feature 0x11） | 2.4G |
| 迈从 2.4G 接收器 | `41E4:2004` | Compx G87（FF02 usage 2） | 2.4G |
| ATK 8K NANO 接收器 | `373B:11FE` | Nordic52（FF02） | 2.4G |
| 迈从 G98 V2 | `41E4:2203` | 迈从 hub（feature 0x09/0x87） | 有线 |

内置型号表（src/BatteryTray.cs 的 InitModels）另覆盖：ATK F1 / A9 / Zero、VXE R1、VGN F1 / F2、迈从 A5 / AX5 / A7 / L7 / G87 / G3 / V7 等常见 VID:PID。其它采用 Compx / Nordic 芯片方案的外设（ATK / VXE / VGN / Dareu 部分型号）大概率可用「＋添加」向导直接接入。

## 工作原理

每 **60 秒**轮询一次（插拔设备时即时触发）：

1. 枚举 HID 接口（SetupAPI）→ 按 VID / usage 页匹配协议族
2. 发送厂商电量查询帧，解析应答
3. 蓝牙设备直接读系统 PnP 属性缓存，**零射频开销**

| 协议族 | 发送方式 | 解析 |
|---|---|---|
| RealTek A7 | feature `0x11` + `0xF9` + 全 FF（65 字节） | 应答逐字节 XOR 0xFF，第 11 字节 = 电量 |
| Compx G87 | 写 `13 4A 01` | input 应答 `r[5]` = 电量 |
| Nordic52（ATK/VXE/VGN） | 17 字节帧 `08 04 00 ... 49` | `r[6]` = 电量，`r[7]` = 充电/有线，校验和 0x55 |
| Nordic54（ATK Zero） | 64 字节帧 | `r[7]` = 电量，`r[8..9]` = 电压 |
| 迈从 hub（G98 V2 有线） | 先 Set `09 87 00 00 01 00 02 00` 再 Get（520 字节 feature） | `[8]` = 电量，`[9]` 高 4 位 = 充电 |
| 蓝牙 | — | `DEVPKEY_Bluetooth_Battery`（零功耗） |

> ⚠️ 电量数字均为设备固件上报的原始值，工具不做估算。部分三模外设的电量计较粗糙，**充电中读数可能卡值 / 抖动**，拔线放电状态下的数值才可靠。

## 编译

双击 `build.bat`，或手动：

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize+ /platform:x64 /target:winexe ^
  /win32manifest:src\batterytray.manifest /out:BatteryTray.exe src\BatteryTray.cs ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll
```

csc 来自系统自带 .NET Framework 4.x，无需安装 SDK / NuGet / Node / Rust 工具链。

安装包编译：`installer\build_setup.bat`（内嵌主程序 + 卸载器，产出单文件 BatteryTraySetup.exe）。

## 目录结构

```
├── build.bat                 一键编译主程序
├── src/BatteryTray.cs        托盘主程序（单文件，约 1300 行）
├── src/HidScan.cs            HID 诊断扫描器（--all 探测各协议，找新设备用）
├── src/batterytray.manifest  DPI / OS 兼容清单
├── installer/                单文件安装包源码（Installer / Uninstall / build_setup.bat）
├── scripts/                  部署与诊断辅助（热替换、状态检查、图标预览）
├── assets/                   预览图
├── devices.txt.example       自定义设备配置模板
└── debug/                    开发期调试产物（gitignore）
```

## 常见问题

**Q：托盘图标不显示？**
A：必须**以管理员身份运行**。Win11 的 UIPI 会拒绝低完整性进程注册托盘图标（Shell_NotifyIcon 返回拒绝访问），这是系统限制；自启走计划任务（RL HIGHEST）可静默提权。

**Q：电量一直不变 / 拔线后跳变？**
A：设备固件电量计的问题，不是工具缓存——工具每 60 秒如实记录上报值。充电中读数不可靠是常见现象，放电状态才准。

**Q：2.4G 接收器显示的是接收器名字，不是鼠标型号？**
A：接收器硬件不暴露配对设备型号，面板显示 HID 产品名；点「✎」改成你喜欢的名字即可（存 devices.txt）。

**Q：支持某款外设吗？**
A：看它是否属于 Compx / Nordic（ATK、VXE、VGN、Dareu、迈从等）；用 HidScan.exe --all 扫一下就知道，能读到电量的通常也能用「＋添加」接入。

**Q：为什么只有几十 KB？**
A：图形界面用系统自带 .NET Framework 4.8，不打包运行时；纯 Win32 P/Invoke + 单文件代码，无第三方库。同功能 Electron 方案约 150 MB，Python 打包约 20 MB。

## 已知限制

- 部分设备不上报充电状态（如 K7 Ultra）
- G98 V2 仅实测有线模式（蓝牙 / 2.4G 子协议未验证）
- 2.4G 接收器不暴露配对设备型号
- 仅 Windows 10 / 11（x64）

## 贡献

欢迎提 Issue / PR。新设备协议适配集中在 `src/BatteryTray.cs` 的 `Try*` 探针函数与 `InitModels` 型号表，调试用 `src/HidScan.cs`。

## License

[MIT](LICENSE) © skrluna