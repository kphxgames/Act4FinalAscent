//=============================================================================
// ArchitectGuardbot.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches Guardbot.GuardMove so that Guardbots summoned in the Architect fight grant block to the Architect instead of their usual Fabricator-targeting behavior.
// ZH: 补丁修改Guardbot.GuardMove，使在建筑师战斗中召唤的守卫机器人为建筑师提供格挡，而非原本针对制造者的逻辑。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.ValueProps;

namespace Act4Placeholder;

/// <summary>
/// Patches Guardbot.GuardMove so that when summoned inside an Architect fight
/// the bot grants block to the Architect (4.5 % of his MaxHp) instead of
/// looking for a Fabricator that will never be present.
/// Normal Fabricator fights are completely unaffected.
/// </summary>
[HarmonyPatch(typeof(Guardbot), "GuardMove")]
internal static class GuardbotArchitectPatch
{
	[HarmonyPrefix]
	private static bool Prefix(Guardbot __instance, ref Task __result)
	{
		Creature guardCreature = ((MonsterModel)__instance).Creature;
		Creature architect = guardCreature?.CombatState?.Enemies
			.FirstOrDefault(c => c.Monster is Act4ArchitectBoss && c.IsAlive);

		if (architect == null)
		{
			// Not in an Architect fight - let the original Fabricator-targeting logic run.
			return true;
		}

		__result = ArchitectGuardMoveAsync(guardCreature, architect);
		return false; // skip original
	}

	private static async Task ArchitectGuardMoveAsync(Creature guard, Creature architect)
	{
		await CreatureCmd.TriggerAnim(guard, "Cast", 0.6f);
		int blockAmount = Math.Max(1, (int)Math.Ceiling(architect.MaxHp * 0.045m));
		await CreatureCmd.GainBlock(architect, (decimal)blockAmount, ValueProp.Unpowered, null);
	}
}
