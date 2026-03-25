//=============================================================================
// ArchitectSummonThornsPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Thorns-like power that pings attackers for fixed retaliatory damage.
//     Omnislice is handled explicitly because its damage path can bypass normal "Move" flags.
// ZH: 类荆棘反伤能力：攻击者命中建筑师时会受到固定反伤。
//     额外兼容Omnislice，因为该牌的伤害路径可能绕过普通"Move"标记。
//=============================================================================
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;

namespace Act4Placeholder;

internal sealed class ArchitectSummonThornsPower : PowerModel
{
	//=============================================================================
	// EN: Plain buff/counter metadata.
	// ZH: 基础能力元数据（增益/计数层）。
	//=============================================================================
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	//=============================================================================
	// EN: Retaliate when Architect gets hit by a real attack.
	//     We keep Omnislice as an explicit fallback because in practice it may not always
	//     look like a standard Move-powered hit to this hook.
	//
	//     Damage is intentionally BLOCKABLE now:
	//     - `ValueProp.Move` keeps it as a regular combat hit.
	//     - no `Unpowered` flag means block can absorb it like normal.
	//
	// ZH: 当建筑师受到有效攻击时触发反伤。
	//     Omnislice保留显式兜底，实战中它不一定总是以标准Move命中进入此钩子。
	//
	//     反伤现在可被格挡：
	//     - 使用 `ValueProp.Move` 作为常规命中
	//     - 不再附加 `Unpowered`，因此格挡可正常生效。
	//=============================================================================
	public override async Task BeforeDamageReceived(PlayerChoiceContext choiceContext, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		bool isAttack = props.HasFlag(ValueProp.Move) && !props.HasFlag(ValueProp.Unpowered);
		if (target == base.Owner && dealer != null && (isAttack || cardSource is Omnislice))
		{
			Flash();
			await CreatureCmd.Damage(choiceContext, dealer, base.Amount, ValueProp.Move | ValueProp.SkipHurtAnim, base.Owner, null);
		}
	}
}
