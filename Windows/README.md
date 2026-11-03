# NJC AI Monitor for Windows

利民 Trofeo Vision 11.3 LCD 的 Windows 10/11 原生版本。它将 Codex、Cursor、
Claude Code 任务、股票/加密货币行情、TextRadar 新闻、天气和系统内存状态渲染为
1920×400 仪表盘，并通过
WinUSB 向 `0416:5408` LCD 发送 JPEG 帧。

## 已实现

- 1920×400 五栏仪表盘、本机缩放预览、PNG/JPEG 快照；
- 右一固定整合时间、天气、CPU、GPU、内存和进程数；
- 左侧四栏优先显示最近活跃的 Codex、Cursor、Claude Code 会话；每个 Agent 面板同时显示工作区/仓库名称与真实聊天标题，不显示对话正文；
- Agent 状态统一为 `Agent Working`、`Task Completed` 和 `Waiting for You`；Working 状态使用呼吸边框、流动高光和圆点脉冲；
- AI 会话完成后，对应栏目持续橙色闪烁，同时弹出不会自动消失的桌面确认卡片；卡片按 Agent 显示动态标题，支持逐条确认、全部确认和托盘清除，未确认提醒会保存到本地并在重启后恢复；
- Codex 工作区优先取会话中的 Git 仓库名，缺失时读取工作目录；聊天标题从 Codex 本地会话索引读取；内部 Guardian 审查不会单独显示，但其活动会归并到父聊天；
- AI 会话不足四栏时依次用 MARKETS、NEWS、AI QUOTA 补齐；完全没有活跃 AI 时再显示未来天气 FORECAST；
- Codex 面板显示本会话 Token、`update_plan` 步骤进度和本地额度读数；Cursor/Claude Code 显示可从本地日志解析的状态与计划；
- MARKETS 显示 BGBL.AX、NDQ.AX、IBIT.AX、IVV.AX 的 AUD 延迟行情，以及 USDT/AUD，
  以及 BTC、ETH、WLFI 的 USDT 实时行情；
- MARKETS 右上角根据 ASX 行情状态显示 `LIVE`、`CLOSED` 或 `UNKNOWN`；
- NEWS 显示 TextRadar 聚合新闻，FORECAST 使用 Open-Meteo 显示未来 6 小时的温度、降雨、风况和温度趋势；
- 自适应 2/15 FPS、托盘菜单；
- WinUSB/LibUsbDotNet 的 LY 握手、512 字节分块、4096 字节批量发送和 ACK；
- USB 插拔轮询、`WM_DEVICECHANGE`、睡眠唤醒后自动重连；
- 登录自启、JPEG 质量和 180° 旋转设置；
- 单实例保护：重复双击 EXE 时不会再次连接 USB，而是提示使用现有托盘程序；
- 自定义青蛙应用图标，并在 EXE、任务栏和系统托盘中统一显示；
- 无硬件预览、USB 诊断、协议与渲染自检。

## 屏幕布局

整张画面为 1920×400，共五个物理栏位。左侧四栏由 `DynamicPanelResolver` 动态安排，最右栏始终固定：

| 活跃 AI 会话数 | 左一至左四 | 最右固定栏 |
|---:|---|---|
| 4 个或更多 | Agent、Agent、Agent、Agent | 时间、当前天气、CPU、GPU、Memory、Processes |
| 3 个 | Agent、Agent、Agent、Markets | 同左 |
| 2 个 | Agent、Agent、Markets、News | 同左 |
| 1 个 | Agent、Markets、News、AI Quota | 同左 |
| 0 个 | Markets、News、AI Quota、Forecast | 同左 |

各面板的职责如下：

- **Agent**：工作区/仓库、真实聊天标题、状态，以及可获得的 Token、计划或额度信息；不显示对话正文；
- **Markets**：四只 ASX ETF 和四项 Crypto 行情、涨跌幅及短周期走势；
- **News**：TextRadar 新闻，每页四条，20 秒自动翻页；
- **AI Quota**：Codex 本地额度与 Claude Code 本地状态；
- **Forecast**：未来数小时的温度、降雨和风况，不重复显示当前天气；
- **固定栏**：时间、当前天气、CPU、GPU、Memory 与 Processes。

## 运行要求

- Windows 10 1809 或更新版本，推荐 Windows 11；
- 利民 Trofeo Vision 11.3 LCD（USB ID `0416:5408`，握手配置 `0x05`）；
- 设备绑定 WinUSB 或 libusbK 驱动。安装官方 TRCC 通常会安装所需驱动；
- 使用 LCD 前必须退出官方 TRCC，同一时间只能有一个程序占用 WinUSB 设备。

系统指标由 LibreHardwareMonitor 提供。普通用户权限可以读取大部分指标，但部分
主板的温度传感器只在“以管理员身份运行”时可用；温度不可用不会影响其他功能。

## 构建

Windows 源码位于 `Windows/src/NjcAI.Monitor`。仓库不提交 .NET SDK、NuGet
缓存、`bin/obj` 或发布产物；构建脚本会在需要时重新生成这些文件。

源码构建需要安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

在仓库根目录打开 PowerShell：

