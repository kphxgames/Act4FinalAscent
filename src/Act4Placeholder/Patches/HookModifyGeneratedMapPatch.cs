//=============================================================================
// HookModifyGeneratedMapPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches Hook.ModifyGeneratedMap to replace the Act 4 generated map with a ShortAct4Map when the Act 4 placeholder or Architect configuration is active.
// ZH: 补丁修改Hook.ModifyGeneratedMap，在第四幕配置激活时将生成的地图替换为自定义的ShortAct4Map（短版第四幕地图）。
//=============================================================================
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

[HarmonyPatch(typeof(Hook), "ModifyGeneratedMap")]
internal static class HookModifyGeneratedMapPatch
{
	private static void Postfix(IRunState runState, int actIndex, ref ActMap __result)
	{
		RunState val = runState as RunState;
		if (val != null && actIndex == 3 && (ModSupport.IsAct4Placeholder(val) || ModSupport.IsAct4ArchitectConfigured(val, actIndex)))
		{
			__result = new ShortAct4Map(val);
		}
	}
}
