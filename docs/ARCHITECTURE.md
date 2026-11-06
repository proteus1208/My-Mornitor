# Architecture

NJC AI Monitor is a Windows tray application that turns local and public data into a
1920×400 JPEG dashboard and streams it to a Thermalright Trofeo Vision 11.3 LCD.

## Runtime flow

```text
local agent logs ─┐
system sensors ───┼─> Services ─> DashboardSnapshot ─> Rendering ─> JPEG ─> Usb ─> LCD
public data APIs ─┘                                  └────────────> UI preview
```

`AppController` owns the refresh loops and coordinates the tray, settings, renderer, and USB
connection. It is the composition root; feature-specific parsing and device details should not be
added there.

## Source map

- `Core/` — immutable data models, settings, logging, demo data, and the single-instance guard.
- `Services/` — agent log and Grok Bot local-cache parsing, completion tracking, market/news/weather collection, system
  metrics, and dynamic panel selection. Codex child/guardian activity is aggregated into its
  user-owned parent chat instead of becoming a separate visible panel.
- `Rendering/` — converts a `DashboardSnapshot` into the fixed 1920×400 holographic dashboard;
  the first four columns are dynamic and the fifth fixed panel combines time, current weather,
  CPU, GPU, memory, and process status.
- `UI/` — WPF preview, settings, and persistent task-completion cards.
- `Usb/` — LY framing, device discovery, connection lifecycle, and frame transfer.
- `SelfTest.cs` — dependency-free smoke tests used by local builds and GitHub Actions.

## Panel selection and agent lifecycle

`DynamicPanelResolver` selects up to four sessions, ordered by working state, unacknowledged
completion, attention state, and last activity. It applies a 30-second replacement cooldown to
avoid panel churn. Sessions with ordinary activity leave the dashboard 5 minutes after their last
event; an unacknowledged completion remains until the user confirms it. A running turn may stay in
the working state for up to 30 minutes without a new event so a long tool call is not mislabeled.
When that same session records a newer working event, `AgentCompletionTracker` automatically removes
its stale persisted completion alert; the desktop card closes and the panel returns to working.

The four dynamic columns resolve to:

| Active sessions | Dynamic columns |
|---:|---|
| 4+ | Agent × 4 |
| 3 | Agent × 3, Markets |
| 2 | Agent × 2, Markets, News |
| 1 | Agent, Markets, News, AI Quota |
| 0 | Markets, News, AI Quota, Forecast |

The Forecast panel intentionally contains future conditions only. Current weather belongs to the
fixed fifth column, which prevents duplicate information when Forecast is selected.

## Refresh cadence

- System metrics: approximately every 900 ms.
- Agent session logs: approximately every 1.5 seconds.
- Crypto market loop: every 5 seconds; slower ASX sources are internally throttled.
- News and weather: every 5 minutes.
- News pagination: every 20 seconds, four articles per page.
- LCD rendering: 2 FPS when idle and 15 FPS when animation or attention is active.

## Persistence

Settings, caches, logs, and unacknowledged completion alerts are stored under
`%LOCALAPPDATA%\NJC AI Monitor`. The repository and release package contain no user data or
credentials.

## Reliability boundaries

Network collectors retain their last successful values and log transient failures. USB reconnect
logic is independent of data collection. Only the USB layer may open the display, and the process
uses a named mutex to prevent multiple instances from competing for the device.

## Release boundary

`Windows/build.ps1` publishes a self-contained executable and runs `--self-test` on the host
architecture. `Windows/package.ps1` stages a clean release and creates architecture-specific ZIPs.
Release archives contain only the executable, native libusb dependency, README, license, project
notice, and third-party notices. They intentionally exclude symbols, caches, credentials, and
user data.
