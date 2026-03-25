//=============================================================================
// ShadowNecrobinder.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Shadow Champion variant of the Necrobinder: applies PiercingWail to all players on its buff move, and applies Weak plus Vulnerable on its hex move.
// ZH: 亡灵缚者的「影子冠军」变体：增益行动对所有玩家施加穿刺哀鸣，诅咒行动施加虚弱与易伤。
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

public sealed class ShadowNecrobinder : ArchitectShadowChampion
{
	protected override string ShadowVisualsPath => "res://scenes/creature_visuals/necrobinder.tscn";

	protected override int BaseMultiDamage => 5;

	protected override int MultiHits => 5;

	protected override int BaseHeavyDamage => 18;

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
		await PowerCmd.Apply<PiercingWailPower>(CombatState.Players.Select(p => p.Creature), 8m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}

	protected override async Task HexMove(IReadOnlyList<Creature> targets)
	{
		if (Act4Settings.CursedBookChosen)
		{
			await HeavyMove(targets);
			return;
		}
		await PowerCmd.Apply<WeakPower>(CombatState.Players.Select(p => p.Creature), 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
		await PowerCmd.Apply<VulnerablePower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}
}
