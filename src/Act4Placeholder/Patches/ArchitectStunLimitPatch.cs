//=============================================================================
// ArchitectStunLimitPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Harmony Prefix on Creature.StunInternal, limits external stuns (e.g. Whistle)
//     to once per Architect phase. Artifact does not naturally block StunInternal
//     because that method bypasses power checks and forces the move state directly.
// ZH: 对Creature.StunInternal的Harmony前缀补丁——将外部击晕（如哨子）限制为每阶段一次。
//     原版Artifact无法阻止StunInternal，因为该方法绕过能力检查、直接强制设置招式状态。
//=============================================================================
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Act4Placeholder;

[HarmonyPatch(typeof(Creature), "StunInternal")]
internal static class ArchitectStunLimitPatch
{
	/// <summary>
	/// EN: Suppress any external stun on the Architect after the first one this phase.
	/// ZH: 每阶段首次外部击晕后，忽略后续外部击晕请求。
	/// </summary>
	private static bool Prefix(Creature __instance)
	{
		if (__instance.Monster is not Act4ArchitectBoss boss)
			return true; // not the Architect, allow normally

		if (boss.ExternalStunUsedThisPhase)
		{
			Act4Logger.Info($"ArchitectStunLimitPatch: external stun suppressed (once-per-phase cap reached, phase={boss.PhaseNumber})");
			return false; // suppress the stun call entirely
		}

		boss.ExternalStunUsedThisPhase = true;
		Act4Logger.Info($"ArchitectStunLimitPatch: external stun allowed (first this phase, phase={boss.PhaseNumber})");
		return true;
	}
}
