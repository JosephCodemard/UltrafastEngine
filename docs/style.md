# Style guide

The one rule book for everything UltrafastEngine writes for people: documentation pages, example READMEs, tutorials, CLI and
engine messages (warnings, errors), Python docstrings and code comments. It codifies what the best existing pages already do
([Concepts](concepts.md), [Nonlinear physics](reference/nonlinear.md), [Tutorial 1](tutorials/01-first-run.md)). The word list
and the term list below are tables in a fixed format so that a linter can read them.

**Scope of the spelling and term rules.** They apply to prose. They do not apply to code: JSON keys, element types, option
values, identifiers, file names, URLs and text inside backticks or code blocks keep their declared spelling (`polarization`,
`fiber`, `centerWavelengthM`, `optimizeTod`). Quoted titles of papers and product names keep theirs too.

## Voice

* **Say what the engine does, in the present tense.** Not what it tries to do, will do, or how hard it was to build.
  Good: "The engine books the energy each term removes to its mechanism." Bad: "We have worked hard to make sure energy
  accounting will be handled robustly."
* **One idea per sentence; short sentences.** Split a sentence at "and" when the second half is a new claim.
* **Lead with the result, then the reason.** Good: "The rate is 16 % above Gordon's formula; the cause is the formula's
  first-moment approximation." Bad: "After careful consideration of the various approximations involved, it turns out that..."
* **A claim comes with its evidence:** a number, its reference (formula, paper, other code) and the agreement, or the test name.
  Good: "SPM broadening of a Gaussian agrees with Agrawal eq. 4.1.15 to 1 % (`NonlinearTests`)." Bad: "SPM is accurately modelled."
