//=============================================================================
// ArchitectJudgmentPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Phase 3 Judgment threshold marker on the Architect. Stores the HP% trigger; removed on hit, then deals retaliatory damage to all players. Logic in Act4ArchitectBoss.cs (SyncPhaseThreeJudgmentAsync).
// ZH: 第三阶段建筑师的「审判」阈值标记，记录HP%触发点；被触发后移除并对所有玩家造成报复伤害。逻辑见Act4ArchitectBoss.cs中的SyncPhaseThreeJudgmentAsync。
//=============================================================================
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace Act4Placeholder;

internal sealed class ArchitectJudgmentPower : PowerModel
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;
}
