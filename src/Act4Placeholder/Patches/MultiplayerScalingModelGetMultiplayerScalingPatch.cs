//=============================================================================
// MultiplayerScalingModelGetMultiplayerScalingPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches MultiplayerScalingModel.GetMultiplayerScaling to return custom Act 4-specific HP scaling values instead of the default Act 3 multiplayer formula.
// ZH: 补丁修改MultiplayerScalingModel.GetMultiplayerScaling，在第四幕中返回自定义多人HP缩放值，替代原版第三幕的多人缩放公式。
//=============================================================================
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Singleton;

namespace Act4Placeholder;

[HarmonyPatch(typeof(MultiplayerScalingModel), "GetMultiplayerScaling")]
internal static class MultiplayerScalingModelGetMultiplayerScalingPatch
{
	private static bool Prefix(EncounterModel? encounter, int actIndex, ref decimal __result)
	{
		if (actIndex != 3)
		{
			return true;
		}
		__result = Act4Config.MpPerPlayerScaling;
		return false;
	}
}
