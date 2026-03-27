//=============================================================================
// ModEntry.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Main mod entry point; registers every Harmony patch on initialization and reports success/failure counts at startup.
// ZH: Mod主入口；在初始化时注册所有Harmony补丁，并在启动时报告成功/失败数量。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.addons.mega_text;

namespace Act4Placeholder;

[ModInitializer("Init")]
public static class ModEntry
{
	public const string ModVersion = "0.1.2c";

	private static readonly MegaCrit.Sts2.Core.Logging.Logger Logger =
		new MegaCrit.Sts2.Core.Logging.Logger("Act4Placeholder", (LogType)0);

	/// <summary>
	/// Number of Harmony patches that were successfully applied during Init().
	/// Exposed so the main-menu UI can display a confirmation indicator.
	/// </summary>
	public static int PatchesApplied { get; private set; }

	/// <summary>
	/// Number of Harmony patch classes that failed during Init().
	/// </summary>
	public static int PatchesFailed { get; private set; }

	/// <summary>
	/// Detailed list of patch failures for diagnostic display.
	/// </summary>
	public static List<string> PatchFailures { get; } = new();

	/// <summary>
	/// True once Init() has completed (regardless of individual patch outcomes).
	/// </summary>
	public static bool InitCompleted { get; private set; }

	[HarmonyPatch(typeof(NGameOverScreen), "_Ready")]
	private static class NGameOverScreenReadyPatch
	{
		private static void Postfix(NGameOverScreen __instance)
		{
			RunState value = Traverse.Create((object)__instance).Field<RunState>("_runState").Value;
			RunHistory value2 = Traverse.Create((object)__instance).Field<RunHistory>("_history").Value;
			if (value == null || value2 == null || !ModSupport.IsAct4Placeholder(value) || !value.IsGameOver)
			{
				return;
			}
			bool bossDefeated = ModSupport.WasAct4BossActuallyDefeated(value) || ModSupport.ShouldTreatCurrentAct4BossRoomAsVictory(value);
			if (!bossDefeated)
			{
				AbstractRoom currentRoom = value.CurrentRoom;
				CombatRoom val = currentRoom as CombatRoom;
				ModelId killedByEncounter = ((val != null) ? ((AbstractModel)val.Encounter).Id : value2.KilledByEncounter);
				RunHistory value3 = new RunHistory
				{
					SchemaVersion = value2.SchemaVersion,
					PlatformType = value2.PlatformType,
					GameMode = value2.GameMode,
					Win = false,
					Seed = value2.Seed,
					StartTime = value2.StartTime,
					RunTime = value2.RunTime,
					Ascension = value2.Ascension,
					BuildId = value2.BuildId,
					WasAbandoned = value2.WasAbandoned,
					KilledByEncounter = killedByEncounter,
					KilledByEvent = value2.KilledByEvent,
					Players = value2.Players,
					Acts = value2.Acts,
					Modifiers = value2.Modifiers,
					MapPointHistory = value2.MapPointHistory
				};
				Traverse.Create((object)__instance).Field<RunHistory>("_history").Value = value3;
				Traverse.Create((object)__instance).Method("InitializeBannerAndQuote", Array.Empty<object>()).GetValue();
			}
			else if (value2.Win)
			{
				MegaRichTextLabel value4 = Traverse.Create((object)__instance).Field<MegaRichTextLabel>("_victoryDamageLabel").Value;
				MegaRichTextLabel value5 = Traverse.Create((object)__instance).Field<MegaRichTextLabel>("_deathQuote").Value;
				if (value4 != null)
				{
					value4.Text = ModLoc.T("The Architect is defeated. The summit opens, and your ascent is complete.",
						"建筑师已被击败。通往巾峰的大门已经开启，你的攀登终于完成。",
						fra: "L'Architecte est vaincu. Le sommet s'ouvre, et votre ascension est achevée.",
						deu: "Der Architekt ist besiegt. Der Gipfel öffnet sich, und euer Aufstieg ist vollbracht.",
						jpn: "建築士が打ち倒された。頂へ至る道が開かれ、江の是非は完遂した。",
						kor: "건축가가 센러졌다. 정상이 열리고, 너희의 등정은 완성되었다.",
						por: "O Arquiteto foi derrotado. O cume se abre, e sua ascensão está completa.",
						rus: "Архитектор повержен. Вершина открылась, и ваше восхождение завершено.",
						spa: "El Arquitecto ha sido derrotado. La cima se abre, y su ascenso está completo.");
				}
				if (value5 != null)
				{
					value5.Text = string.Empty;
				}
				Traverse.Create((object)__instance).Field<string>("_encounterQuote").Value = ModLoc.T(
					"The Architect has fallen. The last design is broken, and the way above is finally yours.",
					"建筑师已经倒下，最后的设计已被粉碎，通往更高处的道路终于属于你。",
					fra: "L'Architecte est tombé. Le dernier plan est brisé, et la voie vers le haut vous appartient enfin.",
					deu: "Der Architekt ist gefallen. Der letzte Entwurf ist zerbrochen, und der Weg nach oben gehört euch endlich.",
					jpn: "建築士は倒れた。最後の設計は砕かれ、上へと続く道はついにあなたのものとなった。",
					kor: "건축가가 쿞러졌다. 마지막 설계는 부서지고, 위로 가는 길은 마침내 너희 것이 되었다.",
					por: "O Arquiteto caiu. O último projeto está destruído, e o caminho acima é finalmente seu.",
					rus: "Архитектор пал. Последний замысел разрушен, и путь наверх наконец принадлежит вам.",
					spa: "El Arquitecto ha caído. El último diseño está roto, y el camino hacia arriba es finalmente suyo.");
			}
		}
	}

