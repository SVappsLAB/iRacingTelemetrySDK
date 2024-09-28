# Tools

## vars

Some python scripts to help with the generation of the `iRacingData.cs` file used during code generation.

* findDistinctVars.py

    Reads a glob of *.csv files (from various variable dumps of live and ibt files), and outputs a single CSV file with the unique/distinct set of variables

* csv-to-cs.py

    Reads a CSV file of variables and outputs a snippet of C# code, that can be merged with the `iRacingData.cs` file

### usage

```bash
python findDistinctVars.py "variableDumps/*.csv" "distinctVars.csv"
python csv-to-cs.py "distinctVars.csv" "varDictData.cs"
```
use merge tool to manually merge `varDictData.cs` with `iRacingData.cs`
