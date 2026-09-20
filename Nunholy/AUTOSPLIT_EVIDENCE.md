# Autosplit trigger evidence — Nunholy v1.2.0-rc3-reattach-manual-test

> Historical RC3 evidence for unchanged timing/split behavior, retained with RC4.
> See README.md for the RC4 cold-start change, current verification scope and debug capture instructions.

## Auto-start and manual Start

The game starts the run scene through `LoadScene("Battle")` /
`FadeandLoadScene("Battle")` (`SelectTargetCanvas.cs:26`, `VampireIndexSlot.cs:34–36`,
`SynopsysCanvas.cs:77–92`). `MissionManager.Awake()` then sets
`curChapter = Rating.Knight` and samples `startTime = Time.time`
(`MissionManager.cs:10–17`).

Classic/Any% clears `GameManager.targetVampire` before loading Battle
(`SelectTargetCanvas.cs:20–26`), while Vampire Index assigns a concrete target
(`VampireIndexSlot.cs:30–36`). The ASL therefore requires `targetVampire == null`,
observes a non-Battle scene before auto-arming, and starts only from the guarded
level state `Battle`, chapter `0`, IGT in `[0,10]`. A valid managed sample may
arrive one poll after scene activation; the implementation does not require a
fragile one-poll scene edge.

The base Start checkbox is checked in `start {}`. That action only records an
automatic-start attempt; `onStart {}` commits the run latch. A fresh guarded
Battle observation is consumed independently of the custom/base Start controls,
so enabling Start after the same fresh level remains visible cannot replay it.
This prevents a predicate callback from mutating run state when basic Start is
disabled.

Manual Start is supported without assuming it is an automatic start:

- from a menu/non-Battle scene, it waits without producing an auto-reset
  candidate, then binds the first fresh Classic/Any% Battle sample;
- on a fresh valid Battle sample, it binds immediately;
- when attached mid-run, it fails closed and does not adopt chapter, startTime,
  or split latches.

When the ASL is watched before Nunholy starts, the original candidate's `init {}`
looked for `UnityPlayer.dll`, hashed the three build files, and scanned native
signatures before the generated helper load ran. That ordering is a plausible
diagnostic lead, not a confirmed root cause: `ASLScript.DoInit` may retry after
exceptions, and this offline checkout does not reproduce that user report.

The pinned `asl-help` source at commit
`9a17c5ec7108aba1fe1932358703f4e6adaa6b2c` confirms the lifecycle boundary:
`src/Basic/HelperBase.cs:22-29` appends `vars.Helper.Load()` to `init`,
prepends `if (!vars.Helper.Loaded) return false; vars.Helper.MapPointers();` to
`update`, and prepends `vars.Helper.Dispose()` to both `exit` and `shutdown`.
The ASL leaves `shutdown {}` empty so this generated cleanup runs exactly once.
`HelperBase.Load()` (`:70-119`) starts the bounded asynchronous module/managed
load, and `Unity.Load.cs:18-48` finds `UnityPlayer.dll` and the Mono module
before managed `TryLoad` completes. `HelperBase.Dispose()` (`:194-202`) cancels
that load and clears helper state.

The diagnostic candidate leaves `init {}` non-throwing, lets the generated
helper reach `Loaded`, then performs the exact build hashes and native
signatures from `update {}`. Missing module/files, read failures, exceptions,
and scan races retry indefinitely on a 30-poll cooldown; the attempt counter
saturates at 20 for diagnostics only. A complete readable wrong build digest is
the only permanent rejection until `exit {}` or a new `init {}`. Successful file
verification is cached only for the current process and reset on exit/init.
Content-safe transition logs are emitted only when their reason changes. The
production-action fixture executes explicit generated init/update/exit/shutdown
wrappers, covers more than twenty transient misses, helper readiness, exit
reset, generated single-dispose shutdown, and relaunch reattach without
reloading the ASL; it remains offline evidence, not live Windows validation.
The exact-legacy compiler gate separately compiles every action inside the
1.8.17 dynamic `Execute` shape after its global return rewrite and checks the
four startup settings with helper activation stubbed; it does not claim
LiveSplit `RunStartup` or Windows execution. This is safe lifecycle hardening
and a diagnostic candidate, not proof of the historical root cause.

## Auto-reset and restart identity

