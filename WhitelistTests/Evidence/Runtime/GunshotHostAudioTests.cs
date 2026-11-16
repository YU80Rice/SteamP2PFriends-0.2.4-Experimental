using HarmonyLib;
using SDG.NetTransport;
using SDG.Unturned;
using SteamP2PFriends.Core.Patches.GunshotHostAudio;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using UnityEngine;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// t116：Host 枪声叶补偿的自动化契约。
    ///
    /// 自动化层只证明：门控、账本、调用点、去重、RNG 恢复、fail-closed。
    /// 不证明听感；真实 1H+1G / 1H+2G 可听性仍属 UNKNOWN。
    ///
    /// Red-first：Test_CompensationLeafInvokedOnce 依赖 TryCompensateAfterSend 内真实的
    /// 补偿叶调用；删掉该调用后此用例必红。
    /// </summary>
    internal static class GunshotHostAudioTests
    {
        private const uint TestNetId = 0x0000ABCDU;

        internal static bool Test_AnchorLocatesUniqueSendPlayShootCallSite()
        {
            MethodInfo fire = AccessTools.Method(typeof(UseableGun), "fire", Type.EmptyTypes);
            Assert(fire != null, "UseableGun.fire not found");

            List<GunshotIlScan.IlInstruction> source = GunshotIlScan.Read(fire);
            Assert(source != null && source.Count > 0, "fire IL empty");

            int sendFieldLoads = 0;
            foreach (GunshotIlScan.IlInstruction instruction in source)
            {
                FieldInfo field = instruction.Operand as FieldInfo;
                if (instruction.OpCode == OpCodes.Ldsfld && field != null
                    && field.DeclaringType == typeof(UseableGun) && field.Name == "SendPlayShoot")
                {
                    sendFieldLoads++;
                }
            }

            Assert(sendFieldLoads == 1, "SendPlayShoot ldsfld count=" + sendFieldLoads + " expected=1");

            bool located = GunshotHostAudioPatch.TryLocateAnchor(source, out int anchor, out string detail);
            Assert(located, "anchor not located: " + detail);
            MethodInfo invoked = source[anchor].Operand as MethodInfo;
            Assert(invoked != null && invoked.DeclaringType == typeof(ClientInstanceMethod) && invoked.Name == "Invoke",
                "anchor instruction must call ClientInstanceMethod.Invoke");

            // 测试宿主缺 UnityEx.dll，Harmony/MonoMod 无法解析 fire() 全部 token；
            // transpiler 的插入逻辑改用合成 IL 验证（与真实 IL 共用同一锚点匹配器）。
            FieldInfo sendField = typeof(UseableGun).GetField(
                "SendPlayShoot", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(sendField != null, "SendPlayShoot field not found");
            MethodInfo invoke = typeof(ClientInstanceMethod).GetMethod(
                "Invoke", new[] { typeof(NetId), typeof(ENetReliability), typeof(List<ITransportConnection>) });
            Assert(invoke != null, "ClientInstanceMethod.Invoke(List) not found");

            var synthetic = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldsfld, sendField),
                new CodeInstruction(OpCodes.Callvirt, invoke),
                new CodeInstruction(OpCodes.Ret),
            };

            MethodInfo compensation = AccessTools.Method(
                typeof(GunshotHostAudioRuntime), nameof(GunshotHostAudioRuntime.TryCompensateAfterSend));
            Assert(compensation != null, "TryCompensateAfterSend not found");

            var patched = new List<CodeInstruction>(
                GunshotHostAudioPatch.Fire_Transpiler(synthetic, null));
            Assert(GunshotHostAudioPatch.TranspilerInsertions == 2,
                "transpiler insertions=" + GunshotHostAudioPatch.TranspilerInsertions + " expected=2");
            Assert(patched.Count == synthetic.Count + 2, "patched count mismatch");
            Assert(patched[2].opcode == OpCodes.Ldarg_0, "inserted instruction 1 must be ldarg.0");
            Assert(Equals(patched[3].operand, compensation), "inserted instruction 2 must call TryCompensateAfterSend");
            return true;
        }

        internal static bool Test_CompensationLeafInvokedOnce()
        {
            int leafCalls = 0;
            UseableGun gun = NewGun(TestNetId);
            Begin(true, AllTrue(), _ => leafCalls++);

            GunshotHostAudioRuntime.OnFireEnter(gun);
            GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
            GunshotHostAudioRuntime.OnFireExit(gun, false);

            Assert(leafCalls == 1, "compensation leaf calls=" + leafCalls + " expected=1");
            Assert(GunshotHostAudioRuntime.LeafCallCount == 1, "runtime LeafCallCount mismatch");
            Assert(GunshotHostAudioRuntime.LastGateLine.Contains("decision=Compensate"),
                "gate line must record Compensate: " + GunshotHostAudioRuntime.LastGateLine);
            return true;
        }

        internal static bool Test_Negative_ProjectileExcluded()
        {
            return NegativeNoLeaf(facts => { facts.ProjectileNull = false; return facts; }, "projectileNull=0");
        }

        internal static bool Test_Negative_HostShooterExcluded()
        {
            return NegativeNoLeaf(facts => { facts.ShooterNotLocal = false; return facts; }, "shooterNotLocal=0");
        }

        internal static bool Test_Negative_GuestOwnerPathExcluded()
        {
            return NegativeNoLeaf(facts => { facts.P2PHostMode = false; return facts; }, "p2pHost=0");
        }

        internal static bool Test_Negative_TurretExcluded()
        {
            return NegativeNoLeaf(facts => { facts.TurretFalse = false; return facts; }, "turretFalse=0");
        }

        internal static bool Test_Negative_DisabledDoesNothing()
        {
            int leafCalls = 0;
            UseableGun gun = NewGun(TestNetId);
            Begin(false, AllTrue(), _ => leafCalls++);

            GunshotHostAudioRuntime.OnFireEnter(gun);
            GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
            GunshotHostAudioRuntime.OnFireExit(gun, false);

            Assert(leafCalls == 0, "disabled must not invoke leaf");
            Assert(GunshotHostAudioRuntime.GateLogCount == 0, "disabled must not log gate decisions");

            GunshotHostAudioPatch.ResetForTest();
            bool registered = GunshotHostAudioPatch.RegisterManual(null);
            Assert(registered, "disabled RegisterManual must succeed without patching");
            Assert(!GunshotHostAudioPatch.Installed, "disabled must not install any patch");
            return true;
        }

        internal static bool Test_Negative_AnchorMismatchFailsClosed()
        {
            var empty = new List<GunshotIlScan.IlInstruction>
            {
                new GunshotIlScan.IlInstruction { Offset = 0, OpCode = OpCodes.Ret },
            };
            Assert(!GunshotHostAudioPatch.TryLocateAnchor(empty, out _, out _),
                "empty/absent anchor must be rejected");

            FieldInfo sendField = typeof(UseableGun).GetField(
                "SendPlayShoot", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(sendField != null, "SendPlayShoot field not found");
            MethodInfo invoke = typeof(ClientInstanceMethod).GetMethod(
                "Invoke", new[] { typeof(NetId), typeof(ENetReliability), typeof(List<ITransportConnection>) });
            Assert(invoke != null, "ClientInstanceMethod.Invoke(List) not found");

            var ambiguous = new List<GunshotIlScan.IlInstruction>
            {
                new GunshotIlScan.IlInstruction { Offset = 0, OpCode = OpCodes.Ldsfld, Operand = sendField },
                new GunshotIlScan.IlInstruction { Offset = 1, OpCode = OpCodes.Callvirt, Operand = invoke },
                new GunshotIlScan.IlInstruction { Offset = 2, OpCode = OpCodes.Ldsfld, Operand = sendField },
                new GunshotIlScan.IlInstruction { Offset = 3, OpCode = OpCodes.Callvirt, Operand = invoke },
            };
            Assert(!GunshotHostAudioPatch.TryLocateAnchor(ambiguous, out _, out string detail),
                "ambiguous anchor must be rejected: " + detail);
            return true;
        }

        internal static bool Test_Dedup_SameKeyTwiceOnlyOneLeaf()
        {
            int leafCalls = 0;
            UseableGun gun = NewGun(TestNetId);
            Begin(true, AllTrue(), _ => leafCalls++);

            GunshotHostAudioRuntime.OnFireEnter(gun);
            GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
            GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
            GunshotHostAudioRuntime.OnFireExit(gun, false);

            Assert(leafCalls == 1, "duplicate anchor must not repeat leaf, calls=" + leafCalls);
            Assert(GunshotHostAudioRuntime.LastGateLine.Contains("decision=Duplicate"),
                "second anchor must be logged Duplicate: " + GunshotHostAudioRuntime.LastGateLine);
            return true;
        }

        internal static bool Test_Dedup_MultiPelletSingleFireOneLeaf()
        {
            int leafCalls = 0;
            UseableGun gun = NewGun(TestNetId);
            Begin(true, AllTrue(), _ => leafCalls++);

            GunshotHostAudioRuntime.OnFireEnter(gun);
            for (int pellet = 0; pellet < 8; pellet++)
            {
                GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
            }
            GunshotHostAudioRuntime.OnFireExit(gun, false);

            Assert(leafCalls == 1, "multi-pellet single fire must stay one leaf, calls=" + leafCalls);
            return true;
        }

        internal static bool Test_Dedup_DifferentFiresEachOneLeaf()
        {
            int leafCalls = 0;
            UseableGun gun = NewGun(TestNetId);
            Begin(true, AllTrue(), _ => leafCalls++);

            for (int fire = 0; fire < 3; fire++)
            {
                GunshotHostAudioRuntime.OnFireEnter(gun);
                GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
                GunshotHostAudioRuntime.OnFireExit(gun, false);
            }

            Assert(leafCalls == 3, "three distinct fires must each compensate once, calls=" + leafCalls);
            return true;
        }

        internal static bool Test_FailedLeafNotRetried()
        {
            int attempts = 0;
            UseableGun gun = NewGun(TestNetId);
            Begin(true, AllTrue(), _ =>
            {
                attempts++;
                throw new InvalidOperationException("leaf boom");
            });

            GunshotHostAudioRuntime.OnFireEnter(gun);
            GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
            GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
            GunshotHostAudioRuntime.OnFireExit(gun, false);

            Assert(attempts == 1, "failed leaf must not be retried, attempts=" + attempts);
            Assert(GunshotHostAudioRuntime.LastGateLine.Contains("decision=Duplicate"),
                "retry must be rejected as Duplicate: " + GunshotHostAudioRuntime.LastGateLine);
            return true;
        }

        internal static bool Test_RngStateRestoredAfterLeaf()
        {
            // 测试宿主没有 UnityEngine.Random 的 ECall，因此用可观测的保存/恢复替身：
            // 替身模拟一个随机状态；叶内部两次推进；finally 必须把状态恢复原值，
            // 之后的序列与未补偿时一致。
            int[] state = { 20261108 };
            int saveCalls = 0;
            int restoreCalls = 0;
            int expectedNext = Next(state[0]);

            UseableGun gun = NewGun(TestNetId);
            Begin(true, AllTrue(), _ =>
            {
                state[0] = Next(state[0]);
                state[0] = Next(state[0]);
            });
            GunshotHostAudioRuntime.RandomStateSaveForTest = () =>
            {
                saveCalls++;
                return state[0];
            };
            GunshotHostAudioRuntime.RandomStateRestoreForTest = saved =>
            {
                restoreCalls++;
                state[0] = (int)saved;
            };

            GunshotHostAudioRuntime.OnFireEnter(gun);
            GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
            GunshotHostAudioRuntime.OnFireExit(gun, false);

            Assert(saveCalls == 1, "random state must be saved exactly once, calls=" + saveCalls);
            Assert(restoreCalls == 1, "random state must be restored exactly once, calls=" + restoreCalls);
            Assert(state[0] == 20261108, "random state must be restored to pre-leaf value");
            Assert(Next(state[0]) == expectedNext, "subsequent random sequence must be unchanged");
            return true;
        }

        private static int Next(int value)
        {
            unchecked
            {
                return (value * 1103515245) + 12345;
            }
        }

        internal static bool Test_LedgerStateMachine()
        {
            GunshotHostAudioLedger.Reset();
            var key = new GunshotFireKey(1UL, 2U, 3U, 4U, 5L);
            Assert(GunshotHostAudioLedger.StateOf(key) == GunshotAttemptState.Unclaimed, "initial state");
            Assert(GunshotHostAudioLedger.TryClaim(key), "first claim");
            Assert(GunshotHostAudioLedger.StateOf(key) == GunshotAttemptState.ClaimedBeforeCall, "claimed state");
            Assert(!GunshotHostAudioLedger.TryClaim(key), "second claim must fail");
            Assert(GunshotHostAudioLedger.Complete(key), "complete claimed");
            Assert(GunshotHostAudioLedger.StateOf(key) == GunshotAttemptState.Completed, "completed state");
            Assert(!GunshotHostAudioLedger.Fail(key), "cannot fail completed key");
            Assert(!GunshotHostAudioLedger.TryClaim(key), "completed key never reclaims");
            GunshotHostAudioLedger.Reset();
            return true;
        }

        internal static bool Test_PrefixFinalizerStackAssociation()
        {
            int leafCalls = 0;
            UseableGun gunA = NewGun(0x1111U);
            UseableGun gunB = NewGun(0x2222U);
            Begin(true, AllTrue(), _ => leafCalls++);

            GunshotHostAudioRuntime.OnFireEnter(gunA);
            Assert(GunshotHostAudioRuntime.FrameDepth == 1, "prefix must push one frame");
            GunshotHostAudioRuntime.TryCompensateAfterSend(gunB);
            Assert(leafCalls == 0, "mismatched gun must not compensate");
            Assert(GunshotHostAudioRuntime.LastGateLine.Contains("decision=AssociationMismatch"),
                "mismatch must be logged: " + GunshotHostAudioRuntime.LastGateLine);
            GunshotHostAudioRuntime.OnFireExit(gunA, false);
            Assert(GunshotHostAudioRuntime.FrameDepth == 0, "finalizer must pop frame");

            GunshotHostAudioRuntime.OnFireEnter(gunA);
            GunshotHostAudioRuntime.TryCompensateAfterSend(gunA);
            GunshotHostAudioRuntime.OnFireExit(gunA, true);
            Assert(leafCalls == 1, "single compensation before fault");
            return true;
        }

        internal static bool Test_GateLineContainsAllFactsAndDecision()
        {
            var key = new GunshotFireKey(7UL, 8U, 9U, 10U, 11L);
            string line = GunshotHostAudioPolicy.FormatGateLine(AllTrue(), key, "Compensate", "PEI");
            string[] required =
            {
                "p2pHost=1", "listenHost=1", "notDedicated=1", "connected=1", "server=1",
                "levelLoaded=1", "shooterNotLocal=1", "sendAnchor=1", "projectileNull=1",
                "turretFalse=1", "weaponValid=1", "audioSource=1", "clip=1", "leafNotEntered=1",
                "association=1", "epoch=7", "connGen=8", "weaponGen=9", "netId=10",
                "ordinal=11", "map=PEI", "decision=Compensate",
            };
            foreach (string token in required)
            {
                Assert(line.Contains(token), "gate line missing " + token + ": " + line);
            }

            Assert(!GunshotHostAudioPolicy.IsEligible(default(GunshotGateFacts)), "default facts must be ineligible");
            Assert(GunshotHostAudioPolicy.IsEligible(AllTrue()), "all-true facts must be eligible");
            return true;
        }

        internal static bool Test_ConfigDefaultIsOff()
        {
            Assert(!GunshotHostAudioControl.DefaultEnable, "cfg default must be false");
            // t145：开关已并入插件主 cfg，不再有独立 cfg 文件名。
            Assert(SteamP2PFriends.Core.Config.PluginConfig.Bug2HostGunshotEnable != null,
                "main config Bug2HostGunshot.Enable must be bound");
            Assert(!SteamP2PFriends.Core.Config.PluginConfig.Bug2HostGunshotEnable.Value,
                "main cfg default must be false");
            GunshotHostAudioRuntime.ResetForTest();
            GunshotHostAudioPatch.ResetForTest();
            Assert(GunshotHostAudioPatch.RegisterManual(null), "disabled registration returns true");
            Assert(!GunshotHostAudioPatch.Installed, "disabled registration must not patch");
            return true;
        }

        internal static bool Test_NativeLeafObserverBlocksCompensation()
        {
            int leafCalls = 0;
            UseableGun gun = NewGun(TestNetId);
            Begin(true, AllTrue(), _ => leafCalls++);

            GunshotHostAudioRuntime.OnFireEnter(gun);
            GunshotHostAudioRuntime.OnNativeLeafObserved(gun);
            GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
            GunshotHostAudioRuntime.OnFireExit(gun, false);

            Assert(leafCalls == 0, "native leaf already entered must not compensate, calls=" + leafCalls);
            Assert(GunshotHostAudioRuntime.LastGateLine.Contains("leafNotEntered=0"),
                "gate must show native leaf entered: " + GunshotHostAudioRuntime.LastGateLine);
            Assert(GunshotHostAudioRuntime.LastGateLine.Contains("decision=NotEligible"),
                "decision must be NotEligible: " + GunshotHostAudioRuntime.LastGateLine);
            Assert(GunshotHostAudioRuntime.NativeLeafObservationCount == 1,
                "native leaf observation count=" + GunshotHostAudioRuntime.NativeLeafObservationCount);
            return true;
        }

        internal static bool Test_ProductionLeafResolution()
        {
            GunshotHostAudioRuntime.ResetForTest();
            GunshotHostAudioPatch.ResetForTest();

            Action<UseableGun> leaf = GunshotHostAudioRuntime.ResolveDefaultLeaf();
            Assert(leaf != null, "ResolveDefaultLeaf must resolve UseableGun.playGunshot");

            MethodInfo expected = AccessTools.Method(typeof(UseableGun), "playGunshot", Type.EmptyTypes);
            Assert(expected != null, "UseableGun.playGunshot not found");
            Assert(leaf.Method == expected, "resolved leaf must target UseableGun.playGunshot");
            Assert(GunshotHostAudioRuntime.LeafResolveFailures == 0,
                "resolve failures=" + GunshotHostAudioRuntime.LeafResolveFailures);
            return true;
        }

        internal static bool Test_HotEnableDisableCycle()
        {
            GunshotHostAudioRuntime.ResetForTest();
            GunshotHostAudioPatch.ResetForTest();
            GunshotHostAudioControl.ResetForTest();

            // R24-06 / T126-F1：安装、卸载、回查全部走真实 Harmony。
            // 真实 fire()/playGunshot() 的 IL 复制需要 UnityEx（测试宿主没有），
            // 因此把 patch 目标换成 FakeGun 的无害方法；控制流与 GetPatchInfo 回查仍是生产代码。
            MethodInfo fakeFire = AccessTools.Method(typeof(FakeGun), "FakeFire", Type.EmptyTypes);
            MethodInfo fakeLeaf = AccessTools.Method(typeof(FakeGun), "FakeLeaf", Type.EmptyTypes);
            Assert(fakeFire != null && fakeLeaf != null, "FakeGun methods not found");
            GunshotHostAudioPatch.FireTargetForTest = fakeFire;
            GunshotHostAudioPatch.PlayGunshotTargetForTest = fakeLeaf;
            GunshotHostAudioPatch.SkipAnchorVerifyForTest = true;
            GunshotHostAudioPatch.SkipFirePatchForTest = true;

            var harmony = new Harmony("spf.t127.hot-enable");
            Assert(GunshotHostAudioControl.ApplyForTest(harmony, true), "hot enable must install");
            Assert(GunshotHostAudioPatch.Installed, "Installed after enable");
            Assert(GunshotHostAudioRuntime.NativeObserverInstalled, "native observer installed");
            Assert(CountOurPostfixes(harmony, fakeLeaf) == 1,
                "observer postfix must be visible in GetPatchInfo");

            Assert(GunshotHostAudioControl.ApplyForTest(harmony, false), "disable must succeed");
            Assert(!GunshotHostAudioPatch.Installed, "uninstalled after disable");
            Assert(!GunshotHostAudioRuntime.NativeObserverInstalled, "observer uninstalled");
            Assert(CountOurPostfixes(harmony, fakeLeaf) == 0, "observer postfix must be removed");

            Assert(GunshotHostAudioControl.ApplyForTest(harmony, true), "re-enable must install again");
            Assert(GunshotHostAudioPatch.Installed, "Installed after re-enable (latch cleared)");
            Assert(!GunshotHostAudioRuntime.FailClosed, "fail-closed must be cleared on re-enable");
            Assert(CountOurPostfixes(harmony, fakeLeaf) == 1, "observer postfix must be re-installed");

            // harmony=null 不得落 _attempted 闩锁：失败后仍可用真实 harmony 重试。
            GunshotHostAudioPatch.ResetForTest();
            GunshotHostAudioPatch.FireTargetForTest = fakeFire;
            GunshotHostAudioPatch.PlayGunshotTargetForTest = fakeLeaf;
            GunshotHostAudioPatch.SkipAnchorVerifyForTest = true;
            GunshotHostAudioPatch.SkipFirePatchForTest = true;
            GunshotHostAudioRuntime.SetEnabled(true);
            Assert(!GunshotHostAudioPatch.RegisterManual(null), "null harmony must fail");
            Assert(!GunshotHostAudioPatch.Installed, "null harmony must not install");
            Assert(GunshotHostAudioPatch.RegisterManual(harmony), "retry after failure must install");
            Assert(GunshotHostAudioPatch.Installed, "Installed after retry");
            GunshotHostAudioPatch.Uninstall();
            return true;
        }

        /// <summary>
        /// R24-03：安装中途失败（transpiler 抛异常）必须同时撤销 fire 与 playGunshot 两个 patch，
        /// 做到 fail-closed 零 patch。这里用 FakeGun.FakeFire 的真实 Harmony 安装触发失败。
        /// </summary>
        internal static bool Test_RealHarmonyFailureLeavesNoPatch()
        {
            GunshotHostAudioRuntime.ResetForTest();
            GunshotHostAudioPatch.ResetForTest();
            GunshotHostAudioControl.ResetForTest();

            MethodInfo fakeFire = AccessTools.Method(typeof(FakeGun), "FakeFire", Type.EmptyTypes);
            MethodInfo fakeLeaf = AccessTools.Method(typeof(FakeGun), "FakeLeaf", Type.EmptyTypes);
            GunshotHostAudioPatch.FireTargetForTest = fakeFire;
            GunshotHostAudioPatch.PlayGunshotTargetForTest = fakeLeaf;
            GunshotHostAudioPatch.SkipAnchorVerifyForTest = true;
            GunshotHostAudioPatch.SkipFirePatchForTest = false;
            GunshotHostAudioRuntime.SetEnabled(true);

            var harmony = new Harmony("spf.t127.failure");
            Assert(!GunshotHostAudioPatch.RegisterManual(harmony),
                "forced transpiler failure must return false");
            Assert(!GunshotHostAudioPatch.Installed, "not installed after failure");
            Assert(CountOurPostfixes(harmony, fakeLeaf) == 0, "observer postfix must be removed");
            Assert(CountPatches(harmony, fakeFire) == 0, "fire patches must be removed");
            Assert(GunshotHostAudioRuntime.FailClosed, "failure must fail closed");
            return true;
        }

        /// <summary>
        /// R24-02：leaf 解析失败（resolver 返回 null）必须 fail-closed + LeafUnresolved，且不调用 leaf。
        /// </summary>
        internal static bool Test_LeafResolverNullFailsClosed()
        {
            int leafCalls = 0;
            UseableGun gun = NewGun(TestNetId);
            Begin(true, AllTrue(), _ => leafCalls++);
            GunshotHostAudioRuntime.LeafInvokerForTest = null;
            GunshotHostAudioRuntime.LeafResolverForTest = () => null;

            GunshotHostAudioRuntime.OnFireEnter(gun);
            GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
            GunshotHostAudioRuntime.OnFireExit(gun, false);

            Assert(leafCalls == 0, "unresolved leaf must not be invoked");
            Assert(GunshotHostAudioRuntime.FailClosed, "unresolved leaf must fail closed");
            Assert(GunshotHostAudioRuntime.LastGateLine.Contains("decision=LeafUnresolved"),
                "gate must mark LeafUnresolved: " + GunshotHostAudioRuntime.LastGateLine);
            return true;
        }

        /// <summary>
        /// R24-04：15 项门控的纯映射逐项断言（只翻转一个输入）。
        /// </summary>
        internal static bool Test_BuildFactsFromStateAllGates()
        {
            GunshotGateFacts all = Build(true, true, false, true, true, true, true, true, false,
                true, true, false, false, true, true, true, true, true);
            Assert(GunshotHostAudioPolicy.IsEligible(all), "all-true state must be eligible");

            Assert(!Build(false, true, false, true, true, true, true, true, false,
                true, true, false, false, true, true, true, true, true).P2PHostMode, "p2pHostMode");
            Assert(!Build(true, false, false, true, true, true, true, true, false,
                true, true, false, false, true, true, true, true, true).ListenHostActive, "listenHostActive");
            Assert(!Build(true, true, true, true, true, true, true, true, false,
                true, true, false, false, true, true, true, true, true).NotDedicated, "notDedicated");
            Assert(!Build(true, true, false, false, true, true, true, true, false,
                true, true, false, false, true, true, true, true, true).Connected, "connected");
            Assert(!Build(true, true, false, true, false, true, true, true, false,
                true, true, false, false, true, true, true, true, true).ServerActive, "serverActive");
            Assert(!Build(true, true, false, true, true, false, true, true, false,
                true, true, false, false, true, true, true, true, true).LevelLoaded, "levelLoaded");
            Assert(!Build(true, true, false, true, true, true, false, true, false,
                true, true, false, false, true, true, true, true, true).ShooterNotLocal, "shooterPresent");
            Assert(!Build(true, true, false, true, true, true, true, false, false,
                true, true, false, false, true, true, true, true, true).ShooterNotLocal, "shooterChannelPresent");
            Assert(!Build(true, true, false, true, true, true, true, true, true,
                true, true, false, false, true, true, true, true, true).ShooterNotLocal, "shooterChannelIsLocal");
            Assert(!Build(true, true, false, true, true, true, true, true, false,
                false, true, false, false, true, true, true, true, true).WeaponInstanceValid, "gunPresent");
            Assert(!Build(true, true, false, true, true, true, true, true, false,
                true, false, false, false, true, true, true, true, true).ProjectileNull, "assetPresent");
            Assert(!Build(true, true, false, true, true, true, true, true, false,
                true, true, true, false, true, true, true, true, true).ProjectileNull, "projectilePresent");
            Assert(!Build(true, true, false, true, true, true, true, true, false,
                true, true, false, true, true, true, true, true, true).TurretFalse, "isTurret");
            Assert(!Build(true, true, false, true, true, true, true, true, false,
                true, true, false, false, false, true, true, true, true).ClipPresent, "clipPresent");
            Assert(!Build(true, true, false, true, true, true, true, true, false,
                true, true, false, false, true, false, true, true, true).AudioSourcePresent, "audioSourcePresent");
            Assert(!Build(true, true, false, true, true, true, true, true, false,
                true, true, false, false, true, true, false, true, true).RealSendAnchor, "realSendAnchor");
            Assert(!Build(true, true, false, true, true, true, true, true, false,
                true, true, false, false, true, true, true, false, true).NativeLeafNotEntered, "nativeLeafNotEntered");
            Assert(!Build(true, true, false, true, true, true, true, true, false,
                true, true, false, false, true, true, true, true, false).AssociationComplete, "associationComplete");
            return true;
        }

        /// <summary>
        /// R24-07：清理路径必须同步重建插入队列（队列长度 == 字典条目数，且有界）。
        /// </summary>
        internal static bool Test_LedgerQueueRebuiltAfterPurge()
        {
            GunshotHostAudioLedger.Reset();
            for (int i = 0; i < 8; i++)
            {
                Assert(GunshotHostAudioLedger.TryClaim(new GunshotFireKey(11UL, 1U, 1U, 7U, i)),
                    "claim key " + i);
            }

            Assert(GunshotHostAudioLedger.InsertionQueueCount == GunshotHostAudioLedger.Count,
                "queue must mirror dictionary before purge");
            Assert(GunshotHostAudioLedger.PurgeExcept(11UL, 2U) == 8, "all old keys purged");
            Assert(GunshotHostAudioLedger.Count == 0, "dictionary cleared");
            Assert(GunshotHostAudioLedger.InsertionQueueCount == 0, "queue rebuilt empty");

            Assert(GunshotHostAudioLedger.TryClaim(new GunshotFireKey(11UL, 2U, 1U, 7U, 99L)),
                "claim current key");
            for (int i = 0; i < GunshotHostAudioLedger.MaxEntries + 16; i++)
            {
                GunshotHostAudioLedger.TryClaim(new GunshotFireKey(11UL, 2U, 1U, 7U, 100L + i));
            }

            Assert(GunshotHostAudioLedger.Count <= GunshotHostAudioLedger.MaxEntries,
                "ledger must stay bounded, count=" + GunshotHostAudioLedger.Count);
            Assert(GunshotHostAudioLedger.InsertionQueueCount == GunshotHostAudioLedger.Count,
                "queue must stay in sync after eviction, queue="
                + GunshotHostAudioLedger.InsertionQueueCount + " dict=" + GunshotHostAudioLedger.Count);
            GunshotHostAudioLedger.Reset();
            return true;
        }

        /// <summary>
        /// R24-08：插入点 anchor+1 不得被任何分支/switch 当作目标（否则插指令会破坏分支）。
        /// </summary>
        internal static bool Test_AnchorPlusOneNotBranchTarget()
        {
            MethodInfo fire = AccessTools.Method(typeof(UseableGun), "fire", Type.EmptyTypes);
            List<GunshotIlScan.IlInstruction> source = GunshotIlScan.Read(fire);
            Assert(GunshotHostAudioPatch.TryLocateAnchor(source, out int anchor, out string detail),
                "anchor not located: " + detail);
            Assert(anchor + 1 < source.Count, "anchor must not be the last instruction");

            int insertedOffset = source[anchor + 1].Offset;
            for (int i = 0; i < source.Count; i++)
            {
                int[] switchTargets = source[i].Operand as int[];
                if (switchTargets != null)
                {
                    for (int t = 0; t < switchTargets.Length; t++)
                    {
                        Assert(switchTargets[t] != insertedOffset,
                            "switch at idx=" + i + " targets anchor+1 (0x"
                            + insertedOffset.ToString("X4") + ")");
                    }
                }

                if (!IsBranch(source[i].OpCode))
                {
                    continue;
                }

                Assert(Convert.ToInt32(source[i].Operand) != insertedOffset,
                    "branch at idx=" + i + " targets anchor+1 (0x"
                    + insertedOffset.ToString("X4") + ")");
            }

            return true;
        }

        /// <summary>
        /// T126-F2：`_supplementalLeafDepth` 守卫必须绑定——补偿叶内部再触发观察器时，
        /// 不得把本次补偿误记成「原生叶已进入」。
        /// </summary>
        internal static bool Test_SupplementalLeafDepthGuard()
        {
            int leafCalls = 0;
            UseableGun gun = NewGun(TestNetId);
            Begin(true, AllTrue(), _ => leafCalls++);
            GunshotHostAudioRuntime.LeafInvokerForTest = g =>
            {
                leafCalls++;
                GunshotHostAudioRuntime.OnNativeLeafObserved(g);
            };

            GunshotHostAudioRuntime.OnFireEnter(gun);
            GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
            GunshotHostAudioRuntime.OnFireExit(gun, false);

            Assert(leafCalls == 1, "leaf must be invoked once, calls=" + leafCalls);
            Assert(GunshotHostAudioRuntime.NativeLeafObservationCount == 0,
                "supplemental leaf must not be observed as native, count="
                + GunshotHostAudioRuntime.NativeLeafObservationCount);
            Assert(GunshotHostAudioRuntime.LastGateLine.Contains("leafNotEntered=1"),
                "gate must still report native leaf not entered: " + GunshotHostAudioRuntime.LastGateLine);
            return true;
        }

        /// <summary>
        /// R24-05 / R25-06：Initialize 的 bool 返回值必须有语义——注册失败时返回 false，
        /// 且 LastFailure 能读到具体原因（不是只断言签名）。
        /// </summary>
        internal static bool Test_InitializeReturnsSemanticBool()
        {
            MethodInfo initialize = typeof(GunshotHostAudioControl).GetMethod(
                "Initialize",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert(initialize != null, "Initialize not found");
            Assert(initialize.ReturnType == typeof(bool), "Initialize must return bool");

            GunshotHostAudioRuntime.ResetForTest();
            GunshotHostAudioPatch.ResetForTest();
            GunshotHostAudioControl.ResetForTest();
            try
            {
                GunshotHostAudioPatch.PlayGunshotTargetForTest =
                    AccessTools.Method(typeof(FakeGun), "FakeLeaf", Type.EmptyTypes);
                GunshotHostAudioPatch.SkipAnchorVerifyForTest = true;
                GunshotHostAudioPatch.SkipFirePatchForTest = true;
                GunshotHostAudioPatch.PostfixVerifierForTest = (h, t, p) => false;
                var harmony = new Harmony("spf.t129.init-semantic");
                Assert(!GunshotHostAudioControl.ApplyForTest(harmony, true),
                    "module init failure must surface false");
                Assert(GunshotHostAudioControl.LastFailure.Contains("native leaf postfix not installed"),
                    "LastFailure must carry reason: " + GunshotHostAudioControl.LastFailure);
            }
            finally
            {
                // R26-04：断言中途失败也不得把静态标志/已装 patch 泄漏给后续用例。
                GunshotHostAudioPatch.Uninstall();
                GunshotHostAudioRuntime.ResetForTest();
                GunshotHostAudioPatch.ResetForTest();
                GunshotHostAudioControl.ResetForTest();
            }

            return true;
        }

        /// <summary>
        /// R25-01：Harmony.Patch 返回但 GetPatchInfo 看不到我们的 Postfix 时，
        /// 必须拒绝启用（fail-closed）、撤销已装 patch、observer 标志为 false、原因可读。
        /// 停用回查守卫（if 恒假）后本用例必红。
        /// </summary>
        internal static bool Test_PostfixVerifierRejectionFailsClosed()
        {
            GunshotHostAudioRuntime.ResetForTest();
            GunshotHostAudioPatch.ResetForTest();
            GunshotHostAudioControl.ResetForTest();

            try
            {
                MethodInfo fakeFire = AccessTools.Method(typeof(FakeGun), "FakeFire", Type.EmptyTypes);
                MethodInfo fakeLeaf = AccessTools.Method(typeof(FakeGun), "FakeLeaf", Type.EmptyTypes);
                GunshotHostAudioPatch.FireTargetForTest = fakeFire;
                GunshotHostAudioPatch.PlayGunshotTargetForTest = fakeLeaf;
                GunshotHostAudioPatch.SkipAnchorVerifyForTest = true;
                GunshotHostAudioPatch.SkipFirePatchForTest = true;
                // 模拟「安装返回成功、回查看不到」的静默失败。
                GunshotHostAudioPatch.PostfixVerifierForTest = (h, t, p) => false;
                GunshotHostAudioRuntime.SetEnabled(true);

                var harmony = new Harmony("spf.t129.postfix-verify");
                Assert(!GunshotHostAudioPatch.RegisterManual(harmony),
                    "postfix verify failure must refuse to enable");
                Assert(!GunshotHostAudioPatch.Installed, "must not mark Installed after verify failure");
                Assert(!GunshotHostAudioRuntime.NativeObserverInstalled,
                    "must SetNativeObserverInstalled(false) after verify failure");
                Assert(GunshotHostAudioPatch.FailureReason.Contains("native leaf postfix not installed"),
                    "failure reason=" + GunshotHostAudioPatch.FailureReason);
                Assert(CountOurPostfixes(harmony, fakeLeaf) == 0,
                    "verify failure must unpatch the just-installed postfix");
                Assert(CountPatches(harmony, fakeFire) == 0,
                    "must not install fire patch after verify failure");
                Assert(GunshotHostAudioRuntime.FailClosed, "verify failure must fail closed");
            }
            finally
            {
                // R26-04：断言中途失败也不得把静态标志/已装 patch 泄漏给后续用例。
                GunshotHostAudioPatch.Uninstall();
                GunshotHostAudioRuntime.ResetForTest();
                GunshotHostAudioPatch.ResetForTest();
                GunshotHostAudioControl.ResetForTest();
            }

            return true;
        }

        /// <summary>
        /// R25-02：Collect 的「SDK 读取 → BuildFactsFromState 实参」接线必须可被用例抓到。
        /// 这里令 Provider.isConnected=true、Level.isLoaded=false：若 levelLoaded 的读取源被换成
        /// Provider.isConnected，观察器会看到 LevelLoaded=true，本用例必红。
        /// </summary>
        internal static bool Test_CollectWiringFromSdkSources()
        {
            GunshotHostAudioRuntime.ResetForTest();
            GunshotHostAudioPatch.ResetForTest();

            FieldInfo connectedField = typeof(Provider).GetField("_isConnected",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo serverField = typeof(Provider).GetField("_isServer",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo loadedField = typeof(Level).GetField("_isLoaded",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo dedicatedField = typeof(Dedicator).GetField("_isDedicated",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(connectedField != null && serverField != null
                && loadedField != null && dedicatedField != null,
                "SDK static fields not found");

            object oldConnected = connectedField.GetValue(null);
            object oldServer = serverField.GetValue(null);
            object oldLoaded = loadedField.GetValue(null);
            object oldDedicated = dedicatedField.GetValue(null);
            try
            {
                connectedField.SetValue(null, true);
                serverField.SetValue(null, false);
                loadedField.SetValue(null, false);
                dedicatedField.SetValue(null, false);

                bool observed = false;
                GunshotHostAudioRuntime.GunshotCollectSources captured =
                    default(GunshotHostAudioRuntime.GunshotCollectSources);
                GunshotHostAudioRuntime.CollectObserverForTest = sources =>
                {
                    captured = sources;
                    observed = true;
                };

                GunshotGateFacts facts = GunshotHostAudioRuntime.Collect(null);
                Assert(observed, "Collect must invoke observer; collection error=" + facts.CollectionError);
                Assert(captured.Connected, "connected must come from Provider.isConnected=true");
                Assert(!captured.LevelLoaded,
                    "levelLoaded must come from Level.isLoaded=false (got true: source was replaced)");
                Assert(facts.Connected && !facts.LevelLoaded, "facts must mirror the SDK sources");
                Assert(facts.NotDedicated, "notDedicated must come from Dedicator=false");
            }
            finally
            {
                connectedField.SetValue(null, oldConnected);
                serverField.SetValue(null, oldServer);
                loadedField.SetValue(null, oldLoaded);
                dedicatedField.SetValue(null, oldDedicated);
                GunshotHostAudioRuntime.CollectObserverForTest = null;
            }

            return true;
        }

        /// <summary>
        /// R25-03：Host 枪声模块初始化失败必须把 false 传播进诊断阶段的 ok；
        /// 同时用 IL 契约证明 InitializeDiagnosticStage 真的调用了这个传播函数。
        /// </summary>
        internal static bool Test_DiagnosticsStagePropagatesModuleFailure()
        {
            SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = _ => false;
            try
            {
                Assert(!SteamP2PFriendsPlugin.ApplyGunshotModuleOutcome(true, null),
                    "module failure must turn ok=true into false");
            }
            finally
            {
                SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = null;
            }

            SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = _ => true;
            try
            {
                Assert(SteamP2PFriendsPlugin.ApplyGunshotModuleOutcome(true, null),
                    "module success must preserve ok=true");
                Assert(!SteamP2PFriendsPlugin.ApplyGunshotModuleOutcome(false, null),
                    "incoming ok=false must stay false");
            }
            finally
            {
                SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = null;
            }

            MethodInfo stage = AccessTools.Method(
                typeof(SteamP2PFriendsPlugin), "InitializeDiagnosticStage", Type.EmptyTypes);
            Assert(stage != null, "InitializeDiagnosticStage not found");
            int calls = IlContractProbe.CountMethodCalls(
                stage, "SteamP2PFriends.SteamP2PFriendsPlugin", "ApplyGunshotModuleOutcome");
            Assert(calls >= 1,
                "InitializeDiagnosticStage must call ApplyGunshotModuleOutcome, calls=" + calls);
            return true;
        }

        /// <summary>
        /// R27-01：模块结果折进 stage ok 必须是纯函数语义，而不是 IL 形状。
        /// 穷举 stageOk × moduleOk 四组真值表：只有 (true,true) 为 true；
        /// 任何失败都不得被翻回 true。Fold 恒 true / 改成或 / 分支体写 ok=true 都会违反断言。
        /// </summary>
        internal static bool Test_DiagnosticsStageAdoptsModuleResult()
        {
            Assert(SteamP2PFriendsPlugin.FoldGunshotOutcome(true, true),
                "fold(true,true) must be true");
            Assert(!SteamP2PFriendsPlugin.FoldGunshotOutcome(true, false),
                "fold(true,false) must be false（模块失败不得被翻回 true）");
            Assert(!SteamP2PFriendsPlugin.FoldGunshotOutcome(false, true),
                "fold(false,true) must be false");
            Assert(!SteamP2PFriendsPlugin.FoldGunshotOutcome(false, false),
                "fold(false,false) must be false");

            // 生产接线：ApplyGunshotModuleOutcome 的失败结果必须经过 Fold 才能成为 stage ok。
            SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = _ => false;
            try
            {
                bool moduleOk = SteamP2PFriendsPlugin.ApplyGunshotModuleOutcome(true, null);
                Assert(!moduleOk, "module failure must yield moduleOk=false");
                Assert(!SteamP2PFriendsPlugin.FoldGunshotOutcome(true, moduleOk),
                    "stage ok must not remain true when module failed");
            }
            finally
            {
                SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = null;
            }

            SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = _ => true;
            try
            {
                bool moduleOk = SteamP2PFriendsPlugin.ApplyGunshotModuleOutcome(true, null);
                Assert(moduleOk, "module success must yield moduleOk=true");
                Assert(SteamP2PFriendsPlugin.FoldGunshotOutcome(true, moduleOk),
                    "fold must preserve true when both inputs are true");
            }
            finally
            {
                SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = null;
            }

            // R28-01/R28-02：stage 隔离改用 internal static 开关（签名恢复无参），
            // 并补「前序已失败」起点：把前序 ok 硬置 true 或直接丢弃的变异必须变红。
            MethodInfo isolatedStage = AccessTools.Method(
                typeof(SteamP2PFriendsPlugin), "InitializeDiagnosticStage", Type.EmptyTypes);
            Assert(isolatedStage != null, "InitializeDiagnosticStage not found");
            object plugin = FormatterServices.GetUninitializedObject(typeof(SteamP2PFriendsPlugin));
            SteamP2PFriendsPlugin.GunshotStageOnlyForTest = true;
            try
            {
                SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = _ => true;
                SteamP2PFriendsPlugin.PriorStageFailedForTest = false;
                Assert((bool)isolatedStage.Invoke(plugin, null),
                    "stage must stay true when prior ok and module ok");

                SteamP2PFriendsPlugin.PriorStageFailedForTest = true;
                Assert(!(bool)isolatedStage.Invoke(plugin, null),
                    "stage must keep prior failure（丢弃前序 ok 的变异必须红）");

                SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = _ => false;
                Assert(!(bool)isolatedStage.Invoke(plugin, null),
                    "stage must adopt module failure");

                SteamP2PFriendsPlugin.PriorStageFailedForTest = false;
                Assert(!(bool)isolatedStage.Invoke(plugin, null),
                    "stage must reject module failure even when prior ok");
            }
            finally
            {
                SteamP2PFriendsPlugin.ResetDiagnosticsStageForTest();
                SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = null;
            }

            return true;
        }
        /// <summary>
        /// R30-01：seam 改为「替换动作」后，五段的真实调用必须可证。
        /// 注入记录器包装每一段：断言五段全部被调用、顺序正确、real.Method 身份与预期一致
        /// （「把真实动作换成空 lambda」会因身份不匹配而红）；
        /// 「删掉 Run 里唯一的 a() 调用」会因 entered.Count==0 而红。
        /// 抛异常模式下 stage 仍必须返回 false。
        /// </summary>
        internal static bool Test_DiagnosticStageProductionModeSections()
        {
            MethodInfo stage = AccessTools.Method(
                typeof(SteamP2PFriendsPlugin), "InitializeDiagnosticStage", Type.EmptyTypes);
            Assert(stage != null, "InitializeDiagnosticStage not found");
            object plugin = FormatterServices.GetUninitializedObject(typeof(SteamP2PFriendsPlugin));
            var entered = new List<string>();
            string throwSection = null;
            try
            {
                SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = _ => true;
                SteamP2PFriendsPlugin.DiagnosticStageActionForTest = (section, real) => () =>
                {
                    entered.Add(section + "|" + real.Method.DeclaringType.Name + "." + real.Method.Name);
                    if (section == throwSection)
                        throw new InvalidOperationException("test-injected diagnostic section failure: " + section);
                };

                Assert((bool)stage.Invoke(plugin, null),
                    "production mode must return true when all five sections run");
                Assert(entered.Count == 5,
                    "all five sections must be invoked, got " + entered.Count + ": " + string.Join(", ", entered));
                string[] sections = { "unity", "nativeSns", "bug1Probe", "bug1ProbeControl", "gate" };
                for (int i = 0; i < sections.Length; i++)
                {
                    Assert(entered[i].StartsWith(sections[i] + "|", StringComparison.Ordinal),
                        "section order mismatch at " + i + ": " + entered[i]);
                }
                Assert(entered[0] == "unity|UnityLogBridgePatch.Initialize",
                    "unity action identity changed: " + entered[0]);
                Assert(entered[1].StartsWith("nativeSns|", StringComparison.Ordinal)
                    && entered[1].Contains(".<InitializeDiagnosticStage>b__"),
                    "nativeSns action must stay the InitializeDiagnosticStage lambda: " + entered[1]);
                Assert(entered[2].StartsWith("bug1Probe|", StringComparison.Ordinal)
                    && entered[2].Contains(".<InitializeDiagnosticStage>b__"),
                    "bug1Probe action must stay the InitializeDiagnosticStage lambda: " + entered[2]);
                Assert(entered[3] == "bug1ProbeControl|Bug1ProbeControl.Initialize",
                    "bug1ProbeControl action identity changed: " + entered[3]);
                Assert(entered[4] == "gate|Bug1GateProbe.Initialize",
                    "gate action identity changed: " + entered[4]);

                foreach (string section in sections)
                {
                    entered.Clear();
                    throwSection = section;
                    Assert(!(bool)stage.Invoke(plugin, null),
                        "production mode must return false when section '" + section + "' throws");
                }
                throwSection = null;
            }
            finally
            {
                SteamP2PFriendsPlugin.ResetDiagnosticsStageForTest();
                SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = null;
            }

            return true;
        }

        /// <summary>
        /// R31-01：生产分支（seam == null）必须真的进入唯一执行点五次。
        /// 本用例不注入任何 seam：真实动作体在测试宿主里可能抛异常（Unity/BepInEx 不存在），
        /// 由既有 catch 折成 ok=false，但「进入执行点」的计数不受影响；因此只断言计数==5，
        /// 不断言返回值。N2 变异（唯一调用点改成 if (seam != null) a();）会让计数为 0 → 红。
        /// 注意：这只证明「以正确顺序进入执行点」，真实动作体的实际效果仍是 UNKNOWN。
        /// </summary>
        internal static bool Test_DiagnosticStageProductionBranchEntersAllSections()
        {
            MethodInfo stage = AccessTools.Method(
                typeof(SteamP2PFriendsPlugin), "InitializeDiagnosticStage", Type.EmptyTypes);
            Assert(stage != null, "InitializeDiagnosticStage not found");
            object plugin = FormatterServices.GetUninitializedObject(typeof(SteamP2PFriendsPlugin));
            try
            {
                SteamP2PFriendsPlugin.ResetDiagnosticsStageForTest();
                SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = null;
                SteamP2PFriendsPlugin.DiagnosticStageActionForTest = null;
                try
                {
                    stage.Invoke(plugin, null);
                }
                catch (TargetInvocationException)
                {
                    // 真实动作体在测试宿主抛异常是预期内；计数已在 a() 之前完成。
                }
                Assert(SteamP2PFriendsPlugin.DiagnosticSectionsEnteredForTest == 5,
                    "production branch (seam == null) must enter all five sections, got "
                    + SteamP2PFriendsPlugin.DiagnosticSectionsEnteredForTest);
            }
            finally
            {
                SteamP2PFriendsPlugin.ResetDiagnosticsStageForTest();
            }

            // t148-F1：wrapper 必须无条件调用委托 —— 这正是 M6（把 counted(); 改成
            // if (seam != null) counted();）要破坏的性质。票面建议的两条断言单独都不够：
            // M6 引用外层局部 seam 时，编译器把 seam 提升为 display-class 字段，
            // 对 DiagnosticStageActionForTest 的字段读取仍是 0、Action.Invoke 仍是 1；
            // 因此这里追加「wrapper 零分支」断言，从语义上钉住「无条件调用」。
            MethodInfo wrapper = FindDiagnosticWrapper();
            Assert(wrapper != null,
                "RunDiagnosticSectionForTest wrapper not found (compiler pattern changed)");
            int seamLoads = IlContractProbe.CountFieldLoads(
                wrapper, typeof(SteamP2PFriendsPlugin).FullName, "DiagnosticStageActionForTest");
            Assert(seamLoads == 0,
                "wrapper must not read DiagnosticStageActionForTest, field loads=" + seamLoads);
            int invokes = IlContractProbe.CountMethodCalls(
                wrapper, typeof(System.Action).FullName, "Invoke");
            Assert(invokes == 1,
                "wrapper must invoke the wrapped Action exactly once, calls=" + invokes);
            int branches = CountBranches(wrapper);
            Assert(branches == 0,
                "wrapper must be branch-free (unconditional delegate call); branches=" + branches);

            // 计数自增必须在 Action.Invoke 之前；M3（先 counted() 再自增）会让顺序断言红。
            int incrementIndex = -1;
            int invokeIndex = -1;
            int instructionIndex = 0;
            foreach (GunshotIlScan.IlInstruction instruction in GunshotIlScan.Read(wrapper))
            {
                if (instruction.OpCode == OpCodes.Stsfld &&
                    instruction.Operand is FieldInfo incremented &&
                    incremented.DeclaringType == typeof(SteamP2PFriendsPlugin) &&
                    incremented.Name == "DiagnosticSectionsEnteredForTest" &&
                    incrementIndex < 0)
                {
                    incrementIndex = instructionIndex;
                }
                if (instruction.OpCode.FlowControl == FlowControl.Call &&
                    instruction.Operand is MethodBase callee &&
                    callee.DeclaringType == typeof(System.Action) &&
                    callee.Name == "Invoke" &&
                    invokeIndex < 0)
                {
                    invokeIndex = instructionIndex;
                }
                instructionIndex++;
            }
            Assert(incrementIndex >= 0,
                "wrapper must increment DiagnosticSectionsEnteredForTest");
            Assert(invokeIndex >= 0, "wrapper must invoke the wrapped Action");
            Assert(incrementIndex < invokeIndex,
                "wrapper must increment before invoking (increment idx=" + incrementIndex
                + ", invoke idx=" + invokeIndex + ")");

            return true;
        }

        /// <summary>
        /// t148-F2：RunDiagnosticSectionForTest 的 a == null 守卫必须可失败。
        /// 注入返回 null 的 seam 后：stage 返回 false、计数保持 0（守卫在包装前抛出），
        /// 且 IL 中保留可读的守卫消息。把守卫改成 if (false &amp;&amp; a == null) 时
        /// 包装 delegate 会先自增再 NullReference → 计数为 1 → 本用例红。
        /// </summary>
        internal static bool Test_DiagnosticStageNullSeamGuard()
        {
            MethodInfo stage = AccessTools.Method(
                typeof(SteamP2PFriendsPlugin), "InitializeDiagnosticStage", Type.EmptyTypes);
            Assert(stage != null, "InitializeDiagnosticStage not found");
            object plugin = FormatterServices.GetUninitializedObject(typeof(SteamP2PFriendsPlugin));
            try
            {
                SteamP2PFriendsPlugin.ResetDiagnosticsStageForTest();
                SteamP2PFriendsPlugin.GunshotModuleInitializerForTest = null;
                SteamP2PFriendsPlugin.DiagnosticStageActionForTest = (section, real) => null;
                bool result = true;
                try
                {
                    result = (bool)stage.Invoke(plugin, null);
                }
                catch (TargetInvocationException ex)
                {
                    Assert(false, "stage must not throw when seam returns null, got " + ex.InnerException);
                }
                Assert(!result, "stage must return false when the injected seam returns null");
                Assert(SteamP2PFriendsPlugin.DiagnosticSectionsEnteredForTest == 0,
                    "null action must not be counted as entered, got "
                    + SteamP2PFriendsPlugin.DiagnosticSectionsEnteredForTest);

                MethodInfo runner = AccessTools.Method(
                    typeof(SteamP2PFriendsPlugin), "RunDiagnosticSectionForTest",
                    new[] { typeof(string), typeof(System.Action) });
                Assert(runner != null, "RunDiagnosticSectionForTest not found");
                Assert(MethodContainsString(runner, "diagnostic stage action is null for section: "),
                    "null-guard message missing from RunDiagnosticSectionForTest IL");
            }
            finally
            {
                SteamP2PFriendsPlugin.ResetDiagnosticsStageForTest();
            }

            return true;
        }

        /// 1) SDK 字段仍存在且类型是 AudioSource；2) 生产 GetGunshotAudioSource 能从实例读回该字段。
        /// 变异「GetGunshotAudioSource 恒 null」会让第 2 步红；SDK 字段改名会让第 1 步红。
        /// </summary>
        internal static bool Test_AudioSourceReflectionContract()
        {
            FieldInfo field = typeof(UseableGun).GetField(
                "gunshotAudioSource", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(field != null, "UseableGun.gunshotAudioSource field not found (SDK rename?)");
            Assert(field.FieldType == typeof(AudioSource),
                "gunshotAudioSource must be AudioSource, got " + field.FieldType.FullName);

            GunshotHostAudioRuntime.ResetForTest();
            UseableGun gun = NewGun(TestNetId);
            // UseableGun 继承链最终是 UnityEngine.Object：FormatterServices 造出的实例
            // m_CachedPtr=0，会被 Unity 的 op_Equality/op_Inequality 判成 fake-null。
            // 生产 GetGunshotAudioSource 用 `gun == null`（Unity 运算符）判空，因此这里必须
            // 注入非零 m_CachedPtr，才能走到真实的字段读取路径（与 Bug3-50 的 SteamChannel 同理）。
            Type unityObjectType = typeof(UseableGun);
            while (unityObjectType != null && unityObjectType.FullName != "UnityEngine.Object")
            {
                unityObjectType = unityObjectType.BaseType;
            }
            Assert(unityObjectType != null, "UnityEngine.Object base type not found");
            SetInstanceField(unityObjectType, gun, "m_CachedPtr", new IntPtr(1));

            var source = (AudioSource)FormatterServices.GetUninitializedObject(typeof(AudioSource));
            SetInstanceField(typeof(UseableGun), gun, "gunshotAudioSource", source);

            // 测试侧读回：确认 SetInstanceField 真的写进了字段（ldfld 不触发 UseableGun cctor）。
            var reader = new DynamicMethod(
                "ReadGunshotAudioSourceForTest",
                typeof(AudioSource),
                new[] { typeof(UseableGun) },
                typeof(GunshotHostAudioTests).Module,
                true);
            ILGenerator readerIl = reader.GetILGenerator();
            readerIl.Emit(OpCodes.Ldarg_0);
            readerIl.Emit(OpCodes.Ldfld, field);
            readerIl.Emit(OpCodes.Ret);
            object readBack = reader.Invoke(null, new object[] { gun });
            Assert(ReferenceEquals(readBack, source),
                "test-side readback failed: SetInstanceField did not write gunshotAudioSource");

            FieldInfo viaAccessTools = AccessTools.Field(typeof(UseableGun), "gunshotAudioSource");
            Assert(viaAccessTools != null, "AccessTools.Field returned null for gunshotAudioSource");
            Assert(viaAccessTools == field, "AccessTools.Field returned a different FieldInfo");

            MethodInfo getter = AccessTools.Method(
                typeof(GunshotHostAudioRuntime), "GetGunshotAudioSource", new[] { typeof(UseableGun) });
            Assert(getter != null, "GetGunshotAudioSource not found");
            object resolved = getter.Invoke(null, new object[] { gun });
            Assert(ReferenceEquals(resolved, source),
                "GetGunshotAudioSource must resolve the SDK field"
                + "（恒 null 变异必须让本用例红）");
            return true;
        }

        /// <summary>
        /// R27-02：AudioSourcePresent 必须来自「枪 + 可用 AudioSource」的单一判定函数。
        /// Collect 接缝把音频源探测置 null 时必须 false 且门控拒绝；
        /// 变异把接线放宽成 `gun != null` 会让本用例红。
        /// </summary>
        internal static bool Test_AudioSourcePresentRequiresResolvedSource()
        {
            GunshotHostAudioRuntime.ResetForTest();

            UseableGun gun = NewGun(TestNetId);
            Type unityObjectType = typeof(UseableGun);
            while (unityObjectType != null && unityObjectType.FullName != "UnityEngine.Object")
            {
                unityObjectType = unityObjectType.BaseType;
            }
            Assert(unityObjectType != null, "UnityEngine.Object base type not found");
            SetInstanceField(unityObjectType, gun, "m_CachedPtr", new IntPtr(1));

            // Collect 会读 gun.player.equipment.asset；给假枪补最小可读链，避免异常路径吞掉事实。
            var player = (Player)FormatterServices.GetUninitializedObject(typeof(Player));
            var equipment = (PlayerEquipment)FormatterServices.GetUninitializedObject(typeof(PlayerEquipment));
            SetInstanceField(typeof(Player), player, "_equipment", equipment);
            SetInstanceField(typeof(PlayerCaller), gun, "_player", player);

            GunshotHostAudioRuntime.AudioSourceProbeForTest = _ => null;
            GunshotGateFacts withoutSource = GunshotHostAudioRuntime.Collect(gun);
            Assert(string.IsNullOrEmpty(withoutSource.CollectionError),
                "Collect must not fail: " + withoutSource.CollectionError);
            Assert(!withoutSource.AudioSourcePresent,
                "AudioSourcePresent must be false when no playable source"
                + "（接线被放宽成 gun != null 时必须红）");
            Assert(!GunshotHostAudioPolicy.IsEligible(withoutSource),
                "missing audio source must reject the gate");

            var source = (AudioSource)FormatterServices.GetUninitializedObject(typeof(AudioSource));
            // 同理：AudioSource 也是 UnityEngine.Object，m_CachedPtr=0 会被 Unity 的
            // op_Inequality 判成 fake-null，HasGunshotAudioSource 的 `!= null` 会误判为 false。
            SetInstanceField(unityObjectType, source, "m_CachedPtr", new IntPtr(1));
            GunshotHostAudioRuntime.AudioSourceProbeForTest = _ => source;
            GunshotGateFacts withSource = GunshotHostAudioRuntime.Collect(gun);
            Assert(string.IsNullOrEmpty(withSource.CollectionError),
                "Collect with source must not fail: " + withSource.CollectionError);
            Assert(withSource.AudioSourcePresent,
                "resolved AudioSource must set AudioSourcePresent=true");

            GunshotHostAudioRuntime.AudioSourceProbeForTest = null;
            return true;
        }

        /// <summary>
        /// R27-03：SessionEpoch 的异常降级必须可观测；变异「catch 静默 return 1」会让本用例红。
        /// </summary>
        internal static bool Test_SessionEpochFallbackObservable()
        {
            GunshotHostAudioRuntime.ResetForTest();
            MethodInfo epochMethod = AccessTools.Method(
                typeof(GunshotHostAudioRuntime), "SessionEpoch", Type.EmptyTypes);
            Assert(epochMethod != null, "SessionEpoch not found");

            GunshotHostAudioRuntime.SessionIdProviderForTest = () => "session-abc";
            ulong normal = (ulong)epochMethod.Invoke(null, null);
            Assert(normal != 1UL, "normal session id must not collapse to 1");
            Assert(GunshotHostAudioRuntime.SessionEpochFallbackCount == 0,
                "normal path must not mark fallback, count="
                + GunshotHostAudioRuntime.SessionEpochFallbackCount);

            GunshotHostAudioRuntime.SessionIdProviderForTest = () =>
            {
                throw new InvalidOperationException("boom");
            };
            ulong fallback = (ulong)epochMethod.Invoke(null, null);
            Assert(fallback == 1UL, "fallback epoch must be 1, got " + fallback);
            Assert(GunshotHostAudioRuntime.SessionEpochFallbackCount == 1,
                "fallback must be observable, count="
                + GunshotHostAudioRuntime.SessionEpochFallbackCount);
            Assert(GunshotHostAudioRuntime.SessionEpochFallbackReason == "InvalidOperationException",
                "fallback reason=" + GunshotHostAudioRuntime.SessionEpochFallbackReason);

            GunshotHostAudioRuntime.SessionIdProviderForTest = null;
            return true;
        }

        internal static bool Test_AbortedAfterAnchorMarked()
        {
            int leafCalls = 0;
            UseableGun gun = NewGun(TestNetId);
            Begin(true, AllTrue(), _ => leafCalls++);

            GunshotHostAudioRuntime.OnFireEnter(gun);
            GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
            GunshotHostAudioRuntime.OnFireExit(gun, true);

            Assert(leafCalls == 1, "leaf must be invoked once before abort");
            Assert(GunshotHostAudioRuntime.AbortedAfterAnchorCount == 1,
                "aborted-after-anchor count=" + GunshotHostAudioRuntime.AbortedAfterAnchorCount);
            Assert(GunshotHostAudioRuntime.SuccessfulShotCount == 0,
                "successful shot count must stay 0 on abort");
            Assert(GunshotHostAudioRuntime.LastGateLine.Contains("decision=AbortedAfterAnchor"),
                "abort must be marked in gate line: " + GunshotHostAudioRuntime.LastGateLine);
            return true;
        }

        internal static bool Test_LedgerPurgeByGeneration()
        {
            GunshotHostAudioLedger.Reset();
            var oldKey = new GunshotFireKey(11UL, 1U, 1U, 7U, 1L);
            var newKey = new GunshotFireKey(11UL, 2U, 1U, 7U, 2L);

            Assert(GunshotHostAudioLedger.TryClaim(oldKey), "claim old generation key");
            Assert(GunshotHostAudioLedger.PurgeExcept(11UL, 2U) == 1, "old generation key must be purged");
            Assert(GunshotHostAudioLedger.StateOf(oldKey) == GunshotAttemptState.Unclaimed,
                "old key must be removed from ledger");

            Assert(GunshotHostAudioLedger.TryClaim(newKey), "claim current generation key");
            Assert(!GunshotHostAudioLedger.TryClaim(newKey), "current generation dedup must stay intact");

            for (int i = 0; i < GunshotHostAudioLedger.MaxEntries + 16; i++)
            {
                GunshotHostAudioLedger.TryClaim(new GunshotFireKey(11UL, 2U, 1U, 7U, 100L + i));
            }

            Assert(GunshotHostAudioLedger.Count <= GunshotHostAudioLedger.MaxEntries,
                "ledger must stay bounded, count=" + GunshotHostAudioLedger.Count);
            GunshotHostAudioLedger.Reset();
            return true;
        }

        internal static bool Test_RealFireTranspilerInsertion()
        {
            MethodInfo fire = AccessTools.Method(typeof(UseableGun), "fire", Type.EmptyTypes);
            Assert(fire != null, "UseableGun.fire not found");
            List<GunshotIlScan.IlInstruction> source = GunshotIlScan.Read(fire);
            Assert(source != null && source.Count > 0, "fire IL empty");
            Assert(GunshotHostAudioPatch.TryLocateAnchor(source, out int anchor, out string detail),
                "anchor not located: " + detail);

            var codes = new List<CodeInstruction>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                codes.Add(new CodeInstruction(source[i].OpCode, source[i].Operand));
            }

            // GunshotIlScan 对 br/br.s 的 Operand 已是绝对目标 offset（ReadOperand 返回 position+delta），
            // switch 的 Operand 是绝对目标数组。逐分支验证目标落在指令边界上。
            for (int i = 0; i < source.Count; i++)
            {
                int[] switchTargets = source[i].Operand as int[];
                if (switchTargets != null)
                {
                    for (int t = 0; t < switchTargets.Length; t++)
                    {
                        Assert(IndexOfOffset(source, switchTargets[t]) >= 0,
                            "switch target not on instruction boundary at idx=" + i
                            + " target=0x" + switchTargets[t].ToString("X4"));
                    }
                    continue;
                }

                if (!IsBranch(source[i].OpCode))
                {
                    continue;
                }

                int targetOffset = Convert.ToInt32(source[i].Operand);
                Assert(IndexOfOffset(source, targetOffset) >= 0,
                    "branch target not on instruction boundary at idx=" + i
                    + " op=" + source[i].OpCode.Name + " off=0x" + source[i].Offset.ToString("X4")
                    + " target=0x" + targetOffset.ToString("X4"));
            }

            MethodInfo compensation = AccessTools.Method(
                typeof(GunshotHostAudioRuntime), nameof(GunshotHostAudioRuntime.TryCompensateAfterSend));
            var patched = new List<CodeInstruction>(GunshotHostAudioPatch.Fire_Transpiler(codes, null));
            Assert(GunshotHostAudioPatch.TranspilerInsertions == 2,
                "transpiler insertions=" + GunshotHostAudioPatch.TranspilerInsertions + " expected=2");
            Assert(patched.Count == codes.Count + 2, "patched count mismatch");
            Assert(patched[anchor + 1].opcode == OpCodes.Ldarg_0, "inserted instruction 1 must be ldarg.0");
            Assert(Equals(patched[anchor + 2].operand, compensation),
                "inserted instruction 2 must call TryCompensateAfterSend");
            return true;
        }

        private static bool IsBranch(OpCode opcode)
        {
            return opcode == OpCodes.Br || opcode == OpCodes.Br_S
                || opcode == OpCodes.Brfalse || opcode == OpCodes.Brfalse_S
                || opcode == OpCodes.Brtrue || opcode == OpCodes.Brtrue_S
                || opcode == OpCodes.Beq || opcode == OpCodes.Beq_S
                || opcode == OpCodes.Bge || opcode == OpCodes.Bge_S
                || opcode == OpCodes.Bgt || opcode == OpCodes.Bgt_S
                || opcode == OpCodes.Ble || opcode == OpCodes.Ble_S
                || opcode == OpCodes.Blt || opcode == OpCodes.Blt_S
                || opcode == OpCodes.Bne_Un || opcode == OpCodes.Bne_Un_S
                || opcode == OpCodes.Bge_Un || opcode == OpCodes.Bge_Un_S
                || opcode == OpCodes.Bgt_Un || opcode == OpCodes.Bgt_Un_S
                || opcode == OpCodes.Ble_Un || opcode == OpCodes.Ble_Un_S
                || opcode == OpCodes.Blt_Un || opcode == OpCodes.Blt_Un_S
                || opcode == OpCodes.Leave || opcode == OpCodes.Leave_S;
        }

        private static int InstructionSize(OpCode opcode)
        {
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone:
                    return 1;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 2;
                case OperandType.InlineVar:
                    return 3;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 9;
                case OperandType.InlineSwitch:
                    return 1;
                default:
                    return 5;
            }
        }

        private static int IndexOfOffset(List<GunshotIlScan.IlInstruction> source, int offset)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i].Offset == offset)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool NegativeNoLeaf(Func<GunshotGateFacts, GunshotGateFacts> mutate, string expectedToken)
        {
            GunshotGateFacts facts = mutate(AllTrue());
            int leafCalls = 0;
            UseableGun gun = NewGun(TestNetId);
            Begin(true, facts, _ => leafCalls++);

            GunshotHostAudioRuntime.OnFireEnter(gun);
            GunshotHostAudioRuntime.TryCompensateAfterSend(gun);
            GunshotHostAudioRuntime.OnFireExit(gun, false);

            Assert(leafCalls == 0, "negative control must not compensate");
            Assert(GunshotHostAudioRuntime.LastGateLine.Contains(expectedToken),
                "gate line must show " + expectedToken + ": " + GunshotHostAudioRuntime.LastGateLine);
            Assert(GunshotHostAudioRuntime.LastGateLine.Contains("decision=NotEligible"),
                "gate decision must be NotEligible: " + GunshotHostAudioRuntime.LastGateLine);
            return true;
        }

        private static void Begin(bool enabled, GunshotGateFacts facts, Action<UseableGun> leaf)
        {
            GunshotHostAudioRuntime.ResetForTest();
            GunshotHostAudioPatch.ResetForTest();
            GunshotHostAudioRuntime.SetEnabled(enabled);
            GunshotHostAudioRuntime.SetNativeObserverInstalled(true);
            GunshotHostAudioRuntime.FactsProviderForTest = _ => facts;
            GunshotHostAudioRuntime.LeafInvokerForTest = leaf;
            // 测试宿主没有 UnityEngine.Random 的 ECall；默认空替身让补偿路径可走，
            // RNG 恢复用例会在 Begin 之后覆盖这两个缝。
            GunshotHostAudioRuntime.RandomStateSaveForTest = () => null;
            GunshotHostAudioRuntime.RandomStateRestoreForTest = _ => { };
        }

        private static GunshotGateFacts AllTrue()
        {
            return new GunshotGateFacts
            {
                P2PHostMode = true,
                ListenHostActive = true,
                NotDedicated = true,
                Connected = true,
                ServerActive = true,
                LevelLoaded = true,
                ShooterNotLocal = true,
                RealSendAnchor = true,
                ProjectileNull = true,
                TurretFalse = true,
                WeaponInstanceValid = true,
                AudioSourcePresent = true,
                ClipPresent = true,
                NativeLeafNotEntered = true,
                AssociationComplete = true,
            };
        }

        private static UseableGun NewGun(uint netId)
        {
            var gun = (UseableGun)FormatterServices.GetUninitializedObject(typeof(UseableGun));
            SetInstanceField(typeof(PlayerCaller), gun, "_netId", new NetId(netId));
            return gun;
        }

        private static void SetInstanceField(Type declaringType, object target, string fieldName, object value)
        {
            FieldInfo field = declaringType.GetField(
                fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert(field != null, "field not found: " + declaringType.Name + "." + fieldName);

            var setter = new DynamicMethod(
                "Set_" + declaringType.Name + "_" + fieldName,
                typeof(void),
                new[] { typeof(object), typeof(object) },
                typeof(GunshotHostAudioTests).Module,
                true);
            ILGenerator il = setter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, declaringType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, field.FieldType);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);
            setter.Invoke(null, new object[] { target, value });
        }

        private static GunshotGateFacts Build(
            bool p2pHostMode, bool listenHostActive, bool dedicated, bool connected, bool serverActive,
            bool levelLoaded, bool shooterPresent, bool shooterChannelPresent, bool shooterChannelIsLocal,
            bool gunPresent, bool assetPresent, bool projectilePresent, bool isTurret, bool clipPresent,
            bool audioSourcePresent, bool realSendAnchor, bool nativeLeafNotEntered, bool associationComplete)
        {
            return GunshotHostAudioRuntime.BuildFactsFromState(
                p2pHostMode, listenHostActive, dedicated, connected, serverActive, levelLoaded,
                shooterPresent, shooterChannelPresent, shooterChannelIsLocal, gunPresent, assetPresent,
                projectilePresent, isTurret, clipPresent, audioSourcePresent, realSendAnchor,
                nativeLeafNotEntered, associationComplete);
        }

        private static int CountOurPostfixes(Harmony harmony, MethodInfo target)
        {
            HarmonyLib.Patches info = Harmony.GetPatchInfo(target);
            if (info == null || info.Postfixes == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < info.Postfixes.Count; i++)
            {
                HarmonyLib.Patch patch = info.Postfixes[i];
                if (patch != null && patch.owner == harmony.Id)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountPatches(Harmony harmony, MethodInfo target)
        {
            HarmonyLib.Patches info = Harmony.GetPatchInfo(target);
            if (info == null)
            {
                return 0;
            }

            int count = 0;
            count += CountOwned(info.Prefixes, harmony);
            count += CountOwned(info.Postfixes, harmony);
            count += CountOwned(info.Transpilers, harmony);
            count += CountOwned(info.Finalizers, harmony);
            return count;
        }

        private static int CountOwned(System.Collections.Generic.IList<HarmonyLib.Patch> patches, Harmony harmony)
        {
            if (patches == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < patches.Count; i++)
            {
                if (patches[i] != null && patches[i].owner == harmony.Id)
                {
                    count++;
                }
            }

            return count;
        }

        private static MethodInfo FindDiagnosticWrapper()
        {
            var pending = new System.Collections.Generic.Stack<Type>();
            pending.Push(typeof(SteamP2PFriendsPlugin));
            while (pending.Count > 0)
            {
                Type type = pending.Pop();
                foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    pending.Push(nested);
                }
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (method.Name.StartsWith("<RunDiagnosticSectionForTest>b__", StringComparison.Ordinal))
                    {
                        return method;
                    }
                }
            }
            return null;
        }

        private static int CountBranches(MethodBase method)
        {
            int count = 0;
            foreach (GunshotIlScan.IlInstruction instruction in GunshotIlScan.Read(method))
            {
                FlowControl flow = instruction.OpCode.FlowControl;
                if (flow == FlowControl.Branch || flow == FlowControl.Cond_Branch)
                {
                    count++;
                }
            }
            return count;
        }

        private static bool MethodContainsString(MethodBase method, string value)
        {
            foreach (GunshotIlScan.IlInstruction instruction in GunshotIlScan.Read(method))
            {
                if (instruction.Operand is string text &&
                    text.IndexOf(value, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }

    /// <summary>
    /// 测试替身：真实 fire()/playGunshot() 的 IL 复制在测试宿主会因 UnityEx 依赖失败，
    /// 因此用无害方法作为 Harmony patch 目标；安装/卸载/GetPatchInfo 回查仍是真实 Harmony。
    /// </summary>
    internal sealed class FakeGun : UseableGun
    {
        public void FakeFire()
        {
        }

        public void FakeLeaf()
        {
        }
    }
}
