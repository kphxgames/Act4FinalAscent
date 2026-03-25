//=============================================================================
// LinkedShadowPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Display buff applied to each Phase 4 Linked Shadow. When the shadow dies,
//     Act4ArchitectBoss loses 10% of its Max HP (non-fatal). The HP drain is
//     triggered from Phase4LinkedShadow.BeforeRemovedFromRoom.
// ZH: 四阶段「连结之影」身上附加的显示增益。影子死亡时，建筑师损失最大HP的10%（不致命）。
//     HP扣减逻辑触发于Phase4LinkedShadow.BeforeRemovedFromRoom。
//=============================================================================
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace Act4Placeholder;

internal sealed class LinkedShadowPower : PowerModel
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.None;
}