```powershell
.\Windows\build.ps1
```

脚本使用系统的 .NET 8 SDK。输出位于：

```text
Windows\dist\win-x64\NJC AI Monitor.exe
```

生成可分发 ZIP：

```powershell
.\Windows\package.ps1
```

首次构建需要访问 NuGet 下载 LibUsbDotNet 和 LibreHardwareMonitorLib。

### 发布到 GitHub

```powershell
.\Windows\package.ps1 -Runtime win-x64
.\Windows\package.ps1 -Runtime win-arm64
```

压缩包会生成到 `Windows\dist`。该目录已被 Git 忽略，请把 ZIP 作为 GitHub Release
附件上传，不要使用 `git add -f` 把二进制发布产物提交到源码历史。仓库自带的 GitHub
Actions 工作流也会为两个架构生成可下载的 ZIP。

## 使用

双击 `NJC AI Monitor.exe` 后应用驻留系统托盘。程序不需要另装 .NET；请保留同目录的
`libusb-1.0.dll`，它负责通过 WinUSB/libusbK 与 LCD 通信。

当前社区发布的 EXE 未进行代码签名。首次启动如遇 Windows SmartScreen，请先确认 ZIP 来源，
再选择“更多信息”→“仍要运行”。

- 双击托盘图标：打开预览；
- 右键 → 设置：配置开机自启、画面旋转和 JPEG 质量；
- 右键 → USB 诊断：检查设备和驱动；
- 右键 → 重新连接 LCD：在退出官方 TRCC 后立即重试。

AI 日志只从当前用户目录只读加载。程序会扫描 Codex、Cursor 和 Claude Code 的本地会话，
将 `Agent Working` 排在最前，其次是尚未确认的 `Task Completed` 和 `Waiting for You`，再按最后活动时间排序。最近 10 分钟内活动的
会话可以占据前四栏；面板选中后有 30 秒切换冷却，避免多个任务同时写日志时画面频繁跳动。
Codex 会话按日志文件的实际最后更新时间发现；即使任务保存在较早的日期目录，只要最近
14 天内重新使用过，仍会被读取和显示。
只显示 `thread_source` 为 `user` 或 `subagent` 的 Codex 会话；内部的
`guardian_review` 审批日志不会占用面板，但其更新时间会传递给对应父会话，避免正在工作的聊天被误判为空闲。
Codex 的 `task_started`、推理、commentary 和工具调用都会保持 `Agent Working`；final answer、
task complete 或中止事件进入 `Task Completed`，确认后显示 `Waiting for You`。正在运行但暂时没有新日志的任务最多保持 30 分钟，
避免长工具调用被旧版的 90 秒窗口误判。

### Agent 状态生命周期

| 状态 | 含义 | 何时离开 |
|---|---|---|
| `Agent Working` | Agent 正在推理、输出 commentary 或调用工具 | 收到 final、完成或中止事件 |
| `Task Completed` | 任务已结束但提醒尚未确认 | 点击桌面卡片“确认”、托盘清除或全部确认 |
| `Waiting for You` | Agent 已回复，等待用户继续操作 | 会话最后活动超过 10 分钟 |

常规顺序为 `Agent Working` → `Task Completed` → `Waiting for You` → 从动态栏移除。
10 分钟窗口以会话的**最后活动时间**计算，不会因为确认完成卡片而重新计时；尚未确认的
`Task Completed` 不受该窗口影响，会持续闪烁并在重启后恢复。如果用户未点击确认便直接
继续同一会话，检测到新的工作事件后会自动清除旧提醒、关闭桌面卡片，并切换回
`Agent Working`；其他会话的完成提醒不受影响。

该规则与上方“屏幕布局”表一致。面板选中后会保持 30 秒，减少多个日志同时更新造成的频繁换位。

AI QUOTA 在动态栏位仍有空位时作为备用面板出现，并且只关注 Codex 与 Claude Code。Codex 的
剩余百分比、额度窗口和重置倒计时来自本地会话事件，不读取登录令牌；Claude Code 的
标准本地会话日志目前不包含 5 小时/7 天额度，因此未获得数据时会显示 `NOT CONNECTED`
或 `WAITING FOR /usage`，而不是估算一个不可靠的数值。

### 免费行情来源

- BGBL.AX、NDQ.AX、IBIT.AX、IVV.AX：Yahoo Finance 非官方公开接口，每 60 秒刷新一次；
- USDT/AUD、BTC、ETH、WLFI：使用 OKX Australia 的 `USDT-AUD`、`BTC-USDT`、
  `ETH-USDT`、`WLFI-USDT` 公开行情，每 5 秒刷新一次。BTC、ETH、WLFI 的 OKX
  交易对暂不可用时依次回退 Binance 和 CoinGecko；USDT/AUD 只使用 OKX。
- USDT/AUD 表示 OKX 市场中 1 USDT 可兑换多少 AUD，不是严格意义上的银行间法定
  USD/AUD 汇率；它和其他 Crypto 行情一样按照 OKX 的真实报价精度显示。
