# Contributing

NJC AI Monitor targets Windows 10/11 and the Thermalright Trofeo Vision 11.3 LCD.

## Development setup

1. Install the .NET 8 SDK.
2. Clone the repository.
3. Run `./Windows/build.ps1` from PowerShell.
4. Run `./Windows/package.ps1 -Runtime win-x64` before preparing a release.

The build script publishes a self-contained application and runs the built-in self-test. Build
outputs are written under `Windows/dist` and must not be committed.

## Change guidelines

- Keep device I/O in `Usb`, data acquisition in `Services`, drawing in `Rendering`, and WPF
  behavior in `UI`.
- Do not add API keys, authentication tokens, local logs, build outputs, or user-specific paths.
- Preserve offline behavior: a failed public data request must not stop LCD rendering.
- Add or update a self-test when changing parsers, panel selection, protocol framing, or rendering.
- Keep the x64 and ARM64 packaging workflows working.

## Pull requests

Describe the user-visible change, the hardware or preview mode used for verification, and any new
network data source. By contributing, you agree that your contribution is licensed under the MIT
License.
