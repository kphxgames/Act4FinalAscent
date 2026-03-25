//=============================================================================
// SingleplayerRunSlotsPatches.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Adds a multi-slot save system for singleplayer runs (up to 3 slots), patching the main menu and run management to support saving, loading, and switching between multiple concurrent run files.
// ZH: 为单人跑图添加最多3个存档槽位的多槽存档系统，通过补丁修改主菜单和跑图管理，支持多个存档的保存、读取和切换。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Daily;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.Metrics;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Act4Placeholder;

internal static class SingleplayerRunSlotsPatches
{
	private sealed class RunSlotEntry
	{
		public int SlotId { get; set; }

		public string FilePath { get; set; } = string.Empty;

		public SerializableRun Run { get; set; } = null!;

		public long StartTime => Run.StartTime;

		public long SaveTime => Run.SaveTime;
	}

	private const int MaxRunSlots = 3;

	private const string CurrentRunFileName = "current_run.save";

	private const string SlotFileNamePrefix = "current_run_slot";

	private const string SlotFileNameSuffix = ".save";

	private const string ActiveSlotFileName = "current_run_slot_active.txt";

	private static readonly MegaCrit.Sts2.Core.Logging.Logger Logger = new MegaCrit.Sts2.Core.Logging.Logger("Act4Placeholder.RunSlots", (LogType)0);

	private static bool _allowSingleplayerStartBypass;

	[HarmonyPatch(typeof(NMainMenu), "_Ready")]
	private static class NMainMenuReadyPatch
	{
		private static void Postfix(NMainMenu __instance)
		{
			ModSupport.EnsureAct4DynamicTextLocalizationReady();
			NormalizeRunSlots();
			__instance.RefreshButtons();
		}
	}

	[HarmonyPatch(typeof(NMainMenu), "OnSubmenuStackChanged")]
	private static class NMainMenuOnSubmenuStackChangedPatch
	{
		private static void Postfix(NMainMenu __instance)
		{
			_ = __instance;
		}
	}

	[HarmonyPatch(typeof(NMainMenu), "RefreshButtons")]
	private static class NMainMenuRefreshButtonsPatch
	{
		private static void Prefix()
		{
			NormalizeRunSlots();
		}

		private static void Postfix(NMainMenu __instance)
		{
			int runCount = GetSlotEntries().Count;
			NMainMenuTextButton nodeOrNull = ((Node)__instance).GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/SingleplayerButton");
			NMainMenuTextButton nodeOrNull2 = ((Node)__instance).GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/ContinueButton");
			NMainMenuTextButton nodeOrNull3 = ((Node)__instance).GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/AbandonRunButton");
			if (runCount > 0)
			{
				if (nodeOrNull != null)
				{
					((CanvasItem)nodeOrNull).Visible = true;
					nodeOrNull.SetEnabled(enabled: true);
				}
				if (nodeOrNull2 != null)
				{
					((CanvasItem)nodeOrNull2).Visible = true;
					nodeOrNull2.SetEnabled(enabled: true);
				}
				if (nodeOrNull3 != null)
				{
					((CanvasItem)nodeOrNull3).Visible = true;
				}
				if (runCount > 1)
				{
					__instance.ContinueRunInfo.SetResult(null);
				}
				else
				{
					__instance.ContinueRunInfo.SetResult(SaveManager.Instance.LoadRunSave());
				}
			}
		}
	}

	[HarmonyPatch(typeof(NMainMenu), "SingleplayerButtonPressed")]
	private static class NMainMenuSingleplayerButtonPressedPatch
	{
		private static bool Prefix(NMainMenu __instance)
		{
			if (_allowSingleplayerStartBypass)
			{
				_allowSingleplayerStartBypass = false;
				return true;
			}
			NormalizeRunSlots();
			int distinctInProgressRunCount = GetDistinctInProgressRunCount();
			if (distinctInProgressRunCount >= MaxRunSlots)
			{
				TaskHelper.RunSafely(ShowInfoPopupAsync("Run Slots Full", $"You already have {MaxRunSlots} paused singleplayer runs. Continue or abandon one first."));
				return false;
			}
			if (distinctInProgressRunCount >= 1)
			{
				TaskHelper.RunSafely(ShowConfirmNewRunPopupAsync(__instance));
				return false;
			}
			return true;
		}
	}

