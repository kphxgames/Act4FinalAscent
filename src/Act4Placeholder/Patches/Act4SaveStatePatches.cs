//=============================================================================
// Act4SaveStatePatches.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches RunManager.ToSave and RunState.FromSerializable to inject and restore Act 4-specific mod flags into the serialized save data.
// ZH: 补丁修改RunManager.ToSave和RunState.FromSerializable，在序列化存档数据中注入并恢复第四幕Mod专用的运行状态标记。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace Act4Placeholder;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.ToSave), new[] { typeof(AbstractRoom) })]
internal static class RunManagerToSaveAct4Patch
{
	private static void Postfix(RunManager __instance, ref SerializableRun __result)
	{
		ModSupport.ApplyAct4SaveMarkers(__result, __instance?.DebugOnlyGetState());
	}
}

[HarmonyPatch(typeof(RunState), nameof(RunState.FromSerializable))]
internal static class RunStateFromSerializableAct4Patch
{
	private static void Postfix(SerializableRun save, ref RunState __result)
	{
		ModSupport.RestoreAct4FlagsFromSave(save, __result);
	}
}

[HarmonyPatch(typeof(ProgressSaveManager), nameof(ProgressSaveManager.LoadProgress))]
internal static class ProgressSaveManagerLoadProgressSanitizePatch
{
	private static void Postfix(ProgressSaveManager __instance, ReadSaveResult<SerializableProgress> __result)
	{
		try
		{
			if (!__result.Success || __result.SaveData == null)
			{
				return;
			}
			SerializableProgress serializableProgress = __instance.Progress?.ToSerializable();
			if (serializableProgress == null)
			{
				return;
			}
			serializableProgress.SchemaVersion = __result.SaveData.SchemaVersion;
			if (AreEquivalent(__result.SaveData, serializableProgress))
			{
				return;
			}
			__instance.SaveProgress();
			Act4Logger.Info("Progress cleanup: rewrote progress.save after removing stale unknown IDs.");
		}
		catch (Exception ex)
		{
			Act4Logger.Warn($"Progress cleanup failed: {ex.Message}");
		}
	}

	private static bool AreEquivalent(SerializableProgress left, SerializableProgress right)
	{
		return SerializeNormalized(left) == SerializeNormalized(right);
	}

