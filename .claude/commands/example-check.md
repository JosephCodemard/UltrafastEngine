---
description: Re-run documentation examples and update the numbers quoted in their READMEs and tutorials
argument-hint: "[example folder names, e.g. 21-measured-data 26-transforms; empty = all]"
---
Re-check examples: $ARGUMENTS

```bash
dotnet build src/UltrafastEngine.Cli -c Release -f net10.0 2>&1 | grep -E "rror\(s\)"
python3 build/example_check.py $ARGUMENTS                  # add --against HEAD to diff every summary.json against a git ref
```

The script (`build/example_check.py`, read its docstring) runs every command of the example READMEs and tutorials in strict mode and
checks the quoted output blocks and `key | number` table rows. Examples 07 and cpp are manual. Prose and prose tables are not
checked: when `--against` shows a change, update them by hand and explain any change larger than noise in the README or CHANGELOG.
Plain ``` blocks count as expected output: use ```text for anything else.
