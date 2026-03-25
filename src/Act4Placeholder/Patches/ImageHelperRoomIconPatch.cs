//=============================================================================
// ImageHelperRoomIconPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches ImageHelper.GetRoomIconPath and GetRoomIconOutlinePath to return custom icon and outline image paths for the Architect boss encounter map node.
// ZH: 补丁修改ImageHelper.GetRoomIconPath和GetRoomIconOutlinePath，为建筑师Boss遭遇地图节点返回自定义图标和描边图像路径。
//=============================================================================
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace Act4Placeholder;

[HarmonyPatch(typeof(ImageHelper), nameof(ImageHelper.GetRoomIconPath))]
internal static class ImageHelperGetRoomIconPathPatch
{
	private static bool Prefix(MapPointType mapPointType, RoomType roomType, ModelId? modelId, ref string? __result)
	{
		if (modelId != null && modelId.Entry == "ACT4_ARCHITECT_BOSS_ENCOUNTER")
		{
			__result = "res://images/ui/run_history/act4_architect_boss_encounter.png";
			return false;
		}
		return true;
	}
}

[HarmonyPatch(typeof(ImageHelper), nameof(ImageHelper.GetRoomIconOutlinePath))]
internal static class ImageHelperGetRoomIconOutlinePathPatch
{
	private static bool Prefix(MapPointType mapPointType, RoomType roomType, ModelId? modelId, ref string? __result)
	{
		if (modelId != null && modelId.Entry == "ACT4_ARCHITECT_BOSS_ENCOUNTER")
		{
			__result = "res://images/ui/run_history/act4_architect_boss_encounter_outline.png";
			return false;
		}
		return true;
	}
}