	private static string SerializeNormalized(SerializableProgress progress)
	{
		List<CharacterStats> charStats = progress.CharStats ?? new List<CharacterStats>();
		List<CardStats> cardStats = progress.CardStats ?? new List<CardStats>();
		List<EncounterStats> encounterStats = progress.EncounterStats ?? new List<EncounterStats>();
		List<EnemyStats> enemyStats = progress.EnemyStats ?? new List<EnemyStats>();
		List<AncientStats> ancientStats = progress.AncientStats ?? new List<AncientStats>();
		List<ModelId> discoveredCards = progress.DiscoveredCards ?? new List<ModelId>();
		List<ModelId> discoveredRelics = progress.DiscoveredRelics ?? new List<ModelId>();
		List<ModelId> discoveredPotions = progress.DiscoveredPotions ?? new List<ModelId>();
		List<ModelId> discoveredEvents = progress.DiscoveredEvents ?? new List<ModelId>();
		List<ModelId> discoveredActs = progress.DiscoveredActs ?? new List<ModelId>();
		List<SerializableEpoch> epochs = progress.Epochs ?? new List<SerializableEpoch>();
		List<string> ftueCompleted = progress.FtueCompleted ?? new List<string>();
		List<SerializableUnlockedAchievement> unlockedAchievements = progress.UnlockedAchievements ?? new List<SerializableUnlockedAchievement>();

		SerializableProgress normalized = new SerializableProgress
		{
			UniqueId = progress.UniqueId,
			SchemaVersion = progress.SchemaVersion,
			EnableFtues = progress.EnableFtues,
			TotalPlaytime = progress.TotalPlaytime,
			TotalUnlocks = progress.TotalUnlocks,
			CurrentScore = progress.CurrentScore,
			FloorsClimbed = progress.FloorsClimbed,
			ArchitectDamage = progress.ArchitectDamage,
			WongoPoints = progress.WongoPoints,
			PreferredMultiplayerAscension = progress.PreferredMultiplayerAscension,
			MaxMultiplayerAscension = progress.MaxMultiplayerAscension,
			TestSubjectKills = progress.TestSubjectKills,
			PendingCharacterUnlock = progress.PendingCharacterUnlock,
			CharStats = charStats.OrderBy(stat => stat.Id?.ToString(), StringComparer.Ordinal).Select(stat => new CharacterStats
			{
				Id = stat.Id,
				MaxAscension = stat.MaxAscension,
				PreferredAscension = stat.PreferredAscension,
				TotalWins = stat.TotalWins,
				TotalLosses = stat.TotalLosses,
				FastestWinTime = stat.FastestWinTime,
				BestWinStreak = stat.BestWinStreak,
				CurrentWinStreak = stat.CurrentWinStreak,
				Playtime = stat.Playtime
			}).ToList(),
			CardStats = cardStats.OrderBy(stat => stat.Id?.ToString(), StringComparer.Ordinal).Select(stat => new CardStats
			{
				Id = stat.Id,
				TimesPicked = stat.TimesPicked,
				TimesSkipped = stat.TimesSkipped,
				TimesWon = stat.TimesWon,
				TimesLost = stat.TimesLost
			}).ToList(),
			EncounterStats = encounterStats.OrderBy(stat => stat.Id.ToString(), StringComparer.Ordinal).Select(stat => new EncounterStats
			{
				Id = stat.Id,
				FightStats = SortFightStats(stat.FightStats)
			}).ToList(),
			EnemyStats = enemyStats.OrderBy(stat => stat.Id.ToString(), StringComparer.Ordinal).Select(stat => new EnemyStats
			{
				Id = stat.Id,
				FightStats = SortFightStats(stat.FightStats)
			}).ToList(),
			AncientStats = ancientStats.OrderBy(stat => stat.Id.ToString(), StringComparer.Ordinal).Select(stat => new AncientStats
			{
				Id = stat.Id,
				CharStats = SortAncientCharacterStats(stat.CharStats)
			}).ToList(),
			DiscoveredCards = SortModelIds(discoveredCards),
			DiscoveredRelics = SortModelIds(discoveredRelics),
			DiscoveredPotions = SortModelIds(discoveredPotions),
			DiscoveredEvents = SortModelIds(discoveredEvents),
			DiscoveredActs = SortModelIds(discoveredActs),
			Epochs = epochs.OrderBy(epoch => epoch.Id, StringComparer.Ordinal).ThenBy(epoch => epoch.ObtainDate).Select(epoch => new SerializableEpoch(epoch.Id, epoch.State)
			{
				State = epoch.State,
				ObtainDate = epoch.ObtainDate
			}).ToList(),
			FtueCompleted = ftueCompleted.OrderBy(id => id, StringComparer.Ordinal).ToList(),
			UnlockedAchievements = unlockedAchievements.OrderBy(achievement => achievement.Achievement, StringComparer.Ordinal).ThenBy(achievement => achievement.UnlockTime).Select(achievement => new SerializableUnlockedAchievement
			{
				Achievement = achievement.Achievement,
				UnlockTime = achievement.UnlockTime
			}).ToList()
		};
		return JsonSerializationUtility.ToJson(normalized);
	}

	private static List<FightStats> SortFightStats(IEnumerable<FightStats> fightStats)
	{
		return (fightStats ?? Enumerable.Empty<FightStats>()).OrderBy(stat => stat.Character.ToString(), StringComparer.Ordinal).Select(stat => new FightStats
		{
			Character = stat.Character,
			Wins = stat.Wins,
			Losses = stat.Losses
		}).ToList();
	}

	private static List<AncientCharacterStats> SortAncientCharacterStats(IEnumerable<AncientCharacterStats> characterStats)
	{
		return (characterStats ?? Enumerable.Empty<AncientCharacterStats>()).OrderBy(stat => stat.Character.ToString(), StringComparer.Ordinal).Select(stat => new AncientCharacterStats
		{
			Character = stat.Character,
			Wins = stat.Wins,
			Losses = stat.Losses
		}).ToList();
	}

	private static List<ModelId> SortModelIds(IEnumerable<ModelId> modelIds)
	{
		return (modelIds ?? Enumerable.Empty<ModelId>()).OrderBy(modelId => modelId.ToString(), StringComparer.Ordinal).ToList();
	}
}

// EN: Suppresses the "Act 4 is not yet implemented" and "EpochModel was not found" errors
//     that spam the log when the Architect boss fight ends. Act 4 (CurrentActIndex == 3)
//     has no handler in ObtainCharUnlockEpoch, we skip it silently since there is no Act 5.
// ZH: 压制建筑师Boss战结束时出现的 "Act 4 is not yet implemented" 和 "EpochModel was not found"
//     错误信息。第四幕(CurrentActIndex == 3)在ObtainCharUnlockEpoch中没有对应处理——我们静默跳过，因为没有第五幕。
[HarmonyPatch(typeof(ProgressSaveManager), "ObtainCharUnlockEpoch")]
internal static class ProgressSaveManagerAct4EpochSuppressPatch
{
	private static bool Prefix(int act)
	{
		// act is 0-indexed; act==3 means Act 4, which has no defined epoch in the base game.
		// Return false to skip the original method and suppress the error logs.
		return act != 3;
	}
}
