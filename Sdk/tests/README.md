# Testing

## Unit Tests

Unit tests cover individual classes and functions without requiring iRacing or live telemetry.

```powershell
dotnet test --project .\Sdk\tests\UnitTests\UnitTests.csproj
```

## Offline IBT Smoke Tests

IBT smoke tests use bundled `.ibt` files under `Sdk/tests/SmokeTests/data/ibt` and are the preferred repeatable smoke test set.

```powershell
dotnet run --project .\Sdk\tests\SmokeTests\SmokeTests.csproj -- --filter-trait Category=ibt
```

## Live Smoke Tests

Live tests require iRacing running on Windows in an active session. Load a track and car, get into the car so the simulator sends telemetry data, then run:

```powershell
dotnet run --project .\Sdk\tests\SmokeTests\SmokeTests.csproj -- --filter-trait Category=live
```

If iRacing isn't running (its shared-memory status isn't "connected"), the live tests are reported as
**skipped** via `Assert.SkipUnless` rather than timing out and failing. Being in iRacing doesn't guarantee
telemetry is flowing, though - you still need to be in the car.

## Manual Tests

Manual tests depend on local developer setup, local telemetry files, timing-sensitive behavior, or deliberate inspection.
They are marked `Explicit = true`, so no test run includes them unless you pass `--explicit on`:

```powershell
dotnet run --project .\Sdk\tests\SmokeTests\SmokeTests.csproj -- --filter-trait Category=manual --explicit on
dotnet run --project .\Sdk\tests\UnitTests\UnitTests.csproj -- --filter-trait Category=manual --explicit on
```

## All Tests

The full solution test run skips live tests when iRacing isn't running and never runs explicit (manual) tests.

```powershell
dotnet test --solution .\Sdk\SVappsLAB.iRacingTelemetrySDK.slnx
```

This repo pins `test.runner` to `Microsoft.Testing.Platform` in `global.json` (required on the .NET 10 SDK,
where `dotnet test`'s legacy VSTest bridge no longer supports MTP-based projects like these xUnit v3 ones).
Two consequences:

- `dotnet test` requires `--project <csproj>` or `--solution <slnx>` - a bare positional path is rejected.
- `--filter unit` / `--filter ibt` / `--filter live` (VSTest-style filters) are not supported at all; use
  `--filter-trait Category=...` instead (append it after `--`, e.g. `dotnet test --project X.csproj --
  --filter-trait Category=unit`).

For `SmokeTests` specifically, prefer `dotnet run --project` over `dotnet test` as shown above:
`SmokeTests`' `Properties/launchSettings.json` supplies a default `--filter-class` argument that `dotnet
test` combines with (rather than overrides with) any `--filter-trait` you pass on the command line, which
silently produces "zero tests ran" instead of an error. `dotnet run --project` does not have this problem.
