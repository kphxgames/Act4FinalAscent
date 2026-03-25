//=============================================================================
// ArchitectAttackReadingsPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Extends ArchitectReadingsPowerBase to track Attack cards played by players each turn; grants the Architect vigor each time the threshold is reached.
// ZH: 继承ArchitectReadingsPowerBase，追踪玩家每回合打出的攻击牌；每达到阈值时为建筑师施加活力。
//=============================================================================
using MegaCrit.Sts2.Core.Entities.Cards;

namespace Act4Placeholder;

internal sealed class ArchitectAttackReadingsPower : ArchitectReadingsPowerBase
{
	protected override CardType WatchedCardType => CardType.Attack;
}
