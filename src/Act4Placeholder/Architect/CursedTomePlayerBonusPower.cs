//=============================================================================
// CursedTomePlayerBonusPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Applied once to each player at combat start when the Cursed Tome is chosen.
//     Permanently grants +2 max Energy and +2 card draw each turn for the player
//     who owns this power.
// ZH: 选择诅咒典后在战斗开始时为每位玩家各应用一次。
//     永久为拥有此能力的玩家提供 +2 最大能量和每回合 +2 摸牌。
//=============================================================================
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace Act4Placeholder;

internal sealed class CursedTomePlayerBonusPower : PowerModel
{
	private const int BonusDraw   = 2;
	private const int BonusEnergy = 2;

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override decimal ModifyHandDraw(Player player, decimal count)
	{
		// Only boost draw for the player who owns this power (permanent, no self-removal).
		if (player != base.Owner.Player) return count;
		return count + BonusDraw;
	}

	public override decimal ModifyMaxEnergy(Player player, decimal amount)
	{
		if (player != base.Owner.Player) return amount;
		return amount + BonusEnergy;
	}
}
