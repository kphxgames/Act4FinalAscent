//=============================================================================
// LinkedShadowRegent.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Phase 4 Linked Shadow, Regent variant. On its BUFF turn: gains block AND
//     applies 1 temporary Weak to all players (half-strength vs Phase 3 ShadowRegent).
// ZH: 四阶段连结之影——摄政者变体。BUFF回合：获得格挡并对所有玩家施加1层临时虚弱。
//=============================================================================
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace Act4Placeholder;

public sealed class LinkedShadowRegent : Phase4LinkedShadow
{
	public override int BaseLinkedShadowHp => Act4Config.LinkedShadowRegentHp;
	protected override string StartingMoveStateId => "BUFF"; // buff → heavy → multi → loop
	protected override string ShadowVisualsPath => "res://scenes/creature_visuals/regent.tscn";
	// Positioned near the Architect (right side) → needs → arrow not ← arrow.
	protected override bool IsRightSideShadow => true;
	protected override int BaseMultiDamage => Act4Config.LinkedShadowRegentBaseMulti;
	protected override int MultiHits       => Act4Config.LinkedShadowRegentMultiHits;
	protected override int BaseHeavyDamage => Act4Config.LinkedShadowRegentBaseHeavy;

	// Show buff + defend + debuff icons on the buff turn.
	protected override AbstractIntent[] GetLinkedShadowBuffIntents()
		=> new AbstractIntent[] { new BuffIntent(), new DefendIntent(), new DebuffIntent(false) };

	// After gaining block, apply 1 temporary Weak to all players.
	protected override async Task OnLinkedShadowBuffAsync()
	{
		await PowerCmd.Apply<WeakPower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}
}
