//=============================================================================
// ArchitectBlockPiercerPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: After each of the Architect's attacks is blocked by players, deals 25% of the blocked amount as unblockable pierce damage to the blocking target.
// ZH: 每次建筑师攻击被玩家格挡后，对格挡目标造成被格挡伤害25%的无法格挡穿透伤害。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Act4Placeholder;

internal sealed class ArchitectBlockPiercerPower : PowerModel
{
	private static bool IsPoweredAttack(ValueProp props)
	{
		return props.HasFlag(ValueProp.Move) && !props.HasFlag(ValueProp.Unpowered);
	}

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override async Task BeforeAttack(AttackCommand command)
	{
		await Task.CompletedTask;
	}

	public override async Task AfterAttack(AttackCommand command)
	{
		if (command.Attacker != base.Owner || command.TargetSide == base.Owner.Side || !IsPoweredAttack(command.DamageProps))
		{
			return;
		}
		bool piercedAnyBlock = false;
		// EN: Group ALL results per target (don't pre-filter on BlockedDamage) so we can check
		//     whether the block was overwhelmed.  Pierce only fires when block fully absorbed the
		//     attack (unblockedDamage == 0); if the player already took direct HP loss the block
		//     already failed and no extra pierce is warranted.
		foreach (IGrouping<Creature, DamageResult> targetResults in command.Results
			.Where((DamageResult result) => result.Receiver != null && result.Receiver.Side != base.Owner.Side)
			.GroupBy((DamageResult result) => result.Receiver))
		{
			Creature hitTarget = targetResults.Key;
			if (hitTarget == null || !hitTarget.IsAlive)
			{
				continue;
			}
			int blockedThisAttack = targetResults.Sum((DamageResult result) => result.BlockedDamage);
			if (blockedThisAttack <= 0)
			{
				continue; // no block absorption at all → nothing to pierce
			}
			// If the block was overwhelmed the player already suffered unmitigated damage, no pierce.
			int unblockedThisAttack = targetResults.Sum((DamageResult result) => result.UnblockedDamage);
			if (unblockedThisAttack > 0)
			{
				continue;
			}
			int pierceDamage = Math.Max(1, (int)Math.Ceiling((decimal)blockedThisAttack * 0.25m));
			await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), hitTarget, pierceDamage, ValueProp.Unblockable | ValueProp.Unpowered, base.Owner, null);
			piercedAnyBlock = true;
		}
		if (piercedAnyBlock)
		{
			Flash();
		}
	}

	public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (side == base.Owner.Side)
		{
			await PowerCmd.TickDownDuration(this);
		}
	}
}
