//=============================================================================
// LinkedShadowSilent.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Phase 4 Linked Shadow, Silent variant. 4-hit assassin.
//     Applies 1 Poison to all players after each Heavy or Multi attack (nerfed vs Phase 3).
// ZH: 四阶段连结之影——潜行者变体。4连击刺客。
//     每次重击或多段攻击后对所有玩家施加1层毒素（弱化版，低于三阶段）。
//=============================================================================
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Act4Placeholder;

public sealed class LinkedShadowSilent : Phase4LinkedShadow
{
	public override int BaseLinkedShadowHp => Act4Config.LinkedShadowSilentHp;
	protected override string StartingMoveStateId => "HEAVY"; // heavy → multi → buff → loop (aligned with left-trio)
	protected override string ShadowVisualsPath => "res://scenes/creature_visuals/silent.tscn";
	protected override int BaseMultiDamage => Act4Config.LinkedShadowSilentBaseMulti;
	protected override int MultiHits       => Act4Config.LinkedShadowSilentMultiHits;
	protected override int BaseHeavyDamage => Act4Config.LinkedShadowSilentBaseHeavy;
	// 4-hit assassin: per-hit = half of 2-hit warriors; total multi = same (4×1 = 2×2).

	// 1 Poison to all players after heavy.
	protected override async Task AfterHeavyAttackAsync(AttackCommand _)
	{
		await PowerCmd.Apply<PoisonPower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}

	// 1 Poison to all players after multi.
	protected override async Task AfterMultiAttackAsync(AttackCommand _)
	{
		await PowerCmd.Apply<PoisonPower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}
}
