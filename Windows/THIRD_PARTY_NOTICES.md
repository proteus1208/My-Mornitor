# Third-party notices

NJC AI Monitor for Windows uses the following packages:

- **LibUsbDotNet 3.0.224** — LGPL-3.0-or-later. Source and license:
  <https://github.com/LibUsbDotNet/LibUsbDotNet>
- **libusb 1.0.30** — LGPL-2.1-or-later. The Windows x64 and ARM64 runtime binaries
  are taken from the official release archive. Source, binaries and license:
  <https://github.com/libusb/libusb/releases/tag/v1.0.30>
- **LibreHardwareMonitorLib 0.9.6** — MPL-2.0, with separately licensed portions listed
  by that project. Source and licenses:
  <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor>

NuGet resolves the transitive dependencies and copies their required assemblies into the
published application. Recipients retain all rights granted by the corresponding licenses.

The LY USB framing logic is a clean C# port of the protocol implementation in this repository
and is informed by the protocol documentation from `thermalright-trcc-linux`.

Market quotes are requested at runtime from Yahoo Finance, ASX public market data, OKX public
market data, Binance fallback data, and CoinGecko fallback data. These services and their data are
not bundled with the application and remain subject to their respective terms, availability, rate
limits, and market-data delays. The quotes are for informational display only.

News headlines are requested at runtime from TextRadar public pages. Weather observations and
forecasts are requested at runtime from Open-Meteo and are attributed in the dashboard. Neither
service nor its data is bundled with the application; use remains subject to the provider's terms,
availability, and rate limits.

The Frogana application icon artwork was supplied by the repository owner. Confirm that you hold
the required redistribution rights before publishing a fork or binary release; the software's MIT
license does not grant rights to third-party artwork.
