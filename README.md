# softkeys

Custom on-screen keyboard for Windows 11 — a stateless input emitter that never
queries other processes. Draw buttons; on press, inject via `SendInput`; done.

Authoritative specification: [docs/softkeys-sdlc.md](docs/softkeys-sdlc.md).
Behavioral reference (layout, sticky modifiers, auto-repeat): `reference/vboard.py`.

**Status:** v1.0.0 release candidate.

Settings persist to `%APPDATA%\softkeys\settings.json`; corrupt or missing
files fall back to defaults silently.

## Release build

Requires the .NET 8 SDK. Produces one self-contained exe — no installer,
no runtime prerequisite; copy it anywhere and run it (standard user).

    dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

Output: `bin/Release/net8.0-windows/win-x64/publish/softkeys.exe`

**v1.0.0 exe SHA-256:**

    860fefe9149116a2d0b46dacc223f808d1e78f98db73de7aa87f9213c0bc91bd

## Development

    dotnet build
    dotnet run                  # launch the keyboard window (single instance)
    dotnet run -- --selftest    # 163 checks: struct layouts (D-08), scan-code
                                # table, key map, settings fallback
    dotnet run -- --harness     # launches Notepad, types "test" via SendInput