	private static async Task ShowConfirmNewRunPopupAsync(NMainMenu mainMenu)
	{
		if (mainMenu == null || !GodotObject.IsInstanceValid(mainMenu))
		{
			return;
		}
		// EN: Vanilla really only expects one active run file.
		//     This popup is the heads-up that Save Sync is about to branch that world into slots,
		//     so the player does not feel like the old run quietly disappeared.
		// ZH: 原版其实只认一个活动跑图文件。
		//     这里先提醒一下 Save Sync 要把它分流进槽位里，免得玩家以为旧跑图被悄悄吞掉了。
		LocString body = new LocString("events", "ACT4_DYNAMIC_TEXT");
		LocString header = new LocString("events", "ACT4_DYNAMIC_TEXT");
		body.Add("text", "You already have an ongoing run, but Save Sync will save multiple runs for you.");
		header.Add("text", "Save Sync");
		await WaitForNoGenericPopupAsync();
		NGenericPopup val = NGenericPopup.Create();
		NModalContainer instance = await WaitForModalContainerAsync();
		if (val == null || instance == null)
		{
			return;
		}
		instance.Add(val);
		LocString noButton = new LocString("events", "ACT4_DYNAMIC_TEXT");
		LocString yesButton = new LocString("events", "ACT4_DYNAMIC_TEXT");
		noButton.Add("text", "BACK");
		yesButton.Add("text", "CONTINUE");
		bool flag = await val.WaitForConfirmation(body, header, noButton, yesButton);
		if (!flag)
		{
			return;
		}
		_allowSingleplayerStartBypass = true;
		AccessTools.Method(typeof(NMainMenu), "SingleplayerButtonPressed")?.Invoke(mainMenu, new object[1] { null });
	}

	[HarmonyPatch(typeof(NMainMenu), "OnContinueButtonPressed")]
	private static class NMainMenuOnContinueButtonPressedPatch
	{
		private static bool Prefix(NMainMenu __instance)
		{
			NormalizeRunSlots();
			List<RunSlotEntry> slotEntries = GetSlotEntries();
			if (slotEntries.Count <= 1)
			{
				return true;
			}
			TaskHelper.RunSafely(ShowRunSlotPickerAsync(continuePicker: true));
			return false;
		}
	}

	[HarmonyPatch(typeof(NMainMenu), "OnAbandonRunButtonPressed")]
	private static class NMainMenuOnAbandonRunButtonPressedPatch
	{
		private static bool Prefix(NMainMenu __instance)
		{
			NormalizeRunSlots();
			List<RunSlotEntry> slotEntries = GetSlotEntries();
			if (slotEntries.Count <= 1)
			{
				return true;
			}
			TaskHelper.RunSafely(ShowRunSlotPickerAsync(continuePicker: false));
			return false;
		}
	}

	private static async Task ShowRunSlotPickerAsync(bool continuePicker)
	{
		NormalizeRunSlots();
		List<RunSlotEntry> slotEntries = GetSlotEntries().OrderBy((RunSlotEntry s) => s.SlotId).ToList();
		if (slotEntries.Count <= 1)
		{
			return;
		}
		NMainMenu mainMenu = NGame.Instance?.MainMenu;
		if (mainMenu == null || !GodotObject.IsInstanceValid(mainMenu))
		{
			return;
		}
		// EN: We reuse the stock generic popup and page through slots one at a time.
		//     It looks a little homemade, but staying inside the vanilla modal flow keeps focus,
		//     controller input, and close behavior much less cursed than a custom window.
		// ZH: 这里借用原版通用弹窗，逐个翻看槽位。
		//     看着有点土，但继续走原版模态流程后，焦点、手柄输入和关闭逻辑都稳定得多。
		int num = 0;
		while (true)
		{
			await WaitForNoGenericPopupAsync();
			RunSlotEntry runSlotEntry = slotEntries[num];
			LocString body = new LocString("events", "ACT4_DYNAMIC_TEXT");
			LocString header = new LocString("events", "ACT4_DYNAMIC_TEXT");
			body.Add("text", FormatSlotSummaryWithIcons(runSlotEntry));
			header.Add("text", $"Save Sync: {(continuePicker ? "Continue" : "Abandon")} Run ({num + 1}/{slotEntries.Count})");
			NGenericPopup val = NGenericPopup.Create();
			NModalContainer instance = await WaitForModalContainerAsync();
			if (val == null || instance == null)
			{
				return;
			}
			instance.Add(val);
			bool closeRequested = false;
			AddCloseButtonToGenericPopup(val, delegate
			{
				closeRequested = true;
			});
			LocString noButton = new LocString("events", "ACT4_DYNAMIC_TEXT");
			LocString yesButton = new LocString("events", "ACT4_DYNAMIC_TEXT");
			noButton.Add("text", "Next Slot");
			yesButton.Add("text", continuePicker ? "Continue This Run" : "Abandon This Run");
			bool flag = await val.WaitForConfirmation(body, header, noButton, yesButton);
			if (closeRequested)
			{
				return;
			}
			if (flag)
			{
				if (continuePicker)
				{
					await ContinueFromSlotAsync(runSlotEntry.SlotId);
				}
				else
				{
					await AbandonSlotAsync(runSlotEntry.SlotId);
				}
				return;
			}
			await WaitForNoGenericPopupAsync();
			num = (num + 1) % slotEntries.Count;
		}
	}

