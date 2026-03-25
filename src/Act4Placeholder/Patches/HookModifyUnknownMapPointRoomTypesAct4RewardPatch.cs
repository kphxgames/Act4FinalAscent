//=============================================================================
// HookModifyUnknownMapPointRoomTypesAct4RewardPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches Hook.ModifyUnknownMapPointRoomTypes to mark Act 4 map rows 1 and 5 as event-only rooms, ensuring those nodes always resolve to the Act 4 reward events.
// ZH: 补丁修改Hook.ModifyUnknownMapPointRoomTypes，将第四幕地图第1行和第5行的未知房间强制设为纯事件房间，确保其始终指向第四幕奖励事件。
//=============================================================================
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

[HarmonyPatch(typeof(Hook), "ModifyUnknownMapPointRoomTypes")]
internal static class HookModifyUnknownMapPointRoomTypesAct4RewardPatch
{
	private static void Postfix(IRunState runState, ref IReadOnlySet<RoomType> __result)
	{
		RunState val = runState as RunState;
		if (val != null && ModSupport.IsAct4Placeholder(val) && val.CurrentMapCoord.HasValue)
		{
			int row = val.CurrentMapCoord.Value.row;
			if (row == 1 || row == 5 || row == 8)
			{
				HashSet<RoomType> obj = new HashSet<RoomType>();
				obj.Add((RoomType)6);
				__result = obj;
			}
		}
	}
}
