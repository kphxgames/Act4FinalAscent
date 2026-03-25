//=============================================================================
// CombatManagerAfterCreatureAddedPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches CombatManager.AfterCreatureAdded to apply Act 4 room-specific buff powers to enemy creatures after they are fully initialized in combat.
// ZH: 补丁修改CombatManager.AfterCreatureAdded，在敌方生物完全初始化后为其施加第四幕特定房间的Buff能力。
//=============================================================================
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Act4Placeholder;

[HarmonyPatch(typeof(CombatManager), "AfterCreatureAdded")]
internal static class CombatManagerAfterCreatureAddedPatch
{
	private static void Postfix(ref Task __result, Creature creature)
	{
		if (creature.IsEnemy)
		{
			__result = ApplyAct4BuffsAfterCreatureAddedAsync(__result, creature);
		}
		else if (creature.IsPlayer)
		{
			// EN: Apply the co-op weakest-contributor buff (+10 Str/+5 Dex) for all Act 4 battles.
			//     Runs on both machines deterministically, no desync risk.
			// ZH: 为联机最低输出玩家在整个第四幕的战斗中施加+10力量/+5敏捷。
			//     在双方机器上确定性执行，无同步风险。
			__result = ApplyAct4WeakestPlayerBuffAfterCreatureAddedAsync(__result, creature);
		}
	}

	private static async Task ApplyAct4BuffsAfterCreatureAddedAsync(Task originalTask, Creature creature)
	{
		await originalTask;
		await ModSupport.ApplyAct4EnemyRoomBuffsAsync(creature);
	}

	private static async Task ApplyAct4WeakestPlayerBuffAfterCreatureAddedAsync(Task originalTask, Creature creature)
	{
		await originalTask;
		await ModSupport.ApplyAct4WeakestPlayerBuffAsync(creature);
	}
}
