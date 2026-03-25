//=============================================================================
// DynamicMultiAttackIntent.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Custom AttackIntent whose damage and repeat count are evaluated lazily via delegates at display time, enabling accurate tooltips for dynamically-scaling multi-attack moves.
// ZH: 自定义AttackIntent，通过委托在显示时动态计算伤害与攻击次数，确保运行时可变的多段攻击意图提示框显示准确数值。
//=============================================================================
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace Act4Placeholder;

internal sealed class DynamicMultiAttackIntent : AttackIntent
{
	private readonly Func<int> _repeatCalc;

	public override int Repeats => _repeatCalc.Invoke();

	protected override LocString IntentLabelFormat => new LocString("intents", "FORMAT_DAMAGE_MULTI");

	public DynamicMultiAttackIntent(Func<decimal> damageCalc, Func<int> repeatCalc)
	{
		DamageCalc = damageCalc;
		_repeatCalc = repeatCalc;
	}

	public override int GetTotalDamage(IEnumerable<Creature> targets, Creature owner)
	{
		return GetSingleDamage(targets, owner) * Repeats;
	}

	public override LocString GetIntentLabel(IEnumerable<Creature> targets, Creature owner)
	{
		LocString intentLabelFormat = IntentLabelFormat;
		intentLabelFormat.Add("Damage", (decimal)GetSingleDamage(targets, owner));
		intentLabelFormat.Add("Repeat", (decimal)Repeats);
		return intentLabelFormat;
	}
}
