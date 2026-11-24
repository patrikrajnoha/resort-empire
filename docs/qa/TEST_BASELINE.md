# Resort Empire Unity test execution baseline

**Date** 2026-11-24 (Europe/Bratislava)

**Revision** `cb6303dbeae72663398d9070f6f81bd451dac475` on `main`

**Checkout at start** Clean and tracking `origin/main`

## Environment

| Item | Observed value |
| --- | --- |
| OS | Windows 11 Home Single Language, build 26200, x64 |
| Unity Editor | `6000.4.0f1 (8cf496087c8f)`, x64 |
| Test tooling | Unity CLI `1.0.0-beta.17`, Unity Test Framework `1.6.0`, Unity NUnit package `2.0.3` |
| Run mode | Standalone Editor batch processes with graphics enabled, no test filters and no retries |
| Graphics | Direct3D 11 on NVIDIA GeForce RTX 4060 Laptop GPU |
| Source | Git archive of the audited revision extracted into a fresh temporary project |
| Save isolation | PlayMode fixture redirects the save root per test and uses real JSON in a temporary folder. Filesystem EditMode tests use unique temporary paths. |

The only Pipeline-ready Editor reported by `unity status --format json` was open on a different checkout. The audit used independent batch processes to avoid interacting with that Editor or its saves. The matching Unity Editor was already installed. `unity self-update --check --format json` reported a newer CLI release. The installed CLI launched both runs and returned the test verdicts. The source archive included hydrated Git LFS assets. The ocean video, one navigation mask and a Rubik font were checked against the workspace copies.

## Commands executed

The command sequence below uses the actual temporary run directory. `git archive` consumed the clean `HEAD` shown above. The two test commands ran sequentially on the isolated project. Unity's log headers confirm `-batchmode -runTests`, the requested `-testPlatform` and the NUnit output path.

```powershell
git status --short --branch
git rev-parse HEAD
unity self-update --check --format json
unity status --format json
unity test --help

$auditRoot = Join-Path $env:TEMP 'resort-qa-cb6303-fc18c6c3'
$archive = Join-Path $auditRoot 'source.tar'
$project = Join-Path $auditRoot 'project'
git archive --format=tar --output="$archive" HEAD
tar -xf "$archive" -C "$project"

unity test "$project" --mode EditMode --editor-version 6000.4.0f1 --timeout 900 `
  --output (Join-Path $auditRoot 'EditMode.xml') --format json -- -logFile (Join-Path $auditRoot 'EditMode.log')
unity test "$project" --mode PlayMode --editor-version 6000.4.0f1 --timeout 900 `
  --output (Join-Path $auditRoot 'PlayMode.xml') --format json -- -logFile (Join-Path $auditRoot 'PlayMode.log')
```

For a fresh run, create the archive and project directories under a new temporary root before extraction. The initial EditMode call included a cold package and asset import. PlayMode reused the resulting project cache. The package and assets in the audited workspace were untouched.

## Actual results

| Mode | Runner start (CET) | Runner duration | Cases | Passed | Failed | Skipped | Inconclusive | Blocked | CLI exit |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| EditMode | 19:46:25 | 3.767 s | 318 | 318 | 0 | 0 | 0 | 0 | 0 |
| PlayMode | 19:47:29 | 17.931 s | 8 | 8 | 0 | 0 | 0 | 0 | 0 |
| **Total** | | **21.698 s** | **326** | **326** | **0** | **0** | **0** | **0** | |

Durations are NUnit runner timings and exclude Editor startup and asset import. Both logs contain `Test run completed. Exiting with code 0 (Ok). Run completed.` Each XML contains a passing `test-run` record and one passing leaf per inventoried case. `Blocked` is the audit count for inventoried cases unable to execute. Each case had a Unity result.

Evidence is retained locally under `%TEMP%\resort-qa-cb6303-fc18c6c3`. Redacted log copies omit the transient launch credential. The SHA-256 hashes below identify the NUnit result files.

| Mode | NUnit results | SHA-256 | Editor log copy |
| --- | --- | --- | --- |
| EditMode | `EditMode.xml` | `cf62f0af0a0f221d2b785cb32fd387c4e3a4a3dd7a2f5f3f2bfdf4cf1afb0ae6` | `EditMode.redacted.log` |
| PlayMode | `PlayMode.xml` | `8ed11443a30e649e329b70c6383bf85594f27fd0610a1f88d6e1afffeedbb92c` | `PlayMode.redacted.log` |

## Limits

- The baseline ran the project's two assemblies in the Windows Unity Editor. Android Player builds, physical touch, OS lifecycle, real device safe areas, performance and manual visual acceptance were outside this execution. The Android-target extension message in the Editor log did not block either Editor test run.
- The MainMenu fixture uses real scene raycasts and synthetic pointer-click dispatch. Its screen-size matrix emulates layout using a Camera and RenderTextures. PlayMode coverage of the Resort scene centers on session navigation and menu availability. [PlayMode test fixture](../../Assets/_ResortEmpire/Tests/Gameplay/PlayMode/MainMenuNavigationTests.cs)
- The PlayMode fixture suppresses Unity's default failing-log handler and collects Error, Exception and Assert logs itself. It asserts that collection is empty at teardown and excludes one known ocean-video fallback error prefix. This Windows run logged H.264 timestamp warnings. Passing test counts represent the fixture's actual criteria. [Log collector](../../Assets/_ResortEmpire/Tests/Gameplay/PlayMode/MainMenuNavigationTests.cs#L432)
- Package loading emitted warnings for non-selected package test assemblies. XML case counts cover the project's configured tests. No code coverage percentages, mutation score or repeated-run flakiness rate were measured.

[Per-case verdicts](TEST_INVENTORY.csv) · [Quality audit](TEST_AUDIT_REPORT.md)
