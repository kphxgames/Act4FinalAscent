//=============================================================================
// UnifiedSavePathPatches.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Injects a collapsible panel into the main menu for save synchronization (vanilla to modded import/export with backup and recovery) and Act 4 feature toggles (help potions, extra rewards).
// ZH: 向主菜单注入可折叠面板，提供存档同步（原版与Mod版存档的导入/导出及备份恢复）和第四幕功能开关（辅助药水、额外奖励）。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

internal static class UnifiedSavePathPatches
{
	private sealed class SaveSyncOperationRecord
	{
		public long TimestampUtc { get; set; }

		public int ProfileId { get; set; }

		public bool FromVanillaToModded { get; set; }

		public bool DestinationIsVanilla { get; set; }

		public bool IsSourceBackupMirror { get; set; }

		public long Playtime { get; set; }

		public string BackupDirectory { get; set; } = string.Empty;
	}

	private sealed class ProfileOption
	{
		public int ProfileId { get; set; }

		public long Playtime { get; set; }

		public bool IsLikelyBlank { get; set; }

		public string WarningText { get; set; } = string.Empty;

		public string SavesDir { get; set; } = string.Empty;
	}

	private readonly struct ProgressSummary
	{
		public long TotalPlaytime { get; init; }

		public int NumberOfRuns { get; init; }

		public int CharacterStatsCount { get; init; }

		public int EncounterStatsCount { get; init; }

		public int EnemyStatsCount { get; init; }

		public int AncientStatsCount { get; init; }

		public int DiscoveredActsCount { get; init; }

		public int DiscoveredCardsCount { get; init; }

		public int DiscoveredEventsCount { get; init; }

		public int DiscoveredPotionsCount { get; init; }

		public int DiscoveredRelicsCount { get; init; }
	}

	private const string RootName = "Act4Placeholder_SaveSyncRoot";

	private const string ToggleBtnName = "Act4Placeholder_SaveSyncToggle";

	private const string ImportBtnName = "Btn_ImportVanillaToModded";

	private const string StatusLabelName = "Act4Placeholder_SaveSyncStatus";

	private const string RecoveryPopupName = "Act4Placeholder_SaveSyncRecoveryPopup";

	private const string CopyPopupName = "Act4Placeholder_SaveSyncCopyPopup";

	private const string BackupRootRelativePath = "act4placeholder/save_backups";

	private const string LatestSourceBackupRootRelativePath = "act4placeholder/save_backups_latest_source";

	private const int MaxBackupEntries = 7;

	// ── Act 4 tab ──
	private const string TabBarName = "Act4Placeholder_TabBar";
	private const string SaveSyncTabBtnName = "Act4Placeholder_SaveSyncTabBtn";
	private const string Act4TabBtnName = "Act4Placeholder_Act4TabBtn";
	private const string SaveSyncContentName = "Act4Placeholder_SaveSyncContent";
	private const string Act4ContentName = "Act4Placeholder_Act4Content";
	private const string HelpPotionsToggleName = "Act4Placeholder_HelpPotionsToggle";
	private const string ExtraRewardsToggleName = "Act4Placeholder_ExtraRewardsToggle";

	// null = not yet determined; true/false = user-set or auto-initialized
	private static bool? _saveSyncExpanded = null;

	private static bool _act4TabActive = false;

	private static readonly List<SaveSyncOperationRecord> ActiveRecoveryEntries = new List<SaveSyncOperationRecord>();

	private static readonly MegaCrit.Sts2.Core.Logging.Logger Logger = new MegaCrit.Sts2.Core.Logging.Logger("Act4Placeholder.UnifiedSavePath", (LogType)0);

	[HarmonyPatch(typeof(NMainMenu), "_Ready")]
	private static class NMainMenuReadyPatch
	{
		private static void Postfix(NMainMenu __instance)
		{
			try
			{
				ModSupport.EnsureAct4DynamicTextLocalizationReady();
				Act4Logger.Section("Main Menu - SaveSync");
				Logger.Info("[SaveSync] NMainMenu._Ready Postfix fired - UI will appear in 5 s.", 2);
				Act4Logger.Info("[SaveSync] NMainMenu._Ready patch fired - scheduling EnsureUi in 3 s.");

				// Delay creation so the panel doesn't flash up while the main menu is still fading in.
				NMainMenu captured = __instance;
				SceneTreeTimer delayTimer = ((Node)__instance).GetTree().CreateTimer(3.0, true);
				((GodotObject)delayTimer).Connect(SceneTreeTimer.SignalName.Timeout, Callable.From(delegate
				{
					try
					{
						if (!GodotObject.IsInstanceValid(captured)) return;
						EnsureUi(captured);
						UpdateUiState(captured, ModLoc.T("Save sync ready.", "存档同步已就绪。", fra: "Sync de sauvegarde prête.", deu: "Spielstand-Sync bereit.", jpn: "セーブ同期が準備完了。", kor: "저장 동기화 준비 완료.", por: "Sincronização de saves pronta.", rus: "Синхронизация сохранений готова.", spa: "Sincronización de guardados lista."));
						UpdatePanelVisibility(captured);
						Control postRoot = ((Node)captured).GetNodeOrNull<Control>(RootName);
						Logger.Info($"[SaveSync]   Panel created (deferred): {postRoot != null}", 2);
						Act4Logger.Info($"[SaveSync]   Panel created (deferred): {postRoot != null}");
					}
					catch (Exception ex)
					{
						Logger.Error($"[SaveSync] EXCEPTION in deferred EnsureUi: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", 1);
						Act4Logger.Error($"[SaveSync] EXCEPTION in deferred EnsureUi: {ex.GetType().Name}: {ex.Message}");
					}
				}), 0u);
			}
			catch (Exception ex)
			{
				Logger.Error($"[SaveSync] EXCEPTION in NMainMenu._Ready Postfix: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", 1);
				Act4Logger.Error($"[SaveSync] EXCEPTION in _Ready Postfix: {ex.GetType().Name}: {ex.Message}");
			}
		}
	}

	[HarmonyPatch(typeof(NMainMenu), "OnSubmenuStackChanged")]
	private static class NMainMenuOnSubmenuStackChangedPatch
	{
		private static void Postfix(NMainMenu __instance)
		{
			try
			{
				UpdatePanelVisibility(__instance);
			}
			catch (Exception ex)
			{
				Logger.Error($"[SaveSync] EXCEPTION in OnSubmenuStackChanged Postfix: {ex.GetType().Name}: {ex.Message}", 1);
			}
		}
	}

