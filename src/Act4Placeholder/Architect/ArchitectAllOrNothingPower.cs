//=============================================================================
// ArchitectAllOrNothingPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Phase 2 threshold tracker on the Architect. Stores the remaining HP% threshold; removed when hit triggers the All-or-Nothing stun. Logic in Act4ArchitectBoss.cs (SyncPhaseTwoAllOrNothingAsync).
// ZH: 第二阶段建筑师的孤注一掷阈值追踪。记录剩余HP%阈值；触发后视情况眩晕建筑师。逻辑见Act4ArchitectBoss.cs中的SyncPhaseTwoAllOrNothingAsync。
//=============================================================================
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace Act4Placeholder;

internal sealed class ArchitectAllOrNothingPower : PowerModel
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;
}
