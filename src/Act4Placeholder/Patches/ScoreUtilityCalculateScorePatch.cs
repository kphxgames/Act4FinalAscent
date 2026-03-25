//=============================================================================
// ScoreUtilityCalculateScorePatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches both overloads of ScoreUtility.CalculateScore to append an Act 4 progression bonus to the player's final run score based on how far they advanced in Act 4.
// ZH: 补丁同时修改ScoreUtility.CalculateScore的两个重载，根据玩家在第四幕的推进程度为最终跑图分数追加进度奖励分。
//=============================================================================
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Act4Placeholder;

[HarmonyPatch(typeof(ScoreUtility), nameof(ScoreUtility.CalculateScore), new[] { typeof(IRunState), typeof(bool) })]
internal static class ScoreUtilityCalculateScoreRunStatePatch
{
	private static void Postfix(IRunState runState, bool won, ref int __result)
	{
		if (runState is RunState concreteRunState)
		{
			__result += ModSupport.GetAct4ProgressionBonus(concreteRunState, won);
		}
	}
}

[HarmonyPatch(typeof(ScoreUtility), nameof(ScoreUtility.CalculateScore), new[] { typeof(SerializableRun), typeof(bool) })]
internal static class ScoreUtilityCalculateScoreSerializableRunPatch
{
	private static void Postfix(SerializableRun run, bool won, ref int __result)
	{
		__result += ModSupport.GetAct4ProgressionBonus(run, won);
	}
}