- ASX 价格固定显示两位小数；Crypto 每 12 小时读取 OKX `instruments` 中的 `tickSz`，
  按照各交易对的真实报价精度显示（LCD 最多保留六位小数）。使用 Binance 回退行情时改用
  Binance `PRICE_FILTER.tickSize`；精度元数据不可用时使用安全回退格式，五分钟后重试。
- 程序记录每条 Crypto 行情来源并显示在面板底部；OKX 返回时间戳早于当前已显示行情时，
  会保留较新的现有价格，避免多个服务节点的缓存差异导致价格时间倒退。
- 所有公开行情接口都不需要填写 API Key。短暂断网或被限流时，屏幕保留最后一次成功价格并显示提示。

### 免费新闻和天气来源

- NEWS：每 5 分钟读取一次 [TextRadar](https://textradar.app/)；优先尝试结构化数据，
  不可用时解析网站公开页面。屏幕按重要度和时间排序，每页显示 4 条并自动翻页；
- FORECAST：每 5 分钟读取一次 [Open-Meteo](https://open-meteo.com/) 的免密钥 API，
  显示未来 6 小时温度、降雨概率、风速风向、今日高低温和温度趋势；
- 默认位置为 Sydney。可在“设置”中填写显示名称、纬度和经度；
- 新闻与天气都会缓存最后一次成功结果，短暂断网时继续显示缓存。

### 刷新频率

| 内容 | 频率 |
|---|---:|
| 系统指标 | 约 900 毫秒 |
| Agent 本地日志与完成提醒 | 约 1.5 秒 |
| OKX Crypto 行情 | 约 5 秒 |
| Yahoo Finance ASX 行情 | 约 60 秒（在 5 秒行情循环内节流） |
| TextRadar 新闻 | 约 5 分钟 |
| Open-Meteo 天气 | 约 5 分钟 |
| News 自动翻页 | 20 秒 |
| LCD 渲染 | 空闲 2 FPS；动画或提醒活跃时 15 FPS |

网络请求失败时程序会保留最近一次成功数据；行情循环失败后约 20 秒重试，新闻/天气循环
失败后约 30 秒重试。以上是程序刷新频率，不代表数据提供方本身的交易所级实时延迟。

程序只把公开标的代码发送给行情服务。它不会读取 `.codex\auth.json`；Codex 额度只取自
本地会话事件中的 `rate_limits`，不会发送 OAuth 令牌。免费行情适合作为桌面信息显示，
不应作为下单或财务决策的唯一依据。

## 命令行

```powershell
& ".\NJC AI Monitor.exe" --preview
& ".\NJC AI Monitor.exe" --demo --preview
& ".\NJC AI Monitor.exe" --demo-idle --preview
& ".\NJC AI Monitor.exe" --demo-completed --preview
& ".\NJC AI Monitor.exe" --snapshot dashboard.png
& ".\NJC AI Monitor.exe" --demo --snapshot demo.png
& ".\NJC AI Monitor.exe" --demo-idle --snapshot idle.png
& ".\NJC AI Monitor.exe" --demo-completed --snapshot completed.png
& ".\NJC AI Monitor.exe" --self-test
```

`--snapshot` 会尝试获取一次真实行情、新闻和天气；`--demo --snapshot` 不联网，演示
动态 Agent/信息面板；`--demo-completed` 演示一个工作中、一个刚完成的双 Agent 画面；`--demo-idle` 演示
“MARKETS + NEWS + AI QUOTA + FORECAST”。

`--self-test` 会验证 11.3/9.16 握手区分、LY 分块、动态面板的 4/3/2/1/0 AI 状态、
行情/新闻/天气解析、1920×400 渲染和 JPEG 大小，成功时退出码为 `0`。

## 发布包内容

正式 ZIP 只包含以下文件，不包含源码、PDB、缓存或用户数据：

```text
NJC AI Monitor.exe
libusb-1.0.dll
README.md
LICENSE
THIRD_PARTY_NOTICES.md
NOTICE.md
```

`win-x64` 适用于绝大多数 Intel/AMD Windows 电脑；`win-arm64` 仅用于 ARM64 Windows。

## USB 故障排查

1. 完全退出利民官方 TRCC，包括系统托盘中的后台进程；
2. 在设备管理器确认硬件 ID 包含 `VID_0416&PID_5408`；
3. 先运行应用中的“USB 诊断”；
4. 如果设备可见但无法打开，确认它使用 WinUSB/libusbK 驱动；
5. 不建议直接用 Zadig 覆盖官方驱动。只有确认可恢复原驱动时才进行替换。

日志位于 `%LOCALAPPDATA%\NJC AI Monitor\NJC-AI-Monitor.log`。

11.3 与部分 9.16 型号共用 `0416:5408`。程序会读取初始化握手的型号配置，只有
`0x05` 才按 11.3 的 1920×400 画面发送；检测到 9.16 时会拒绝发送，避免错误尺寸画面。

从旧版应用升级时，程序会自动迁移原有
设置、行情/新闻/天气缓存和登录自启项。

## 第三方组件

项目来源与致谢见仓库或发布包附带的 `NOTICE.md`，第三方组件详见
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。应用图标的再分发权由发布者负责确认。
