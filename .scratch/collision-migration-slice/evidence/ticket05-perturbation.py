#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""票 05 扰动负控制驱动（可重放）。

逐个回退本票的修复/门禁，实测哪些门变红，再还原并复核回全绿。
用法：python .scratch/collision-migration-slice/evidence/ticket05-perturbation.py

每个扰动都先备份被改文件，跑完立即还原；脚本退出前（含中途异常）都会从备份还原，
因此不会把工作树留在扰动状态。扰动以「按行匹配」表达，避免嵌套转义带来的假失配。
"""
import io
import os
import shutil
import subprocess
import sys
import tempfile

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
MSB = r"C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe"
NL = chr(10)
COMPARATOR = "Adapters/Collision/CollisionShadowComparator.cs"
POLICY = "Adapters/Collision/CollisionDemandPolicy.cs"
COORDINATOR = "Core/ControlPlane/MultiObserverShadowCoordinator.cs"
PATCH = "Adapters/Collision/Patches/LevelObjectRemoteCollisionPatch.cs"
TOUCHED = [COMPARATOR, POLICY, COORDINATOR, PATCH]


def read(path):
    return io.open(os.path.join(REPO, path), encoding="utf-8", newline="").read()


def write(path, text):
    io.open(os.path.join(REPO, path), "w", encoding="utf-8", newline="").write(text)


def patch(find_text, replace_text, path, tag):
    text = read(path)
    if find_text not in text:
        raise RuntimeError("%s: pattern not found in %s" % (tag, path))
    write(path, text.replace(find_text, replace_text, 1))


def run_round(tag):
    for project, target in (("SteamP2PFriends.csproj", "Build"),
                            ("WhitelistTests/SteamP2PFriends.WhitelistTests.csproj", "Build")):
        result = subprocess.run([MSB, project, "-t:" + target, "-p:Configuration=Release", "-m", "-v:minimal"],
                                cwd=REPO, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if result.returncode != 0:
            print("%s BUILD-FAILED %s exit=%d" % (tag, project, result.returncode))
            return
    exe = os.path.join(REPO, "WhitelistTests", "bin", "Release", "SteamP2PFriends.WhitelistTests.exe")
    output = subprocess.run([exe], cwd=REPO, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    text = output.stdout.decode("utf-8", "replace")
    summary = [line for line in text.splitlines() if "Final Result" in line]
    print("%s %s" % (tag, summary[-1] if summary else "NO-RESULT"))
    for line in text.splitlines():
        if "FAIL [" in line:
            print("%s %s" % (tag, line.strip()))
    sys.stdout.flush()


HOST_BRANCH = ("            if (hostCovers)" + NL
               + "                return Difference(policy, region, ECollisionShadowDifferenceKind.HostAddedCoverage, ids, tokens," + NL
               + "                    \"host-local-player\");" + NL)
GUEST_BRANCH = ("            if (eligibleCovers)" + NL
                + "                return Difference(policy, region, ECollisionShadowDifferenceKind.CanonicalLifecycleStability," + NL
                + "                    ids, tokens, \"canonical-fact-claimant\");" + NL)
TEMPORAL_LOOP = ("            foreach (RegionKey region in SymmetricDifference(newRegions, previousRegions))\n"
                 "            {\n"
                 "                if (IsOutOfBounds(region, frame.WorldSize)) continue;\n"
                 "                CollisionShadowDifference temporal =\n"
                 "                    ClassifyTemporal(policy, frame, region, newRegions, previousRegions);\n"
                 "                if (temporal != null) differences.Add(temporal);\n"
                 "            }\n\n")
FOREIGN_GUARD = ("            if (policy.Domain != DomainIds.Collision || frame.Domain != policy.Domain)\n"
                 "            {\n")
LEGACY_VERDICT = ("                if (claim.State == ECollisionShadowLegacyState.Eligible)" + NL
                  + "                {" + NL
                  + "                    stillNeeded = true;" + NL)
PENDING_BRANCH = ("            if (pendingGuest)\n"
                  "                return Difference(policy, region, ECollisionShadowDifferenceKind.PendingGuestCoverageExit,\n"
                  "                    legacyIds, legacyTokens);\n")
DEPARTED_BRANCH = ("            if (anyLegacyClaimant)\n"
                   "                return Difference(policy, region, ECollisionShadowDifferenceKind.DepartedObserverCoverageExit,\n"
                   "                    legacyIds, legacyTokens);\n")
UNEXPLAINED_BRANCH = ("            return Difference(policy, region, ECollisionShadowDifferenceKind.UnexplainedLegacyCoverage,\n"
                      "                legacyIds, legacyTokens);\n")

PERTURBATIONS = [
    ("P1", "全部差异塌成预期差异（禁止差异不再失败闭合）", [
        (COMPARATOR, "                    return ECollisionShadowDisposition.Forbidden;",
         "                    return ECollisionShadowDisposition.Expected;"),
    ]),
    ("P2", "Host 覆盖不再单独归类（并入 canonical 生命周期稳定性）", [
        (COMPARATOR, HOST_BRANCH + GUEST_BRANCH, GUEST_BRANCH + HOST_BRANCH),
    ]),
    ("P3a", "只关「当前 canonical 认领者」判定", [
        (COMPARATOR, "                stillNeeded = true;", "                stillNeeded = false;"),
    ]),
    ("P3b", "只关「旧侧认领者」判定", [
        (COMPARATOR, LEGACY_VERDICT, LEGACY_VERDICT.replace("stillNeeded = true;", "stillNeeded = false;")),
    ]),
    ("P3c", "两条判定同时关", [
        (COMPARATOR, "                stillNeeded = true;", "                stillNeeded = false;"),
        (COMPARATOR, LEGACY_VERDICT, LEGACY_VERDICT.replace("stillNeeded = true;", "stillNeeded = false;")),
    ]),
    ("P4", "取消时间轴对照（抖动与错误释放不再判定）", [
        (COMPARATOR, TEMPORAL_LOOP, ""),
    ]),
    ("P5", "取消越界 Region Key 判定", [
        (COMPARATOR, "            AddOutOfBounds(policy, frame, newRegions, legacyRegions, previousRegions, differences);" + NL, ""),
    ]),
    # 注：最初写成 `if (false)` 被 warnaserror 的 CS0162（不可达代码）拦下、不可编译；
    # 改为把守卫条件改成在异域帧下不成立但仍可编译的形态。
    ("P6", "取消异域需求失败闭合（当作 Collision 继续比较）", [
        (COMPARATOR, "            if (policy.Domain != DomainIds.Collision || frame.Domain != policy.Domain)",
         "            if (policy.Domain != DomainIds.Collision && frame.Domain != DomainIds.Collision)"),
    ]),
    ("P7", "旧侧差异借用非零连接代次（归因代次不再区分来源）", [
        (COMPARATOR, "                AddClaimant(legacyIds, legacyTokens, claim.ObserverId, 0UL);",
         "                AddClaimant(legacyIds, legacyTokens, claim.ObserverId, 1UL);"),
    ]),
    ("P8", "影子路径开始读旧碰撞账本（把旧账本当权威）", [
        (COORDINATOR, "            if (_collisionDemandEngine == null || _collisionDemandPolicy == null) return;",
         "            LevelObjectCollisionAdapter.IsRemoteCollisionRequired(0, 0);" + NL
         + "            if (_collisionDemandEngine == null || _collisionDemandPolicy == null) return;"),
    ]),
    ("P9", "Collision 资格改为自行判断授权（不再沿用观察者事实）", [
        (POLICY, "                presence => presence.GameplayAuthorized);",
         "                presence => presence.GameplayAuthorized" + NL
         + "                    && !SteamP2PFriends.Security.P2PApprovalManager.IsPending(" + NL
         + "                        new Steamworks.CSteamID(presence.ObserverId)));"),
    ]),
    ("P10", "协调器不再把 Host 样本提交给投影（加本地玩家过滤）", [
        (COORDINATOR, "                    || !seen.Add(sample.ObserverId))" + NL
         + "                    continue;" + NL
         + "                var presence = new ObserverPresence(",
         "                    || !seen.Add(sample.ObserverId))" + NL
         + "                    continue;" + NL
         + "                if (sample.IsLocalPlayer) continue;" + NL
         + "                var presence = new ObserverPresence("),
    ]),
    ("P11", "只读快照顺手走旧刷新写入路径", [
        (PATCH, "            coveredRegions.Clear();",
         "            RebuildCoverageAndRefresh(\"shadow-snapshot\");" + NL + "            coveredRegions.Clear();"),
    ]),
    ("P12", "把暂缓态当成离开（原因词折叠，准入故障被误报成已离开）", [
        (COMPARATOR, "                case ECollisionShadowLegacyState.Deferred: return \"deferred-sample\";",
         "                case ECollisionShadowLegacyState.Deferred: return \"departed-observer\";"),
    ]),
    ("P13", "无法归因的旧覆盖不再失败闭合（塌成预期差异）", [
        (COMPARATOR, "            return Difference(policy, region, ECollisionShadowDifferenceKind.UnexplainedLegacyCoverage,",
         "            return Difference(policy, region, ECollisionShadowDifferenceKind.ResourceCoupledCoverageExit,"),
    ]),
    ("P14", "取消影子路径的确认离开清理（离开者需求永久残留）", [
        (COORDINATOR, "                    _collisionDemandEngine.RemoveObserver(_collisionDemandPolicy, observerId);" + NL, ""),
    ]),
    ("P15", "取消缺席移除资格门（无条件按缺席清理）", [
        (COORDINATOR, "            if (plan.AllowAbsenceRemoval)", "            if (true)"),
    ]),
    ("P16", "身份不可判定时不再把旧侧认领者按暂缓处理（仍然断言离开）", [
        (COORDINATOR, "                else if (state == ECollisionShadowLegacyState.Absent && !plan.AllowAbsenceRemoval)" + NL
         + "                {" + NL
         + "                    // 本拍存在身份不可读的记录：谁在场无法判定，因此也不能断言「离开」。" + NL
         + "                    state = ECollisionShadowLegacyState.Deferred;" + NL
         + "                }" + NL, ""),
    ]),
    ("P17", "取消暂缓认领的合成（暂缓者的保留区域会变成凭空需求）", [
        (COORDINATOR, "            foreach (ulong observerId in deferred)" + NL
         + "            {" + NL
         + "                if (seen.Contains(observerId)) continue;" + NL
         + "                ObserverPresence known;" + NL
         + "                if (!_collisionDemandEngine.Authority.TryGet(observerId, out known)) continue;" + NL
         + "                claims.Add(new CollisionShadowClaim(known, isHost: false, isDeferred: true));" + NL
         + "            }" + NL, ""),
    ]),
]


def main():
    backup = tempfile.mkdtemp(prefix="ticket05-perturb-")
    for path in TOUCHED:
        target = os.path.join(backup, path.replace("/", os.sep))
        os.makedirs(os.path.dirname(target), exist_ok=True)
        shutil.copyfile(os.path.join(REPO, path), target)

    def restore():
        for path in TOUCHED:
            shutil.copyfile(os.path.join(backup, path.replace("/", os.sep)), os.path.join(REPO, path))

    try:
        if not sys.argv[1:]:
            run_round("BASELINE")
        selected = set(sys.argv[1:])
        for tag, description, patches in PERTURBATIONS:
            if selected and tag not in selected:
                continue
            print("--- %s %s ---" % (tag, description))
            for path, find_text, replace_text in patches:
                patch(find_text, replace_text, path, tag)
            run_round(tag)
            restore()
        print("--- 还原后最终复核 ---")
        run_round("RESTORED")
    finally:
        restore()
    print("backup_dir=%s" % backup)


if __name__ == "__main__":
    main()
