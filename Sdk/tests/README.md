# Testing

## unit tests

simple tests for individual classes and functions. 

can be run anytime.

    dotnet test --filter unit

## offline

**IBT_Tests** - this project loads and extracts data from IBT files in the `IBT_Tests/data` directory

can be run anytime.

	dotnet test --filter ibt

## online

**Live_Tests** - this project connects to a running instance of iRacing, and pulls data from the sim

these tests can **only** be run against iRacing, in a live session.

to run these tests
1. load a track and car in iRacing
1. you must get **into** the car, so the sim is sending telemetry data (sitting in the pits is fine)
1. run the `Live_Tests` project

    dotnet test --filter live



