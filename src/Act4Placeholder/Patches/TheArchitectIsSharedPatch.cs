//=============================================================================
// TheArchitectIsSharedPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches EventModel.get_IsShared to force TheArchitect event to be treated as a shared multiplayer event in Act 3, ensuring both players see the Act 4 difficulty-choice options in co-op.
// ZH: 补丁修改EventModel.get_IsShared，在第三幕强制将建筑师事件标记为多人共享事件，确保联机时双方玩家均能看到第四幕难度选择选项。
//=============================================================================
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

[HarmonyPatch(typeof(EventModel), "get_IsShared")]
internal static class TheArchitectIsSharedPatch
{
	private static void Postfix(EventModel __instance, ref bool __result)
	{
		if (__instance is not TheArchitect)
			return;

		// Use RunManager state directly - the canonical EventModel (used by EventSynchronizer)
		// never has BeginEvent() called, so Owner is always null.  Checking Owner.RunState
		// caused the canonical event's IsShared to return false, breaking shared-event sync.
		RunState runState = RunManager.Instance?.DebugOnlyGetState();
		if (runState != null
			&& ((IReadOnlyCollection<Player>)runState.Players).Count > 1
			&& runState.CurrentActIndex == 2
			&& ((IReadOnlyCollection<ActModel>)runState.Acts).Count <= 3)
		{
			__result = true;
		}
	}
}
