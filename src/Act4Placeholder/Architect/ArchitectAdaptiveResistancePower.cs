//=============================================================================
// ArchitectAdaptiveResistancePower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Adaptive Resistance display power on the Architect. Amount is updated by SyncAdaptiveResistancePowerAsync each phase entry; represents current damage reduction %. Logic in Act4ArchitectBoss.cs.
// ZH: 建筑师「自适应抗性」显示型能力，每次进入阶段由SyncAdaptiveResistancePowerAsync更新Amount，表示当前伤害减免%。逻辑见Act4ArchitectBoss.cs。
//=============================================================================
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace Act4Placeholder;

internal sealed class ArchitectAdaptiveResistancePower : PowerModel
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;
}
