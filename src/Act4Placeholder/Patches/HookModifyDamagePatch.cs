//=============================================================================
// HookModifyDamagePatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches Hook.ModifyDamage to apply an Act 4 enemy damage multiplier, an additional 20% reduction on the Architect's own attacks, and a flat -1 reduction to minion damage.
// ZH: 补丁修改Hook.ModifyDamage，对第四幕所有敌方有效攻击应用伤害倍率，对建筑师自身攻击额外降低30%，并将召唤物伤害减少1点。
//=============================================================================
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace Act4Placeholder;

[HarmonyPatch(typeof(Hook), "ModifyDamage")]
internal static class HookModifyDamagePatch
{
	private static void Postfix(IRunState runState, Creature? dealer, ValueProp props, ref decimal __result)
	{
		RunState val = runState as RunState;
		bool isPoweredAttack = props.HasFlag((ValueProp)8) && !props.HasFlag((ValueProp)4);
		if (val == null || !ModSupport.IsAct4Placeholder(val) || dealer == null || (int)dealer.Side != 2 || !isPoweredAttack)
		{
			return;
		}
		__result *= ModSupport.GetAct4DamageMultiplier(val);
		// EN: Architect ×0.8 softener disabled, the global ×1.2 it was compensating for has also been removed.
		//     Re-enable both together if a global Act 4 hardness boost is reintroduced.
		// ZH: 建筑师×0.8减弱已禁用——其补偿的全局×1.2也已移除。如需重新引入全局强度加成，两者需同步开启。
		// if (dealer.Monster is Act4ArchitectBoss)
		// {
		// 	__result *= 0.8m;
		// }
		if (dealer.Monster is Guardbot || dealer.Monster is Noisebot || dealer.Monster is SpectralKnight || dealer.Monster is MagiKnight || dealer.Monster is ArchitectSummonedFogmog || dealer.Monster is ArchitectShadowChampion)
		{
			__result = Math.Max(0m, __result - 1m);
		}
		AbstractRoom currentRoom = val.CurrentRoom;
		CombatRoom val2 = currentRoom as CombatRoom;
		if (val2 != null && (int)((AbstractRoom)val2).RoomType == 2)
		{
			MapCoord? currentMapCoord = val.CurrentMapCoord;
			if (currentMapCoord.HasValue && currentMapCoord.GetValueOrDefault().row == 4)
			{
				__result = Math.Max(0m, __result - 6m);
			}
		}
		if (ModSupport.HelpWeakenEnemiesEnabled)
		{
			__result = Math.Max(0m, __result - 1m);
		}
		// Keep linked-shadow multi-hit intents meaningful after all flat reductions.
		// Phase 4 linked shadows should never display/resolve as 0-damage pings.
		if (dealer.Monster is Phase4LinkedShadow)
		{
			__result = Math.Max(2m, __result);
		}
		else if (dealer.Monster is ArchitectShadowChampion)
		{
			__result = Math.Max(1m, __result);
		}
	}
}

[HarmonyPatch(typeof(Hook), "AfterDamageGiven")]
internal static class HookAfterDamageGivenPatch
{
	private static void Postfix(CombatState combatState, Creature? dealer, DamageResult results, Creature target)
	{
		ModSupport.RecordRunDamageContribution(combatState, dealer, results, target);
	}
}
