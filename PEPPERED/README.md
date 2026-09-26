# PEPPERED LiveSplit auto splitter

Read-only auto start, world/room splits, reset and ending timestamps for the supported original Windows Mono build of **PEPPERED**. Project-owned source and `Peppered.AutoSplitter.dll` are MIT-licensed. The third-party `asl-help` dependency is pinned and separately attributed below.

## Release status

This repository provides `0.4.0-rc14-optional-friend-loss-manual-test`, the RC14 build verified by the maintainer. Runtime source, emitted ASL, custom DLL and supported-build identity retain the exact tested bytes. The existing RC14 version string is retained rather than changing the executable solely for publication.

The maintainer confirmed on September 26, 2026 that the auto splitter has been tested on all endings, closing the previously outstanding Ending6 With/Against checks. This is user-confirmed gameplay testing, not a guarantee of frame-perfect timing. The intermittent auto-start issue is documented with a restart/main-menu workaround below; no unverified lifecycle patch has been applied.

Source and downloads are hosted in [WDN2010/livesplit-autosplitters](https://github.com/WDN2010/livesplit-autosplitters). Central LiveSplit registry availability depends on the separate registry submission. Clean Windows registry-style download/activation and helper startup remain distinct from the verified manual installation; registry acceptance is not claimed here.

## Supported build and timing

- Original Windows Mono build; exact supported file identities are in `supported-build.json`.
- The process and27-file build identity are checked before using the reader. Other builds are not implicitly supported.
- Read-only process access: no code injection, game-memory writes, remote game method calls or save editing.
- Use **Real Time**. No custom game-time calculation, load removal or death-animation compensation is added.
- Timing marks the selected physical event. It does not enforce star counts, rating, no-death/no-loss conditions or category validity.

Do not redistribute the original game binaries referenced by the hash manifest. They are optional private inputs for specific verification fixtures, not project dependencies to upload.

## Manual installation

Publication follows the same repository/direct-download model as Nunholy. Download these three files:

- [PEPPERED.asl](https://raw.githubusercontent.com/WDN2010/livesplit-autosplitters/refs/heads/main/PEPPERED/PEPPERED.asl)
- [Peppered.AutoSplitter.dll](https://raw.githubusercontent.com/WDN2010/livesplit-autosplitters/refs/heads/main/PEPPERED/Components/Peppered.AutoSplitter.dll)
- [asl-help](https://raw.githubusercontent.com/ero-qt/asl-help/9a17c5ec7108aba1fe1932358703f4e6adaa6b2c/lib/asl-help)

Place them at the paths shown below. The helper is the exact same pinned dependency as Nunholy, not a separate PEPPERED version. Project source, build scripts, tests and licenses are in this directory. No separate GitHub Release or tag is required for this distribution model.

Fully close LiveSplit before replacing the ASL and custom DLL together. Install relative to the directory containing `LiveSplit.exe`:

```text
PEPPERED.asl
Components/Peppered.AutoSplitter.dll
Components/asl-help
```

For a manual Scriptable Auto Splitter component, select `PEPPERED.asl`. An existing `Peppered/PEPPERED.asl` path also works if you keep an identical ASL copy there. Dependency paths are relative to the LiveSplit working directory, not the folder containing the selected script.

Keep a backup of the previous matching ASL/DLL pair. Never mix a new ASL with an old DLL: the script checks the exact bytes and fails closed on a mismatch. Preserve personal LSS/LSL/PB/history files; none are required to be overwritten by a runtime update.

The registry-style layout is different: the downloader puts all three payloads in `LiveSplit/Components`, including the ASL. The script already opens its dependencies at those `Components/...` paths. A clean registry-style activation check is still required before treating that installation path as tested.

## Settings

There are137 custom setting IDs. Timer actions and category/scene selections remain opt-in; bounded diagnostics may be enabled by default. Enable the timer master, the appropriate auto-start/reset and split controls, and the selected route items under their parents. Standard component Start/Split/Reset controls must also permit the corresponding action.

The selected events must match the number and order of segments in your LSS. Revisited scenes and world entries use per-run one-shot behavior; enabling every scene is not a substitute for a route-specific preset. Settings are stored in the LSL component, not in the LSS alone. Do not enable multiple unrelated finish options for an ordinary category run.

### Ending boundaries

- Ending1: first qualified execution black frame.
- Ending2: first qualified abrupt family-ending black frame.
- Ending3: first qualified bar dialogue.
- Ending4: first qualified permanent-death black frame.
- Endings5/6/7 **With Karmina**: start watching TV with Karmina after the playable aftermath. Neither battle victory nor ordinary defeat is the finish for these presets.
- Endings5/6/7 **Against Karmina**: confirmation of the LAST CONTINUE / GIVE UP choice in `B_End`; opening the menu is not the finish.
- Ending8, ordinary route: LAST GIVE HER THE STARS confirmation, not the first offer.
- Ending8, optional friendship route: fresh defeat in the final Merdeka fight. Separate default-off `ending.8.friend_loss` checkbox, labeled **NOT Ending7**. It detects defeat only; the pre-existing friendship flag is the runner's route prerequisite, not an enforced check.
- Ending9: first qualified Theodore victory/HP-zero event.
- Ending10: qualified Theodore golf-death cut.
- Ending11: first qualified final-dialogue dash in the Secret GEM route.

### Category caveats

For an ordinary good Against ending, successful persuasion with zero stars still reaches the zero-star bad branch: preserve at least one star through the final judgement. The Ending6 checkbox does not certify that all80 stars or the no-loss category conditions were achieved. A full80-star pickup itinerary on the Against branch is not established by this timing implementation; do not assume the With-only +40 Merdeka victory reward applies there.

For the optional friendship-loss Ending8 route, do not enable the new checkbox during ordinary Ending7 runs: it intentionally requests an earlier battle-loss split. The original surrender8 and all old136 setting definitions remain unchanged.

## Troubleshooting

1. **Splits do not start automatically.** Restart the game first. If that does not help, restart LiveSplit as well. LiveSplit must be running with the auto splitter loaded while the game is still at the main menu, before starting a new game. If restarting LiveSplit with the game open, keep the game at the main menu until the auto splitter has loaded.
2. **The timer display slows down during Life Star loss scenes.** This visual slowdown or stutter is expected behavior in these scenes; LiveSplit still measures the correct elapsed Real Time. It does not require time compensation or a different timing method.
3. **Timing may not be frame-perfect.** The auto splitter can differ from the exact frame boundary. The final time of a submitted run is determined by the moderator reviewing it on [speedrun.com](https://www.speedrun.com/), according to the published rules for that game and category.
4. **Problems or questions?** Contact **@WDN2010 on Discord**.

## Diagnostics

Use Sysinternals DebugView while LiveSplit runs and filter for `PEPPERED`. The emitter is LiveSplit, not the game process. Standard ASL `print()` does not guarantee an automatically created `LiveSplit.log` file.

A useful report includes the boot banner `0.4.0-rc14 optional-friend-loss`, attach/build result, selected endpoint, observed split time and a short video around the event. Remove private paths or unrelated captured application messages before sharing diagnostics publicly. Do not upload a save or game binary as a default troubleshooting step.

To isolate an endpoint, use a separate one-segment Finish profile. Enable only that ending and required timer controls; disable scene/world splits, other endings and Auto Start/Reset. Reset and manually Start before the qualifying event. Do not expect loading a save already at a terminal state to replay the event.

## Reproduce the build and checks

Requirements: Python3.10+ and Mono `mcs`/`mono`. The delivered binary was built with mcs6.14.1.0 and Mono6.14.1. A different compiler output changes the DLL digest and requires rebuilding the matching ASL and requalification rather than mixing binaries.

From `PEPPERED/`:

```sh
python3 fetch_dependencies.py
python3 tests/run_all.py
python3 tests/official_engine_probe.py --runtime-dir /path/to/LiveSplit-1.8.37 --archive /path/to/LiveSplit_1.8.37.zip
python3 package.py
python3 tests/package_regression.py
```

The official-engine inputs are not distributed here. The probe verifies their pinned identities before use. The default offline matrix does not need the game installed. Set `PEPPERED_GAME_ROOT` only for additional original-game hash fixtures; a missing private input is reported as SKIP rather than live acceptance. Canonical package verification normalizes that optional environment, so run the default receipt-producing gate without it when packaging.

`build.py` checks source/dependency/build identities, compiles the DLL and emits an ASL bound to those exact bytes. `package.py` rebuilds from the explicit allowlist, checks receipts, rejects symlinks/private payloads and creates deterministic archives. It does not publish them. The install ZIP and source/test ZIP have different purposes; select the ASL from a correctly extracted LiveSplit installation, not a version-wrapped source tree with unmatched dependencies.

## Dependency provenance

`asl-help` is fetched from its pinned upstream revision, not a floating branch:

```text
https://github.com/ero-qt/asl-help/raw/9a17c5ec7108aba1fe1932358703f4e6adaa6b2c/lib/asl-help
SHA-256 c0ece0762d65cb831082a465af2c2cd64cc745d5ede2b7eb339fb84030cb1fb5
```

Its license is retained in `third-party/asl-help/LICENSE`; upstream source is https://github.com/ero-qt/asl-help/tree/9a17c5ec7108aba1fe1932358703f4e6adaa6b2c . The helper binary and its complete corresponding source are available from the same pinned upstream repository. The local helper binary is ignored by Git. The project-owned custom DLL is distributed with its complete matching `src/` source and MIT license. A registry record must retain the main-ASL-first ordering and exact downloaded basenames.

## Verification and limitations

The RC14 runtime passed logic538, reader181, metadata bridge123, parent regression2907 and emitted-ASL322 assertions, with320 in the pre-refresh control. The137-setting official parser/settings gate and a separate enabled-optional-setting round-trip passed. All12 emitted action bodies matched the tested bodies. Fourteen legacy layouts were replayed against the delivered DLL with the new optional setting off, without changing their files.

The original-Mono owned-object evidence belongs to the unchanged reader and is not a new live-game run. Parser/settings and package passes do not prove full helper startup through the registry downloader. `build-receipt.json` records machine-generated build provenance; its runtime flag must not be substituted for the separately documented user acceptance status.

See `evidence/PARENT-VERIFIED.md` for the public evidence summary. Original game payloads, private native probes, raw conversation notes and personal profile data are intentionally excluded.