	[HarmonyPatch(typeof(NGameOverScreen), "InitializeBannerAndQuote")]
	private static class NGameOverScreenInitializeBannerAndQuotePatch
	{
		private static void Postfix(NGameOverScreen __instance)
		{
			RunState runState = Traverse.Create((object)__instance).Field<RunState>("_runState").Value;
			RunHistory history = Traverse.Create((object)__instance).Field<RunHistory>("_history").Value;
			if (runState == null || history == null || !history.Win || !ModSupport.IsAct4Placeholder(runState))
			{
				return;
			}
			if (!ModSupport.WasAct4BossActuallyDefeated(runState) && !ModSupport.ShouldTreatCurrentAct4BossRoomAsVictory(runState))
			{
				return;
			}
			MegaRichTextLabel victoryDamageLabel = Traverse.Create((object)__instance).Field<MegaRichTextLabel>("_victoryDamageLabel").Value;
			MegaRichTextLabel deathQuote = Traverse.Create((object)__instance).Field<MegaRichTextLabel>("_deathQuote").Value;
			object banner = Traverse.Create((object)__instance).Field("_banner").GetValue();
			MegaLabel bannerLabel = ((banner != null) ? Traverse.Create(banner).Field<MegaLabel>("label").Value : null);
			if (bannerLabel != null)
			{
				bannerLabel.SetTextAutoSize(ModLoc.T("Victory",
					"胜利",
					fra: "Victoire", deu: "Sieg", jpn: "勝利", kor: "승리",
					por: "Vitória", rus: "Победа", spa: "Victoria"));
			}
			if (victoryDamageLabel != null)
			{
				victoryDamageLabel.Text = ModLoc.T(
					"The Architect is defeated. The summit opens, and your ascent is complete.",
					"建筑师已被击败。通往巾峰的大门已经开启，你的攀登终于完成。",
					fra: "L'Architecte est vaincu. Le sommet s'ouvre, et votre ascension est achevée.",
					deu: "Der Architekt ist besiegt. Der Gipfel öffnet sich, und euer Aufstieg ist vollbracht.",
					jpn: "建築士が打ち倒された。頂へ至る道が開かれ、江の是非は完遂した。",
					kor: "건축가가 센러졌다. 정상이 열리고, 너희의 등정은 완성되었다.",
					por: "O Arquiteto foi derrotado. O cume se abre, e sua ascensão está completa.",
					rus: "Архитектор повержен. Вершина открылась, и ваше восхождение завершено.",
					spa: "El Arquitecto ha sido derrotado. La cima se abre, y su ascenso está completo.");
			}
			if (deathQuote != null)
			{
				deathQuote.Text = string.Empty;
			}
			Traverse.Create((object)__instance).Field<string>("_encounterQuote").Value = ModLoc.T(
				"The Architect has fallen. The last design is broken, and the way above is finally yours.",
				"建筑师已经倒下，最后的设计已被粉碎，通往更高处的道路终于属于你。",
				fra: "L'Architecte est tombé. Le dernier plan est brisé, et la voie vers le haut vous appartient enfin.",
				deu: "Der Architekt ist gefallen. Der letzte Entwurf ist zerbrochen, und der Weg nach oben gehört euch endlich.",
				jpn: "建築士は倒れた。最後の設計は砕かれ、上へと続く道はついにあなたのものとなった。",
				kor: "건축가가 쿞러졌다. 마지막 설계는 부서지고, 위로 가는 길은 마침내 너희 것이 되었다.",
				por: "O Arquiteto caiu. O último projeto está destruído, e o caminho acima é finalmente seu.",
				rus: "Архитектор пал. Последний замысел разрушен, и путь наверх наконец принадлежит вам.",
				spa: "El Arquitecto ha caído. El último diseño está roto, y el camino hacia arriba es finalmente suyo.");
		}
	}

