# PEPPERED RC14 verification summary

This is a publication-safe summary of the delivered RC14 candidate. Raw private game evidence, diagnostic captures, conversation records, absolute workstation paths and original game payloads are not part of this source distribution.

## Frozen runtime identity

- Delivered source baseline: `3ac6492bd08e65679a1ff9fcfb5da2307b54724d` in the private preparation history. This is provenance, not a claim that the commit is hosted by the public repository.
- ASL SHA-256: `4b1b99eab3bedcc005a863b845df4296e3eb16360c9f9d816eba4c5562acf33e`
- DLL SHA-256: `fe378022fb5c5a36700ee6fb76b68f1a37ce1cef1aabd41fa993b04ccbe6b270`
- Helper SHA-256: `c0ece0762d65cb831082a465af2c2cd64cc745d5ede2b7eb339fb84030cb1fb5`
- Supported-build manifest SHA-256: `650b38cbecb50db26e6f9126223232be533ba7dc39e0f88eb06fe82eb5668bcf`

Publication preserves these runtime bytes. Public documentation/build packaging has separate fresh package receipts with the same executable identities.

## What was tested

1. Six offline commands: deterministic build, pure logic538, reader181, bridge123, parent regression2907, emitted-ASL322 plus320 control assertions.
2. Exact LiveSplit1.8.37 parser/compiler and137-setting readback. This gate uses a settings-only startup scope, not full game/helper activation.
3. Default-off optional Ending8 friendship-loss setting, all136 old setting definitions unchanged, and an enabled-optional-only137-setting serializer round-trip.
4. Exact final emitted12action bodies compared with tested rendering, source/build receipt integrity, deterministic packaging and adversarial package regression cases.
5. Fourteen existing layouts replayed on the exact DLL with their files unchanged: six With5/6/7 layouts, six Against5/6/7 layouts, and two ordinary surrender8 layouts. This is routing/settings compatibility, not a new live traversal of every preset.
6. The unchanged read-only reader has earlier original-Mono owned-object/layout qualification. Those private probes are not shipped and are not equivalent to executing game constructors, Unity callbacks or a running-game reader through the full helper.

Fresh publication-copy gate results must accompany the prepared artifacts. Existing history cannot stand in for a newly failed check.

## Live acceptance and distribution scope

On September 26, 2026 the maintainer explicitly reported successful testing on all endings. This supersedes the earlier pending Ending6 With/Against checks. It is user-reported gameplay acceptance, not a new instrumented frame-by-frame audit. The intermittent auto-start observation remains a documented limitation with a restart/main-menu workaround; it was not claimed fixed in code. The user requested documentation rather than further log investigation. Final submitted-run timing is adjudicated by speedrun.com moderators under the published rules.

A successful persuasion with zero stars does not establish a good game ending. The splitter intentionally marks physical timestamps and does not validate80 stars, rating, no-death history, friendship or category eligibility.

The maintainer authorized publication on September 26, 2026 after the gameplay confirmations and troubleshooting documentation. Clean Windows registry-style activation, including actual Script startup/settings through its downloader-created component, remains a separate distribution check. Publishing this source or opening the registry submission is not a claim that this registry-only check or upstream acceptance has completed.

## Reproduction scope

All public source and tests are included. The helper fetcher checks an immutable upstream digest. Exact supported game identities are hashes only. Optional private original-build checks may be run via the documented environment variable; absence is an explicit SKIP, not a fabricated pass. Package validation must continue rejecting unlisted files, symlink substitutions, forged receipts and binary/source mismatches.
