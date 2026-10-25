---
description: Triage a reported bug - reproduce it minimally, decide if it is valid and why, fix, test, document
argument-hint: "<bug report text>"
---
Bug report: $ARGUMENTS

1. Reproduce with the smallest possible system/run (CLI in a scratch folder or a C# test). Record the exact
   symptom. If it does not reproduce, find the condition that does (the reported cause may be close but wrong).
2. Verdict: valid / valid with a different cause / not a bug -- with the evidence (numbers, analytic value).
3. Write a failing test that captures it, then fix the cause (not the symptom). Check the same pattern elsewhere
   (1-D vs spatial vs carrier-resolved; CLI vs Python).
4. Full tests; Python tests after `build_engine.py` if C# changed; docs and CHANGELOG (Unreleased → Fixed) with a
   one-sentence description a user recognises.
5. Report per bug: verdict, cause, fix, test name.
