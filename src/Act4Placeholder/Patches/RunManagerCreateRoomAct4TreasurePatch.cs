//=============================================================================
// RunManagerCreateRoomAct4TreasurePatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches RunManager.CreateRoom to always spawn the Architect boss encounter at boss rooms in Act 4, and to use the Act 4 variant for treasure rooms.
// ZH: 补丁修改RunManager.CreateRoom，在第四幕Boss房间始终生成建筑师Boss战，宝藏房间使用第四幕专用版本。
//=============================================================================
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

[HarmonyPatch(typeof(RunManager), "CreateRoom")]
internal static class RunManagerCreateRoomAct4TreasurePatch
{
	private static bool Prefix(RunManager __instance, RoomType roomType, MapPointType mapPointType, ref AbstractRoom __result)
	{
		RunState? state = __instance.DebugOnlyGetState();
		bool isActiveAct4 = ModSupport.IsAct4Placeholder(state);
		bool hasArchitectFinalAct = ModSupport.IsAct4ArchitectConfigured(state, 3);
		if (!isActiveAct4 && !hasArchitectFinalAct)
		{
			return true;
		}
		if (roomType == RoomType.Boss || mapPointType == MapPointType.Boss)
		{
			__result = new CombatRoom(ModelDb.Encounter<Act4ArchitectBossEncounter>().ToMutable(), state);
			return false;
		}
		if (!isActiveAct4)
		{
			return true;
		}
		if (roomType != RoomType.Treasure)
		{
			return true;
		}
		__result = new TreasureRoom(2);
		return false;
	}
}
