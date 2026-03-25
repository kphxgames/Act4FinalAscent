//=============================================================================
// ShadowDefect.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Shadow Champion variant of the Defect: applies Vulnerable to all players on its buff move, and applies Frail plus gains Strength on its hex move.
// ZH: 赤焰机的「影子冠军」变体：增益行动对所有玩家施加易伤，诅咒行动施加虚弱并为自身获得力量。
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
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace Act4Placeholder;

public sealed class ShadowDefect : ArchitectShadowChampion
{
	protected override string ShadowVisualsPath => "res://scenes/creature_visuals/defect.tscn";

	protected override int BaseMultiDamage => 3;

	protected override int MultiHits => 8;

	protected override int BaseHeavyDamage => 16;

	protected override AbstractIntent[] GetBuffIntents()
	{
		return new AbstractIntent[2]
		{
			new BuffIntent(),
			new DebuffIntent(false)
		};
	}

	protected override async Task BuffMove(IReadOnlyList<Creature> _)
	{
		await base.BuffMove(_);
		await PowerCmd.Apply<VulnerablePower>(CombatState.Players.Select(p => p.Creature), 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}

	protected override async Task HexMove(IReadOnlyList<Creature> targets)
	{
		if (Act4Settings.CursedBookChosen)
		{
			await HeavyMove(targets);
			return;
		}
		await PowerCmd.Apply<FrailPower>(CombatState.Players.Select(p => p.Creature), 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
		await PowerCmd.Apply<StrengthPower>(((MonsterModel)this).Creature, 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}
}
