//=============================================================================
// ArchitectOblivionPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Countdown power on the Architect during Phase 4. Amount decrements each player round; reaching 0 triggers an Oblivion finisher. Full logic in Act4ArchitectBoss.cs (TickPhaseFourOblivionAsync).
// ZH: 第四阶段建筑师「湮灭」倒计时能力，每个玩家回合递减；归零时触发湮灭终结。完整逻辑见Act4ArchitectBoss.cs中的TickPhaseFourOblivionAsync。
//=============================================================================
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace Act4Placeholder;

internal sealed class ArchitectOblivionPower : PowerModel
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;
}
