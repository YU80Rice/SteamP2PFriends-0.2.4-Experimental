using Mono.Cecil;
using Mono.Cecil.Cil;
using SteamP2PFriends.Adapters.Collision;
using SteamP2PFriends.Adapters.Collision.Patches;
using System;
using System.Linq;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class RemoteCollisionAnimationPolicyTests
    {
        internal static bool Test_RC1_CullingPolicyIsSavedAndRestored()
        {
            using (ModuleDefinition module = ModuleDefinition.ReadModule(
                typeof(LevelObjectCollisionAdapter).Assembly.Location))
            {
                TypeDefinition type = module.Types.FirstOrDefault(item =>
                    item.FullName == typeof(LevelObjectCollisionAdapter).FullName);
                MethodDefinition acquire = FindMethod(type, "AcquireDoorAnimations");
                MethodDefinition restore = FindMethod(type, "RestoreRegionState");
                MethodDefinition revoke = FindMethod(type, "RevokeNativeOverride");
                return acquire != null && restore != null && revoke != null
                    && Calls(acquire, "UnityEngine.Animation", "set_cullingType")
                    && Calls(restore, "UnityEngine.Animation", "set_cullingType")
                    && Calls(revoke, "UnityEngine.Animation", "set_cullingType");
            }
        }

        internal static bool Test_RC2_CullingPolicyPrecedesRootActivation()
        {
            using (ModuleDefinition module = ModuleDefinition.ReadModule(
                typeof(LevelObjectCollisionAdapter).Assembly.Location))
            {
                TypeDefinition type = module.Types.FirstOrDefault(item =>
                    item.FullName == typeof(LevelObjectCollisionAdapter).FullName);
                MethodDefinition acquire = FindMethod(type, "AcquireLevelObject");
                MethodDefinition animation = FindMethod(type, "AcquireDoorAnimations");
                return acquire != null && animation != null
                    && Calls(acquire, type.FullName, "AcquireDoorAnimations")
                    && Calls(acquire, "UnityEngine.GameObject", "SetActive");
            }
        }

        private static TypeDefinition FindPatchType(ModuleDefinition module)
        {
            return module.Types.FirstOrDefault(type =>
                type.FullName == typeof(LevelObjectRemoteCollisionPatch).FullName);
        }

        private static MethodDefinition FindMethod(TypeDefinition type, string name)
        {
            return type?.Methods.FirstOrDefault(method => method.Name == name);
        }

        private static bool Calls(MethodDefinition method, string declaringType, string methodName)
        {
            return FindCallIndex(method, declaringType, methodName) >= 0;
        }

        private static int FindCallIndex(MethodDefinition method, string declaringType, string methodName)
        {
            if (method?.Body == null)
                return -1;

            for (int index = 0; index < method.Body.Instructions.Count; index++)
            {
                Instruction instruction = method.Body.Instructions[index];
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                    continue;
                if (!(instruction.Operand is MethodReference target))
                    continue;
                if (target.Name == methodName && target.DeclaringType.FullName.StartsWith(declaringType, StringComparison.Ordinal))
                    return index;
            }
            return -1;
        }
    }
}
