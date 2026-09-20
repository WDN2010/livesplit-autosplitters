# Nunholy AutoSplitter + exact IGT v1.2.0-rc4-cold-start-manual-test

`Nunholy_IGT.asl` is a read-only LiveSplit manual-test candidate for Classic/Any%.
WDN2010 has manually exercised the current candidate and reports normal operation.
The reproducible checks in this repository are offline: they do not establish a
complete Windows first-launch/relaunch or clean registry-activation acceptance
matrix. An earlier one-off attach omission was reported but not explained by a
captured runtime trace.

## RC4: first-run cold-start recovery

RC3 could miss every menu sample while `asl-help` or the native clock reader was
still loading. Its first valid `Battle` sample was then fresh but unarmed, so the
timer did not start; returning to the menu armed the next attempt. This omission
was reproduced offline in the actual emitted ASL action bodies.

RC4 adds two narrowly scoped ways to recover that missing first-run baseline:

- A guarded, init-only `Process.StartTime` check proves that the game process
  started after this ASL was loaded. The first coherent settled Classic/Any%
  chapter-0 Battle sample may then start within the existing 0–10-second IGT
  window. Attaching the ASL to an already-running process does not grant this proof.
- If the first coherent Battle sample instead contains zero/unsettled managed
  `startTime`, the splitter waits for a positive settled identity and fresh IGT.
  Zero alone cannot start a cold run. An already-observed menu retains its previous
  behavior, including legitimate warm starts whose `startTime` is zero.

The opportunity is process-scoped and one-shot. Invalid polls preserve it;
manual Start/Reset, process exit, a higher chapter, another mode, or an unsuitable
phase cancel it. Reset does not manufacture another first observation. A settled
but late sample or a fresh observation with Start disabled is consumed without
later replay. There are no sleeps or enlarged freshness windows. IGT arithmetic,
floor/final splits and freeze behavior are unchanged.

### First Windows check for RC4

1. Load the updated ASL in LiveSplit **before launching Nunholy**. Keep Game Time
   selected and the existing custom/base Start controls enabled.
2. Launch the game and start the first Classic/Any% run; do not make a sacrificial
   menu-return attempt first. Verify one auto-start and advancing Game Time.
3. Return to the menu and start a second run. Then close/relaunch the game while
   keeping LiveSplit and the same ASL loaded, and repeat the first-run check.
