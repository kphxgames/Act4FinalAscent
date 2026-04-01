//=============================================================================
// ShadowRegent.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Shadow Champion variant of the Regent: applies Weak to all players on its buff move, and applies Weak plus Vulnerable together on its hex move.
// ZH: 摄政王的「影子冠军」变体：增益行动对所有玩家施加虚弱，诅咒行动同时施加虚弱与易伤。
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

public sealed class ShadowRegent : ArchitectShadowChampion
{
	protected override string ShadowVisualsPath => "res://scenes/creature_visuals/regent.tscn";

	protected override int BaseMultiDamage => 4;

	protected override int MultiHits => 6;

	protected override int BaseHeavyDamage => 15;

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
		await PowerCmd.Apply<WeakPower>(CombatState.Players.Select(p => p.Creature), 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}

	protected override async Task HexMove(IReadOnlyList<Creature> targets)
	{
		if (Act4Settings.CursedBookChosen)
		{
			await HeavyMove(targets);
			return;
		}
		await PowerCmd.Apply<WeakPower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		await PowerCmd.Apply<VulnerablePower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}
}
