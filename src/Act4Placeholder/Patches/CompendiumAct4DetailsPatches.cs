//=============================================================================
// CompendiumAct4DetailsPatches.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Injects Act 4-specific win/loss statistics and run-history filtering into the Compendium and Stats screens, adding toggle buttons and extra stat rows for Act 4 runs.
// ZH: 向图鉴和统计界面注入第四幕专属的胜负统计与跑图历史筛选功能，添加切换按钮和额外的第四幕统计行。
//=============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.StatsScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.addons.mega_text;

namespace Act4Placeholder;

internal static class CompendiumAct4DetailsPatches
{
	private const string RunHistoryToggleButtonName = "Act4Placeholder_RunHistoryAct4Toggle";

	private const string StatsToggleButtonName = "Act4Placeholder_StatsAct4Toggle";

	private const string OverallAct4EntryName = "Act4Placeholder_OverallAct4WinLossEntry";

	private const string OverallAct4BrutalEntryName = "Act4Placeholder_OverallAct4BrutalWinLossEntry";

	private const string CharacterAct4EntryName = "Act4Placeholder_CharacterAct4WinLossEntry";

	private const string CharacterAct4BrutalEntryName = "Act4Placeholder_CharacterAct4BrutalWinLossEntry";

	private static readonly string StatsSwordsIconPath = ImageHelper.GetImagePath("atlases/stats_screen_atlas.sprites/stats_swords.tres");

	private static bool _includeAct4Details;

	private static NRunHistory _runHistoryScreen;

	private static NStatsScreen _statsScreen;

	private sealed class CharacterComputedStats
	{
		public long Playtime;

		public long FastestWinTime = -1L;

		public int TotalWins;

		public int TotalLosses;

		public int BrutalWins;

		public int BrutalLosses;

		public int MaxAscension;

		public long BestWinStreak;

		public long CurrentWinStreak;
	}

	private sealed class ComputedStatsSnapshot
	{
		public long TotalPlaytime;

		public long FastestWinTime = -1L;

		public int AggregateAscensionProgress;

		public int TotalWins;

		public int TotalLosses;

		public long BestWinStreak;

		public Dictionary<ModelId, CharacterComputedStats> PerCharacter { get; } = new Dictionary<ModelId, CharacterComputedStats>();
	}

	private sealed class Act4ComputedStatsSnapshot
	{
		public int TotalWins;

		public int TotalLosses;

		public int BrutalWins;

		public int BrutalLosses;

		public Dictionary<ModelId, CharacterComputedStats> PerCharacter { get; } = new Dictionary<ModelId, CharacterComputedStats>();
	}

	[HarmonyPatch(typeof(NRunHistory), "_Ready")]
	private static class NRunHistoryReadyPatch
	{
		private static void Postfix(NRunHistory __instance)
		{
			_runHistoryScreen = __instance;
			EnsureRunHistoryToggleButton(__instance);
		}
	}

	[HarmonyPatch(typeof(NRunHistory), nameof(NRunHistory.OnSubmenuOpened))]
	private static class NRunHistoryOnSubmenuOpenedPatch
	{
		private static void Prefix(NRunHistory __instance)
		{
			_runHistoryScreen = __instance;
			_includeAct4Details = false;
			UpdateRunHistoryToggleButtonVisuals(__instance);
		}
	}

	[HarmonyPatch(typeof(NRunHistory), "DisplayRun")]
	private static class NRunHistoryDisplayRunPatch
	{
		private static void Prefix(ref RunHistory history)
		{
			history = NormalizeRunForDisplay(history, _includeAct4Details);
		}
	}

	[HarmonyPatch(typeof(NMapPointHistory), "LoadHistory")]
	private static class NMapPointHistoryLoadHistoryPatch
	{
		private static void Postfix(NMapPointHistory __instance, RunHistory history)
		{
			if (!_includeAct4Details || !IsAct4Run(history))
			{
				return;
			}
			Control nodeOrNull = __instance.GetNodeOrNull<Control>("%Acts");
			if (nodeOrNull == null || nodeOrNull.GetChildCount() <= 3)
			{
				return;
			}
			Node child = nodeOrNull.GetChild(nodeOrNull.GetChildCount() - 1);
			MegaLabel nodeOrNull2 = child?.GetNodeOrNull<MegaLabel>("%Title");
			nodeOrNull2?.SetTextAutoSize(ModLoc.T("Act 4", "第四幕", fra: "Acte 4", deu: "Akt 4", jpn: "第4章", kor: "4막", por: "Ato 4", rus: "Акт 4", spa: "Acto 4"));
		}
	}

	[HarmonyPatch(typeof(NStatsScreen), "_Ready")]
	private static class NStatsScreenReadyPatch
	{
		private static void Postfix(NStatsScreen __instance)
		{
			_statsScreen = __instance;
			EnsureStatsToggleButton(__instance);
		}
	}

