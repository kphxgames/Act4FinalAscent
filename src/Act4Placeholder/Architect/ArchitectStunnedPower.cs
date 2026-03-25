//=============================================================================
// ArchitectStunnedPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Stunned state marker on the Architect. Applied when All-or-Nothing or Judgment thresholds trigger a stun; removed on phase transition. Logic in Act4ArchitectBoss.cs.
// ZH: 建筑师「眩晕」状态标记，在孤注一掷或审判阈值触发眩晕时施加，阶段切换时移除。逻辑见Act4ArchitectBoss.cs。
//=============================================================================
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace Act4Placeholder;

internal sealed class ArchitectStunnedPower : PowerModel
{
	//=============================================================================
	// EN: Pure marker power.
	//     No fancy hooks here - Act4ArchitectBoss owns the actual stun flow.
	// ZH: 纯标记型能力。
	//     这里不做复杂逻辑，眩晕流程由Act4ArchitectBoss统一控制。
	//=============================================================================
	public override PowerType Type => PowerType.Debuff;

	public override PowerStackType StackType => PowerStackType.Counter;
}