	[HarmonyPatch(typeof(NPower), "OnHovered")]
	private static class NPowerOnHoveredPatch
	{
		private static bool Prefix(NPower __instance)
		{
			if (__instance?.Model?.Owner?.Monster is not Act4ArchitectBoss)
			{
				return true;
			}
			if (__instance.Model.HoverTips == null)
			{
				return true;
			}
			NHoverTipSet.Remove(__instance);
			NHoverTipSet nHoverTipSet = NHoverTipSet.CreateAndShow(__instance, __instance.Model.HoverTips, HoverTipAlignment.Right);
			if (nHoverTipSet == null)
			{
				return true;
			}
			if (!NHoverTipSet.shouldBlockHoverTips)
			{
				try
				{
					nHoverTipSet.SetFollowOwner();
					nHoverTipSet.SetExtraFollowOffset(new Vector2(56f, 8f));
				}
				catch (Exception)
				{
					// Fallback to engine default handling if follow-target data is unavailable.
					return true;
				}
			}
			TextureRect icon = __instance.GetNodeOrNull<TextureRect>("%Icon");
			if (icon != null)
			{
				icon.Scale = Vector2.One * 1.1f;
			}
			return false;
		}
	}

	[HarmonyPatch(typeof(NPower), "OnUnhovered")]
	private static class NPowerOnUnhoveredPatch
	{
		private static bool Prefix(NPower __instance)
		{
			if (__instance?.Model?.Owner?.Monster is not Act4ArchitectBoss)
			{
				return true;
			}
			NHoverTipSet.Remove(__instance);
			TextureRect icon = __instance.GetNodeOrNull<TextureRect>("%Icon");
			if (icon != null)
			{
				icon.Scale = Vector2.One;
			}
			return false;
		}
	}

