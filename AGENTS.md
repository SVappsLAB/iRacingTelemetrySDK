# AGENTS.md

This file gives coding agents the repo-level context needed to work on iRacingTelemetrySDK.

For consumer-app usage patterns, read `docs/ai/SDK_USAGE.md` first and `docs/ai/SDK_REFERENCE.md` only for advanced scenarios.

## Layout

- `Sdk/SVappsLAB.iRacingTelemetrySDK/` - Core SDK, telemetry client, data providers, models, YAML parsing, and sim control (win32 broadcast commands).
- `Sdk/SVappsLAB.iRacingTelemetrySDK.CodeGen/` - Roslyn source generator for strongly typed telemetry records.
- `Sdk/SVappsLAB.iRacingTelemetrySDK.EnumsAndFlags/` - iRacing telemetry variable enums, flags, and related types.
- `Sdk/tests/UnitTests/` - Unit tests.
- `Sdk/tests/SmokeTests/` - IBT and live integration/smoke tests.
- `Samples/` - Example consumer applications.
- `docs/ai/` - Consumer-focused guidance for AI coding agents.

## Commands

```powershell
dotnet build .\Sdk\SVappsLAB.iRacingTelemetrySDK.slnx
dotnet build .\Samples\Samples.slnx
dotnet test --project .\Sdk\tests\UnitTests\UnitTests.csproj --no-build
dotnet run --project .\Sdk\tests\SmokeTests\SmokeTests.csproj -- --filter-trait Category=ibt
dotnet pack .\Sdk\SVappsLAB.iRacingTelemetrySDK\SVappsLAB.iRacingTelemetrySDK.csproj
```

`Category=live` smoke tests need iRacing running in an active Windows session; without it they report as skipped (via `Assert.SkipUnless`), not failed. `Category=manual` tests are marked `Explicit = true`, so they never run by default - add `--explicit on` to run them, and only when the local developer setup matches the test requirements.

`global.json` pins `test.runner` to `Microsoft.Testing.Platform`, so `dotnet test` needs `--project`/`--solution` (a bare positional path is rejected) and does not understand VSTest-style `--filter`. Use `dotnet run --project`, not `dotnet test`, for `SmokeTests` - its `launchSettings.json` injects a default `--filter-class` that `dotnet test` combines with (rather than overrides with) any `--filter-trait` you add, silently yielding "zero tests ran". See `Sdk/tests/README.md` for details.

## Style

- Target .NET 8 for the SDK.
- Keep nullable annotations enabled and handle nullable telemetry values explicitly.
- Use a `TelemetryHandlers<TelemetryData>` variable and pass it to `Monitor(handlers, ct)` for normal telemetry consumption.
- Keep telemetry handlers fast, especially `OnTelemetryUpdate` at 60 Hz.
- Preserve public API docs when changing SDK contracts.

## Docs

- `README.md` - User-facing overview and getting started.
- `docs/ADVANCED.md` - Direct stream access and advanced cancellation behavior.
- `docs/MIGRATION_GUIDE.md` - Upgrade guidance.
- `docs/ai/SDK_USAGE.md` - Consumer-app usage guide for AI coding agents.
- `docs/ai/SDK_REFERENCE.md` - Advanced consumer-app reference for AI coding agents.