	[HarmonyPatch(typeof(NStatsScreen), nameof(NStatsScreen.OnSubmenuOpened))]
	private static class NStatsScreenOnSubmenuOpenedPatch
	{
		private static void Prefix(NStatsScreen __instance)
		{
			_statsScreen = __instance;
			_includeAct4Details = false;
			UpdateStatsToggleButtonVisuals(__instance);
		}
	}

	[HarmonyPatch(typeof(NGeneralStatsGrid), "LoadStats")]
	private static class NGeneralStatsGridLoadStatsPatch
	{
		private static void Postfix(NGeneralStatsGrid __instance)
		{
			// Always override the base rows so Act 4 runs are counted as wins
			// (entering Act 4 IS a win; the Architect fight result is shown in the extra rows).
			ApplyAct3CutoffGeneralStats(__instance);
			ApplyAct4OnlyGeneralLine(__instance, _includeAct4Details);
		}
	}

	[HarmonyPatch(typeof(NCharacterStats), "LoadStats")]
	private static class NCharacterStatsLoadStatsPatch
	{
		private static void Postfix(NCharacterStats __instance)
		{
			// Always override the base rows so Act 4 runs are counted as wins
			// (entering Act 4 IS a win; the Architect fight result is shown in the extra rows).
			ApplyAct3CutoffCharacterStats(__instance);
			ApplyAct4OnlyCharacterLine(__instance, _includeAct4Details);
		}
	}

	private static void EnsureRunHistoryToggleButton(NRunHistory screen)
	{
		NButton nodeOrNull = ((Node)screen).GetNodeOrNull<NButton>(RunHistoryToggleButtonName);
		if (nodeOrNull == null)
		{
			nodeOrNull = CreateToggleButton(RunHistoryToggleButtonName);
			nodeOrNull.Position = new Vector2(24f, 22f);
			((Node)screen).AddChild(nodeOrNull, false, Node.InternalMode.Disabled);
			((GodotObject)nodeOrNull).Connect(NClickableControl.SignalName.Released, Callable.From<NButton>((Action<NButton>)OnRunHistoryToggleReleased), 0u);
		}
		UpdateToggleButtonVisuals(nodeOrNull);
	}

	private static void EnsureStatsToggleButton(NStatsScreen screen)
	{
		NButton nodeOrNull = ((Node)screen).GetNodeOrNull<NButton>(StatsToggleButtonName);
		if (nodeOrNull == null)
		{
			nodeOrNull = CreateToggleButton(StatsToggleButtonName);
			nodeOrNull.Position = new Vector2(24f, 22f);
			((Node)screen).AddChild(nodeOrNull, false, Node.InternalMode.Disabled);
			((GodotObject)nodeOrNull).Connect(NClickableControl.SignalName.Released, Callable.From<NButton>((Action<NButton>)OnStatsToggleReleased), 0u);
		}
		UpdateToggleButtonVisuals(nodeOrNull);
	}

