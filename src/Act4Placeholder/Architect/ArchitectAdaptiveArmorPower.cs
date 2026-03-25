//=============================================================================
// ArchitectAdaptiveArmorPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Perpetual buff on the Architect in Phases 3 and 4, caps each incoming
//     hit at 999 damage and prevents the Architect's own block from clearing
//     at end of turn (replacing the role previously held by ArchitectBlockCapPower).
// ZH: 建筑师在三、四阶段的持续增益：每次受击伤害上限999，并阻止建筑师自身
//     的格挡在回合结束时被清除（承担之前ArchitectBlockCapPower的双重职责）。
//=============================================================================
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Act4Placeholder;

internal sealed class ArchitectAdaptiveArmorPower : PowerModel
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	/// EN: Cap every incoming hit on the Architect at 999 damage.
	/// ZH: 将建筑师受到的每次攻击伤害上限限制为999。
	public override decimal ModifyDamageCap(Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target != base.Owner)
		{
			return decimal.MaxValue;
		}
		return 999m;
	}

	public override Task AfterModifyingDamageAmount(CardModel? cardSource)
	{
		Flash();
		return Task.CompletedTask;
	}

	/// EN: Prevent the Architect's block from clearing at the end of the enemy turn.
	///     Returns true (clear normally) for all OTHER creatures; false (keep block) for the Architect.
	/// ZH: 阻止建筑师在敌方回合结束时清除格挡；对其他生物正常清除。
	public override bool ShouldClearBlock(Creature creature)
	{
		return creature != base.Owner;
	}
}
