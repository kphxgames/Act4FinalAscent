//=============================================================================
// SporeNourishmentPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Stacking counter on the Fogmog (starts at 5). Applied in ArchitectSummonedFogmog.AfterAddedToRoom; currently a display marker, future updates may add nourishment effects.
// ZH: 雾魔「孢子滋养」叠层计数（初始5层），在ArchitectSummonedFogmog.AfterAddedToRoom中施加；目前用于显示，后续版本可能加入滋养效果。
//=============================================================================
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace Act4Placeholder;

internal sealed class SporeNourishmentPower : PowerModel
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;
}
