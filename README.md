# LiveSplit auto splitters

Read-only LiveSplit auto splitters maintained by [WDN2010](https://github.com/WDN2010).

## Nunholy

- [Setup, scope, supported build and testing](Nunholy/README.md)
- [ASL source](Nunholy/Nunholy_IGT.asl)
- Classic/Any%: automatic start/reset, floor/final splits and game time.
- Current source is based on the reviewed `1.2.0-rc4-cold-start-manual-test` candidate. The tester reports normal manual-load operation, but a full recorded Windows registry-activation/lifecycle matrix is still pending. This repository is not a claim of central-registry acceptance.

Game files, private runtime collections, personal logs and unrelated experimental splitters are not distributed. The dependency fetcher verifies the pinned upstream `asl-help` bytes. Root and Nunholy source are MIT-licensed; helper attribution and its license are retained under `Nunholy/third-party/asl-help/`.

## Reproduce offline checks

From `Nunholy/`, with Python 3 and Mono (`mcs`, `mono`):

```sh
python3 fetch_dependencies.py
python3 tests/test_lifecycle.py
python3 tests/validate.py
python3 tests/compile_and_test_asl.py
python3 tests/compile_asl_actions.py
python3 -O tests/test_native_signature_negative.py
python3 package.py
python3 tests/test_packaging.py
```

`tests/verify_native_signature.py` needs the private supported game build via `NUNHOLY_RUNTIME_DIR`; without it the result is explicitly SKIP, not native runtime verification.
