//=============================================================================
// ShadowIronclad.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Shadow Champion variant of the Ironclad: gains Plating scaled by player count on its buff move, and applies Vulnerable to all players on its hex move.
// ZH: 铁甲战士的「影子冠军」变体：增益行动根据玩家数量获得护甲，诅咒行动对所有玩家施加易伤。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Act4Placeholder;

public sealed class ShadowIronclad : ArchitectShadowChampion
{
	protected override string ShadowVisualsPath => "res://scenes/creature_visuals/ironclad.tscn";

	protected override int BaseMultiDamage => 5;

	protected override int MultiHits => 5;

	protected override int BaseHeavyDamage => 16;

	protected override async Task BuffMove(IReadOnlyList<Creature> _)
	{
		await base.BuffMove(_);
		await PowerCmd.Apply<PlatingPower>(((MonsterModel)this).Creature, ((MonsterModel)this).CombatState.Players.Count * 20, ((MonsterModel)this).Creature, (CardModel)null, false);
	}

	protected override async Task HexMove(IReadOnlyList<Creature> targets)
	{
		if (Act4Settings.CursedBookChosen)
		{
			await HeavyMove(targets);
			return;
		}
		await PowerCmd.Apply<VulnerablePower>(CombatState.Players.Select(p => p.Creature), 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}
}