	private static async Task<NModalContainer?> WaitForModalContainerAsync()
	{
		for (int i = 0; i < 20; i++)
		{
			NModalContainer instance = NModalContainer.Instance;
			if (instance != null && GodotObject.IsInstanceValid(instance))
			{
				return instance;
			}
			await Task.Delay(16);
		}
		return null;
	}

	private static async Task WaitForNoGenericPopupAsync()
	{
		for (int i = 0; i < 30; i++)
		{
			NModalContainer instance = NModalContainer.Instance;
			if (instance == null || !GodotObject.IsInstanceValid(instance))
			{
				return;
			}
			bool flag = false;
			foreach (Node child in instance.GetChildren())
			{
				if (child is NGenericPopup)
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				return;
			}
			await Task.Delay(16);
		}
	}

	private static void AddCloseButtonToGenericPopup(NGenericPopup popup, Action onClose)
	{
		NButton val = new NButton
		{
			Name = "Act4PlaceholderSlotPickerClose",
			FocusMode = Control.FocusModeEnum.All,
			MouseFilter = Control.MouseFilterEnum.Stop,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
			CustomMinimumSize = new Vector2(86f, 36f),
			Size = new Vector2(86f, 36f),
			TooltipText = "Close"
		};
		val.SetAnchorsPreset(Control.LayoutPreset.TopRight, false);
		val.Position = new Vector2(-96f, -60f);
		ColorRect val2 = new ColorRect
		{
			Color = new Color(0.18f, 0.24f, 0.31f, 0.95f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)val2).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Node)val).AddChild(val2, false, Node.InternalMode.Disabled);
		Label val3 = new Label
		{
			Text = "CLOSE",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)val3).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Control)val3).AddThemeFontSizeOverride("font_size", 14);
		((Control)val3).AddThemeColorOverride("font_color", new Color(0.94f, 0.97f, 1f, 1f));
		((Node)val).AddChild(val3, false, Node.InternalMode.Disabled);
		((GodotObject)val).Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(delegate(NButton _)
		{
			onClose?.Invoke();
			NButton nodeOrNull = ((Node)popup).GetNodeOrNull<NButton>("VerticalPopup/NoButton");
			if (nodeOrNull != null && GodotObject.IsInstanceValid(nodeOrNull))
			{
				((GodotObject)nodeOrNull).EmitSignal(NClickableControl.SignalName.Released, nodeOrNull);
			}
			else
			{
				NModalContainer.Instance?.Clear();
			}
		}), 0u);
		((Node)popup).AddChild(val, false, Node.InternalMode.Disabled);
	}

	private static async Task ContinueFromSlotAsync(int slotId)
	{
		try
		{
			NormalizeRunSlots();
			string profileSavesDir = GetProfileSavesDir();
			if (string.IsNullOrWhiteSpace(profileSavesDir))
			{
				return;
			}
			string slotFilePath = GetSlotFilePath(profileSavesDir, slotId);
			string currentRunPath = GetCurrentRunPath(profileSavesDir);
			if (!File.Exists(slotFilePath))
			{
				await ShowInfoPopupAsync("Continue Failed", $"Run slot {slotId} is missing.");
				return;
			}
			// EN: Continue still loads from current_run.save.
			//     Slot continue is really "copy the chosen slot back into the canonical path,
			//     then let vanilla keep doing the actual load".
			// ZH: 游戏继续时还是从 current_run.save 读取。
			//     所以这里本质上是先把槽位拷回原版认的文件名，再交给原版去真正加载。
			File.Copy(slotFilePath, currentRunPath, true);
			WriteActiveSlotId(profileSavesDir, slotId);
			ReadSaveResult<SerializableRun> readSaveResult = SaveManager.Instance.LoadRunSave();
			if (!readSaveResult.Success || readSaveResult.SaveData == null)
			{
				DisplayLoadSaveError();
				return;
			}
			NMainMenuTextButton nodeOrNull = ((Node)NGame.Instance.MainMenu).GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/ContinueButton");
			nodeOrNull?.Disable();
			NAudioManager.Instance?.StopMusic();
			SerializableRun saveData = readSaveResult.SaveData;
			RunState runState = RunState.FromSerializable(saveData);
			RunManager.Instance.SetUpSavedSinglePlayer(runState, saveData);
			SfxCmd.Play(runState.Players[0].Character.CharacterTransitionSfx);
			await NGame.Instance.Transition.FadeOut(0.8f, runState.Players[0].Character.CharacterSelectTransitionPath);
			NGame.Instance.ReactionContainer.InitializeNetworking(new MegaCrit.Sts2.Core.Multiplayer.NetSingleplayerGameService());
			await NGame.Instance.LoadRun(runState, saveData.PreFinishedRoom);
			await NGame.Instance.Transition.FadeIn();
		}
		catch (Exception ex)
		{
			Logger.Warn($"Continue from slot failed: {ex.Message}", 1);
			DisplayLoadSaveError();
		}
	}

	private static async Task AbandonSlotAsync(int slotId)
	{
		NMainMenu mainMenu = NGame.Instance?.MainMenu;
		if (mainMenu == null || !GodotObject.IsInstanceValid(mainMenu))
		{
			return;
		}
		string profileSavesDir = GetProfileSavesDir();
		if (string.IsNullOrWhiteSpace(profileSavesDir))
		{
			return;
		}
		string slotFilePath = GetSlotFilePath(profileSavesDir, slotId);
		if (!TryReadRun(slotFilePath, out var run))
		{
			await ShowInfoPopupAsync("Abandon Failed", $"Run slot {slotId} is missing or invalid.");
			NormalizeRunSlots();
			mainMenu.RefreshButtons();
			return;
		}
		LocString body = new LocString("events", "ACT4_DYNAMIC_TEXT");
		LocString header = new LocString("events", "ACT4_DYNAMIC_TEXT");
		RunSlotEntry entry = new RunSlotEntry
		{
			SlotId = slotId,
			FilePath = slotFilePath,
			Run = run
		};
		body.Add("text", FormatAbandonConfirmationWithIcons(entry));
		header.Add("text", "Confirm Abandon");
		await WaitForNoGenericPopupAsync();
		NGenericPopup val = NGenericPopup.Create();
		NModalContainer instance = await WaitForModalContainerAsync();
		if (val == null || instance == null)
		{
			return;
		}
		instance.Add(val);
		if (!await val.WaitForConfirmation(body, header, new LocString("main_menu_ui", "GENERIC_POPUP.cancel"), new LocString("main_menu_ui", "GENERIC_POPUP.confirm")))
		{
			mainMenu.RefreshButtons();
			return;
		}
		try
		{
			SaveManager.Instance.UpdateProgressWithRunData(run, victory: false);
			RunHistoryUtilities.CreateRunHistoryEntry(run, victory: false, isAbandoned: true, run.PlatformType);
			if (run.DailyTime.HasValue)
			{
				int score = ScoreUtility.CalculateScore(run, won: false);
				TaskHelper.RunSafely(DailyRunUtility.UploadScore(run.DailyTime.Value, score, run.Players));
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed uploading metrics for abandoned slot run: {ex.Message}", 1);
		}
		TryDeleteFile(slotFilePath);
		string currentRunPath = GetCurrentRunPath(profileSavesDir);
		if (TryReadRun(currentRunPath, out var run2) && run2.StartTime == run.StartTime)
		{
			TryDeleteFile(currentRunPath);
		}
		NormalizeRunSlots();
		mainMenu.RefreshButtons();
		GC.Collect();
	}

	private static async Task ShowInfoPopupAsync(string headerText, string bodyText)
	{
		LocString body = new LocString("events", "ACT4_DYNAMIC_TEXT");
		LocString header = new LocString("events", "ACT4_DYNAMIC_TEXT");
		body.Add("text", bodyText);
		header.Add("text", headerText);
		NGenericPopup val = NGenericPopup.Create();
		NModalContainer instance = NModalContainer.Instance;
		if (val == null || instance == null)
		{
			return;
		}
		instance.Add(val);
		await val.WaitForConfirmation(body, header, null, new LocString("main_menu_ui", "GENERIC_POPUP.confirm"));
	}

	private static void DisplayLoadSaveError()
	{
		NErrorPopup modalToCreate = NErrorPopup.Create(new LocString("main_menu_ui", "INVALID_SAVE_POPUP.title"), new LocString("main_menu_ui", "INVALID_SAVE_POPUP.description_run"), new LocString("main_menu_ui", "INVALID_SAVE_POPUP.dismiss"), showReportBugButton: true);
		NModalContainer.Instance.Add(modalToCreate);
		NModalContainer.Instance.ShowBackstop();
	}

	private static void NormalizeRunSlots()
	{
		try
		{
			// EN: This is the janitor for the whole slot system.
			//     It keeps numbered slot files, vanilla's current_run.save, and our active-slot marker
			//     telling the same story after quits, crashes, and old leftovers.
			// ZH: 这是整个多槽系统的清洁工。
			//     它会让编号槽位、原版 current_run.save，以及我们的激活槽标记在退出、崩溃、残留旧档后重新对齐。
			string profileSavesDir = GetProfileSavesDir();
			if (string.IsNullOrWhiteSpace(profileSavesDir))
			{
				return;
			}
			Directory.CreateDirectory(profileSavesDir);
			List<RunSlotEntry> slotEntries = GetSlotEntries(profileSavesDir, deleteInvalid: true);
			PruneFinishedRuns(slotEntries, profileSavesDir);
			slotEntries = GetSlotEntries(profileSavesDir, deleteInvalid: true);
			string currentRunPath = GetCurrentRunPath(profileSavesDir);
			if (TryReadRun(currentRunPath, out var run))
			{
				RunSlotEntry runSlotEntry = slotEntries.FirstOrDefault((RunSlotEntry s) => s.StartTime == run.StartTime);
				if (runSlotEntry != null)
				{
					File.Copy(currentRunPath, runSlotEntry.FilePath, true);
					WriteActiveSlotId(profileSavesDir, runSlotEntry.SlotId);
				}
				else
				{
					int availableSlotId = GetAvailableSlotId(slotEntries);
					if (availableSlotId == -1)
					{
						RunSlotEntry runSlotEntry2 = slotEntries.OrderBy((RunSlotEntry s) => s.SaveTime).FirstOrDefault();
						availableSlotId = ((runSlotEntry2 != null) ? runSlotEntry2.SlotId : 1);
					}
					File.Copy(currentRunPath, GetSlotFilePath(profileSavesDir, availableSlotId), true);
					WriteActiveSlotId(profileSavesDir, availableSlotId);
				}
			}
			slotEntries = GetSlotEntries(profileSavesDir, deleteInvalid: true);
			if (!File.Exists(currentRunPath))
			{
				RunSlotEntry preferredSlot = GetPreferredSlot(slotEntries, profileSavesDir);
				if (preferredSlot != null)
				{
					File.Copy(preferredSlot.FilePath, currentRunPath, true);
					WriteActiveSlotId(profileSavesDir, preferredSlot.SlotId);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"NormalizeRunSlots failed: {ex.Message}", 1);
		}
	}

	private static void PruneFinishedRuns(List<RunSlotEntry> slotEntries, string profileSavesDir)
	{
		string path = Path.Combine(profileSavesDir, "history");
		if (!Directory.Exists(path))
		{
			return;
		}
		foreach (RunSlotEntry slotEntry in slotEntries)
		{
			if (slotEntry.StartTime > 0 && File.Exists(Path.Combine(path, $"{slotEntry.StartTime}.run")))
			{
				TryDeleteFile(slotEntry.FilePath);
			}
		}
	}

	private static int GetDistinctInProgressRunCount()
	{
		string profileSavesDir = GetProfileSavesDir();
		if (string.IsNullOrWhiteSpace(profileSavesDir))
		{
			return 0;
		}
		HashSet<long> hashSet = new HashSet<long>(GetSlotEntries(profileSavesDir, deleteInvalid: true).Select((RunSlotEntry s) => s.StartTime));
		if (TryReadRun(GetCurrentRunPath(profileSavesDir), out var run))
		{
			hashSet.Add(run.StartTime);
		}
		return hashSet.Count;
	}

	private static List<RunSlotEntry> GetSlotEntries()
	{
		string profileSavesDir = GetProfileSavesDir();
		if (string.IsNullOrWhiteSpace(profileSavesDir))
		{
			return new List<RunSlotEntry>();
		}
		return GetSlotEntries(profileSavesDir, deleteInvalid: true);
	}

	private static List<RunSlotEntry> GetSlotEntries(string profileSavesDir, bool deleteInvalid)
	{
		// EN: If a slot file cannot round-trip through SaveManager, we stop trusting it.
		//     Broken JSON is not "maybe fine later", it just keeps poisoning the menu state.
		// ZH: 只要槽位文件不能被 SaveManager 正常读回，这里就不再信它。
		//     坏 JSON 不是“以后也许能好”，只会持续污染菜单状态。
		List<RunSlotEntry> list = new List<RunSlotEntry>();
		for (int i = 1; i <= MaxRunSlots; i++)
		{
			string slotFilePath = GetSlotFilePath(profileSavesDir, i);
			if (!File.Exists(slotFilePath))
			{
				continue;
			}
			if (TryReadRun(slotFilePath, out var run))
			{
				list.Add(new RunSlotEntry
				{
					SlotId = i,
					FilePath = slotFilePath,
					Run = run
				});
			}
			else if (deleteInvalid)
			{
				TryDeleteFile(slotFilePath);
			}
		}
		return list;
	}

	private static bool TryReadRun(string filePath, out SerializableRun run)
	{
		run = null;
		try
		{
			if (!File.Exists(filePath))
			{
				return false;
			}
			ReadSaveResult<SerializableRun> readSaveResult = SaveManager.FromJson<SerializableRun>(File.ReadAllText(filePath));
			if (!readSaveResult.Success || readSaveResult.SaveData == null)
			{
				return false;
			}
			run = readSaveResult.SaveData;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static RunSlotEntry GetPreferredSlot(List<RunSlotEntry> slots, string profileSavesDir)
	{
		if (slots == null || slots.Count == 0)
		{
			return null;
		}
		int activeSlotId = ReadActiveSlotId(profileSavesDir);
		RunSlotEntry runSlotEntry = slots.FirstOrDefault((RunSlotEntry s) => s.SlotId == activeSlotId);
		if (runSlotEntry != null)
		{
			return runSlotEntry;
		}
		return slots.OrderByDescending((RunSlotEntry s) => s.SaveTime).FirstOrDefault();
	}

	private static int GetAvailableSlotId(List<RunSlotEntry> slots)
	{
		for (int i = 1; i <= MaxRunSlots; i++)
		{
			if (!slots.Any((RunSlotEntry s) => s.SlotId == i))
			{
				return i;
			}
		}
		return -1;
	}

	private static string GetProfileSavesDir()
	{
		SaveManager instance = SaveManager.Instance;
		if (instance == null)
		{
			return string.Empty;
		}
		return ResolveAbsolutePath(instance.GetProfileScopedPath(UserDataPathProvider.SavesDir));
	}

	private static string ResolveAbsolutePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return string.Empty;
		}
		if (path.StartsWith("user://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			return ProjectSettings.GlobalizePath(path);
		}
		return path;
	}

	private static bool ResourceOrFileExists(string resourcePath)
	{
		if (string.IsNullOrWhiteSpace(resourcePath))
		{
			return false;
		}
		try
		{
			if (ResourceLoader.Exists(resourcePath))
			{
				return true;
			}
		}
		catch
		{
		}
		string absolutePath = ResolveAbsolutePath(resourcePath);
		return !string.IsNullOrWhiteSpace(absolutePath) && File.Exists(absolutePath);
	}

	private static string GetCurrentRunPath(string profileSavesDir)
	{
		return Path.Combine(profileSavesDir, CurrentRunFileName);
	}

	private static string GetSlotFilePath(string profileSavesDir, int slotId)
	{
		return Path.Combine(profileSavesDir, $"{SlotFileNamePrefix}{slotId}{SlotFileNameSuffix}");
	}

	private static int ReadActiveSlotId(string profileSavesDir)
	{
		try
		{
			string path = Path.Combine(profileSavesDir, ActiveSlotFileName);
			if (!File.Exists(path))
			{
				return 1;
			}
			if (!int.TryParse(File.ReadAllText(path).Trim(), out var result))
			{
				return 1;
			}
			return Math.Clamp(result, 1, MaxRunSlots);
		}
		catch
		{
			return 1;
		}
	}

	private static void WriteActiveSlotId(string profileSavesDir, int slotId)
	{
		try
		{
			File.WriteAllText(Path.Combine(profileSavesDir, ActiveSlotFileName), Math.Clamp(slotId, 1, MaxRunSlots).ToString(CultureInfo.InvariantCulture));
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed writing active slot id: {ex.Message}", 1);
		}
	}

	private static void TryDeleteFile(string filePath)
	{
		try
		{
			if (File.Exists(filePath))
			{
				File.Delete(filePath);
			}
		}
		catch
		{
		}
	}

	private static string FormatSlotOptionLabel(RunSlotEntry entry)
	{
		SerializableRun run = entry.Run;
		string runTypeLabel = GetRunTypeLabel(run);
		SerializablePlayer serializablePlayer = run.Players.FirstOrDefault();
		string text = ModLoc.T("Unknown", "未知", fra: "Inconnu", deu: "Unbekannt", jpn: "不明", kor: "알 수 없음", por: "Desconhecido", rus: "Неизвестно", spa: "Desconocido");
		if (serializablePlayer != null)
		{
			try
			{
				CharacterModel byId = ModelDb.GetById<CharacterModel>(serializablePlayer.CharacterId);
				if (byId != null)
				{
					text = byId.Title.GetFormattedText();
				}
			}
			catch
			{
			}
		}
		string text2 = ModLoc.T("Act ?", "第 ? 幕", fra: "Acte ?", deu: "Akt ?", jpn: "第?章", kor: "제? 막", por: "Ato ?", rus: "Акт ?", spa: "Acto ?");
		int num = 0;
		try
		{
			if (run.Acts != null && run.Acts.Count > 0 && run.CurrentActIndex >= 0 && run.CurrentActIndex < run.Acts.Count)
			{
				ActModel byId2 = ModelDb.GetById<ActModel>(run.Acts[run.CurrentActIndex].Id);
				if (byId2 != null)
				{
					text2 = byId2.Title.GetFormattedText();
				}
			}
			num = run.VisitedMapCoords?.Count ?? 0;
			for (int i = 0; i < run.CurrentActIndex && run.Acts != null && i < run.Acts.Count; i++)
			{
				ActModel byId3 = ModelDb.GetById<ActModel>(run.Acts[i].Id);
				if (byId3 != null)
				{
					num += byId3.GetNumberOfFloors(run.Players.Count > 1);
				}
			}
		}
		catch
		{
		}
		string text3 = TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(run.SaveTime).UtcDateTime, TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
		string text4 = TimeFormatting.Format(Math.Max(0L, run.RunTime));
		int num2 = serializablePlayer?.CurrentHp ?? 0;
		int num3 = serializablePlayer?.MaxHp ?? 0;
		int num4 = serializablePlayer?.Gold ?? 0;
		string text5 = ((run.Ascension > 0) ? $" A{run.Ascension}" : string.Empty);
		return $"{ModLoc.T("Slot ", "槽位", fra: "Emplacement ", deu: "Slot ", jpn: "スロット", kor: "슬롯 ", por: "Slot ", rus: "Слот ", spa: "Ranura ")}{entry.SlotId}: {runTypeLabel}\n{text}\n{text2}{ModLoc.T(" Floor ", " 层数", fra: " Étage ", deu: " Etage ", jpn: " フロア", kor: " 콘 ", por: " Andar ", rus: " Этаж ", spa: " Piso ")}{num}{ModLoc.T(" | HP ", " | 生命 ", fra: " | PV ", deu: " | LP ", jpn: " | HP ", kor: " | HP ", por: " | HP ", rus: " | ОЗ ", spa: " | PS ")}{num2}/{num3}{ModLoc.T(" | Gold ", " | 金币 ", fra: " | Or ", deu: " | Gold ", jpn: " | G ", kor: " | 골드 ", por: " | Ouro ", rus: " | Злт ", spa: " | Oro ")}{num4}{ModLoc.T(" | Time ", " | 时长 ", fra: " | Durée ", deu: " | Zeit ", jpn: " | 時間 ", kor: " | 시간 ", por: " | Tempo ", rus: " | Время ", spa: " | Tiempo ")}{text4}{ModLoc.T(" | Saved ", " | 保存于 ", fra: " | Sauvegardé ", deu: " | Gespeichert ", jpn: " | 保存 ", kor: " | 저장 ", por: " | Salvo ", rus: " | Сохранено ", spa: " | Guardado ")}{text3}{text5}";
	}

	private static string FormatSlotSummaryWithIcons(RunSlotEntry entry)
	{
		SerializableRun run = entry.Run;
		string runTypeLabel = GetRunTypeLabel(run);
		SerializablePlayer serializablePlayer = run.Players.FirstOrDefault();
		string text = ModLoc.T("Unknown", "未知", fra: "Inconnu", deu: "Unbekannt", jpn: "不明", kor: "알 수 없음", por: "Desconhecido", rus: "Неизвестно", spa: "Desconocido");
		string text2 = string.Empty;
		if (serializablePlayer != null)
		{
			try
			{
				CharacterModel byId = ModelDb.GetById<CharacterModel>(serializablePlayer.CharacterId);
				if (byId != null)
				{
					text = byId.Title.GetFormattedText();
					string text6 = byId.Id.Entry.ToLowerInvariant();
					string iconPath = $"res://images/ui/top_panel/character_icon_{text6}.png";
					if (ResourceOrFileExists(iconPath))
					{
						text2 = $"[img]{iconPath}[/img] ";
					}
				}
			}
			catch
			{
			}
		}
		string text4 = ModLoc.T("Act ?", "第 ? 幕", fra: "Acte ?", deu: "Akt ?", jpn: "第?章", kor: "제? 막", por: "Ato ?", rus: "Акт ?", spa: "Acto ?");
		int num = 0;
		try
		{
			if (run.Acts != null && run.Acts.Count > 0 && run.CurrentActIndex >= 0 && run.CurrentActIndex < run.Acts.Count)
			{
				ActModel byId2 = ModelDb.GetById<ActModel>(run.Acts[run.CurrentActIndex].Id);
				if (byId2 != null)
				{
					text4 = byId2.Title.GetFormattedText();
				}
			}
			num = run.VisitedMapCoords?.Count ?? 0;
			for (int i = 0; i < run.CurrentActIndex && run.Acts != null && i < run.Acts.Count; i++)
			{
				ActModel byId3 = ModelDb.GetById<ActModel>(run.Acts[i].Id);
				if (byId3 != null)
				{
					num += byId3.GetNumberOfFloors(run.Players.Count > 1);
				}
			}
		}
		catch
		{
		}
		string text3 = TimeFormatting.Format(Math.Max(0L, run.RunTime));
		int num2 = serializablePlayer?.CurrentHp ?? 0;
		int num3 = serializablePlayer?.MaxHp ?? 0;
		int num4 = serializablePlayer?.Gold ?? 0;
		string text5 = ((run.Ascension > 0) ? $" A{run.Ascension}" : string.Empty);
		return $"{ModLoc.T("Slot ", "槽位", fra: "Emplacement ", deu: "Slot ", jpn: "スロット", kor: "슬롯 ", por: "Slot ", rus: "Слот ", spa: "Ranura ")}{entry.SlotId}  |  {runTypeLabel}\n{text2}{text}\n{text4}{ModLoc.T(" Floor ", " 层数", fra: " Étage ", deu: " Etage ", jpn: " フロア", kor: " 콘 ", por: " Andar ", rus: " Этаж ", spa: " Piso ")}{num}{text5}\n❤ {num2}/{num3}   💰 {num4}   ⏱ {text3}";
	}

	private static string GetRunTypeLabel(SerializableRun run)
	{
		if (run?.DailyTime.HasValue == true)
		{
			return ModLoc.T("Daily Run", "每日挑战", fra: "Partie quotidienne", deu: "Täglicher Lauf", jpn: "デイリーラン", kor: "데일리 런", por: "Run Diária", rus: "Ежедневный забег", spa: "Partida diaria");
		}
		if (run?.Modifiers != null && run.Modifiers.Count > 0)
		{
			return ModLoc.T("Custom Run", "自定义模式", fra: "Partie personnalisée", deu: "Benutzerdefinierter Lauf", jpn: "カスタムラン", kor: "커스텀 런", por: "Run Personalizada", rus: "Произвольный забег", spa: "Partida personalizada");
		}
		return ModLoc.T("Standard Run", "标准模式", fra: "Partie standard", deu: "Standardlauf", jpn: "スタンダードラン", kor: "표준 런", por: "Run Padrão", rus: "Стандартный забег", spa: "Partida estándar");
	}

	private static string FormatAbandonConfirmationWithIcons(RunSlotEntry entry)
	{
		string text = TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(entry.Run.SaveTime).UtcDateTime, TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
		return $"{ModLoc.T("Abandon this run?", "要放弃这局游戲吗？", fra: "Abandonner cette partie ?", deu: "Diesen Spielstand abbrechen?", jpn: "このランを放棄しますか？", kor: "이 런을 포기하시겠습니까?", por: "Abandonar esta run?", rus: "Отказаться от этого забега?", spa: "¿Abandonar esta partida?")}\n\n{FormatSlotSummaryWithIcons(entry)}\n{ModLoc.T("Saved: ", "保存时间：", fra: "Sauvegardé : ", deu: "Gespeichert: ", jpn: "保存時刻：", kor: "저장 시각: ", por: "Salvo: ", rus: "Сохранено: ", spa: "Guardado: ")}{text}\n\n{ModLoc.T("This cannot be undone.", "此操作无法撤销。", fra: "Cette action est irréversible.", deu: "Diese Aktion kann nicht rükgängig gemacht werden.", jpn: "この操作は取り消せません。", kor: "이 작업은 취소할 수 없습니다.", por: "Esta ação não pode ser desfeita.", rus: "Это действие нельзя отменить.", spa: "Esta acción no se puede deshacer.")}";
	}
}
