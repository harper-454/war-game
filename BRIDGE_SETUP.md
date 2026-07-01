# GameCI Unity Bridge — EpochWar

## What the bridge is

This repo is a large Node/Cloudflare monorepo, but the **EpochWar** game lives as
loose C# under `Assets/EpochWar/`. Because `Assets/` sits at the repository root,
the **Unity project root = the repo root**.

The "bridge" is the minimal Unity project scaffolding + a GameCI workflow that
lets a **real headless Unity Editor** do two things in GitHub Actions on every
push to `epoch-war-combat-visuals-expansion` (and on PRs into `main`):

1. **Compile** the project (`EpochWar.Core`, `EpochWar.Unity`, and the test
   assemblies) with the real engine + package references.
2. **Run the EditMode tests** and publish the results as a downloadable
   artifact (NUnit results XML) plus inline GitHub annotations.

You iterate by reading the CI run results directly on GitHub — no local Unity
install is required to see whether the project compiles.

### Files that make up the bridge

| File | Purpose |
|------|---------|
| `Packages/manifest.json` | Unity 6 package set the game needs + test framework + `testables`. |
| `ProjectSettings/ProjectVersion.txt` | Pins the exact Editor version. |
| `.github/workflows/unity-ci.yml` | GameCI workflow: compile + EditMode tests. |
| `.gitignore` (Unity section) | Ignores generated Unity artifacts, keeps source. |

## The ONE Unity version source of truth

The Editor version is pinned in **two** places that **must be identical** and
**must equal the Unity Editor version installed on your machine**:

1. `ProjectSettings/ProjectVersion.txt` → `m_EditorVersion:` line
2. `.github/workflows/unity-ci.yml` → the `unityVersion:` value in the
   *Run EditMode tests* step

Currently both are pinned to **`6000.5.2f1`** (Unity 6 LTS). To move to a
different Editor version, change **exactly those two lines** and keep them equal.

## Required GitHub repo secrets

Set these under **Settings → Secrets and variables → Actions**.

### Personal (free / Personal) license

| Secret | Value |
|--------|-------|
| `UNITY_EMAIL` | Your Unity account email. |
| `UNITY_PASSWORD` | Your Unity account password. |
| `UNITY_LICENSE` | The full contents of your `.ulf` activation file. |

To produce the `.ulf` (standard GameCI manual activation flow):

1. Run the GameCI activation once to obtain a `.alf` request file — see the
   GameCI **Activation** docs:
   <https://game.ci/docs/github/activation>
2. Upload the `.alf` to <https://license.unity3d.com/manual>.
3. Download the resulting `.ulf` file.
4. Paste the **entire contents** of the `.ulf` into the `UNITY_LICENSE` secret.

### Plus / Pro license

Instead of `UNITY_LICENSE`, set:

| Secret | Value |
|--------|-------|
| `UNITY_SERIAL` | Your Unity Plus/Pro serial key. |
| `UNITY_EMAIL` | Your Unity account email. |
| `UNITY_PASSWORD` | Your Unity account password. |

The workflow already exposes `UNITY_SERIAL` in the job env, so Plus/Pro works
without further edits.

## Test dependencies (FsCheck + FSharp.Core) are fetched at CI time

The EditMode test asmdef (`EpochWar.Tests.EditMode`) declares precompiled
references:

```
"precompiledReferences": [ "nunit.framework.dll", "FsCheck.dll", "FSharp.Core.dll" ]
```

- `nunit.framework.dll` is provided by `com.unity.test-framework` — nothing to do.
- `FsCheck.dll` and `FSharp.Core.dll` are **NuGet** DLLs, not Unity packages. The
  workflow downloads them from nuget.org and drops them into
  `Assets/EpochWar/Tests/Plugins/` (FsCheck **2.16.6**, FSharp.Core, preferring
  the `netstandard2.0` build). That folder is **git-ignored** and must never be
  committed.

The EditMode test assembly is gated by `defineConstraints: ["UNITY_INCLUDE_TESTS"]`,
so the **game assemblies compile even without** those DLLs — they are only needed
to actually build/run the property tests.

## The iterate loop

```
edit C# under Assets/EpochWar/  →  push to epoch-war-combat-visuals-expansion
      →  GameCI spins up real headless Unity  →  compiles + runs EditMode tests
      →  read the run result / annotations / uploaded results XML on GitHub
      →  fix  →  push again
```

## Local note

Opening the project locally in the Unity Editor will generate `.meta` files and
a `Library/` folder (both handled by `.gitignore`). The game assemblies compile
locally without FsCheck. To **run the property tests locally**, drop
`FsCheck.dll` and `FSharp.Core.dll` into `Assets/EpochWar/Tests/Plugins/`
yourself (the same DLLs CI fetches) so the EditMode test assembly's precompiled
references resolve.
