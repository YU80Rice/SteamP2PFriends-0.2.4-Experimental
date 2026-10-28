# SFP-R2-G1 strict-fixture isolation result

Run: `SFP-R2-G1`, fresh isolated roots from fixed baseline `5c496c0b3c8aa61ba825348bec637071759223c0` plus the current A fixes and strict fixture; RED adds only the semantically old-gate coordinator extraction. Both cases use the same SFP03 test source, callback, fixture data, simulated clock and assertions. The fixture has 12 expired Natural items; it advances 21 × 1/60 s = 0.35 simulated seconds. Callback calls real `ObserveItemAuthority` and the shared flush only; no `seam.Flush`.

## RED: actual old frame gate reproduces the strict-fixture failure

- Command: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File <absolute b-final-fixture/run-sfp03-case.ps1> -CaseName RED`.
- Latest build/target outputs: `logs/RED/attempt-3/`; command, start/end UTC, stdout, stderr and exit are separately saved per production build, whitelist build and target. Earlier compile failures are retained in `attempt-1/` (CS0104 ambiguity) and `attempt-2/` (CS0414 treated as error); `attempt-3` preserves the CS0414 warning as a warning only, then builds.
- Production and whitelist build exit `0`; target exit `1`, `result=False`, expected strict-fixture business Red. Output: `latePrepared=True duplicateBegin=True callback=1 items=11 deliveries=0 held=True bounded=True partial=True fault=none` (`partial=True` means the no-partial-delivery assertion held).
- Physical production and test-copy plugin MVID both `efd4ffb8-9e98-4550-ac77-f8cb3c7ff7d4`; loaded unique plugin is the RED test-copy path. Test EXE MVID `84d9a125-79a8-4adc-b139-840acb827175`; both loaded locations are under RED. Exact identities are in target stdout.

## GREEN: same strict fixture completes within 21 frames

- Command: same runner with `-CaseName GREEN`; outputs in `logs/GREEN/attempt-1/`.
- Production build, whitelist build and target exit `0`; target `result=True`, output `latePrepared=True duplicateBegin=True callback=1 items=0 deliveries=1 held=True bounded=True partial=True fault=none`.
- Physical production and test-copy plugin MVID both `84da1e58-319c-4b5f-91e1-d97dc3297c06`; loaded unique plugin is the GREEN test-copy path. Test EXE MVID `5e78a4ce-b435-4873-8bd8-5f3276b40367`; both loaded locations are under GREEN.

The target was the actual Framework `Test_SFP03_RealFrameGateProgressesBoundedSettlement`, invoked after its existing BattlEye type-resolution prelude. Runner verifies physical metadata with declared Mono.Cecil and checks loaded plugin/test assembly case path and MVID before and after invocation. The complete current Main remains the previously valid 635/635 run: main production/test inputs did not change in this isolated experiment. This proves the strict fixture distinguishes the old one-second consumer gate from current per-frame bounded settlement; it does not establish that player-visible delay is resolved (R04 remains pending).
