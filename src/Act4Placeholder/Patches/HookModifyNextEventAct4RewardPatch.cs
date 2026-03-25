//=============================================================================
// HookModifyNextEventAct4RewardPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches Hook.ModifyNextEvent to force the Act4EmpyrealCache event at Act 4 map row 1, the Act4RoyalTreasury event at map row 5, and the Act4GrandLibraryEvent at map row 8 (pre-boss).
// ZH: 补丁修改Hook.ModifyNextEvent，在第四幕强制将地图第1行替换为帝国宝库事件，第5行替换为皇家金库事件，第8行（Boss前）替换为秘典馆事件。
//=============================================================================
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

[HarmonyPatch(typeof(Hook), "ModifyNextEvent")]
internal static class HookModifyNextEventAct4RewardPatch
{
	private static void Postfix(IRunState runState, ref EventModel __result)
	{
		RunState val = runState as RunState;
		if (val != null && ModSupport.IsAct4Placeholder(val) && val.CurrentMapCoord.HasValue)
		{
			MapCoord value = val.CurrentMapCoord.Value;
			if (value.row == 1)
			{
				__result = ModelDb.Event<Act4EmpyrealCache>();
			}
			else if (value.row == 5)
			{
				__result = ModelDb.Event<Act4RoyalTreasury>();
			}
			else if (value.row == 8)
			{
				__result = ModelDb.Event<Act4GrandLibraryEvent>();
			}
		}
	}
}