4. If the first auto-start is absent, capture the ASL debug output with
   [Microsoft Sysinternals DebugView](https://learn.microsoft.com/en-us/sysinternals/downloads/debugview).
   Start capture before LiveSplit/game launch, enable Capture Win32 (and Global
   Win32 if needed), then compare the first attempt with a successful menu retry.
   Use Ctrl+L with Include `*Nunholy*;*asl-help*;*ASL*` and an empty Exclude field;
   save the captured output. Standard ASL `print()` does not automatically create
   a `LiveSplit.log` file. RC3 remains the locally preserved rollback candidate.

Offline checks include 132 emitted-ASL assertions, lifecycle regressions,
all 13 action bodies, native-negative guards and deterministic packaging.
The exact ASL also passed the official LiveSplit 1.8.37 parser/compiler and an
actual settings-only `RunStartup` check of all four controls. Full helper startup,
private native signatures, process-clock behavior and the real first Windows run
remain unverified; this is a manual-test RC, not a live acceptance claim.

## Requirements and status

- LiveSplit with Scriptable Auto Splitter **>= 1.8.17**. The candidate uses the
  `onStart` and `onSplit` callbacks.
- Unity `2022.3.56f1`, Mono, Windows x64, and the exact supported build tuple
  below.
- The private game binaries are not distributed. The native verifier prints an
  explicit `SKIP` when `NUNHOLY_RUNTIME_DIR` is not available; that is not live
  verification. The dependency helper is public and fetched separately.

The package is a manual-test candidate, not a claim of central-registry
acceptance or live Windows acceptance. The generated ZIP is deterministic, but
it is still a release-candidate manual-test artifact and is not publishable as
live-validated registry evidence until the Windows gate passes.

`Collect-NunholyRuntime.ps1` produces private evidence only and is never part of
the public package. Its report redacts the full game path by default; use
`-IncludeGamePath` only for an explicitly private handoff.

## What it provides

- exact game IGT: `(float)UnityEngine.Time.time - (float)MissionManager.startTime`;
- exact native `Time.timeScale` validation, including a zero-scale Moon/pause
  freeze latch while preserving fractional hitstop/slow-motion;
- automatic start on a fresh guarded Classic/Any% `Battle` run; a fresh
  observation is consumed even when custom or base Start is disabled, so
  re-enabling Start cannot replay that same level;
- automatic reset after death, leaving an active run, or same-scene restart;
- four floor splits when `curChapter` advances `0→1→2→3→4`;
- an optional final split at the fifth-boss result gate after the game's timer
  has frozen;
- no process-memory writes, injection, hooks, remote calls, or game plugin.

## Supported build tuple

- Unity `2022.3.56f1`, Mono, Windows x64
- `UnityPlayer.dll`: SHA-256
  `208fd2c5bd25ff300f2dad8bdf8a3a04402eb8875b16719981ff24b8bc9c1b0d`
- `Nunholy_Data/Managed/Assembly-CSharp.dll`: SHA-256
  `2fdb400a70f217b22e7fef27f9848be8b90e622479040313aa1ed8afc88598eb`
- `Nunholy_Data/Managed/UnityEngine.CoreModule.dll`: SHA-256
  `39a749674ededf51a44fcb7d08b35ffca932c11f73493df3b97a12f8f7af9bca`

A complete, readable file with a wrong digest is the only permanent native
attach rejection. Missing files, read/I/O failures, module publication races,
and unavailable signatures remain pending and retry on a bounded cooldown.
The bundled `asl-help` bytes are checked with SHA-256
`c0ece0762d65cb831082a465af2c2cd64cc745d5ede2b7eb339fb84030cb1fb5`, and the
exact byte array that was hashed is passed to `Assembly.Load`.

## Installation and manual test

1. From `Nunholy/`, fetch the pinned helper and build the package:
   `python3 fetch_dependencies.py && python3 package.py`.
2. Use the contents of `dist/Nunholy-AutoSplitter-1.2.0-rc4-cold-start-manual-test.zip`, or copy
   `Components\\asl-help` to LiveSplit's `Components` directory.
3. In LiveSplit, add **Control → Scriptable Auto Splitter** and select
   `Nunholy_IGT.asl`.
4. Configure five LiveSplit segments: four floors and the final boss.
5. Display and compare against **Game Time**, not Real Time.
6. Start from the title/menu for the first automatic-start test. Verify a
   fresh Classic/Any% Battle run, all four floor transitions, Moon/pause, a
   death/reset, and the final result screen.
7. For the first-launch check, leave the LiveSplit layout and this ASL loaded,
   launch Nunholy, and confirm it attaches on that first launch.
8. Without reloading the ASL, close Nunholy completely, wait for its process to
   disappear, and relaunch it. Use DebugView (capture procedure above) to save
   LiveSplit's debug messages containing `Nunholy`, `asl-help`, or `ASL` around
   the `process exit/reset`, init, helper/native, and native-attached transitions.
   No automatically created `LiveSplit.log` file is expected. Repeat from both the
   title/menu and an early startup window; the ASL must reattach without an ASL
   reload. Confirm Game Time resumes and the same build guards still apply.

The historical root cause is not proven by the offline fixture or this patch:
the earlier init-order failure is only a candidate because `ASLScript.DoInit`
may retry after exceptions. Treat this as safe lifecycle hardening and a
bounded diagnostic candidate until the exact live close/relaunch test above
produces the transition log evidence.

The basic LiveSplit Start/Split/Reset checkboxes are honored. `start {}` and
`split {}` only publish candidates; `onStart` and `onSplit` commit/consume the
corresponding latches. The custom options are:

- **Auto-start on a fresh Classic/Any% run**;
- **Auto-reset after death or leaving the run**;
- **Split floors**;
- **Split final**.

### Manual Start behavior

- Manual Start in a menu or other non-Battle scene waits without auto-reset and
  binds to the first fresh Classic/Any% Battle sample.
- Manual Start on a fresh valid Battle sample binds immediately.
- A manual Start attached mid-run is explicitly unsupported and fails closed for
  auto-splits; it does not adopt the current chapter or corrupt reset latches.

### Reset, Undo, and process exit

Undo changes LiveSplit's segment state but cannot restore the game's observed
chapter/final latches. After Undo, use LiveSplit Reset and begin a new run (or
reload the ASL) before relying on automatic splits.

On process exit, the candidate always resets Running or Paused timers, even when
custom **Auto-reset** or the base Reset checkbox is disabled. This forced cleanup
prevents an unfinished timer from surviving after the game process identity is
gone. It preserves an Ended result. No game-dependent `current`, `game`, or
`modules` state is read from the exit callback.

## Moon, pause, and exact IGT

The supported UnityPlayer has two independently verified native wrappers that
resolve to the same TimeManager pointer:

```text
Time.time      = float(*(double *)(TimeManager + 0x90))
Time.timeScale =       *(float  *)(TimeManager + 0xFC)
```

`VampiricPowerManager.OnEnable()` calls `TimeManager.ToggleTimer(false)`, which
sets `Time.timeScale` to exactly zero for Moon and ordinary Vampiric Power
selection. While an accepted run is at exact zero scale, the ASL holds the
first paused IGT sample. Values `0.01`, `0.02`, and `0.1` are not frozen,
preserving scaled hitstop, perfect-evade, and victory-slowdown semantics.

## Split semantics

Floor splits are observed in `update` when `Curtain.NextChapterPortal()` has
committed the next `MissionManager.curChapter` value. The expected ordinal is
advanced even if the base or custom split control is disabled, so turning a
control on later cannot emit a retroactive split. The comparison uses a
per-run last-valid chapter sample, committed only after the complete native and
active-scene identity sample is valid; empty, null, overlong, invalid, or
changed-during-read scene identities cannot erase or replace the source level.
Floor/final observations are consumed and deduplicated in every timer phase, but
are queued only while the timer is `Running` and both controls are enabled.
Thus a floor/final observation seen while `Paused` or in another non-Running
phase cannot emit after resume. `onSplit` consumes a queued split after
LiveSplit accepts it.

Every split observation is additionally gated by the captured
`MissionManager.startTime`, the same Battle run, and the next expected chapter.
A retry, stale timer, duplicate edge, or out-of-order chapter transition fails
closed.

The final candidate keeps the chapter/order/same-run guard and requires all of:

```text
chapter == 4
expected chapter == 5
same captured startTime
last-valid isBattle == true -> current.isBattle == false
current.TimeScale == 0 exactly
result-freeze observation
```

For Queen, `QueenIsDead` enables `ResultCanvas`; `ResultCanvas.OnEnable()`
freezes `Time.time` before `YouHuntedCo` clears `isBattle`. Both changes happen
in the same Unity frame, so the exact-zero guard is accepted on the first poll;
requiring a second stable poll would lose the one-poll `isBattle` edge.

## Offline checks

From `Nunholy/`:

```text
python3 fetch_dependencies.py
python3 tests/test_lifecycle.py
python3 tests/validate.py
python3 tests/compile_asl_actions.py
python3 tests/compile_and_test_asl.py
python3 tests/verify_native_signature.py
python3 -O tests/test_native_signature_negative.py
python3 package.py
python3 tests/test_packaging.py
```

`compile_asl_actions.py` is the exact-legacy-compiler regression gate. For each
real production action it applies Scriptable Auto Splitter 1.8.17's global
`return;` → `return null;` rewrite, then compiles the result inside the
dynamic-return `Execute(...)` wrapper used by `ASLMethod`. This is deliberately
not a typed raw action-method check. Its compiled startup fixture verifies that
all four custom settings are constructed and reachable. The fixture skips only
the external `Assembly.Load(...).CreateInstance("Unity")` activation because
that requires the real LiveSplit.Core runtime; it therefore does not claim
LiveSplit `RunStartup`, Windows, or game-process execution. The gate is also
run at the start of `compile_and_test_asl.py`.

`compile_and_test_asl.py` additionally extracts the actual production action
bodies and runs callback-order fixtures for early init before module/helper
readiness, process close/relaunch reattach without ASL reload, generated
shutdown cleanup, the paused same-scene restart, first-poll final edge, manual
pre-attach start, setting/no-retro behavior, and Ended reset/start. It is an
offline adapter check, not the Windows LiveSplit process.

For private native verification only, provide the runtime root to the native
verifier (this is the only command that uses `NUNHOLY_RUNTIME_DIR`):

```text
NUNHOLY_RUNTIME_DIR=/path/to/private/Nunholy python3 tests/verify_native_signature.py
```

Without private game binaries, the native verifier prints an explicit `SKIP`
and exits successfully. `package.py` never falls back to a runtime directory:
fetch the exact helper first, then package. Do not copy game binaries, private
logs, or saves into this repository.

The optimized negative gate (`python3 -O tests/test_native_signature_negative.py`)
proves that wrong native/managed hashes and signature hits still fail nonzero when
assertions are disabled. The verifier itself uses explicit checks for every
acceptance/security condition.

## Evidence and integrity

See `AUTOSPLIT_EVIDENCE.md`, `FINISH_TRIGGER_EVIDENCE.md`, and
`NATIVE_TIME_EVIDENCE.md`. The deterministic package receipt is tracked at
`evidence/package-verification.json`; it records the relative archive path,
member hashes/counts, `source_binaries_excluded: true`, and
`live_windows_runtime: false`. Packaging opens every source through
`ROOT`-relative, no-follow file descriptors and fails closed when the platform
cannot provide that boundary; direct and parent-directory symlinks are rejected.
The bundled helper license is `third-party\\asl-help\\LICENSE`; the package
source is MIT-licensed in `LICENSE`. Corresponding `asl-help` source for the
exact bundled revision is
<https://github.com/ero-qt/asl-help/tree/9a17c5ec7108aba1fe1932358703f4e6adaa6b2c>;
the exact fetched file URL is
<https://github.com/ero-qt/asl-help/raw/9a17c5ec7108aba1fe1932358703f4e6adaa6b2c/lib/asl-help>.
