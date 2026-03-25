//=============================================================================
// ArchitectBlockCapPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Buff that caps each incoming hit to the Architect at its current block value while it has block, causing excess damage to be absorbed rather than passed through.
// ZH: Buff能力，在建筑师拥有格挡时将每次受击伤害上限限制为当前格挡值，使超额伤害被格挡吸收而非穿透。
//=============================================================================
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Act4Placeholder;

internal sealed class ArchitectBlockCapPower : PowerModel
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override bool ShouldClearBlock(Creature creature)
	{
		return creature != base.Owner;
	}

	public override decimal ModifyDamageCap(Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target != base.Owner || base.Owner.Block <= 0 || !props.HasFlag(ValueProp.Move) || props.HasFlag(ValueProp.Unpowered))
		{
			return decimal.MaxValue;
		}
		return base.Amount;
	}

	public override Task AfterModifyingDamageAmount(CardModel? cardSource)
	{
		if (base.Owner.Block > 0)
		{
			Flash();
		}
		return Task.CompletedTask;
	}
}
