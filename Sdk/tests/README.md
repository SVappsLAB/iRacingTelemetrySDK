# Testing

## Unit Tests

Unit tests cover individual classes and functions without requiring iRacing or live telemetry.

```powershell
dotnet test .\Sdk\tests\UnitTests\UnitTests.csproj
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

## Manual Tests

Manual tests depend on local developer setup, local telemetry files, timing-sensitive behavior, or deliberate inspection. Run them intentionally:

```powershell
dotnet run --project .\Sdk\tests\SmokeTests\SmokeTests.csproj -- --filter-trait Category=manual
dotnet run --project .\Sdk\tests\UnitTests\UnitTests.csproj -- --filter-trait Category=manual
```

## All Tests

The full solution test run may include tests that require live iRacing data or local manual-test setup.

```powershell
dotnet test .\Sdk\SVappsLAB.iRacingTelemetrySDK.slnx
```

Do not rely on `dotnet test --filter unit`, `--filter ibt`, or `--filter live` with the current Microsoft.Testing.Platform setup; those VSTest-style filters are ignored. Use `--filter-trait Category=...` with `dotnet run --project` for filtered MTP test runs.
