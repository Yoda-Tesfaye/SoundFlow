# SoundFlow

把手机（或其他蓝牙音频源）的声音通过蓝牙实时送到这台电脑上播放。常驻通知区域，
**没有主窗口**，所有操作都在任务栏右键菜单里完成。

> 它只负责“接收”：电脑作为蓝牙音频接收端（A2DP Sink）接收手机送来的声音，
> 不是把已配对的耳机切换成电脑的输出设备。

## 特性

- 托盘单图标，无主窗口、无后台服务、不联网；
- 右键（或左键）图标：已配对的手机列表，点击开始接收，再点一次断开；
- Windows 11 原生观感：DWM 圆角与投影、浅色/深色主题、系统强调色、Segoe UI Variable、随 DPI 缩放；
- **便携单文件**：无安装程序、不下载运行时（.NET Framework 4.8 是 Windows 10/11 自带组件）；
- 设备名解析：读取经典蓝牙设备真实名称，避免菜单里出现 A2DP 接口的随机名；
- 接收音频期间暂停后台蓝牙枚举，尽量不打扰正在播放的流。

## 下载与使用（便携版）

1. 下载 [`dist/SoundFlow-1.5.0-win64-portable.zip`](dist/SoundFlow-1.5.0-win64-portable.zip)，解压到任意目录（无需安装）；
2. 双击 `SoundFlow.exe`，通知区域出现蓝牙图标；
3. 先在系统“蓝牙设置”里与手机配对（只做一次），然后右键托盘图标 → 选择手机；
4. 手机上播放的声音就会从电脑输出；再点一次同一设备断开。菜单里“退出”会断开全部连接。

重复运行不会多开，只会弹出已有实例的菜单。勾选“开机启动”可登录后自动运行。

## 菜单

| 菜单项 | 作用 |
| --- | --- |
| 已配对的手机列表 | 点击开始接收；接收中的设备带勾选与“接收中”，再次点击断开 |
| 刷新设备 | 重新读取已配对的音频源设备（并重新解析设备名） |
| 蓝牙设置 | 打开“设置 → 蓝牙和设备”，用于配对手机 |
| 开机启动 | 登录后自动运行（写入当前用户的启动项） |
| 退出 | 断开所有连接并退出 |

## 系统要求

Windows 10 2004 及以上 / Windows 11（x64）。程序为托管 .NET Framework 4.8 应用，
运行时随 Windows 提供，无需额外安装。

## 构建

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

构建只使用 Windows 自带的编译器
（`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`）和 `%WINDIR%\System32\WinMetadata`
下的 WinRT 元数据，不需要 Visual Studio、Windows SDK 或 .NET SDK。

产物为单个 `SoundFlow.exe`（约 90 KB）：托管代码、无附加 DLL、无运行时下载。

## 目录

```
SoundFlow.exe                可执行文件（便携单文件）
app.manifest                 高 DPI（Per-Monitor V2）与权限清单
build.ps1                    一键构建
assets/app.ico               程序图标（由 build.ps1 生成）
docs/                        截图
src/Native.cs                Win32 互操作
src/Theme.cs                 系统颜色 / 字体 / 强调色
src/AudioLink.cs             WinRT A2DP Sink 封装、设备后台刷新、错误日志
src/TrayIcon.cs              通知区域图标、提示与气泡
src/MenuForm.cs              Win11 风格弹出菜单（自绘）
src/Program.cs               程序入口、状态机、菜单内容
tools/IconGen.cs             生成多尺寸 ICO
```

## 关于设备名称

有些手机会在蓝牙 LE 上广播一个随机名称（例如 `NGMB40c9FSJYx4567IBBh9lC0`），而 Windows 给
“A2DP 音频源接口”取显示名时会取到它，导致菜单里出现一串随机字符。

程序先用设备 ID 里的蓝牙地址找到经典蓝牙设备对象并读取它的真实名称（例如 `YODATESFAYE📱`，
其中 emoji 因 GDI 文本渲染限制会被去掉），拿不到时才退回接口名；如果接口名仍是那种 24 位
随机串，则显示“蓝牙设备 XXXX”（MAC 尾号）以避免误导。结果按 MAC 缓存，菜单里的“刷新设备”
会清空缓存重新读取。

## 实现要点

- 连接：WinRT `Windows.Media.Audio.AudioPlaybackConnection`，`GetDeviceSelector()` →
  `TryCreateFromId()` → `StartAsync()` → `OpenAsync()`；连接期间用定时器轮询异步操作状态，不阻塞界面。
- 菜单：无边框弹出窗口 + 自绘。Win11 的圆角与阴影由 DWM 提供
  （`DWMWA_WINDOW_CORNER_PREFERENCE`、`DwmExtendFrameIntoClientArea`），文字用 GDI 渲染保持清晰；
  颜色、字体、强调色在每次打开时读取系统设置，因此能即时跟随浅色/深色主题。
- 设备枚举在后台 MTA 线程刷新（8 秒一次，启动时先跑一次），打开菜单无需等待蓝牙协议栈；
  接收音频期间暂停自动刷新，只有点“刷新设备”才会主动重新枚举。
- 无后台服务、不联网；唯一写入的配置是 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  下的启动项（菜单里可随时取消）。

## 许可

[MIT](LICENSE)。行为与交互参考了 [ysc3839/AudioPlaybackConnector](https://github.com/ysc3839/AudioPlaybackConnector)（MIT），
代码为独立实现。
