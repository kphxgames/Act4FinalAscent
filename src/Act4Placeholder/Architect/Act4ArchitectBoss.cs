//=============================================================================
// Act4ArchitectBoss.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Defines the multi-phase Architect boss monster model with three distinct skeleton animations and color tints per phase, plus SFX constants for its attacks.
// ZH: 定义多阶段建筑师Boss怪物模型，包含三组骨骼动画与各阶段色调，以及攻击音效常量。
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
	// Steam API compatibility: SetMaxHp changed return type across builds (Task vs Task<decimal>).
	// We invoke it via reflection so our DLL does not hard-bind to one specific return signature.
	private static readonly MethodInfo? CreatureCmdSetMaxHpMethod = typeof(CreatureCmd).GetMethod(
		"SetMaxHp",
		BindingFlags.Public | BindingFlags.Static,
		null,
		new[] { typeof(Creature), typeof(decimal) },
		null);

	private bool _armPhaseTwoAllOrNothingOnEnemyTurnStart;

	private bool _armPhaseThreeJudgmentOnEnemyTurnStart;

	private int _phaseTwoAllOrNothingThresholdPercent = 20;

	private bool _phaseTwoAllOrNothingShouldStun;

	private bool _phaseThreeJudgmentWasAttackedByCard;

	private bool _phaseThreeJudgmentTriggered;

	private readonly HashSet<ulong> _phaseThreeJudgmentTriggeredAttackers = new HashSet<ulong>();

	private int _enemyTurnCount;

	private int _phaseTurnCount;

	private int _pendingPhaseThreeCarriedStrength;

	private int _pendingPhaseTwoCarriedStrength;

	private int _currentPlayerRoundDamageTaken;

	private int _lastCompletedPlayerRoundDamagePercent = 20;

	private bool _isHandlingAfterSideTurnStart;

	private bool _isRetaliationEndingTurn;

	private bool _hasQueuedRetaliationForNextPlayerTurn;

	private bool _hasQueuedRetaliationActionRequested;

	private bool _hasTemporaryPhaseThreeThorns;

	private bool _hasPersistentSummonThorns;

	private int _persistentSummonThornsAmount;

	private bool _pendingSummonLinkedThornsSync;

	/// EN: Once-per-phase cap for external stuns (e.g. Whistle)  -  reset each phase entry.
	/// ZH: 每阶段限制一次的外部击晕标志（如㈜笛尾）——每阶段入口重置。
	internal bool ExternalStunUsedThisPhase;

	private bool _hasAppliedGrandLibraryBookCombatEffects;

	protected override string VisualsPath => "res://scenes/creature_visuals/architect.tscn";

	public override bool HasDeathSfx => false;

	public override int MinInitialHp
	{
		get
		{
			return Math.Max(1, (int)Math.Ceiling(GetPhaseOneTargetHp()));
		}
	}

	public override int MaxInitialHp => ((MonsterModel)this).MinInitialHp;

		public int PhaseNumber { get; set; } = 1;

	public bool IsPhaseTwo => PhaseNumber >= 2;

	public bool IsPhaseThree => PhaseNumber == 3;

	public bool IsPhaseFour => PhaseNumber >= 4;

	public override bool ShouldDisappearFromDoom => IsPhaseFour;

	// Instant mode: NMonsterDeathVfx.Create returns null, and AnimDie's MoveChild crashes on null.
	// Guard here so Phase 4 death still gets its fade VFX in normal/fast gameplay.
	public override bool ShouldFadeAfterDeath =>
		IsPhaseFour && SaveManager.Instance?.PrefsSave.FastMode != FastModeType.Instant;

		public bool HasTriggeredPhaseOneSummon { get; set; }

		public bool HasTriggeredPhaseTwoSummon { get; set; }

		public bool HasTriggeredPhaseThreeSummon { get; set; }

		public bool HasTriggeredPhaseThreeSecondSummon { get; set; }

		public bool HasTriggeredPhaseOneEmergencyFogmog { get; set; }

		public bool HasTriggeredPhaseTwoEmergencyFogmog { get; set; }

		public bool HasTriggeredPhaseThreeEmergencyFogmog { get; set; }

		public bool HasTriggeredPhaseOneRetaliation { get; set; }

		public bool HasTriggeredPhaseTwoRetaliation { get; set; }

		public bool HasTriggeredPhaseThreeRetaliation { get; set; }

		public int PendingPhaseNumber { get; set; }

	public bool IsAwaitingPhaseTransition => PendingPhaseNumber > 0;

	public override void OnDieToDoom()
	{
		if (((MonsterModel)this).Creature == null || ((MonsterModel)this).Creature.IsDead || IsAwaitingPhaseTransition)
		{
			return;
		}
		if (IsPhaseFour)
		{
			LogArchitect("OnDieToDoom:phase-four-queue-finish-run");
			QueuePhaseFourFinishRun("OnDieToDoom");
			return;
		}
		int nextPhaseNumber = IsPhaseThree ? 4 : (IsPhaseTwo ? 3 : 2);
		LogArchitect($"OnDieToDoom:redirect-to-phase-transition nextPhase={nextPhaseNumber} hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp}");
		BeginAwaitingPhaseTransition(nextPhaseNumber);
	}

	public override async Task AfterDiedToDoom(PlayerChoiceContext choiceContext, IReadOnlyList<Creature> creatures)
	{
		await base.AfterDiedToDoom(choiceContext, creatures);
		if (!IsPhaseFour || RunManager.Instance == null || ((MonsterModel)this).Creature == null)
		{
			return;
		}
		if (!creatures.Contains(((MonsterModel)this).Creature) || !((MonsterModel)this).Creature.IsDead)
		{
			return;
		}
		LogArchitect("AfterDiedToDoom:phase-four-finish-run");
		QueuePhaseFourFinishRun("AfterDiedToDoom");
	}

		public int PhaseOneMaxHpSnapshot { get; set; }

		public bool HasTriggeredPhaseThreeHalfHpThorns { get; set; }

	// EN: All balance constants for the Architect boss have been moved to Act4Config.cs.
	// ZH: 建筑师Boss的所有平衡常量已移至 Act4Config.cs。

	private int CoOpSingleDamageBonus
	{
		get
		{
			int architectPlayerCount = GetArchitectPlayerCount();
			if (architectPlayerCount >= 4) return Act4Config.CoOpSingleBonus4p;
			if (architectPlayerCount >= 3) return Act4Config.CoOpSingleBonus3p;
			return 0;
		}
	}

	private int CoOpMultiDamageBonus
	{
		get
		{
			int architectPlayerCount = GetArchitectPlayerCount();
			if (architectPlayerCount >= 4) return Act4Config.CoOpMultiBonus4p;
			if (architectPlayerCount >= 3) return Act4Config.CoOpMultiBonus3p;
			return 0;
		}
	}

	private int PhaseOneHeavyDamage => ApplySoloHeavyNerf(Act4Config.ArchitectP1HeavyDamage + CoOpSingleDamageBonus);

	private int PhaseTwoHeavyDamage => ApplySoloHeavyNerf(Act4Config.ArchitectP2HeavyDamage + CoOpSingleDamageBonus);

	private int PhaseOneMultiDamage => Math.Max(1, Act4Config.ArchitectP1MultiDamage + CoOpMultiDamageBonus);

	private int PhaseTwoMultiDamage => Math.Max(1, Act4Config.ArchitectP2MultiDamage + CoOpMultiDamageBonus);

	private int PhaseThreeHeavyDamage => ApplySoloHeavyNerf(Act4Config.ArchitectP3HeavyDamage + CoOpSingleDamageBonus + 2);

	private int PhaseFourHeavyDamage => ApplySoloHeavyNerf(Act4Config.ArchitectP4HeavyDamage + CoOpSingleDamageBonus);

	private int PhaseThreeMultiDamage => Math.Max(1, Act4Config.ArchitectP3MultiDamage + CoOpMultiDamageBonus + 1);

	private int PhaseFourMultiDamage => Math.Max(1, Act4Config.ArchitectP4MultiDamage + CoOpMultiDamageBonus);

	private int PhaseOneMultiHits => Act4Config.ArchitectP1MultiHits;

	private int PhaseTwoMultiHits => Act4Config.ArchitectP2MultiHits;

	private int PhaseThreeMultiHits => Act4Config.ArchitectP3MultiHits;

	private int PhaseFourMultiHits => Act4Config.ArchitectP4MultiHits;

	private int GetArchitectPlayerCount()
	{
		try
		{
			RunState val = RunManager.Instance?.DebugOnlyGetState();
			if (val != null)
			{
				return Math.Max(1, val.Players.Count);
			}
		}
		catch
		{
		}
		try
		{
			CombatState combatState = ((MonsterModel)this).Creature?.CombatState;
			if (combatState != null)
			{
				return Math.Max(1, combatState.PlayerCreatures.Count);
			}
		}
		catch
		{
		}
		return 1;
	}

	private int GetProtectionStacksForPlayers(int stacksPerPlayer)
	{
		return Math.Max(1, GetArchitectPlayerCount() * stacksPerPlayer);
	}

	private void LogArchitect(string message)
	{
		GD.Print($"[Act4Placeholder][Architect] {message}");
	}

	private async Task SetMaxHpCompatAsync(Creature creature, decimal targetMaxHp)
	{
		if (creature == null)
		{
			return;
		}
		// EN: Steam 1.00 changed CreatureCmd.SetMaxHp from Task to Task<decimal>.
		//     Reflection is ugly, but it lets one DLL survive both signatures instead of shipping
		//     separate builds just because the engine changed a return type on us.
		// ZH: Steam 1.00 把 CreatureCmd.SetMaxHp 从 Task 改成了 Task<decimal>。
		//     这里用反射虽然不好看，但能让同一份 DLL 同时兼容两种签名，不用被迫拆双版本。
		try
		{
			if (CreatureCmdSetMaxHpMethod != null)
			{
				object? result = CreatureCmdSetMaxHpMethod.Invoke(null, new object[] { creature, targetMaxHp });
				if (result is Task task)
				{
					await task;
					return;
				}
			}
		}
		catch (Exception ex)
		{
			LogArchitect($"SetMaxHpCompatAsync:reflection-failed error={ex.Message}");
		}

		creature.SetMaxHpInternal(targetMaxHp);
		if (creature.CurrentHp > creature.MaxHp)
		{
			creature.SetCurrentHpInternal((decimal)creature.MaxHp);
		}
	}

	/// EN: Initialize combat-only state, visuals, powers, and the opening move.
	/// ZH: 初始化战斗内状态、表现、能力与开场动作。
	public override async Task AfterAddedToRoom()
	{
		LogArchitect($"AfterAddedToRoom:start hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp} phase={PhaseNumber} pending={PendingPhaseNumber}");
		await base.AfterAddedToRoom();
		LogArchitect("AfterAddedToRoom:after-base");
		// EN: The engine calls ScaleMonsterHpForMultiplayer (hp × playerCount × perActFactor)
		//     in CombatState.CreateCreature() BEFORE AfterAddedToRoom. Since we already bake our
		//     own per-player scaling into GetPhaseOneTargetHp(), that results in double-scaling.
		//     Reset the HP here to the value we actually intend.
		// ZH: 引擎在CreateCreature()中调用ScaleMonsterHpForMultiplayer（HP × 人数 × 幕倍率），
		//     早于AfterAddedToRoom。由于GetPhaseOneTargetHp()已包含我们的人数缩放逻辑，
		//     此处会发生双重缩放，需将HP重置为预期值。
		{
			int intendedHp = Math.Max(1, (int)Math.Ceiling(GetPhaseOneTargetHp()));
			((MonsterModel)this).Creature.SetMaxHpInternal((decimal)intendedHp);
			((MonsterModel)this).Creature.SetCurrentHpInternal((decimal)intendedHp);
			LogArchitect($"AfterAddedToRoom:hp-corrected intended={intendedHp} was={((MonsterModel)this).Creature.MaxHp}");
		}
		PhaseOneMaxHpSnapshot = ((MonsterModel)this).Creature.MaxHp;
		ExternalStunUsedThisPhase = false;
		await PowerCmd.Apply<Act4ArchitectRevivalPower>(((MonsterModel)this).Creature, 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		LogArchitect($"AfterAddedToRoom:revival-applied phaseOneMaxHp={PhaseOneMaxHpSnapshot}");
		ApplyArchitectVisuals(ArchitectBaseTint, 1.05f);
		await SyncAdaptiveResistancePowerAsync();
		NCombatRoom instance = NCombatRoom.Instance;
		NCreature creatureNode = ((instance != null) ? instance.GetCreatureNode(((MonsterModel)this).Creature) : null);
		if (creatureNode != null)
		{
			if (creatureNode.Visuals?.IntentPosition != null)
			{
				creatureNode.Visuals.IntentPosition.Position = new Vector2(creatureNode.Visuals.IntentPosition.Position.X, -540f);
				creatureNode.IntentContainer.Position = creatureNode.Visuals.IntentPosition.Position - creatureNode.IntentContainer.Size / 2f;
			}
			if (creatureNode.Visuals?.TalkPosition != null)
			{
				creatureNode.Visuals.TalkPosition.Position += new Vector2(200f, 20f);
			}
			ApplyArchitectBounds(creatureNode);
			Node2D intentPosition = creatureNode.Visuals?.IntentPosition;
			LogArchitect($"AfterAddedToRoom:node-ready pos={((Control)creatureNode).Position} intentPos={(intentPosition != null ? intentPosition.Position.ToString() : "null")}");
		}
		else
		{
			LogArchitect("AfterAddedToRoom:creature-node-missing");
		}
		_phaseTurnCount = 0;
		_phaseFourOpeningOblivionPending = false;
		await SyncRetaliationCounterAsync();
		await PowerCmd.Apply<StrengthPower>(((MonsterModel)this).Creature, 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
		// EN: Weakest-player Str/Dex buff is now applied universally via CombatManagerAfterCreatureAddedPatch
		//     for ALL Act 4 battles (not just the Architect). See ModSupport.ApplyAct4WeakestPlayerBuffAsync.
		// ZH: 最弱玩家的力量/敏捷增益现通过CombatManagerAfterCreatureAddedPatch在第四幕所有战斗中生效，
		//     而非仅限于建筑师战斗。详见ModSupport.ApplyAct4WeakestPlayerBuffAsync。
		LogArchitect($"AfterAddedToRoom:opening-buffs artifact={((MonsterModel)this).Creature.GetPower<ArtifactPower>()?.Amount ?? 0} slippery={((MonsterModel)this).Creature.GetPower<SlipperyPower>()?.Amount ?? 0} strength={((MonsterModel)this).Creature.GetPower<StrengthPower>()?.Amount ?? 0}");
		await EnsurePersistentShieldAsync();
		LogArchitect($"AfterAddedToRoom:shield-ready block={((MonsterModel)this).Creature.Block}");
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
		if (_phaseOneBuffState != null)
		{
			((MonsterModel)this).SetMoveImmediate(_phaseOneBuffState, true);
		}
		await ApplyBookPhaseStartEffectsAsync();
		ShowArchitectSpeech(GetArchitectOpeningSpeech(), VfxColor.Blue, 4.2);
		_ambientPulseCount = 0;
		_ = TaskHelper.RunSafely(RunAmbientVfxLoopAsync(++_ambientVfxLoopGeneration));
		LogArchitect("AfterAddedToRoom:complete");
	}

	public override void BeforeRemovedFromRoom()
	{
		_ambientVfxLoopGeneration++;
		base.BeforeRemovedFromRoom();
	}

	public override async Task AfterSideTurnStart(CombatSide side, CombatState combatState)
	{
		if (_isHandlingAfterSideTurnStart)
		{
			LogArchitect($"AfterSideTurnStart:reentrant side={side} currentSide={combatState.CurrentSide}");
			return;
		}
		_isHandlingAfterSideTurnStart = true;
		try
		{
			await base.AfterSideTurnStart(side, combatState);
			LogArchitect($"AfterSideTurnStart side={side} hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp} phase={PhaseNumber} pending={PendingPhaseNumber} tempThorns={_hasTemporaryPhaseThreeThorns}");
			if (((MonsterModel)this).Creature.IsDead)
			{
				if (IsPhaseFour)
				{
					QueuePhaseFourFinishRun("AfterSideTurnStartDeadFallback");
				}
				// If the Architect is dead but awaiting a phase transition, the
				// ReviveMove will execute during this enemy turn.  Clear stale
				// retaliation state so the next phase starts with a clean slate
				// and the turn cycle is not blocked.
				if (IsAwaitingPhaseTransition)
				{
					_isRetaliationEndingTurn = false;
					_hasQueuedRetaliationForNextPlayerTurn = false;
					_hasQueuedRetaliationActionRequested = false;
					LogArchitect($"AfterSideTurnStart:dead-awaiting-transition pending={PendingPhaseNumber}");
				}
				else
				{
					LogArchitect("AfterSideTurnStart:skipped");
				}
				return;
			}
			if (side == (CombatSide)2)
			{
				// The player turn has ended successfully  -  the retaliation EndTurn fired and the
				// combat engine advanced to the enemy side.  Clear the guard flag unconditionally
				// so any stale value from a prior trigger (e.g. a partially-dropped network packet)
				// cannot block future retaliation checks.
				_isRetaliationEndingTurn = false;
				// Remove ChainsOfBindingPower from all players at the start of every enemy turn.
				// Chains is applied during the enemy turn and should last exactly 1 player turn;
				// since Counter powers don't auto-decrement in STS2, we manually remove here.
				foreach (Player chainsPlayer in CombatState.Players)
				{
					if (chainsPlayer?.Creature != null && chainsPlayer.Creature.GetPower<ChainsOfBindingPower>() != null)
						await PowerCmd.Remove<ChainsOfBindingPower>(chainsPlayer.Creature);
				}
				// Enemy (Architect) turn starting - show mushroom VFX (Phase 4 only), remove player-turn VFX.
				if (!((MonsterModel)this).Creature.IsDead && IsPhaseFour)
				{
					RemovePlayerTurnVfx();
					EnsureMushroomVfx();
				}
				bool armedRetaliationStatus = false;
				if (IsPhaseFour && (_phaseFourOblivionUnlocked || _phaseFourOblivionCastCount > 0) && _phaseFourOblivionState != null)
				{
					((MonsterModel)this).SetMoveImmediate(_phaseFourOblivionState, true);
				}
				if (_armPhaseTwoAllOrNothingOnEnemyTurnStart && !IsPhaseTwo)
				{
					_armPhaseTwoAllOrNothingOnEnemyTurnStart = false;
				}
				if (_armPhaseThreeJudgmentOnEnemyTurnStart && !IsPhaseThree)
				{
					_armPhaseThreeJudgmentOnEnemyTurnStart = false;
				}
				await ApplyAdaptiveResistanceAsync();
				if (IsPhaseThree && _pendingSummonLinkedThornsSync)
				{
					await SyncSummonLinkedThornsAsync();
				}
				if (_hasTemporaryPhaseThreeThorns)
				{
					ThornsPower thornsPower = ((MonsterModel)this).Creature.GetPower<ThornsPower>();
					int remaining = Math.Max(0, (thornsPower != null) ? (((PowerModel)thornsPower).Amount - 10) : 0);
					if (remaining > 0)
					{
						await PowerCmd.SetAmount<ThornsPower>(((MonsterModel)this).Creature, (decimal)remaining, ((MonsterModel)this).Creature, (CardModel)null);
					}
					else
					{
						await PowerCmd.Remove<ThornsPower>(((MonsterModel)this).Creature);
					}
					_hasTemporaryPhaseThreeThorns = false;
					LogArchitect($"AfterSideTurnStart:cleared-temp-thorns remaining={((MonsterModel)this).Creature.GetPower<ThornsPower>()?.Amount ?? 0}");
				}
				if (_armPhaseTwoAllOrNothingOnEnemyTurnStart)
				{
					_armPhaseTwoAllOrNothingOnEnemyTurnStart = false;
					await ArmPhaseTwoAllOrNothingAsync();
					armedRetaliationStatus = true;
				}
				if (_armPhaseThreeJudgmentOnEnemyTurnStart)
				{
					_armPhaseThreeJudgmentOnEnemyTurnStart = false;
					await ArmPhaseThreeJudgmentAsync();
					armedRetaliationStatus = true;
				}
				if (armedRetaliationStatus)
				{
					await SyncArchitectBarricadeAsync();
					LogArchitect("AfterSideTurnStart:armed-retaliation-status");
					return;
				}
				await ResolvePhaseTwoAllOrNothingAsync();
				await ResolvePhaseThreeJudgmentAsync();
				if (IsPhaseThree && ((MonsterModel)this).Creature.GetPower<ArchitectJudgmentPower>() != null)
					EnsurePhaseThreeJudgmentAuraVfx();
				else
					RemovePhaseThreeJudgmentAuraVfx();
				await SyncArchitectBarricadeAsync();
				LogArchitect($"AfterSideTurnStart:complete artifact={((MonsterModel)this).Creature.GetPower<ArtifactPower>()?.Amount ?? 0} slippery={((MonsterModel)this).Creature.GetPower<SlipperyPower>()?.Amount ?? 0} thorns={((MonsterModel)this).Creature.GetPower<ThornsPower>()?.Amount ?? 0}");
				return;
			}
			if (side != CombatSide.Player)
			{
				LogArchitect("AfterSideTurnStart:skipped-non-player");
				return;
			}
			// Player turn starting - show legend VFX (Phase 4 only), remove architect-turn VFX.
			if (!((MonsterModel)this).Creature.IsDead && IsPhaseFour)
			{
				RemovePhaseFourMushroomVfx();
				EnsureLegendVfx(combatState);
			}
			// Guard: if all players are dead the game-over screen is already queued.
			// Without this check, when the Architect kills the last player in Phase 1/2/3,
			// the engine still fires AfterSideTurnStart for the next "player turn start".
			// With no book chosen every awaitable in this path completes synchronously, so
			// Mono's async state-machine collapses them all inline without the TPL back-off
			// that .NET Core uses, producing a stack-overflow that blocks the game-over screen.
			if (!combatState.PlayerCreatures.Any(pc => pc.IsAlive))
			{
				LogArchitect("AfterSideTurnStart:all-players-dead-skip");
				return;
			}
			// Silver Book: grant each player block = 5% of their max HP at start of their turn.
			if (IsSilverBookChosen())
			{
				foreach (Player player in CombatState.Players)
				{
					int silverBlockAmount = Math.Max(1, (int)Math.Ceiling((decimal)player.Creature.MaxHp * 0.05m));
					await CreatureCmd.GainBlock(player.Creature, (decimal)silverBlockAmount, ValueProp.Move, null);
				}
			}
			_lastCompletedPlayerRoundDamagePercent = Math.Clamp((int)Math.Ceiling((double)(_currentPlayerRoundDamageTaken * 100m / Math.Max(1m, (decimal)((MonsterModel)this).Creature.MaxHp))), 0, 99);
			_currentPlayerRoundDamageTaken = 0;
			if (IsPhaseFour)
			{
				await TickPhaseFourOblivionAsync();
				if (((MonsterModel)this).Creature.IsDead)
				{
					return;
				}
				if (_phaseFourOpeningOblivionPending && _phaseFourOblivionState != null)
				{
					((MonsterModel)this).SetMoveImmediate(_phaseFourOblivionState, true);
					LogArchitect("AfterSideTurnStart:phase-four-preserved-opening-oblivion");
				}
				else
				{
					ScheduleNextPhaseFourMove();
					LogArchitect($"AfterSideTurnStart:phase-four-scheduled move={((MonsterModel)this).NextMove.Id} castCount={_phaseFourOblivionCastCount}");
				}
			}
			if (_hasQueuedRetaliationForNextPlayerTurn)
			{
				_hasQueuedRetaliationForNextPlayerTurn = false;
				await Cmd.Wait(5f, false);
				if (((MonsterModel)this).Creature.IsDead || IsAwaitingPhaseTransition)
				{
					return;
				}
				RequestQueuedRetaliationActionIfHost(combatState);
				LogArchitect($"AfterSideTurnStart:queued-retaliation-action requested={_hasQueuedRetaliationActionRequested}");
				return;
			}
			bool scheduledSummon = TryScheduleSummonIntent();
			if (scheduledSummon)
			{
				LogArchitect("AfterSideTurnStart:summon-intent-scheduled");
			}
			await RefreshPreAttackTrackerAsync();
			LogArchitect($"AfterSideTurnStart:player-ready move={((MonsterModel)this).NextMove.Id} attackIntent={((MonsterModel)this).IntendsToAttack}");
		}
		finally
		{
			_isHandlingAfterSideTurnStart = false;
		}
	}
}
