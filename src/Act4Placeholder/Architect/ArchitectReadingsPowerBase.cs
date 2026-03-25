//=============================================================================
// ArchitectReadingsPowerBase.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Abstract base for the Architect's Readings powers; tracks how many cards of a specific type players play and grants the Architect vigor when a threshold is met. The threshold equals the number of players, capped at 4 (so solo = every 1, 2p = every 2, 3p = every 3, 4p+ = every 4).
// ZH: 建筑师「读取」系列能力的抽象基类；追踪玩家打出特定类型牌的次数，达到阈值时施加活力。阈值等于玩家数量（上限4）：单人每1张、双人每2张、三人每3张、四人及以上每4张。
//=============================================================================
using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Act4Placeholder;

internal abstract class ArchitectReadingsPowerBase : PowerModel
{
	protected abstract CardType WatchedCardType { get; }

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	private int PlayerCount => base.Owner?.CombatState?.Players.Count ?? 1;

	// EN: Trigger threshold = player count, capped at 4. Solo = 1, 2p = 2, 3p = 3, 4p+ = 4.
	// ZH: 触发阈值 = 玩家数量（上限4）。单人=1，双人=2，三人=3，四人及以上=4。
	private int GetTriggerThreshold() => Math.Min(4, Math.Max(1, PlayerCount));

	// EN: Show a per-player-count description so the tooltip always reflects the actual threshold.
	// ZH: 根据玩家数量显示对应提示，确保工具提示始终反映当前实际阈值。
	public override LocString Description
	{
		get
		{
			string suffix = PlayerCount switch
			{
				1 => ".description",
				2 => ".2pDescription",
				3 => ".3pDescription",
				_ => ".coopDescription" // 4+ players
			};
			return new LocString("powers", base.Id.Entry + suffix);
		}
	}

	protected override string SmartDescriptionLocKey => PlayerCount switch
	{
		1 => base.Id.Entry + ".smartDescription",
		2 => base.Id.Entry + ".2pSmartDescription",
		3 => base.Id.Entry + ".3pSmartDescription",
		_ => base.Id.Entry + ".coopSmartDescription" // 4+ players
	};

	private int _cardsSeen;

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (cardPlay.Card.Type != WatchedCardType)
		{
			return;
		}
		if (cardPlay.Card.Owner?.Creature == null || cardPlay.Card.Owner.Creature.Side == base.Owner.Side)
		{
			return;
		}
		_cardsSeen++;
		if (_cardsSeen < GetTriggerThreshold()) return;
		_cardsSeen = 0;
		Flash();
		await PowerCmd.Apply<VigorPower>(base.Owner, base.Amount, base.Owner, null);
	}

	public override async Task AfterAttack(AttackCommand command)
	{
		if (command.Attacker == base.Owner)
		{
			await PowerCmd.Remove(this);
		}
	}
}
