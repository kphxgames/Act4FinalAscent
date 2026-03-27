//=============================================================================
// Act4ArchitectBossMechanics.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Retaliation, adaptive resistance, summon bookkeeping, and combat-side helper logic for the Architect.
// ZH: 建筑师的反击、自适应抗性、召唤记账以及战斗侧辅助逻辑。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace Act4Placeholder;

public sealed partial class Act4ArchitectBoss : MonsterModel
{
	private static readonly MethodInfo[] MonsterSetupSkinsMethods = typeof(MonsterModel)
		.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
		.Where(static method => method.Name == nameof(SetupSkins))
		.ToArray();

	private int _ambientPulseCount;

	private bool _phaseFourFinishRunQueued;

	// Linked Shadow death drain accumulator, batched so concurrent deaths do not race each other.
	private int _pendingLinkedShadowDrainHp;

	private bool _linkedShadowDrainScheduled;

	private int _ambientVfxLoopGeneration;

	private void RequestQueuedRetaliationActionIfHost(CombatState combatState)
	{
		if (_hasQueuedRetaliationActionRequested || combatState.CurrentSide != CombatSide.Player)
		{
			return;
		}
		RunManager runManager = RunManager.Instance;
		if (runManager == null || runManager.NetService.Type == NetGameType.Client)
		{
			return;
		}
		Player me = LocalContext.GetMe(combatState);
		if (me == null)
		{
			LogArchitect("RequestQueuedRetaliationActionIfHost:missing-local-player");
			return;
		}
		ActionQueueSynchronizer actionQueueSynchronizer = runManager.ActionQueueSynchronizer;
		if (actionQueueSynchronizer == null)
		{
			LogArchitect("RequestQueuedRetaliationActionIfHost:missing-action-queue");
			return;
		}
		_hasQueuedRetaliationActionRequested = true;
		actionQueueSynchronizer.RequestEnqueue(new Act4ArchitectQueuedRetaliationAction(me));
	}

	/// EN: Execute the deferred retaliation after turn-start processing is finished.
	/// ZH: 在回合开始处理结束后执行延迟反击。
	internal async Task ExecuteQueuedRetaliationActionAsync()
	{
		_hasQueuedRetaliationActionRequested = false;
		await TryTriggerRetaliationAsync();
	}

