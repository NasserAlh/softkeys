# softkeys

Custom on-screen keyboard for Windows 11 — a stateless input emitter that never
queries other processes. Draw buttons; on press, inject via `SendInput`; done.

Authoritative specification: [docs/softkeys-sdlc.md](docs/softkeys-sdlc.md).
Behavioral reference (layout, sticky modifiers, auto-repeat): `reference/vboard.py`.

**Status:** M2 — window shell (NOACTIVATE, topmost-once, capture exclusion, opacity).

## Build

Requires the .NET 8 SDK.

    dotnet build

## Run

    dotnet run                  # launch the keyboard window (single instance)
    dotnet run -- --selftest    # struct-layout (D-08) + scan-code table tests
    dotnet run -- --harness     # launches Notepad, types "test" via SendInput