	/// <summary>
	/// Applies all Harmony patches from a single type, wrapped in error handling.
	/// Returns true on success, false on failure.
	/// </summary>
	private static bool TryPatch(Harmony harmony, Type patchType, string label)
	{
		if (patchType == null)
		{
			Log.Error($"[Act4Placeholder]   FAIL: {label} - patch type not found (null)");
			Act4Logger.Error($"  FAIL: {label} - patch type not found");
			PatchFailures.Add($"{label}: type not found");
			return false;
		}
		try
		{
			harmony.CreateClassProcessor(patchType).Patch();
			Log.Info($"[Act4Placeholder]   OK: {label}", 2);
			Act4Logger.Info($"  OK: {label}");
			return true;
		}
		catch (Exception ex)
		{
			Log.Error($"[Act4Placeholder]   FAIL: {label} - {ex.GetType().Name}: {ex.Message}");
			Log.Error($"[Act4Placeholder]     StackTrace: {ex.StackTrace}");
			if (ex.InnerException != null)
			{
				Log.Error($"[Act4Placeholder]     Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
			}
			Act4Logger.Error($"  FAIL: {label} - {ex.GetType().Name}: {ex.Message}");
			PatchFailures.Add($"{label}: {ex.GetType().Name}: {ex.Message}");
			return false;
		}
	}

	public static void Init()
	{
		Act4Logger.Initialize(ModVersion);

		Log.Info($"[Act4Placeholder] v{ModVersion} - Initializing...", 2);
		Act4Logger.Info($"v{ModVersion} - Initializing...");
		Log.Info($"[Act4Placeholder]   .NET Runtime: {System.Environment.Version}", 2);
		Log.Info($"[Act4Placeholder]   OS: {System.Environment.OSVersion}", 2);

		try
		{
			var gameAssembly = typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu).Assembly;
			Log.Info($"[Act4Placeholder]   Game Assembly: {gameAssembly.GetName().Name} v{gameAssembly.GetName().Version}", 2);
			Act4Logger.Info($"  Game Assembly: {gameAssembly.GetName().Name} v{gameAssembly.GetName().Version}");
		}
		catch (Exception ex)
		{
			Log.Error($"[Act4Placeholder]   Could not read game assembly version: {ex.Message}");
			Act4Logger.Warn($"  Could not read game assembly version: {ex.Message}");
		}

		Harmony harmony = new Harmony("act4placeholder.mod");
		int success = 0;
		int fail = 0;

		// ────────────────────────────────────────────────────────
		// Group 1: Core - Main Menu UI (SaveSync + RunSlots)
		// ────────────────────────────────────────────────────────
		Log.Info("[Act4Placeholder] Patching Group 1: Main Menu UI...", 2);
		Act4Logger.Info("Patching Group 1: Main Menu UI (SaveSync + RunSlots)");

		if (TryPatch(harmony, typeof(UnifiedSavePathPatches).GetNestedType("NMainMenuReadyPatch", BindingFlags.NonPublic | BindingFlags.Static), "SaveSync - NMainMenu._Ready")) success++; else fail++;
		if (TryPatch(harmony, typeof(UnifiedSavePathPatches).GetNestedType("NMainMenuOnSubmenuStackChangedPatch", BindingFlags.NonPublic | BindingFlags.Static), "SaveSync - NMainMenu.OnSubmenuStackChanged")) success++; else fail++;
		if (TryPatch(harmony, typeof(SingleplayerRunSlotsPatches).GetNestedType("NMainMenuReadyPatch", BindingFlags.NonPublic | BindingFlags.Static), "RunSlots - NMainMenu._Ready")) success++; else fail++;
		if (TryPatch(harmony, typeof(SingleplayerRunSlotsPatches).GetNestedType("NMainMenuOnSubmenuStackChangedPatch", BindingFlags.NonPublic | BindingFlags.Static), "RunSlots - NMainMenu.OnSubmenuStackChanged")) success++; else fail++;
		if (TryPatch(harmony, typeof(SingleplayerRunSlotsPatches).GetNestedType("NMainMenuRefreshButtonsPatch", BindingFlags.NonPublic | BindingFlags.Static), "RunSlots - NMainMenu.RefreshButtons")) success++; else fail++;
		if (TryPatch(harmony, typeof(SingleplayerRunSlotsPatches).GetNestedType("NMainMenuSingleplayerButtonPressedPatch", BindingFlags.NonPublic | BindingFlags.Static), "RunSlots - NMainMenu.SingleplayerButtonPressed")) success++; else fail++;
		if (TryPatch(harmony, typeof(SingleplayerRunSlotsPatches).GetNestedType("NMainMenuOnContinueButtonPressedPatch", BindingFlags.NonPublic | BindingFlags.Static), "RunSlots - NMainMenu.OnContinueButtonPressed")) success++; else fail++;
		if (TryPatch(harmony, typeof(SingleplayerRunSlotsPatches).GetNestedType("NMainMenuOnAbandonRunButtonPressedPatch", BindingFlags.NonPublic | BindingFlags.Static), "RunSlots - NMainMenu.OnAbandonRunButtonPressed")) success++; else fail++;

		// ────────────────────────────────────────────────────────
		// Group 2: Core - Architect Event & Act 4 Injection
		// ────────────────────────────────────────────────────────
		Log.Info("[Act4Placeholder] Patching Group 2: Architect Event & Act 4 Injection...", 2);
		Act4Logger.Info("Patching Group 2: Architect Event & Act 4 Injection");

		if (TryPatch(harmony, typeof(EventModelSetEventStatePatch), "Architect - EventModel.SetEventState")) success++; else fail++;
		if (TryPatch(harmony, typeof(TheArchitectIsSharedPatch), "Architect - EventModel.get_IsShared")) success++; else fail++;
		if (TryPatch(harmony, typeof(EventSynchronizerArchitectChoicePatch), "Architect - EventSynchronizer.ChooseSharedEventOption")) success++; else fail++;
		if (TryPatch(harmony, typeof(EventSynchronizerArchitectHostVotePatch), "Architect - EventSynchronizer.PlayerVotedForSharedOptionIndex")) success++; else fail++;
		if (TryPatch(harmony, typeof(NEventRoomAct4HostChoicePatch), "Architect - NEventRoom host-only Act 4 choice UI")) success++; else fail++;
		if (TryPatch(harmony, typeof(EventSynchronizerGrandLibraryChoicePatch), "GrandLibrary - EventSynchronizer shared vote (host-resolve)")) success++; else fail++;
		if (TryPatch(harmony, typeof(RunManagerEnterNextActPatch), "Act4 - RunManager.EnterNextAct")) success++; else fail++;
		if (TryPatch(harmony, typeof(RunManagerOnEndedPatch), "Act4 - RunManager.OnEnded")) success++; else fail++;
		if (TryPatch(harmony, typeof(NPauseMenuSaveAndQuitPatch), "Act4 - NPauseMenu.OnSaveAndQuitButtonPressed")) success++; else fail++;

		// ────────────────────────────────────────────────────────
		// Group 3: Act 4 Map, Rooms & Rewards
		// ────────────────────────────────────────────────────────
		Log.Info("[Act4Placeholder] Patching Group 3: Map, Rooms & Rewards...", 2);
		Act4Logger.Info("Patching Group 3: Map, Rooms & Rewards");

		if (TryPatch(harmony, typeof(HookModifyGeneratedMapPatch), "Map - Hook.ModifyGeneratedMap")) success++; else fail++;
		if (TryPatch(harmony, typeof(HookModifyNextEventAct4RewardPatch), "Map - Hook.ModifyNextEvent")) success++; else fail++;
		if (TryPatch(harmony, typeof(HookModifyUnknownMapPointRoomTypesAct4RewardPatch), "Map - Hook.ModifyUnknownMapPointRoomTypes")) success++; else fail++;
		if (TryPatch(harmony, typeof(RunManagerCreateRoomAct4TreasurePatch), "Map - RunManager.CreateRoom")) success++; else fail++;
		if (TryPatch(harmony, typeof(RewardsSetWithRewardsFromRoomPatch), "Rewards - RewardsSet.WithRewardsFromRoom")) success++; else fail++;
		if (TryPatch(harmony, typeof(HookModifyCardRewardCreationOptionsPatch), "Rewards - Hook.ModifyCardRewardCreationOptions")) success++; else fail++;
		if (TryPatch(harmony, typeof(ImageHelperGetRoomIconPathPatch), "Icons - ImageHelper.GetRoomIconPath")) success++; else fail++;
		if (TryPatch(harmony, typeof(ImageHelperGetRoomIconOutlinePathPatch), "Icons - ImageHelper.GetRoomIconOutlinePath")) success++; else fail++;
		if (TryPatch(harmony, typeof(NBossMapPointReadyPatch), "Icons - NBossMapPoint._Ready")) success++; else fail++;
		if (TryPatch(harmony, typeof(NMapScreenOpenPatch), "Map - NMapScreen.Open")) success++; else fail++;
		if (TryPatch(harmony, typeof(NMapScreenReadyPatch), "Map - NMapScreen._Ready")) success++; else fail++;

		// ────────────────────────────────────────────────────────
		// Group 4: Combat & Scaling
		// ────────────────────────────────────────────────────────
		Log.Info("[Act4Placeholder] Patching Group 4: Combat & Scaling...", 2);
		Act4Logger.Info("Patching Group 4: Combat & Scaling");

		if (TryPatch(harmony, typeof(HookModifyDamagePatch), "Combat - Hook.ModifyDamage")) success++; else fail++;
		if (TryPatch(harmony, typeof(HookAfterDamageGivenPatch), "Combat - Hook.AfterDamageGiven")) success++; else fail++;
		if (TryPatch(harmony, typeof(GuardbotArchitectPatch), "Combat - Guardbot.GuardMove (Architect fix)")) success++; else fail++;
		if (TryPatch(harmony, typeof(HookModifyHandDrawPatch), "Combat - Hook.ModifyHandDraw")) success++; else fail++;
		if (TryPatch(harmony, typeof(MultiplayerScalingModelGetMultiplayerScalingPatch), "Combat - MultiplayerScalingModel.GetMultiplayerScaling")) success++; else fail++;
		if (TryPatch(harmony, typeof(CombatManagerAddCreaturePatch), "Combat - CombatManager.AddCreature")) success++; else fail++;
		if (TryPatch(harmony, typeof(CombatManagerAfterCreatureAddedPatch), "Combat - CombatManager.AfterCreatureAdded")) success++; else fail++;
		if (TryPatch(harmony, typeof(Act4ArchitectBackgroundPatch), "Combat - EncounterModel.CreateBackground")) success++; else fail++;
		if (TryPatch(harmony, typeof(NRestSiteCharacterReadyAct4Patch), "Rest - NRestSiteCharacter._Ready")) success++; else fail++;
		if (TryPatch(harmony, typeof(ArchitectStunLimitPatch), "Combat - Creature.StunInternal (Architect once-per-phase stun cap)")) success++; else fail++;
		if (TryPatch(harmony, typeof(NPlayerHandReturnHolderToHandPatch), "Combat - NPlayerHand.ReturnHolderToHand")) success++; else fail++;

		// ────────────────────────────────────────────────────────
		// Group 5: Save State & History
		// ────────────────────────────────────────────────────────
		Log.Info("[Act4Placeholder] Patching Group 5: Save State & History...", 2);
		Act4Logger.Info("Patching Group 5: Save State & History");

		if (TryPatch(harmony, typeof(RunManagerToSaveAct4Patch), "Save - RunManager.ToSave")) success++; else fail++;
		if (TryPatch(harmony, typeof(RunStateFromSerializableAct4Patch), "Save - RunState.FromSerializable")) success++; else fail++;
		if (TryPatch(harmony, typeof(ProgressSaveManagerLoadProgressSanitizePatch), "Save - ProgressSaveManager.LoadProgress")) success++; else fail++;
		if (TryPatch(harmony, typeof(ProgressSaveManagerAct4EpochSuppressPatch), "Save - ProgressSaveManager.ObtainCharUnlockEpoch")) success++; else fail++;
		if (TryPatch(harmony, typeof(ScoreUtilityCalculateScoreRunStatePatch), "Score - ScoreUtility.CalculateScore(IRunState)")) success++; else fail++;
		if (TryPatch(harmony, typeof(ScoreUtilityCalculateScoreSerializableRunPatch), "Score - ScoreUtility.CalculateScore(SerializableRun)")) success++; else fail++;

		// ────────────────────────────────────────────────────────
		// Group 6: Compendium, Run History & Stats
		// ────────────────────────────────────────────────────────
		Log.Info("[Act4Placeholder] Patching Group 6: Compendium & Stats...", 2);
		Act4Logger.Info("Patching Group 6: Compendium & Stats");

		if (TryPatch(harmony, typeof(CompendiumAct4DetailsPatches).GetNestedType("NRunHistoryReadyPatch", BindingFlags.NonPublic | BindingFlags.Static), "Compendium - NRunHistory._Ready")) success++; else fail++;
		if (TryPatch(harmony, typeof(CompendiumAct4DetailsPatches).GetNestedType("NRunHistoryOnSubmenuOpenedPatch", BindingFlags.NonPublic | BindingFlags.Static), "Compendium - NRunHistory.OnSubmenuOpened")) success++; else fail++;
		if (TryPatch(harmony, typeof(CompendiumAct4DetailsPatches).GetNestedType("NRunHistoryDisplayRunPatch", BindingFlags.NonPublic | BindingFlags.Static), "Compendium - NRunHistory.DisplayRun")) success++; else fail++;
		if (TryPatch(harmony, typeof(CompendiumAct4DetailsPatches).GetNestedType("NMapPointHistoryLoadHistoryPatch", BindingFlags.NonPublic | BindingFlags.Static), "Compendium - NMapPointHistory.LoadHistory")) success++; else fail++;
		if (TryPatch(harmony, typeof(CompendiumAct4DetailsPatches).GetNestedType("NStatsScreenReadyPatch", BindingFlags.NonPublic | BindingFlags.Static), "Compendium - NStatsScreen._Ready")) success++; else fail++;
		if (TryPatch(harmony, typeof(CompendiumAct4DetailsPatches).GetNestedType("NStatsScreenOnSubmenuOpenedPatch", BindingFlags.NonPublic | BindingFlags.Static), "Compendium - NStatsScreen.OnSubmenuOpened")) success++; else fail++;
		if (TryPatch(harmony, typeof(CompendiumAct4DetailsPatches).GetNestedType("NGeneralStatsGridLoadStatsPatch", BindingFlags.NonPublic | BindingFlags.Static), "Compendium - NGeneralStatsGrid.LoadStats")) success++; else fail++;
		if (TryPatch(harmony, typeof(CompendiumAct4DetailsPatches).GetNestedType("NCharacterStatsLoadStatsPatch", BindingFlags.NonPublic | BindingFlags.Static), "Compendium - NCharacterStats.LoadStats")) success++; else fail++;
		if (TryPatch(harmony, typeof(Act4RunHistoryRuntimePatch), "History - NRun.ShowGameOverScreen")) success++; else fail++;

		// ────────────────────────────────────────────────────────
		// Group 7: Game Over & Power Hover (nested in ModEntry)
		// ────────────────────────────────────────────────────────
		Log.Info("[Act4Placeholder] Patching Group 7: Game Over & Power Hover", 2);
		Act4Logger.Info("Patching Group 7: Game Over & Power Hover");

		if (TryPatch(harmony, typeof(NGameOverScreenReadyPatch), "GameOver - NGameOverScreen._Ready")) success++; else fail++;
		if (TryPatch(harmony, typeof(NGameOverScreenInitializeBannerAndQuotePatch), "GameOver - NGameOverScreen.InitializeBannerAndQuote")) success++; else fail++;
		if (TryPatch(harmony, typeof(NPowerOnHoveredPatch), "Power - NPower.OnHovered")) success++; else fail++;
		if (TryPatch(harmony, typeof(NPowerOnUnhoveredPatch), "Power - NPower.OnUnhovered")) success++; else fail++;

		PatchesApplied = success;
		PatchesFailed = fail;

		// Log final summary.
		int patchedMethodCount = harmony.GetPatchedMethods().Count();
		Log.Info($"[Act4Placeholder] ═══════════════════════════════════════════════", 2);
		Log.Info($"[Act4Placeholder]   Patch classes: {success} OK, {fail} FAILED", 2);
		Log.Info($"[Act4Placeholder]   Patched methods total: {patchedMethodCount}", 2);
		Act4Logger.Info($"Patch classes: {success} OK, {fail} FAILED - methods patched: {patchedMethodCount}");

		if (fail > 0)
		{
			Log.Error($"[Act4Placeholder]   FAILURES:");
			Act4Logger.Error("Failures:");
			foreach (string f in PatchFailures)
			{
				Log.Error($"[Act4Placeholder]     - {f}");
				Act4Logger.Error($"  - {f}");
			}
		}

		Log.Info($"[Act4Placeholder] ═══════════════════════════════════════════════", 2);
		Log.Info($"[Act4Placeholder] v{ModVersion} initialization complete.", 2);
		Act4Logger.Info($"v{ModVersion} initialization complete.");
		InitCompleted = true;
	}
}
