# NJC AI Monitor for Windows

NJC AI Monitor is a native Windows 10/11 dashboard for the Thermalright Trofeo Vision 11.3 LCD.
It renders local AI-agent activity, public market data, English news, weather, and system metrics
into a 1920×400 five-column frame and transfers JPEG frames to the display over WinUSB.

For screenshots and the concise project overview, see the repository [README](../README.md).

## Display layout

The first four columns are dynamic. Recent AI sessions take priority and information panels fill
the remaining space. The fifth column always shows time, current weather, CPU, GPU, memory, and
process status.

| Active AI sessions | Dynamic columns 1–4 | Fixed column 5 |
|---:|---|---|
| 4 or more | Agent × 4 | Time, weather, CPU, GPU, memory, processes |
| 3 | Agent × 3, Markets | Same |
| 2 | Agent × 2, Markets, News | Same |
| 1 | Agent, Markets, News, AI Quota | Same |
| 0 | Markets, News, AI Quota, Forecast | Same |

Agent panels show the workspace/repository, real chat title, state, and any locally available
token, plan, or allowance information. Conversation text is never shown on the LCD.

## Agent lifecycle

The application reads local Codex, Cursor, Claude Code, and Grok Bot session records. Codex guardian or
internal child activity is aggregated into its user-owned parent chat instead of occupying a
separate panel.

Grok Bot integration reads its local selected-session roster and weekly usage cache. It does not
require a Grok API key or send Grok account data anywhere.

The AI Quota card is configurable in Settings: choose zero, one, or two primary gauges, then add
any Codex, Claude Code, or Grok Bot square-meter rows independently.

| State | Meaning | Transition |
|---|---|---|
| `Agent Working` | The agent is reasoning, writing commentary, or using tools | A final, completed, or aborted event is recorded |
| `Task Completed` | Work finished and its alert is not acknowledged | The user confirms the desktop card or clears alerts from the tray |
| `Waiting for You` | The reply is complete and acknowledged | The session leaves the display after 5 minutes without activity |

An unacknowledged completion remains visible and is restored after restart. If the same chat starts
working again before acknowledgement, its stale alert closes automatically and the panel returns
to `Agent Working`. Panel selection has a 30-second cooldown to avoid visual churn.

## Data sources and refresh cadence

### Markets

- `BGBL.AX`, `NDQ.AX`, `IBIT.AX`, and `IVV.AX`: Yahoo Finance public endpoints, throttled to
  approximately 60 seconds.
- `USDT/AUD`, `BTC`, `ETH`, and `WLFI`: OKX public market data, refreshed approximately every
  5 seconds. BTC, ETH, and WLFI can fall back to Binance and CoinGecko when necessary.
- ASX prices use two decimals. Crypto precision follows exchange tick-size metadata, up to six
  decimals on the LCD.
- No market-data API key is required. Quotes are for informational display only.

### News

- GDELT DOC API for world, business, markets, geopolitics, and Australian coverage.
- Official Hacker News API for AI and technology stories.
- CoinDesk RSS for crypto and finance coverage.
- News refreshes approximately every 5 minutes and rotates four headlines every 20 seconds.

### Weather and system data

- Open-Meteo provides current conditions and the future forecast without an API key, refreshed
  approximately every 5 minutes.
- The default location is Sydney. Name, latitude, and longitude are configurable in Settings.
- System metrics refresh approximately every 900 ms through LibreHardwareMonitor.
- Agent logs refresh approximately every 1.5 seconds.
- LCD rendering runs at 2 FPS while idle and up to 15 FPS for animation or attention states.

Collectors retain their last successful values during transient network failures. Settings,
caches, logs, and pending completion alerts are stored under `%LOCALAPPDATA%\NJC AI Monitor` and
are not part of the repository or release archive.

## Color themes

Five layout-preserving themes are included:

- Bitcoin Orange
- Web3 Emerald
- Graphite Titanium
- Cyber Neon
- Solana Aurora

Choose a theme from **Settings → Color Theme**, then select **Save Settings**. The palette applies
to the LCD, local preview, market charts, agent animation, quota graphics, and completion alerts.

