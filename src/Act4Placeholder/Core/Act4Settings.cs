//=============================================================================
// Act4Settings.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Stores host-authoritative optional-feature toggle states (help potions, extra rewards) and per-run runtime flags that reset at the end of every run.
// ZH: 存储主机权威的可选功能开关状态（辅助药水、额外奖励）及每次跑图结束时重置的运行时标志。
//=============================================================================
namespace Act4Placeholder;

/// <summary>
/// Host-authoritative settings for Act4Placeholder optional features.
/// Checkbox preferences live here. In co-op, ONLY the host propagates settings
/// to all machines via GameActions at the start of each run.
/// </summary>
internal static class Act4Settings
{
	// ── Main-menu checkbox preferences (local to this machine) ──
	internal static bool HelpPotionsEnabled { get; set; } = false;
	internal static bool ExtraRewardsEnabled { get; set; } = false;

	// ── Per-run runtime flags (reset at end of each run, set by GameActions or directly in solo) ──
	internal static bool ExtraRewardsActiveForCurrentRun { get; set; } = false;
	internal static bool HelpPotionsGivenForCurrentRun { get; set; } = false;

	// ── Grand Library book-choice flags (set when player steals a book from the event) ──
	// These replace the broken PowerCmd.Apply approach (which silently no-ops outside combat).
	internal static bool HolyBookChosen { get; set; } = false;
	internal static bool ShadowBookChosen { get; set; } = false;
	internal static bool SilverBookChosen { get; set; } = false;
	internal static bool CursedBookChosen { get; set; } = false;

	internal static void ResetForNewRun()
	{
		ExtraRewardsActiveForCurrentRun = false;
		HelpPotionsGivenForCurrentRun = false;
		HolyBookChosen = false;
		ShadowBookChosen = false;
		SilverBookChosen = false;
		CursedBookChosen = false;
	}
}