	private static void EnsureUi(NMainMenu mainMenu)
	{
		if (((Node)mainMenu).GetNodeOrNull<Control>(RootName) != null)
		{
			return;
		}
		// EN: This panel is cheap enough to rebuild whenever the menu scene wakes up.
		//     That is much less fragile than assuming one old Godot node will survive every fade,
		//     reload, and third-party patch intact.
		// ZH: 这块面板足够轻，可以在菜单场景醒来时整块重建。
		//     比起假设某个旧 Godot 节点能扛住所有淡入、重载和第三方补丁，这样稳得多。

		// isFirstTime: re-read profile playtime each time the panel is created.
		// This ensures the yellow-star highlight only shows when the profile truly
		// has playtime == 0 (never played), not based on a stale cached value from
		// earlier in the session (e.g., after the player saves & quits back to menu).
		bool isFirstTime = false;
		try
		{
			int profileId = GetCurrentProfileId();
			string moddedSavesDir = GetSavesDir(false, profileId);
			long playtime = ReadPlaytimeFromProfileSave(moddedSavesDir);
			isFirstTime = (playtime == 0);
		}
		catch
		{
			isFirstTime = false; // don't show star on read error
		}

		// Expand state: auto-expand once for first-time users; otherwise keep previous state.
		if (_saveSyncExpanded == null)
			_saveSyncExpanded = isFirstTime;

		bool expanded = _saveSyncExpanded ?? true;

		Control val = new Control
		{
			Name = RootName,
			MouseFilter = Control.MouseFilterEnum.Pass,
			ZIndex = 95
		};
		val.SetAnchorsPreset(Control.LayoutPreset.TopLeft, false);
		val.Position = new Vector2(20f, 88f);
		val.Size = expanded ? new Vector2(344f, 355f) : new Vector2(40f, 40f);

		ColorRect val2 = new ColorRect
		{
			Name = "Background",
			Color = expanded ? new Color(0.06f, 0.08f, 0.1f, 0.86f) : new Color(0.12f, 0.16f, 0.22f, 0.92f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)val2).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Node)val).AddChild(val2, false, Node.InternalMode.Disabled);

		// Toggle button - always visible small square at top-left of root
		NButton toggleBtn = new NButton
		{
			Name = ToggleBtnName,
			FocusMode = Control.FocusModeEnum.All,
			MouseFilter = Control.MouseFilterEnum.Stop,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
			CustomMinimumSize = new Vector2(36f, 36f),
			Size = new Vector2(36f, 36f),
			Position = new Vector2(2f, 2f),
			TooltipText = ModLoc.T("Toggle Save Sync panel", "切换存档同步面板", fra: "Activer/désactiver le panneau de sync", deu: "Spielstand-Sync-Panel ein-/ausblenden", jpn: "セーブ同期パネルの切り替え", kor: "저장 동기화 패널 토글", por: "Alternar painel de sincronização", rus: "Переключить панель синхронизации сохранений", spa: "Alternar panel de sincronización")
		};
		ColorRect toggleBg = new ColorRect
		{
			Name = "Background",
			Color = new Color(0.2f, 0.28f, 0.38f, 0.9f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)toggleBg).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Node)toggleBtn).AddChild(toggleBg, false, Node.InternalMode.Disabled);
		Label toggleLbl = new Label
		{
			Name = "Label",
			Text = expanded ? "\u2715" : "\u2261", // ✕ or ≡
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)toggleLbl).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Control)toggleLbl).AddThemeFontSizeOverride("font_size", 18);
		((Control)toggleLbl).AddThemeColorOverride("font_color", new Color(0.85f, 0.9f, 1f, 1f));
		((Node)toggleBtn).AddChild(toggleLbl, false, Node.InternalMode.Disabled);
		((GodotObject)toggleBtn).Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(OnToggleSaveSyncReleased), 0u);

		VBoxContainer val3 = new VBoxContainer
		{
			Name = "VBox",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Position = new Vector2(10f, 10f),
			Size = new Vector2(324f, 333f)
		};
		((CanvasItem)val3).Visible = expanded;
		((Node)val).AddChild(val3, false, Node.InternalMode.Disabled);
		// Toggle button added last so it is on top in draw order - Godot routes mouse events
		// to the last-added sibling first, preventing the VBox from absorbing toggle clicks.
		((Node)val).AddChild(toggleBtn, false, Node.InternalMode.Disabled);
		Label val4 = new Label
		{
			Name = "Title",
			Text = ModLoc.T("Save Sync", "存档同步",
				fra: "Sync de Sauvegarde", deu: "Spielstand-Sync",
				jpn: "セーブ同期", kor: "저장 동기화",
				por: "Sincronizar Saves", rus: "Синхронизация сохранений", spa: "Sincronizar Guardados"),
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)val4).AddThemeFontSizeOverride("font_size", 16);
		((Control)val4).AddThemeColorOverride("font_color", new Color(0.96f, 0.96f, 0.9f, 1f));
		((Node)val3).AddChild(val4, false, Node.InternalMode.Disabled);
		// ── Tab bar ──
		HBoxContainer tabBar = new HBoxContainer
		{
			Name = TabBarName,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(324f, 36f)
		};
		NButton saveSyncTabBtn = new NButton
		{
			Name = SaveSyncTabBtnName,
			FocusMode = Control.FocusModeEnum.All,
			MouseFilter = Control.MouseFilterEnum.Stop,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
			CustomMinimumSize = new Vector2(0f, 36f)
		};
		((Control)saveSyncTabBtn).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		ColorRect ssBg = new ColorRect { Name = "Background", MouseFilter = Control.MouseFilterEnum.Ignore };
		((Control)ssBg).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		ssBg.Color = !_act4TabActive
			? new Color(0.15f, 0.32f, 0.52f, 0.95f)
			: new Color(0.12f, 0.16f, 0.22f, 0.88f);
		((Node)saveSyncTabBtn).AddChild(ssBg, false, Node.InternalMode.Disabled);
		Label ssTabLbl = new Label
		{
			Name = "Label",
			Text = ModLoc.T("Save Sync", "存档同步", fra: "Sync Sauvegarde", deu: "Spielstand-Sync", jpn: "セーブ同期", kor: "저장 동기화", por: "Sincronizar", rus: "Синхронизация", spa: "Sincronizar"),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)ssTabLbl).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Control)ssTabLbl).AddThemeFontSizeOverride("font_size", 12);
		((Control)ssTabLbl).AddThemeColorOverride("font_color", !_act4TabActive
			? new Color(0.96f, 0.98f, 1f, 1f)
			: new Color(0.7f, 0.75f, 0.8f, 1f));
		((Node)saveSyncTabBtn).AddChild(ssTabLbl, false, Node.InternalMode.Disabled);
		((GodotObject)saveSyncTabBtn).Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(OnSaveSyncTabReleased), 0u);
		((Node)tabBar).AddChild(saveSyncTabBtn, false, Node.InternalMode.Disabled);

		NButton act4TabBtn = new NButton
		{
			Name = Act4TabBtnName,
			FocusMode = Control.FocusModeEnum.All,
			MouseFilter = Control.MouseFilterEnum.Stop,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
			CustomMinimumSize = new Vector2(0f, 36f)
		};
		((Control)act4TabBtn).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		ColorRect a4Bg = new ColorRect { Name = "Background", MouseFilter = Control.MouseFilterEnum.Ignore };
		((Control)a4Bg).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		a4Bg.Color = _act4TabActive
			? new Color(0.15f, 0.32f, 0.52f, 0.95f)
			: new Color(0.12f, 0.16f, 0.22f, 0.88f);
		((Node)act4TabBtn).AddChild(a4Bg, false, Node.InternalMode.Disabled);
		Label a4TabLbl = new Label
		{
			Name = "Label",
			Text = ModLoc.T("Act 4", "第四幕", fra: "Acte 4", deu: "Akt 4", jpn: "第4幕", kor: "4막", por: "Ato 4", rus: "Акт 4", spa: "Acto 4"),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)a4TabLbl).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Control)a4TabLbl).AddThemeFontSizeOverride("font_size", 12);
		((Control)a4TabLbl).AddThemeColorOverride("font_color", _act4TabActive
			? new Color(0.96f, 0.98f, 1f, 1f)
			: new Color(0.7f, 0.75f, 0.8f, 1f));
		((Node)act4TabBtn).AddChild(a4TabLbl, false, Node.InternalMode.Disabled);
		((GodotObject)act4TabBtn).Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(OnAct4TabReleased), 0u);
		((Node)tabBar).AddChild(act4TabBtn, false, Node.InternalMode.Disabled);
		((Node)val3).AddChild(tabBar, false, Node.InternalMode.Disabled);

		// ── Save Sync content (Tab 1) ──
		VBoxContainer saveSyncContent = new VBoxContainer
		{
			Name = SaveSyncContentName,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((CanvasItem)saveSyncContent).Visible = !_act4TabActive;

		Label val5 = new Label
		{
			Name = StatusLabelName,
			Text = string.Empty,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)val5).AddThemeFontSizeOverride("font_size", 12);
		((Control)val5).AddThemeColorOverride("font_color", new Color(0.8f, 0.94f, 1f, 1f));
		((Node)saveSyncContent).AddChild(val5, false, Node.InternalMode.Disabled);

		// Import button - highlighted for first-time users to make it obvious
		NButton importBtn = CreateActionButton(ModLoc.T("Import Vanilla -> Modded", "导入 原版 → 模组", fra: "Importer Vanilla → Moddé", deu: "Importieren Vanilla → Modded", jpn: "インポート バニラ → Mod版", kor: "가져오기 바닐라 → 모드", por: "Importar Vanilla → Modded", rus: "Импорт Ванилла → Моды", spa: "Importar Vanilla → Mod"), OnCopyVanillaToModdedReleased);
		importBtn.Name = ImportBtnName;
		if (isFirstTime)
		{
			ColorRect importBg = importBtn.GetNodeOrNull<ColorRect>("Background");
			if (importBg != null) importBg.Color = new Color(0.7f, 0.5f, 0.1f, 0.95f);
			Label importLbl = importBtn.GetNodeOrNull<Label>("Label");
			if (importLbl != null)
			{
				importLbl.Text = "\u2605 " + ModLoc.T("Import Vanilla -> Modded", "导入 原版 → 模组", fra: "Importer Vanilla → Moddé", deu: "Importieren Vanilla → Modded", jpn: "インポート バニラ → Mod版", kor: "가져오기 바닐라 → 모드", por: "Importar Vanilla → Modded", rus: "Импорт Ванилла → Моды", spa: "Importar Vanilla → Mod"); // ★ prefix
				((Control)importLbl).AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.6f, 1f));
			}
		}
		((Node)saveSyncContent).AddChild(importBtn, false, Node.InternalMode.Disabled);

		((Node)saveSyncContent).AddChild(CreateActionButton(ModLoc.T("Export Modded -> Vanilla", "导出 模组 → 原版", fra: "Exporter Moddé → Vanilla", deu: "Exportieren Modded → Vanilla", jpn: "エクスポート Mod版 → バニラ", kor: "내보내기 모드 → 바닐라", por: "Exportar Modded → Vanilla", rus: "Экспорт Моды → Ванилла", spa: "Exportar Mod → Vanilla"), OnCopyModdedToVanillaReleased), false, Node.InternalMode.Disabled);
		((Node)saveSyncContent).AddChild(CreateActionButton(ModLoc.T("I messed up. Recover a lost save file.", "出错了。恢复丢失的存档文件。", fra: "J'ai raté. Récupérer un fichier de sauvegarde perdu.", deu: "Ich hab's verbockt. Verlorene Speicherdatei wiederherstellen.", jpn: "やらかした。失った保存ファイルを復元する。", kor: "망쳐버렸다. 잃어버린 세이브 파일 복구하기.", por: "Me ferrei. Recuperar um arquivo de save perdido.", rus: "Я облажался. Восстановить потерянный файл сохранения.", spa: "La cagué. Recuperar un archivo de guardado perdido."), OnOpenRecoveryPickerReleased), false, Node.InternalMode.Disabled);
		((Node)saveSyncContent).AddChild(CreateActionButton(ModLoc.T("Relaunch FULL Vanilla (No Mods)", "完全重启原版 (无Mod)", fra: "Relancer Vanilla COMPLET (Sans Mods)", deu: "Vollständig Vanilla neu starten (Keine Mods)", jpn: "完全バニラで再起動（Modなし）", kor: "완전 바닐라로 재시작(모드 없음)", por: "Reiniciar Vanilla COMPLETO (Sem Mods)", rus: "Перезапустить полную Ваниллу (Без Модов)", spa: "Reiniciar Vanilla COMPLETO (Sin Mods)"), OnRelaunchVanillaReleased), false, Node.InternalMode.Disabled);
		((Node)val3).AddChild(saveSyncContent, false, Node.InternalMode.Disabled);

		// ── Act 4 settings content (Tab 2) ──
		ModSupport.EnsureAct4PrefsLoaded();
		VBoxContainer act4Content = new VBoxContainer
		{
			Name = Act4ContentName,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((CanvasItem)act4Content).Visible = _act4TabActive;

		Label coopNoteLabel = new Label
		{
			Text = ModLoc.T("In co-op, only the host's settings apply.",
				"联机时，仅主机的设置生效。",
				fra: "En coop, seuls les paramètres de l'hôte s'appliquent.",
				deu: "Im Koop gilt nur die Einstellung des Hosts.",
				jpn: "Co-opでは、ホストの設定のみが適用されます。",
				kor: "협동 플레이에서는 호스트의 설정만 적용됩니다.",
				por: "No co-op, apenas as configurações do host se aplicam.",
				rus: "В кооперативе применяются только настройки хоста.",
				spa: "En cooperativo, solo se aplican los ajustes del anfitrión."),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)coopNoteLabel).AddThemeFontSizeOverride("font_size", 11);
		((Control)coopNoteLabel).AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.8f, 1f));
		((Node)act4Content).AddChild(coopNoteLabel, false, Node.InternalMode.Disabled);

		((Node)act4Content).AddChild(CreateToggleRow(
			HelpPotionsToggleName,
			ModLoc.T("Receive Help Potions in Act 1 to assist journey to Act 4",
				"在第1幕获得帮助药水，以助力前往第4幕的旅程",
				fra: "Recevoir des Potions d'Aide en Acte 1 pour faciliter le voyage vers l'Acte 4",
				deu: "Hilfs-Tränke in Akt 1 erhalten, um die Reise zu Akt 4 zu erleichtern",
				jpn: "第1幕で助けポーションを受け取り、第4幕への旅を助ける",
				kor: "1막에서 도움 물약을 받아 4막으로 가는 여정을 지원",
				por: "Receber Poções de Ajuda no Ato 1 para auxiliar a jornada até o Ato 4",
				rus: "Получить зелья помощи в Акте 1, чтобы помочь в путешествии к Акту 4",
				spa: "Recibir Pociones de Ayuda en el Acto 1 para facilitar el viaje al Acto 4"),
			Act4Settings.HelpPotionsEnabled,
			OnHelpPotionsToggleReleased
		), false, Node.InternalMode.Disabled);

		((Node)act4Content).AddChild(CreateToggleRow(
			ExtraRewardsToggleName,
			ModLoc.T("Receive extra reward drafts from Act 4 Empyreal Cache and Royal Treasury (+1 extra draft at each node)",
				"从第4幕天界宝库和皇家宝藏处获得额外奖励选择（每个节点多1次额外选择）",
				fra: "Recevoir des choix de récompenses supplémentaires des événements d'Acte 4 (+1 choix supplémentaire par nœud)",
				deu: "Erhalte zusätzliche Belohnungswahlen von Akt-4-Events (+1 zusätzliche Wahl pro Knoten)",
				jpn: "第4幕イベントで追加報酬ドラフトを受け取る（各ノードで追加選択+1）",
				kor: "4막 이벤트에서 추가 보상 선택 받기(각 노드마다 추가 선택 +1)",
				por: "Receber seleções extras de recompensa dos eventos do Ato 4 (+1 seleção extra em cada nó)",
				rus: "Получать дополнительные выборы наград от событий Акта 4 (+1 дополнительный выбор в каждом узле)",
				spa: "Recibir selecciones extra de recompensas de los eventos del Acto 4 (+1 selección extra en cada nodo)"),
			Act4Settings.ExtraRewardsEnabled,
			OnExtraRewardsToggleReleased
		), false, Node.InternalMode.Disabled);

		((Node)val3).AddChild(act4Content, false, Node.InternalMode.Disabled);

		// Mod status indicator at bottom of panel
		string statusText = ModEntry.InitCompleted
			? $"Act4 Mod v{ModEntry.ModVersion} - {ModEntry.PatchesApplied} {ModLoc.T("patches OK", "个补丁已完成", fra: "correctifs OK", deu: "Patches OK", jpn: "パッチ完了", kor: "패치 적용됨", por: "patches OK", rus: "патчей применено", spa: "parches OK")}"
			  + (ModEntry.PatchesFailed > 0 ? $", {ModEntry.PatchesFailed} {ModLoc.T("FAILED", "失败", fra: "ÉCHOUÉ", deu: "FEHLGESCHLAGEN", jpn: "失敗", kor: "실패", por: "FALHOU", rus: "ОШИБКА", spa: "FALLIDO")}" : "")
			: $"Act4 Mod v{ModEntry.ModVersion} - {ModLoc.T("initializing...", "初始化中...", fra: "initialisation...", deu: "Initialisierung...", jpn: "初期化中...", kor: "초기화 중...", por: "inicializando...", rus: "инициализация...", spa: "inicializando...")}";
		Color statusColor = (ModEntry.PatchesFailed > 0)
			? new Color(1f, 0.4f, 0.4f, 1f)
			: new Color(0.5f, 0.8f, 0.5f, 0.85f);
		Label modStatusLabel = new Label
		{
			Name = "Act4Placeholder_ModStatusLabel",
			Text = statusText,
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)modStatusLabel).AddThemeFontSizeOverride("font_size", 10);
		((Control)modStatusLabel).AddThemeColorOverride("font_color", statusColor);
		((Node)val3).AddChild(modStatusLabel, false, Node.InternalMode.Disabled);

		((Node)mainMenu).AddChild(val, false, Node.InternalMode.Disabled);
		Control nodeOrNull = ((Node)mainMenu).GetNodeOrNull<Control>("MainMenuTextButtons");
		if (nodeOrNull != null)
		{
			((GodotObject)nodeOrNull).Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(delegate
			{
				UpdatePanelVisibility(mainMenu);
			}));
		}
		// Defensive: also re-evaluate when the main menu itself becomes visible (e.g. after fade-in).
		// Without this, if mainMenu.Visible == false during _Ready(), the panel stays hidden forever
		// when MainMenuTextButtons is absent (fallback path has no signal connected).
		((GodotObject)mainMenu).Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(delegate
		{
			UpdatePanelVisibility(mainMenu);
		}));
	}

	private static void SetSaveSyncExpanded(bool expanded, Control root)
	{
		_saveSyncExpanded = expanded;
		root.Size = expanded ? new Vector2(344f, 355f) : new Vector2(40f, 40f);
		VBoxContainer vbox = root.GetNodeOrNull<VBoxContainer>("VBox");
		if (vbox != null) ((CanvasItem)vbox).Visible = expanded;
		ColorRect bg = root.GetNodeOrNull<ColorRect>("Background");
		if (bg != null) bg.Color = expanded
			? new Color(0.06f, 0.08f, 0.1f, 0.86f)
			: new Color(0.12f, 0.16f, 0.22f, 0.92f);
		NButton toggle = root.GetNodeOrNull<NButton>(ToggleBtnName);
		if (toggle != null)
		{
			Label lbl = toggle.GetNodeOrNull<Label>("Label");
			if (lbl != null) lbl.Text = expanded ? "\u2715" : "\u2261"; // ✕ or ≡
		}
	}

	private static void OnToggleSaveSyncReleased(NButton btn)
	{
		// Walk directly up to the root - more reliable than going through NGame.Instance.
		// The toggle button is a direct child of the root Control node.
		Control root = ((Node)btn).GetParent() as Control;
		if (root == null) return;
		SetSaveSyncExpanded(!(_saveSyncExpanded ?? false), root);
	}

	private static void OnSaveSyncTabReleased(NButton _)
	{
		SwitchToTab(NGame.Instance?.MainMenu, false);
	}

	private static void OnAct4TabReleased(NButton _)
	{
		SwitchToTab(NGame.Instance?.MainMenu, true);
	}

	private static void SwitchToTab(NMainMenu mainMenu, bool toAct4Tab)
	{
		_act4TabActive = toAct4Tab;
		if (mainMenu == null || !GodotObject.IsInstanceValid(mainMenu)) return;
		Control root = ((Node)mainMenu).GetNodeOrNull<Control>(RootName);
		if (root == null) return;

		Control ssContent = root.GetNodeOrNull<Control>("VBox/" + SaveSyncContentName);
		Control a4Content = root.GetNodeOrNull<Control>("VBox/" + Act4ContentName);
		if (ssContent != null) ((CanvasItem)ssContent).Visible = !toAct4Tab;
		if (a4Content != null) ((CanvasItem)a4Content).Visible = toAct4Tab;

		NButton ssBtn = root.GetNodeOrNull<NButton>("VBox/" + TabBarName + "/" + SaveSyncTabBtnName);
		NButton a4Btn = root.GetNodeOrNull<NButton>("VBox/" + TabBarName + "/" + Act4TabBtnName);
		UpdateTabBtnVisuals(ssBtn, !toAct4Tab);
		UpdateTabBtnVisuals(a4Btn, toAct4Tab);
	}

	private static void UpdateTabBtnVisuals(NButton btn, bool isActive)
	{
		if (btn == null) return;
		ColorRect bg = btn.GetNodeOrNull<ColorRect>("Background");
		if (bg != null) bg.Color = isActive
			? new Color(0.15f, 0.32f, 0.52f, 0.95f)
			: new Color(0.12f, 0.16f, 0.22f, 0.88f);
		Label lbl = btn.GetNodeOrNull<Label>("Label");
		if (lbl != null) ((Control)lbl).AddThemeColorOverride("font_color", isActive
			? new Color(0.96f, 0.98f, 1f, 1f)
			: new Color(0.7f, 0.75f, 0.8f, 1f));
	}

	private static HBoxContainer CreateToggleRow(string toggleName, string labelText, bool initialValue, Action<NButton> onToggle)
	{
		HBoxContainer row = new HBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(324f, 52f)
		};

		NButton toggleBtn = new NButton
		{
			Name = toggleName,
			FocusMode = Control.FocusModeEnum.All,
			MouseFilter = Control.MouseFilterEnum.Stop,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
			CustomMinimumSize = new Vector2(64f, 48f),
			Size = new Vector2(64f, 48f)
		};
		ColorRect toggleBg = new ColorRect { Name = "Background", MouseFilter = Control.MouseFilterEnum.Ignore };
		((Control)toggleBg).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		toggleBg.Color = initialValue
			? new Color(0.1f, 0.42f, 0.18f, 0.92f)
			: new Color(0.32f, 0.14f, 0.14f, 0.90f);
		((Node)toggleBtn).AddChild(toggleBg, false, Node.InternalMode.Disabled);
		Label toggleLbl = new Label
		{
			Name = "Label",
			Text = initialValue
				? ModLoc.T("ON", "开", fra: "ON", deu: "AN", jpn: "ON", kor: "ON", por: "ON", rus: "ВКЛ", spa: "ON")
				: ModLoc.T("OFF", "关", fra: "OFF", deu: "AUS", jpn: "OFF", kor: "OFF", por: "OFF", rus: "ВЫКЛ", spa: "OFF"),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)toggleLbl).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Control)toggleLbl).AddThemeFontSizeOverride("font_size", 13);
		((Control)toggleLbl).AddThemeColorOverride("font_color", new Color(0.96f, 0.98f, 1f, 1f));
		((Node)toggleBtn).AddChild(toggleLbl, false, Node.InternalMode.Disabled);
		((GodotObject)toggleBtn).Connect(NClickableControl.SignalName.Released, Callable.From(onToggle), 0u);
		((Node)row).AddChild(toggleBtn, false, Node.InternalMode.Disabled);

		Label descLabel = new Label
		{
			Text = labelText,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)descLabel).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		((Control)descLabel).AddThemeFontSizeOverride("font_size", 11);
		((Control)descLabel).AddThemeColorOverride("font_color", new Color(0.92f, 0.95f, 1f, 1f));
		((Node)row).AddChild(descLabel, false, Node.InternalMode.Disabled);

		return row;
	}

	private static void UpdateToggleVisuals(NButton btn, bool enabled)
	{
		if (btn == null) return;
		ColorRect bg = btn.GetNodeOrNull<ColorRect>("Background");
		if (bg != null) bg.Color = enabled
			? new Color(0.1f, 0.42f, 0.18f, 0.92f)
			: new Color(0.32f, 0.14f, 0.14f, 0.90f);
		Label lbl = btn.GetNodeOrNull<Label>("Label");
		if (lbl != null) lbl.Text = enabled
			? ModLoc.T("ON", "开", fra: "ON", deu: "AN", jpn: "ON", kor: "ON", por: "ON", rus: "ВКЛ", spa: "ON")
			: ModLoc.T("OFF", "关", fra: "OFF", deu: "AUS", jpn: "OFF", kor: "OFF", por: "OFF", rus: "ВЫКЛ", spa: "OFF");
	}

	private static void OnHelpPotionsToggleReleased(NButton btn)
	{
		Act4Settings.HelpPotionsEnabled = !Act4Settings.HelpPotionsEnabled;
		UpdateToggleVisuals(btn, Act4Settings.HelpPotionsEnabled);
		ModSupport.SaveAct4Prefs();
	}

	private static void OnExtraRewardsToggleReleased(NButton btn)
	{
		Act4Settings.ExtraRewardsEnabled = !Act4Settings.ExtraRewardsEnabled;
		UpdateToggleVisuals(btn, Act4Settings.ExtraRewardsEnabled);
		ModSupport.SaveAct4Prefs();
	}

	private static NButton CreateActionButton(string text, Action<NButton> onReleased)
	{
		NButton val = new NButton
		{
			Name = "Btn_" + text.Replace(" ", "_").Replace("-", "_").Replace(">", "to").Replace("(", "_").Replace(")", "_").Replace(",", "_").Replace(".", "_"),
			FocusMode = Control.FocusModeEnum.All,
			MouseFilter = Control.MouseFilterEnum.Stop,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
			CustomMinimumSize = new Vector2(324f, 48f),
			Size = new Vector2(324f, 48f),
			TooltipText = text
		};
		ColorRect val2 = new ColorRect
		{
			Name = "Background",
			Color = new Color(0.18f, 0.24f, 0.31f, 0.92f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)val2).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Node)val).AddChild(val2, false, Node.InternalMode.Disabled);
		Label val3 = new Label
		{
			Name = "Label",
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)val3).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Control)val3).AddThemeFontSizeOverride("font_size", 12);
		((Control)val3).AddThemeColorOverride("font_color", new Color(0.94f, 0.97f, 1f, 1f));
		((Node)val).AddChild(val3, false, Node.InternalMode.Disabled);
		((GodotObject)val).Connect(NClickableControl.SignalName.Released, Callable.From(onReleased), 0u);
		return val;
	}

	private static void OnCopyVanillaToModdedReleased(NButton _)
	{
		OpenCopyPicker(fromVanillaToModded: true);
	}

	private static void OnCopyModdedToVanillaReleased(NButton _)
	{
		OpenCopyPicker(fromVanillaToModded: false);
	}

	private static void OnOpenRecoveryPickerReleased(NButton _)
	{
		OpenRecoveryPicker();
	}

	private static void OnRelaunchVanillaReleased(NButton _)
	{
		TaskHelper.RunSafely(RelaunchVanillaAsync());
	}

	private static void OpenCopyPicker(bool fromVanillaToModded)
	{
		// EN: This stays profile-to-profile on purpose.
		//     Letting players browse raw save folders would be more powerful, and also much better
		//     at helping them overwrite the wrong thing while sleepy.
		// ZH: 这里故意做成“档案到档案”的选择。
		//     让玩家直接点原始存档目录确实更强，也更容易在犯困时覆盖错目标。
		NMainMenu mainMenu = NGame.Instance?.MainMenu;
		if (mainMenu == null || !GodotObject.IsInstanceValid(mainMenu))
		{
			return;
		}
		CloseRecoveryPopup();
		CloseCopyPopup();
		bool sourceIsVanilla = fromVanillaToModded;
		bool destinationIsVanilla = !fromVanillaToModded;
		List<ProfileOption> profileOptions = BuildProfileOptions(sourceIsVanilla);
		List<ProfileOption> profileOptions2 = BuildProfileOptions(destinationIsVanilla);
		PopupPanel val = new PopupPanel
		{
			Name = CopyPopupName,
			Position = new Vector2I(390, 136),
			Size = new Vector2I(620, 268)
		};
		VBoxContainer val2 = new VBoxContainer
		{
			Name = "CopyVBox",
			Position = new Vector2(14f, 12f),
			Size = new Vector2(592f, 240f)
		};
		((Node)val).AddChild(val2, false, Node.InternalMode.Disabled);
		Label val3 = new Label
		{
			Text = fromVanillaToModded
				? ModLoc.T("Import: choose Vanilla source and Modded destination profile", "导入：选择原版存档来源档案和模组目标档案")
				: ModLoc.T("Export: choose Modded source and Vanilla destination profile", "导出：选择模组存档来源档案和原版目标档案"),
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		((Control)val3).AddThemeFontSizeOverride("font_size", 14);
		((Node)val2).AddChild(val3, false, Node.InternalMode.Disabled);
		OptionButton val4 = new OptionButton
		{
			Name = "SourceSelect",
			CustomMinimumSize = new Vector2(592f, 46f),
			Size = new Vector2(592f, 46f)
		};
		foreach (ProfileOption item in profileOptions)
		{
			val4.AddItem(BuildProfileOptionLabel(item, sourceIsVanilla), item.ProfileId);
		}
		ConfigureCompactDropdown(val4);
		((Node)val2).AddChild(val4, false, Node.InternalMode.Disabled);
		OptionButton val5 = new OptionButton
		{
			Name = "DestinationSelect",
			CustomMinimumSize = new Vector2(592f, 46f),
			Size = new Vector2(592f, 46f)
		};
		foreach (ProfileOption item2 in profileOptions2)
		{
			val5.AddItem(BuildProfileOptionLabel(item2, destinationIsVanilla), item2.ProfileId);
		}
		ConfigureCompactDropdown(val5);
		((Node)val2).AddChild(val5, false, Node.InternalMode.Disabled);
		HBoxContainer val6 = new HBoxContainer
		{
			CustomMinimumSize = new Vector2(592f, 48f),
			Size = new Vector2(592f, 48f)
		};
		((Node)val2).AddChild(val6, false, Node.InternalMode.Disabled);
		NButton val7 = CreateCompactButton(ModLoc.T("Confirm", "确认", fra: "Confirmer", deu: "Bestätigen", jpn: "確認", kor: "확인", por: "Confirmar", rus: "Подтвердить", spa: "Confirmar"), delegate
		{
			TaskHelper.RunSafely(ConfirmCopyPickerSelectionAsync(fromVanillaToModded));
		});
		NButton val8 = CreateCompactButton(ModLoc.T("Cancel", "取消", fra: "Annuler", deu: "Abbrechen", jpn: "キャンセル", kor: "취소", por: "Cancelar", rus: "Отмена", spa: "Cancelar"), delegate
		{
			CloseCopyPopup();
		});
		((Control)val7).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		((Control)val8).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		((Node)val6).AddChild(val7, false, Node.InternalMode.Disabled);
		((Node)val6).AddChild(val8, false, Node.InternalMode.Disabled);
		((Node)mainMenu).AddChild(val, false, Node.InternalMode.Disabled);
		val.Popup();
	}

	private static async Task ConfirmCopyPickerSelectionAsync(bool fromVanillaToModded)
	{
		NMainMenu mainMenu = NGame.Instance?.MainMenu;
		if (mainMenu == null || !GodotObject.IsInstanceValid(mainMenu))
		{
			return;
		}
		PopupPanel nodeOrNull = ((Node)mainMenu).GetNodeOrNull<PopupPanel>(CopyPopupName);
		OptionButton nodeOrNull2 = nodeOrNull?.GetNodeOrNull<OptionButton>("CopyVBox/SourceSelect");
		OptionButton nodeOrNull3 = nodeOrNull?.GetNodeOrNull<OptionButton>("CopyVBox/DestinationSelect");
		if (nodeOrNull2 == null || nodeOrNull3 == null || nodeOrNull2.Selected < 0 || nodeOrNull3.Selected < 0)
		{
			UpdateUiState(mainMenu, "Select source and destination first.");
			return;
		}
		int itemId = nodeOrNull2.GetItemId(nodeOrNull2.Selected);
		int itemId2 = nodeOrNull3.GetItemId(nodeOrNull3.Selected);
		Logger.Info($"SaveSync picker confirm: mode={(fromVanillaToModded ? "import" : "export")}, sourceProfile={itemId}, destinationProfile={itemId2}", 1);
		CloseCopyPopup();
		await CopySaveDataAsync(fromVanillaToModded, Math.Clamp(itemId, 1, 3), Math.Clamp(itemId2, 1, 3));
	}

	private static async Task CopySaveDataAsync(bool fromVanillaToModded, int sourceProfileId, int destinationProfileId)
	{
		// EN: All scary prompts stay here on the UI thread.
		//     The real file work runs in Task.Run so larger history copies do not make the menu
		//     feel frozen while we back up and mirror data.
		// ZH: 危险提示和确认都留在这里走主线程。
		//     真正的文件工作丢进 Task.Run，避免 history 稍大一点就把菜单卡得像死机。
		NMainMenu mainMenu = NGame.Instance?.MainMenu;
		if (!await ConfirmCopyAsync(fromVanillaToModded, sourceProfileId, destinationProfileId))
		{
			UpdateUiState(mainMenu, "Copy canceled.");
			return;
		}
		string sourceSavesDir = GetSavesDir(isVanilla: fromVanillaToModded, sourceProfileId);
		string destinationSavesDir = GetSavesDir(isVanilla: !fromVanillaToModded, destinationProfileId);
		Logger.Info($"SaveSync begin: mode={(fromVanillaToModded ? "import" : "export")}, sourceProfile={sourceProfileId}, destinationProfile={destinationProfileId}, source='{sourceSavesDir}', destination='{destinationSavesDir}'", 1);
		string warningText;
		if (TryGetBlankOrSuspiciousWarning(sourceSavesDir, out warningText) && !await ConfirmSuspiciousSourceAsync(warningText))
		{
			Logger.Info($"SaveSync canceled after suspicious-source warning: sourceProfile={sourceProfileId}, source='{sourceSavesDir}', warning='{warningText}'", 1);
			UpdateUiState(mainMenu, "Copy canceled.");
			return;
		}
		UpdateUiState(mainMenu, $"Copying profile {sourceProfileId} -> {destinationProfileId}...");
		string result = await Task.Run(delegate
		{
			return CopySaveDataInternal(fromVanillaToModded, sourceProfileId, destinationProfileId);
		});
		Logger.Info($"SaveSync end: mode={(fromVanillaToModded ? "import" : "export")}, sourceProfile={sourceProfileId}, destinationProfile={destinationProfileId}, result='{result}'", 1);
		UpdateUiState(NGame.Instance?.MainMenu, result);
		if (!fromVanillaToModded || destinationProfileId != GetCurrentProfileId())
		{
			return;
		}
		await Task.Yield();
		ReloadCurrentProfile();
		UpdateUiState(NGame.Instance?.MainMenu, result + "\nModded profile reloaded.");
	}

	private static async Task RelaunchVanillaAsync()
	{
		NMainMenu mainMenu = NGame.Instance?.MainMenu;
		LocString body = new LocString("events", "ACT4_DYNAMIC_TEXT");
		LocString header = new LocString("events", "ACT4_DYNAMIC_TEXT");
		body.Add("text", "Relaunch in FULL vanilla mode now? This will close the current modded game.");
		header.Add("text", "Confirm Relaunch");
		if (!await ShowGenericConfirmationAsync(body, header))
		{
			UpdateUiState(mainMenu, "Vanilla relaunch canceled.");
			return;
		}
		try
		{
			string executablePath = OS.GetExecutablePath();
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = "cmd.exe",
				Arguments = $"/c timeout /t 2 /nobreak >nul & start \"\" \"{executablePath}\" --nomods",
				UseShellExecute = true,
				WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty
			};
			Process.Start(startInfo);
			UpdateUiState(mainMenu, ModLoc.T("Launching vanilla in 2s... closing modded game.", "2秒后启动原版...关闭模组游戏。", fra: "Lancement Vanilla dans 2s... fermeture du jeu modifié.", deu: "In 2s wird Vanilla gestartet... schließt das Modded-Spiel.", jpn: "2秒後にバニラを起動します...Mod版を終了します。", kor: "2초 후 바닐라 실행... 모드 게임 종료.", por: "Iniciando Vanilla em 2s... fechando jogo modded.", rus: "Запуск Ваниллы через 2с... закрытие модифицированной игры.", spa: "Iniciando Vanilla en 2s... cerrando juego modded."));
			NGame.Instance?.Quit();
		}
		catch (Exception ex)
		{
			Logger.Warn($"Vanilla relaunch failed: {ex.Message}", 1);
			UpdateUiState(mainMenu, ModLoc.T("Failed to launch vanilla. Check logs.", "启动原版失败。请检查日志。", fra: "Échec du lancement Vanilla. Consultez les journaux.", deu: "Vanilla konnte nicht gestartet werden. Protokolle prüfen.", jpn: "バニラの起動に失敗しました。ログをご確認ください。", kor: "바닐라 실행에 실패했습니다. 로그를 확인하세요.", por: "Falha ao iniciar Vanilla. Verifique os logs.", rus: "Не удалось запустить Ваниллу. Проверьте журналы.", spa: "Error al iniciar Vanilla. Revisa los registros."));
		}
	}

	private static void OpenRecoveryPicker()
	{
		// EN: Recovery only surfaces backups that our own sync flow recorded, newest first.
		//     We are not trying to be a generic save-file browser here, because that gets messy fast
		//     and makes "recover the wrong thing" much easier.
		// ZH: 恢复列表只展示我们自己的同步流程记录下来的备份，并按最新优先。
		//     这里不想做成通用存档浏览器，不然很快就会乱，还更容易恢复错东西。
		NMainMenu mainMenu = NGame.Instance?.MainMenu;
		if (mainMenu == null || !GodotObject.IsInstanceValid(mainMenu))
		{
			return;
		}
		CloseCopyPopup();
		PopupPanel nodeOrNull = ((Node)mainMenu).GetNodeOrNull<PopupPanel>(RecoveryPopupName);
		if (nodeOrNull != null)
		{
			((Node)nodeOrNull).QueueFree();
			return;
		}
		ActiveRecoveryEntries.Clear();
		foreach (SaveSyncOperationRecord entry in DiscoverBackupOperations().OrderByDescending((SaveSyncOperationRecord e) => e.TimestampUtc))
		{
			ActiveRecoveryEntries.Add(entry);
		}
		if (ActiveRecoveryEntries.Count == 0)
		{
			UpdateUiState(mainMenu, ModLoc.T("No backups found.", "没有找到可恢复的备份。", fra: "Aucune sauvegarde trouvée.", deu: "Keine Sicherungen gefunden.", jpn: "バックアップが見つかりません。", kor: "백업을 찾을 수 없습니다.", por: "Nenhum backup encontrado.", rus: "Резервные копии не найдены.", spa: "No se encontraron copias de seguridad."));
			return;
		}
		PopupPanel val = new PopupPanel
		{
			Name = RecoveryPopupName,
			Position = new Vector2I(390, 136),
			Size = new Vector2I(620, 230)
		};
		VBoxContainer val2 = new VBoxContainer
		{
			Name = "RecoveryVBox",
			Position = new Vector2(14f, 12f),
			Size = new Vector2(592f, 200f)
		};
		((Node)val).AddChild(val2, false, Node.InternalMode.Disabled);
		Label val3 = new Label
		{
			Text = ModLoc.T("Select a backup to recover:", "选择要恢复的备份：", fra: "Sélectionner une sauvegarde à restaurer :", deu: "Sicherung zur Wiederherstellung auswählen:", jpn: "復元するバックアップを選択：", kor: "복구할 백업을 선택하세요:", por: "Selecione um backup para restaurar:", rus: "Выберите резервную копию для восстановления:", spa: "Selecciona una copia de seguridad para restaurar:"),
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		((Control)val3).AddThemeFontSizeOverride("font_size", 15);
		((Node)val2).AddChild(val3, false, Node.InternalMode.Disabled);
		OptionButton val4 = new OptionButton
		{
			Name = "RecoverySelect",
			CustomMinimumSize = new Vector2(592f, 46f),
			Size = new Vector2(592f, 46f)
		};
		for (int i = 0; i < ActiveRecoveryEntries.Count; i++)
		{
			val4.AddItem(FormatHistoryEntryLabel(ActiveRecoveryEntries[i]), i);
		}
		ConfigureCompactDropdown(val4);
		((Node)val2).AddChild(val4, false, Node.InternalMode.Disabled);
		HBoxContainer val5 = new HBoxContainer
		{
			CustomMinimumSize = new Vector2(592f, 48f),
			Size = new Vector2(592f, 48f)
		};
		((Node)val2).AddChild(val5, false, Node.InternalMode.Disabled);
		NButton val6 = CreateCompactButton(ModLoc.T("Recover Selected", "恢复所选备份", fra: "Restaurer la sélection", deu: "Ausgewählte wiederherstellen", jpn: "選択した項目を復元", kor: "선택 항목 복구", por: "Recuperar selecionado", rus: "Восстановить выбранное", spa: "Recuperar seleccionado"), OnRecoverSelectedReleased);
		NButton val7 = CreateCompactButton(ModLoc.T("Cancel", "取消", fra: "Annuler", deu: "Abbrechen", jpn: "キャンセル", kor: "취소", por: "Cancelar", rus: "Отмена", spa: "Cancelar"), OnCancelRecoveryPickerReleased);
		((Control)val6).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		((Control)val7).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		((Node)val5).AddChild(val6, false, Node.InternalMode.Disabled);
		((Node)val5).AddChild(val7, false, Node.InternalMode.Disabled);
		((Node)mainMenu).AddChild(val, false, Node.InternalMode.Disabled);
		val.Popup();
	}

	private static NButton CreateCompactButton(string text, Action<NButton> onReleased)
	{
		NButton val = new NButton
		{
			Name = "Btn_" + text.Replace(" ", "_"),
			CustomMinimumSize = new Vector2(286f, 44f),
			Size = new Vector2(286f, 44f),
			TooltipText = text
		};
		ColorRect val2 = new ColorRect
		{
			Name = "Background",
			Color = new Color(0.18f, 0.24f, 0.31f, 0.92f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		((Control)val2).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Node)val).AddChild(val2, false, Node.InternalMode.Disabled);
		Label val3 = new Label
		{
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		((Control)val3).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Control)val3).AddThemeFontSizeOverride("font_size", 12);
		((Control)val3).AddThemeColorOverride("font_color", new Color(0.94f, 0.97f, 1f, 1f));
		((Node)val).AddChild(val3, false, Node.InternalMode.Disabled);
		((GodotObject)val).Connect(NClickableControl.SignalName.Released, Callable.From(onReleased), 0u);
		return val;
	}

	private static void ConfigureCompactDropdown(OptionButton select)
	{
		if (select == null)
		{
			return;
		}
		ApplyCompactDropdownSize(select);
		((GodotObject)select).Connect(BaseButton.SignalName.Pressed, Callable.From(delegate
		{
			ApplyCompactDropdownSize(select);
		}), 0u);
	}

	private static void ApplyCompactDropdownSize(OptionButton select)
	{
		PopupMenu popup = select.GetPopup();
		if (popup == null)
		{
			return;
		}
		int num = Math.Max(520, Mathf.RoundToInt(((Control)select).Size.X));
		int itemCount = Math.Max(1, popup.ItemCount);
		int num2 = Math.Clamp(14 + itemCount * 34, 72, 176);
		popup.MaxSize = new Vector2I(num, num2);
		popup.Size = new Vector2I(num, num2);
	}

	private static void OnCancelRecoveryPickerReleased(NButton _)
	{
		CloseRecoveryPopup();
	}

	private static void OnRecoverSelectedReleased(NButton _)
	{
		TaskHelper.RunSafely(RecoverSelectedBackupAsync());
	}

	private static async Task RecoverSelectedBackupAsync()
	{
		// EN: Recovery always writes into the current modded profile.
		//     When someone is already in "please save my run" mode, another destination picker
		//     is usually just one more chance to misclick.
		// ZH: 恢复永远写回当前模组档案。
		//     玩家都已经进入“救命把档搞坏了”的状态时，再加一个目标选择器通常只是在增加点错概率。
		NMainMenu mainMenu = NGame.Instance?.MainMenu;
		if (mainMenu == null || !GodotObject.IsInstanceValid(mainMenu))
		{
			return;
		}
		PopupPanel nodeOrNull = ((Node)mainMenu).GetNodeOrNull<PopupPanel>(RecoveryPopupName);
		OptionButton nodeOrNull2 = nodeOrNull?.GetNodeOrNull<OptionButton>("RecoveryVBox/RecoverySelect");
		if (nodeOrNull2 == null)
		{
			UpdateUiState(mainMenu, "No backup selected.");
			return;
		}
		int selected = nodeOrNull2.Selected;
		if (selected < 0 || selected >= ActiveRecoveryEntries.Count)
		{
			UpdateUiState(mainMenu, "No backup selected.");
			return;
		}
		SaveSyncOperationRecord saveSyncOperationRecord = ActiveRecoveryEntries[selected];
		int currentProfileId = GetCurrentProfileId();
		CloseRecoveryPopup();
		LocString body = new LocString("events", "ACT4_DYNAMIC_TEXT");
		LocString header = new LocString("events", "ACT4_DYNAMIC_TEXT");
		body.Add("text", $"Recover this backup into your CURRENT MODDED profile (Profile {currentProfileId})?");
		header.Add("text", "Confirm Recovery");
		if (!await ShowGenericConfirmationAsync(body, header))
		{
			UpdateUiState(mainMenu, "Recovery canceled.");
			return;
		}
		UpdateUiState(mainMenu, $"Recovering into profile {currentProfileId}...");
		string result = await Task.Run(delegate
		{
			return RecoverOperationIntoCurrentProfileInternal(saveSyncOperationRecord, currentProfileId);
		});
		UpdateUiState(mainMenu, result);
		await Task.Yield();
		ReloadCurrentProfile();
		UpdateUiState(NGame.Instance?.MainMenu, result + "\nModded profile reloaded.");
	}

	private static string RecoverOperationIntoCurrentProfileInternal(SaveSyncOperationRecord entry, int targetProfileId)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(entry.BackupDirectory) || !Directory.Exists(entry.BackupDirectory))
			{
				return ModLoc.T("Backup folder missing. Recovery failed.", "备份目录不存在，恢复失败。", fra: "Dossier de sauvegarde introuvable. Récupération échouée.", deu: "Backup-Ordner fehlt. Wiederherstellung fehlgeschlagen.", jpn: "バックアップフォルダが見つかりません。復元に失敗しました。", kor: "백업 폴더를 찾을 수 없습니다. 복구에 실패했습니다.", por: "Pasta de backup não encontrada. Recuperação falhou.", rus: "Папка резервной копии не найдена. Восстановление не удалось.", spa: "Carpeta de backup no encontrada. Recuperación fallida.");
			}
			string savesDir = GetSavesDir(isVanilla: false, targetProfileId);
			Directory.CreateDirectory(savesDir);
			RestoreFileFromBackup(savesDir, "progress.save", entry.BackupDirectory);
			RestoreFileFromBackup(savesDir, "prefs.save", entry.BackupDirectory);
			RestoreFileFromBackup(savesDir, "current_run_slot_active.txt", entry.BackupDirectory);
			if (Directory.Exists(Path.Combine(entry.BackupDirectory, "history")))
			{
				RestoreDirectoryFromBackup(savesDir, "history", entry.BackupDirectory);
			}
			TryPushDestinationProfileToCloud(destinationIsVanilla: false, targetProfileId);
			return $"{ModLoc.T("Recovery complete for profile ", "恢复完成，档案", fra: "Récupération complète pour le profil ", deu: "Wiederherstellung abgeschlossen für Profil ", jpn: "プロファイルの復元完了 ", kor: "프로필 복구 완료 ", por: "Recuperação concluída para o perfil ", rus: "Восстановление завершено для профиля ", spa: "Recuperación completa para el perfil ")}{targetProfileId}.";
		}
		catch (Exception ex)
		{
			Logger.Warn($"Recovery failed: {ex.Message}", 1);
			return ModLoc.T("Recovery failed. Check logs.", "恢复失败。请检查日志。", fra: "Récupération échouée. Consultez les journaux.", deu: "Wiederherstellung fehlgeschlagen. Protokolle prüfen.", jpn: "復元に失敗しました。ログをご確認ください。", kor: "복구에 실패했습니다. 로그를 확인하세요.", por: "Recuperação falhou. Verifique os logs.", rus: "Восстановление не удалось. Проверьте журналы.", spa: "Recuperación fallida. Revisa los registros.");
		}
	}

	private static string FormatHistoryEntryLabel(SaveSyncOperationRecord entry)
	{
		DateTime dateTime = DateTimeOffset.FromUnixTimeSeconds(entry.TimestampUtc).LocalDateTime;
		string text = (entry.DestinationIsVanilla ? ModLoc.T("Vanilla", "原版", fra: "Vanilla", deu: "Vanilla", jpn: "バニラ", kor: "바닐라", por: "Vanilla", rus: "Ванилла", spa: "Vanilla") : ModLoc.T("Modded", "模组", fra: "Moddé", deu: "Modded", jpn: "Mod版", kor: "모드", por: "Modded", rus: "Моды", spa: "Mod"));
		string text2 = (entry.IsSourceBackupMirror ? ModLoc.T("Backed up", "已备份", fra: "Sauvegardé", deu: "Gesichert", jpn: "バックアップ済み", kor: "백업됨", por: "Backup", rus: "Скопировано", spa: "Respaldado") : ModLoc.T("Overwritten", "已覆盖", fra: "Écrasé", deu: "Überschrieben", jpn: "上書き済み", kor: "덮어쓀", por: "Sobrescrito", rus: "Перезаписано", spa: "Sobrescrito"));
		string dateStr = dateTime.ToString(ModLoc.T("MMMM d, yyyy", "yyyy年M月d日", fra: "d MMMM yyyy", deu: "d. MMMM yyyy", jpn: "yyyy年M月d日", kor: "yyyy년 M월 d일", por: "d 'de' MMMM 'de' yyyy", rus: "d MMMM yyyy 'г.'", spa: "d 'de' MMMM 'de' yyyy"));
		return $"{ModLoc.T("Restore ", "恢复 ", fra: "Restaurer ", deu: "Wiederherstellen ", jpn: "復元 ", kor: "복구 ", por: "Restaurar ", rus: "Восстановить ", spa: "Restaurar ")}{dateStr}: {text2} {text} {entry.ProfileId}{ModLoc.T(" Profile. Playtime: ", " 档案。游玩时长：", fra: " Profil. Temps de jeu : ", deu: " Profil. Spielzeit: ", jpn: " プロファイル。プレイ時間：", kor: " 프로필. 플레이 시간: ", por: " Perfil. Tempo de jogo: ", rus: " Профиль. Время игры: ", spa: " Perfil. Tiempo de juego: ")}{FormatPlaytime(entry.Playtime)}";
	}

	private static void CloseRecoveryPopup()
	{
		NMainMenu mainMenu = NGame.Instance?.MainMenu;
		if (mainMenu == null || !GodotObject.IsInstanceValid(mainMenu))
		{
			return;
		}
		PopupPanel nodeOrNull = ((Node)mainMenu).GetNodeOrNull<PopupPanel>(RecoveryPopupName);
		if (nodeOrNull != null)
		{
			((Node)nodeOrNull).QueueFree();
		}
	}

	private static void CloseCopyPopup()
	{
		NMainMenu mainMenu = NGame.Instance?.MainMenu;
		if (mainMenu == null || !GodotObject.IsInstanceValid(mainMenu))
		{
			return;
		}
		PopupPanel nodeOrNull = ((Node)mainMenu).GetNodeOrNull<PopupPanel>(CopyPopupName);
		if (nodeOrNull != null)
		{
			((Node)nodeOrNull).QueueFree();
		}
	}

	private static async Task<bool> ConfirmCopyAsync(bool fromVanillaToModded, int sourceProfileId, int destinationProfileId)
	{
		LocString body = new LocString("events", "ACT4_DYNAMIC_TEXT");
		LocString header = new LocString("events", "ACT4_DYNAMIC_TEXT");
		body.Add("text", fromVanillaToModded
			? string.Format(ModLoc.T("Are you SURE you want to import Vanilla {0} into Modded {1}?", "你确定要将原版{0}导入模组{1}吗？", fra: "Êtes-vous SÛR de vouloir importer Vanilla {0} dans Modded {1} ?", deu: "Bist du SICHER, dass du Vanilla {0} in Modded {1} importieren möchtest?", jpn: "本当にバニラ{0}をMod版{1}にインポートしますか？", kor: "정말로 바닐라 {0}을(를) 모드 {1}로 가져오시겠습니까?", por: "Tem CERTEZA de que quer importar Vanilla {0} para Modded {1}?", rus: "Вы УВЕРЕНЫ, что хотите импортировать Vanilla {0} в Modded {1}?", spa: "¿Estás SEGURO de que quieres importar Vanilla {0} a Modded {1}?"), sourceProfileId, destinationProfileId)
			: string.Format(ModLoc.T("Are you SURE you want to export Modded {0} into Vanilla {1}?", "你确定要将模组{0}导出到原版{1}吗？", fra: "Êtes-vous SÛR de vouloir exporter Modded {0} dans Vanilla {1} ?", deu: "Bist du SICHER, dass du Modded {0} in Vanilla {1} exportieren möchtest?", jpn: "本当にMod版{0}をバニラ{1}にエクスポートしますか？", kor: "정말로 모드 {0}을(를) 바닐라 {1}로 내보내시겠습니까?", por: "Tem CERTEZA de que quer exportar Modded {0} para Vanilla {1}?", rus: "Вы УВЕРЕНЫ, что хотите экспортировать Modded {0} в Vanilla {1}?", spa: "¿Estás SEGURO de que quieres exportar Modded {0} a Vanilla {1}?"), sourceProfileId, destinationProfileId));
		header.Add("text", ModLoc.T("Confirm Save Sync", "确认存档同步", fra: "Confirmer la Sync de Sauvegarde", deu: "Spielstand-Sync bestätigen", jpn: "セーブ同期の確認", kor: "저장 동기화 확인", por: "Confirmar Sincronização de Saves", rus: "Подтвердить синхронизацию сохранений", spa: "Confirmar Sincronización de Guardados"));
		return await ShowGenericConfirmationAsync(body, header);
	}

	private static async Task<bool> ConfirmSuspiciousSourceAsync(string warningText)
	{
		LocString body = new LocString("events", "ACT4_DYNAMIC_TEXT");
		LocString header = new LocString("events", "ACT4_DYNAMIC_TEXT");
		body.Add("text", warningText + ModLoc.T(" Continue anyway?", " 继续吗？", fra: " Continuer quand même ?", deu: " Trotzdem fortfahren?", jpn: " 続けてもよいですか？", kor: " 그래도 계속하시겠습니까?", por: " Continuar mesmo assim?", rus: " Всё равно продолжить?", spa: " ¿Continuar de todos modos?"));
		header.Add("text", ModLoc.T("Warning", "警告", fra: "Avertissement", deu: "Warnung", jpn: "警告", kor: "경고", por: "Aviso", rus: "Предупреждение", spa: "Advertencia"));
		return await ShowGenericConfirmationAsync(body, header);
	}

	private static async Task<bool> ShowGenericConfirmationAsync(LocString body, LocString header)
	{
		NGenericPopup val = NGenericPopup.Create();
		NModalContainer instance = NModalContainer.Instance;
		if (val == null || instance == null)
		{
			Logger.Warn("Could not open confirmation popup.", 1);
			return false;
		}
		instance.Add(val);
		return await val.WaitForConfirmation(body, header, new LocString("main_menu_ui", "GENERIC_POPUP.cancel"), new LocString("main_menu_ui", "GENERIC_POPUP.confirm"));
	}

	private static bool TryGetBlankOrSuspiciousWarning(string sourceSavesDir, out string warningText)
	{
		// EN: Soft brake only.
		//     We are not proving the source is healthy here, just catching the obvious foot-guns
		//     before a real profile gets replaced by an empty shell.
		// ZH: 这里只是软刹车。
		//     目标不是证明来源档一定健康，只是先拦住那些明显的坑，别把正常档换成空壳。
		warningText = string.Empty;
		string path = Path.Combine(sourceSavesDir, "progress.save");
		if (!File.Exists(path))
		{
			warningText = "Source looks blank or invalid: progress.save is missing.";
			return true;
		}
		long length = new FileInfo(path).Length;
		if (length <= 64L)
		{
			warningText = ModLoc.T("Source progress file is very small and may be blank.", "来源进度文件非常小，可能是空存档。");
			return true;
		}
		try
		{
			string json = File.ReadAllText(path);
			ReadSaveResult<SerializableProgress> readSaveResult = SaveManager.FromJson<SerializableProgress>(json);
			if (!readSaveResult.Success || readSaveResult.SaveData == null)
			{
				warningText = ModLoc.T("Source progress file could not be parsed cleanly.", "来源进度文件无法被正常解析。");
				return true;
			}
			SerializableProgress saveData = readSaveResult.SaveData;
			if (saveData.NumberOfRuns <= 0 && saveData.TotalPlaytime <= 0L)
			{
				warningText = ModLoc.T("Source has zero runs and zero playtime and may be near-empty.", "来源存档的局数和游玩时长均为 0，可能接近空白。");
				return true;
			}
			if (saveData.TotalPlaytime <= 0L)
			{
				warningText = ModLoc.T("Source profile playtime is 00:00:00 and may be an empty or placeholder save.", "来源档案的游玩时长为 00:00:00，可能是空存档或占位存档。");
				return true;
			}
		}
		catch
		{
			warningText = ModLoc.T("Source progress file could not be read.", "无法读取来源进度文件。");
			return true;
		}
		return false;
	}

	private static string CopySaveDataInternal(bool fromVanillaToModded, int sourceProfileId, int destinationProfileId)
	{
		// EN: Real file mutation lives here so callers can safely kick it off-thread.
		//     The order matters more than it first appears: back up destination first, mirror source second,
		//     then copy files, and only after that refresh profile-facing state.
		// ZH: 真正改文件的逻辑都放在这里，方便外层安全地丢去后台线程跑。
		//     顺序比看起来更关键，先备份目标，再镜像来源，然后复制文件，最后才刷新玩家看到的状态。
		try
		{
			string accountRootPath = GetAccountRootPath();
			string text = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
			string text2 = fromVanillaToModded ? "_vanilla_to_modded" : "_modded_to_vanilla";
			string backupRoot = Path.Combine(accountRootPath, "act4placeholder", "save_backups", text + text2, $"overwritten_profile{destinationProfileId}");
			string latestSourceRoot = Path.Combine(GetLatestSourceBackupRootPath(), text + text2, $"source_profile{sourceProfileId}");
			string savesDir = GetSavesDir(isVanilla: fromVanillaToModded, sourceProfileId);
			string savesDir2 = GetSavesDir(isVanilla: !fromVanillaToModded, destinationProfileId);
			Logger.Info($"SaveSync copy internal: mode={(fromVanillaToModded ? "import" : "export")}, source='{savesDir}', destination='{savesDir2}'", 1);
			bool backupOverwritten = ReadPlaytimeFromProfileSave(savesDir2) > 0L;
			if (!Directory.Exists(savesDir))
			{
				Logger.Warn($"SaveSync source profile missing: '{savesDir}'", 1);
				return $"{ModLoc.T("Source profile not found: profile", "未找到来源档案：profile")}{sourceProfileId}";
			}
			Directory.CreateDirectory(savesDir2);
			PrepareLatestSourceBackupRoot();
			BackupCurrentSourceState(savesDir, latestSourceRoot, fromVanillaToModded);
			BackupAndCopyProgressFile(savesDir, savesDir2, backupRoot, backupOverwritten, scrubForVanilla: !fromVanillaToModded);
			BackupAndCopyFile(savesDir, savesDir2, "current_run_slot_active.txt", backupRoot, backupOverwritten);
			BackupAndCopyFile(savesDir, savesDir2, "prefs.save", backupRoot, backupOverwritten);
			BackupAndCopyHistoryDirectory(savesDir, savesDir2, backupRoot, backupOverwritten, scrubForVanilla: !fromVanillaToModded);
			TryPushDestinationProfileToCloud(destinationIsVanilla: !fromVanillaToModded, destinationProfileId);
			Logger.Info($"SaveSync copy internal complete: sourceProfile={sourceProfileId}, destinationProfile={destinationProfileId}, backupOverwritten={backupOverwritten}", 1);
			PruneBackupDirectories();
			return $"{ModLoc.T("Copy complete: ", "复制完成：", fra: "Copie réussie : ", deu: "Kopierung abgeschlossen: ", jpn: "コピー完了：", kor: "복사 완료: ", por: "Cópia concluída: ", rus: "Копирование завершено: ", spa: "Copia completa: ")}{(fromVanillaToModded ? ModLoc.T("Vanilla", "原版", fra: "Vanilla", deu: "Vanilla", jpn: "バニラ", kor: "바닐라", por: "Vanilla", rus: "Ванилла", spa: "Vanilla") : ModLoc.T("Modded", "模组", fra: "Moddé", deu: "Modded", jpn: "Mod版", kor: "모드", por: "Modded", rus: "Моды", spa: "Mod"))} {sourceProfileId} -> {(!fromVanillaToModded ? ModLoc.T("Vanilla", "原版", fra: "Vanilla", deu: "Vanilla", jpn: "バニラ", kor: "바닐라", por: "Vanilla", rus: "Ванилла", spa: "Vanilla") : ModLoc.T("Modded", "模组", fra: "Moddé", deu: "Modded", jpn: "Mod版", kor: "모드", por: "Modded", rus: "Моды", spa: "Mod"))} {destinationProfileId}.";
		}
		catch (Exception ex)
		{
			Logger.Warn($"Copy save data failed: {ex.Message}", 1);
			return ModLoc.T("Copy failed. Check logs.", "复制失败。请检查日志。", fra: "Copie échouée. Consultez les journaux.", deu: "Kopierung fehlgeschlagen. Protokolle prüfen.", jpn: "コピーに失敗しました。ログをご確認ください。", kor: "복사에 실패했습니다. 로그를 확인하세요.", por: "Cópia falhou. Verifique os logs.", rus: "Копирование не удалось. Проверьте журналы.", spa: "Copia fallida. Revisa los registros.");
		}
	}

	private static void BackupAndCopyFile(string sourceSavesDir, string destinationSavesDir, string fileName, string backupRoot, bool backupOverwritten)
	{
		string path = Path.Combine(sourceSavesDir, fileName);
		if (!File.Exists(path))
		{
			return;
		}
		string path2 = Path.Combine(destinationSavesDir, fileName);
		if (backupOverwritten && File.Exists(path2))
		{
			string path3 = Path.Combine(backupRoot, fileName);
			Directory.CreateDirectory(Path.GetDirectoryName(path3) ?? backupRoot);
			File.Copy(path2, path3, true);
		}
		File.Copy(path, path2, true);
	}

	private static void BackupAndCopyProgressFile(string sourceSavesDir, string destinationSavesDir, string backupRoot, bool backupOverwritten, bool scrubForVanilla)
	{
		string path = Path.Combine(sourceSavesDir, "progress.save");
		if (!File.Exists(path))
		{
			return;
		}
		string path2 = Path.Combine(destinationSavesDir, "progress.save");
		if (backupOverwritten && File.Exists(path2))
		{
			string path3 = Path.Combine(backupRoot, "progress.save");
			Directory.CreateDirectory(Path.GetDirectoryName(path3) ?? backupRoot);
			File.Copy(path2, path3, true);
		}
		if (!scrubForVanilla)
		{
			File.Copy(path, path2, true);
			return;
		}
		try
		{
			string json = File.ReadAllText(path);
			ReadSaveResult<SerializableProgress> readSaveResult = SaveManager.FromJson<SerializableProgress>(json);
			if (!readSaveResult.Success || readSaveResult.SaveData == null)
			{
				Logger.Warn("Export to vanilla: progress.save could not be parsed, copying raw file.", 1);
				File.Copy(path, path2, true);
				return;
			}
			SerializableProgress saveData = readSaveResult.SaveData;
			if (!ScrubAct4ProgressForVanilla(saveData))
			{
				File.Copy(path, path2, true);
				return;
			}
			string json2 = SaveManager.ToJson(saveData);
			if (!IsPlausibleScrubbedProgressJson(json, json2))
			{
				Logger.Warn("Export to vanilla: scrub result looked invalid (playtime/runs/stats collapsed). Copying raw progress.save instead.", 1);
				File.Copy(path, path2, true);
				return;
			}
			File.WriteAllText(path2, json2);
			Logger.Info("Export to vanilla: wrote scrubbed progress.save without Act4Placeholder IDs.", 1);
		}
		catch (Exception ex)
		{
			Logger.Warn($"Export to vanilla: progress scrub failed, copying raw file instead. {ex.Message}", 1);
			File.Copy(path, path2, true);
		}
	}

	private static bool IsPlausibleScrubbedProgressJson(string sourceJson, string scrubbedJson)
	{
		if (!TryReadProgressSummary(sourceJson, out var source) || !TryReadProgressSummary(scrubbedJson, out var scrubbed))
		{
			// If we cannot inspect metrics, do not block export.
			return true;
		}
		if (source.TotalPlaytime > 0L && scrubbed.TotalPlaytime <= 0L)
		{
			return false;
		}
		if (source.NumberOfRuns > 0 && scrubbed.NumberOfRuns <= 0)
		{
			return false;
		}
		if (source.CharacterStatsCount > 0 && scrubbed.CharacterStatsCount <= 0)
		{
			return false;
		}
		if (source.EncounterStatsCount > 0 && scrubbed.EncounterStatsCount <= 0)
		{
			return false;
		}
		if (source.EnemyStatsCount > 0 && scrubbed.EnemyStatsCount <= 0)
		{
			return false;
		}
		if (source.AncientStatsCount > 0 && scrubbed.AncientStatsCount <= 0)
		{
			return false;
		}
		// Discovery pools can shrink if ACT4 data is removed, but should not collapse to empty
		// when the source has non-trivial vanilla progress.
		if (source.DiscoveredCardsCount > 20 && scrubbed.DiscoveredCardsCount < 5)
		{
			return false;
		}
		if (source.DiscoveredRelicsCount > 10 && scrubbed.DiscoveredRelicsCount < 2)
		{
			return false;
		}
		return true;
	}

	private static bool TryReadProgressSummary(string json, out ProgressSummary summary)
	{
		summary = default;
		if (string.IsNullOrWhiteSpace(json))
		{
			return false;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			JsonElement rootElement = jsonDocument.RootElement;
			summary = new ProgressSummary
			{
				TotalPlaytime = GetInt64Property(rootElement, "total_playtime"),
				NumberOfRuns = GetInt32Property(rootElement, "number_of_runs"),
				CharacterStatsCount = GetArrayCount(rootElement, "character_stats"),
				EncounterStatsCount = GetArrayCount(rootElement, "encounter_stats"),
				EnemyStatsCount = GetArrayCount(rootElement, "enemy_stats"),
				AncientStatsCount = GetArrayCount(rootElement, "ancient_stats"),
				DiscoveredActsCount = GetArrayCount(rootElement, "discovered_acts"),
				DiscoveredCardsCount = GetArrayCount(rootElement, "discovered_cards"),
				DiscoveredEventsCount = GetArrayCount(rootElement, "discovered_events"),
				DiscoveredPotionsCount = GetArrayCount(rootElement, "discovered_potions"),
				DiscoveredRelicsCount = GetArrayCount(rootElement, "discovered_relics")
			};
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static int GetArrayCount(JsonElement root, string propertyName)
	{
		if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return 0;
		}
		return value.GetArrayLength();
	}

	private static int GetInt32Property(JsonElement root, string propertyName)
	{
		if (!root.TryGetProperty(propertyName, out var value))
		{
			return 0;
		}
		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var value2))
		{
			return value2;
		}
		if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var result))
		{
			return result;
		}
		return 0;
	}

	private static long GetInt64Property(JsonElement root, string propertyName)
	{
		if (!root.TryGetProperty(propertyName, out var value))
		{
			return 0L;
		}
		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var value2))
		{
			return value2;
		}
		if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var result))
		{
			return result;
		}
		return 0L;
	}

	private static bool ScrubAct4ProgressForVanilla(SerializableProgress progress)
	{
		bool changed = false;
		changed |= RemoveWhere(progress.CharStats, stat => IsAct4PlaceholderProgressId(stat.Id));
		changed |= RemoveWhere(progress.CardStats, stat => IsAct4PlaceholderProgressId(stat.Id));
		changed |= RemoveWhere(progress.EncounterStats, stat => IsAct4PlaceholderProgressId(stat.Id));
		changed |= RemoveWhere(progress.EnemyStats, stat => IsAct4PlaceholderProgressId(stat.Id));
		changed |= RemoveWhere(progress.AncientStats, stat => IsAct4PlaceholderProgressId(stat.Id));
		changed |= RemoveWhere(progress.DiscoveredCards, IsAct4PlaceholderProgressId);
		changed |= RemoveWhere(progress.DiscoveredRelics, IsAct4PlaceholderProgressId);
		changed |= RemoveWhere(progress.DiscoveredEvents, IsAct4PlaceholderProgressId);
		changed |= RemoveWhere(progress.DiscoveredPotions, IsAct4PlaceholderProgressId);
		changed |= RemoveWhere(progress.DiscoveredActs, IsAct4PlaceholderProgressId);
		if (IsAct4PlaceholderProgressId(progress.PendingCharacterUnlock))
		{
			progress.PendingCharacterUnlock = ModelId.none;
			changed = true;
		}
		return changed;
	}

	private static bool IsAct4PlaceholderProgressId(ModelId? modelId)
	{
		if (modelId == null || modelId == ModelId.none)
		{
			return false;
		}
		string entry = modelId.Entry ?? string.Empty;
		return entry.StartsWith("ACT4_", StringComparison.OrdinalIgnoreCase)
			|| entry.StartsWith("ARCHITECT", StringComparison.OrdinalIgnoreCase)
			|| entry.Contains("_ARCHITECT_", StringComparison.OrdinalIgnoreCase)
			|| entry.Contains("THE_ARCHITECT", StringComparison.OrdinalIgnoreCase);
	}

	private static bool RemoveWhere<T>(List<T>? list, Predicate<T> predicate)
	{
		if (list == null || list.Count == 0)
		{
			return false;
		}
		int count = list.Count;
		list.RemoveAll(predicate);
		return list.Count != count;
	}

	private static void PrepareLatestSourceBackupRoot()
	{
		string latestSourceBackupRootPath = GetLatestSourceBackupRootPath();
		if (Directory.Exists(latestSourceBackupRootPath))
		{
			Directory.Delete(latestSourceBackupRootPath, true);
		}
		Directory.CreateDirectory(latestSourceBackupRootPath);
	}

	private static void BackupCurrentSourceState(string sourceSavesDir, string latestSourceRoot, bool sourceIsVanilla)
	{
		if (!Directory.Exists(sourceSavesDir))
		{
			return;
		}
		Directory.CreateDirectory(latestSourceRoot);
		CopyFileIfExists(Path.Combine(sourceSavesDir, "progress.save"), Path.Combine(latestSourceRoot, "progress.save"));
		CopyFileIfExists(Path.Combine(sourceSavesDir, "prefs.save"), Path.Combine(latestSourceRoot, "prefs.save"));
		CopyFileIfExists(Path.Combine(sourceSavesDir, "current_run_slot_active.txt"), Path.Combine(latestSourceRoot, "current_run_slot_active.txt"));
		string path = Path.Combine(sourceSavesDir, "history");
		if (Directory.Exists(path))
		{
			CopyDirectory(path, Path.Combine(latestSourceRoot, "history"));
		}
	}

	private static void CopyFileIfExists(string sourcePath, string destinationPath)
	{
		if (!File.Exists(sourcePath))
		{
			return;
		}
		string directoryName = Path.GetDirectoryName(destinationPath);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		File.Copy(sourcePath, destinationPath, true);
	}

	private static void BackupAndCopyDirectory(string sourceSavesDir, string destinationSavesDir, string dirName, string backupRoot, bool backupOverwritten)
	{
		string path = Path.Combine(sourceSavesDir, dirName);
		if (!Directory.Exists(path))
		{
			return;
		}
		string path2 = Path.Combine(destinationSavesDir, dirName);
		if (Directory.Exists(path2))
		{
			if (backupOverwritten)
			{
				string backupDirectory = Path.Combine(backupRoot, dirName);
				CopyDirectory(path2, backupDirectory);
			}
			Directory.Delete(path2, true);
		}
		CopyDirectory(path, path2);
	}

	private static void BackupAndCopyHistoryDirectory(string sourceSavesDir, string destinationSavesDir, string backupRoot, bool backupOverwritten, bool scrubForVanilla)
	{
		string path = Path.Combine(sourceSavesDir, "history");
		if (!Directory.Exists(path))
		{
			return;
		}
		string path2 = Path.Combine(destinationSavesDir, "history");
		if (Directory.Exists(path2))
		{
			if (backupOverwritten)
			{
				string backupDirectory = Path.Combine(backupRoot, "history");
				CopyDirectory(path2, backupDirectory);
			}
			Directory.Delete(path2, true);
		}
		if (!scrubForVanilla)
		{
			CopyDirectory(path, path2);
			return;
		}
		CopyHistoryDirectoryForVanilla(path, path2);
	}

	private static void CopyHistoryDirectoryForVanilla(string sourceHistoryDir, string destinationHistoryDir)
	{
		Directory.CreateDirectory(destinationHistoryDir);
		foreach (string file in Directory.GetFiles(sourceHistoryDir))
		{
			string fileName = Path.GetFileName(file);
			string path = Path.Combine(destinationHistoryDir, fileName);
			if (fileName.EndsWith(".run", StringComparison.OrdinalIgnoreCase))
			{
				CopyAndScrubRunHistoryFileForVanilla(file, path);
			}
			else
			{
				File.Copy(file, path, true);
			}
		}
	}

	private static void CopyAndScrubRunHistoryFileForVanilla(string sourceFilePath, string destinationFilePath)
	{
		try
		{
			string json = File.ReadAllText(sourceFilePath);
			ReadSaveResult<RunHistory> readSaveResult = SaveManager.FromJson<RunHistory>(json);
			if (!readSaveResult.Success || readSaveResult.SaveData == null)
			{
				File.Copy(sourceFilePath, destinationFilePath, true);
				return;
			}
			RunHistory saveData = readSaveResult.SaveData;
			if (!IsAct4RunHistory(saveData))
			{
				File.Copy(sourceFilePath, destinationFilePath, true);
				return;
			}
			RunHistory runHistory = BuildAct3RunHistoryForVanillaExport(saveData);
			string contents = SaveManager.ToJson(runHistory);
			File.WriteAllText(destinationFilePath, contents);
		}
		catch (Exception ex)
		{
			Logger.Warn($"Export to vanilla: run history scrub failed for '{Path.GetFileName(sourceFilePath)}', copying raw file. {ex.Message}", 1);
			File.Copy(sourceFilePath, destinationFilePath, true);
		}
	}

	private static bool IsAct4RunHistory(RunHistory history)
	{
		return history?.Acts != null && history.Acts.Count > 3;
	}

	private static RunHistory BuildAct3RunHistoryForVanillaExport(RunHistory history)
	{
		if (!IsAct4RunHistory(history))
		{
			return history;
		}
		RunHistory snapshot;
		if (ModSupport.TryGetAct3Snapshot(history, out snapshot) && snapshot != null)
		{
			return CreateAct3FilteredRunForVanillaExport(snapshot);
		}
		int num = Math.Min(3, history.Acts.Count);
		RunHistory history2 = new RunHistory
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
		return CreateAct3FilteredRunForVanillaExport(history2);
	}

	private static RunHistory CreateAct3FilteredRunForVanillaExport(RunHistory history)
	{
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

	private static void RestoreFileFromBackup(string destinationSavesDir, string fileName, string backupRoot)
	{
		string path = Path.Combine(destinationSavesDir, fileName);
		string path2 = Path.Combine(backupRoot, fileName);
		if (!File.Exists(path2))
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			return;
		}
		File.Copy(path2, path, true);
	}

	private static void RestoreDirectoryFromBackup(string destinationSavesDir, string dirName, string backupRoot)
	{
		string path = Path.Combine(destinationSavesDir, dirName);
		if (Directory.Exists(path))
		{
			Directory.Delete(path, true);
		}
		string path2 = Path.Combine(backupRoot, dirName);
		if (!Directory.Exists(path2))
		{
			return;
		}
		CopyDirectory(path2, path);
	}

	private static void CopyDirectory(string sourceDir, string destinationDir)
	{
		Directory.CreateDirectory(destinationDir);
		foreach (string file in Directory.GetFiles(sourceDir))
		{
			string fileName = Path.GetFileName(file);
			File.Copy(file, Path.Combine(destinationDir, fileName), true);
		}
		foreach (string directory in Directory.GetDirectories(sourceDir))
		{
			string fileName2 = Path.GetFileName(directory);
			CopyDirectory(directory, Path.Combine(destinationDir, fileName2));
		}
	}

	private static void TryPushDestinationProfileToCloud(bool destinationIsVanilla, int destinationProfileId)
	{
		try
		{
			SaveManager instance = SaveManager.Instance;
			CloudSaveStore cloudSaveStore = GetCloudSaveStore(instance);
			if (cloudSaveStore == null)
			{
				Logger.Info("SaveSync cloud push skipped: active save store is not cloud-backed.", 1);
				return;
			}
			bool wasModded = UserDataPathProvider.IsRunningModded;
			try
			{
				UserDataPathProvider.IsRunningModded = !destinationIsVanilla;
				List<Task> list = new List<Task>
				{
					cloudSaveStore.OverwriteCloudWithLocal(ProgressSaveManager.GetProgressPathForProfile(destinationProfileId)),
					cloudSaveStore.OverwriteCloudWithLocal(PrefsSaveManager.GetPrefsPath(destinationProfileId)),
					cloudSaveStore.OverwriteCloudWithLocal(RunSaveManager.GetRunSavePath(destinationProfileId, "current_run.save")),
					cloudSaveStore.OverwriteCloudWithLocal(RunSaveManager.GetRunSavePath(destinationProfileId, "current_run_mp.save")),
					cloudSaveStore.OverwriteCloudWithLocal(Path.Combine(UserDataPathProvider.GetProfileDir(destinationProfileId), UserDataPathProvider.SavesDir, "current_run_slot_active.txt"))
				};
				list.AddRange(cloudSaveStore.OverwriteCloudWithLocalDirectory(RunHistorySaveManager.GetHistoryPath(destinationProfileId), 5242880, 100));
				cloudSaveStore.BeginSaveBatch();
				try
				{
					Task.WhenAll(list).GetAwaiter().GetResult();
				}
				finally
				{
					cloudSaveStore.EndSaveBatch();
				}
			}
			finally
			{
				UserDataPathProvider.IsRunningModded = wasModded;
			}
			Logger.Info($"SaveSync cloud push complete: target={(destinationIsVanilla ? "vanilla" : "modded")} profile {destinationProfileId}.", 1);
		}
		catch (Exception ex)
		{
			Logger.Warn($"SaveSync cloud push failed for profile {destinationProfileId}: {ex.Message}", 1);
		}
	}

	private static CloudSaveStore? GetCloudSaveStore(SaveManager saveManager)
	{
		try
		{
			return AccessTools.Field(typeof(SaveManager), "_saveStore")?.GetValue(saveManager) as CloudSaveStore;
		}
		catch
		{
			return null;
		}
	}

	private static void ReloadCurrentProfile()
	{
		SaveManager instance = SaveManager.Instance;
		if (instance == null)
		{
			return;
		}
		int currentProfileId = GetCurrentProfileId();
		instance.SwitchProfileId(currentProfileId);
		ReadSaveResult<PrefsSave> prefsReadResult = instance.InitPrefsData();
		ReadSaveResult<SerializableProgress> progressReadResult = instance.InitProgressData();
		NGame instance2 = NGame.Instance;
		if (instance2 != null)
		{
			instance2.ReloadMainMenu();
			instance2.CheckShowSaveFileError(progressReadResult, prefsReadResult, new ReadSaveResult<SettingsSave>(instance.SettingsSave));
		}
	}

	private static List<SaveSyncOperationRecord> DiscoverBackupOperations()
	{
		try
		{
			string backupRootPath = GetBackupRootPath();
			List<SaveSyncOperationRecord> list = new List<SaveSyncOperationRecord>();
			if (Directory.Exists(backupRootPath))
			{
				foreach (string item in Directory.GetDirectories(backupRootPath))
				{
					string fileName = Path.GetFileName(item);
					bool flag = fileName.Contains("_vanilla_to_modded", StringComparison.OrdinalIgnoreCase);
					bool flag2 = fileName.Contains("_modded_to_vanilla", StringComparison.OrdinalIgnoreCase);
					if (!flag && !flag2)
					{
						continue;
					}
					long timestampUtc = ParseBackupTimestampToUnixSeconds(fileName);
					foreach (string item2 in Directory.GetDirectories(item, "*profile*"))
					{
						string fileName2 = Path.GetFileName(item2);
						int profileId;
						if (!TryParseTrailingProfile(fileName2, "overwritten_profile", out profileId) && !TryParseTrailingProfile(fileName2, "profile", out profileId))
						{
							continue;
						}
						list.Add(new SaveSyncOperationRecord
						{
							TimestampUtc = timestampUtc,
							ProfileId = Math.Clamp(profileId, 1, 3),
							FromVanillaToModded = flag,
							DestinationIsVanilla = flag2,
							IsSourceBackupMirror = false,
							Playtime = ReadPlaytimeFromProfileSave(item2),
							BackupDirectory = item2
						});
					}
				}
			}
			string latestSourceBackupRootPath = GetLatestSourceBackupRootPath();
			if (Directory.Exists(latestSourceBackupRootPath))
			{
				foreach (string item3 in Directory.GetDirectories(latestSourceBackupRootPath))
				{
					string fileName3 = Path.GetFileName(item3);
					bool flag3 = fileName3.Contains("_vanilla_to_modded", StringComparison.OrdinalIgnoreCase);
					bool flag4 = fileName3.Contains("_modded_to_vanilla", StringComparison.OrdinalIgnoreCase);
					if (!flag3 && !flag4)
					{
						continue;
					}
					long timestampUtc2 = ParseBackupTimestampToUnixSeconds(fileName3);
					foreach (string item4 in Directory.GetDirectories(item3, "source_profile*"))
					{
						string fileName4 = Path.GetFileName(item4);
						if (!TryParseTrailingProfile(fileName4, "source_profile", out var profileId2))
						{
							continue;
						}
						list.Add(new SaveSyncOperationRecord
						{
							TimestampUtc = timestampUtc2,
							ProfileId = Math.Clamp(profileId2, 1, 3),
							FromVanillaToModded = flag3,
							DestinationIsVanilla = flag3,
							IsSourceBackupMirror = true,
							Playtime = ReadPlaytimeFromProfileSave(item4),
							BackupDirectory = item4
						});
					}
				}
			}
			return list.OrderByDescending((SaveSyncOperationRecord e) => e.TimestampUtc).ToList();
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed discovering save backups: {ex.Message}", 1);
			return new List<SaveSyncOperationRecord>();
		}
	}

	private static long ParseBackupTimestampToUnixSeconds(string folderName)
	{
		try
		{
			string text = folderName.Split('_').Length >= 2 ? string.Join("_", folderName.Split('_').Take(2)) : folderName;
			if (DateTime.TryParseExact(text, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result))
			{
				return new DateTimeOffset(result).ToUnixTimeSeconds();
			}
		}
		catch
		{
		}
		return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
	}

	private static bool TryParseTrailingProfile(string folderName, string prefix, out int profileId)
	{
		profileId = 1;
		if (!folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return int.TryParse(folderName.Substring(prefix.Length), out profileId);
	}

	private static void PruneBackupDirectories()
	{
		List<SaveSyncOperationRecord> list = DiscoverBackupOperations();
		if (list.Count > MaxBackupEntries)
		{
			List<SaveSyncOperationRecord> list2 = list.Skip(MaxBackupEntries).ToList();
			foreach (string item2 in list2.Select((SaveSyncOperationRecord e) => Path.GetDirectoryName(e.BackupDirectory)).Where((string d) => !string.IsNullOrWhiteSpace(d)).Distinct())
			{
				TryDeleteBackupDirectory(item2);
			}
		}
	}

	private static void TryDeleteBackupDirectory(string backupDirectory)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(backupDirectory) && Directory.Exists(backupDirectory))
			{
				Directory.Delete(backupDirectory, true);
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed deleting old backup directory '{backupDirectory}': {ex.Message}", 1);
		}
	}

	private static int GetCurrentProfileId()
	{
		return Math.Clamp(SaveManager.Instance?.CurrentProfileId ?? 1, 1, 3);
	}

	private static List<ProfileOption> BuildProfileOptions(bool isVanilla)
	{
		List<ProfileOption> list = new List<ProfileOption>();
		for (int i = 1; i <= 3; i++)
		{
			string savesDir = GetSavesDir(isVanilla, i);
			string warningText;
			bool isLikelyBlank = TryGetBlankOrSuspiciousWarning(savesDir, out warningText);
			list.Add(new ProfileOption
			{
				ProfileId = i,
				SavesDir = savesDir,
				Playtime = ReadPlaytimeFromProfileSave(savesDir),
				IsLikelyBlank = isLikelyBlank,
				WarningText = warningText
			});
		}
		return list;
	}

	private static string BuildProfileOptionLabel(ProfileOption option, bool isVanilla)
	{
		string modeLabel = isVanilla
			? ModLoc.T("Vanilla", "原版", fra: "Vanilla", deu: "Vanilla", jpn: "バニラ", kor: "바닐라", por: "Vanilla", rus: "Ванилла", spa: "Vanilla")
			: ModLoc.T("Modded", "模组", fra: "Moddé", deu: "Modded", jpn: "Mod版", kor: "모드", por: "Modded", rus: "Моды", spa: "Mod");
		string playtimeLabel = ModLoc.T("Playtime", "游玩时长", fra: "Temps de jeu", deu: "Spielzeit", jpn: "プレイ時間", kor: "플레이 시간", por: "Tempo de jogo", rus: "Время игры", spa: "Tiempo de juego");
		string text = $"{modeLabel} {option.ProfileId} | {playtimeLabel}: {FormatPlaytime(option.Playtime)}";
		if (option.IsLikelyBlank)
		{
			text += " | " + ModLoc.T("Likely Empty", "可能为空", fra: "Probablement vide", deu: "Wahrscheinlich leer", jpn: "空の可能性", kor: "비어 있을 가능성", por: "Provavelmente vazio", rus: "Вероятно пусто", spa: "Probablemente vacío");
		}
		return text;
	}

	private static long ReadPlaytimeFromProfileSave(string savesDir)
	{
		try
		{
			string path = Path.Combine(savesDir, "progress.save");
			if (!File.Exists(path))
			{
				return 0L;
			}
			string json = File.ReadAllText(path);
			ReadSaveResult<SerializableProgress> readSaveResult = SaveManager.FromJson<SerializableProgress>(json);
			return (!readSaveResult.Success || readSaveResult.SaveData == null) ? 0 : Math.Max(0L, readSaveResult.SaveData.TotalPlaytime);
		}
		catch
		{
			return 0L;
		}
	}

	private static string FormatPlaytime(long totalPlaytime)
	{
		try
		{
			return TimeFormatting.Format(Math.Max(0L, totalPlaytime));
		}
		catch
		{
			return "00:00:00";
		}
	}

	private static string GetAccountRootPath()
	{
		// Keep backups under the standard vanilla account root rather than any live modded
		// save-store redirect so they remain easy to find outside the mod sandbox.
		return ProjectSettings.GlobalizePath(UserDataPathProvider.GetAccountScopedBasePath(null));
	}

	private static string GetSavesDir(bool isVanilla, int profileId)
	{
		string profileScopedSavesPath = GetProfileScopedSavesPathForMode(isVanilla, profileId);
		return ProjectSettings.GlobalizePath(profileScopedSavesPath);
	}

	private static string GetProfileScopedSavesPathForMode(bool isVanilla, int profileId)
	{
		bool wasModded = UserDataPathProvider.IsRunningModded;
		try
		{
			UserDataPathProvider.IsRunningModded = !isVanilla;
			return UserDataPathProvider.GetProfileScopedPath(profileId, UserDataPathProvider.SavesDir);
		}
		finally
		{
			UserDataPathProvider.IsRunningModded = wasModded;
		}
	}

	private static string GetBackupRootPath()
	{
		return Path.Combine(GetAccountRootPath(), BackupRootRelativePath.Replace('/', Path.DirectorySeparatorChar));
	}

	private static string GetLatestSourceBackupRootPath()
	{
		return Path.Combine(GetAccountRootPath(), LatestSourceBackupRootRelativePath.Replace('/', Path.DirectorySeparatorChar));
	}

	private static void UpdateUiState(NMainMenu mainMenu, string message)
	{
		if (mainMenu == null || !GodotObject.IsInstanceValid(mainMenu))
		{
			return;
		}
		Control nodeOrNull = ((Node)mainMenu).GetNodeOrNull<Control>(RootName);
		Label nodeOrNull2 = nodeOrNull?.GetNodeOrNull<Label>("VBox/" + SaveSyncContentName + "/" + StatusLabelName);
		if (nodeOrNull2 == null)
		{
			return;
		}
		int currentProfileId = GetCurrentProfileId();
		nodeOrNull2.Text = $"Profile {currentProfileId}\n{message}";
	}

	private static void UpdatePanelVisibility(NMainMenu mainMenu)
	{
		if (mainMenu == null || !GodotObject.IsInstanceValid(mainMenu))
		{
			return;
		}
		Control nodeOrNull = ((Node)mainMenu).GetNodeOrNull<Control>(RootName);
		if (nodeOrNull == null)
		{
			return;
		}
		try
		{
			Control nodeOrNull2 = ((Node)mainMenu).GetNodeOrNull<Control>("MainMenuTextButtons");
			bool submenusOpen = mainMenu.SubmenuStack?.SubmenusOpen ?? false;
			bool flag = nodeOrNull2 != null ? ((CanvasItem)nodeOrNull2).IsVisibleInTree() : !submenusOpen;
			bool visible = ((CanvasItem)mainMenu).Visible && flag;
			((CanvasItem)nodeOrNull).Visible = visible;
			if (!visible || submenusOpen)
			{
				CloseRecoveryPopup();
				CloseCopyPopup();
			}
		}
		catch (Exception ex)
		{
			Logger.Error($"[SaveSync] EXCEPTION in UpdatePanelVisibility: {ex.GetType().Name}: {ex.Message}", 1);
		}
	}
}
