# 票 04 冻结后双次 Rebuild 身份核对（可重放证据，Run 1 / Run 2 原始输出）

目的：让「两次 Release Rebuild 指纹一致」可被独立复核，而不是只能采信报告里的数字
（`docs/agents/real-machine-test-loop.md` 的可复现身份要求；`spec.md`：「BuildArtifact：程序集版本与身份为 0.2.4.9；独立核验 SHA-256 / MVID / Case-ID」）。

## 重放方式

```
bash .scratch/collision-migration-slice/evidence/ticket04-rebuild-identity-replay.sh
```

脚本连续执行两次 `-t:Rebuild -p:Configuration=Release`（主 DLL 与测试 exe），逐次采集退出码 / SHA-256 / MVID / 字节数，逐项做字符串相等比较，末尾再跑独立核验工具与唯一入口测试。下面是该脚本在本工作树的实测输出原文（同目录 `ticket04-replay-raw.txt` 为同一份原始输出）。

## 实测输出（原文）

```
RUN1 plugin_exit=0 tests_exit=0
RUN1 plugin_sha256=14c3fd9a5417e547e61f6b9b4bc1f87ac6c3b10deaef458e4be5c3b66fb5f7ca
RUN1 plugin_mvid=6ff5c638-25ed-48b5-8057-1799d2274d6c
RUN1 plugin_size=1174528
RUN1 tests_sha256=e1190c1541d8549e9a83f6a66b3217b49c751868210f6f65ca06efb337409421
RUN1 tests_mvid=446a97ae-eb5d-4b37-b464-13f2bfc20760
RUN1 tests_size=317952
RUN2 plugin_exit=0 tests_exit=0
RUN2 plugin_sha256=14c3fd9a5417e547e61f6b9b4bc1f87ac6c3b10deaef458e4be5c3b66fb5f7ca
RUN2 plugin_mvid=6ff5c638-25ed-48b5-8057-1799d2274d6c
RUN2 plugin_size=1174528
RUN2 tests_sha256=e1190c1541d8549e9a83f6a66b3217b49c751868210f6f65ca06efb337409421
RUN2 tests_mvid=446a97ae-eb5d-4b37-b464-13f2bfc20760
RUN2 tests_size=317952
=== 逐项机械比较（Run 1 vs Run 2） ===
IDENTICAL  plugin_exit=0 tests_exit=0
IDENTICAL  plugin_sha256=14c3fd9a5417e547e61f6b9b4bc1f87ac6c3b10deaef458e4be5c3b66fb5f7ca
IDENTICAL  plugin_mvid=6ff5c638-25ed-48b5-8057-1799d2274d6c
IDENTICAL  plugin_size=1174528
IDENTICAL  tests_sha256=e1190c1541d8549e9a83f6a66b3217b49c751868210f6f65ca06efb337409421
IDENTICAL  tests_mvid=446a97ae-eb5d-4b37-b464-13f2bfc20760
IDENTICAL  tests_size=317952
=== 独立核验工具 ===
INDEPENDENT_ARTIFACT_VERIFICATION_PASS
=== 唯一入口测试 ===
===============================================================
=== Final Result: 360/360 PASS (Failed: 0) ===
===============================================================
```

## 结论

- 两次 Rebuild 的退出码、SHA-256、MVID、字节数逐项 `IDENTICAL`（机械比较，非人工目测）；
- 产物身份：插件 DLL `14c3fd9a5417e547e61f6b9b4bc1f87ac6c3b10deaef458e4be5c3b66fb5f7ca` / MVID `6ff5c638-25ed-48b5-8057-1799d2274d6c`，测试 exe `e1190c1541d8549e9a83f6a66b3217b49c751868210f6f65ca06efb337409421` / MVID `446a97ae-eb5d-4b37-b464-13f2bfc20760`；
- 独立核验工具 `INDEPENDENT_ARTIFACT_VERIFICATION_PASS`；唯一入口测试 **360/360 PASS**；
- 版本身份 `0.2.4.9` / Case-ID `SPF-0.2.4.9-Experimental-CollisionSlice`。