	private static NButton CreateToggleButton(string name)
	{
		NButton val = new NButton
		{
			Name = name,
			TooltipText = ModLoc.T("Toggle whether Compendium displays include Act 4 data.", "切换图鉴与统计界面是否显示第四幕数据。", fra: "Activer/désactiver l'affichage des données de l'Acte 4 dans le Compendium.", deu: "Umschalten ob Compendium-Ansichten Akt 4-Daten enthalten.", jpn: "図鑑と統計に第4章データを含めるか切り替えます。", kor: "도감과 통계에 4막 데이터를 포함할지 전환합니다.", por: "Alternar se as visualizações do Compêndio incluem dados do Ato 4.", rus: "Переключить отображение данных Акта 4 в Компендиуме.", spa: "Alternar si las vistas del Compendio incluyen datos del Acto 4."),
			FocusMode = Control.FocusModeEnum.All,
			MouseFilter = Control.MouseFilterEnum.Stop,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
			CustomMinimumSize = new Vector2(220f, 40f),
			Size = new Vector2(220f, 40f),
			ZIndex = 75
		};
		ColorRect val2 = new ColorRect
		{
			Name = "Background",
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)val2).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Node)val).AddChild(val2, false, Node.InternalMode.Disabled);
		Label val3 = new Label
		{
			Name = "Label",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = (HorizontalAlignment)1,
			VerticalAlignment = (VerticalAlignment)1
		};
		((Control)val3).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Control)val3).AddThemeFontSizeOverride("font_size", 14);
		((Node)val).AddChild(val3, false, Node.InternalMode.Disabled);
		return val;
	}

	private static void OnRunHistoryToggleReleased(NButton _)
	{
		_includeAct4Details = !_includeAct4Details;
		if (_runHistoryScreen != null)
		{
			UpdateRunHistoryToggleButtonVisuals(_runHistoryScreen);
			RefreshRunHistory(_runHistoryScreen);
		}
	}

	private static void OnStatsToggleReleased(NButton _)
	{
		_includeAct4Details = !_includeAct4Details;
		if (_statsScreen == null)
		{
			return;
		}
		UpdateStatsToggleButtonVisuals(_statsScreen);
		NGeneralStatsGrid value = Traverse.Create((object)_statsScreen).Field<NGeneralStatsGrid>("_statsGrid").Value;
		value?.LoadStats();
	}

	private static void RefreshRunHistory(NRunHistory screen)
	{
		int value = Traverse.Create((object)screen).Field<int>("_index").Value;
		object value2 = Traverse.Create((object)screen).Method("RefreshAndSelectRun", new object[1] { value }).GetValue();
		Task task = value2 as Task;
		if (task != null)
		{
			TaskHelper.RunSafely(task);
		}
	}

	private static void UpdateRunHistoryToggleButtonVisuals(NRunHistory screen)
	{
		NButton nodeOrNull = ((Node)screen).GetNodeOrNull<NButton>(RunHistoryToggleButtonName);
		if (nodeOrNull != null)
		{
			UpdateToggleButtonVisuals(nodeOrNull);
		}
	}

	private static void UpdateStatsToggleButtonVisuals(NStatsScreen screen)
	{
		NButton nodeOrNull = ((Node)screen).GetNodeOrNull<NButton>(StatsToggleButtonName);
		if (nodeOrNull != null)
		{
			UpdateToggleButtonVisuals(nodeOrNull);
		}
	}

	private static void UpdateToggleButtonVisuals(NButton button)
	{
		ColorRect nodeOrNull = ((Node)button).GetNodeOrNull<ColorRect>("Background");
		if (nodeOrNull != null)
		{
			nodeOrNull.Color = (_includeAct4Details ? new Color(0.15f, 0.37f, 0.18f, 0.92f) : new Color(0.38f, 0.16f, 0.12f, 0.9f));
		}
		Label nodeOrNull2 = ((Node)button).GetNodeOrNull<Label>("Label");
		if (nodeOrNull2 != null)
		{
			nodeOrNull2.Text = _includeAct4Details
				? ModLoc.T("Act 4 Details: ON", "第四幕详情：开启", fra: "Détails Acte 4 : ACTIVÉ", deu: "Akt 4 Details: EIN", jpn: "第4章詳細: オン", kor: "4막 세부정보: 켜짐", por: "Detalhes Ato 4: ATIVADO", rus: "Подробности Акта 4: ВКЛ", spa: "Detalles Acto 4: ACTIVADO")
				: ModLoc.T("Act 4 Details: OFF", "第四幕详情：关闭", fra: "Détails Acte 4 : DÉSACTIVÉ", deu: "Akt 4 Details: AUS", jpn: "第4章詳細: オフ", kor: "4막 세부정보: 꺼짐", por: "Detalhes Ato 4: DESATIVADO", rus: "Подробности Акта 4: ВЫКЛ", spa: "Detalles Acto 4: DESACTIVADO");
			((CanvasItem)nodeOrNull2).Modulate = (_includeAct4Details ? new Color(0.9f, 1f, 0.9f, 1f) : new Color(1f, 0.92f, 0.92f, 1f));
		}
		((Control)button).TooltipText = _includeAct4Details
			? ModLoc.T("Including Act 4 data in Compendium views.", "图鉴与统计界面当前包含第四幕数据。", fra: "Données de l'Acte 4 incluses dans les vues du Compendium.", deu: "Akt 4-Daten sind in den Compendium-Ansichten enthalten.", jpn: "図鑑ビューに第4章のデータが含まれています。", kor: "도감 보기에 4막 데이터가 포함되어 있습니다.", por: "Dados do Ato 4 incluídos nas visualizações do Compêndio.", rus: "Данные Акта 4 включены в просмотры Компендиума.", spa: "Datos del Acto 4 incluidos en las vistas del Compendio.")
			: ModLoc.T("Showing Act 3 cutoff data in Compendium views.", "图鉴与统计界面当前只显示截止到第三幕的数据。", fra: "Affichage des données jusqu'à l'Acte 3 dans le Compendium.", deu: "Nur Akt 3-Daten in den Compendium-Ansichten.", jpn: "図鑑ビューに第3章までのデータのみが表示されています。", kor: "4막 이전 데이터만 도감 보기에 표시됩니다.", por: "Exibindo apenas dados até o Ato 3 nas visualizações.", rus: "Только данные до Акта 3 в Компендиуме.", spa: "Solo datos hasta el Acto 3 en las vistas del Compendio.");
	}

	private static RunHistory NormalizeRunForDisplay(RunHistory history, bool includeAct4Details)
	{
		if (history == null)
		{
			return history;
		}
		if (includeAct4Details)
		{
			return EnsureAct4VisibleRunTime(history);
		}
		if (!IsAct4Run(history))
		{
			return history;
		}
		RunHistory snapshot;
		if (ModSupport.TryGetAct3Snapshot(history, out snapshot) && snapshot != null)
		{
			return CreateAct3FilteredRun(snapshot);
		}
		int num = Math.Min(3, history.Acts.Count);
		RunHistory runHistory = new RunHistory
		{
			SchemaVersion = history.SchemaVersion,
			PlatformType = history.PlatformType,
			GameMode = history.GameMode,
			Win = true,
			Seed = history.Seed,
			StartTime = history.StartTime,
			RunTime = history.RunTime,
			Ascension = history.Ascension,
			BuildId = history.BuildId,
			WasAbandoned = history.WasAbandoned,
			KilledByEncounter = ModelId.none,
			KilledByEvent = ModelId.none,
			Players = history.Players.ToList(),
			Acts = history.Acts.Take(num).ToList(),
			Modifiers = history.Modifiers.ToList(),
			MapPointHistory = history.MapPointHistory.Take(num).Select((List<MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry> entries) => entries.ToList()).ToList()
		};
		return CreateAct3FilteredRun(runHistory);
	}

	private static RunHistory CreateAct3FilteredRun(RunHistory history)
	{
		if (history == null)
		{
			return history;
		}
		int num = history.MapPointHistory?.Sum((List<MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry> rooms) => rooms?.Count ?? 0) ?? 0;
		List<RunHistoryPlayer> list = history.Players?.Select((RunHistoryPlayer player) => new RunHistoryPlayer
		{
			Id = player.Id,
			Character = player.Character,
			Deck = (player.Deck ?? Enumerable.Empty<SerializableCard>()).Where((SerializableCard card) => !card.FloorAddedToDeck.HasValue || card.FloorAddedToDeck.Value <= num).ToList(),
			Relics = (player.Relics ?? Enumerable.Empty<SerializableRelic>()).Where((SerializableRelic relic) => !relic.FloorAddedToDeck.HasValue || relic.FloorAddedToDeck.Value <= num).ToList(),
			Potions = (player.Potions ?? Enumerable.Empty<SerializablePotion>()).ToList(),
			MaxPotionSlotCount = player.MaxPotionSlotCount
		}).ToList() ?? new List<RunHistoryPlayer>();
		return new RunHistory
		{
			SchemaVersion = history.SchemaVersion,
			PlatformType = history.PlatformType,
			GameMode = history.GameMode,
			Win = history.Win,
			Seed = history.Seed,
			StartTime = history.StartTime,
			RunTime = history.RunTime,
			Ascension = history.Ascension,
			BuildId = history.BuildId,
			WasAbandoned = history.WasAbandoned,
			KilledByEncounter = history.KilledByEncounter,
			KilledByEvent = history.KilledByEvent,
			Players = list,
			Acts = history.Acts?.ToList() ?? new List<ModelId>(),
			Modifiers = history.Modifiers?.ToList() ?? new List<SerializableModifier>(),
			MapPointHistory = history.MapPointHistory?.Select((List<MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry> entries) => entries.ToList()).ToList() ?? new List<List<MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry>>()
		};
	}

	private static RunHistory EnsureAct4VisibleRunTime(RunHistory history)
	{
		if (!IsAct4Run(history))
		{
			return history;
		}
		float num = history.RunTime;
		RunHistory snapshot;
		if (ModSupport.TryGetAct3Snapshot(history, out snapshot) && snapshot != null)
		{
			num = Math.Max(num, snapshot.RunTime);
		}
		float fullRunTimeFromFile = TryGetFullRunTimeFromHistoryFile(history);
		if (fullRunTimeFromFile > num)
		{
			num = fullRunTimeFromFile;
		}
		float act4ExtraTimeEstimate = TryEstimateAct4ExtraTime(history, snapshot);
		if (act4ExtraTimeEstimate > num)
		{
			num = act4ExtraTimeEstimate;
		}
		if (num <= history.RunTime)
		{
			return history;
		}
		return CloneWithRunTime(history, num);
	}

	private static float TryGetFullRunTimeFromHistoryFile(RunHistory history)
	{
		try
		{
			SaveManager instance = SaveManager.Instance;
			if (instance == null || history.StartTime <= 0)
			{
				return history.RunTime;
			}
			string path = Path.Combine(RunHistorySaveManager.GetHistoryPath(instance.CurrentProfileId), $"{history.StartTime}.run");
			if (!File.Exists(path))
			{
				return history.RunTime;
			}
			DateTime dateTime = File.GetLastWriteTimeUtc(path);
			float num = new DateTimeOffset(dateTime).ToUnixTimeSeconds() - history.StartTime;
			return Math.Max(history.RunTime, num);
		}
		catch
		{
			return history.RunTime;
		}
	}

	private static float TryEstimateAct4ExtraTime(RunHistory history, RunHistory? snapshot)
	{
		try
		{
			if (history?.MapPointHistory == null || history.MapPointHistory.Count <= 3)
			{
				return history?.RunTime ?? 0f;
			}
			int num = 0;
			for (int i = 3; i < history.MapPointHistory.Count; i++)
			{
				List<MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry> list = history.MapPointHistory[i];
				if (list == null)
				{
					continue;
				}
				foreach (MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry item in list)
				{
					if (item?.Rooms == null)
					{
						continue;
					}
					foreach (MegaCrit.Sts2.Core.Runs.History.MapPointRoomHistoryEntry room in item.Rooms)
					{
						num += Math.Max(0, room?.TurnsTaken ?? 0);
					}
				}
			}
			float num2 = (snapshot?.RunTime ?? history.RunTime) + (float)num * 6f;
			return Math.Max(history.RunTime, num2);
		}
		catch
		{
			return history?.RunTime ?? 0f;
		}
	}

	private static RunHistory CloneWithRunTime(RunHistory history, float runTime)
	{
		return new RunHistory
		{
			SchemaVersion = history.SchemaVersion,
			PlatformType = history.PlatformType,
			GameMode = history.GameMode,
			Win = history.Win,
			Seed = history.Seed,
			StartTime = history.StartTime,
			RunTime = runTime,
			Ascension = history.Ascension,
			BuildId = history.BuildId,
			WasAbandoned = history.WasAbandoned,
			KilledByEncounter = history.KilledByEncounter,
			KilledByEvent = history.KilledByEvent,
			Players = history.Players.ToList(),
			Acts = history.Acts.ToList(),
			Modifiers = history.Modifiers.ToList(),
			MapPointHistory = history.MapPointHistory.Select((List<MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry> entries) => entries.ToList()).ToList()
		};
	}

	private static bool IsAct4Run(RunHistory history)
	{
		return history.Acts != null && history.Acts.Count > 3;
	}

	private static void ApplyAct3CutoffGeneralStats(NGeneralStatsGrid grid)
	{
		ComputedStatsSnapshot computedStatsSnapshot = BuildComputedStatsSnapshot(includeAct4Details: false);
		NStatEntry value = Traverse.Create((object)grid).Field<NStatEntry>("_playtimeEntry").Value;
		NStatEntry value2 = Traverse.Create((object)grid).Field<NStatEntry>("_winLossEntry").Value;
		NStatEntry value3 = Traverse.Create((object)grid).Field<NStatEntry>("_streakEntry").Value;
		if (value != null)
		{
			LocString locString = new LocString("stats_screen", "ENTRY_PLAYTIME.top");
			locString.Add("Playtime", TimeFormatting.Format(computedStatsSnapshot.TotalPlaytime));
			value.SetTopText(locString.GetFormattedText());
			if (computedStatsSnapshot.FastestWinTime >= 0)
			{
				locString = new LocString("stats_screen", "ENTRY_PLAYTIME.bottom");
				locString.Add("FastestWin", TimeFormatting.Format(computedStatsSnapshot.FastestWinTime));
				value.SetBottomText(locString.GetFormattedText());
			}
			else
			{
				value.SetBottomText(string.Empty);
			}
		}
		if (value2 != null)
		{
			LocString locString2 = new LocString("stats_screen", "ENTRY_WIN_LOSS.top");
			locString2.Add("Amount", StringHelper.RatioFormat(computedStatsSnapshot.AggregateAscensionProgress, SaveManager.GetAggregateAscensionCount()));
			value2.SetTopText(locString2.GetFormattedText() ?? "");
			locString2 = new LocString("stats_screen", "ENTRY_WIN_LOSS.bottom");
			locString2.Add("Wins", computedStatsSnapshot.TotalWins);
			locString2.Add("Losses", computedStatsSnapshot.TotalLosses);
			value2.SetBottomText(locString2.GetFormattedText());
		}
		if (value3 != null)
		{
			LocString locString3 = new LocString("stats_screen", "ENTRY_STREAK.top");
			locString3.Add("Amount", computedStatsSnapshot.BestWinStreak);
			value3.SetTopText(locString3.GetFormattedText());
		}
	}

	private static void ApplyAct3CutoffCharacterStats(NCharacterStats characterStatsNode)
	{
		CharacterStats value = Traverse.Create((object)characterStatsNode).Field<CharacterStats>("_characterStats").Value;
		if (value == null || value.Id == null)
		{
			return;
		}
		ComputedStatsSnapshot computedStatsSnapshot = BuildComputedStatsSnapshot(includeAct4Details: false);
		CharacterComputedStats value2;
		if (!computedStatsSnapshot.PerCharacter.TryGetValue(value.Id, out value2))
		{
			value2 = new CharacterComputedStats();
		}
		NStatEntry value3 = Traverse.Create((object)characterStatsNode).Field<NStatEntry>("_playtimeEntry").Value;
		NStatEntry value4 = Traverse.Create((object)characterStatsNode).Field<NStatEntry>("_winLossEntry").Value;
		NStatEntry value5 = Traverse.Create((object)characterStatsNode).Field<NStatEntry>("_streakEntry").Value;
		if (value3 != null)
		{
			LocString locString = new LocString("stats_screen", "ENTRY_CHAR_PLAYTIME.top");
			locString.Add("Playtime", TimeFormatting.Format(value2.Playtime));
			value3.SetTopText(locString.GetFormattedText());
			if (value2.FastestWinTime >= 0)
			{
				locString = new LocString("stats_screen", "ENTRY_CHAR_PLAYTIME.bottom");
				locString.Add("FastestWin", TimeFormatting.Format(value2.FastestWinTime));
				value3.SetBottomText(locString.GetFormattedText());
			}
			else
			{
				value3.SetBottomText(string.Empty);
			}
		}
		if (value4 != null)
		{
			LocString locString2 = new LocString("stats_screen", "ENTRY_CHAR_WIN_LOSS.top");
			locString2.Add("Amount", value2.MaxAscension);
			value4.SetTopText((value2.MaxAscension > 0) ? ("[red]" + locString2.GetFormattedText() + "[/red]") : locString2.GetFormattedText());
			locString2 = new LocString("stats_screen", "ENTRY_CHAR_WIN_LOSS.bottom");
			locString2.Add("Wins", value2.TotalWins);
			locString2.Add("Losses", value2.TotalLosses);
			value4.SetBottomText(locString2.GetFormattedText());
		}
		if (value5 != null)
		{
			LocString locString3 = new LocString("stats_screen", "ENTRY_CHAR_STREAK.top");
			locString3.Add("Amount", value2.CurrentWinStreak);
			value5.SetTopText(locString3.GetFormattedText());
			locString3 = new LocString("stats_screen", "ENTRY_CHAR_STREAK.bottom");
			locString3.Add("Amount", value2.BestWinStreak);
			value5.SetBottomText(locString3.GetFormattedText());
		}
	}

	private static void ApplyAct4OnlyGeneralLine(NGeneralStatsGrid grid, bool show)
	{
		Node gridContainer = Traverse.Create((object)grid).Field<Node>("_gridContainer").Value;
		if (gridContainer == null)
		{
			return;
		}
		NStatEntry normalEntry = gridContainer.GetNodeOrNull<NStatEntry>(OverallAct4EntryName);
		NStatEntry brutalEntry = gridContainer.GetNodeOrNull<NStatEntry>(OverallAct4BrutalEntryName);
		if (!show)
		{
			normalEntry?.QueueFree();
			brutalEntry?.QueueFree();
			return;
		}
		Act4ComputedStatsSnapshot snap = BuildAct4StatsSnapshot();
		int normalWins = snap.TotalWins - snap.BrutalWins;
		int normalLosses = snap.TotalLosses - snap.BrutalLosses;
		string winsLabel = ModLoc.T("Wins", "胜", fra: "Victoires", deu: "Siege", jpn: "勝利", kor: "승리", por: "Vitórias", rus: "Победы", spa: "Victorias");
		string lossesLabel = ModLoc.T("Losses", "败", fra: "Défaites", deu: "Niederlagen", jpn: "敗北", kor: "패배", por: "Derrotas", rus: "Поражения", spa: "Derrotas");
		if (normalEntry == null)
		{
			normalEntry = NStatEntry.Create(StatsSwordsIconPath);
			normalEntry.Name = OverallAct4EntryName;
			gridContainer.AddChild(normalEntry);
		}
		normalEntry.SetTopText(ModLoc.T("Act 4 Normal", "第四幕（普通）", fra: "Acte 4 Normal", deu: "Akt 4 Normal", jpn: "第4章（ノーマル）", kor: "4막 일반", por: "Ato 4 Normal", rus: "Акт 4 Обычный", spa: "Acto 4 Normal"));
		normalEntry.SetBottomText($"[color=#6FD37B]{normalWins} {winsLabel}[/color] [color=#E66B6B]{normalLosses} {lossesLabel}[/color]");
		if (brutalEntry == null)
		{
			brutalEntry = NStatEntry.Create(StatsSwordsIconPath);
			brutalEntry.Name = OverallAct4BrutalEntryName;
			gridContainer.AddChild(brutalEntry);
		}
		brutalEntry.SetTopText(ModLoc.T("Act 4 Brutal", "第四幕（残酷）", fra: "Acte 4 Brutal", deu: "Akt 4 Brutal", jpn: "第4章（ブルータル）", kor: "4막 브루탈", por: "Ato 4 Brutal", rus: "Акт 4 Брутальный", spa: "Acto 4 Brutal"));
		brutalEntry.SetBottomText($"[color=#6FD37B]{snap.BrutalWins} {winsLabel}[/color] [color=#E66B6B]{snap.BrutalLosses} {lossesLabel}[/color]");
	}

	private static void ApplyAct4OnlyCharacterLine(NCharacterStats characterStatsNode, bool show)
	{
		Node statsContainer = Traverse.Create((object)characterStatsNode).Field<Node>("_statsContainer").Value;
		if (statsContainer == null)
		{
			return;
		}
		NStatEntry normalEntry = statsContainer.GetNodeOrNull<NStatEntry>(CharacterAct4EntryName);
		NStatEntry brutalEntry = statsContainer.GetNodeOrNull<NStatEntry>(CharacterAct4BrutalEntryName);
		if (!show)
		{
			normalEntry?.QueueFree();
			brutalEntry?.QueueFree();
			return;
		}
		if (normalEntry == null)
		{
			normalEntry = NStatEntry.Create(StatsSwordsIconPath);
			normalEntry.Name = CharacterAct4EntryName;
			statsContainer.AddChild(normalEntry);
		}
		CharacterStats value = Traverse.Create((object)characterStatsNode).Field<CharacterStats>("_characterStats").Value;
		Act4ComputedStatsSnapshot act4ComputedStatsSnapshot = BuildAct4StatsSnapshot();
		CharacterComputedStats value2;
		if (!act4ComputedStatsSnapshot.PerCharacter.TryGetValue(value?.Id, out value2) || value2 == null)
		{
			value2 = new CharacterComputedStats();
		}
		string winsLabel = ModLoc.T("Wins", "胜", fra: "Victoires", deu: "Siege", jpn: "勝利", kor: "승리", por: "Vitórias", rus: "Победы", spa: "Victorias");
		string lossesLabel = ModLoc.T("Losses", "败", fra: "Défaites", deu: "Niederlagen", jpn: "敗北", kor: "패배", por: "Derrotas", rus: "Поражения", spa: "Derrotas");
		normalEntry.SetTopText(ModLoc.T("Act 4 Normal", "第四幕（普通）", fra: "Acte 4 Normal", deu: "Akt 4 Normal", jpn: "第4章（ノーマル）", kor: "4막 일반", por: "Ato 4 Normal", rus: "Акт 4 Обычный", spa: "Acto 4 Normal"));
		normalEntry.SetBottomText($"[color=#6FD37B]{value2.TotalWins - value2.BrutalWins} {winsLabel}[/color] [color=#E66B6B]{value2.TotalLosses - value2.BrutalLosses} {lossesLabel}[/color]");
		if (brutalEntry == null)
		{
			brutalEntry = NStatEntry.Create(StatsSwordsIconPath);
			brutalEntry.Name = CharacterAct4BrutalEntryName;
			statsContainer.AddChild(brutalEntry);
		}
		brutalEntry.SetTopText(ModLoc.T("Act 4 Brutal", "第四幕（残酷）", fra: "Acte 4 Brutal", deu: "Akt 4 Brutal", jpn: "第4章（ブルータル）", kor: "4막 브루탈", por: "Ato 4 Brutal", rus: "Акт 4 Брутальный", spa: "Acto 4 Brutal"));
		brutalEntry.SetBottomText($"[color=#6FD37B]{value2.BrutalWins} {winsLabel}[/color] [color=#E66B6B]{value2.BrutalLosses} {lossesLabel}[/color]");
	}

	private static Act4ComputedStatsSnapshot BuildAct4StatsSnapshot()
	{
		Act4ComputedStatsSnapshot act4ComputedStatsSnapshot = new Act4ComputedStatsSnapshot();
		List<RunHistory> list = LoadAllRunHistories().OrderBy((RunHistory run) => run.StartTime).ThenBy((RunHistory run) => run.Seed).ToList();
		foreach (RunHistory item in list)
		{
			if (!DidRunEnterAct4(item))
			{
				continue;
			}
			bool flag = DidRunWinAct4(item);
			bool isBrutal = ModSupport.IsAct4BrutalRunFromHistory(item);
			if (flag)
			{
				act4ComputedStatsSnapshot.TotalWins++;
				if (isBrutal) act4ComputedStatsSnapshot.BrutalWins++;
			}
			else
			{
				act4ComputedStatsSnapshot.TotalLosses++;
				if (isBrutal) act4ComputedStatsSnapshot.BrutalLosses++;
			}
			if (item?.Players == null || item.Players.Count == 0)
			{
				continue;
			}
			foreach (RunHistoryPlayer player in item.Players)
			{
				CharacterComputedStats value;
				if (!act4ComputedStatsSnapshot.PerCharacter.TryGetValue(player.Character, out value))
				{
					value = new CharacterComputedStats();
					act4ComputedStatsSnapshot.PerCharacter[player.Character] = value;
				}
				if (flag)
				{
					value.TotalWins++;
					if (isBrutal) value.BrutalWins++;
				}
				else
				{
					value.TotalLosses++;
					if (isBrutal) value.BrutalLosses++;
				}
			}
		}
		return act4ComputedStatsSnapshot;
	}

	private static bool DidRunWinAct4(RunHistory? history)
	{
		if (history == null || !DidRunEnterAct4(history))
			return false;
		// Definitive markers take priority.
		if (ModSupport.WasAct4BossActuallyDefeated(history))
			return true;
		if (ModSupport.WasAct4EnteredWithoutVictory(history))
			return false;
		// Legacy runs (pre-fix): history.Win was only set to true if the boss was defeated,
		// so use it as the truth for old entries that predate the marker files.
		return history.Win;
	}

	private static bool DidRunEnterAct4(RunHistory? history)
	{
		return history?.Acts != null && history.Acts.Count > 3;
	}

	private static ComputedStatsSnapshot BuildComputedStatsSnapshot(bool includeAct4Details)
	{
		ComputedStatsSnapshot computedStatsSnapshot = new ComputedStatsSnapshot();
		List<RunHistory> list = LoadAllRunHistories().OrderBy((RunHistory run) => run.StartTime).ThenBy((RunHistory run) => run.Seed).ToList();
		foreach (RunHistory item in list)
		{
			RunHistory runHistory = NormalizeRunForDisplay(item, includeAct4Details);
			long num = (long)Math.Max(0.0, Math.Round(runHistory.RunTime));
			computedStatsSnapshot.TotalPlaytime += num;
			if (runHistory.Players == null || runHistory.Players.Count == 0)
			{
				continue;
			}
			foreach (RunHistoryPlayer player in runHistory.Players)
			{
				CharacterComputedStats value;
				if (!computedStatsSnapshot.PerCharacter.TryGetValue(player.Character, out value))
				{
					value = new CharacterComputedStats();
					computedStatsSnapshot.PerCharacter[player.Character] = value;
				}
				value.Playtime += num;
				if (runHistory.Win)
				{
					value.TotalWins++;
					value.MaxAscension = Math.Max(value.MaxAscension, runHistory.Ascension);
					if (value.FastestWinTime < 0 || num < value.FastestWinTime)
					{
						value.FastestWinTime = num;
					}
					value.CurrentWinStreak++;
					value.BestWinStreak = Math.Max(value.BestWinStreak, value.CurrentWinStreak);
				}
				else
				{
					value.TotalLosses++;
					value.CurrentWinStreak = 0L;
				}
			}
		}
		foreach (CharacterComputedStats value2 in computedStatsSnapshot.PerCharacter.Values)
		{
			computedStatsSnapshot.TotalWins += value2.TotalWins;
			computedStatsSnapshot.TotalLosses += value2.TotalLosses;
			computedStatsSnapshot.AggregateAscensionProgress += Math.Max(0, value2.MaxAscension);
			computedStatsSnapshot.BestWinStreak = Math.Max(computedStatsSnapshot.BestWinStreak, value2.BestWinStreak);
			if (value2.FastestWinTime >= 0 && (computedStatsSnapshot.FastestWinTime < 0 || value2.FastestWinTime < computedStatsSnapshot.FastestWinTime))
			{
				computedStatsSnapshot.FastestWinTime = value2.FastestWinTime;
			}
		}
		return computedStatsSnapshot;
	}

	private static IEnumerable<RunHistory> LoadAllRunHistories()
	{
		SaveManager instance = SaveManager.Instance;
		if (instance == null)
		{
			yield break;
		}
		// Deduplicate by (StartTime, Seed, PlayerCount) to guard against duplicate files
		// the game may write for the same logical run (e.g. mid-run saves + end-of-run saves).
		var seenKeys = new HashSet<string>();
		foreach (string item in instance.GetAllRunHistoryNames())
		{
			ReadSaveResult<RunHistory> readSaveResult = instance.LoadRunHistory(item);
			if (readSaveResult.Success && readSaveResult.SaveData != null)
			{
				var run = readSaveResult.SaveData;
				string key = $"{run.StartTime}|{run.Seed ?? string.Empty}|{run.Players?.Count ?? 0}";
				if (seenKeys.Add(key))
				{
					yield return run;
				}
			}
		}
	}
}
