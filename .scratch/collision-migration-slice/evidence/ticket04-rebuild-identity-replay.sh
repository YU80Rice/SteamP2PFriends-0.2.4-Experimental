#!/usr/bin/env bash
# 票 04 冻结后「两次 Release Rebuild 指纹一致」的可重放核对脚本。
#
# 用途：让任何人在同一工作树上重放两次构建并逐项比较身份取值，不依赖报告里的数字。
# 用法：bash .scratch/collision-migration-slice/evidence/ticket04-rebuild-identity-replay.sh
# 前提：MSBuild 18.9.1（Visual Studio Insiders）可用；工作树为票 04 冻结提交（9171641 及之后仅文档/证据增量）。
set -u
cd "$(dirname "$0")/../../.." || exit 1
MSB="/c/Program Files/Microsoft Visual Studio/18/Insiders/MSBuild/Current/Bin/MSBuild.exe"
PLUGIN="bin/Release/SteamP2PFriends.dll"
TESTS="WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe"

capture_run() {
  local tag="$1"
  "$MSB" SteamP2PFriends.csproj -t:Rebuild -p:Configuration=Release -m -v:minimal >/dev/null 2>&1
  local plugin_exit=$?
  "$MSB" WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -t:Rebuild -p:Configuration=Release -m -v:minimal >/dev/null 2>&1
  local tests_exit=$?
  echo "${tag} plugin_exit=${plugin_exit} tests_exit=${tests_exit}"
  echo "${tag} plugin_sha256=$(sha256sum "$PLUGIN" | cut -d' ' -f1)"
  echo "${tag} plugin_mvid=$(powershell -NoProfile -Command "[System.Reflection.Assembly]::LoadFrom((Resolve-Path '$PLUGIN')).ManifestModule.ModuleVersionId.Guid" 2>/dev/null | tail -1)"
  echo "${tag} plugin_size=$(stat -c%s "$PLUGIN")"
  echo "${tag} tests_sha256=$(sha256sum "$TESTS" | cut -d' ' -f1)"
  echo "${tag} tests_mvid=$(powershell -NoProfile -Command "[System.Reflection.Assembly]::LoadFrom((Resolve-Path '$TESTS')).ManifestModule.ModuleVersionId.Guid" 2>/dev/null | tail -1)"
  echo "${tag} tests_size=$(stat -c%s "$TESTS")"
}

capture_run RUN1 > /tmp/ticket04-run1.txt
capture_run RUN2 > /tmp/ticket04-run2.txt

cat /tmp/ticket04-run1.txt
cat /tmp/ticket04-run2.txt

echo "=== 逐项机械比较（Run 1 vs Run 2） ==="
paste <(sed 's/^RUN1 //' /tmp/ticket04-run1.txt) <(sed 's/^RUN2 //' /tmp/ticket04-run2.txt) |
  awk -F'\t' '{ judge = ($1==$2) ? "IDENTICAL" : "DIFFER"; print judge "  " $1 (judge=="DIFFER" ? "  |  " $2 : "") }'

echo "=== 独立核验工具 ==="
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-BuildFingerprintArtifact.ps1 2>&1 | tail -1
echo "=== 唯一入口测试 ==="
"./$TESTS" 2>&1 | tail -3
