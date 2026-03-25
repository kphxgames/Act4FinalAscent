//=============================================================================
// MysteriousSporesPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Stacking counter on the Fogmog (starts at 30). Applied in ArchitectSummonedFogmog.AfterAddedToRoom; currently a display marker, future updates may add stack-decay effects.
// ZH: 雾魔「神秘孢子」叠层计数（初始30层），在ArchitectSummonedFogmog.AfterAddedToRoom中施加；目前用于显示，后续版本可能加入衰减效果。
//=============================================================================
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace Act4Placeholder;

internal sealed class MysteriousSporesPower : PowerModel
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;
}
