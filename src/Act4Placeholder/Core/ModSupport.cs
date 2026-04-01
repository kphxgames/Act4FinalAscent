//=============================================================================
// ModSupport.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Central support library for Act 4: manages run snapshots, boss-victory tracking, save file helpers, admin debug buttons, and the core ProceedToAct4Async transition logic.
// ZH: 第四幕核心支持库：管理跑图快照、Boss胜利记录、存档辅助方法、管理员调试按钮及进入第四幕的异步过渡逻辑。
//=============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.addons.mega_text;

namespace Act4Placeholder;

internal static class ModSupport
{
	// EN: Reflection-based JSON options for our private nested store types that are unknown
	//     to the game's source-generated MegaCritSerializerContext. Using these avoids the
	//     "JsonTypeInfo metadata not provided by TypeInfoResolver" error on SaveManager.ToJson.
	// ZH: 使用反射的JSON选项，专门用于私有内部存档类型。
	private static readonly JsonSerializerOptions Act4StoreJsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		WriteIndented = false
	};

	private static string Act4StoreToJson<T>(T obj) => JsonSerializer.Serialize(obj, Act4StoreJsonOptions);

	private static T? Act4StoreFromJson<T>(string json) where T : new()
	{
		try { return JsonSerializer.Deserialize<T>(json, Act4StoreJsonOptions); }
		catch { return default; }
	}

	private sealed class Act3SnapshotEntry
	{
		[JsonPropertyName("key")]
		public string Key { get; set; } = string.Empty;

		[JsonPropertyName("run_history")]
		public RunHistory Snapshot { get; set; } = new RunHistory();
	}

	private sealed class Act3SnapshotStore : ISaveSchema
	{
		[JsonPropertyName("schema_version")]
		public int SchemaVersion { get; set; } = 1;

		[JsonPropertyName("entries")]
		public List<Act3SnapshotEntry> Entries { get; set; } = new List<Act3SnapshotEntry>();
	}

	private sealed class Act4BossVictoryStore : ISaveSchema
	{
		[JsonPropertyName("schema_version")]
		public int SchemaVersion { get; set; } = 1;

		[JsonPropertyName("keys")]
		public List<string> Keys { get; set; } = new List<string>();
	}

	private sealed class Act4BrutalRunStore : ISaveSchema
	{
		[JsonPropertyName("schema_version")]
		public int SchemaVersion { get; set; } = 1;

		[JsonPropertyName("keys")]
		public List<string> Keys { get; set; } = new List<string>();
	}

	// Grand Library book-choice flags persisted per-run (bitmask: bit0=Holy, bit1=Shadow, bit2=Silver, bit3=Cursed)
	private sealed class Act4BookChoiceEntry
	{
		[JsonPropertyName("key")]
		public string Key { get; set; } = string.Empty;

		[JsonPropertyName("bitmask")]
		public int Bitmask { get; set; }
	}

	private sealed class Act4BookChoiceStore : ISaveSchema
	{
		[JsonPropertyName("schema_version")]
		public int SchemaVersion { get; set; } = 2;

		[JsonPropertyName("entries")]
		public List<Act4BookChoiceEntry> Entries { get; set; } = new List<Act4BookChoiceEntry>();

		[JsonPropertyName("choices")]
		public Dictionary<string, int> Choices { get; set; } = new Dictionary<string, int>();
	}

	private sealed class RunDamageContributionPlayerEntry
	{
		[JsonPropertyName("player_net_id")]
		public ulong PlayerNetId { get; set; }

		[JsonPropertyName("damage")]
		public long Damage { get; set; }
	}

	private sealed class RunDamageContributionEntry
	{
		[JsonPropertyName("key")]
		public string Key { get; set; } = string.Empty;

		[JsonPropertyName("players")]
		public List<RunDamageContributionPlayerEntry> Players { get; set; } = new List<RunDamageContributionPlayerEntry>();
	}

	private sealed class RunDamageContributionStore : ISaveSchema
	{
		[JsonPropertyName("schema_version")]
		public int SchemaVersion { get; set; } = 1;

		[JsonPropertyName("entries")]
		public List<RunDamageContributionEntry> Entries { get; set; } = new List<RunDamageContributionEntry>();
	}

	// Weakest damage contributor in co-op: Str+Dex buff entitlement per player per run
	private sealed class Act4WeakestBuffStore : ISaveSchema
	{
		[JsonPropertyName("schema_version")]
		public int SchemaVersion { get; set; } = 1;

		// Each entry is "{runKey}|{playerNetId}"
		[JsonPropertyName("player_run_keys")]
		public List<string> PlayerRunKeys { get; set; } = new List<string>();
	}

	// UI toggle preferences (persisted across game restarts, profile-scoped)
	private sealed class Act4PrefsStore : ISaveSchema
	{
		[JsonPropertyName("schema_version")]
		public int SchemaVersion { get; set; } = 1;

		[JsonPropertyName("help_potions_enabled")]
		public bool HelpPotionsEnabled { get; set; }

		[JsonPropertyName("extra_rewards_enabled")]
		public bool ExtraRewardsEnabled { get; set; }
	}

	internal const string ArchitectBossEncounterId = "ACT4_ARCHITECT_BOSS_ENCOUNTER";

	private static readonly MegaCrit.Sts2.Core.Logging.Logger Logger = new MegaCrit.Sts2.Core.Logging.Logger("Act4Placeholder", (LogType)0);

	private static readonly FieldInfo? RunStateActsField = AccessTools.Field(typeof(RunState), "<Acts>k__BackingField");

	private static readonly FieldInfo? ActModelRoomsField = AccessTools.Field(typeof(ActModel), "_rooms");

	private static readonly FieldInfo? RunManagerStartTimeField = AccessTools.Field(typeof(RunManager), "_startTime");

	private static readonly PropertyInfo? NCreatureVisualsProperty = AccessTools.Property(typeof(NCreature), "Visuals");

	private static readonly HashSet<ulong> AdminPlayers = new HashSet<ulong>();

	private static readonly HashSet<RunState> Act4BossVictories = new HashSet<RunState>();

	private static readonly HashSet<RunState> BrutalAct4Runs = new HashSet<RunState>();

	private static bool _architectDifficultyChoiceActive;

	private static uint _architectDifficultyNormalIndex;

	private static uint _architectDifficultyBrutalIndex;

	private const string Act3SnapshotPath = "act4placeholder/act3_run_snapshots.json";

	private const string Act4BossVictoryPath = "act4placeholder/act4_boss_victories.json";

	private const string Act4BrutalRunsPath = "act4placeholder/act4_brutal_runs.json";

	private const string Act4EnteredWithoutVictoryPath = "act4placeholder/act4_entered_without_victory.json";

	private const string Act4BookChoicesPath = "act4placeholder/act4_book_choices.json";

	private const string Act4RunDamageContributionsPath = "act4placeholder/act4_run_damage_contributions.json";

	private const string Act4WeakestBuffPath = "act4placeholder/act4_weakest_buff_grants.json";

	private const string Act4PrefsPath = "act4placeholder/act4_prefs.json";

	private const int MaxPersistedBookChoices = 20;

	private const int MaxPersistedRunDamageContributions = 20;

	private static readonly Dictionary<string, RunHistory> Act3SnapshotsByKey = new Dictionary<string, RunHistory>();

	private static readonly HashSet<string> Act4BossVictoryKeys = new HashSet<string>();

	private static readonly HashSet<string> Act4BrutalRunKeys = new HashSet<string>();

	// Runs that entered Act 4 but did not defeat the Architect boss
	private static readonly HashSet<string> Act4EnteredWithoutVictoryKeys = new HashSet<string>();

	// key → bitmask (bit0=Holy, bit1=Shadow, bit2=Silver, bit3=Cursed)
	private static readonly Dictionary<string, int> BookChoicesByRunKey = new Dictionary<string, int>();

	// key(run) -> (playerNetId -> total damage contributed this run)
	private static readonly Dictionary<string, Dictionary<ulong, long>> RunDamageContributionByRunKey = new Dictionary<string, Dictionary<ulong, long>>();

	private static readonly List<string> RunDamageContributionRunKeyOrder = new List<string>();

	private static readonly object RunDamageContributionLock = new object();

	private static readonly List<string> BookChoiceRunKeyOrder = new List<string>();

	private static bool _act3SnapshotsLoaded;

	private static int _act3SnapshotsProfileId = -1;

	private static bool _act4BossVictoriesLoaded;

	private static int _act4BossVictoriesProfileId = -1;

	private static bool _act4BrutalRunsLoaded;

	private static int _act4BrutalRunsProfileId = -1;

	private static bool _act4EnteredNoVictoryLoaded;

	private static int _act4EnteredNoVictoryProfileId = -1;

	private static bool _act4BookChoicesLoaded;

	private static int _act4BookChoicesProfileId = -1;

	private static bool _runDamageContributionsLoaded;

	private static int _runDamageContributionsProfileId = -1;

	private static bool _runDamageContributionsDirty;

	// EN: Track the last act index at which we persisted damage contributions.
	//     We only write to disk at act boss transitions (end of act 1/2/3), not every auto-save.
	// ZH: 记录上次持久化伤害贡献时的幕索引，仅在幕Boss战结束时写入磁盘，而非每次自动存档。
	private static int _lastDamageContributionPersistActIndex = -1;

	// Weakest buff: set of "{runKey}|{netId}" strings for players who earned Str/Dex for the Architect fight
	private static readonly HashSet<string> Act4WeakestBuffGrantedKeys = new HashSet<string>();

	private static bool _act4WeakestBuffLoaded;

	// EN: Pending host→client join sync state. Set from ClientLoadJoinSyncPatch when the client receives a
	//     ClientLoadJoinResponseMessage that contains our extra Act4 state bytes. Consumed once by
	//     RestoreAct4FlagsFromSave so the brutal flag and weakest buff grants match the host.
	// ZH: 主机→客户端加入同步的待处理状态。当客户端收到含有Act4额外状态字节的
	//     ClientLoadJoinResponseMessage时由ClientLoadJoinSyncPatch设置，
	//     并被RestoreAct4FlagsFromSave消费一次，用于确保残暴模式标志、最弱增益赠予、
	//     以及偷书选择与主机一致。
	private static bool? _pendingJoinBrutalOverride;
	private static List<ulong>? _pendingJoinWeakestBuffIds;
	private static int? _pendingJoinBookChoiceBitmask;
	private static long _pendingJoinSyncRunStartTime;
	// EN: Per-player cumulative damage totals received from the host via join-sync. Keyed by NetId.
	// ZH: 通过加入同步从主机接收的各玩家累计伤害总量，以NetId为键。
	private static Dictionary<ulong, long>? _pendingJoinDamageContributions;

	private static int _act4WeakestBuffProfileId = -1;

	private static bool _act4PrefsLoaded;

	private static int _act4PrefsProfileId = -1;

	private static bool _isAdminButtonPressed;

	private static bool _dynamicTextLocalizationReady;

	private static readonly HashSet<string> AdminCombatCardGrantKeys = new HashSet<string>();

	// Safety-net flag: set synchronously at the start of ProceedToAct4Async (before any await)
	// so RunManagerEnterNextActPatch can force AppendAct4Placeholder if the async path hasn't
	// completed yet.  Fixes a multiplayer race where stale _readyPlayers causes MoveToNextAct
	// to fire before AppendAct4Placeholder runs on the client.
	internal static volatile bool Act4TransitionPending;

	// EN: All tunable Act 4 balance constants have been moved to Act4Config.cs.
	// ZH: 所有可调整的第四幕平衡常量已移至 Act4Config.cs。

	internal static bool AdminButtonUiEnabled => false;

	internal static void EnsureAct4DynamicTextLocalizationReady()
	{
		if (_dynamicTextLocalizationReady)
		{
			return;
		}
		// EN: We register one tiny runtime-only "{text}" key so ad-hoc popups can render arbitrary text
		//     without bloating localization files with throwaway entries. If this breaks, the mod still runs,
		//     but custom popups get a lot more brittle.
		// ZH: 这里动态注册一个 "{text}" 键，给临时弹窗复用。
		//     这样不用为了零散提示塞一堆一次性本地化条目。这里炸了模组未必会挂，但弹窗会脆很多。
		try
		{
			LocManager? locManager = LocManager.Instance;
			if (locManager == null)
			{
				return;
			}

			MethodInfo? getTable = locManager.GetType().GetMethod("GetTable", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
			object? eventsTable = getTable?.Invoke(locManager, new object[] { "events" });
			if (eventsTable == null)
			{
				return;
			}

			MethodInfo? mergeWith = eventsTable.GetType().GetMethod("MergeWith", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Dictionary<string, string>) }, null);
			if (mergeWith == null)
			{
				return;
			}

			mergeWith.Invoke(eventsTable, new object[]
			{
				new Dictionary<string, string>
				{
					["ACT4_DYNAMIC_TEXT"] = "{text}"
				}
			});

			_dynamicTextLocalizationReady = true;
		}
		catch (Exception ex)
		{
			Logger.Warn($"EnsureAct4DynamicTextLocalizationReady failed: {ex.Message}", 1);
		}
	}

	internal static bool IsAdminEnabled(Player? player)
	{
		return player != null && AdminPlayers.Contains(player.NetId);
	}

	internal static MegaSprite? TryGetCreatureSpineController(NCreature? creatureNode)
	{
		if (creatureNode == null)
		{
			return null;
		}

		try
		{
			object? visuals = NCreatureVisualsProperty?.GetValue(creatureNode);
			if (visuals == null)
			{
				return null;
			}

			PropertyInfo? spineBodyProperty = visuals.GetType().GetProperty("SpineBody", BindingFlags.Public | BindingFlags.Instance);
			return spineBodyProperty?.GetValue(visuals) as MegaSprite;
		}
		catch (Exception ex)
		{
			Logger.Warn($"TryGetCreatureSpineController failed: {ex.Message}", 1);
			return null;
		}
	}

	internal static Node2D? TryGetCreatureBodyNode(NCreature? creatureNode)
	{
		return TryGetCreatureSpineController(creatureNode)?.BoundObject as Node2D;
	}

	internal static void EnsureAdminButton(NMapScreen mapScreen)
	{
		// EN: Safe to call whenever the map screen wakes up.
		//     Creation is idempotent, host-only, and always followed by RefreshAdminUi so
		//     late joins, reloads, and rebuilt scenes all collapse back to one path.
		// ZH: 地图界面每次醒来都可以放心调这里。
		//     按钮创建是幂等的，只给房主生成，最后统一走 RefreshAdminUi 收口。
		RunState? _adminCheckState = GetActiveRunState();
		if (AdminButtonUiEnabled && GodotObject.IsInstanceValid(mapScreen) && (_adminCheckState == null || IsLocalPlayerHost(_adminCheckState)) && FindAdminButton(mapScreen) == null)
		{
			Logger.Info("Creating admin mode button on the map screen (host only)", 1);
			NButton val = CreateAdminButton();
			((Node)mapScreen).AddChild(val, false, Node.InternalMode.Disabled);
		}
		RefreshAdminUi(mapScreen);
	}

	internal static void RefreshAdminUi(NMapScreen? mapScreen)
	{
		if (mapScreen == null || !GodotObject.IsInstanceValid(mapScreen))
		{
			return;
		}
		// EN: This is the truth source for admin-map state.
		//     Host ownership, free travel, and button visuals all converge here so we do not end up
		//     with three slightly different ideas of whether admin mode is really on.
		// ZH: 这里是管理员地图状态的总开关。
		//     房主限制、自由移动和按钮视觉都在这里统一收口，避免出现几套不一致的“管理员模式已开”。
		NButton val = FindAdminButton(mapScreen);
		if (!AdminButtonUiEnabled)
		{
			if (val != null)
			{
				((Node)val).QueueFree();
			}
		}
		RunState activeRunState = GetActiveRunState();
		if (activeRunState == null)
		{
			return;
		}
		// Admin button is host-only - remove it from non-host clients
		if (val != null && !IsLocalPlayerHost(activeRunState))
		{
			((Node)val).QueueFree();
			val = null;
		}
		Player me = LocalContext.GetMe(activeRunState);
		if (me == null)
		{
			return;
		}
		bool flag = IsAdminEnabled(me);
		bool flag2 = flag; // free travel applies in all acts including Act 4
		mapScreen.SetDebugTravelEnabled(flag2);
		if (val != null)
		{
			UpdateAdminButtonVisuals(val, flag);
			((NClickableControl)val).Enable();
		}
	}

	internal static async Task EnableAdminModeAsync(Player player)
	{
		// EN: Intentionally overkill.
		//     Testers love toggling admin mode mid-run instead of from a clean boot, so this has to patch up
		//     both persistent player stats and the current combat state if one already exists.
		// ZH: 这里故意给得很夸张。
		//     测试时管理员模式经常是半路才开的，所以既要补玩家常驻属性，也要补当前战斗状态。
		if (AdminPlayers.Add(player.NetId))
		{
			Logger.Info($"Admin mode enabled for player {player.NetId}", 1);
			player.MaxEnergy = Math.Max(player.MaxEnergy, 6);
			player.Creature.SetMaxHpInternal((decimal)(player.Creature.MaxHp + 50));
			player.Creature.SetCurrentHpInternal((decimal)(player.Creature.CurrentHp + 50));
			if (player.Creature.CombatState != null)
			{
				if (player.PlayerCombatState != null)
				{
					player.PlayerCombatState.GainEnergy((decimal)Math.Max(0, player.PlayerCombatState.MaxEnergy - player.PlayerCombatState.Energy));
				}
				await PowerCmd.Apply<StrengthPower>(player.Creature, 90m, player.Creature, (CardModel)null, false);
				await PowerCmd.Apply<VigorPower>(player.Creature, 50m, player.Creature, (CardModel)null, false);
				await PowerCmd.Apply<DexterityPower>(player.Creature, 20m, player.Creature, (CardModel)null, false);
				await PowerCmd.Apply<EnvenomPower>(player.Creature, 20m, player.Creature, (CardModel)null, false);
				await GrantAdminTestCardsAsync(player);
			}
			if (LocalContext.IsMe(player))
			{
				RefreshAdminUi(NMapScreen.Instance);
				NGame instance = NGame.Instance;
				if (instance != null)
				{
					GodotTreeExtensions.AddChildSafely(instance, NFullscreenTextVfx.Create("ADMIN MODE ENABLED"));
				}
				IRunState runState = player.RunState;
			}
			return;
		}
		if (LocalContext.IsMe(player))
		{
			RefreshAdminUi(NMapScreen.Instance);
		}
	}

	// Applies admin stat buffs that must be synced across all machines.
	// Called inside AdminSkipToAct3Action.ExecuteAction() so both host and client
	// execute identical state mutations before the checksum is computed.
	internal static void ApplyAdminStatBuffsOutOfCombat(Player player)
	{
		AdminPlayers.Add(player.NetId);
		player.MaxEnergy = Math.Max(player.MaxEnergy, 6);
		player.Creature.SetMaxHpInternal((decimal)(player.Creature.MaxHp + 50));
		player.Creature.SetCurrentHpInternal((decimal)(player.Creature.CurrentHp + 50));
		// Show UI feedback on whichever machine owns this player
		if (LocalContext.IsMe(player))
		{
			RefreshAdminUi(NMapScreen.Instance);
			NGame instance = NGame.Instance;
			if (instance != null)
				GodotTreeExtensions.AddChildSafely(instance, NFullscreenTextVfx.Create("ADMIN MODE ENABLED"));
		}
	}

	internal static void DisableAdminMode(Player player)
	{
		if (!AdminPlayers.Remove(player.NetId))
		{
			if (LocalContext.IsMe(player))
			{
				RefreshAdminUi(NMapScreen.Instance);
			}
			return;
		}
		Logger.Info($"Admin mode disabled for player {player.NetId}", 1);
		if (LocalContext.IsMe(player))
		{
			RefreshAdminUi(NMapScreen.Instance);
			NGame instance = NGame.Instance;
			if (instance != null)
			{
				GodotTreeExtensions.AddChildSafely(instance, NFullscreenTextVfx.Create("ADMIN MODE DISABLED"));
			}
		}
	}

	internal static Task ApplyAdminCombatBonusAsync(Creature creature)
	{
		if (!creature.IsPlayer || creature.Player == null)
		{
			return Task.CompletedTask;
		}
		if (!IsAdminEnabled(creature.Player))
		{
			return Task.CompletedTask;
		}
		creature.Player.MaxEnergy = Math.Max(creature.Player.MaxEnergy, 6);
		return ApplyAdminCombatBuffsAsync(creature);
	}

	private static async Task ApplyAdminCombatBuffsAsync(Creature creature)
	{
		await PowerCmd.Apply<StrengthPower>(creature, 90m, creature, (CardModel)null, false);
		await PowerCmd.Apply<VigorPower>(creature, 50m, creature, (CardModel)null, false);
		await PowerCmd.Apply<DexterityPower>(creature, 20m, creature, (CardModel)null, false);
		await PowerCmd.Apply<EnvenomPower>(creature, 20m, creature, (CardModel)null, false);
		await GrantAdminTestCardsAsync(creature.Player);
	}

	private static async Task GrantAdminTestCardsAsync(Player player)
	{
		if (player?.Creature?.CombatState == null)
		{
			return;
		}
		string item = $"{player.NetId}:{player.Creature.CombatState.GetHashCode()}";
		if (!AdminCombatCardGrantKeys.Add(item))
		{
			return;
		}
		List<CardModel> list = new List<CardModel>
		{
			player.Creature.CombatState.CreateCard<BlightStrike>(player),
			player.Creature.CombatState.CreateCard<BlightStrike>(player)
		};
		IReadOnlyList<CardPileAddResult> readOnlyList = await CardPileCmd.AddGeneratedCardsToCombat((IEnumerable<CardModel>)list, PileType.Hand, addedByPlayer: true);
		if (readOnlyList.Count == 0)
		{
			List<CardModel> list2 = new List<CardModel>
			{
				player.Creature.CombatState.CreateCard<BlightStrike>(player),
				player.Creature.CombatState.CreateCard<BlightStrike>(player)
			};
			await CardPileCmd.AddGeneratedCardsToCombat((IEnumerable<CardModel>)list2, PileType.Draw, addedByPlayer: true, CardPilePosition.Random);
		}
	}

	internal static bool IsAct4Placeholder(RunState? runState)
	{
		return runState != null && ((IReadOnlyCollection<ActModel>)runState.Acts).Count > 3 && runState.CurrentActIndex == 3;
	}

	internal static bool IsAct4ArchitectConfigured(RunState? runState, int actIndex = 3)
	{
		if (runState == null || actIndex < 0 || ((IReadOnlyCollection<ActModel>)runState.Acts).Count <= actIndex)
		{
			return false;
		}
		return runState.Acts[actIndex]?.BossEncounter?.Id.Entry == ArchitectBossEncounterId;
	}

	internal static void EnsureAct4ArchitectBossConfigured(RunState? runState, int actIndex = 3)
	{
		if (runState == null || actIndex < 0 || ((IReadOnlyCollection<ActModel>)runState.Acts).Count <= actIndex)
		{
			return;
		}
		ActModel val = runState.Acts[actIndex];
		if (val == null || val.BossEncounter?.Id.Entry == ArchitectBossEncounterId)
		{
			return;
		}
		Logger.Warn($"Reasserting Architect boss encounter on act index {actIndex}. Existing={val.BossEncounter?.Id.Entry ?? "<null>"}", 1);
		val.SetSecondBossEncounter((EncounterModel)null);
		val.SetBossEncounter(ModelDb.Encounter<Act4ArchitectBossEncounter>());
	}

	internal static void MarkAct4BossVictory(RunState? runState)
	{
		if (runState != null)
		{
			Act4BossVictories.Add(runState);
			SerializableRun serializableRun = RunManager.Instance?.ToSave((AbstractRoom)null);
			if (serializableRun != null)
			{
				RecordAct4BossVictory(serializableRun);
			}
		}
	}

	internal static void ClearAct4BossVictory(RunState? runState)
	{
		if (runState != null)
		{
			Act4BossVictories.Remove(runState);
		}
	}

	internal static bool WasAct4BossActuallyDefeated(RunState? runState)
	{
		return runState != null && Act4BossVictories.Contains(runState);
	}

	internal static bool WasAct4BossActuallyDefeated(RunHistory? history)
	{
		if (history == null || history.Acts == null || history.Acts.Count <= 3)
		{
			return false;
		}
		EnsureAct4BossVictoriesLoaded();
		string key = BuildAct3SnapshotKey(history.StartTime, history.Seed, history.Ascension, history.Players?.Count ?? 0);
		return Act4BossVictoryKeys.Contains(key);
	}

	// Records that a run entered Act 4 but did NOT defeat the Architect.
	// Called in RunManagerOnEndedPatch when we set isVictory=true for an Act4 loss.
	internal static void RecordAct4EnteredWithoutVictory(RunState? runState)
	{
		if (runState == null) return;
		try
		{
			SerializableRun? run = RunManager.Instance?.ToSave((AbstractRoom)null);
			if (run == null) return;
			EnsureAct4EnteredWithoutVictoryLoaded();
			string key = BuildAct3SnapshotKey(run.StartTime, run.SerializableRng?.Seed ?? string.Empty, run.Ascension, run.Players?.Count ?? 0);
			if (!string.IsNullOrWhiteSpace(key) && Act4EnteredWithoutVictoryKeys.Add(key))
			{
				PersistAct4EnteredWithoutVictory();
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed recording Act 4 entered-without-victory: {ex.Message}", 1);
		}
	}

	internal static bool WasAct4EnteredWithoutVictory(RunHistory? history)
	{
		if (history == null || history.Acts == null || history.Acts.Count <= 3)
			return false;
		try
		{
			EnsureAct4EnteredWithoutVictoryLoaded();
			string key = BuildAct3SnapshotKey(history.StartTime, history.Seed, history.Ascension, history.Players?.Count ?? 0);
			return !string.IsNullOrWhiteSpace(key) && Act4EnteredWithoutVictoryKeys.Contains(key);
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed checking Act 4 entered-without-victory: {ex.Message}", 1);
			return false;
		}
	}

	internal static bool IsAct4BrutalRunFromHistory(RunHistory? history)
	{
		if (history == null) return false;
		try
		{
			EnsureAct4BrutalRunsLoaded();
			string key = BuildAct3SnapshotKey(history.StartTime, history.Seed, history.Ascension, history.Players?.Count ?? 0);
			return !string.IsNullOrWhiteSpace(key) && Act4BrutalRunKeys.Contains(key);
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed resolving Act 4 brutal flag from run history: {ex.Message}", 1);
			return false;
		}
	}

	internal static bool ShouldTreatCurrentAct4BossRoomAsVictory(RunState? runState)
	{
		if (!IsAct4Placeholder(runState))
		{
			return false;
		}
		AbstractRoom currentRoom = runState.CurrentRoom;
		CombatRoom combatRoom = currentRoom as CombatRoom;
		if (combatRoom == null || combatRoom.Encounter is not Act4ArchitectBossEncounter)
		{
			return false;
		}
		CombatState combatState = combatRoom.CombatState;
		if (combatState == null)
		{
			return false;
		}
		return !combatState.Enemies.Any((Creature enemy) => enemy.IsAlive);
	}

	internal static int GetAct4ProgressionBonus(RunState? runState, bool won)
	{
		if (!IsAct4Placeholder(runState))
		{
			return 0;
		}
		return won ? 300 : 100;
	}

	internal static int GetAct4ProgressionBonus(SerializableRun? run, bool won)
	{
		if (run == null || run.Acts == null || run.Acts.Count <= 3 || run.CurrentActIndex != 3)
		{
			return 0;
		}
		return won ? 300 : 100;
	}

	internal static void ApplyAct4SaveMarkers(SerializableRun? run, RunState? runState)
	{
		if (run == null)
		{
			return;
		}
		TrackBrutalAct4Run(run, IsBrutalAct4(runState));
		TrackBookChoices(run);
		// EN: Only persist damage contributions when the act index changes (boss kill transitions).
		//     This avoids excessive disk writes on every auto-save while ensuring data survives act transitions.
		// ZH: 仅在幕索引变化时（Boss战转换）持久化伤害贡献，避免每次自动存档都写入磁盘。
		int currentActIndex = runState?.CurrentActIndex ?? run.CurrentActIndex;
		if (currentActIndex != _lastDamageContributionPersistActIndex)
		{
			TrackRunDamageContributions(run);
			_lastDamageContributionPersistActIndex = currentActIndex;
		}
		PersistWeakestBuffGrantsIfDirty();
	}

	internal static void RestoreAct4FlagsFromSave(SerializableRun? run, RunState? runState)
	{
		if (run == null || runState == null)
		{
			return;
		}
		// EN: If this client received brutal/weakest-buff sync from the host via ClientLoadJoinResponseMessage,
		//     apply it instead of reading our local disk files (which clients don't have).
		// ZH: 若此客户端通过ClientLoadJoinResponseMessage收到来自主机的残暴/最弱增益同步，
		//     则使用该同步数据而非读取本地磁盘文件（客户端不拥有这些文件）。
		bool isBrutal;
		string restoreSource;
		int weakestSyncCount = 0;
		int damageSyncCount = 0;
		int? pendingBookChoiceBitmask = null;
		if (_pendingJoinBrutalOverride.HasValue && _pendingJoinSyncRunStartTime == run.StartTime)
		{
			isBrutal = _pendingJoinBrutalOverride.Value;
			restoreSource = "join-sync";
			pendingBookChoiceBitmask = _pendingJoinBookChoiceBitmask;
			if (_pendingJoinWeakestBuffIds != null)
			{
				weakestSyncCount = _pendingJoinWeakestBuffIds.Count;
				EnsureWeakestBuffGrantsLoaded();
				string runKey = BuildAct3SnapshotKey(run.StartTime, run.SerializableRng?.Seed ?? string.Empty, run.Ascension, run.Players?.Count ?? 0);
				if (!string.IsNullOrWhiteSpace(runKey))
				{
					lock (RunDamageContributionLock)
					{
						foreach (ulong playerId in _pendingJoinWeakestBuffIds)
							Act4WeakestBuffGrantedKeys.Add($"{runKey}|{playerId}");
					}
				}
			}
			// EN: If the host sent damage contribution totals, merge them into local tracking.
			//     This seeds the client's damage data so IsOwnerWeakestRunDamageContributor works
			//     correctly after a save&quit rejoin (otherwise damage starts from zero).
			// ZH: 若主机发送了伤害贡献总量，将其合并到本地追踪中，使客户端重连后
			//     IsOwnerWeakestRunDamageContributor能正确工作（否则伤害从零开始）。
			if (_pendingJoinDamageContributions != null && _pendingJoinDamageContributions.Count > 0)
			{
				EnsureRunDamageContributionsLoaded();
				string dmgRunKey = BuildAct3SnapshotKey(run.StartTime, run.SerializableRng?.Seed ?? string.Empty, run.Ascension, run.Players?.Count ?? 0);
				if (!string.IsNullOrWhiteSpace(dmgRunKey))
				{
					lock (RunDamageContributionLock)
					{
						// EN: Replace (not merge) the client's damage data with host's authoritative snapshot.
						// ZH: 用主机权威快照替换（而非合并）客户端的伤害数据。
						RunDamageContributionByRunKey[dmgRunKey] = new Dictionary<ulong, long>(_pendingJoinDamageContributions);
						if (!RunDamageContributionRunKeyOrder.Contains(dmgRunKey))
							RunDamageContributionRunKeyOrder.Add(dmgRunKey);
						damageSyncCount = _pendingJoinDamageContributions.Count;
					}
				}
			}
			_pendingJoinBrutalOverride = null;
			_pendingJoinWeakestBuffIds = null;
			_pendingJoinBookChoiceBitmask = null;
			_pendingJoinSyncRunStartTime = 0;
			_pendingJoinDamageContributions = null;
		}
		else
		{
			isBrutal = IsTrackedBrutalAct4Run(run);
			restoreSource = "local-store";
		}
		SetAct4Brutal(runState, isBrutal);
		string bookChoiceSource;
		int bookChoiceBitmask;
		if (pendingBookChoiceBitmask.HasValue)
		{
			bookChoiceSource = "join-sync";
			bookChoiceBitmask = pendingBookChoiceBitmask.Value;
			ApplyBookChoiceBitmask(bookChoiceBitmask);
			EnsureAct4BookChoicesLoaded();
			string runKey = BuildAct3SnapshotKey(run.StartTime, run.SerializableRng?.Seed ?? string.Empty, run.Ascension, run.Players?.Count ?? 0);
			if (!string.IsNullOrWhiteSpace(runKey))
			{
				UpsertBookChoice(runKey, bookChoiceBitmask, trimToLimit: false);
			}
		}
		else
		{
			bookChoiceSource = "local-store";
			RestoreBookChoicesFromSave(run);
			bookChoiceBitmask = GetBookChoiceBitmaskForRun(run);
		}
		Logger.Info($"RestoreAct4FlagsFromSave: startTime={run.StartTime} source={restoreSource} brutal={isBrutal} weakestCount={weakestSyncCount} damagePlayers={damageSyncCount} bookSource={bookChoiceSource} bookBitmask={bookChoiceBitmask}", 1);
		EnsureRunDamageContributionsLoaded();
		EnsureWeakestBuffGrantsLoaded();
		// Update the mod log header with current run info now that flags are restored.
		Act4Logger.UpdateRunInfo();
	}

	/// <summary>
	/// EN: Called by ClientLoadJoinSyncPatch (Deserialize Postfix) on clients when they receive
	///     the host's Act4 state bytes appended to ClientLoadJoinResponseMessage.
	/// ZH: 当客户端在ClientLoadJoinResponseMessage中收到主机附加的Act4状态字节时，
	///     由ClientLoadJoinSyncPatch（Deserialize Postfix）调用。
	/// </summary>
	internal static void SetPendingJoinSyncState(long runStartTime, bool isBrutal, List<ulong> weakestBuffPlayerIds, int? bookChoiceBitmask = null, Dictionary<ulong, long>? damageContributions = null)
	{
		_pendingJoinBrutalOverride = isBrutal;
		_pendingJoinWeakestBuffIds = weakestBuffPlayerIds;
		_pendingJoinBookChoiceBitmask = bookChoiceBitmask;
		_pendingJoinSyncRunStartTime = runStartTime;
		// EN: Store host-authoritative damage contributions so the client can seed its local tracking
		//     on rejoin instead of starting from zero.
		// ZH: 存储主机权威伤害贡献数据，使客户端在重连时可以直接使用而非从零开始。
		_pendingJoinDamageContributions = damageContributions;
		Logger.Info($"SetPendingJoinSyncState: startTime={runStartTime} brutal={isBrutal} weakestCount={weakestBuffPlayerIds?.Count ?? 0} bookBitmask={(bookChoiceBitmask.HasValue ? bookChoiceBitmask.Value.ToString() : "none")} damagePlayerCount={damageContributions?.Count ?? 0}", 1);
	}

	/// <summary>
	/// EN: Reads the brutal flag for the given run from the host's local disk (used by Serialize Postfix to
	///     embed the flag for clients). Falls back gracefully if data is unavailable.
	/// ZH: 从主机本地磁盘读取指定运行的残暴标志（供Serialize Postfix嵌入给客户端使用）。不可用时优雅降级。
	/// </summary>
	internal static bool GetBrutalFlagForRun(SerializableRun run)
	{
		try { return IsTrackedBrutalAct4Run(run); }
		catch { return false; }
	}

	internal static int GetBookChoiceBitmaskForRun(SerializableRun run)
	{
		try
		{
			EnsureAct4BookChoicesLoaded();
			string key = BuildAct3SnapshotKey(run.StartTime, run.SerializableRng?.Seed ?? string.Empty, run.Ascension, run.Players?.Count ?? 0);
			if (string.IsNullOrWhiteSpace(key))
			{
				return 0;
			}
			return BookChoicesByRunKey.TryGetValue(key, out int bitmask) ? bitmask : 0;
		}
		catch
		{
			return 0;
		}
	}

	/// <summary>
	/// EN: Returns the NetIds of all players with weakest-buff entitlement for the given run.
	///     Used by Serialize Postfix to embed the list for clients.
	/// ZH: 返回指定运行中所有拥有最弱增益权利的玩家NetID列表。供Serialize Postfix嵌入给客户端使用。
	/// </summary>
	internal static List<ulong> GetWeakestBuffPlayerIdsForRun(SerializableRun run)
	{
		try
		{
			EnsureWeakestBuffGrantsLoaded();
			string prefix = BuildAct3SnapshotKey(run.StartTime, run.SerializableRng?.Seed ?? string.Empty, run.Ascension, run.Players?.Count ?? 0) + "|";
			if (string.IsNullOrWhiteSpace(prefix)) return new List<ulong>();
			lock (RunDamageContributionLock)
			{
				var result = new List<ulong>();
				foreach (string key in Act4WeakestBuffGrantedKeys)
				{
					if (key.StartsWith(prefix, StringComparison.Ordinal)
						&& ulong.TryParse(key[prefix.Length..], out ulong playerId))
						result.Add(playerId);
				}
				return result;
			}
		}
		catch { return new List<ulong>(); }
	}

	/// <summary>
	/// EN: Returns a snapshot of per-player damage contributions for the given run.
	///     Used by Serialize Postfix to embed damage totals for clients during join-sync.
	/// ZH: 返回指定运行中每位玩家的伤害贡献快照。供Serialize Postfix在加入同步时嵌入给客户端。
	/// </summary>
	internal static Dictionary<ulong, long> GetDamageContributionsForRun(SerializableRun run)
	{
		try
		{
			EnsureRunDamageContributionsLoaded();
			string key = BuildAct3SnapshotKey(run.StartTime, run.SerializableRng?.Seed ?? string.Empty, run.Ascension, run.Players?.Count ?? 0);
			if (string.IsNullOrWhiteSpace(key)) return new Dictionary<ulong, long>();
			lock (RunDamageContributionLock)
			{
				if (RunDamageContributionByRunKey.TryGetValue(key, out Dictionary<ulong, long>? contributions) && contributions != null)
				{
					return new Dictionary<ulong, long>(contributions);
				}
			}
			return new Dictionary<ulong, long>();
		}
		catch { return new Dictionary<ulong, long>(); }
	}

	internal static void SaveAct3SnapshotFromRunState(RunState? runState)
	{
		if (runState == null)
		{
			return;
		}
		try
		{
			RunManager instance = RunManager.Instance;
			if (instance == null)
			{
				return;
			}
			SerializableRun serializableRun = instance.ToSave((AbstractRoom)null);
			ApplyAct4SaveMarkers(serializableRun, runState);
			RunHistory runHistory = CreateRunHistoryFromSerializable(serializableRun, victory: true, isAbandoned: false);
			if (runHistory.Acts == null || runHistory.Acts.Count == 0)
			{
				return;
			}
			EnsureAct3SnapshotsLoaded();
			string text = BuildAct3SnapshotKey(runHistory.StartTime, runHistory.Seed, runHistory.Ascension, runHistory.Players?.Count ?? 0);
			Act3SnapshotsByKey[text] = runHistory;
			PersistAct3Snapshots();
			Logger.Info($"Saved Act 3 snapshot for key {text}", 1);
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed saving Act 3 snapshot: {ex.Message}", 1);
		}
	}

	internal static bool TryGetAct3Snapshot(RunHistory? history, out RunHistory snapshot)
	{
		snapshot = null;
		if (history == null || history.Acts == null || history.Acts.Count <= 3)
		{
			return false;
		}
		EnsureAct3SnapshotsLoaded();
		string key = BuildAct3SnapshotKey(history.StartTime, history.Seed, history.Ascension, history.Players?.Count ?? 0);
		if (Act3SnapshotsByKey.TryGetValue(key, out snapshot) && snapshot != null)
		{
			return true;
		}
		int playerCount = history.Players?.Count ?? 0;
		RunHistory runHistory = Act3SnapshotsByKey.Values.FirstOrDefault((RunHistory s) => s != null && s.StartTime == history.StartTime && s.Ascension == history.Ascension && (s.Players?.Count ?? 0) == playerCount && s.Acts != null && s.Acts.Count <= 3);
		if (runHistory != null)
		{
			snapshot = runHistory;
			return true;
		}
		runHistory = Act3SnapshotsByKey.Values.FirstOrDefault((RunHistory s) => s != null && s.StartTime == history.StartTime && s.Acts != null && s.Acts.Count <= 3);
		if (runHistory != null)
		{
			snapshot = runHistory;
			return true;
		}
		return false;
	}

	private static RunHistory CreateRunHistoryFromSerializable(SerializableRun run, bool victory, bool isAbandoned)
	{
		List<RunHistoryPlayer> list = new List<RunHistoryPlayer>();
		foreach (SerializablePlayer player in run.Players ?? new List<SerializablePlayer>())
		{
			RunHistoryPlayer item = new RunHistoryPlayer
			{
				Id = player.NetId,
				Character = player.CharacterId ?? ModelId.none,
				Deck = player.Deck?.ToList() ?? new List<SerializableCard>(),
				Relics = player.Relics?.ToList() ?? new List<SerializableRelic>(),
				Potions = player.Potions?.ToList() ?? new List<SerializablePotion>(),
				MaxPotionSlotCount = player.MaxPotionSlotCount
			};
			list.Add(item);
		}
		List<List<MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry>> list2 = run.MapPointHistory?.Select((List<MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry> entries) => entries.ToList()).ToList() ?? new List<List<MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry>>();
		return new RunHistory
		{
			BuildId = "ACT4_PLACEHOLDER_ACT3_SNAPSHOT",
			PlatformType = run.PlatformType,
			Players = list,
			GameMode = (run.DailyTime.HasValue ? GameMode.Daily : GameMode.Standard),
			Win = victory,
			KilledByEncounter = ModelId.none,
			KilledByEvent = ModelId.none,
			WasAbandoned = isAbandoned,
			Seed = (run.SerializableRng?.Seed ?? string.Empty),
			StartTime = run.StartTime,
			RunTime = ((run.WinTime > 0) ? run.WinTime : run.RunTime),
			MapPointHistory = list2,
			Ascension = run.Ascension,
			Acts = run.Acts?.Select((SerializableActModel a) => a.Id).ToList() ?? new List<ModelId>(),
			Modifiers = run.Modifiers?.ToList() ?? new List<SerializableModifier>()
		};
	}

	private static string BuildAct3SnapshotKey(long startTime, string seed, int ascension, int playerCount)
	{
		return $"{startTime}|{seed ?? string.Empty}|{ascension}|{playerCount}";
	}

	private static void RecordAct4BossVictory(SerializableRun run)
	{
		try
		{
			EnsureAct4BossVictoriesLoaded();
			string key = BuildAct3SnapshotKey(run.StartTime, run.SerializableRng?.Seed ?? string.Empty, run.Ascension, run.Players?.Count ?? 0);
			if (!string.IsNullOrWhiteSpace(key) && Act4BossVictoryKeys.Add(key))
			{
				PersistAct4BossVictories();
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed recording Act 4 boss victory: {ex.Message}", 1);
		}
	}

	private static void EnsureAct3SnapshotsLoaded()
	{
		SaveManager instance = SaveManager.Instance;
		if (instance == null)
		{
			return;
		}
		int currentProfileId = instance.CurrentProfileId;
		if (_act3SnapshotsLoaded && _act3SnapshotsProfileId == currentProfileId)
		{
			return;
		}
		_act3SnapshotsLoaded = true;
		_act3SnapshotsProfileId = currentProfileId;
		Act3SnapshotsByKey.Clear();
		try
		{
			string profileScopedPath = ResolveProfileScopedPath(instance, Act3SnapshotPath);
			if (!File.Exists(profileScopedPath))
			{
				return;
			}
			ReadSaveResult<Act3SnapshotStore> readSaveResult = SaveManager.FromJson<Act3SnapshotStore>(File.ReadAllText(profileScopedPath));
			if (!readSaveResult.Success || readSaveResult.SaveData?.Entries == null)
			{
				return;
			}
			foreach (Act3SnapshotEntry entry in readSaveResult.SaveData.Entries)
			{
				if (!string.IsNullOrEmpty(entry?.Key) && entry.Snapshot != null)
				{
					Act3SnapshotsByKey[entry.Key] = entry.Snapshot;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed loading Act 3 snapshots: {ex.Message}", 1);
		}
	}

	private static void EnsureAct4BossVictoriesLoaded()
	{
		SaveManager instance = SaveManager.Instance;
		if (instance == null)
		{
			return;
		}
		int currentProfileId = instance.CurrentProfileId;
		if (_act4BossVictoriesLoaded && _act4BossVictoriesProfileId == currentProfileId)
		{
			return;
		}
		_act4BossVictoriesLoaded = true;
		_act4BossVictoriesProfileId = currentProfileId;
		Act4BossVictoryKeys.Clear();
		try
		{
			string profileScopedPath = ResolveProfileScopedPath(instance, Act4BossVictoryPath);
			if (!File.Exists(profileScopedPath))
			{
				return;
			}
			Act4BossVictoryStore? readStore = Act4StoreFromJson<Act4BossVictoryStore>(File.ReadAllText(profileScopedPath));
			if (readStore?.Keys == null)
			{
				return;
			}
			foreach (string key in readStore.Keys)
			{
				if (!string.IsNullOrWhiteSpace(key))
				{
					Act4BossVictoryKeys.Add(key);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed loading Act 4 boss victories: {ex.Message}", 1);
		}
	}

	private static void EnsureAct4BrutalRunsLoaded()
	{
		SaveManager instance = SaveManager.Instance;
		if (instance == null)
		{
			return;
		}
		int currentProfileId = instance.CurrentProfileId;
		if (_act4BrutalRunsLoaded && _act4BrutalRunsProfileId == currentProfileId)
		{
			return;
		}
		_act4BrutalRunsLoaded = true;
		_act4BrutalRunsProfileId = currentProfileId;
		Act4BrutalRunKeys.Clear();
		try
		{
			string profileScopedPath = ResolveProfileScopedPath(instance, Act4BrutalRunsPath);
			if (!File.Exists(profileScopedPath))
			{
				return;
			}
			Act4BrutalRunStore? readBrutalStore = Act4StoreFromJson<Act4BrutalRunStore>(File.ReadAllText(profileScopedPath));
			if (readBrutalStore?.Keys == null)
			{
				return;
			}
			foreach (string key in readBrutalStore.Keys)
			{
				if (!string.IsNullOrWhiteSpace(key))
				{
					Act4BrutalRunKeys.Add(key);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed loading Act 4 brutal flags: {ex.Message}", 1);
		}
	}

	private static void EnsureAct4EnteredWithoutVictoryLoaded()
	{
		SaveManager instance = SaveManager.Instance;
		if (instance == null) return;
		int currentProfileId = instance.CurrentProfileId;
		if (_act4EnteredNoVictoryLoaded && _act4EnteredNoVictoryProfileId == currentProfileId) return;
		_act4EnteredNoVictoryLoaded = true;
		_act4EnteredNoVictoryProfileId = currentProfileId;
		Act4EnteredWithoutVictoryKeys.Clear();
		try
		{
			string profileScopedPath = ResolveProfileScopedPath(instance, Act4EnteredWithoutVictoryPath);
			if (!File.Exists(profileScopedPath)) return;
			Act4BossVictoryStore? store = Act4StoreFromJson<Act4BossVictoryStore>(File.ReadAllText(profileScopedPath));
			if (store?.Keys == null) return;
			foreach (string key in store.Keys)
			{
				if (!string.IsNullOrWhiteSpace(key))
					Act4EnteredWithoutVictoryKeys.Add(key);
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed loading Act 4 entered-without-victory keys: {ex.Message}", 1);
		}
	}

	private static void PersistAct4EnteredWithoutVictory()
	{
		SaveManager instance = SaveManager.Instance;
		if (instance == null) return;
		try
		{
			Act4BossVictoryStore store = new Act4BossVictoryStore
			{
				SchemaVersion = 1,
				Keys = Act4EnteredWithoutVictoryKeys.OrderBy(k => k, StringComparer.Ordinal).ToList()
			};
			string profileScopedPath = ResolveProfileScopedPath(instance, Act4EnteredWithoutVictoryPath);
			string? directoryName = Path.GetDirectoryName(profileScopedPath);
			if (!string.IsNullOrEmpty(directoryName))
				Directory.CreateDirectory(directoryName);
			File.WriteAllText(profileScopedPath, Act4StoreToJson(store));
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed persisting Act 4 entered-without-victory keys: {ex.Message}", 1);
		}
	}

	private static void PersistAct3Snapshots()
	{
		SaveManager instance = SaveManager.Instance;
		if (instance == null)
		{
			return;
		}
		try
		{
			Act3SnapshotStore act3SnapshotStore = new Act3SnapshotStore
			{
				SchemaVersion = 1,
				Entries = Act3SnapshotsByKey.Select((KeyValuePair<string, RunHistory> kvp) => new Act3SnapshotEntry
				{
					Key = kvp.Key,
					Snapshot = kvp.Value
				}).ToList()
			};
			string profileScopedPath = ResolveProfileScopedPath(instance, Act3SnapshotPath);
			string directoryName = Path.GetDirectoryName(profileScopedPath);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.WriteAllText(profileScopedPath, SaveManager.ToJson(act3SnapshotStore));
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed persisting Act 3 snapshots: {ex.Message}", 1);
		}
	}

	private static void PersistAct4BossVictories()
	{
		SaveManager instance = SaveManager.Instance;
		if (instance == null)
		{
			return;
		}
		try
		{
			Act4BossVictoryStore act4BossVictoryStore = new Act4BossVictoryStore
			{
				SchemaVersion = 1,
				Keys = Act4BossVictoryKeys.OrderBy(k => k, StringComparer.Ordinal).ToList()
			};
			string profileScopedPath = ResolveProfileScopedPath(instance, Act4BossVictoryPath);
			string directoryName = Path.GetDirectoryName(profileScopedPath);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.WriteAllText(profileScopedPath, Act4StoreToJson(act4BossVictoryStore));
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed persisting Act 4 boss victories: {ex.Message}", 1);
		}
	}

	private static void PersistAct4BrutalRuns()
	{
		SaveManager instance = SaveManager.Instance;
		if (instance == null)
		{
			return;
		}
		try
		{
			Act4BrutalRunStore act4BrutalRunStore = new Act4BrutalRunStore
			{
				SchemaVersion = 1,
				Keys = Act4BrutalRunKeys.ToList()
			};
			string profileScopedPath = ResolveProfileScopedPath(instance, Act4BrutalRunsPath);
			string directoryName = Path.GetDirectoryName(profileScopedPath);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.WriteAllText(profileScopedPath, Act4StoreToJson(act4BrutalRunStore));
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed persisting Act 4 brutal flags: {ex.Message}", 1);
		}
	}

	private static void TrackBrutalAct4Run(SerializableRun run, bool isBrutal)
	{
		try
		{
			EnsureAct4BrutalRunsLoaded();
			string key = BuildAct3SnapshotKey(run.StartTime, run.SerializableRng?.Seed ?? string.Empty, run.Ascension, run.Players?.Count ?? 0);
			if (string.IsNullOrWhiteSpace(key))
			{
				return;
			}
			bool changed = isBrutal ? Act4BrutalRunKeys.Add(key) : Act4BrutalRunKeys.Remove(key);
			if (changed)
			{
				PersistAct4BrutalRuns();
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed tracking Act 4 brutal flag: {ex.Message}", 1);
		}
	}

	private static bool IsTrackedBrutalAct4Run(SerializableRun run)
	{
		try
		{
			EnsureAct4BrutalRunsLoaded();
			string key = BuildAct3SnapshotKey(run.StartTime, run.SerializableRng?.Seed ?? string.Empty, run.Ascension, run.Players?.Count ?? 0);
			return !string.IsNullOrWhiteSpace(key) && Act4BrutalRunKeys.Contains(key);
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed resolving Act 4 brutal flag: {ex.Message}", 1);
			return false;
		}
	}

	// ── Book choice persistence ──────────────────────────────────────────────
	private static void EnsureAct4BookChoicesLoaded()
	{
		SaveManager instance = SaveManager.Instance;
		if (instance == null) return;
		int currentProfileId = instance.CurrentProfileId;
		if (_act4BookChoicesLoaded && _act4BookChoicesProfileId == currentProfileId) return;
		_act4BookChoicesLoaded = true;
		_act4BookChoicesProfileId = currentProfileId;
		BookChoicesByRunKey.Clear();
		BookChoiceRunKeyOrder.Clear();
		try
		{
			string profileScopedPath = ResolveProfileScopedPath(instance, Act4BookChoicesPath);
			if (!File.Exists(profileScopedPath)) return;
			if (TryDeserializeAct4BookChoiceStore(File.ReadAllText(profileScopedPath), out Act4BookChoiceStore? store)
				&& store != null)
			{
				if (store.Entries.Count > 0)
				{
					foreach (Act4BookChoiceEntry entry in store.Entries)
					{
						if (!string.IsNullOrWhiteSpace(entry.Key))
						{
							UpsertBookChoice(entry.Key, entry.Bitmask, trimToLimit: false);
						}
					}
				}
				else
				{
					foreach (KeyValuePair<string, int> kv in store.Choices)
					{
						UpsertBookChoice(kv.Key, kv.Value, trimToLimit: false);
					}
				}
				TrimBookChoicesToLimit();
			}
		}
		catch (Exception ex) { Logger.Warn($"Failed loading Act 4 book choices: {ex.Message}", 1); }
	}

	private static void PersistBookChoices()
	{
		try
		{
			SaveManager instance = SaveManager.Instance;
			if (instance == null) return;
			TrimBookChoicesToLimit();
			Act4BookChoiceStore store = new Act4BookChoiceStore();
			foreach (string key in BookChoiceRunKeyOrder)
			{
				if (!BookChoicesByRunKey.TryGetValue(key, out int bitmask))
				{
					continue;
				}
				store.Entries.Add(new Act4BookChoiceEntry { Key = key, Bitmask = bitmask });
				store.Choices[key] = bitmask;
			}
			string profileScopedPath = ResolveProfileScopedPath(instance, Act4BookChoicesPath);
			TryWriteTextFile(profileScopedPath, SerializeAct4BookChoiceStore(store), "Act 4 book choices");
		}
		catch (Exception ex) { Logger.Warn($"Failed persisting Act 4 book choices: {ex.Message}", 1); }
	}

	private static void TrackBookChoices(SerializableRun run)
	{
		try
		{
			int bitmask = (Act4Settings.HolyBookChosen   ? 1 : 0)
			            | (Act4Settings.ShadowBookChosen  ? 2 : 0)
			            | (Act4Settings.SilverBookChosen  ? 4 : 0)
			            | (Act4Settings.CursedBookChosen  ? 8 : 0);
			if (bitmask == 0) return; // nothing chosen yet, don't write an empty entry
			EnsureAct4BookChoicesLoaded();
			string key = BuildAct3SnapshotKey(run.StartTime, run.SerializableRng?.Seed ?? string.Empty, run.Ascension, run.Players?.Count ?? 0);
			if (string.IsNullOrWhiteSpace(key)) return;
			if (!BookChoicesByRunKey.TryGetValue(key, out int existing) || existing != bitmask)
			{
				UpsertBookChoice(key, bitmask);
				PersistBookChoices();
			}
		}
		catch (Exception ex) { Logger.Warn($"Failed tracking Act 4 book choices: {ex.Message}", 1); }
	}

	private static void RestoreBookChoicesFromSave(SerializableRun run)
	{
		try
		{
			ApplyBookChoiceBitmask(0);
			EnsureAct4BookChoicesLoaded();
			string key = BuildAct3SnapshotKey(run.StartTime, run.SerializableRng?.Seed ?? string.Empty, run.Ascension, run.Players?.Count ?? 0);
			if (string.IsNullOrWhiteSpace(key) || !BookChoicesByRunKey.TryGetValue(key, out int bitmask)) return;
			ApplyBookChoiceBitmask(bitmask);
			Logger.Info($"Restored book choices bitmask {bitmask} for run key '{key}'", 1);
		}
		catch (Exception ex) { Logger.Warn($"Failed restoring Act 4 book choices: {ex.Message}", 1); }
	}

	private static void EnsureRunDamageContributionsLoaded()
	{
		SaveManager? instance = SaveManager.Instance;
		if (instance == null)
		{
			return;
		}
		int currentProfileId = instance.CurrentProfileId;
		if (_runDamageContributionsLoaded && _runDamageContributionsProfileId == currentProfileId)
		{
			return;
		}
		_runDamageContributionsLoaded = true;
		_runDamageContributionsProfileId = currentProfileId;
		_runDamageContributionsDirty = false;
		lock (RunDamageContributionLock)
		{
			RunDamageContributionByRunKey.Clear();
			RunDamageContributionRunKeyOrder.Clear();
		}
		try
		{
			string profileScopedPath = ResolveProfileScopedPath(instance, Act4RunDamageContributionsPath);
			if (!File.Exists(profileScopedPath))
			{
				return;
			}
			if (!TryDeserializeRunDamageContributionStore(File.ReadAllText(profileScopedPath), out RunDamageContributionStore? store) || store == null)
			{
				return;
			}
			lock (RunDamageContributionLock)
			{
				foreach (RunDamageContributionEntry entry in store.Entries)
				{
					if (string.IsNullOrWhiteSpace(entry.Key))
					{
						continue;
					}
					Dictionary<ulong, long> contributions = new Dictionary<ulong, long>();
					foreach (RunDamageContributionPlayerEntry playerEntry in entry.Players)
					{
						if (playerEntry.PlayerNetId == 0 || playerEntry.Damage <= 0)
						{
							continue;
						}
						contributions[playerEntry.PlayerNetId] = playerEntry.Damage;
					}
					RunDamageContributionByRunKey[entry.Key] = contributions;
					RunDamageContributionRunKeyOrder.Remove(entry.Key);
					RunDamageContributionRunKeyOrder.Add(entry.Key);
				}
				TrimRunDamageContributionCacheLocked();
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed loading run damage contributions: {ex.Message}", 1);
		}
	}

	private static void PersistRunDamageContributions()
	{
		try
		{
			SaveManager? instance = SaveManager.Instance;
			if (instance == null)
			{
				return;
			}
			RunDamageContributionStore store = new RunDamageContributionStore();
			lock (RunDamageContributionLock)
			{
				TrimRunDamageContributionCacheLocked();
				foreach (string runKey in RunDamageContributionRunKeyOrder)
				{
					if (string.IsNullOrWhiteSpace(runKey) || !RunDamageContributionByRunKey.TryGetValue(runKey, out Dictionary<ulong, long>? contributions))
					{
						continue;
					}
					RunDamageContributionEntry entry = new RunDamageContributionEntry { Key = runKey };
					foreach (KeyValuePair<ulong, long> kv in contributions.OrderBy((KeyValuePair<ulong, long> p) => p.Key))
					{
						if (kv.Key == 0 || kv.Value <= 0)
						{
							continue;
						}
						entry.Players.Add(new RunDamageContributionPlayerEntry { PlayerNetId = kv.Key, Damage = kv.Value });
					}
					store.Entries.Add(entry);
				}
				_runDamageContributionsDirty = false;
			}
			string profileScopedPath = ResolveProfileScopedPath(instance, Act4RunDamageContributionsPath);
			TryWriteTextFile(profileScopedPath, SerializeRunDamageContributionStore(store), "run damage contributions");
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed persisting run damage contributions: {ex.Message}", 1);
		}
	}

	// ── Weakest-player Str/Dex buff grant (co-op only) ─────────────────────
	// EN: Records that a player has earned +3 Str / +3 Dex for the Architect boss fight
	//     because they were the weakest damage contributor in this co-op run.
	//     Stored as "{runKey}|{netId}" in a profile-scoped JSON so it survives save & quit.
	// ZH: 记录玩家因输出最低而获得建筑师Boss战+3力量/+3敏捷增益的权利。
	//     以"{runKey}|{netId}"格式存入描述文件范围的JSON中，以便存档退出后仍能读取。

	internal static void GrantAct4WeakestBuff(Player? player)
	{
		if (player == null) return;
		try
		{
			EnsureWeakestBuffGrantsLoaded();
			if (!TryBuildCurrentRunKey(out string runKey)) return;
			string playerRunKey = $"{runKey}|{player.NetId}";
			lock (RunDamageContributionLock)
			{
				if (Act4WeakestBuffGrantedKeys.Add(playerRunKey))
				{
					Logger.Info($"Granted Act4 weakest buff to player {player.NetId} for run key {runKey}", 1);
					PersistWeakestBuffGrantsIfDirty(forcePersist: true);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"GrantAct4WeakestBuff failed: {ex.Message}", 1);
		}
	}

	/// <summary>
	/// Returns true when this player earned the +3 Str / +3 Dex buff entitlement
	/// for the Architect boss fight in a co-op run.
	/// Safe to call on either host or client machine; reads the persisted JSON.
	/// </summary>
	internal static bool HasAct4WeakestBuff(Player? player)
	{
		if (player == null) return false;
		try
		{
			EnsureWeakestBuffGrantsLoaded();
			if (!TryBuildCurrentRunKey(out string runKey)) return false;
			string playerRunKey = $"{runKey}|{player.NetId}";
			lock (RunDamageContributionLock)
			{
				return Act4WeakestBuffGrantedKeys.Contains(playerRunKey);
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"HasAct4WeakestBuff failed: {ex.Message}", 1);
			return false;
		}
	}

	private static void EnsureWeakestBuffGrantsLoaded()
	{
		SaveManager? instance = SaveManager.Instance;
		if (instance == null) return;
		int currentProfileId = instance.CurrentProfileId;
		if (_act4WeakestBuffLoaded && _act4WeakestBuffProfileId == currentProfileId) return;
		_act4WeakestBuffLoaded = true;
		_act4WeakestBuffProfileId = currentProfileId;
		lock (RunDamageContributionLock)
		{
			Act4WeakestBuffGrantedKeys.Clear();
		}
		try
		{
			string path = ResolveProfileScopedPath(instance, Act4WeakestBuffPath);
			if (!File.Exists(path)) return;
			if (!TryDeserializeAct4WeakestBuffStore(File.ReadAllText(path), out Act4WeakestBuffStore? store) || store?.PlayerRunKeys == null)
			{
				return;
			}
			lock (RunDamageContributionLock)
			{
				foreach (string key in store.PlayerRunKeys)
				{
					if (!string.IsNullOrWhiteSpace(key))
						Act4WeakestBuffGrantedKeys.Add(key);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed loading Act4 weakest buff grants: {ex.Message}", 1);
		}
	}

	private static void PersistWeakestBuffGrantsIfDirty(bool forcePersist = false)
	{
		if (!forcePersist && Act4WeakestBuffGrantedKeys.Count == 0) return;
		SaveManager? instance = SaveManager.Instance;
		if (instance == null) return;
		try
		{
			Act4WeakestBuffStore store;
			lock (RunDamageContributionLock)
			{
				store = new Act4WeakestBuffStore
				{
					SchemaVersion = 1,
					PlayerRunKeys = Act4WeakestBuffGrantedKeys.OrderBy(k => k, StringComparer.Ordinal).ToList()
				};
			}
			string path = ResolveProfileScopedPath(instance, Act4WeakestBuffPath);
			TryWriteTextFile(path, SerializeAct4WeakestBuffStore(store), "Act4 weakest buff grants");
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed persisting Act4 weakest buff grants: {ex.Message}", 1);
		}
	}

	private static void TrackRunDamageContributions(SerializableRun run)
	{
		try
		{
			EnsureRunDamageContributionsLoaded();
			if (!_runDamageContributionsDirty)
			{
				return;
			}
			string key = BuildAct3SnapshotKey(run.StartTime, run.SerializableRng?.Seed ?? string.Empty, run.Ascension, run.Players?.Count ?? 0);
			if (string.IsNullOrWhiteSpace(key))
			{
				return;
			}
			lock (RunDamageContributionLock)
			{
				// EN: Update LRU order if this run's key is already in memory.
				//     If it isn't (e.g. on a fresh session where combat happened before the first
				//     ToSave checkpoint), still call Persist so that whatever IS in memory gets written.
				// ZH: 若该跑图的key已在内存中，更新LRU顺序。
				//     若不在（例如首次ToSave检查点前已发生战斗的全新会话），
				//     仍调用Persist，将内存中的数据写入磁盘。
				if (RunDamageContributionByRunKey.ContainsKey(key))
				{
					RunDamageContributionRunKeyOrder.Remove(key);
					RunDamageContributionRunKeyOrder.Add(key);
					TrimRunDamageContributionCacheLocked();
				}
			}
			PersistRunDamageContributions();
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed tracking run damage contributions: {ex.Message}", 1);
		}
	}

	private static void ApplyBookChoiceBitmask(int bitmask)
	{
		// Sanitize: only one book can be chosen (Grand Library is a single shared choice).
		// If multiple bits are set (stale-flag corruption), keep only the lowest set bit.
		if (bitmask != 0 && (bitmask & (bitmask - 1)) != 0)
		{
			int sanitized = bitmask & (-bitmask);
			Logger.Warn($"ApplyBookChoiceBitmask: multi-bit bitmask {bitmask} sanitized to {sanitized}", 1);
			bitmask = sanitized;
		}
		Act4Settings.HolyBookChosen = (bitmask & 1) != 0;
		Act4Settings.ShadowBookChosen = (bitmask & 2) != 0;
		Act4Settings.SilverBookChosen = (bitmask & 4) != 0;
		Act4Settings.CursedBookChosen = (bitmask & 8) != 0;
	}

	private static bool TryBuildCurrentRunKey(out string key)
	{
		key = string.Empty;
		RunState? runState = GetActiveRunState();
		RunManager? runManager = RunManager.Instance;
		if (runState == null || runManager == null)
		{
			return false;
		}
		if (RunManagerStartTimeField?.GetValue(runManager) is not long startTime || startTime <= 0)
		{
			return false;
		}
		key = BuildAct3SnapshotKey(startTime, runState.Rng.StringSeed, runState.AscensionLevel, runState.Players.Count);
		return !string.IsNullOrWhiteSpace(key);
	}

	internal static void RecordRunDamageContribution(CombatState? combatState, Creature? dealer, DamageResult result, Creature target)
	{
		try
		{
			EnsureRunDamageContributionsLoaded();
			Player? dealerPlayer = dealer?.Player;
			RunState? runState = combatState?.RunState as RunState ?? dealerPlayer?.RunState as RunState;
			if (dealerPlayer == null || runState == null || runState.Players.Count <= 0)
			{
				return;
			}
			// Track only meaningful player -> enemy health damage.
			// CombatSide enum: None=0, Player=1, Enemy=2.
			if ((int)dealer.Side != 1 || target == null || (int)target.Side != 2)
			{
				return;
			}
			long delta = Math.Max(0, result.UnblockedDamage + result.OverkillDamage);
			if (delta <= 0)
			{
				return;
			}
			if (!TryBuildCurrentRunKey(out string runKey))
			{
				return;
			}
			lock (RunDamageContributionLock)
			{
				if (!RunDamageContributionByRunKey.TryGetValue(runKey, out Dictionary<ulong, long>? contributions))
				{
					contributions = new Dictionary<ulong, long>();
					RunDamageContributionByRunKey[runKey] = contributions;
					RunDamageContributionRunKeyOrder.Remove(runKey);
					RunDamageContributionRunKeyOrder.Add(runKey);
				}
				contributions.TryGetValue(dealerPlayer.NetId, out long existing);
				contributions[dealerPlayer.NetId] = existing + delta;
				RunDamageContributionRunKeyOrder.Remove(runKey);
				RunDamageContributionRunKeyOrder.Add(runKey);
				TrimRunDamageContributionCacheLocked();
				_runDamageContributionsDirty = true;
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"RecordRunDamageContribution failed: {ex.Message}", 1);
		}
	}

	internal static bool IsOwnerWeakestRunDamageContributor(Player? owner)
	{
		try
		{
			EnsureRunDamageContributionsLoaded();
			RunState? runState = owner?.RunState as RunState;
			if (owner == null || runState == null || runState.Players.Count < 2)
			{
				return false;
			}
			if (!TryBuildCurrentRunKey(out string runKey))
			{
				return false;
			}
			lock (RunDamageContributionLock)
			{
				if (!RunDamageContributionByRunKey.TryGetValue(runKey, out Dictionary<ulong, long>? contributions)
					|| contributions.Count == 0)
				{
					// EN: No damage data recorded for this run. Nobody qualifies as weakest.
					//     This is normal if no combat has occurred yet (e.g. reward node before first fight).
					// ZH: 此跑图无伤害数据（可能尚未战斗），无人符合"最弱"条件。
					Logger.Info($"IsOwnerWeakestRunDamageContributor: no damage data for run key {runKey}, returning false for player {owner.NetId}", 1);
					return false;
				}
				ulong weakestId = 0;
				long weakestDamage = long.MaxValue;
				ulong weakestTieBreakNetId = ulong.MaxValue;
				for (int i = 0; i < runState.Players.Count; i++)
				{
					Player player = runState.Players[i];
					contributions.TryGetValue(player.NetId, out long damage);
					ulong tieBreakNetId = player.NetId;
					if (damage < weakestDamage || (damage == weakestDamage && tieBreakNetId < weakestTieBreakNetId))
					{
						weakestDamage = damage;
						weakestId = player.NetId;
						weakestTieBreakNetId = tieBreakNetId;
					}
				}
				// EN: Log all players' damage for debugging desync reports.
				// ZH: 记录所有玩家的伤害数据以便调试同步问题。
				foreach (Player player in runState.Players)
				{
					contributions.TryGetValue(player.NetId, out long dmg);
					Logger.Info($"IsOwnerWeakestRunDamageContributor: player {player.NetId} damage={dmg}", 1);
				}
				bool isWeakest = weakestId == owner.NetId;
				Logger.Info($"IsOwnerWeakestRunDamageContributor: weakest={weakestId} owner={owner.NetId} result={isWeakest}", 1);
				return isWeakest;
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"IsOwnerWeakestRunDamageContributor failed: {ex.Message}", 1);
			return false;
		}
	}

	/// <summary>
	/// EN: Builds a BBCode-colored damage summary for the weakest-player dialogue.
	///     The owner's damage is shown in [red], other players' damage in [green].
	/// ZH: 为最弱玩家对话构建BBCode着色的伤害摘要。拥有者伤害为[red]，队友为[green]。
	/// </summary>
	internal static string BuildDamageSummaryForWeakestDialogue(Player? owner)
	{
		try
		{
			EnsureRunDamageContributionsLoaded();
			RunState? runState = owner?.RunState as RunState;
			if (owner == null || runState == null || runState.Players.Count < 2)
			{
				return "";
			}
			if (!TryBuildCurrentRunKey(out string runKey))
			{
				return "";
			}
			lock (RunDamageContributionLock)
			{
				if (!RunDamageContributionByRunKey.TryGetValue(runKey, out Dictionary<ulong, long>? contributions)
					|| contributions == null || contributions.Count == 0)
				{
					return "";
				}
				var sb = new StringBuilder();
				sb.Append("\n\n");
				sb.Append(ModLoc.T("Fun fact: ", "趣事："));
				bool first = true;
				for (int i = 0; i < runState.Players.Count; i++)
				{
					Player player = runState.Players[i];
					contributions.TryGetValue(player.NetId, out long dmg);
					string charName = player.Character?.Title?.GetFormattedText() ?? "???";
					bool isOwner = player.NetId == owner.NetId;
					string colorTag = isOwner ? "red" : "green";
					if (!first) sb.Append(" ");
					if (isOwner)
					{
						sb.Append(ModLoc.T(
							$"You dealt [{colorTag}]{dmg:N0}[/{colorTag}] damage.",
							$"你造成了 [{colorTag}]{dmg:N0}[/{colorTag}] 点伤害。"));
					}
					else
					{
						sb.Append(ModLoc.T(
							$"Your friend {charName} dealt [{colorTag}]{dmg:N0}[/{colorTag}] damage.",
							$"你的队友{charName}造成了 [{colorTag}]{dmg:N0}[/{colorTag}] 点伤害。"));
					}
					first = false;
				}
				sb.Append(" ");
				sb.Append(ModLoc.T(
					"You could tell them, or not. Maybe keep it a secret between us.",
					"你可以告诉他们，也可以不说。也许我们之间的小秘密就好。"));
				return sb.ToString();
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"BuildDamageSummaryForWeakestDialogue failed: {ex.Message}", 1);
			return "";
		}
	}

	private static void TrimRunDamageContributionCacheLocked()
	{
		while (RunDamageContributionRunKeyOrder.Count > MaxPersistedRunDamageContributions)
		{
			string oldest = RunDamageContributionRunKeyOrder[0];
			if (string.IsNullOrWhiteSpace(oldest))
			{
				break;
			}
			RunDamageContributionRunKeyOrder.RemoveAt(0);
			RunDamageContributionByRunKey.Remove(oldest);
			_runDamageContributionsDirty = true;
		}
	}

	/// <summary>
	/// Immediately writes the current book-choice state to the external JSON,
	/// without waiting for the next RunManager.ToSave checkpoint.
	/// Called directly from Act4GrandLibraryEvent when a book is chosen so
	/// the choice is persisted even if the player quits before the next room entry save.
	/// </summary>
	internal static void PersistBookChoiceNow()
	{
		try
		{
			int bitmask = (Act4Settings.HolyBookChosen   ? 1 : 0)
			            | (Act4Settings.ShadowBookChosen  ? 2 : 0)
			            | (Act4Settings.SilverBookChosen  ? 4 : 0)
			            | (Act4Settings.CursedBookChosen  ? 8 : 0);
			if (bitmask == 0) return;
			EnsureAct4BookChoicesLoaded();
			if (TryBuildCurrentRunKey(out string key))
			{
				UpsertBookChoice(key, bitmask);
				PersistBookChoices();
				Logger.Info($"PersistBookChoiceNow: wrote bitmask {bitmask} for run key '{key}'", 1);
				return;
			}

			// Fallback to RunManager.ToSave if the live run key is temporarily unavailable.
			SerializableRun? run = RunManager.Instance?.ToSave((AbstractRoom?)null);
			if (run != null)
			{
				TrackBookChoices(run);
				Logger.Info($"PersistBookChoiceNow: wrote bitmask {bitmask} via RunManager.ToSave fallback", 1);
				return;
			}

			Logger.Warn("PersistBookChoiceNow could not resolve the current run key; no book-choice file was written.", 1);
		}
		catch (Exception ex) { Logger.Warn($"PersistBookChoiceNow failed: {ex.Message}", 1); }
	}

	private static void UpsertBookChoice(string key, int bitmask, bool trimToLimit = true)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		BookChoicesByRunKey[key] = bitmask;
		BookChoiceRunKeyOrder.Remove(key);
		BookChoiceRunKeyOrder.Add(key);
		if (trimToLimit)
		{
			TrimBookChoicesToLimit();
		}
	}

	private static void TrimBookChoicesToLimit()
	{
		while (BookChoiceRunKeyOrder.Count > MaxPersistedBookChoices)
		{
			string oldestKey = BookChoiceRunKeyOrder[0];
			BookChoiceRunKeyOrder.RemoveAt(0);
			BookChoicesByRunKey.Remove(oldestKey);
		}
	}

	/// <summary>
	/// Fallback restore for book choices, called from NMapScreen._Ready so
	/// SaveManager is guaranteed to be ready.  If the initial
	/// RunState.FromSerializable restore already set a flag, this is a no-op.
	/// </summary>
	internal static void TryRestoreBookChoicesForActiveRun()
	{
		// No-op if any book flag is already restored.
		if (Act4Settings.HolyBookChosen || Act4Settings.ShadowBookChosen
			|| Act4Settings.SilverBookChosen || Act4Settings.CursedBookChosen) return;
		// Only needed once we're in Act 4 (Grand Library is Act-4-specific).
		RunState? rs = GetActiveRunState();
		if (rs == null || !IsAct4Placeholder(rs)) return;
		try
		{
			EnsureAct4BookChoicesLoaded();
			if (TryBuildCurrentRunKey(out string key) && BookChoicesByRunKey.TryGetValue(key, out int bitmask))
			{
				ApplyBookChoiceBitmask(bitmask);
				Logger.Info($"Restored book choices bitmask {bitmask} for active run key '{key}'", 1);
				return;
			}

			SerializableRun? run = RunManager.Instance?.ToSave((AbstractRoom?)null);
			if (run != null) RestoreBookChoicesFromSave(run);
		}
		catch (Exception ex) { Logger.Warn($"TryRestoreBookChoicesForActiveRun failed: {ex.Message}", 1); }
	}

	// ── UI toggle preferences persistence ───────────────────────────────────
	internal static void EnsureAct4PrefsLoaded()
	{
		SaveManager? instance = SaveManager.Instance;
		int currentProfileId = instance?.CurrentProfileId ?? -1;
		if (_act4PrefsLoaded && _act4PrefsProfileId == currentProfileId) return;
		_act4PrefsLoaded = true;
		_act4PrefsProfileId = currentProfileId;
		try
		{
			string profileScopedPath = GetCurrentProfileScopedPrefsPath(instance);
			if (TryLoadAct4PrefsFromPath(profileScopedPath, out string loadedFrom))
			{
				Logger.Info($"Loaded Act 4 UI prefs from {loadedFrom}", 1);
				return;
			}

			// Migration: pull forward anything previously written to the newer account/fallback paths
			// into the same profile-scoped folder used by help_potion_claims.json.
			string accountScopedPath = ResolveAccountScopedPath(Act4PrefsPath);
			if (TryLoadAct4PrefsFromPath(accountScopedPath, out loadedFrom))
			{
				Logger.Info($"Loaded Act 4 UI prefs from legacy account-scoped path {loadedFrom}; migrating to profile path", 1);
				SaveAct4Prefs();
				return;
			}

			string fallbackPath = GetFallbackPrefsPath();
			if (!string.IsNullOrWhiteSpace(fallbackPath)
				&& TryLoadAct4PrefsFromPath(fallbackPath, out loadedFrom))
			{
				Logger.Info($"Loaded Act 4 UI prefs from fallback path {loadedFrom}; migrating to profile path", 1);
				SaveAct4Prefs();
				return;
			}

			// Legacy profile-scoped path from earlier builds.
			if (instance != null)
			{
				string legacyProfileScopedPath = ResolveProfileScopedPath(instance, Act4PrefsPath);
				if (!string.IsNullOrWhiteSpace(legacyProfileScopedPath)
					&& TryLoadAct4PrefsFromPath(legacyProfileScopedPath, out loadedFrom))
				{
					Logger.Info($"Loaded Act 4 UI prefs from {loadedFrom}", 1);
					SaveAct4Prefs();
					return;
				}
			}

			Logger.Info($"No Act 4 UI prefs found; expected profile path: {profileScopedPath}", 1);
		}
		catch (Exception ex) { Logger.Warn($"Failed loading Act 4 prefs: {ex.Message}", 1); }
	}

	internal static void SaveAct4Prefs()
	{
		try
		{
			SaveManager? instance = SaveManager.Instance;
			var store = new Act4PrefsStore
			{
				HelpPotionsEnabled  = Act4Settings.HelpPotionsEnabled,
				ExtraRewardsEnabled = Act4Settings.ExtraRewardsEnabled,
			};
			string json = SerializeAct4PrefsStore(store);

			string profileScopedPath = GetCurrentProfileScopedPrefsPath(instance);
			TryWriteTextFile(profileScopedPath, json, "Act 4 UI prefs");

			string accountScopedPath = ResolveAccountScopedPath(Act4PrefsPath);
			if (!string.Equals(accountScopedPath, profileScopedPath, StringComparison.OrdinalIgnoreCase))
			{
				TryWriteTextFile(accountScopedPath, json, "Act 4 UI prefs (account-scoped mirror)");
			}

			string fallbackPath = GetFallbackPrefsPath();
			if (!string.Equals(fallbackPath, profileScopedPath, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(fallbackPath, accountScopedPath, StringComparison.OrdinalIgnoreCase))
			{
				TryWriteTextFile(fallbackPath, json, "Act 4 UI prefs (fallback mirror)");
			}

			_act4PrefsLoaded = true;
			_act4PrefsProfileId = instance?.CurrentProfileId ?? -1;
		}
		catch (Exception ex) { Logger.Warn($"Failed saving Act 4 prefs: {ex.Message}", 1); }
	}

	private static string GetCurrentProfileScopedPrefsPath(SaveManager? saveManager = null)
	{
		SaveManager? instance = saveManager ?? SaveManager.Instance;
		if (instance == null)
		{
			return string.Empty;
		}
		return ResolveProfileScopedPath(instance, Act4PrefsPath);
	}

	private static string GetFallbackPrefsPath()
	{
		// ProjectSettings.GlobalizePath converts user:// to the OS-level app-data path;
		// this always works regardless of whether a game account is signed in.
		try { return ProjectSettings.GlobalizePath("user://" + Act4PrefsPath); }
		catch { return string.Empty; }
	}

	private static void TryWriteTextFile(string path, string text, string description)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		try
		{
			string? directoryName = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.WriteAllText(path, text);
			Logger.Info($"Saved {description} to {path}", 1);
			Act4Logger.Info($"Saved {description}.");
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed saving {description} to {path}: {ex.Message}", 1);
			Act4Logger.Warn($"Failed saving {description}: {ex.Message}");
		}
	}

	private static string SerializeAct4PrefsStore(Act4PrefsStore store)
	{
		using MemoryStream stream = new MemoryStream();
		using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
		{
			writer.WriteStartObject();
			writer.WriteNumber("schema_version", store.SchemaVersion);
			writer.WriteBoolean("help_potions_enabled", store.HelpPotionsEnabled);
			writer.WriteBoolean("extra_rewards_enabled", store.ExtraRewardsEnabled);
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static bool TryDeserializeAct4PrefsStore(string text, out Act4PrefsStore? store)
	{
		store = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		using JsonDocument document = JsonDocument.Parse(text);
		JsonElement root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		store = new Act4PrefsStore
		{
			HelpPotionsEnabled = TryGetJsonBoolean(root, "help_potions_enabled", out bool helpPotionsEnabled)
				? helpPotionsEnabled
				: TryGetJsonBoolean(root, "HelpPotionsEnabled", out helpPotionsEnabled) && helpPotionsEnabled,
			ExtraRewardsEnabled = TryGetJsonBoolean(root, "extra_rewards_enabled", out bool extraRewardsEnabled)
				? extraRewardsEnabled
				: TryGetJsonBoolean(root, "ExtraRewardsEnabled", out extraRewardsEnabled) && extraRewardsEnabled,
		};
		return true;
	}

	private static string SerializeAct4BookChoiceStore(Act4BookChoiceStore store)
	{
		using MemoryStream stream = new MemoryStream();
		using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
		{
			writer.WriteStartObject();
			writer.WriteNumber("schema_version", store.SchemaVersion);
			writer.WritePropertyName("entries");
			writer.WriteStartArray();
			foreach (Act4BookChoiceEntry entry in store.Entries)
			{
				writer.WriteStartObject();
				writer.WriteString("key", entry.Key);
				writer.WriteNumber("bitmask", entry.Bitmask);
				writer.WriteEndObject();
			}
			writer.WriteEndArray();
			writer.WritePropertyName("choices");
			writer.WriteStartObject();
			foreach (KeyValuePair<string, int> choice in store.Choices)
			{
				writer.WriteNumber(choice.Key, choice.Value);
			}
			writer.WriteEndObject();
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static bool TryDeserializeAct4BookChoiceStore(string text, out Act4BookChoiceStore? store)
	{
		store = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		using JsonDocument document = JsonDocument.Parse(text);
		JsonElement root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		store = new Act4BookChoiceStore();
		if (TryGetJsonProperty(root, "entries", out JsonElement entriesElement) && entriesElement.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement entryElement in entriesElement.EnumerateArray())
			{
				if (entryElement.ValueKind != JsonValueKind.Object)
				{
					continue;
				}

				if (!TryGetJsonProperty(entryElement, "key", out JsonElement keyElement)
					|| keyElement.ValueKind != JsonValueKind.String)
				{
					continue;
				}

				string? key = keyElement.GetString();
				if (string.IsNullOrWhiteSpace(key)
					|| !TryGetJsonProperty(entryElement, "bitmask", out JsonElement bitmaskElement)
					|| bitmaskElement.ValueKind != JsonValueKind.Number
					|| !bitmaskElement.TryGetInt32(out int bitmask))
				{
					continue;
				}

				store.Entries.Add(new Act4BookChoiceEntry { Key = key, Bitmask = bitmask });
				store.Choices[key] = bitmask;
			}
			return store.Entries.Count > 0;
		}

		if (!TryGetJsonProperty(root, "choices", out JsonElement choicesElement) || choicesElement.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		foreach (JsonProperty property in choicesElement.EnumerateObject())
		{
			if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int bitmask) && !string.IsNullOrWhiteSpace(property.Name))
			{
				store.Entries.Add(new Act4BookChoiceEntry { Key = property.Name, Bitmask = bitmask });
				store.Choices[property.Name] = bitmask;
			}
		}
		return true;
	}

	private static string SerializeRunDamageContributionStore(RunDamageContributionStore store)
	{
		using MemoryStream stream = new MemoryStream();
		using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
		{
			writer.WriteStartObject();
			writer.WriteNumber("schema_version", store.SchemaVersion);
			writer.WritePropertyName("entries");
			writer.WriteStartArray();
			foreach (RunDamageContributionEntry entry in store.Entries)
			{
				writer.WriteStartObject();
				writer.WriteString("key", entry.Key);
				writer.WritePropertyName("players");
				writer.WriteStartArray();
				foreach (RunDamageContributionPlayerEntry player in entry.Players)
				{
					writer.WriteStartObject();
					writer.WriteNumber("player_net_id", player.PlayerNetId);
					writer.WriteNumber("damage", player.Damage);
					writer.WriteEndObject();
				}
				writer.WriteEndArray();
				writer.WriteEndObject();
			}
			writer.WriteEndArray();
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static bool TryDeserializeRunDamageContributionStore(string text, out RunDamageContributionStore? store)
	{
		store = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		using JsonDocument document = JsonDocument.Parse(text);
		JsonElement root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
		{
			return false;
		}
		if (!TryGetJsonProperty(root, "entries", out JsonElement entriesElement) || entriesElement.ValueKind != JsonValueKind.Array)
		{
			return false;
		}
		store = new RunDamageContributionStore();
		foreach (JsonElement entryElement in entriesElement.EnumerateArray())
		{
			if (entryElement.ValueKind != JsonValueKind.Object)
			{
				continue;
			}
			if (!TryGetJsonProperty(entryElement, "key", out JsonElement keyElement) || keyElement.ValueKind != JsonValueKind.String)
			{
				continue;
			}
			string? key = keyElement.GetString();
			if (string.IsNullOrWhiteSpace(key))
			{
				continue;
			}
			RunDamageContributionEntry entry = new RunDamageContributionEntry { Key = key };
			if (TryGetJsonProperty(entryElement, "players", out JsonElement playersElement) && playersElement.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement playerElement in playersElement.EnumerateArray())
				{
					if (playerElement.ValueKind != JsonValueKind.Object)
					{
						continue;
					}
					if (!TryGetJsonProperty(playerElement, "player_net_id", out JsonElement playerIdElement)
						|| playerIdElement.ValueKind != JsonValueKind.Number
						|| !playerIdElement.TryGetUInt64(out ulong playerNetId))
					{
						continue;
					}
					if (!TryGetJsonProperty(playerElement, "damage", out JsonElement damageElement)
						|| damageElement.ValueKind != JsonValueKind.Number
						|| !damageElement.TryGetInt64(out long damage))
					{
						continue;
					}
					entry.Players.Add(new RunDamageContributionPlayerEntry { PlayerNetId = playerNetId, Damage = damage });
				}
			}
			store.Entries.Add(entry);
		}
		return true;
	}

	private static string SerializeAct4WeakestBuffStore(Act4WeakestBuffStore store)
	{
		using MemoryStream stream = new MemoryStream();
		using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
		{
			writer.WriteStartObject();
			writer.WriteNumber("schema_version", store.SchemaVersion);
			writer.WritePropertyName("player_run_keys");
			writer.WriteStartArray();
			foreach (string key in store.PlayerRunKeys)
			{
				if (!string.IsNullOrWhiteSpace(key))
				{
					writer.WriteStringValue(key);
				}
			}
			writer.WriteEndArray();
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static bool TryDeserializeAct4WeakestBuffStore(string text, out Act4WeakestBuffStore? store)
	{
		store = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		using JsonDocument document = JsonDocument.Parse(text);
		JsonElement root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
		{
			return false;
		}
		if (!TryGetJsonProperty(root, "player_run_keys", out JsonElement keysElement))
		{
			TryGetJsonProperty(root, "PlayerRunKeys", out keysElement);
		}
		if (keysElement.ValueKind != JsonValueKind.Array)
		{
			return false;
		}
		store = new Act4WeakestBuffStore();
		if (TryGetJsonProperty(root, "schema_version", out JsonElement schemaElement) && schemaElement.ValueKind == JsonValueKind.Number && schemaElement.TryGetInt32(out int schemaVersion))
		{
			store.SchemaVersion = schemaVersion;
		}
		foreach (JsonElement keyElement in keysElement.EnumerateArray())
		{
			if (keyElement.ValueKind != JsonValueKind.String)
			{
				continue;
			}
			string? key = keyElement.GetString();
			if (!string.IsNullOrWhiteSpace(key))
			{
				store.PlayerRunKeys.Add(key);
			}
		}
		return true;
	}

	private static bool TryGetJsonProperty(JsonElement root, string propertyName, out JsonElement value)
	{
		foreach (JsonProperty property in root.EnumerateObject())
		{
			if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;
				return true;
			}
		}

		value = default;
		return false;
	}

	private static bool TryGetJsonBoolean(JsonElement root, string propertyName, out bool value)
	{
		value = false;
		if (!TryGetJsonProperty(root, propertyName, out JsonElement propertyValue))
		{
			return false;
		}

		if (propertyValue.ValueKind == JsonValueKind.True)
		{
			value = true;
			return true;
		}

		if (propertyValue.ValueKind == JsonValueKind.False)
		{
			value = false;
			return true;
		}

		if (propertyValue.ValueKind == JsonValueKind.String && bool.TryParse(propertyValue.GetString(), out bool parsed))
		{
			value = parsed;
			return true;
		}

		return false;
	}

	private static bool TryLoadAct4PrefsFromPath(string path, out string loadedFrom)
	{
		loadedFrom = path;
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return false;
		}
		if (!TryDeserializeAct4PrefsStore(File.ReadAllText(path), out Act4PrefsStore? store) || store == null)
		{
			return false;
		}
		Act4Settings.HelpPotionsEnabled = store.HelpPotionsEnabled;
		Act4Settings.ExtraRewardsEnabled = store.ExtraRewardsEnabled;
		return true;
	}

	private static string ResolveAccountScopedPath(string relativePath)
	{
		string accountScopedPath = UserDataPathProvider.GetAccountScopedBasePath(relativePath);
		if (string.IsNullOrWhiteSpace(accountScopedPath))
		{
			return accountScopedPath;
		}
		if (accountScopedPath.StartsWith("user://", StringComparison.OrdinalIgnoreCase) || accountScopedPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			return ProjectSettings.GlobalizePath(accountScopedPath);
		}
		return accountScopedPath;
	}

	private static string ResolveProfileScopedPath(SaveManager saveManager, string relativePath)
	{
		string profileScopedPath = saveManager.GetProfileScopedPath(relativePath);
		if (string.IsNullOrWhiteSpace(profileScopedPath))
		{
			return profileScopedPath;
		}
		if (profileScopedPath.StartsWith("user://", StringComparison.OrdinalIgnoreCase) || profileScopedPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			return ProjectSettings.GlobalizePath(profileScopedPath);
		}
		return profileScopedPath;
	}

	internal static decimal GetAct4BrutalHpMultiplierForPlayers(int playerCount)
	{
		playerCount = Math.Max(1, playerCount);
		if (playerCount <= 1) return Act4Config.Brutal1pHpMult;
		if (playerCount == 2) return Act4Config.Brutal2pHpMult;
		if (playerCount == 3) return Act4Config.Brutal3pHpMult;
		if (playerCount == 4) return Act4Config.Brutal4pHpMult;
		return Act4Config.Brutal5pCapHpMult;
	}

	/// EN: Brutal-only: +1% max HP per Ascension level for all Act 4 enemies (normals, elites, summons, boss).
	/// ZH: 残酷模式专属：每级升华为所有第四幕敌人增加1%最大HP（杂兵、精英、召唤物、Boss均适用）。
	internal static decimal GetAct4BrutalAscensionHpMultiplier(RunState? runState)
	{
		if (runState == null) return 1m;
		return 1m + runState.AscensionLevel * Act4Config.BrutalAscensionHpPerLevel;
	}

	/// EN: Ascension 8+: flat +5% HP for all Act 4 enemies regardless of Brutal mode.
	/// ZH: 升华8+：所有第四幕敌人固定+5%HP，与残酷模式无关。
	internal static decimal GetAct4Ascension8HpMultiplier(RunState? runState)
	{
		if (runState == null || runState.AscensionLevel < 8) return 1m;
		return 1m + Act4Config.Ascension8HpBonus;
	}

	/// EN: Ascension 9+: flat +5% damage for all Act 4 enemies regardless of Brutal mode.
	/// ZH: 升华9+：所有第四幕敌人固定+5%伤害，与残酷模式无关。
	internal static decimal GetAct4Ascension9DmgMultiplier(RunState? runState)
	{
		if (runState == null || runState.AscensionLevel < 9) return 1m;
		return 1m + Act4Config.Ascension9DmgBonus;
	}

	internal static decimal GetAct4DamageMultiplier(RunState runState)
	{
		decimal num = 1.05m + GetAct4Progress(runState) * 0.18m;
		if (((IReadOnlyCollection<Player>)runState.Players).Count <= 1)
		{
			num *= 0.9m;
		}
		AbstractRoom currentRoom = runState.CurrentRoom;
		// EN: Elite rooms intentionally use no extra damage multiplier; the global ×1.2
		//     already makes Act 4 enemies hit hard enough.  HP is boosted separately.
		// ZH: 精英房间不额外增加伤害倍率；全局×1.2已足够体现Act4难度，HP通过Act4EliteRoomHpBoost单独提升。
		if (((IReadOnlyCollection<Player>)runState.Players).Count <= 1 && IsEmpoweredAct4EliteRoom(runState))
		{
			num *= 0.9m;
		}
		// EN: Global 10% damage reduction for all Act 4 elite enemies (configurable via Act4Config.EliteDamageMultiplier).
		// ZH: 所有第四幕精英敌人全局减伤10%（通过Act4Config.EliteDamageMultiplier可调）。
		if (IsEmpoweredAct4EliteRoom(runState))
		{
			num *= Act4Config.EliteDamageMultiplier;
		}
		// EN: Global ×1.2 Act 4 hardness boost disabled, paired removal of the Architect's ×0.8
		//     in HookModifyDamagePatch means both cancel out, so removing both gives a cleaner baseline.
		// ZH: 全局×1.2强度加成已禁用——与HookModifyDamagePatch中建筑师×0.8的同步移除相抵消，移除双方得到更干净的基准。
		// num *= 1.2m;
		if (IsBrutalAct4(runState))
		{
			num *= 1.2m;
		}
		num *= GetAct4Ascension9DmgMultiplier(runState);
		return num;
	}

	internal static decimal GetAct4MultiplayerHpMultiplier(EncounterModel? encounter, int playerCount)
	{
		if (playerCount <= 1) return 1m;
		if (playerCount == 2) return Act4Config.Mp2pHpMult;
		if (playerCount == 3) return Act4Config.Mp3pHpMult;
		if (playerCount == 4) return Act4Config.Mp4pHpMult;
		return (decimal)playerCount * Act4Config.MpPerPlayerScaling;
	}

	/// EN: Returns the explicit Act 4 base HP (1-player normal) for a known Act 4 elite monster type,
	///     or -1 if the monster is not a named Act 4 elite (summons, minions, etc.).
	/// ZH: 返回已知第四幕精英怪物的显式基础HP（1人普通模式），若不是命名精英则返回-1（召唤物、杂兵等）。
	private static int GetAct4EliteBaseHp(MonsterModel monster)
	{
		if (monster is FlailKnight || monster is SpectralKnight || monster is MagiKnight)
		{
			if (monster is FlailKnight)    return Act4Config.EliteFlailKnightHp;
			if (monster is SpectralKnight) return Act4Config.EliteSpectralKnightHp;
			if (monster is MagiKnight)     return Act4Config.EliteMagiKnightHp;
		}
		if (monster is MechaKnight) return Act4Config.EliteMechaKnightHp;
		if (monster is SoulNexus)   return Act4Config.EliteSoulNexusHp;
		return -1; // unknown monster, summons/minions handled separately
	}

	/// EN: Sets the final HP for one Act 4 non-boss elite creature.
	///     Formula: ceil(act4BaseHp_1pNormal × mpMult × brutalMult × ascensionMult).
	///     Unknown monsters (summons, minions) are skipped, Architect handles its summons itself.
	/// ZH: 为一个第四幕非Boss精英生物设定最终HP。
	///     公式：上取整(1人普通基础HP × 联机倍率 × 残酷倍率 × 升华倍率)。
	///     未知怪物（召唤物、杂兵）跳过——建筑师自行管理其召唤物HP。
	internal static void ScaleAct4Enemy(Creature creature)
	{
		if (!creature.IsEnemy || creature.Monster == null)
		{
			return;
		}
		CombatState combatState = creature.CombatState;
		IRunState obj = ((combatState != null) ? combatState.RunState : null);
		RunState val = obj as RunState;
		if (!IsAct4Placeholder(val)) return;
		if (creature.Monster is Act4ArchitectBoss) return;

		// Skip boss room, Architect summons are managed by NormalizeArchitectSummonHpAsync.
		CombatRoom bossRoomCast = val.CurrentRoom as CombatRoom;
		if (bossRoomCast != null && (int)((AbstractRoom)bossRoomCast).RoomType == 3)
			return;

		int baseHp = GetAct4EliteBaseHp(creature.Monster);
		int playerCount = ((IReadOnlyCollection<Player>)val.Players).Count;

		if (baseHp <= 0)
		{
			// Unknown monster in Act 4, only boost HP in normal combat rooms (RoomType.Monster = 1).
			// Elite rooms: the named elites above cover everything; skip unknowns there.
			// Boss room is already filtered out above.
			if (bossRoomCast == null || (int)((AbstractRoom)bossRoomCast).RoomType != 1) return;
			// The game's ScaleMonsterHpForMultiplayer already ran; double it to reach
			// the ~100 HP range expected for Act 4 normals (base game gives ~50 solo).
			int fallbackHp = Math.Max(1, (int)Math.Ceiling(creature.MaxHp * 2.0m));
			creature.SetMaxHpInternal((decimal)fallbackHp);
			creature.SetCurrentHpInternal((decimal)fallbackHp);
			return;
		}

		decimal hp = (decimal)baseHp * GetAct4MultiplayerHpMultiplier(null, playerCount);
		hp *= GetAct4Ascension8HpMultiplier(val);
		if (IsBrutalAct4(val))
		{
			hp *= GetAct4BrutalHpMultiplierForPlayers(playerCount);
			hp *= GetAct4BrutalAscensionHpMultiplier(val);
		}
		int finalHp = (int)Math.Ceiling(hp);
		creature.SetMaxHpInternal((decimal)finalHp);
		creature.SetCurrentHpInternal((decimal)finalHp);
	}

	/// <summary>
	/// EN: Applies the co-op weakest-contributor +3 Strength / +3 Dexterity buff to a player creature
	///     at the start of every Act 4 combat. Called by CombatManagerAfterCreatureAddedPatch on both
	///     host and client identically, so it is fully deterministic and never causes desync.
	///     The entitlement is persisted in JSON (act4_weakest_buff_grants.json), so save & quit → resume
	///     will re-apply the buff correctly at the next combat start without requiring any new interaction.
	///     A localized fullscreen notification is shown only for the local player (purely visual, no sync needed).
	/// ZH: 在每场第四幕战斗开始时，为联机最低输出玩家施加+3力量/+3敏捷。
	///     由CombatManagerAfterCreatureAddedPatch在主机和客户端上完全一致地调用，
	///     因此完全确定，绝不引发同步问题。
	///     权利通过JSON持久化，存档退出后继续游戏时可在下次战斗开始时正确补发。
	///     本地玩家会看到一条全屏本地化通知（纯视觉效果，无需同步）。
	/// </summary>
	internal static async Task ApplyAct4WeakestPlayerBuffAsync(Creature creature)
	{
		Player? player = creature.Player;
		if (player == null) return;
		RunState? runState = player.RunState as RunState;
		// Only applies during Act 4 and only when the player earned the buff entitlement.
		if (!IsAct4Placeholder(runState)) return;
		if (!HasAct4WeakestBuff(player)) return;

		// EN: Solo players receive a smaller buff (+4/+4, +2 class bonus) from the Empyreal Cache
		//     dialogue; co-op weakest players receive the full buff (+8/+8, +4 class bonus).
		// ZH: 单人玩家获得较少增益（+4/+4，+2职业加成），联机最弱玩家获得完整增益（+8/+8，+4职业加成）。
		bool isSolo = ((IReadOnlyCollection<Player>)runState.Players).Count <= 1;
		decimal strDex = isSolo ? 4m : 8m;
		decimal classBonus = isSolo ? 2m : 4m;

		await PowerCmd.Apply<StrengthPower>(creature, strDex, creature, (CardModel)null, false);
		await PowerCmd.Apply<DexterityPower>(creature, strDex, creature, (CardModel)null, false);
		// EN: Character-specific bonus: Focus for Defect, Envenom for Silent.
		// ZH: 角色专属加成：机器人集中，静默猎人淬毒。
		if (player.Character is Defect)
			await PowerCmd.Apply<FocusPower>(creature, classBonus, creature, (CardModel)null, false);
		else if (player.Character is Silent)
			await PowerCmd.Apply<EnvenomPower>(creature, classBonus, creature, (CardModel)null, false);
		Logger.Info($"ApplyAct4WeakestPlayerBuffAsync: Str{strDex}+Dex{strDex} (+{classBonus} class bonus) applied to player {player.NetId} (solo={isSolo})", 1);
	}

	internal static async Task ApplyAct4EnemyRoomBuffsAsync(Creature creature)
	{
		CombatState combatState = creature.CombatState;
		RunState runState = (combatState != null ? combatState.RunState : null) as RunState;
		if (!creature.IsEnemy || creature.Monster == null || !IsEmpoweredAct4EliteRoom(runState))
		{
			return;
		}
		int enemyCount = Math.Max(1, creature.CombatState.Creatures.Count(c => c.IsEnemy && c.Monster != null));
		// Reduce Slippery and Artifact stacks in trio fights (3 enemies), total protection
		// is already high when enemies outnumber players.
		int protectionStacks = enemyCount >= 3 ? 1 : 2;
		await PowerCmd.Apply<ArtifactPower>(creature, (decimal)protectionStacks, creature, (CardModel)null, false);
		await PowerCmd.Apply<StrengthPower>(creature, 5m, creature, (CardModel)null, false);
		// EN: Slippery only in Brutal mode; non-Brutal elites already have Artifact for protection.
		// ZH: 滑溜仅在残酷模式下生效——非残酷精英已有神器之力提供保护。
		if (IsBrutalAct4(runState))
			await PowerCmd.Apply<SlipperyPower>(creature, (decimal)protectionStacks, creature, (CardModel)null, false);
		await PowerCmd.Apply<ArchitectBlockPiercerPower>(creature, 1m, creature, (CardModel)null, false);
	}

	internal static void AppendAct4Placeholder(RunState runState)
	{
		if (((IReadOnlyCollection<ActModel>)runState.Acts).Count <= 3)
		{
			Logger.Info("Appending placeholder Act 4 to the active run", 1);
			if (RunStateActsField == (FieldInfo)null)
			{
				throw new MissingFieldException(typeof(RunState).FullName, "<Acts>k__BackingField");
			}
			ActModel val = ((ActModel)ModelDb.Act<Glory>()).ToMutable();
			val.SetSecondBossEncounter((EncounterModel)null);
			val.SetBossEncounter(ModelDb.Encounter<Act4ArchitectBossEncounter>());
			List<ActModel> val2 = runState.Acts.ToList();
			val2.Add(val);
			RunStateActsField.SetValue((object)runState, (object)val2.ToArray());
			val.GenerateRooms(runState.Rng.UpFront, runState.UnlockState, ((IReadOnlyCollection<Player>)runState.Players).Count > 1);
			EnsureAct4AncientIsNotRecentRepeat(runState, val);
		}
	}

	private static void EnsureAct4AncientIsNotRecentRepeat(RunState runState, ActModel act4)
	{
		try
		{
			if (ActModelRoomsField == null)
			{
				return;
			}
			RoomSet roomSet = ActModelRoomsField.GetValue((object)act4) as RoomSet;
			if (roomSet == null)
			{
				return;
			}
			List<AncientEventModel> list = act4.GetUnlockedAncients(runState.UnlockState).ToList();
			if (list.Count == 0)
			{
				return;
			}
			HashSet<ModelId> hashSet = new HashSet<ModelId>();
			foreach (ActModel item in runState.Acts.Take(3))
			{
				EventModel val = null;
				try
				{
					val = item.PullAncient();
				}
				catch
				{
				}
				if (val is AncientEventModel ancientEventModel)
				{
					hashSet.Add(((AbstractModel)ancientEventModel).Id);
				}
			}
			List<AncientEventModel> list2 = list.Where(a => !hashSet.Contains(((AbstractModel)a).Id)).ToList();
			List<AncientEventModel> list3 = ((list2.Count > 0) ? list2 : list);
			if (list3.Count == 0)
			{
				return;
			}
			string text = string.Join("|", hashSet.OrderBy(id => id.Entry, StringComparer.Ordinal).Select(id => id.Entry));
			uint num = runState.Rng.Seed;
			int index = (int)(StableHash32($"{num}:act4_ancient:{text}") % (uint)list3.Count);
			roomSet.Ancient = list3[index];
			Logger.Info($"Act 4 ancient selected deterministically: {((AbstractModel)roomSet.Ancient).Id.Entry}", 1);
		}
		catch (Exception ex)
		{
			Logger.Warn($"Failed to de-duplicate Act 4 ancient: {ex.Message}", 1);
		}
	}

	private static uint StableHash32(string value)
	{
		uint num = 2166136261u;
		foreach (char c in value)
		{
			num ^= c;
			num *= 16777619;
		}
		return (num == 0) ? 1u : num;
	}

	internal static bool ShouldOverrideFinalActWin(RunState? runState)
	{
		if (!IsAct4Placeholder(runState))
		{
			return false;
		}
		AbstractRoom currentRoom = runState.CurrentRoom;
		CombatRoom val = currentRoom as CombatRoom;
		return val != null && (int)((AbstractRoom)val).RoomType == 3;
	}

	internal static async Task FinishRunAfterAct4BossAsync(RunManager runManager)
	{
		using NetLoadingHandle _ = new NetLoadingHandle(runManager.NetService);
		await ShowPinnedFullscreenTextAsync("THE ARCHITECT FALLS", 2f);
		MarkAct4BossVictory(runManager.DebugOnlyGetState());
		SerializableRun serializableRun = runManager.OnEnded(true);
		NRun.Instance?.ShowGameOverScreen(serializableRun);
		await Cmd.Wait(0.1f, false);
	}

	internal static async Task ShowPinnedFullscreenTextAsync(string text, float durationSeconds)
	{
		if (NGame.Instance == null) return;
		Control node = PreloadManager.Cache.GetScene("res://scenes/vfx/vfx_fullscreen_text.tscn").Instantiate<Control>(PackedScene.GenEditState.Disabled);
		((Node)node).GetNode<MegaLabel>("Label").SetTextAutoSize(text);
		GodotTreeExtensions.AddChildSafely(NGame.Instance, node);
		await Cmd.Wait(durationSeconds, false);
		GodotTreeExtensions.QueueFreeSafely(node);
	}

	internal static async Task ShowAct4EnterTextAsync()
	{
		await ShowPinnedFullscreenTextAsync("ACT 4 FINAL ASCENT", 2f);
	}

	// Grant the next ascension level unlock the moment a player commits to entering Act 4.
	// This runs on each machine (once per machine, not once per player) so each player's
	// local progress file is updated independently, matching what WinRun does at run end.
	internal static void GrantAscensionForAct4Transition(RunState runState)
	{
		const int MaxAllowed = 10;
		SaveManager? sm = SaveManager.Instance;
		if (sm == null) return;
		ProgressState progress = sm.Progress;
		int ascension = runState.AscensionLevel;
		bool isSinglePlayer = ((IReadOnlyCollection<Player>)runState.Players).Count == 1;
		if (isSinglePlayer)
		{
			// Mirror IncrementSingleplayerAscension: advance the local player's per-character max.
			Player? local = ((IReadOnlyCollection<Player>)runState.Players).FirstOrDefault();
			if (local != null)
			{
				CharacterStats charStats = progress.GetOrCreateCharacterStats(local.Character.Id);
				if (ascension == charStats.MaxAscension && charStats.MaxAscension < MaxAllowed)
				{
					charStats.MaxAscension++;
					charStats.PreferredAscension = charStats.MaxAscension;
					Logger.Info($"GrantAscensionForAct4: solo ascension unlocked to {charStats.MaxAscension} for {local.Character.Id}", 1);
				}
			}
		}
		else
		{
			// Mirror IncrementMultiplayerAscension: advance the shared co-op max.
			if (ascension == progress.MaxMultiplayerAscension && progress.MaxMultiplayerAscension < MaxAllowed)
			{
				progress.MaxMultiplayerAscension++;
				progress.PreferredMultiplayerAscension = progress.MaxMultiplayerAscension;
				Logger.Info($"GrantAscensionForAct4: multiplayer ascension unlocked to {progress.MaxMultiplayerAscension}", 1);
			}
		}
		sm.SaveProgressFile();
	}

	internal static async Task ProceedToAct4Async(RunState runState, bool brutal)
	{
		// Set flag BEFORE any await - RunManagerEnterNextActPatch will check this to
		// force AppendAct4Placeholder if MoveToNextAct fires before our async path completes.
		Act4TransitionPending = true;
		ClearAct4BossVictory(runState);
		SetAct4Brutal(runState, brutal);
		SaveAct3SnapshotFromRunState(runState);
		GrantAscensionForAct4Transition(runState);
		await ShowAct4EnterTextAsync();
		AppendAct4Placeholder(runState);
		Act4TransitionPending = false;
		EnsureAct4ArchitectBossConfigured(runState, 3);
	}

	internal static bool IsBrutalAct4(RunState? runState)
	{
		return runState != null && BrutalAct4Runs.Contains(runState);
	}

	internal static void RecordArchitectDifficultyChoiceOptions(int normalIndex, int brutalIndex)
	{
		if (normalIndex < 0 || brutalIndex < 0)
		{
			return;
		}
		_architectDifficultyChoiceActive = true;
		_architectDifficultyNormalIndex = (uint)normalIndex;
		_architectDifficultyBrutalIndex = (uint)brutalIndex;
		Logger.Info($"RecordArchitectDifficultyChoiceOptions: normalIndex={normalIndex} brutalIndex={brutalIndex}", 1);
	}

	internal static bool TryApplyArchitectDifficultyChoice(RunState? runState, uint optionIndex, string source)
	{
		if (!_architectDifficultyChoiceActive || runState == null || runState.CurrentActIndex != 2 || ((IReadOnlyCollection<ActModel>)runState.Acts).Count > 3)
		{
			return false;
		}
		if (optionIndex != _architectDifficultyNormalIndex && optionIndex != _architectDifficultyBrutalIndex)
		{
			return false;
		}

		bool brutal = optionIndex == _architectDifficultyBrutalIndex;
		SetAct4Brutal(runState, brutal);
		_architectDifficultyChoiceActive = false;
		Logger.Info($"TryApplyArchitectDifficultyChoice: source={source} optionIndex={optionIndex} brutal={brutal}", 1);
		return true;
	}

	internal static decimal GetAct4ProgressForDebug(RunState runState)
	{
		return GetAct4Progress(runState);
	}

	internal static void SetAct4Brutal(RunState? runState, bool brutal)
	{
		if (runState == null)
		{
			return;
		}
		if (brutal)
		{
			BrutalAct4Runs.Add(runState);
			return;
		}
		BrutalAct4Runs.Remove(runState);
	}

	internal static async Task JumpToAct3ForTestingAsync(RunState? runState)
	{
		if (runState != null && runState.CurrentActIndex < 2)
		{
			Logger.Info("Jumping directly to Act 3 for Act 4 testing", 1);
			if (RunManager.Instance != null)
				await RunManager.Instance.EnterAct(2, true);
		}
	}

	private static decimal GetAct4Progress(RunState runState)
	{
		MapCoord? currentMapCoord = runState.CurrentMapCoord;
		if (!currentMapCoord.HasValue)
		{
			return 0m;
		}
		int num = Math.Max(1, runState.Map.GetRowCount() - 1);
		decimal num2 = (decimal)currentMapCoord.Value.row / (decimal)num;
		if (num2 < 0m)
		{
			return 0m;
		}
		if (num2 > 1m)
		{
			return 1m;
		}
		return num2;
	}

	private static bool IsEmpoweredAct4EliteRoom(RunState? runState)
	{
		if (IsAct4Placeholder(runState))
		{
			AbstractRoom currentRoom = runState.CurrentRoom;
			CombatRoom val = currentRoom as CombatRoom;
			if (val != null && (int)((AbstractRoom)val).RoomType == 2)
			{
				MapCoord? currentMapCoord = runState.CurrentMapCoord;
				return currentMapCoord.HasValue && currentMapCoord.GetValueOrDefault().row == 4;
			}
		}
		return false;
	}

	private static bool IsKnightsEliteTrioMember(MonsterModel monster)
	{
		return monster is FlailKnight || monster is SpectralKnight || monster is MagiKnight;
	}

	private static void OnAdminButtonPressed()
	{
		RunState activeRunState = GetActiveRunState();
		if (activeRunState == null)
		{
			Logger.Warn("Admin button pressed without an active run state", 1);
			return;
		}
		Player me = LocalContext.GetMe(activeRunState);
		if (me == null)
		{
			Logger.Warn("Admin button press ignored because no local player was available", 1);
			return;
		}
		if (!IsLocalPlayerHost(activeRunState))
		{
			Logger.Warn($"Admin button press ignored: player {me.NetId} is not the host", 1);
			return;
		}
		if (IsAdminEnabled(me))
		{
			Logger.Info($"Admin button toggled OFF by host player {me.NetId}", 1);
			DisableAdminMode(me);
		}
		else
		{
			Logger.Info($"Admin button pressed by host player {me.NetId} - skipping to Act 3 for {activeRunState.Players.Count} player(s)", 1);
			if (((IReadOnlyCollection<Player>)activeRunState.Players).Count > 1)
			{
				// Multiplayer: enqueue ONE action that applies buffs + EnterAct on BOTH machines.
				// Do NOT call EnableAdminModeAsync here - that mutates MaxEnergy/HP locally only,
				// which diverges from the client and causes an immediate desync.
				RunManager.Instance?.ActionQueueSynchronizer
					?.RequestEnqueue(new AdminSkipToAct3Action(me));
			}
			else
			{
				// Single-player: apply buffs locally then jump
				TaskHelper.RunSafely(EnableAdminModeAsync(me));
				TaskHelper.RunSafely(JumpToAct3ForTestingAsync(activeRunState));
			}
		}
	}

	private static RunState? GetActiveRunState()
	{
		RunManager instance = RunManager.Instance;
		return (instance != null) ? instance.DebugOnlyGetState() : null;
	}

	private static NButton CreateAdminButton()
	{
		NButton val = new NButton
		{
			Name = "Act4Placeholder_AdminModeButton",
			TooltipText = ModLoc.T("Enable test buffs and unrestricted map travel for this player.", "为当前玩家启用测试增益，并允许在第四幕外自由移动地图。", fra: "Activer les buffs de test et les déplacements sur la carte sans restriction pour ce joueur.", deu: "Testbuffs und unbeschränkte Kartenbewegung für diesen Spieler aktivieren.", jpn: "このプレイヤーにテストバフと自由なマップ移動を有効にします。", kor: "이 플레이어에게 테스트 버프와 자유로운 지도 이동을 활성화합니다.", por: "Ativar buffs de teste e viagem irrestrita no mapa para este jogador.", rus: "Включить тестовые бонусы и свободное перемещение по карте для этого игрока.", spa: "Activar buffs de prueba y viaje sin restricciones en el mapa para este jugador."),
			FocusMode = Control.FocusModeEnum.All,
			MouseFilter = Control.MouseFilterEnum.Stop,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
			Position = new Vector2(24f, 199f),
			CustomMinimumSize = new Vector2(250f, 44f),
			Size = new Vector2(250f, 44f),
			ZIndex = 50
		};
		ColorRect val2 = new ColorRect
		{
			Name = "Background",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Color = new Color(0.34f, 0.08f, 0.08f, 0.95f)
		};
		((Control)val2).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Node)val).AddChild(val2, false, Node.InternalMode.Disabled);
		Label val3 = new Label
		{
			Name = "Label",
			Text = ModLoc.T("[ADMIN MODE] ENABLE", "[管理模式] 启用", fra: "[MODE ADMIN] ACTIVER", deu: "[ADMIN-MODUS] AKTIVIEREN", jpn: "[管理者モード] 有効化", kor: "[관리자 모드] 활성화", por: "[MODO ADMIN] ATIVAR", rus: "[РЕЖИМ АДМИНА] ВКЛЮЧИТЬ", spa: "[MODO ADMIN] ACTIVAR"),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = (HorizontalAlignment)1,
			VerticalAlignment = (VerticalAlignment)1
		};
		((Control)val3).SetAnchorsPreset(Control.LayoutPreset.FullRect, false);
		((Control)val3).AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.95f, 1f));
		((Control)val3).AddThemeFontSizeOverride("font_size", 18);
		((Node)val).AddChild(val3, false, Node.InternalMode.Disabled);
		((GodotObject)val).Connect(NClickableControl.SignalName.Released, Callable.From<NButton>((Action<NButton>)OnAdminButtonReleased), 0u);
		return val;
	}

	private static NButton? FindAdminButton(NMapScreen mapScreen)
	{
		return ((Node)mapScreen).GetNodeOrNull<NButton>(new NodePath("Act4Placeholder_AdminModeButton"));
	}

	private static void UpdateAdminButtonVisuals(NButton button, bool isEnabled)
	{
		((Control)button).TooltipText = isEnabled
			? ModLoc.T("Admin mode is active for this player. Free map travel is enabled everywhere.", "当前玩家已启用管理模式。进入第四幕后将禁用自由地图移动。", fra: "Le mode admin est actif pour ce joueur. Le déplacement libre sur la carte est activé partout.", deu: "Admin-Modus ist für diesen Spieler aktiv. Freie Kartenbewegung überall aktiviert.", jpn: "このプレイヤーはアドミンモードが有効です。どこでもマップを自由に移動できます。", kor: "이 플레이어에게 관리자 모드가 활성화되어 있습니다. 어디서든 지도를 자유롭게 이동할 수 있습니다.", por: "O modo admin está ativo para este jogador. Viagem livre no mapa ativada em todos os lugares.", rus: "Режим администратора активен. Свободное перемещение по карте включено.", spa: "El modo admin está activo para este jugador. El viaje libre por el mapa está habilitado en todas partes.")
			: ModLoc.T("Enable test buffs and unrestricted map travel for this player.", "为当前玩家启用测试增益，并允许在第四幕外自由移动地图。", fra: "Activer les buffs de test et les déplacements sur la carte sans restriction pour ce joueur.", deu: "Testbuffs und unbeschränkte Kartenbewegung für diesen Spieler aktivieren.", jpn: "このプレイヤーにテストバフと自由なマップ移動を有効にします。", kor: "이 플레이어에게 테스트 버프와 자유로운 지도 이동을 활성화합니다.", por: "Ativar buffs de teste e viagem irrestrita no mapa para este jogador.", rus: "Включить тестовые бонусы и свободное перемещение по карте для этого игрока.", spa: "Activar buffs de prueba y viaje sin restricciones en el mapa para este jugador.");
		ColorRect nodeOrNull = ((Node)button).GetNodeOrNull<ColorRect>("Background");
		if (nodeOrNull != null)
		{
			nodeOrNull.Color = GetAdminButtonBackground(isEnabled, _isAdminButtonPressed);
		}
		Label nodeOrNull2 = ((Node)button).GetNodeOrNull<Label>("Label");
		if (nodeOrNull2 != null)
		{
			nodeOrNull2.Text = isEnabled
				? ModLoc.T("[ADMIN MODE] ENABLED", "[管理模式] 已启用", fra: "[MODE ADMIN] ACTIVÉ", deu: "[ADMIN-MODUS] AKTIVIERT", jpn: "[管理者モード] 有効", kor: "[관리자 모드] 활성화됨", por: "[MODO ADMIN] ATIVADO", rus: "[РЕЖИМ АДМИНА] ВКЛ", spa: "[MODO ADMIN] ACTIVADO")
				: ModLoc.T("[ADMIN MODE] ENABLE", "[管理模式] 启用", fra: "[MODE ADMIN] ACTIVER", deu: "[ADMIN-MODUS] AKTIVIEREN", jpn: "[管理者モード] 有効化", kor: "[관리자 모드] 활성화", por: "[MODO ADMIN] ATIVAR", rus: "[РЕЖИМ АДМИНА] ВКЛЮЧИТЬ", spa: "[MODO ADMIN] ACTIVAR");
			((CanvasItem)nodeOrNull2).Modulate = (isEnabled ? new Color(1f, 0.98f, 0.98f, 1f) : new Color(1f, 0.95f, 0.95f, 1f));
		}
	}

	private static void OnAdminButtonReleased(NClickableControl _)
	{
		Logger.Info("Admin button released", 1);
		OnAdminButtonPressed();
	}

	internal static bool TryHandleAdminButtonInput(NMapScreen mapScreen, InputEvent inputEvent)
	{
		return HandleMapButtonInput(mapScreen, inputEvent, FindAdminButton(mapScreen), ref _isAdminButtonPressed, delegate(NButton button)
		{
			UpdateAdminButtonVisuals(button, !((NClickableControl)button).IsEnabled);
		}, OnAdminButtonPressed);
	}

	private static bool HandleMapButtonInput(NMapScreen mapScreen, InputEvent inputEvent, NButton? button, ref bool isPressed, Action<NButton> updateVisuals, Action onReleased)
	{
		if (button == null || !((CanvasItem)button).IsVisibleInTree())
		{
			return false;
		}
		InputEventMouseButton val = inputEvent as InputEventMouseButton;
		if (val == null || (long)val.ButtonIndex != 1)
		{
			return false;
		}
		Rect2 val2 = new Rect2(((Control)button).GlobalPosition, ((Control)button).Size);
		bool flag = val2.HasPoint(((InputEventMouse)val).GlobalPosition);
		if (val.Pressed)
		{
			if (!flag)
			{
				return false;
			}
			isPressed = true;
			updateVisuals(button);
			((Node)mapScreen).GetViewport()?.SetInputAsHandled();
			return true;
		}
		if (!isPressed)
		{
			return false;
		}
		isPressed = false;
		updateVisuals(button);
		if (flag)
		{
			onReleased();
		}
		((Node)mapScreen).GetViewport()?.SetInputAsHandled();
		return true;
	}

	private static Color GetAdminButtonBackground(bool isEnabled, bool isPressed)
	{
		if (isPressed)
		{
			return isEnabled ? new Color(0.44f, 0.1f, 0.1f, 0.98f) : new Color(0.22f, 0.05f, 0.05f, 0.98f);
		}
		return isEnabled ? new Color(0.58f, 0.14f, 0.14f, 0.98f) : new Color(0.34f, 0.08f, 0.08f, 0.95f);
	}

	internal static bool IsLocalPlayerHost(RunState runState)
	{
		if (!LocalContext.NetId.HasValue || runState.Players.Count == 0)
			return false;
		return runState.Players[0].NetId == LocalContext.NetId.Value;
	}
}