* **Who is speaking.** Reference pages describe the engine ("the engine", "the run", "the element"). Tutorials and how-to pages
  address the reader as "you". Do not write "we" or "our" (except in a paper replication's commentary, about the replication).
* **Say what is not done.** A limit is stated plainly, with what to use instead. Good: "The beam leaving the element is not
  carried to later elements; for a multipass cell use `cellMode`." Bad: "Some edge cases may not be fully supported yet."
* **No history in user pages.** What changed and when goes in `CHANGELOG.md`. A page may keep one parenthesis when an earlier
  result was wrong and users may have it ("Before 2026-11-24 the GPU path reported 0").
* In running text write "for example" and "that is"; "e.g." and "i.e." only inside parentheses.

### Banned filler

These words add nothing or sell instead of inform. Remove them or replace them with the fact they stand for.

| banned | write instead |
|---|---|
| simply, just (as in "just run"), easily, obviously, clearly, basically, of course | (delete) |
| very, extremely, highly, incredibly | the number |
| powerful, seamless, seamlessly, robust, cutting-edge, state-of-the-art, blazing, world-class | what it does, with a number |
| leverage, utilise, utilize | use |
| in order to | to |
| note that, it should be noted that, please note | (delete; start with the fact) |
| it is important to | (state the consequence instead) |

Good: "The adaptive stepper takes 40 steps where the fixed one takes 400." Bad: "The adaptive stepper is very powerful."

## British spelling

Prose uses British spelling with **-ise** (not Oxford -ize). Counted over today's docs prose (code excluded), the British form
already wins in every pair below except the few US slips the right-hand column lists.

| write | not |
|---|---|
| polarisation, polarised, polariser, polarising | polarization, polarized, polarizer, polarizing |
| fibre, fibres | fiber, fibers |
| optimise, optimised, optimiser, optimisation | optimize, optimized, optimizer, optimization |
| normalise, normalised, normalisation | normalize, normalized, normalization |
| ionisation, ionised | ionization, ionized |
| characterise, minimise, maximise, generalise, parametrise, discretise, digitise, visualise, summarise, recognise, initialise, stabilise | the -ize forms |
| analyse, analysed | analyze, analyzed |
| centre, centred, decentred | center, centered, decentered |
| colour, behaviour, neighbour, vapour, favour | color, behavior, neighbor, vapor, favor |
| modelled, modelling, labelled, travelled, cancelled | modeled, modeling, labeled, traveled, canceled |
| catalogue | catalog |
| grey | gray |
| artefact | artifact |
| metre, metres (the unit) | meter, meters (the unit) |
| licence (noun), license (verb) | license (noun) |
| aluminium | aluminum |
| judgement | judgment |
| towards, among, while | toward, amongst, whilst |

Kept as they are (correct in British English, do not "fix"): **focused, focusing** (one s); **program** (software);
**meter** for an instrument (power meter, energy meter); **sulfur** (IUPAC).

Good: "The polariser is centred on the beam." Bad: "The polarizer is centered on the beam." Good (code keeps its spelling):
"Set `polarization` to `"circular"` to polarise the pulse circularly."

## Term list

One name per concept. Define an abbreviation at its first use on a page ("self-phase modulation (SPM)"), except the
common ones marked *free*.

| term | not | note |
|---|---|---|
| B-integral | B integral, b-integral, B-Integral | `bIntegralRad` in keys |
| χ(2), χ(3) | chi(2), chi2, chi-2 (in prose) | ASCII `chi(2)` in messages and comments |
| carrier-resolved | carrier resolved, carrier-resolving | the real-field model |
| envelope model | envelope-model, SVEA model | |
| self-phase modulation (SPM) | self phase modulation | |
| cross-phase modulation (XPM) | cross phase modulation, XPhM | |
| group-delay dispersion (GDD), third-order dispersion (TOD) | group delay dispersion, 3rd-order dispersion | |
| nonlinear ellipse rotation (NER) | non-linear polarisation rotation | |
| nonlinear | non-linear | |
| FWHM | fwhm, full width half max | *free*; say of what: "intensity FWHM" |
| transform-limited | transform limited, Fourier-limited, bandwidth-limited | |
| multipass cell (MPC) | multi-pass cell | |
| wavefront | wave front, wave-front | |
| time step | timestep, time-step | |
| co-moving frame | comoving frame, moving frame | |
| 1-D, 2-D, 3-D | 1D, 2D, 3D | 1-D model = the beam is a fixed function of z |
| phase matching (noun), phase-matched (adjective) | phasematching | quasi-phase matching (QPM) |
| Kerr lens | Kerr-lens (as a noun) | "Kerr-lens mode locking" as an adjective is fine |
| n2 | n_2, n₂ (in prose) | the nonlinear index in m²/W |
| run spec | run file, run specification, run config | the `run.json` |
| system file | system spec, setup file | the `.ufs` |
| element, instrument, probe | component (for an element), detector (for an instrument) | the schema's names |
| digital twin | digital-twin (as a noun) | |
| d-scan, FROG, SHG, THG, OPA, OPCPA, NOPA, CPA, GPU | dscan, Frog | *free* |
| pyNLO, OpticStudio, ZOS-API, macOS, Apple silicon, Python | PyNLO, Opticstudio, MacOS, python (in prose) | product names as their makers write them |
| the engine | UFE, the software, the tool | "UltrafastEngine" for the product as a whole |

## Units, numbers and symbols

* **SI with the unit in the key** (`lengthM`, `energyJ`, `durationFwhmS`, `gddFs2` is the declared exception in fs²).
  Wavelengths in metres in JSON: `1.03e-6`, not `1030`. See [Concepts: units](concepts.md#units).
* **In prose, a number takes an SI prefix and its unit, with a space**: 40 fs, 200 nJ, 1.03 µm, 5 mm, 3 % (space before %).
  Good: "a 40 fs pulse at 800 nm". Bad: "a 40fs pulse at 0.8um", "a 4e-14 s pulse".
* **Exponent notation** `1e-9` (as in JSON) for tolerances, ratios and unit-less values; superscripts (10⁻⁹) only inside
  displayed equations and tables of results. Good: "the steppers agree to 1e-4". Bad: "agree to 10^-4".
* **Significant figures:** as many as the comparison supports, no more. Quote the agreement as a percentage or a ratio and
  name the reference. Good: "-1.319 THz/m, within 0.3 % of pyNLO". Bad: "-1.31923847 THz/m (excellent agreement)".
* **Symbols in docs prose:** Unicode (ω, χ, λ, µm, fs², 1/e², ×, ≈, ≤). Use `µ` (micro), never `u`, for the prefix. Multiplication
  is `×`, not `x`, between numbers.
* **Symbols in engine messages, docstrings and code comments:** ASCII only (`omega`, `chi(2)`, `um`, `fs^2`, `1/e^2`, `x`), so
  every console shows them. Today no engine message contains a non-ASCII character; keep it that way.
* **Conventions are stated, not assumed:** physics sign convention E(t) = Re{A(t) exp(−iω0t)}, positive GDD puts red first,
  radius is the 1/e² intensity radius, durations are intensity FWHM ([Concepts](concepts.md#the-pulse)).
* **Dashes:** prefer a colon or a full stop. Where a dash is needed, write ` -- ` (as in the titles of tutorials and examples).

## Warnings and errors

A message says **what happened, where (the element id or the key), and the fix** -- in that order, in one or two sentences,
ASCII, no exclamation marks, no "please". If the engine did something instead, say what it did.

* Good: `grid.tail.windowS is shorter than twice the main window; using twice the main window.`
* Good: `element "plate": peak power is 0.4 x the critical power for self-focusing; the 1-D model ignores the Kerr lens. Use a transverse model.`
* Bad: `Invalid configuration!` (what, where, fix: none)
* Bad: `Warning: there might possibly be a problem with the grid, please check.`

An error that names a key gives the accepted values or range: `anisotropicFraction must be between 0 and 1.5 (0 isotropic; 1 purely anisotropic)`.
A deprecated key warns with the new name: `"tau1" is deprecated; use "tau1S".`

## Docstrings and code comments

* **Python builders:** one docstring that says what the engine does with each argument, its unit and its key
  (`` ``gamma`` in 1/(W m) ``), and links the reference page. Keyword-only options, SI units, snake_case with the unit
  (`length_m`). Builders return the generated TypedDicts (autocompletion).
* **C# files:** a header comment with what the file does, its main classes and their key signatures (see
  `Systems/SystemGraph.cs`). Comments say why, not what the next line does.
* Same spelling, term and unit rules as prose; ASCII symbols.

## Page templates

Every new page goes into the `mkdocs.yml` nav and `llms.txt`; `mkdocs build --strict` gives 0 warnings.

### Reference page

```markdown
# <Topic>            (a noun phrase: "Nonlinear physics and solvers", not "How to use nonlinearity")

One paragraph: what it is, which element / option / instrument it belongs to, when you need it.

## Keys
The keys that matter, one line each, with the unit; the full list with defaults is generated:
[options](options.md#elements). Never copy the defaults table by hand.

## Model
The equations the engine solves, in plain text or Unicode, with every symbol and its unit defined and the
conventions stated (sign, normalisation, radius definition). Cite the source (author, journal, volume, page, year).

## Validation
What each test checks against which reference, with the agreement: "SPM broadening against Agrawal eq. 4.1.15 to 1 %
(`NonlinearTests`)". Test names in backticks, as in the validation matrix.

## Limits
What the model leaves out, what the engine warns about, and what to use instead (with links).
```

### Example (README of `docs/examples/<Group>/<name>/`)

The template is `docs/examples/_template.md` (in the repository; not a site page); `build/example_lint.py` checks its structure and
`example.json`. Sections in order: title (= `title` in `example.json`), one summary paragraph with the main number
(= `summary`, word for word), **The story.** (the physics and the reference it is checked against), `hero.png` (and
optionally `animation.gif`), **Layout** (the parameters that matter, in SI with the key names; run time), the commands,
the script's output as printed (it must match a fresh run: `/example-check`), **What it shows.** (each number against its
reference), **Limits.** (what is left out; links). Model: [Raman self-frequency shift](examples/Fibres/raman-soliton-shift/README.md).

**Run it** (examples with `"python": true` in `example.json`; all examples once the D2 work packages are done) sits between **Layout**
and **What it shows.**: two content tabs that run the same simulation and print the same key numbers. Model:
[SPM in a fused-silica plate](examples/GettingStarted/spm-plate/README.md).

````markdown
=== "CLI"

    ```bash
    ultrafast run plate.ufs run.json -o results     # about 1 s
    ```

    ```
    energy 3.984E-06 J (T = 99.60 %), FWHM 251.5 fs (TL 183.6 fs), peak 1.491E+07 W, B = 0.667 rad
    ```

=== "Python"

    <!-- snippet -->
    ```python
    import ultrafast as uf
    from ultrafast import build as b

    system = uf.System.load("plate.ufs")           # the example's own system file
    spec = b.run_spec(b.pulse(1.03e-6, 250e-15, energy=4e-6), ...)   # run.json with the builders
    res = uf.run(system, spec)
    print(f"  energy {res.summary['output']['energyJ']:.4G} J ...")  # the CLI's line, in its format
    ```

    ```
    the same line, as printed
    ```
````

* **CLI tab:** `ultrafast run <system>.ufs <run>.json -o results` when the example has (or can be given) a system file and a run spec;
  an example that is a Python analysis runs its script there (`python script.py`). Its output block quotes the lines of the CLI with the
  key numbers.
* **Python tab:** a complete program that runs in the example folder. The builders of `ultrafast.build` for everything written in
  Python (they return the typed dicts of `ultrafast.types`, so an editor completes every argument and key; no raw dicts where a builder
  exists), `uf.System.load` for the example's own `.ufs`. It prints the same key numbers in the CLI's format (`.4G`; B-integral `.3G`;
  transmission `.2f} %`) and its output block quotes them.
* **The snippet marker:** the line `<!-- snippet -->` directly before the ```` ```python ```` fence, at the same indentation (four spaces
  inside a tab); `<!-- snippet slow -->` for a snippet that takes more than about a minute at quick fidelity. The comment does not show on
  the site. `python/tests/test_readme_snippets.py` runs every marked block at quick fidelity with `ULTRAFAST_STRICT_OPTIONS=1` (slow ones
  with `ULTRAFAST_SLOW=1`); `build/example_check.py` runs them at full fidelity and checks the quoted output; `build/example_lint.py`
  requires the tabs and a snippet when `example.json` has `"python": true`. A ```` ```python ```` block without the marker is not run (a
  fragment).

### Tutorial (`docs/tutorials/NN-<name>.md`)

```markdown
# Tutorial N -- <What you will be able to do>

**Goal:** one sentence, in the reader's terms.

What you need before starting (the previous tutorial, a file in docs/tutorials/files/).

## 1. <First step>       (numbered steps, each a heading; one new idea per step)
The file or command in full, then a bullet per new key: what it is, its unit, and the link to its reference page.

## 2. <Run it>
The command and the output as printed (the numbers must match a fresh run: /example-check).

## 3. <Read the results>
Where each number comes from and how to check it is right.

## N. Try
Two or three changes to make, each with the effect to expect ("energyJ 400e-9: the B-integral doubles").

Next: [Tutorial N+1 -- <title>](NN-<name>.md).
```

## Checklist before a commit

1. Spelling and terms follow the two tables above (prose only; code keeps its declared names).
2. Every number has a unit and, if it is a result, its reference and agreement.
3. No banned filler; no "we"; present tense.
4. Messages: what, where, fix; ASCII.
5. Page in `mkdocs.yml` and `llms.txt`; `python3 -m mkdocs build --strict` with 0 warnings.
