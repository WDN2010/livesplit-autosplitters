# Native `Time.time` / `Time.timeScale` evidence — Nunholy

Supported build:

- Unity `2022.3.56f1`, Mono, Windows x64.
- `UnityPlayer.dll` size: `31,052,472` bytes.
- SHA-256: `208fd2c5bd25ff300f2dad8bdf8a3a04402eb8875b16719981ff24b8bc9c1b0d`.
- CodeView GUID+age: `0BD78390CF1E4199A208FFF56689A98C1`.
- Matching PDB: `UnityPlayer_Win64_player_mono_x64.pdb`.

PDB symbol:

```text
Time_Get_Custom_PropTime
RVA 0x110E40
```

Exact disassembly:

```asm
180110E40  48 8B 05 B1 FB BA 01     mov    rax, qword ptr [rip+0x1BAFBB1]
180110E47  F2 0F 10 80 90 00 00 00  movsd  xmm0, qword ptr [rax+0x90]
180110E4F  66 0F 5A C0              cvtpd2ps xmm0, xmm0
180110E53  C3                       ret
```

Derived data path:

```text
UnityPlayer.dll + 0x1CC09F8  -> TimeManager*
TimeManager* + 0x90          -> native double current time
(double)value converted to float -> UnityEngine.Time.time
```

ASL signature:

```text
48 8B 05 ???????? F2 0F 10 80 90 00 00 00 66 0F 5A C0 C3
```

The signature has exactly one match in the supported DLL, at file offset `0x110240` / RVA `0x110E40`. Its rel32 operand resolves to module RVA `0x1CC09F8`.

The matching PDB also exposes `Time_Get_Custom_PropTimeScale` at RVA `0x1110A0`:

```asm
1801110A0  48 8B 05 51 F9 BA 01     mov    rax, qword ptr [rip+0x1BAF951]
1801110A7  F3 0F 10 80 FC 00 00 00  movss  xmm0, dword ptr [rax+0xFC]
1801110AF  C3                       ret
```

Its independent signature is:

```text
48 8B 05 ???????? F3 0F 10 80 FC 00 00 00 C3
```

It has exactly one match at file offset `0x1104A0` / RVA `0x1110A0`, resolves to the same TimeManager slot `0x1CC09F8`, and proves:

```text
TimeManager* + 0xFC -> native float Time.timeScale
```

`VampiricPowerManager.OnEnable()` calls `TimeManager.ToggleTimer(false)`; `ToggleTimer` sets `Time.timeScale` to zero while its pause stack is non-empty. The ASL uses exact zero only as a defensive freeze latch. Fractional scales used by hitstop and victory slowdown remain governed by the raw native `Time.time` value.

Exact game expression from `ResultCanvas`:

```csharp
Time.time - MissionManager.startTime
```

Both operands are `float`. The ASL therefore reads the native `double`, casts it to `float`, then performs `float - float` before converting the result to `TimeSpan`.

Integrity posture:

- no remote calls;
- no code injection;
- no process-memory writes;
- unknown `UnityPlayer.dll` hashes fail closed;
- pointer/signature scanning and value reads only.
