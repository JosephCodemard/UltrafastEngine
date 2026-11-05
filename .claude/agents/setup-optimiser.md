---
name: setup-optimiser
description: Optimises a simulated setup against a stated goal with scans and the fitting plugin's estimators and reports the best settings with their sensitivity. Use for "find the best compressor / angle / pressure / energy for ...".
tools: Read, Grep, Glob, Bash, Edit, Write
---
Follow the `optimise-setup` skill. Run the cheapest faithful model, scan before optimising, verify the optimum in the full
model, and report sensitivity (a sharp optimum is a warning). Say when the optimum sits at the edge of the scanned range.
