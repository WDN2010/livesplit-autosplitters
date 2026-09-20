# Final split evidence — Nunholy v1.2.0-rc3-reattach-manual-test

> Historical RC3 evidence for unchanged timing/split behavior, retained with RC4.
> See README.md for the RC4 cold-start change, current verification scope and debug capture instructions.

## Observed run

- LiveSplit was manually stopped at `18:45.79` (`1125.790 s`).
- Nearest diagnostic sample: log line 1970, `IGT=1125.699`, `chapter=4`,
  `isBattle=True`.
- The ASL continued reading after LiveSplit stopped:
  - line 1971: `IGT=1127.707`, battle still active;
  - line 1973: `IGT=1131.736`, battle still active;
  - line 1974: `IGT=1133.737`, battle still active;
  - line 1975: `IGT=1134.765`, `isBattle=False`;
  - lines 1976–1994: `IGT=1134.765` remains frozen.
- `1134.765 s` floors to the game's displayed `18:54`.
- The manual split was therefore `8.975 s` early relative to the exact frozen
  result value.

## Decompiled order

1. `Rating.Queen` is enum value 4 (`Rating.cs:4–17`).
2. `NarrationCanvas.QueenIsDead()` calls `UIControl.Inst.OnGameEnd()` and
   activates `resultCanvas` (`NarrationCanvas.cs:112–117`).
3. `ResultCanvas.OnEnable()` calls `TimeManager.ToggleTimer(false)` before
   reading `Time.time - MissionManager.startTime` (`ResultCanvas.cs:11–19`).
4. `NarrationCanvas.YouHuntedCo()` sets `MissionManager.Inst.isBattle = false`
   only after starting `QueenIsDead()` (`NarrationCanvas.cs:89–107`).

Therefore the managed transition is a post-freeze final-result candidate, but
it is not described as a confirmed false positive. The hardened ASL retains
all existing discriminators and adds the exact-zero ResultCanvas guard:

```text
chapter == 4
expected chapter == 5
same captured MissionManager.startTime
last-valid isBattle == true -> current.isBattle == false
current.TimeScale == 0 exactly
```

`QueenIsDead` activates ResultCanvas and then `YouHuntedCo` clears `isBattle`
within the same Unity frame. The ASL must accept the first zero-scale sample;
waiting for a second stable sample would miss the falling edge.

The observation is recorded in `update {}` using the per-run last-valid
`isBattle` sample, which is committed only after all native fields are valid.
Thus a native-invalid final destination poll cannot erase the battle source
before the next valid poll. It is queued only when both the
base Split checkbox and the custom final option are enabled at that sample;
`onSplit {}` consumes the queued split after LiveSplit accepts it. Disabling and later
re-enabling the controls cannot emit this old edge retroactively.

## Executable adapter evidence

`tests/compile_and_test_asl.py` extracts the production `update`, `reset`,
`start`, `onReset`, and `onStart` bodies (and compiles every production action)
into a minimal C# fixture. Its canonical offline run executes the paused
same-scene restart, the one-poll final edge, manual pre-attach start, disabled
settings/no-retro behavior, and Ended-result reset/start ordering. It does not
claim live Windows or LiveSplit-process validation.
