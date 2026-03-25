//=============================================================================
// ArchitectRetaliationPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Retaliation marker on the Architect. Stores HP% at time of retaliation; boss reactivates the Retaliation move when below half HP. Logic in Act4ArchitectBoss.cs (SyncRetaliationPowerAsync / IsRetaliating).
// ZH: 建筑师「报复」标记，记录报复触发时的HP%；Boss血量低于50%时重新激活报复行动。逻辑见Act4ArchitectBoss.cs中的SyncRetaliationPowerAsync和IsRetaliating。
//=============================================================================
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace Act4Placeholder;

internal sealed class ArchitectRetaliationPower : PowerModel
{
	//=============================================================================
	// EN: Another marker-only power.
	//     Think of it as a sticky note for the boss AI: "retaliation mode armed".
	// ZH: 同样是标记型能力。
	//     可以把它理解成给Boss AI贴的便签：“报复模式已就绪”。
	//=============================================================================
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;
}
