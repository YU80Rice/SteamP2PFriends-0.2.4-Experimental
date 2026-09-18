#!/usr/bin/env bash
# 票 05 取证：影子候选（ReadOnlyShadow）与正式切换候选（Cutover）必须是可区分的构建指纹。
#
# 同一份源码、只换候选角色，产物身份（SHA-256 / MVID / 默认 Case-ID / 程序集元数据）必须不同；
# 脚本最后把工作树还原成影子候选构建（本切片当前交付的候选），因为测试门禁要求 DLL 与
# Build/Version.props 的默认角色一致。
#
# 用法：bash .scratch/collision-migration-slice/evidence/ticket05-candidate-role-replay.sh
# 前提：MSBuild 18.9.1（Visual Studio Insiders）可用。
set -u
cd "$(dirname "$0")/../../.." || exit 1
MSB="/c/Program Files/Microsoft Visual Studio/18/Insiders/MSBuild/Current/Bin/MSBuild.exe"
DLL="bin/Release/SteamP2PFriends.dll"

identity() {
  local tag="$1"
  echo "${tag} sha256=$(sha256sum "$DLL" | cut -d' ' -f1)"
  echo "${tag} mvid=$(powershell -NoProfile -Command "[System.Reflection.Assembly]::LoadFile((Resolve-Path '$DLL')).ManifestModule.ModuleVersionId.Guid" 2>/dev/null | tail -1)"
  echo "${tag} size=$(stat -c%s "$DLL")"
  echo "${tag} role_metadata=$(powershell -NoProfile -Command "([System.Reflection.Assembly]::LoadFile((Resolve-Path '$DLL')).GetCustomAttributesData() | Where-Object { \$_.AttributeType.FullName -eq 'System.Reflection.AssemblyMetadataAttribute' -and \$_.ConstructorArguments[0].Value -eq 'SteamP2PFriendsCandidateRole' }).ConstructorArguments[1].Value" 2>/dev/null | tail -1)"
  echo "${tag} case_id_metadata=$(powershell -NoProfile -Command "([System.Reflection.Assembly]::LoadFile((Resolve-Path '$DLL')).GetCustomAttributesData() | Where-Object { \$_.AttributeType.FullName -eq 'System.Reflection.AssemblyMetadataAttribute' -and \$_.ConstructorArguments[0].Value -eq 'SteamP2PFriendsDefaultCaseId' }).ConstructorArguments[1].Value" 2>/dev/null | tail -1)"
}

"$MSB" SteamP2PFriends.csproj -t:Rebuild -p:Configuration=Release -p:SteamP2PFriendsCandidateRole=Cutover -m -v:minimal >/dev/null 2>&1
echo "cutover_build_exit=$?"
identity CUTOVER > /tmp/ticket05-cutover.txt
cat /tmp/ticket05-cutover.txt

"$MSB" SteamP2PFriends.csproj -t:Rebuild -p:Configuration=Release -m -v:minimal >/dev/null 2>&1
echo "shadow_build_exit=$?"
identity SHADOW > /tmp/ticket05-shadow.txt
cat /tmp/ticket05-shadow.txt

echo "=== 逐项机械比较（影子候选 vs 正式切换候选） ==="
paste <(sed 's/^CUTOVER //' /tmp/ticket05-cutover.txt) <(sed 's/^SHADOW //' /tmp/ticket05-shadow.txt) |
  awk -F'\t' '{ judge = ($1==$2) ? "IDENTICAL" : "DIFFER"; print judge "  " $1 (judge=="DIFFER" ? "  |  " $2 : "") }'

echo "=== 独立核验（对当前影子候选产物） ==="
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-BuildFingerprintArtifact.ps1 2>&1 | tail -1