	/// EN: React to damage for phase swaps, retaliation, summons, and boss upkeep.
	/// ZH: 响应受伤事件，处理转阶段、反击、召唤与Boss维护。
	public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target == ((MonsterModel)this).Creature && props.HasFlag(ValueProp.Unpowered))
		{
			LogArchitect($"AfterDamageReceived:skip-unpowered hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp} unblocked={result.UnblockedDamage} pending={PendingPhaseNumber} phase={PhaseNumber}");
			return;
		}
		if (target == ((MonsterModel)this).Creature)
		{
			LogArchitect($"AfterDamageReceived:before hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp} unblocked={result.UnblockedDamage} pending={PendingPhaseNumber} phase={PhaseNumber} dealer={(dealer != null ? dealer.GetType().Name : "null")} props={props}");
		}
		await base.AfterDamageReceived(choiceContext, target, result, props, dealer, cardSource);
		if (target == ((MonsterModel)this).Creature)
		{
			if (((MonsterModel)this).Creature.CombatState?.CurrentSide == CombatSide.Player && result.UnblockedDamage > 0)
			{
				_currentPlayerRoundDamageTaken += result.UnblockedDamage;
			}
			await UpdateRetaliationResolutionStatusAsync(result, props, dealer, cardSource);
			await SyncRetaliationCounterAsync();
			await SyncArchitectBarricadeAsync();
		}
		if (target == ((MonsterModel)this).Creature)
		{
			await SyncArchitectBarricadeAsync();
			if (IsPhaseFour)
			{
				TryShowPhaseFourThresholdSpeech();
			}
			try
			{
				await TryTriggerImmediatePhaseSummonAsync();
			}
			catch (System.Exception ex)
			{
				GD.PrintErr($"[Act4Placeholder] TryTriggerImmediatePhaseSummonAsync failed: {ex}");
			}
			await TryTriggerEmergencyFogmogAsync();
			// If a phase transition was triggered by this damage (e.g. massive hit
			// dropped the Architect to 0 HP), skip retaliation  -  the ReviveMove
			// handles everything from here.
			if (IsAwaitingPhaseTransition)
			{
				LogArchitect($"AfterDamageReceived:skip-retaliation-phase-transition pending={PendingPhaseNumber}");
			}
			else if (ShouldQueueRetaliationForNextPlayerTurn())
			{
				_hasQueuedRetaliationForNextPlayerTurn = true;
				LogArchitect($"RetaliationQueued phase={PhaseNumber} hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp}");
			}
			else
			{
				await TryTriggerRetaliationAsync();
			}
			LogArchitect($"AfterDamageReceived:after hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp} pending={PendingPhaseNumber} phase={PhaseNumber} awaiting={IsAwaitingPhaseTransition}");
		}
		else if (IsPhaseThree && target != null && target.Monster is ArchitectShadowChampion && target.IsDead)
		{
			await SyncSummonLinkedThornsAsync();
			LogArchitect($"AfterDamageReceived:shadow-died sync-thorns shadows={GetCurrentSummonedShadowCount()}");
		}
		else if (IsShadowBookChosen()
			&& dealer?.Player != null
			&& target != null
			&& !props.HasFlag(ValueProp.Unpowered)
			&& result.UnblockedDamage > 0
			&& target.IsAlive
			&& IsArchitectSummonedMinion(target))
		{
			// Shadow Tome: player damage against Architect-summoned minions is doubled.
			int bonusDamage = Math.Min(target.CurrentHp, result.UnblockedDamage);
			if (bonusDamage > 0)
			{
				int beforeHp = target.CurrentHp;
				int afterHp = Math.Max(0, beforeHp - bonusDamage);
				await CreatureCmd.SetCurrentHp(target, (decimal)afterHp);
				NDamageNumVfx? bonusDmgVfx = NDamageNumVfx.Create(target, bonusDamage, requireInteractable: false);
				if (bonusDmgVfx != null)
					NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(bonusDmgVfx);
				LogArchitect($"ShadowTome:double-damage target={target.Monster?.GetType().Name} base={result.UnblockedDamage} bonus={bonusDamage} hp={beforeHp}->{afterHp}");
			}
		}
	}

	private static bool IsArchitectSummonedMinion(Creature target)
	{
		return target.Monster is Guardbot
			|| target.Monster is Noisebot
			|| target.Monster is SpectralKnight
			|| target.Monster is MagiKnight
			|| target.Monster is ArchitectSummonedFogmog
			|| target.Monster is ArchitectShadowChampion;
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		AnimState val = new AnimState("idle_loop", true);
		AnimState val2 = new AnimState("attack", false);
		AnimState val3 = new AnimState("hurt", false);
		val2.NextState = val;
		val3.NextState = val;
		CreatureAnimator val4 = new CreatureAnimator(val, controller);
		val4.AddAnyState("Idle", val, (Func<bool>)null);
		val4.AddAnyState("Attack", val2, (Func<bool>)null);
		val4.AddAnyState("Hit", val3, (Func<bool>)null);
		return val4;
	}

	private static int CurrentPlayerCount()
	{
		RunManager instance = RunManager.Instance;
		int? obj;
		if (instance == null)
		{
			obj = null;
		}
		else
		{
			RunState obj2 = instance.DebugOnlyGetState();
			obj = ((obj2 != null) ? new int?(((IReadOnlyCollection<Player>)obj2.Players).Count) : ((int?)null));
		}
		return Math.Max(1, obj ?? 1);
	}

	private static RunState? CurrentRunState()
	{
		RunManager instance = RunManager.Instance;
		return instance?.DebugOnlyGetState();
	}

	private decimal GetPhaseOneTargetHp()
	{
		int playerCount = CurrentPlayerCount();
		decimal soloHp = Act4Config.ArchitectP1SoloHp;
		decimal hp = (playerCount <= 1) ? soloHp
			: ((playerCount == 2) ? (soloHp * Act4Config.ArchitectP1To2pMultiplier)
			: (soloHp * (decimal)playerCount * Act4Config.ArchitectMpPerPlayerScaling));
		RunState runState = CurrentRunState();
		hp *= ModSupport.GetAct4Ascension8HpMultiplier(runState);
		if (ModSupport.IsBrutalAct4(runState))
		{
			hp *= ModSupport.GetAct4BrutalHpMultiplierForPlayers(playerCount);
			hp *= ModSupport.GetAct4BrutalAscensionHpMultiplier(runState);
		}
		return hp;
	}

	private RunState GetRunState()
	{
		IRunState runState = (((MonsterModel)this).Creature.CombatState != null) ? ((MonsterModel)this).Creature.CombatState.RunState : null;
		return runState as RunState;
	}

	// ── Grand Library book helpers ────────────────────────────────────────────
	// Book choices are tracked via Act4Settings flags (set during the event).
	// PowerCmd.Apply silently no-ops outside combat, so we can't rely on creature powers.
	private bool IsHolyBookChosen() => Act4Settings.HolyBookChosen;
	private bool IsShadowBookChosen() => Act4Settings.ShadowBookChosen;
	private bool IsSilverBookChosen() => Act4Settings.SilverBookChosen;
	private bool IsCursedBookChosen() => Act4Settings.CursedBookChosen;

	/// EN: Apply book-selected phase-start modifiers to players or the boss.
	/// ZH: 应用选书带来的阶段开始效果到玩家或Boss。
	private async Task ApplyBookPhaseStartEffectsAsync()
	{
		if (_hasAppliedGrandLibraryBookCombatEffects)
		{
			return;
		}
		// Gate applies whenever any book is chosen so re-entering a phase never double-applies.
		bool anyBookChosen = IsHolyBookChosen() || IsShadowBookChosen() || IsSilverBookChosen() || IsCursedBookChosen();
		if (!anyBookChosen)
		{
			return;
		}
		if (IsShadowBookChosen())
		{
			// Shadow Tome: doubled player damage vs summoned minions is handled in AfterDamageReceived.
		}
		if (IsCursedBookChosen())
		{
			// Guard against save/reload: CursedTomePlayerBonusPower is serialized with creature state,
			// so if it's already present the effects were applied before the save and must not be stacked.
			bool cursedAlreadyApplied = CombatState.Players.Any(p => p.Creature.GetPower<CursedTomePlayerBonusPower>() != null);
			if (!cursedAlreadyApplied)
			{
				// Cursed Tome: players suffer 99 Vul/Weak/Frail but gain +2 max Energy and +2 draw/turn.
				await PowerCmd.Apply<VulnerablePower>(CombatState.Players.Select(p => p.Creature), 99m, ((MonsterModel)this).Creature, (CardModel)null, false);
				await PowerCmd.Apply<WeakPower>(CombatState.Players.Select(p => p.Creature), 99m, ((MonsterModel)this).Creature, (CardModel)null, false);
				await PowerCmd.Apply<FrailPower>(CombatState.Players.Select(p => p.Creature), 99m, ((MonsterModel)this).Creature, (CardModel)null, false);
				await PowerCmd.Apply<CursedTomePlayerBonusPower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
			}
		}
		_hasAppliedGrandLibraryBookCombatEffects = true;
	}

	private int GetSelfArtifactStacks(int baseStacks)
	{
		return ModSupport.IsBrutalAct4(GetRunState()) ? Math.Max(1, baseStacks * 4) : Math.Max(1, baseStacks);
	}

	/// EN: Keep ambient boss VFX alive across the fight.
	/// ZH: 在整场战斗中维持Boss的环境特效循环。
	private async Task RunAmbientVfxLoopAsync(int loopGeneration)
	{
		// EN: In Instant mode (e.g. BetterSpire2 "Instant Fast Mode"), Cmd.Wait returns immediately,
		//     turning this loop into a tight spin that floods the scene with VFX objects and stalls
		//     RoomFadeIn, causing a black-screen soft-lock. VFX are cosmetic-only; skip entirely.
		// ZH: 即时模式下Cmd.Wait立即返回，此循环死循环堆积特效对象并阻塞RoomFadeIn（黑屏卡死）。纯视觉特效，直接跳过。
		if (SaveManager.Instance?.PrefsSave.FastMode == FastModeType.Instant)
			return;
		LogArchitect("RunAmbientVfxLoop:start");
		while (ShouldKeepAmbientVfxAlive(loopGeneration))
		{
			AddCombatVfx(NBounceSparkVfx.Create(((MonsterModel)this).Creature, (VfxColor)7));
			_ambientPulseCount++;
			if (IsPhaseThree && _ambientPulseCount % 3 == 0)
			{
				NPowerUpVfx.CreateGhostly(((MonsterModel)this).Creature);
			}
			if (_ambientPulseCount <= 3 || _ambientPulseCount % 10 == 0)
			{
				LogArchitect($"RunAmbientVfxLoop:pulse count={_ambientPulseCount} phase={PhaseNumber}");
			}
			await Cmd.Wait(IsPhaseThree ? 0.55f : (IsPhaseTwo ? 0.8f : 1.1f), false);
		}
		LogArchitect("RunAmbientVfxLoop:end");
	}

	private bool ShouldKeepAmbientVfxAlive(int loopGeneration)
	{
		if (loopGeneration != _ambientVfxLoopGeneration)
		{
			return false;
		}
		Creature creature = ((MonsterModel)this).Creature;
		CombatState combatState = creature.CombatState;
		if (combatState == null || !creature.IsAlive)
		{
			return false;
		}
		RunManager instance = RunManager.Instance;
		if (instance != null && (instance.IsAbandoned || instance.IsGameOver))
		{
			return false;
		}
		if (combatState.RunState.IsGameOver)
		{
			return false;
		}
		if (!combatState.PlayerCreatures.Any((Creature playerCreature) => playerCreature.IsAlive))
		{
			return false;
		}
		NCombatRoom instance2 = NCombatRoom.Instance;
		if (instance2 == null || instance2.GetCreatureNode(creature) == null)
		{
			return false;
		}
		return true;
	}

	/// EN: Retry missing protection stacks after a phase shift.
	/// ZH: 在转阶段后补齐缺失的防护层数。
	private async Task EnsurePhaseProtectionAsync(int phaseNumber, decimal requiredArtifactStacks = 2m, decimal requiredSlipperyStacks = 2m)
	{
		for (int i = 0; i < 5; i++)
		{
			await Cmd.Wait(0.08f, true);
			if (((MonsterModel)this).Creature.CombatState == null || !((MonsterModel)this).Creature.IsAlive) break;
			int currentArtifact = ((MonsterModel)this).Creature.GetPower<ArtifactPower>()?.Amount ?? 0;
			int currentSlippery = ((MonsterModel)this).Creature.GetPower<SlipperyPower>()?.Amount ?? 0;
			decimal missingArtifact = Math.Max(0m, requiredArtifactStacks - (decimal)currentArtifact);
			decimal missingSlippery = Math.Max(0m, requiredSlipperyStacks - (decimal)currentSlippery);
			if (missingArtifact <= 0m && missingSlippery <= 0m)
			{
				GD.Print($"[Act4Placeholder] Phase {phaseNumber} protection confirmed. Artifact={currentArtifact} Slippery={currentSlippery}");
				break;
			}
			if (missingArtifact > 0m)
				await PowerCmd.Apply<ArtifactPower>(((MonsterModel)this).Creature, missingArtifact, ((MonsterModel)this).Creature, (CardModel)null, false);
			if (missingSlippery > 0m)
				await PowerCmd.Apply<SlipperyPower>(((MonsterModel)this).Creature, missingSlippery, ((MonsterModel)this).Creature, (CardModel)null, false);
		}
		ArtifactPower? p1 = ((MonsterModel)this).Creature.GetPower<ArtifactPower>();
		SlipperyPower? p2 = ((MonsterModel)this).Creature.GetPower<SlipperyPower>();
		GD.Print($"[Act4Placeholder] Phase {phaseNumber} protection still missing after retries. Artifact={p1?.Amount ?? 0} Slippery={p2?.Amount ?? 0}");
	}

	private async Task SummonPhaseOneBotsAsync()
	{
		GD.Print("[Act4Placeholder] Architect summoning phase 1 bots");
		await SummonMinionAsync<Guardbot>();
		await SummonMinionAsync<Noisebot>();
	}

	private async Task SummonPhaseTwoMinionsAsync()
	{
		if (IsPhaseTwo)
		{
			await SummonPhaseTwoKnightsAsync();
		}
		else
		{
			await SummonPhaseOneBotsAsync();
		}
	}

	private async Task SummonPhaseTwoKnightsAsync()
	{
		GD.Print("[Act4Placeholder] Architect summoning phase 2 knights");
		await SummonMinionAsync<SpectralKnight>();
		await SummonMinionAsync<MagiKnight>();
	}

	private async Task SummonPhaseThreeShadowsAsync(int requestedSummonCount = 2)
	{
		GD.Print("[Act4Placeholder] Architect summoning phase 3 shadows");
		List<Player> players = CombatState.Players.ToList();
		if (players.Count == 0) return;
		int openSlots = Math.Max(0, 2 - GetCurrentSummonedShadowCount());
		int summonCount = Math.Min(Math.Min(requestedSummonCount, openSlots), players.Count);
		if (summonCount <= 0) return;
		if (players.Count > summonCount)
		{
			((MonsterModel)this).RunRng.MonsterAi.Shuffle<Player>((IList<Player>)players);
			players = players.Take(summonCount).ToList();
		}
		int extraPlayerCount = Math.Max(0, ((IReadOnlyCollection<Player>)((MonsterModel)this).CombatState.Players).Count - summonCount);
		decimal hpMultiplier = GetShadowHpMultiplier(extraPlayerCount);
		decimal damageMultiplier = GetShadowDamageMultiplier(extraPlayerCount);
		// Shadow protection = 1 stack per player (e.g. 2 players = 2 stacks, not 12).
		int protectionStacks = Math.Max(1, ((IReadOnlyCollection<Player>)((MonsterModel)this).CombatState.Players).Count);
		foreach (Player player in players)
		{
			MonsterModel shadowModel = CreateShadowModelForPlayer(player.Character);
			ArchitectShadowChampion? shadow = shadowModel as ArchitectShadowChampion;
			if (shadow == null) continue;
			shadow.BonusHpMultiplier = hpMultiplier;
			shadow.BonusDamageMultiplier = damageMultiplier;
			shadow.FlatHeavyDamageBonus += 2;
			shadow.FlatMultiDamageBonus += 1;
			Creature? creature = await SummonSpecificMinionAsync(shadow);
			if (creature == null) break;
			int targetHp = GetArchitectSummonTargetHp(creature);
			await SetMaxHpCompatAsync(creature, (decimal)targetHp);
			await CreatureCmd.Heal(creature, (decimal)targetHp, false);
			// Use SetAmount (idempotent) instead of Apply (additive)  -  in co-op
			// the move executes on both host and client simultaneously, so additive
			// PowerCmd.Apply would stack 3× across network replication paths.
			if (((MonsterModel)this).RunRng.MonsterAi.NextBool())
				await PowerCmd.SetAmount<SlipperyPower>(creature, (decimal)protectionStacks, ((MonsterModel)this).Creature, (CardModel)null);
			else
				await PowerCmd.SetAmount<ArtifactPower>(creature, (decimal)protectionStacks, ((MonsterModel)this).Creature, (CardModel)null);
		}
	}

	private async Task SummonMinionAsync<T>() where T : MonsterModel
	{
		await SummonSpecificMinionAsync(((MonsterModel)ModelDb.Monster<T>()).ToMutable());
	}

	private async Task<Creature?> SummonSpecificMinionAsync(MonsterModel monster)
	{
		if (GetCurrentActiveSummonCount() >= 2)
		{
			return null;
		}
		await Cmd.Wait(0.2f, false);
		if (GetCurrentActiveSummonCount() >= 2)
		{
			return null;
		}
		Creature minion = await CreatureCmd.Add(monster, ((MonsterModel)this).CombatState, (CombatSide)2, (string)null);
		await PowerCmd.Apply<MinionPower>(minion, 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		await NormalizeArchitectSummonHpAsync(minion);
		NPowerUpVfx.CreateNormal(minion);
		PositionSummonedEnemy(minion);
		// Re-compute FallControl position from the final placement (Guardbot/Noisebot have a
		// FallControl node that was initially set at origin before PositionSummonedEnemy ran).
		NCreature minionNode = NCombatRoom.Instance?.GetCreatureNode(minion);
		if (minionNode != null && (monster is Guardbot || monster is Noisebot))
			FabricatorNormal.SetBotFallPosition(minionNode);
		return minion;
	}

	private void AddCombatVfx(Node? node)
	{
		if (node == null)
		{
			return;
		}
		NCombatRoom instance = NCombatRoom.Instance;
		if (instance != null)
		{
			Control combatVfxContainer = instance.CombatVfxContainer;
			if (combatVfxContainer != null)
			{
				GodotTreeExtensions.AddChildSafely(combatVfxContainer, node);
			}
		}
	}

	private int ApplySoloHeavyNerf(int damage)
	{
		if (CurrentPlayerCount() == 1)
		{
			damage = (int)Math.Ceiling((decimal)damage * Act4Config.SoloHeavyNerfFactor);
		}
		return Math.Max(1, damage);
	}

	private Color GetAwaitingPhaseTint()
	{
		return new Color(0.52f, 0.5f, 0.56f, 1f);
	}

	private float GetCurrentVisualScale()
	{
		if (IsPhaseFour)
		{
			return 1.2f;
		}
		if (IsPhaseThree)
		{
			return 1.2f;
		}
		return IsPhaseTwo ? 1.1f : 1.05f;
	}

	private void ApplyArchitectVisuals(Color color, float scale, bool preservePosition = false)
	{
		NCombatRoom instance = NCombatRoom.Instance;
		NCreature val = ((instance != null) ? instance.GetCreatureNode(((MonsterModel)this).Creature) : null);
		Vector2? val2 = ((!preservePosition) ? ((Vector2?)null) : ((val != null) ? new Vector2?(((Control)val).Position) : ((Vector2?)null)));
		if (val != null)
		{
			CanvasGroup nodeOrNull = ((Node)val).GetNodeOrNull<CanvasGroup>(new NodePath("%CanvasGroup"));
			if (nodeOrNull != null)
			{
				((CanvasItem)nodeOrNull).SetSelfModulate(color);
			}
			// Fallback modulation path: some forms don't expose %CanvasGroup consistently.
			((CanvasItem)val).SelfModulate = color;
			if (val.Visuals != null)
			{
				((CanvasItem)val.Visuals).SelfModulate = color;
				val.Visuals.Modulate = color;
			}
		}
		if (val != null)
		{
			val.SetDefaultScaleTo(scale, 0.2f);
		}
		if (val2.HasValue && val != null)
		{
			((Control)val).Position = val2.Value;
		}
	}

	private void RestoreArchitectReviveUi()
	{
		NCombatRoom instance = NCombatRoom.Instance;
		NCreature creatureNode = ((instance != null) ? instance.GetCreatureNode(((MonsterModel)this).Creature) : null);
		if (creatureNode == null)
		{
			LogArchitect("RestoreArchitectReviveUi:missing-node");
			return;
		}
		creatureNode.Visuals.Modulate = Colors.White;
		((CanvasItem)creatureNode).SelfModulate = Colors.White;
		((CanvasItem)creatureNode.Visuals).SelfModulate = Colors.White;
		creatureNode.StartReviveAnim();
		creatureNode.ToggleIsInteractable(((MonsterModel)this).IsHealthBarVisible);
		creatureNode.Hitbox.FocusMode = Control.FocusModeEnum.All;
		ApplyArchitectBounds(creatureNode);
	}

	private void SwapArchitectSkeletonData(string skeletonDataPath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(skeletonDataPath))
			{
				return;
			}
			NCombatRoom instance = NCombatRoom.Instance;
			NCreature creatureNode = ((instance != null) ? instance.GetCreatureNode(((MonsterModel)this).Creature) : null);
			MegaSprite spineController = ModSupport.TryGetCreatureSpineController(creatureNode);
			if (spineController == null)
			{
				LogArchitect($"SwapArchitectSkeletonData:missing-controller path={skeletonDataPath}");
				return;
			}
			string animationName = "idle_loop";
			try
			{
				MegaTrackEntry current = spineController.GetAnimationState().GetCurrent(0);
				string currentAnimationName = current.GetAnimation().GetName();
				if (!string.IsNullOrWhiteSpace(currentAnimationName))
				{
					animationName = currentAnimationName;
				}
			}
			catch (Exception ex)
			{
				LogArchitect($"SwapArchitectSkeletonData:anim-read-failed path={skeletonDataPath} error={ex.Message}");
			}
			Resource resource = ResourceLoader.Load(skeletonDataPath, string.Empty, ResourceLoader.CacheMode.Ignore);
			if (resource == null)
			{
				LogArchitect($"SwapArchitectSkeletonData:load-failed path={skeletonDataPath}");
				return;
			}
			Node2D body = spineController?.BoundObject as Node2D;
			if (body != null)
			{
				body.Visible = true;
				body.Set("skeleton_data_res", resource);
				body.Call("set_skeleton_data_res", resource);
			}
			((CanvasItem)creatureNode).Visible = true;
			if (creatureNode.Visuals != null)
			{
				((CanvasItem)creatureNode.Visuals).Visible = true;
			}
			spineController.SetSkeletonDataRes(new MegaSkeletonDataResource(resource));
			MegaAnimationState animationState = spineController.GetAnimationState();
			TrySetupSkinsCompat(creatureNode.Visuals, spineController);
			if (!spineController.HasAnimation(animationName))
			{
				animationName = "idle_loop";
			}
			animationState.SetAnimation(animationName, animationName == "idle_loop");
			animationState.Update(0f);
			animationState.Apply(spineController.GetSkeleton());
			creatureNode.Visuals.SetUpSkin((MonsterModel)this);
			creatureNode.SetAnimationTrigger("Idle");
			ApplyArchitectBounds(creatureNode);
			LogArchitect($"SwapArchitectSkeletonData:applied path={skeletonDataPath} anim={animationName}");
		}
		catch (Exception ex)
		{
			LogArchitect($"SwapArchitectSkeletonData:apply-failed path={skeletonDataPath} error={ex.GetType().Name}: {ex.Message}");
		}
	}

	private async Task TryApplyPhaseTransitionVisualsAsync(string transitionName, Color tint, float targetScale, bool movingRightwards, string skeletonDataPath)
	{
		try
		{
			await PlayPhaseTransitionBurstAsync(tint, targetScale, movingRightwards);
			SwapArchitectSkeletonData(skeletonDataPath);
			ApplyArchitectVisuals(tint, targetScale, preservePosition: true);
		}
		catch (Exception ex)
		{
			LogArchitect($"{transitionName}:visual-transition-failed error={ex.GetType().Name}: {ex.Message}");
		}
	}

	private void TrySetupSkinsCompat(NCreatureVisuals visuals, MegaSprite spineController)
	{
		try
		{
			MegaSkeleton skeleton = spineController.GetSkeleton();
			foreach (MethodInfo method in MonsterSetupSkinsMethods)
			{
				ParameterInfo[] parameters = method.GetParameters();
				if (parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(visuals))
				{
					method.Invoke(this, new object[] { visuals });
					return;
				}
				if (parameters.Length == 2 &&
					parameters[0].ParameterType.IsInstanceOfType(spineController) &&
					parameters[1].ParameterType.IsInstanceOfType(skeleton))
				{
					method.Invoke(this, new object[] { spineController, skeleton });
					return;
				}
			}
			LogArchitect("TrySetupSkinsCompat:no-compatible-overload");
		}
		catch (Exception ex)
		{
			LogArchitect($"TrySetupSkinsCompat:failed error={ex.Message}");
		}
	}

	private async Task PlayPhaseTransitionBurstAsync(Color tint, float targetScale, bool movingRightwards)
	{
		NCombatRoom instance = NCombatRoom.Instance;
		NCreature creatureNode = ((instance != null) ? instance.GetCreatureNode(((MonsterModel)this).Creature) : null);
		ApplyArchitectVisuals(tint, targetScale);
		NCombatRoom instance2 = NCombatRoom.Instance;
		if (instance2 != null && instance2.CombatVfxContainer != null)
		{
			GodotTreeExtensions.AddChildSafely(instance2.CombatVfxContainer, NAdditiveOverlayVfx.Create((VfxColor)5));
			GodotTreeExtensions.AddChildSafely(instance2.CombatVfxContainer, NHorizontalLinesVfx.Create(new Color("FFF4C880"), 1.1, movingRightwards));
		}
		NPowerUpVfx.CreateGhostly(((MonsterModel)this).Creature);
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
		VfxCmd.PlayOnCreatureCenter(((MonsterModel)this).Creature, "vfx/vfx_gaze");
		AddCombatVfx(NBounceSparkVfx.Create(((MonsterModel)this).Creature, (VfxColor)7));
		if (creatureNode != null)
		{
			creatureNode.ScaleTo(1.18f, 0.08f);
			await Cmd.Wait(0.1f, false);
			creatureNode.ScaleTo(1f, 0.22f);
		}
		await Cmd.Wait(0.2f, false);
	}

	private decimal GetShadowHpMultiplier(int extraPlayerCount)
	{
		return 1m + (decimal)extraPlayerCount * 0.2m;
	}

	private MonsterModel CreateShadowModelForPlayer(CharacterModel character)
	{
		if (character is Ironclad)
		{
			return ((MonsterModel)ModelDb.Monster<ShadowIronclad>()).ToMutable();
		}
		if (character is Silent)
		{
			return ((MonsterModel)ModelDb.Monster<ShadowSilent>()).ToMutable();
		}
		if (character is Defect)
		{
			return ((MonsterModel)ModelDb.Monster<ShadowDefect>()).ToMutable();
		}
		if (character is Regent)
		{
			return ((MonsterModel)ModelDb.Monster<ShadowRegent>()).ToMutable();
		}
		if (character is Necrobinder)
		{
			return ((MonsterModel)ModelDb.Monster<ShadowNecrobinder>()).ToMutable();
		}
		switch (((MonsterModel)this).RunRng.MonsterAi.NextInt(5))
		{
		case 0:
			return ((MonsterModel)ModelDb.Monster<ShadowIronclad>()).ToMutable();
		case 1:
			return ((MonsterModel)ModelDb.Monster<ShadowSilent>()).ToMutable();
		case 2:
			return ((MonsterModel)ModelDb.Monster<ShadowDefect>()).ToMutable();
		case 3:
			return ((MonsterModel)ModelDb.Monster<ShadowRegent>()).ToMutable();
		default:
			return ((MonsterModel)ModelDb.Monster<ShadowNecrobinder>()).ToMutable();
		}
	}

	private decimal GetShadowDamageMultiplier(int extraPlayerCount)
	{
		decimal num = 1m + (decimal)extraPlayerCount * 0.1m;
		if (CurrentPlayerCount() == 1)
		{
			num *= 0.8m;
		}
		return num;
	}

	private async Task EmpowerPhaseTwoSummonAsync(Creature minion)
	{
		int targetHp = GetArchitectSummonTargetHp(minion);
		if (targetHp > minion.MaxHp)
		{
			await SetMaxHpCompatAsync(minion, (decimal)targetHp);
			await CreatureCmd.Heal(minion, (decimal)targetHp, false);
		}
		int artifactStacks = 0;
		int strengthAmount = 1;
		if (CurrentPlayerCount() == 1)
		{
			strengthAmount = Math.Max(0, strengthAmount - 1);
		}
		if (artifactStacks > 0)
		{
			await PowerCmd.SetAmount<ArtifactPower>(minion, (decimal)artifactStacks, ((MonsterModel)this).Creature, (CardModel)null);
		}
		if (strengthAmount > 0)
		{
			await PowerCmd.SetAmount<StrengthPower>(minion, (decimal)strengthAmount, ((MonsterModel)this).Creature, (CardModel)null);
		}
	}

	private async Task NormalizeArchitectSummonHpAsync(Creature minion)
	{
		int targetHp = GetArchitectSummonTargetHp(minion);
		await SetMaxHpCompatAsync(minion, (decimal)targetHp);
		await CreatureCmd.SetCurrentHp(minion, (decimal)targetHp);
	}

	private int GetArchitectSummonTargetHp(Creature minion)
	{
		int soloTargetHp = minion.Monster switch
		{
			Guardbot => 50,
			Noisebot => 45,
			SpectralKnight => 179,
			MagiKnight => 155,
			ArchitectSummonedFogmog => 40,
			Phase4LinkedShadow pls => pls.BaseLinkedShadowHp, // must precede ArchitectShadowChampion (is subtype)
			ArchitectShadowChampion => Act4Config.ArchitectShadowHp,
			_ => 0  // unknown summon: leave HP unchanged
		};
		if (soloTargetHp <= 0)
		{
			// Fallback for any unrecognized summon type: don't normalize HP, avoid
			// double-counting the multiplayer factor that's already baked in.
			return minion.MaxHp;
		}
		int playerCount = CurrentPlayerCount();
		int targetHp;
		if (minion.Monster is Phase4LinkedShadow pls2)
		{
			// Same multiplier table as all other Architect summons:
			// 1p=1.0×  2p=2.0×  3p=3.9×  4p=5.2×  5p=6.5×  …
			decimal linkedFactor = ModSupport.GetAct4MultiplayerHpMultiplier(null, playerCount);
			targetHp = Math.Max(1, (int)Math.Ceiling(pls2.BaseLinkedShadowHp * linkedFactor));
		}
		else if (minion.Monster is ArchitectShadowChampion)
		{
			// Shadows: 2 are summoned for 2+ players so each shadow scales more slowly.
			// Multipliers: 1p=1.0×  2p=0.9×  3p=1.5×  4p=2.1×  5p=2.7×  (+0.6 per extra player above 2)
			decimal shadowMpFactor = (playerCount <= 1)
				? 1.0m
				: Act4Config.ShadowMp2pBase + (playerCount - 2) * Act4Config.ShadowMpStepPerPlayer;
			targetHp = Math.Max(1, (int)Math.Ceiling(Act4Config.ArchitectShadowHp * shadowMpFactor));
		}
		else if (playerCount <= 1)
		{
			targetHp = soloTargetHp;
		}
		else
		{
			// Non-shadow summons: use the same multiplayer HP table as regular Act 4 enemies.
			// (2p=2.0×  3p=3.9×  4p=5.2×  5p=6.5×  …)
			decimal mpFactor = ModSupport.GetAct4MultiplayerHpMultiplier(null, playerCount);
			targetHp = Math.Max(1, (int)Math.Ceiling(soloTargetHp * mpFactor));
		}
		// Ascension 8: +5% HP for all boss-room summons (unconditional).
		// Brutal-mode Ascension bonus: +1% HP per level for boss-room summons
		// (ScaleAct4Enemy is skipped for boss-room creatures, so we apply it here).
		RunState? runState = CurrentRunState();
		targetHp = (int)Math.Ceiling(targetHp * ModSupport.GetAct4Ascension8HpMultiplier(runState));
		if (ModSupport.IsBrutalAct4(runState))
		{
			targetHp = (int)Math.Ceiling(targetHp * ModSupport.GetAct4BrutalAscensionHpMultiplier(runState));
		}
		return targetHp;
	}

	private async Task EnsurePersistentShieldAsync()
	{
		// Silver Book: players stole the shield tome  -  no persistent Barricade shield for the Architect.
		if (IsSilverBookChosen()) return;
		LogArchitect($"EnsurePersistentShield:start hasBarricade={((MonsterModel)this).Creature.HasPower<BarricadePower>()} block={((MonsterModel)this).Creature.Block}");
		decimal shieldAmount = Math.Max(1m, Math.Ceiling((decimal)((MonsterModel)this).Creature.MaxHp * 0.04m));
		await GainArchitectBlockCappedAsync(shieldAmount);
		await SyncArchitectBarricadeAsync();
		LogArchitect($"EnsurePersistentShield:end added={shieldAmount} block={((MonsterModel)this).Creature.Block}");
	}

	private int GetCurrentStrengthCap()
	{
		if (IsPhaseFour)
		{
			return Act4Config.ArchitectP4StrengthCap;
		}
		if (IsPhaseThree)
		{
			return Act4Config.ArchitectP3StrengthCap;
		}
		return IsPhaseTwo ? Act4Config.ArchitectP2StrengthCap : Act4Config.ArchitectP1StrengthCap;
	}

	private int GetUnderCapStrengthCadence()
	{
		if (IsPhaseThree)
		{
			return Act4Config.ArchitectP3UnderCapCadence;
		}
		return Act4Config.ArchitectP1P2UnderCapCadence;
	}

	private int GetOverCapStrengthCadence()
	{
		return IsBrutalHighPlayerCount() ? 2 : Act4Config.ArchitectOverCapCadence;
	}

	private int GetPassiveStrengthGainPerTrigger()
	{
		return IsBrutalHighPlayerCount() ? 2 : 1;
	}

	private bool IsBrutalHighPlayerCount()
	{
		return CurrentPlayerCount() >= 3 && ModSupport.IsBrutalAct4(GetRunState());
	}

	private int GetUpcomingEnemyTurnNumberForNextMoveSelection()
	{
		return _enemyTurnCount + ((((MonsterModel)this).CombatState?.CurrentSide == CombatSide.Enemy) ? 2 : 1);
	}

	// EN: Preview-only helper. This mirrors the passive strength cadence so move planning can show the extra Buff intent up front.
	// ZH: 纯预览辅助。它复刻被动力量的节奏，让招式规划一开始就能把额外增益意图显示出来。
	private bool ShouldShowStrengthBuffIntentForUpcomingMove()
	{
		if (IsPhaseFour)
		{
			return false;
		}
		int nextCount = GetUpcomingEnemyTurnNumberForNextMoveSelection();
		int strengthCap = GetCurrentStrengthCap();
		int currentStrength = ((MonsterModel)this).Creature.GetPower<StrengthPower>()?.Amount ?? 0;
		int underCapCadence = GetUnderCapStrengthCadence();
		if (currentStrength < strengthCap && (underCapCadence <= 1 || nextCount % underCapCadence == 1))
		{
			return true;
		}
		return currentStrength >= strengthCap && nextCount % GetOverCapStrengthCadence() == 0;
	}

	private MoveState? GetPreviewAdjustedMoveState(MoveState? baseState)
	{
		if (baseState == null)
		{
			return null;
		}
		if (!ShouldShowStrengthBuffIntentForUpcomingMove())
		{
			return baseState;
		}
		return _strengthComboMap.TryGetValue(baseState, out MoveState? comboMove) ? comboMove : baseState;
	}

	private void SetPlannedMoveImmediate(MoveState? baseState, bool preserveExistingMoveHistory = true)
	{
		MoveState? plannedState = GetPreviewAdjustedMoveState(baseState);
		if (plannedState != null)
		{
			((MonsterModel)this).SetMoveImmediate(plannedState, preserveExistingMoveHistory);
		}
	}

	private void PositionSummonedEnemy(Creature minion)
	{
		NCombatRoom instance = NCombatRoom.Instance;
		NCreature val = ((instance != null) ? instance.GetCreatureNode(((MonsterModel)this).Creature) : null);
		NCreature val2 = ((instance != null) ? instance.GetCreatureNode(minion) : null);
		if (val == null || val2 == null)
		{
			return;
		}
		List<Creature> list = CombatState.Enemies.Where(c => c != ((MonsterModel)this).Creature && c != minion && c.IsAlive).ToList();
		bool flag = false;
		bool flag2 = false;
		foreach (Creature item in list)
		{
			NCreature val3 = instance?.GetCreatureNode(item);
			if (val3 != null)
			{
				if (((Control)val3).Position.X < ((Control)val).Position.X)
				{
					flag = true;
				}
				else
				{
					flag2 = true;
				}
			}
		}
		Vector2 val4 = ((!flag) ? new Vector2(-305f, -105f) : ((!flag2) ? new Vector2(235f, -105f) : new Vector2(235f, -105f)));
		((Control)val2).Position = ((Control)val).Position + val4;
	}

	/// EN: Summon all 5 Phase 4 Linked Shadows (one of each class) on the left side of the screen.
	///     Each uses a 3-state loop (HEAVY→MULTI→BUFF) at 50% damage; starting state varies per type.
	/// ZH: 召唤全部5种四阶段连结之影（各一），降落于屏幕左侧。各以50%伤害进行3态循环（单击→多段→格挡），起始态因类型而异。
	private async Task SummonPhaseFourLinkedShadowsAsync()
	{
		MonsterModel[] linked = new MonsterModel[]
		{
			((MonsterModel)ModelDb.Monster<LinkedShadowIronclad>()).ToMutable(),
			((MonsterModel)ModelDb.Monster<LinkedShadowSilent>()).ToMutable(),
			((MonsterModel)ModelDb.Monster<LinkedShadowDefect>()).ToMutable(),
			((MonsterModel)ModelDb.Monster<LinkedShadowRegent>()).ToMutable(),
			((MonsterModel)ModelDb.Monster<LinkedShadowNecrobinder>()).ToMutable(),
		};
		int index = 0;
		foreach (MonsterModel model in linked)
		{
			if (model is Phase4LinkedShadow linkedShadow)
			{
				linkedShadow.BonusDamageMultiplier = Act4Config.LinkedShadowDamageMultiplier;
				// No player-count damage scaling exists for linked shadows, so add a flat bonus
				// at 3+ players to keep Phase 4 feeling meaningful in co-op.
				if (CurrentPlayerCount() >= 3)
				{
					linkedShadow.FlatMultiDamageBonus = Act4Config.LinkedShadowCoOpMultiBonus3p;
					linkedShadow.FlatHeavyDamageBonus = Act4Config.LinkedShadowCoOpHeavyBonus3p;
				}
			}
			// Capture index so the lambda closes over the correct value each iteration.
			int capturedIndex = index;
			Creature? creature = await SummonLinkedShadowAsync(model, c => PositionPhaseFourLinkedShadow(c, capturedIndex));
			if (creature == null) { index++; continue; }
			await PowerCmd.Apply<LinkedShadowPower>(creature, 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
			index++;
		}
	}

	/// EN: Spawn a Linked Shadow without the 2-summon cap enforced by SummonSpecificMinionAsync.
	///     onCreatureAdded fires immediately after CreatureCmd.Add so the caller can position
	///     the shadow before the first frame renders, eliminating the visible teleport pop.
	/// ZH: 不受2召唤上限限制地生成连结之影。onCreatureAdded在Add后立即回调，避免位置闪变。
	private async Task<Creature?> SummonLinkedShadowAsync(MonsterModel monster, Action<Creature>? onCreatureAdded = null)
	{
		await Cmd.Wait(0.15f, false);
		Creature minion = await CreatureCmd.Add(monster, ((MonsterModel)this).CombatState, (CombatSide)2, (string)null);
		onCreatureAdded?.Invoke(minion); // Position before any async yield so first rendered frame is correct.
		await PowerCmd.Apply<MinionPower>(minion, 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		await NormalizeArchitectSummonHpAsync(minion);
		NPowerUpVfx.CreateNormal(minion);
		return minion;
	}

	/// EN: Position one of the 5 Linked Shadows for Phase 4.
	///     Defect (index 2) and Regent (index 3) stay near the Architect  -  left-above.
	///     Ironclad (0), Silent (1), Necrobinder (4) go to the far-left side of the screen
	///     using GlobalPosition so they land in the player-side area regardless of which
	///     container the enemy node lives in.
	/// ZH: 将五个连结之影分为两组。暗影偏差（2）和暗影摄政（3）留在建筑师左上附近；
	///     暗影铁甲（0）、暗影沉默（1）和暗影死灵（4）通过全局坐标放置在屏幕最左侧。
	private void PositionPhaseFourLinkedShadow(Creature shadow, int index)
	{
		NCombatRoom room = NCombatRoom.Instance;
		NCreature archNode = room?.GetCreatureNode(((MonsterModel)this).Creature);
		NCreature shadowNode = room?.GetCreatureNode(shadow);
		if (archNode == null || shadowNode == null) return;

		// Defect (index 2) and Regent (index 3): near the Architect, above-left.
		// Reuse the original Ironclad / Silent offsets so they sit where the first
		// two shadows did before the positions were changed.
		if (index == 2 || index == 3)
		{
			Vector2 offset = (index == 3)
				? new Vector2(-305f, -200f)   // Regent: compensated for Architect's rightward shift.
				: new Vector2(-175f, -80f);    // Defect: compensated for Architect's rightward shift.
			((Control)shadowNode).Position = ((Control)archNode).Position + offset;
			return;
		}

		// Ironclad (0), Silent (1), Necrobinder (4): far-left side of the screen.
		// Triangle formation: 1 top center, 2 bottom row. Moved up so they
		// don't overlap the player's hand card area at the bottom.
		Vector2 archGlobal = ((Control)archNode).GlobalPosition;

		// Map: index 0=Ironclad (top), 1=Silent, 4=Necrobinder.
		// Latest tuning:
		// - Silent shifted 5px left.
		// - Necrobinder shifted 15px left.
		// Ironclad gets ZIndex 1 so he stays visually in front.
		int leftSlot = (index == 0) ? 0 : (index == 1) ? 1 : 2;
		float[] xPositions = new float[] { 155f, 445f, 280f }; // Ironclad unchanged, Silent -5, Necrobinder -30
		float[] yOffsets   = new float[] { -150f, -85f, 10f }; // Silent up 35
		((Control)shadowNode).GlobalPosition = new Vector2(xPositions[leftSlot], archGlobal.Y + yOffsets[leftSlot]);
		((CanvasItem)shadowNode).ZIndex = (leftSlot == 0) ? 1 : 0;
	}

	/// EN: At Phase 3→4 transition, smoothly reposition the Architect further right and
	///     players to their fully-centered positions (identical to Kaiser Crab's layout).
	///     Works correctly for 1-7 players: NCombatRoom.PositionPlayersAndPets() with
	///     fullyCenterPlayers=true computes the same algorithm Kaiser Crab uses.
	///     VFX follow automatically because VfxSpawnPosition is a Marker2D child of the
	///     NCreature node  -  GlobalPosition updates as the node moves.
	/// ZH: 三→四阶段过渡时平滑重定位：建筑师向右移，玩家移至居中位置（与凯撒螃蟹相同）。
	///     1-7人均正确处理，VFX随创建者节点自动跟随。
	private void RepositionCreaturesForPhaseFour()
	{
		NCombatRoom? room = NCombatRoom.Instance;
		if (room == null) return;

		// PositionPlayersAndPets expects only allied nodes (players + their pets).
		// Passing enemies here can break its PetOwner lookup logic.
		var allyNodes = room.CreatureNodes.Where(n => n.Entity.IsPlayer || n.Entity.IsPet).ToList();
		var savedPositions = allyNodes.Select(n => n.Position).ToList();

		// Let the game compute proper centered positions for any player count.
		// PositionPlayersAndPets sets Position directly; we capture the targets then
		// animate from the saved positions back to those targets.
		NCombatRoom.PositionPlayersAndPets(
			allyNodes,
			1.0f,               // camera scaling for this encounter (no zoom)
			fullyCenterPlayers: true);

		for (int i = 0; i < allyNodes.Count; i++)
		{
			var node = allyNodes[i];
			Vector2 target = node.Position + new Vector2(100f, 0f);
			node.Position = savedPositions[i]; // restore so the tween animates from here
			Tween t = node.CreateTween();
			t.TweenProperty(node, "position", target, 0.7)
				.SetEase(Tween.EaseType.InOut)
				.SetTrans(Tween.TransitionType.Sine);
		}

		// Shift Architect right so right-side shadows move with it and players can also shift right.
		NCreature? archNode = room.GetCreatureNode(((MonsterModel)this).Creature);
		if (archNode != null)
		{
			Tween archTween = archNode.CreateTween();
			archTween.TweenProperty(archNode, "position:x", ((Control)archNode).Position.X + 275f, 0.7)
				.SetEase(Tween.EaseType.InOut)
				.SetTrans(Tween.TransitionType.Sine);
		}
	}

	private int GetCurrentActiveSummonCount()
	{
		return CombatState.Enemies.Count(c => c != ((MonsterModel)this).Creature && c.IsAlive);
	}

	/// EN: Called by each dying Phase4LinkedShadow. Accumulates HP drain so that
	///     concurrent deaths (e.g. AoE killing all 5) are batched and applied in
	///     one pass  -  avoiding race conditions from reading CurrentHp simultaneously.
	/// ZH: 被每个死亡的连结之影调用。积累HP消耗，防止并发死亡导致竞态条件。
	public void AccumulateLinkedShadowDrainHp(int amount)
	{
		_pendingLinkedShadowDrainHp += amount;
		if (!_linkedShadowDrainScheduled)
		{
			_linkedShadowDrainScheduled = true;
			TaskHelper.RunSafely(ApplyLinkedShadowDrainAsync());
		}
	}

	private async Task ApplyLinkedShadowDrainAsync()
	{
		// Yield once so all simultaneous BeforeRemovedFromRoom calls can accumulate
		// their drain amounts before we apply the total.
		await Task.Yield();

		int totalDrain = _pendingLinkedShadowDrainHp;
		_pendingLinkedShadowDrainHp = 0;
		_linkedShadowDrainScheduled = false;

		Creature? archCreature = ((MonsterModel)this).Creature;
		if (archCreature == null || !archCreature.IsAlive || totalDrain <= 0) return;

		int nextHp = Math.Max(1, archCreature.CurrentHp - totalDrain);
		await CreatureCmd.SetCurrentHp(archCreature, (decimal)nextHp);

		// Show floating damage number identical to card-damage display.
		NDamageNumVfx? dmgVfx = NDamageNumVfx.Create(archCreature, totalDrain, requireInteractable: false);
		if (dmgVfx != null)
			NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(dmgVfx);

		// Play Architect's hit animation and screen shake.
		await CreatureCmd.TriggerAnim(archCreature, "Hit", 0f);
		NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short, 180f);
	}

	/// EN: Queue the next summon intent when HP crosses a phase threshold.
	/// ZH: 当生命跨过阈值时排定下一次召唤意图。
	private bool TryScheduleSummonIntent()
	{
		if (IsPhaseFour)
		{
			return false;
		}
		if (!IsPhaseTwo && !IsPhaseThree && !HasTriggeredPhaseOneSummon && ((MonsterModel)this).Creature.CurrentHp <= ((MonsterModel)this).Creature.MaxHp * 4 / 5)
		{
			LogArchitect($"TryScheduleSummonIntent:phase1 hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp}");
			HasTriggeredPhaseOneSummon = true;
			if (_phaseTwoSummonState != null)
			{
				SetPlannedMoveImmediate(_phaseTwoSummonState, true);
			}
			return true;
		}
		if (IsPhaseThree)
		{
			if (!HasTriggeredPhaseThreeSummon && ((MonsterModel)this).Creature.CurrentHp <= ((MonsterModel)this).Creature.MaxHp * 4 / 5)
			{
				LogArchitect($"TryScheduleSummonIntent:phase3-first hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp}");
				HasTriggeredPhaseThreeSummon = true;
				if (_phaseThreeSummonState != null)
				{
					SetPlannedMoveImmediate(_phaseThreeSummonState, true);
				}
				return true;
			}
		}
		else if (IsPhaseTwo && !HasTriggeredPhaseTwoSummon && ((MonsterModel)this).Creature.CurrentHp <= ((MonsterModel)this).Creature.MaxHp * 4 / 5)
		{
			LogArchitect($"TryScheduleSummonIntent:phase2 hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp}");
			HasTriggeredPhaseTwoSummon = true;
			if (_phaseTwoSummonState != null)
			{
				SetPlannedMoveImmediate(_phaseTwoSummonState, true);
			}
			return true;
		}
		return false;
	}

	/// EN: Fire an immediate summon if damage skips past the normal intent timing.
	/// ZH: 若伤害直接越过正常意图时机，则立即触发召唤。
	private async Task TryTriggerImmediatePhaseSummonAsync()
	{
		if (((MonsterModel)this).Creature.IsDead || IsAwaitingPhaseTransition || IsPhaseFour)
		{
			return;
		}
		if (!IsPhaseTwo && !IsPhaseThree)
		{
			if (!HasTriggeredPhaseOneSummon && ((MonsterModel)this).Creature.CurrentHp <= ((MonsterModel)this).Creature.MaxHp * 4 / 5)
			{
				HasTriggeredPhaseOneSummon = true;
				Act4AudioHelper.PlayTmp("doom_apply.mp3");
				await SummonPhaseOneBotsAsync();
			}
			return;
		}
		if (IsPhaseTwo && !HasTriggeredPhaseTwoSummon && ((MonsterModel)this).Creature.CurrentHp <= ((MonsterModel)this).Creature.MaxHp * 4 / 5)
		{
			HasTriggeredPhaseTwoSummon = true;
			Act4AudioHelper.PlayTmp("doom_apply.mp3");
			await SummonPhaseTwoKnightsAsync();
			return;
		}
		if (IsPhaseThree && !HasTriggeredPhaseThreeSummon && ((MonsterModel)this).Creature.CurrentHp <= ((MonsterModel)this).Creature.MaxHp * 4 / 5)
		{
			HasTriggeredPhaseThreeSummon = true;
			ShowArchitectSpeech("Does this shadow feel familiar?", VfxColor.Black, 3.2);
			Act4AudioHelper.PlayTmp("doom_apply.mp3");
			await SummonPhaseThreeShadowsAsync(2);
			_pendingSummonLinkedThornsSync = true;
		}
	}

	private int GetCurrentSummonedShadowCount()
	{
		return CombatState.Enemies.Count(c => c != ((MonsterModel)this).Creature && c.IsAlive && c.Monster is ArchitectShadowChampion);
	}

	internal async Task SyncSummonLinkedThornsAsync()
	{
		if (((MonsterModel)this).Creature.CombatState == null)
		{
			return;
		}
		if (!IsPhaseThree)
		{
			_pendingSummonLinkedThornsSync = false;
			return;
		}
		// Shadow Book: suppress the Architect's Shadow Thorns entirely.
		if (IsShadowBookChosen())
		{
			if (((MonsterModel)this).Creature.GetPower<ArchitectSummonThornsPower>() != null)
				await PowerCmd.Remove<ArchitectSummonThornsPower>(((MonsterModel)this).Creature);
			if (((MonsterModel)this).Creature.GetPower<ThornsPower>() != null)
				await PowerCmd.Remove<ThornsPower>(((MonsterModel)this).Creature);
			_hasPersistentSummonThorns = false;
			_persistentSummonThornsAmount = 0;
			_pendingSummonLinkedThornsSync = false;
			return;
		}
		int desiredAmount = (IsPhaseThree && GetCurrentSummonedShadowCount() > 0) ? GetSummonLinkedThornsAmount() : 0;
		if (desiredAmount == 0 && !_hasPersistentSummonThorns && ((MonsterModel)this).Creature.GetPower<ThornsPower>() != null)
		{
			await PowerCmd.Remove<ThornsPower>(((MonsterModel)this).Creature);
		}
		if (_hasPersistentSummonThorns && (_persistentSummonThornsAmount != desiredAmount || desiredAmount == 0))
		{
			if (((MonsterModel)this).Creature.GetPower<ArchitectSummonThornsPower>() != null)
			{
				await PowerCmd.Remove<ArchitectSummonThornsPower>(((MonsterModel)this).Creature);
			}
			if (((MonsterModel)this).Creature.GetPower<ThornsPower>() != null)
			{
				await PowerCmd.Remove<ThornsPower>(((MonsterModel)this).Creature);
			}
			_hasPersistentSummonThorns = false;
			_persistentSummonThornsAmount = 0;
		}
		if (desiredAmount > 0 && !_hasPersistentSummonThorns)
		{
			if (((MonsterModel)this).Creature.GetPower<ThornsPower>() != null)
			{
				await PowerCmd.Remove<ThornsPower>(((MonsterModel)this).Creature);
			}
			await PowerCmd.Apply<ArchitectSummonThornsPower>(((MonsterModel)this).Creature, (decimal)desiredAmount, ((MonsterModel)this).Creature, (CardModel)null, false);
			_hasPersistentSummonThorns = true;
			_persistentSummonThornsAmount = desiredAmount;
		}
		_pendingSummonLinkedThornsSync = false;
	}

	/// EN: Finish the run once the true final phase death is confirmed.
	/// ZH: 在最终阶段真实死亡确认后结束本次通关。
	private void QueuePhaseFourFinishRun(string source)
	{
		if (_phaseFourFinishRunQueued || !IsPhaseFour || RunManager.Instance == null)
		{
			return;
		}
		_phaseFourFinishRunQueued = true;
		Act4AudioHelper.StopModBgm();
		LogArchitect($"QueuePhaseFourFinishRun source={source}");
		TaskHelper.RunSafely(ModSupport.FinishRunAfterAct4BossAsync(RunManager.Instance));
	}

	private int GetSummonLinkedThornsAmount()
	{
		return ModSupport.IsBrutalAct4(GetRunState()) ? 7 : 5;
	}

	/// EN: Refresh the preview powers that hint the next attack pattern.
	/// ZH: 刷新用于提示下一类攻击模式的预览能力。
	private async Task RefreshPreAttackTrackerAsync()
	{
		await RemovePreAttackTrackerPowersAsync();
		if (IsAwaitingPhaseTransition || PhaseNumber == 2 || !((MonsterModel)this).IntendsToAttack)
		{
			LogArchitect("RefreshPreAttackTracker:inactive");
			return;
		}
		// Cursed Tome: Architect does not gain Aggressive or Tactical Readings.
		if (IsCursedBookChosen())
		{
			LogArchitect("RefreshPreAttackTracker:cursed-suppressed");
			return;
		}
		bool isMultiIntent = ((MonsterModel)this).NextMove?.Intents?.Any((AbstractIntent intent) => intent is MultiAttackIntent || intent is DynamicMultiAttackIntent) ?? false;
		bool isAttackIntent = ((MonsterModel)this).NextMove?.Intents?.Any((AbstractIntent intent) => intent is SingleAttackIntent || intent is MultiAttackIntent || intent is DynamicMultiAttackIntent) ?? false;
		if (!isAttackIntent)
		{
			LogArchitect("RefreshPreAttackTracker:non-attack");
			return;
		}
		if (isMultiIntent)
		{
			await PowerCmd.Apply<ArchitectAttackReadingsPower>(((MonsterModel)this).Creature, 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
			LogArchitect("RefreshPreAttackTracker:applied-attack");
		}
		else
		{
			await PowerCmd.Apply<ArchitectSkillReadingsPower>(((MonsterModel)this).Creature, 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
			LogArchitect("RefreshPreAttackTracker:applied-skill");
		}
	}

	/// EN: Force-end the player turn and arm the retaliation follow-up.
	/// ZH: 强制结束玩家回合，并布置后续反击效果。
	private async Task TryTriggerRetaliationAsync()
	{
		_hasQueuedRetaliationActionRequested = false;
		CombatManager instance = CombatManager.Instance;
		if (_isRetaliationEndingTurn || IsAwaitingPhaseTransition || ((MonsterModel)this).Creature.IsDead || !instance.IsPlayPhase || instance.EndingPlayerTurnPhaseOne || instance.EndingPlayerTurnPhaseTwo)
		{
			return;
		}
		ArchitectRetaliationPower retaliationPower = ((MonsterModel)this).Creature.GetPower<ArchitectRetaliationPower>();
		if (retaliationPower == null || ((MonsterModel)this).Creature.CurrentHp > ((MonsterModel)this).Creature.MaxHp / 2)
		{
			return;
		}
		if (HasTriggeredRetaliationThisPhase())
		{
			return;
		}
		_isRetaliationEndingTurn = true;
		SetRetaliationTriggeredThisPhase();
		await SyncRetaliationCounterAsync();
		// Cancel all card play FIRST before arming moves so remaining multi-hit
		// damage or queued co-op cards are stopped before they can race with the
		// phase transition.
		Act4AudioHelper.PlayTmp("enemy_turn.mp3");
		ShowArchitectSpeech("Enough.\nYour turn is over.", IsPhaseThree ? VfxColor.Black : (IsPhaseTwo ? VfxColor.Purple : VfxColor.Blue), 3f);
		NPowerUpVfx.CreateGhostly(((MonsterModel)this).Creature);
		NPlayerHand.Instance?.CancelAllCardPlay();
		// Refresh card indices after cancel so any card stuck mid-play (e.g. Prepared's
		// discard picker interrupted by retaliation) snaps back to its correct position.
		NPlayerHand.Instance?.ForceRefreshCardIndices();
		// Force-close any open card selection overlay (e.g. Prepared's discard picker)
		// to prevent soft-lock when retaliation ends the turn mid-selection.
		NOverlayStack.Instance?.Clear();
		foreach (Player player in ((MonsterModel)this).CombatState.Players)
		{
			if (player?.Creature != null && player.Creature.IsAlive && !instance.IsPlayerReadyToEndTurn(player))
			{
				PlayerCmd.EndTurn(player, canBackOut: false);
			}
		}
		// Watchdog: if the turn never advances (e.g. a dropped EndTurn packet in multiplayer),
		// clear the stale flag after 10 s so future retaliation checks are not permanently blocked.
		TaskHelper.RunSafely(RetaliationEndingTurnTimeoutAsync());
		// After cancelling cards and ending turns, the Architect may have died
		// from remaining multi-hit damage that resolved before the cancel took
		// effect.  If a phase transition is now pending, skip arming retaliation
		//  -  the ReviveMove will handle the next phase cleanly.
		if (IsAwaitingPhaseTransition || ((MonsterModel)this).Creature.IsDead)
		{
			LogArchitect($"RetaliationTriggered:aborted-phase-transition phase={PhaseNumber} pending={PendingPhaseNumber}");
			return;
		}
		if (PhaseNumber == 2)
		{
			_armPhaseTwoAllOrNothingOnEnemyTurnStart = true;
			if (_phaseTwoRetaliationMultiState != null)
			{
				SetPlannedMoveImmediate(_phaseTwoRetaliationMultiState, true);
			}
		}
		else if (PhaseNumber == 3)
		{
			_armPhaseThreeJudgmentOnEnemyTurnStart = true;
		}
		LogArchitect($"RetaliationTriggered phase={PhaseNumber} hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp}");
	}

	/// EN: Watchdog that clears the retaliation guard flag if the player turn never advances within 10 s.
	///     Handles the rare case where a PlayerCmd.EndTurn packet is dropped in multiplayer, leaving
	///     _isRetaliationEndingTurn permanently true and blocking any future retaliation triggers.
	/// ZH: 若玩家回合在10秒内未推进，則清除反击守卫标志的监视任务。用于处理联机中 EndTurn 数据包丢失导致标志永久卡死的罕见问题。
	private async Task RetaliationEndingTurnTimeoutAsync()
	{
		await Cmd.Wait(10f, false);
		if (!_isRetaliationEndingTurn) return; // Cleared normally  -  no action needed.
		LogArchitect("RetaliationTimeout:stale-flag-cleared; forcing EndTurn retry");
		_isRetaliationEndingTurn = false;
		CombatManager combatMgr = CombatManager.Instance;
		if (combatMgr != null && combatMgr.IsPlayPhase && !combatMgr.EndingPlayerTurnPhaseOne && !combatMgr.EndingPlayerTurnPhaseTwo)
		{
			NOverlayStack.Instance?.Clear();
			foreach (Player player in ((MonsterModel)this).CombatState?.Players ?? Enumerable.Empty<Player>())
			{
				if (player?.Creature?.IsAlive == true && !combatMgr.IsPlayerReadyToEndTurn(player))
					PlayerCmd.EndTurn(player, canBackOut: false);
			}
		}
	}

	private int GetPhaseTwoAllOrNothingThresholdPercent()
	{
		int basePercent = (_lastCompletedPlayerRoundDamagePercent > 0) ? _lastCompletedPlayerRoundDamagePercent : 20;
		return Math.Clamp(basePercent, 10, 20);
	}

	/// EN: Arm the phase 2 damage-check retaliation test.
	/// ZH: 启动二阶段的伤害检定反击机制。
	private async Task ArmPhaseTwoAllOrNothingAsync()
	{
		if (!IsPhaseTwo || IsAwaitingPhaseTransition || ((MonsterModel)this).Creature.IsDead)
		{
			return;
		}
		int thresholdPercent = GetPhaseTwoAllOrNothingThresholdPercent();
		_phaseTwoAllOrNothingThresholdPercent = thresholdPercent;
		_phaseTwoAllOrNothingShouldStun = false;
		_currentPlayerRoundDamageTaken = 0;
		await PowerCmd.SetAmount<ArchitectAllOrNothingPower>(((MonsterModel)this).Creature, (decimal)thresholdPercent, ((MonsterModel)this).Creature, (CardModel)null);
		if (_phaseTwoRetaliationMultiState != null)
		{
			SetPlannedMoveImmediate(_phaseTwoRetaliationMultiState, true);
			await RefreshPreAttackTrackerAsync();
		}
		ShowArchitectSpeech("All or nothing.", VfxColor.Purple, 2.8);
	}

	/// EN: Resolve whether phase 2 retaliation becomes stun or vigor.
	/// ZH: 结算二阶段反击是转为眩晕还是活力奖励。
	private async Task ResolvePhaseTwoAllOrNothingAsync()
	{
		if (!IsPhaseTwo || IsPhaseThree || IsPhaseFour)
		{
			return;
		}
		ArchitectAllOrNothingPower allOrNothingPower = ((MonsterModel)this).Creature.GetPower<ArchitectAllOrNothingPower>();
		if (allOrNothingPower == null && !_phaseTwoAllOrNothingShouldStun)
		{
			return;
		}
		int thresholdPercent = Math.Max(1, _phaseTwoAllOrNothingThresholdPercent);
		int damagePercent = (int)Math.Ceiling((double)(_currentPlayerRoundDamageTaken * 100m / Math.Max(1m, (decimal)((MonsterModel)this).Creature.MaxHp)));
		if (allOrNothingPower != null)
		{
			await PowerCmd.Remove<ArchitectAllOrNothingPower>(((MonsterModel)this).Creature);
		}
		bool shouldStun = _phaseTwoAllOrNothingShouldStun || damagePercent > thresholdPercent;
		_phaseTwoAllOrNothingShouldStun = false;
		if (shouldStun)
		{
			await PowerCmd.SetAmount<ArchitectStunnedPower>(((MonsterModel)this).Creature, 1m, ((MonsterModel)this).Creature, (CardModel)null);
			await CreatureCmd.Stun(((MonsterModel)this).Creature, PhaseTwoStunnedMove, "PHASE_TWO_RANDOM");
			ShowArchitectSpeech("Too much.", VfxColor.Purple, 2.4);
			LogArchitect($"AllOrNothing:stunned damagePercent={damagePercent} thresholdPercent={thresholdPercent}");
			return;
		}
		if (_currentPlayerRoundDamageTaken > 0)
		{
			await PowerCmd.Apply<VigorPower>(((MonsterModel)this).Creature, 50m, ((MonsterModel)this).Creature, (CardModel)null, false);
			NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
			ShowArchitectSpeech("Then break.", VfxColor.Purple, 2.4);
			LogArchitect($"AllOrNothing:vigor damagePercent={damagePercent} thresholdPercent={thresholdPercent}");
		}
	}

	/// EN: Arm the phase 3 judgment check for the next enemy turn.
	/// ZH: 启动三阶段下个敌方回合要结算的审判机制。
	private async Task ArmPhaseThreeJudgmentAsync()
	{
		if (PhaseNumber != 3 || IsAwaitingPhaseTransition || ((MonsterModel)this).Creature.IsDead)
		{
			return;
		}
		_phaseThreeJudgmentWasAttackedByCard = false;
		_phaseThreeJudgmentTriggered = false;
		_phaseThreeJudgmentTriggeredAttackers.Clear();
		_currentPlayerRoundDamageTaken = 0;
		await PowerCmd.SetAmount<ArchitectJudgmentPower>(((MonsterModel)this).Creature, 1m, ((MonsterModel)this).Creature, (CardModel)null);
		EnsurePhaseThreeJudgmentAuraVfx();
		ShowArchitectSpeech("Judgment.", VfxColor.Black, 2.8);
	}

	/// EN: Resolve phase 3 judgment and self-stun if players triggered it.
	/// ZH: 结算三阶段审判；若被触发则自伤并自眩晕。
	private async Task ResolvePhaseThreeJudgmentAsync()
	{
		if (PhaseNumber != 3 || IsPhaseFour || IsAwaitingPhaseTransition || ((MonsterModel)this).Creature.IsDead || ((MonsterModel)this).Creature.CombatState?.CurrentSide != CombatSide.Enemy)
		{
			return;
		}
		ArchitectJudgmentPower judgmentPower = ((MonsterModel)this).Creature.GetPower<ArchitectJudgmentPower>();
		if (judgmentPower == null && !_phaseThreeJudgmentTriggered)
		{
			RemovePhaseThreeJudgmentAuraVfx();
			return;
		}
		bool wasAttackedByCard = _phaseThreeJudgmentWasAttackedByCard || _phaseThreeJudgmentTriggered;
		_phaseThreeJudgmentWasAttackedByCard = false;
		_phaseThreeJudgmentTriggered = false;
		if (judgmentPower != null)
		{
			await PowerCmd.Remove<ArchitectJudgmentPower>(((MonsterModel)this).Creature);
		}
		RemovePhaseThreeJudgmentAuraVfx();
		if (!wasAttackedByCard)
		{
			return;
		}
		int selfDamage = Math.Max(1, (int)Math.Ceiling((decimal)((MonsterModel)this).Creature.MaxHp * 0.1m));
		if (IsAwaitingPhaseTransition || ((MonsterModel)this).Creature.IsDead)
		{
			return;
		}
		await CreatureCmd.SetCurrentHp(((MonsterModel)this).Creature, Math.Max(1m, ((MonsterModel)this).Creature.CurrentHp - selfDamage));
		if (IsAwaitingPhaseTransition || ((MonsterModel)this).Creature.IsDead)
		{
			return;
		}
		await PowerCmd.SetAmount<ArchitectStunnedPower>(((MonsterModel)this).Creature, 1m, ((MonsterModel)this).Creature, (CardModel)null);
		await CreatureCmd.Stun(((MonsterModel)this).Creature, PhaseThreeStunnedMove, "PHASE_THREE_RANDOM");
		ShowArchitectSpeech("Judgment falls.", VfxColor.Black, 2.8);
		LogArchitect($"Judgment:resolved damageTaken={_currentPlayerRoundDamageTaken} selfDamage={selfDamage}");
	}

	private async Task CleansePlayerBuffsAsync(Player player)
	{
		Creature creature = player?.Creature;
		if (creature == null)
		{
			return;
		}
		List<PowerModel> list = creature.Powers.Where((PowerModel power) => power != null && power.TypeForCurrentAmount == PowerType.Buff && power.GetType() != typeof(CursedTomePlayerBonusPower)).ToList();
		foreach (PowerModel item in list)
		{
			await PowerCmd.Remove(item);
		}
	}

	private async Task UpdateRetaliationResolutionStatusAsync(DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (((MonsterModel)this).Creature.CombatState?.CurrentSide != CombatSide.Player || PhaseNumber != 2)
		{
			bool wasPlayerAttackHit = dealer?.Player != null && props.HasFlag(ValueProp.Move) && !props.HasFlag(ValueProp.Unpowered) && (result.UnblockedDamage > 0 || result.BlockedDamage > 0) && (result.UnblockedDamage + result.BlockedDamage) >= (decimal)((MonsterModel)this).Creature.MaxHp * 0.05m;
			if (((MonsterModel)this).Creature.CombatState?.CurrentSide == CombatSide.Player && PhaseNumber == 3 && ((MonsterModel)this).Creature.GetPower<ArchitectJudgmentPower>() != null && wasPlayerAttackHit)
			{
				_phaseThreeJudgmentWasAttackedByCard = true;
				if (dealer?.Player != null && _phaseThreeJudgmentTriggeredAttackers.Add(dealer.Player.NetId))
				{
					await CleansePlayerBuffsAsync(dealer.Player);
				}
				if (!_phaseThreeJudgmentTriggered)
				{
					_phaseThreeJudgmentTriggered = true;
				}
			}
			return;
		}
		if (((MonsterModel)this).Creature.GetPower<ArchitectAllOrNothingPower>() == null)
		{
			return;
		}
		int damagePercent = (int)Math.Ceiling((double)(_currentPlayerRoundDamageTaken * 100m / Math.Max(1m, (decimal)((MonsterModel)this).Creature.MaxHp)));
		_phaseTwoAllOrNothingShouldStun = damagePercent > _phaseTwoAllOrNothingThresholdPercent;
		int remainingThreshold = _phaseTwoAllOrNothingShouldStun ? 1 : Math.Max(1, _phaseTwoAllOrNothingThresholdPercent - damagePercent);
		await PowerCmd.SetAmount<ArchitectAllOrNothingPower>(((MonsterModel)this).Creature, (decimal)remainingThreshold, ((MonsterModel)this).Creature, (CardModel)null);
		if (_phaseTwoAllOrNothingShouldStun && ((MonsterModel)this).Creature.GetPower<ArchitectStunnedPower>() == null)
		{
			await PowerCmd.SetAmount<ArchitectStunnedPower>(((MonsterModel)this).Creature, 1m, ((MonsterModel)this).Creature, (CardModel)null);
		}
	}

	private bool ShouldQueueRetaliationForNextPlayerTurn()
	{
		if (_hasQueuedRetaliationForNextPlayerTurn || HasTriggeredRetaliationThisPhase() || ((MonsterModel)this).Creature.IsDead || ((MonsterModel)this).Creature.CurrentHp > ((MonsterModel)this).Creature.MaxHp / 2)
		{
			return false;
		}
		CombatState combatState = ((MonsterModel)this).Creature.CombatState;
		if (combatState == null || combatState.CurrentSide != CombatSide.Enemy)
		{
			return false;
		}
		return ((MonsterModel)this).Creature.GetPower<ArchitectRetaliationPower>() != null;
	}

	/// EN: Keep the retaliation counter power synced to current HP thresholds.
	/// ZH: 让反击计数能力与当前生命阈值保持同步。
	private async Task SyncRetaliationCounterAsync()
	{
		// Phase 1 retaliation removed: it is a short phase and the 50% HP retaliation is not needed.
		if (((MonsterModel)this).Creature.IsDead || IsPhaseFour || PhaseNumber == 1)
		{
			if (((MonsterModel)this).Creature.GetPower<ArchitectRetaliationPower>() != null)
			{
				await PowerCmd.Remove<ArchitectRetaliationPower>(((MonsterModel)this).Creature);
			}
			return;
		}
		int hpPercent = (int)Math.Ceiling((double)(((MonsterModel)this).Creature.CurrentHp * 100m / Math.Max(1m, (decimal)((MonsterModel)this).Creature.MaxHp)));
		hpPercent = Math.Clamp(hpPercent, 1, 99);
		await PowerCmd.SetAmount<ArchitectRetaliationPower>(((MonsterModel)this).Creature, (decimal)hpPercent, ((MonsterModel)this).Creature, (CardModel)null);
	}

	private int GetAdaptiveResistancePercent()
	{
		if (!ShouldApplyAdaptiveResistance())
		{
			return 0;
		}
		// Base expulsion %: 20/40/60/80 for phases 1–4.
		int num = PhaseNumber switch
		{
			1 => 20,
			2 => 40,
			3 => 60,
			_ => 80
		};
		if (ModSupport.IsBrutalAct4(GetRunState()))
		{
			num += 20;
		}
		// Holy Tome: halves the expulsion % (e.g. Phase 3 60→30, Phase 4 80→40).
		if (IsHolyBookChosen())
		{
			num /= 2;
		}
		return Math.Clamp(num, 0, 100);
	}

	/// EN: Adaptive Resistance is active in all four phases.
	/// ZH: 「自适应抗性」在全部四个阶段均生效。
	private bool ShouldApplyAdaptiveResistance() => true;

	/// EN: Sync or clear the adaptive resistance display power.
	/// ZH: 同步或移除“适应抗性”的显示能力。
	private async Task SyncAdaptiveResistancePowerAsync()
	{
		if (!ShouldApplyAdaptiveResistance())
		{
			if (((MonsterModel)this).Creature.GetPower<ArchitectAdaptiveResistancePower>() != null)
			{
				await PowerCmd.Remove<ArchitectAdaptiveResistancePower>(((MonsterModel)this).Creature);
			}
			return;
		}
		await PowerCmd.SetAmount<ArchitectAdaptiveResistancePower>(((MonsterModel)this).Creature, (decimal)GetAdaptiveResistancePercent(), ((MonsterModel)this).Creature, (CardModel)null);
	}

	private int GetAdaptiveDebuffClearPercent()
	{
		return IsHolyBookChosen() ? 15 : 30;
	}

	//=============================================================================
	// EN: Turn-start debuff trim for Architect.
	//     Floor the reduction and keep at least 1 stack when reduction happens.
	//     Tiny stacks survive; giant piles calm down.
	// ZH: 建筑师回合开始时的减益裁剪。
	//     向下取整；发生削减时至少保留1层。
	//     小层数基本不动，大层数会被温和压制。
	//=============================================================================
	private async Task ClearArchitectDebuffsAtTurnStartAsync()
	{
		int clearPct = GetAdaptiveDebuffClearPercent();
		if (clearPct <= 0) return;
		List<PowerModel> debuffs = ((MonsterModel)this).Creature.Powers
			.Where(power => power != null && power.TypeForCurrentAmount == PowerType.Debuff && power.Amount > 0)
			.ToList();
		foreach (PowerModel debuff in debuffs)
		{
			int current = debuff.Amount;
			int clear = (int)Math.Floor(current * clearPct / 100.0);
			if (clear <= 0) continue;
			int next = Math.Max(1, current - clear);
			debuff.SetAmount(next);
			LogArchitect($"AdaptiveResistance:turn-clear debuff={debuff.GetType().Name} from={current} to={next} pct={clearPct}");
		}
	}

	private async Task ApplyAdaptiveResistanceAsync()
	{
		// EN: Start-of-turn maintenance lives here instead of inside the display power because order matters.
		//     Poison and Doom should tick first, then we clean up negative Strength and trim debuffs.
		//     Doing it earlier makes debuff builds feel like they got taxed twice for touching the boss.
		// ZH: 回合开始的维护逻辑放在这里，而不是塞进展示用 power 里，因为顺序很关键。
		//     先让毒和厄运跳伤，再处理负力量和减益裁层，不然减益流会有被多收一次税的感觉。
		await SyncAdaptiveResistancePowerAsync();
		if (!ShouldApplyAdaptiveResistance())
		{
			return;
		}
		StrengthPower strengthPower = ((MonsterModel)this).Creature.GetPower<StrengthPower>();
		int currentStrength = (strengthPower != null) ? ((PowerModel)strengthPower).Amount : 0;
		if (currentStrength < 0 && !IsHolyBookChosen())
		{
			await TransferNegativeStrengthToPlayersAsync(currentStrength);
			await ClearTemporaryNegativeStrengthPowersAsync();
			await SetStrengthAmountAsync(1);
			LogArchitect($"AdaptiveResistance:transferred-negative-strength amount={currentStrength} resetTo=1");
		}
		else if (currentStrength < 0 && IsHolyBookChosen())
		{
			// Holy Tome: negative Strength is NOT reset to 1 and NOT transferred to players.
			// Instead, reduce the magnitude by the adaptive resistance % each enemy turn,
			// slowly walking it back toward 0. e.g. -13 at 30% → -13 + ceil(13×0.30) = -9.
			int holyResistPct = GetAdaptiveResistancePercent();
			int reduction = (int)Math.Ceiling(Math.Abs(currentStrength) * holyResistPct / 100.0);
			int newStrength = Math.Min(0, currentStrength + reduction); // approach 0, never go positive
			if (newStrength != currentStrength)
			{
				await SetStrengthAmountAsync(newStrength);
				LogArchitect($"AdaptiveResistance:holy-tome-reduced-negative-strength from={currentStrength} to={newStrength} pct={holyResistPct}");
			}
		}

		// Start-of-turn debuff decay (after poison/doom owner-start ticks resolve).
		await Cmd.Wait(0.1f, false);
		await ClearArchitectDebuffsAtTurnStartAsync();
	}

	public override Task AfterPowerAmountChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
	{
		if (amount <= 0 || (power is not PoisonPower && power is not DoomPower))
			return Task.CompletedTask;
		if (power.Owner != ((MonsterModel)this).Creature || IsAwaitingPhaseTransition || ((MonsterModel)this).Creature.IsDead)
			return Task.CompletedTask;
		int addedStacks = Math.Max(0, (int)Math.Floor(amount));
		if (addedStacks > 0)
			TaskHelper.RunSafely(ImmediateAdaptiveResistanceAsync(power, addedStacks));
		return Task.CompletedTask;
	}

	private async Task ImmediateAdaptiveResistanceAsync(PowerModel changedPower, int addedStacks)
	{
		// EN: The design rule here is "only bite the new stacks".
		//     If the player applies 20 Poison twice, the second application should only expel
		//     part of that second 20, not reach back and re-tax the whole pile already sitting there.
		// ZH: 这里最重要的设计规则是“只咬新增层数”。
		//     连续上两次 20 层毒时，第二次只能处理第二批新上的层数，不能回头把旧层数再抽一遍。
		if (!ShouldApplyAdaptiveResistance() || ((MonsterModel)this).Creature.IsDead || IsAwaitingPhaseTransition)
			return;

		int pct = GetAdaptiveResistancePercent();
		if (pct <= 0) return;
		decimal capPct = IsHolyBookChosen() ? 0.50m : 0.25m;
		int maxDamage = Math.Max(1, (int)Math.Ceiling((decimal)((MonsterModel)this).Creature.MaxHp * capPct));
		int expelled = (int)Math.Floor(addedStacks * pct / 100.0);
		expelled = Math.Min(expelled, maxDamage);
		if (expelled <= 0) return;
		int currentStacks = changedPower.Amount;
		expelled = Math.Min(expelled, currentStacks);
		if (expelled <= 0) return;

		int newStacks = currentStacks - expelled;
		if (changedPower is PoisonPower)
		{
			if (newStacks <= 0) await PowerCmd.Remove<PoisonPower>(((MonsterModel)this).Creature);
			else changedPower.SetAmount(newStacks);
			LogArchitect($"ImmediateResistance:expelled-poison added={addedStacks} expelled={expelled} now={Math.Max(0, newStacks)}");
		}
		else if (changedPower is DoomPower)
		{
			if (newStacks <= 0) await PowerCmd.Remove<DoomPower>(((MonsterModel)this).Creature);
			else changedPower.SetAmount(newStacks);
			LogArchitect($"ImmediateResistance:expelled-doom added={addedStacks} expelled={expelled} now={Math.Max(0, newStacks)}");
		}

		if (!((MonsterModel)this).Creature.IsDead && !IsAwaitingPhaseTransition)
		{
			int currentHp = ((MonsterModel)this).Creature.CurrentHp;
			int newHp = Math.Max(1, currentHp - expelled);
			await CreatureCmd.SetCurrentHp(((MonsterModel)this).Creature, (decimal)newHp);
			await Cmd.Wait(0.15f, false);
			NDamageNumVfx? immediateResistDmgVfx = NDamageNumVfx.Create(((MonsterModel)this).Creature, expelled, requireInteractable: false);
			if (immediateResistDmgVfx != null)
				NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(immediateResistDmgVfx);
			LogArchitect($"ImmediateResistance:self-damage expelled={expelled} hp={currentHp}→{newHp}");
		}
	}

	private async Task TransferNegativeStrengthToPlayersAsync(int transferAmount)
	{
		if (transferAmount >= 0 || ((MonsterModel)this).CombatState == null)
		{
			return;
		}
		int num = Math.Abs(transferAmount);
		if (num <= 0)
		{
			return;
		}
		await PowerCmd.Apply<PiercingWailPower>(CombatState.Players.Select(p => p.Creature), (decimal)num, ((MonsterModel)this).Creature, (CardModel)null, false);
	}

	private async Task ClearTemporaryNegativeStrengthPowersAsync()
	{
		List<TemporaryStrengthPower> list = ((MonsterModel)this).Creature.Powers.OfType<TemporaryStrengthPower>().Where((TemporaryStrengthPower power) => power != null && power.TypeForCurrentAmount == PowerType.Debuff).ToList();
		foreach (TemporaryStrengthPower item in list)
		{
			await PowerCmd.Remove(item);
		}
	}

	private bool HasTriggeredRetaliationThisPhase()
	{
		if (IsPhaseFour)
		{
			return true;
		}
		if (IsPhaseThree)
		{
			return HasTriggeredPhaseThreeRetaliation;
		}
		if (IsPhaseTwo)
		{
			return HasTriggeredPhaseTwoRetaliation;
		}
		return HasTriggeredPhaseOneRetaliation;
	}

	private void SetRetaliationTriggeredThisPhase()
	{
		if (IsPhaseFour)
		{
			return;
		}
		if (IsPhaseThree)
		{
			HasTriggeredPhaseThreeRetaliation = true;
		}
		else if (IsPhaseTwo)
		{
			HasTriggeredPhaseTwoRetaliation = true;
		}
		else
		{
			HasTriggeredPhaseOneRetaliation = true;
		}
	}

	private async Task TryTriggerEmergencyFogmogAsync()
	{
		if (((MonsterModel)this).Creature.IsDead || IsAwaitingPhaseTransition || IsPhaseFour || ((MonsterModel)this).Creature.CurrentHp > ((MonsterModel)this).Creature.MaxHp * 3 / 10)
		{
			return;
		}
		if (IsPhaseThree)
		{
			if (HasTriggeredPhaseThreeEmergencyFogmog)
			{
				return;
			}
			HasTriggeredPhaseThreeEmergencyFogmog = true;
		}
		else if (IsPhaseTwo)
		{
			if (HasTriggeredPhaseTwoEmergencyFogmog)
			{
				return;
			}
			HasTriggeredPhaseTwoEmergencyFogmog = true;
		}
		else
		{
			if (HasTriggeredPhaseOneEmergencyFogmog)
			{
				return;
			}
			HasTriggeredPhaseOneEmergencyFogmog = true;
		}
		if (GetCurrentActiveSummonCount() >= 2)
		{
			return;
		}
		ShowArchitectSpeech("The spores will finish this.", IsPhaseThree ? VfxColor.Black : VfxColor.Purple, 2.8);
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
		Act4AudioHelper.PlayTmp("doom_apply.mp3");
		SfxCmd.Play("event:/sfx/enemy/enemy_attacks/fogmog/fogmog_summon");
		await SummonSpecificMinionAsync(((MonsterModel)ModelDb.Monster<ArchitectSummonedFogmog>()).ToMutable());
		_pendingSummonLinkedThornsSync = true;
		LogArchitect($"EmergencyFogmogTriggered phase={PhaseNumber} hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp} summons={GetCurrentActiveSummonCount()}");
	}

	private void ApplyArchitectBounds(NCreature creatureNode)
	{
		Control bounds = creatureNode.Visuals?.GetNodeOrNull<Control>("Bounds");
		Marker2D intentPos = creatureNode.Visuals?.GetNodeOrNull<Marker2D>("IntentPos");
		if (bounds != null)
		{
			bounds.Position = new Vector2(-236f, -517f);
			bounds.Size = new Vector2(403f, 517f);
		}
		if (intentPos != null)
		{
			intentPos.Position = new Vector2(intentPos.Position.X, -540f);
		}
		creatureNode.Hitbox.Position = new Vector2(-236f, -517f);
		creatureNode.Hitbox.Size = new Vector2(403f, 517f);
	}

	private async Task RemovePreAttackTrackerPowersAsync()
	{
		if (((MonsterModel)this).Creature.GetPower<ArchitectAttackReadingsPower>() != null)
		{
			await PowerCmd.Remove<ArchitectAttackReadingsPower>(((MonsterModel)this).Creature);
		}
		if (((MonsterModel)this).Creature.GetPower<ArchitectSkillReadingsPower>() != null)
		{
			await PowerCmd.Remove<ArchitectSkillReadingsPower>(((MonsterModel)this).Creature);
		}
	}

	private async Task SetStrengthAmountAsync(int amount)
	{
		StrengthPower strengthPower = ((MonsterModel)this).Creature.GetPower<StrengthPower>();
		if (strengthPower != null)
		{
			await PowerCmd.SetAmount<StrengthPower>(((MonsterModel)this).Creature, (decimal)amount, ((MonsterModel)this).Creature, (CardModel)null);
			return;
		}
		if (amount > 0)
		{
			await PowerCmd.Apply<StrengthPower>(((MonsterModel)this).Creature, (decimal)amount, ((MonsterModel)this).Creature, (CardModel)null, false);
		}
	}

	private async Task GainArchitectBlockCappedAsync(decimal amount)
	{
		if (IsSilverBookChosen())
		{
			return;
		}
		int remainingCapacity = Math.Max(0, 999 - ((MonsterModel)this).Creature.Block);
		if (remainingCapacity <= 0)
		{
			return;
		}
		decimal cappedAmount = Math.Min(amount, (decimal)remainingCapacity);
		if (cappedAmount > 0m)
		{
			await CreatureCmd.GainBlock(((MonsterModel)this).Creature, cappedAmount, ValueProp.Move, null);
			await SyncArchitectBarricadeAsync();
		}
	}


	public override async Task AfterBlockGained(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
	{
		// EN: The engine reports every block gain through this hook.
		//     Filtering here keeps the Architect's barricade preview synced even when block comes
		//     from side mechanics instead of a direct Architect move.
		// ZH: 引擎会把所有格挡获得都打到这个回调里。
		//     这里按目标过滤后，建筑师的壁垒预览才能在旁路机制给格挡时也保持同步。
		await base.AfterBlockGained(creature, amount, props, cardSource);
		if (creature == ((MonsterModel)this).Creature)
			await SyncArchitectBarricadeAsync();
	}

	public override async Task AfterBlockCleared(Creature creature)
	{
		await base.AfterBlockCleared(creature);
		if (creature == ((MonsterModel)this).Creature)
			await SyncArchitectBarricadeAsync();
	}

	/// EN: Remove any lingering Barricade power from the Architect.
	///     Block persistence in Phase 3/4 is handled by ArchitectAdaptiveArmorPower.ShouldClearBlock.
	///     Block persistence in Phase 1/2 is handled by Barricade when block > 0 and not Silver Book.
	/// ZH: 移除建筑师身上滞留的壁垒能力。三/四阶段的格挡保留由自适应护甲的ShouldClearBlock处理。
	private async Task SyncArchitectBarricadeAsync()
	{
		BarricadePower? barricadePower = ((MonsterModel)this).Creature.GetPower<BarricadePower>();
		if (IsPhaseThree || IsPhaseFour)
		{
			// Phase 3 & 4: AdaptiveArmorPower.ShouldClearBlock retains block  -  Barricade not needed.
			if (barricadePower != null)
				await PowerCmd.Remove<BarricadePower>(((MonsterModel)this).Creature);
			return;
		}
		// Phase 1 & 2: Apply Barricade to retain block between turns (mirrors old BlockCap behaviour),
		// unless Silver Book was chosen (block should clear each turn).
		if (((MonsterModel)this).Creature.Block > 0 && !IsSilverBookChosen())
		{
			if (barricadePower == null)
				await PowerCmd.Apply<BarricadePower>(((MonsterModel)this).Creature, 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		}
		else if (barricadePower != null)
		{
			await PowerCmd.Remove<BarricadePower>(((MonsterModel)this).Creature);
		}
	}
}