Death returns from Battle to Guild (`NarrationCanvas.cs:50–65`); quitting a run
through Pause returns to Title (`PauseCanvas.cs:64–66`). A strict
`old.Scene == Battle -> current.Scene != Battle` edge can be missed during
helper reload, and a restart can reload Battle without exposing a different
active scene. The ASL therefore resets an accepted run whenever the current
scene is non-Battle, or whenever `MissionManager.startTime` differs from the
accepted `CapturedRunStart`. `MissionManager.Awake()` assigns `startTime = Time.time`
for each new Battle run (`MissionManager.cs:10–17`), making it the authoritative
per-run identity.

A same-scene restart can be observed while the old run is paused at exact zero
scale. The ASL computes the changed accepted identity before pause handling,
clears the old `FrozenIGT`/pause latch, and uses the new raw IGT for the fresh
sample. The reset candidate can therefore reset the old timer and let the new
fresh-start candidate arm; it cannot strand the new run at the old frozen time.

Each sample requires a non-null, valid active scene with a non-empty name of at
most 200 characters. The scene address and name are checked again after the
managed/native reads; a scene change, invalidation, or malformed identity
returns no action and cannot replace the last-valid chapter or `isBattle`
sample.

LiveSplit's documented action order runs ordinary `reset {}` only while
Running/Paused and `start {}` only while NotRunning; neither runs in
`TimerPhase.Ended`. After a completed result leaves Battle, `update {}` uses a
`TimerModel` to reset Ended → NotRunning when Auto-reset and base Reset allow it.
Cleanup is centralized in `onReset {}`. A fresh-run latch is preserved when
that Ended reset is initiated by the same update cycle.

On process exit, the game process identity is gone. The exit callback therefore
always resets Running or Paused timers, even when custom or base Reset controls
are disabled; Ended results are preserved. It reads only the timer phase and
resettable ASL state, never game-dependent `current`, `game`, or `modules` data.

## Floors 1–4

`Rating` values are `Knight=0`, `Baron=1`, `Count=2`, `Duke=3`, `Queen=4`
(`Rating.cs:4–17`). After the player commits to the chapter portal,
`Curtain.NextChapterPortal()` fades to black, increments
`MissionManager.Inst.curChapter`, invokes the map transition, and constructs the
next chapter (`Curtain.cs:106–124`). These four unique transitions are used as
floor observations:

```text
0 -> 1
1 -> 2
2 -> 3
3 -> 4
```

Using `isBattle true -> false` for ordinary floors would be unsafe because every
normal combat room also clears `isBattle` in `MissionManager.RoomComplete()`
(`MissionManager.cs:296–305`). Floor observations are consumed in `update` and
advance the expected ordinal even when base Split or the custom floor option is
off. The source level is the per-run last-valid chapter sample, committed only
after a complete native and scene-identity sample; a native-invalid destination
poll or incoherent scene cannot replace it before the next valid poll. Floor and
final observations are consumed/deduped in every timer phase, but they are
queued only while the timer is `Running` and both controls are enabled. A level
observed while `Paused` or in another non-Running phase therefore cannot emit
when the timer resumes. `onSplit` consumes a queued split after LiveSplit
accepts it.

## Fifth boss / final result

For Queen, `NarrationCanvas.QueenIsDead()` enables `ResultCanvas`
(`NarrationCanvas.cs:89–117`). `ResultCanvas.OnEnable()` freezes game time before
evaluating `Time.time - MissionManager.startTime` (`ResultCanvas.cs:11–19`).
Afterwards `YouHuntedCo()` clears `MissionManager.isBattle`
(`NarrationCanvas.cs:107`).

The final observation retains the chapter, ordinal, captured startTime, and
`last-valid isBattle -> !current.isBattle` guards. It additionally requires exact
`Time.timeScale == 0`. ResultCanvas and the `isBattle` edge occur in the same
Unity frame, so a second frozen poll is deliberately not required. A fractional
hitstop or victory slowdown cannot satisfy the exact-zero guard. The available
code evidence supports this as the post-freeze result gate; it does not support
calling the existing final edge a confirmed false positive.

```text
chapter == 4
expected chapter == 5
same captured startTime
last-valid isBattle == true && current.isBattle == false
current.TimeScale == 0 exactly (ResultCanvas freeze)
```

The supplied live evidence recorded a frozen final IGT of `1134.765 s`,
displayed by the game as `18:54`. The manual split in that source log was
`8.975 s` early; the candidate uses the exact frozen result value instead.
