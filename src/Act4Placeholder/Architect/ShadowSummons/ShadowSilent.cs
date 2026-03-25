//=============================================================================
// ShadowSilent.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Shadow Champion variant of the Silent: poisons all hit players after both its heavy attack and multi-attack, with higher poison stacks on the multi-attack.
// ZH: 刺客的「影子冠军」变体：重击和多段攻击命中后均对玩家施加中毒，多段攻击的中毒层数更高。
//=============================================================================
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Act4Placeholder;

public sealed class ShadowSilent : ArchitectShadowChampion
{
	protected override string ShadowVisualsPath => "res://scenes/creature_visuals/silent.tscn";

	protected override int BaseMultiDamage => 3;

	protected override int MultiHits => 10;

	protected override int BaseHeavyDamage => 15;

	protected override Task AfterHeavyAttackAsync(AttackCommand command)
	{
		return ApplyTotalPoisonAsync(command, 2m);
	}

	protected override Task AfterMultiAttackAsync(AttackCommand command)
	{
		return ApplyTotalPoisonAsync(command, 4m);
	}

	private async Task ApplyTotalPoisonAsync(AttackCommand command, decimal amount)
	{
		foreach (Creature creature in command.Results.Where((DamageResult result) => result.Receiver.IsPlayer && result.Receiver.IsAlive).Select((DamageResult result) => result.Receiver).Distinct())
		{
			await PowerCmd.Apply<PoisonPower>(creature, amount, base.Creature, null, false);
		}
	}
}