## Requirements

- Windows 10 version 1809 or newer; Windows 11 is recommended.
- Thermalright Trofeo Vision 11.3 LCD with USB ID `0416:5408` and 11.3 handshake profile `0x05`.
- WinUSB or libusbK driver for the LCD.
- The official TRCC application must be fully closed before NJC AI Monitor connects because only
  one process can own the device.

The program rejects the incompatible 9.16 handshake profile even when it shares the same USB ID,
preventing a 1920×400 frame from being sent to the wrong panel.

## Download and use

Download the release ZIP matching your architecture. Most Intel and AMD PCs use `win-x64`;
`win-arm64` is for ARM64 Windows only. Extract the entire folder and run:

```text
NJC AI Monitor.exe
```

The release is self-contained; a separate .NET installation is not required. Keep
`libusb-1.0.dll` beside the executable. Current community binaries are unsigned, so Windows
SmartScreen may require **More info → Run anyway** after you verify the download source.

The application stays in the system tray:

- Double-click the tray icon to open the local preview.
- Open **Settings** to configure theme, launch at login, rotation, JPEG quality, weather, and
  completion alerts. Launch at login uses a 20-second delayed current-user startup shortcut, with
  the current-user Run key retained as a fallback when shortcut creation is unavailable.
- Use **Reconnect LCD** after closing another program that owned the device.
- Use **USB Diagnostics** to inspect discovery, profile, and driver status.
- Use **Export Current Frame** to save the current 1920×400 image.

## Build from source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then run from the
repository root:

```powershell
.\Windows\build.ps1
```

The build publishes a self-contained executable and runs the built-in self-test. Output is written
under `Windows\dist`, which is ignored by Git.

Create clean release ZIPs with:

```powershell
.\Windows\package.ps1 -Runtime win-x64
.\Windows\package.ps1 -Runtime win-arm64
```

Each package contains only:

```text
NJC AI Monitor.exe
libusb-1.0.dll
README.md
LICENSE
NOTICE.md
THIRD_PARTY_NOTICES.md
```

It excludes source files, symbols, SDK files, caches, logs, credentials, and user data.

## Command-line modes

```powershell
& ".\NJC AI Monitor.exe" --preview
& ".\NJC AI Monitor.exe" --demo --preview
& ".\NJC AI Monitor.exe" --demo-idle --preview
& ".\NJC AI Monitor.exe" --demo-completed --preview
& ".\NJC AI Monitor.exe" --snapshot dashboard.png
& ".\NJC AI Monitor.exe" --demo-idle --snapshot idle.png
& ".\NJC AI Monitor.exe" --demo-idle --snapshot theme.png --theme CyberNeon
& ".\NJC AI Monitor.exe" --self-test
```

Snapshot mode without demo data performs one live collection pass. Demo snapshot modes are offline.
The optional `--theme` argument accepts `BitcoinOrange`, `Web3Emerald`, `GraphiteTitanium`,
`CyberNeon`, and `SolanaAurora`; it affects only that
snapshot and does not overwrite saved settings.

## USB troubleshooting

1. Exit TRCC completely, including its system-tray process.
2. Confirm Device Manager reports hardware ID `VID_0416&PID_5408`.
3. Run **USB Diagnostics** from the tray menu.
4. If the device is visible but cannot open, confirm it uses WinUSB or libusbK.
5. Avoid replacing the official driver unless you know how to restore it.

Logs are written to `%LOCALAPPDATA%\NJC AI Monitor\NJC-AI-Monitor.log`.

## Privacy and security

Agent information is read only from local session records. NJC AI Monitor does not upload
conversation text, read Codex authentication files, or request exchange API keys. Public network
requests contain only market symbols, news queries, or configured weather coordinates.

Do not commit `.tools`, `bin`, `obj`, `Windows/dist`, local logs, settings, caches, or credentials.
The repository `.gitignore` already excludes the normal local build and runtime directories.

## Third-party notices

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for libraries, runtime services, and license
information. Project origins and acknowledgements are documented in [NOTICE.md](../NOTICE.md).
