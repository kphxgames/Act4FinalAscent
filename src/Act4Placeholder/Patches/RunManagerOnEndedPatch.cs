//=============================================================================
// RunManagerOnEndedPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches RunManager.OnEnded to reset all Act 4 settings at the end of every run, and to override the result to a victory if the run ended by defeating the Architect boss.
// ZH: 补丁修改RunManager.OnEnded，在每次跑图结束时重置所有第四幕设置，若跑图在击败建筑师Boss后结束则将结果强制改为胜利。
//=============================================================================
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

[HarmonyPatch(typeof(RunManager), "OnEnded")]
internal static class RunManagerOnEndedPatch
{
	private static void Prefix(RunManager __instance, ref bool isVictory)
	{
		Act4AudioHelper.StopModBgm();
		Act4Settings.ResetForNewRun();
		RunState runState = __instance.DebugOnlyGetState();
		if (!isVictory && ModSupport.ShouldTreatCurrentAct4BossRoomAsVictory(runState))
		{
			// Player defeated the Architect, full Act 4 victory.
			ModSupport.MarkAct4BossVictory(runState);
			isVictory = true;
		}
		else if (!isVictory && ModSupport.IsAct4Placeholder(runState))
		{
			// Player entered Act 4 but did not defeat the Architect.
			// Count as a normal run win in base character stats;
			// the dedicated Act 4 stats section tracks the loss separately.
			ModSupport.RecordAct4EnteredWithoutVictory(runState);
			isVictory = true;
		}
	}
}
