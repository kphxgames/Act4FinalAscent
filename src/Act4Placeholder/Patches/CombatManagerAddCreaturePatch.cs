//=============================================================================
// CombatManagerAddCreaturePatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches CombatManager.AddCreature to apply Act 4 HP scaling when a monster is added, or admin combat stat buffs when the creature is a player character.
// ZH: 补丁修改CombatManager.AddCreature，添加敌方生物时缩放第四幕HP，添加玩家角色时施加管理员战斗属性加成。
//=============================================================================
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;

namespace Act4Placeholder;

[HarmonyPatch(typeof(CombatManager), "AddCreature")]
internal static class CombatManagerAddCreaturePatch
{
	private static void Postfix(Creature creature)
	{
		if (creature.IsEnemy)
		{
			ModSupport.ScaleAct4Enemy(creature);
		}
		else
		{
			TaskHelper.RunSafely(ModSupport.ApplyAdminCombatBonusAsync(creature));
		}
	}
}
