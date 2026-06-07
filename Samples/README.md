# Samples

This folder contains a number of example projects showing ways to use the SDK

All of these examples can be used with a running 'live' instance of iRacing, or with a previously saved IBT file.

All samples define a `TelemetryHandlers<TelemetryData>` variable and pass it to `Monitor(handlers, ct)` for telemetry consumption. For setup and basic usage see the [main README](../README.md); for direct stream access and other advanced patterns see [Advanced Usage](../docs/ADVANCED.md).

* [MinimalExample](./MinimalExample/)

    The smallest end-to-end example. A good starting point that shows how to create a client and handle telemetry, session info, connection state, and error callbacks via `Monitor(handlers, ct)`.

* [DumpVariables_DumpSessionInfo](./DumpVariables_DumpSessionInfo/)

    Connects to a session (live or IBT file)
    * saves the iRacing variables to a CSV file named `iRacingVariables.csv`
    * saves the raw yaml "session info" string to a file named `IRacingSessionInfo.yaml`

    This is good program to run to see what telemetry variables and session information is available.

* [LocationAndWarnings](./LocationAndWarnings/)

    Shows the use of the bitfield `Flags` and the `Enumerations` iRacing provides, describing the state of the car engine and the track surface.

* [SpeedRPMGear](./SpeedRPMGear/)

    Simple program to output cars telemetry data for speed, engine rpm and what gear it's in.
    [New] - Use 'p' (pause) and 'r' (resume) keys on the keyboard to pause and resume the telemetry output.
