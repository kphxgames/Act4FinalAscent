//=============================================================================
// Act4ArchitectBackgroundPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches EncounterModel.CreateBackground to force the Architect boss encounter to use its own dedicated background instead of the default combat background.
// ZH: 补丁修改EncounterModel.CreateBackground，强制建筑师Boss遭遇使用专属战斗背景而非默认背景。
//=============================================================================
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Act4Placeholder;

[HarmonyPatch(typeof(EncounterModel), nameof(EncounterModel.CreateBackground))]
internal static class Act4ArchitectBackgroundPatch
{
	private const string ArchitectEventBackgroundId = "the_architect_event_encounter";

	private static bool Prefix(EncounterModel __instance, ActModel parentAct, Rng rng, ref NCombatBackground __result)
	{
		if (__instance is not Act4ArchitectBossEncounter)
		{
			return true;
		}
		__result = NCombatBackground.Create(new BackgroundAssets(ArchitectEventBackgroundId, rng));
		return false;
	}
}
