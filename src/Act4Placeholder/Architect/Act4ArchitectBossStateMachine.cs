//=============================================================================
// Act4ArchitectBossStateMachine.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Move-state generation, phase entry flow, and move execution helpers for the Architect.
// ZH: 建筑师的招式状态机构建、阶段入口流程与招式执行逻辑。
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
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
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

namespace Act4Placeholder;

public sealed partial class Act4ArchitectBoss : MonsterModel
{
	private const string PhaseShiftSfx = "event:/sfx/enemy/enemy_attacks/test_subject/test_subject_revive_two_heads";

	private const string PhaseShiftSummonSfx = "event:/sfx/enemy/enemy_attacks/two_tail_rats/two_tail_rats_summon";

	private const string HeavyAttackSfx = "event:/sfx/enemy/enemy_attacks/test_subject/test_subject_bite";

	private const string MultiAttackSfx = "event:/sfx/enemy/enemy_attacks/test_subject/test_subject_slash";

	private const string ArchitectPhaseTwoSkeletonDataPath = "res://animations/monsters/architect_phase2/architect_skel_data.tres";

	private const string ArchitectPhaseThreeSkeletonDataPath = "res://animations/monsters/architect_phase3/architect_skel_data.tres";

	private static readonly Color ArchitectPhaseTwoTint = new Color(1f, 0.97f, 0.78f, 1f);

	private static readonly Color ArchitectPhaseThreeTint = new Color(1f, 0.95f, 0.72f, 1f);

	private static readonly Color ArchitectPhaseFourTint = new Color(0.74f, 0.68f, 0.56f, 1f);

	/// EN: Tint applied while the Architect is downed between phases, darkened to sell the fake-out death.
	/// ZH: 建筑师阶段切换倒地时使用的着色，会整体压暗，好让假死演出更像那么回事。
	private static readonly Color ArchitectDownedTint = new Color(0.38f, 0.35f, 0.42f, 0.65f);

	private static readonly Color[] PurpleFireGradient = new Color[4]
	{
		new Color(0.2f, 0.05f, 0.35f, 0f),
		new Color(0.52f, 0.18f, 0.85f, 0.75f),
		new Color(0.82f, 0.52f, 1f, 0.95f),
		new Color(1f, 0.88f, 1f, 0f)
	};

	private static readonly Color[] BlackFireGradient = new Color[4]
	{
		new Color(0.02f, 0.02f, 0.02f, 0f),
		new Color(0.1f, 0.08f, 0.12f, 0.9f),
		new Color(0.28f, 0.24f, 0.3f, 1f),
		new Color(0.48f, 0.46f, 0.5f, 0f)
	};

	private const float StandardHyperbeamLaserDuration = 1.5f;

	private const float OblivionHyperbeamLaserDuration = 3.0f;

	private MoveState? _phaseTwoHeavyState;

	private MoveState? _phaseTwoMultiState;

	private MoveState? _phaseTwoStunnedState;

	private MoveState? _phaseThreeHeavyState;

	private MoveState? _phaseThreeStunnedState;

	private MoveState? _reviveState;

	private MoveState? _phaseOneDebuffState;

	private MoveState? _phaseTwoDebuffState;

	private MoveState? _phaseThreeDebuffState;

	private MoveState? _phaseTwoBuffState;

	private MoveState? _phaseTwoSummonState;

	private MoveState? _phaseThreeSummonState;

	private MoveState? _phaseThreeEmergencyBuffState;

	private MoveState? _phaseThreeBuffState;

	private MoveState? _phaseThreeCombinedState;

	private MoveState? _phaseFourHeavyState;

	private MoveState? _phaseFourMultiState;

	private MoveState? _phaseFourBuffState;

	private MoveState? _phaseFourBlockState;

	private MoveState? _phaseFourOblivionState;

	private MoveState? _phaseOneBuffState;

	private MoveState? _phaseTwoRetaliationMultiState;

	// Maps each base MoveState to a variant that also shows a BuffIntent when passive strength gain is upcoming.
	private Dictionary<MoveState, MoveState> _strengthComboMap = new();

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState phaseOneHeavyState = new MoveState("PHASE_ONE_HEAVY", (Func<IReadOnlyList<Creature>, Task>)PhaseOneHeavyMove, new AbstractIntent[1] { new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseOneHeavyDamage)) });
		MoveState phaseOneMultiState = new MoveState("PHASE_ONE_MULTI", (Func<IReadOnlyList<Creature>, Task>)PhaseOneMultiMove, new AbstractIntent[1] { new DynamicMultiAttackIntent(() => (decimal)PhaseOneMultiDamage, () => PhaseOneMultiHits) });
		MoveState phaseOneBuffState = new MoveState("PHASE_ONE_BUFF", (Func<IReadOnlyList<Creature>, Task>)PhaseOneBuffMove, new AbstractIntent[2]
		{
			new BuffIntent(),
			new BuffIntent()
		});
		// EN: Cursed Book turns the debuff beat into a disguised heavy hit, so the preview has to tell the same story.
		// ZH: 诅咒之书会把减益节拍偷换成重击，所以预览也得和实际演出保持同一套说法。
		MoveState phaseOneDebuffState = new MoveState("PHASE_ONE_DEBUFF", (Func<IReadOnlyList<Creature>, Task>)PhaseOneDebuffMove, new AbstractIntent[1] { IsCursedBookChosen() ? (AbstractIntent)new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseOneHeavyDamage)) : new DebuffIntent(true) });
		RandomBranchState phaseOneAttackRandomState = new RandomBranchState("PHASE_ONE_RANDOM");
		ConditionalBranchState phaseOneCadenceSelector = new ConditionalBranchState("PHASE_ONE_SELECTOR");

		MoveState phaseTwoHeavyState = new MoveState("PHASE_TWO_HEAVY", (Func<IReadOnlyList<Creature>, Task>)PhaseTwoHeavyMove, new AbstractIntent[1] { new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseTwoHeavyDamage)) });
		MoveState phaseTwoMultiState = new MoveState("PHASE_TWO_MULTI", (Func<IReadOnlyList<Creature>, Task>)PhaseTwoMultiMove, new AbstractIntent[1] { new DynamicMultiAttackIntent(() => (decimal)PhaseTwoMultiDamage, () => PhaseTwoMultiHits) });
		MoveState phaseTwoRetaliationMultiState = new MoveState("PHASE_TWO_RETALIATION_MULTI", (Func<IReadOnlyList<Creature>, Task>)PhaseTwoRetaliationMultiMove, new AbstractIntent[1] { new MultiAttackIntent((int)PhaseTwoMultiDamage, Act4Config.ArchitectP2RetaliationHits) });
		MoveState phaseTwoStunnedState = new MoveState("PHASE_TWO_STUNNED", (Func<IReadOnlyList<Creature>, Task>)PhaseTwoStunnedMove, new AbstractIntent[1] { new StunIntent() });
		RandomBranchState phaseTwoAttackRandomState = new RandomBranchState("PHASE_TWO_RANDOM");
		ConditionalBranchState phaseTwoCadenceSelector = new ConditionalBranchState("PHASE_TWO_SELECTOR");

		MoveState phaseThreeHeavyState = new MoveState("PHASE_THREE_HEAVY", (Func<IReadOnlyList<Creature>, Task>)PhaseThreeHeavyMove, new AbstractIntent[1] { new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseThreeHeavyDamage)) });
		MoveState phaseThreeMultiState = new MoveState("PHASE_THREE_MULTI", (Func<IReadOnlyList<Creature>, Task>)PhaseThreeMultiMove, new AbstractIntent[1] { new DynamicMultiAttackIntent(() => (decimal)PhaseThreeMultiDamage, () => PhaseThreeMultiHits) });
		MoveState phaseThreeStunnedState = new MoveState("PHASE_THREE_STUNNED", (Func<IReadOnlyList<Creature>, Task>)PhaseThreeStunnedMove, new AbstractIntent[1] { new StunIntent() });
		RandomBranchState phaseThreeAttackRandomState = new RandomBranchState("PHASE_THREE_RANDOM");
		ConditionalBranchState phaseThreeCadenceSelector = new ConditionalBranchState("PHASE_THREE_SELECTOR");

		MoveState phaseTwoDebuffState = new MoveState("PHASE_TWO_DEBUFF", (Func<IReadOnlyList<Creature>, Task>)PhaseTwoDebuffMove, new AbstractIntent[1] { IsCursedBookChosen() ? (AbstractIntent)new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseTwoHeavyDamage)) : new DebuffIntent(true) });
		MoveState phaseTwoBuffState = new MoveState("PHASE_TWO_BUFF", (Func<IReadOnlyList<Creature>, Task>)PhaseTwoBuffMove, new AbstractIntent[3]
		{
			new BuffIntent(),
			new DefendIntent(),
			new BuffIntent()
		});
		MoveState phaseTwoSummonState = new MoveState("PHASE_TWO_SUMMON", (Func<IReadOnlyList<Creature>, Task>)PhaseTwoSummonMove, new AbstractIntent[1] { new SummonIntent() });
		MoveState phaseThreeSummonState = new MoveState("PHASE_THREE_SUMMON", (Func<IReadOnlyList<Creature>, Task>)PhaseThreeSummonMove, new AbstractIntent[1] { new SummonIntent() });
		MoveState phaseThreeDebuffState = new MoveState("PHASE_THREE_DEBUFF", (Func<IReadOnlyList<Creature>, Task>)PhaseThreeDebuffMove, new AbstractIntent[1] { IsCursedBookChosen() ? (AbstractIntent)new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseThreeHeavyDamage)) : new DebuffIntent(true) });
		MoveState phaseThreeBuffState = new MoveState("PHASE_THREE_BUFF", (Func<IReadOnlyList<Creature>, Task>)PhaseThreeBuffMove, new AbstractIntent[3]
		{
			new BuffIntent(),
			new DefendIntent(),
			new BuffIntent()
		});
		MoveState phaseThreeEmergencyBuffState = new MoveState("PHASE_THREE_EMERGENCY_BUFF", (Func<IReadOnlyList<Creature>, Task>)PhaseThreeEmergencyBuffMove, new AbstractIntent[1] { new BuffIntent() });
		MoveState phaseThreeCombinedState = new MoveState("PHASE_THREE_COMBINED", (Func<IReadOnlyList<Creature>, Task>)PhaseThreeCombinedMove,
			IsCursedBookChosen()
				? new AbstractIntent[] { new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseThreeHeavyDamage)), new BuffIntent(), new DefendIntent(), new BuffIntent() }
				: new AbstractIntent[] { new DebuffIntent(true), new BuffIntent(), new DefendIntent(), new BuffIntent() });

		MoveState phaseFourHeavyState = new MoveState("PHASE_FOUR_HEAVY", (Func<IReadOnlyList<Creature>, Task>)PhaseFourHeavyMove, new AbstractIntent[1] { new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseFourHeavyDamage)) });
		MoveState phaseFourMultiState = new MoveState("PHASE_FOUR_MULTI", (Func<IReadOnlyList<Creature>, Task>)PhaseFourMultiMove, new AbstractIntent[1] { new DynamicMultiAttackIntent(() => (decimal)PhaseFourMultiDamage, () => PhaseFourMultiHits) });
		MoveState phaseFourBlockState = new MoveState("PHASE_FOUR_BLOCK", (Func<IReadOnlyList<Creature>, Task>)PhaseFourBlockMove, new AbstractIntent[2] { new DefendIntent(), new BuffIntent() });
		MoveState phaseFourBuffState = new MoveState("PHASE_FOUR_BUFF", (Func<IReadOnlyList<Creature>, Task>)PhaseFourBuffMove, new AbstractIntent[2]
		{
			new BuffIntent(),
			new BuffIntent()
		});
		MoveState phaseFourOblivionState = new MoveState("PHASE_FOUR_OBLIVION", (Func<IReadOnlyList<Creature>, Task>)PhaseFourOblivionMove, new AbstractIntent[1] { new DynamicMultiAttackIntent(() => (decimal)GetPhaseFourOblivionDamage(), () => 8) });
		RandomBranchState phaseFourAttackRandomState = new RandomBranchState("PHASE_FOUR_RANDOM");

		MoveState reviveMoveState = new MoveState("REVIVE_MOVE", (Func<IReadOnlyList<Creature>, Task>)ReviveMove, new AbstractIntent[2]
		{
			new HealIntent(),
			new BuffIntent()
		})
		{
			MustPerformOnceBeforeTransitioning = true
		};
		ConditionalBranchState reviveBranchState = new ConditionalBranchState("REVIVE_BRANCH");

		_phaseTwoBuffState = phaseTwoBuffState;
		_phaseOneBuffState = phaseOneBuffState;
		_phaseTwoHeavyState = phaseTwoHeavyState;
		_phaseTwoMultiState = phaseTwoMultiState;
		_phaseTwoRetaliationMultiState = phaseTwoRetaliationMultiState;
		_phaseTwoStunnedState = phaseTwoStunnedState;
		_phaseThreeHeavyState = phaseThreeHeavyState;
		_phaseThreeStunnedState = phaseThreeStunnedState;
		_phaseOneDebuffState = phaseOneDebuffState;
		_phaseTwoDebuffState = phaseTwoDebuffState;
		_phaseTwoSummonState = phaseTwoSummonState;
		_phaseThreeSummonState = phaseThreeSummonState;
		_phaseThreeDebuffState = phaseThreeDebuffState;
		_phaseThreeBuffState = phaseThreeBuffState;
		_phaseThreeEmergencyBuffState = phaseThreeEmergencyBuffState;
		_phaseThreeCombinedState = phaseThreeCombinedState;
		_phaseFourHeavyState = phaseFourHeavyState;
		_phaseFourMultiState = phaseFourMultiState;
		_phaseFourBlockState = phaseFourBlockState;
		_phaseFourBuffState = phaseFourBuffState;
		_phaseFourOblivionState = phaseFourOblivionState;
		_reviveState = reviveMoveState;

		// EN: These follow-ups are the important cleanup. Every normal move returns to a selector that plans the next beat.
		// ZH: 这些后续连接就是这次整理的重点。常规招式先回到选择器，再由选择器规划下一拍。
		phaseOneHeavyState.FollowUpState = phaseOneCadenceSelector; // EN: P1 heavy hands control back to the phase-1 planner. | ZH: 一阶段重击打完后交回一阶段规划器。
		phaseOneMultiState.FollowUpState = phaseOneCadenceSelector; // EN: P1 multi also re-enters the same cadence check. | ZH: 一阶段多段同样回到同一个节奏检查点。
		phaseOneBuffState.FollowUpState = phaseOneCadenceSelector; // EN: P1 buff does not skip cadence, next turn can still be the scripted debuff. | ZH: 一阶段增益不会跳过节奏判断，下回合依旧可能接固定减益。
		phaseOneDebuffState.FollowUpState = phaseOneCadenceSelector; // EN: After the debuff beat, go back to normal phase-1 planning. | ZH: 固定减益打完后，再回归普通的一阶段规划。
		phaseTwoHeavyState.FollowUpState = phaseTwoCadenceSelector; // EN: P2 heavy loops into the phase-2 cadence selector. | ZH: 二阶段重击结束后回到二阶段节奏选择器。
		phaseTwoMultiState.FollowUpState = phaseTwoCadenceSelector; // EN: P2 multi does the same, no stale attack preview anymore. | ZH: 二阶段多段也是如此，不再留下陈旧攻击预览。
		phaseTwoRetaliationMultiState.FollowUpState = phaseTwoCadenceSelector; // EN: Retaliation multi still resumes normal P2 cadence afterward. | ZH: 反击多段结束后也会继续走正常的二阶段节奏。
		phaseTwoStunnedState.FollowUpState = phaseTwoCadenceSelector; // EN: Even a stun turn should not break the cadence script. | ZH: 即便是击晕回合，也不该打断节奏脚本。
		phaseTwoDebuffState.FollowUpState = phaseTwoCadenceSelector; // EN: After P2 debuff, re-plan from the selector. | ZH: 二阶段减益后，再交给选择器重排下一拍。
		phaseTwoBuffState.FollowUpState = phaseTwoCadenceSelector; // EN: After P2 buff, check whether the next turn should be debuff or attack. | ZH: 二阶段增益后，再判断下一回合该是减益还是攻击。
		phaseTwoSummonState.FollowUpState = phaseTwoCadenceSelector; // EN: Summon turns still return to the same cadence spine. | ZH: 召唤回合也会回到同一条节奏主干。
		phaseThreeHeavyState.FollowUpState = phaseThreeCadenceSelector; // EN: P3 heavy returns to the phase-3 combined-turn planner. | ZH: 三阶段重击后回到三阶段组合回合规划器。
		phaseThreeMultiState.FollowUpState = phaseThreeCadenceSelector; // EN: P3 multi also rechecks whether a combined turn is due. | ZH: 三阶段多段后同样会重查组合回合是否到点。
		phaseThreeStunnedState.FollowUpState = phaseThreeCadenceSelector; // EN: Stuns should pause the boss, not desync the script. | ZH: 击晕应该只是暂停Boss，不该把脚本节奏打乱。
		phaseThreeDebuffState.FollowUpState = phaseThreeCadenceSelector; // EN: P3 debuff returns to the same selector flow. | ZH: 三阶段减益后也回到同一条选择流程。
		phaseThreeSummonState.FollowUpState = phaseThreeCadenceSelector; // EN: P3 summon goes back into the cadence planner too. | ZH: 三阶段召唤后同样回到节奏规划器。
		phaseThreeBuffState.FollowUpState = phaseThreeCadenceSelector; // EN: P3 buff still feeds into the combined-turn schedule. | ZH: 三阶段增益后仍会继续接入组合回合时间表。
		phaseThreeEmergencyBuffState.FollowUpState = phaseThreeCadenceSelector; // EN: Emergency buffs recover, then hand the wheel back to the phase plan. | ZH: 紧急增益只是救场，之后还是把方向盘交回阶段计划。
		phaseThreeCombinedState.FollowUpState = phaseThreeCadenceSelector; // EN: Combined move finishes and then resumes normal P3 planning. | ZH: 组合招打完后，再恢复正常的三阶段规划。
		phaseFourHeavyState.FollowUpState = phaseFourAttackRandomState; // EN: P4 still uses its own scheduler, so this only lands in the phase-4 shell. | ZH: 四阶段仍有独立调度，所以这里只是先落回四阶段外壳。
		phaseFourMultiState.FollowUpState = phaseFourAttackRandomState; // EN: Same shell return for P4 multi. | ZH: 四阶段多段也先回到同一个外壳。
		phaseFourBlockState.FollowUpState = phaseFourAttackRandomState; // EN: Block turn also returns to the P4 shell. | ZH: 格挡回合结束后也回到四阶段外壳。
		phaseFourBuffState.FollowUpState = phaseFourAttackRandomState; // EN: Buff turn returns to the P4 shell as well. | ZH: 增益回合同样回到四阶段外壳。
		phaseFourOblivionState.FollowUpState = phaseFourAttackRandomState; // EN: Oblivion exits back to the P4 shell before the custom scheduler intervenes. | ZH: 湮灭结束后先回到四阶段外壳，再由专门调度介入。
		reviveMoveState.FollowUpState = reviveBranchState; // EN: Revive is the bridge, then we jump into the right phase planner. | ZH: 复活是过桥招式，结束后再跳到正确阶段的规划入口。

		MoveState phaseOneHeavyStrengthState = new MoveState("PHASE_ONE_HEAVY_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseOneHeavyMove, new AbstractIntent[] { new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseOneHeavyDamage)), new BuffIntent() });
		MoveState phaseOneMultiStrengthState = new MoveState("PHASE_ONE_MULTI_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseOneMultiMove, new AbstractIntent[] { new DynamicMultiAttackIntent(() => (decimal)PhaseOneMultiDamage, () => PhaseOneMultiHits), new BuffIntent() });
		MoveState phaseOneDebuffStrengthState = new MoveState("PHASE_ONE_DEBUFF_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseOneDebuffMove, new AbstractIntent[] { IsCursedBookChosen() ? (AbstractIntent)new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseOneHeavyDamage)) : new DebuffIntent(true), new BuffIntent() });
		MoveState phaseTwoStunnedStrengthState = new MoveState("PHASE_TWO_STUNNED_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseTwoStunnedMove, new AbstractIntent[] { new StunIntent(), new BuffIntent() });
		MoveState phaseTwoHeavyStrengthState = new MoveState("PHASE_TWO_HEAVY_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseTwoHeavyMove, new AbstractIntent[] { new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseTwoHeavyDamage)), new BuffIntent() });
		MoveState phaseTwoMultiStrengthState = new MoveState("PHASE_TWO_MULTI_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseTwoMultiMove, new AbstractIntent[] { new DynamicMultiAttackIntent(() => (decimal)PhaseTwoMultiDamage, () => PhaseTwoMultiHits), new BuffIntent() });
		MoveState phaseTwoRetaliationMultiStrengthState = new MoveState("PHASE_TWO_RETALIATION_MULTI_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseTwoRetaliationMultiMove, new AbstractIntent[] { new MultiAttackIntent((int)PhaseTwoMultiDamage, Act4Config.ArchitectP2RetaliationHits), new BuffIntent() });
		MoveState phaseTwoDebuffStrengthState = new MoveState("PHASE_TWO_DEBUFF_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseTwoDebuffMove, new AbstractIntent[] { IsCursedBookChosen() ? (AbstractIntent)new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseTwoHeavyDamage)) : new DebuffIntent(true), new BuffIntent() });
		MoveState phaseTwoSummonStrengthState = new MoveState("PHASE_TWO_SUMMON_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseTwoSummonMove, new AbstractIntent[] { new SummonIntent(), new BuffIntent() });
		MoveState phaseThreeStunnedStrengthState = new MoveState("PHASE_THREE_STUNNED_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseThreeStunnedMove, new AbstractIntent[] { new StunIntent(), new BuffIntent() });
		MoveState phaseThreeHeavyStrengthState = new MoveState("PHASE_THREE_HEAVY_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseThreeHeavyMove, new AbstractIntent[] { new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseThreeHeavyDamage)), new BuffIntent() });
		MoveState phaseThreeMultiStrengthState = new MoveState("PHASE_THREE_MULTI_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseThreeMultiMove, new AbstractIntent[] { new DynamicMultiAttackIntent(() => (decimal)PhaseThreeMultiDamage, () => PhaseThreeMultiHits), new BuffIntent() });
		MoveState phaseThreeDebuffStrengthState = new MoveState("PHASE_THREE_DEBUFF_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseThreeDebuffMove, new AbstractIntent[] { IsCursedBookChosen() ? (AbstractIntent)new SingleAttackIntent((Func<decimal>)(() => (decimal)PhaseThreeHeavyDamage)) : new DebuffIntent(true), new BuffIntent() });
		MoveState phaseThreeSummonStrengthState = new MoveState("PHASE_THREE_SUMMON_STR", (Func<IReadOnlyList<Creature>, Task>)PhaseThreeSummonMove, new AbstractIntent[] { new SummonIntent(), new BuffIntent() });
		RandomBranchState phaseOneAttackStrengthRandomState = new RandomBranchState("PHASE_ONE_RANDOM_STR");
		RandomBranchState phaseTwoAttackStrengthRandomState = new RandomBranchState("PHASE_TWO_RANDOM_STR");
		RandomBranchState phaseThreeAttackStrengthRandomState = new RandomBranchState("PHASE_THREE_RANDOM_STR");

		phaseOneHeavyStrengthState.FollowUpState = phaseOneCadenceSelector;
		phaseOneMultiStrengthState.FollowUpState = phaseOneCadenceSelector;
		phaseOneDebuffStrengthState.FollowUpState = phaseOneCadenceSelector;
		phaseTwoStunnedStrengthState.FollowUpState = phaseTwoCadenceSelector;
		phaseTwoHeavyStrengthState.FollowUpState = phaseTwoCadenceSelector;
		phaseTwoMultiStrengthState.FollowUpState = phaseTwoCadenceSelector;
		phaseTwoRetaliationMultiStrengthState.FollowUpState = phaseTwoCadenceSelector;
		phaseTwoDebuffStrengthState.FollowUpState = phaseTwoCadenceSelector;
		phaseTwoSummonStrengthState.FollowUpState = phaseTwoCadenceSelector;
		phaseThreeStunnedStrengthState.FollowUpState = phaseThreeCadenceSelector;
		phaseThreeHeavyStrengthState.FollowUpState = phaseThreeCadenceSelector;
		phaseThreeMultiStrengthState.FollowUpState = phaseThreeCadenceSelector;
		phaseThreeDebuffStrengthState.FollowUpState = phaseThreeCadenceSelector;
		phaseThreeSummonStrengthState.FollowUpState = phaseThreeCadenceSelector;

		phaseOneCadenceSelector.AddState(phaseOneDebuffStrengthState, () => ShouldQueuePhaseDebuffOnTurn(GetUpcomingPhaseTurnNumberForNextMoveSelection()) && ShouldShowStrengthBuffIntentForUpcomingMove());
		phaseOneCadenceSelector.AddState(phaseOneDebuffState, () => ShouldQueuePhaseDebuffOnTurn(GetUpcomingPhaseTurnNumberForNextMoveSelection()));
		phaseOneCadenceSelector.AddState(phaseOneAttackStrengthRandomState, () => ShouldShowStrengthBuffIntentForUpcomingMove());
		phaseOneCadenceSelector.AddState(phaseOneAttackRandomState, () => true);
		phaseTwoCadenceSelector.AddState(phaseTwoBuffState, () => ShouldQueuePhaseBuffOnTurn(GetUpcomingPhaseTurnNumberForNextMoveSelection()));
		phaseTwoCadenceSelector.AddState(phaseTwoDebuffStrengthState, () => ShouldQueuePhaseDebuffOnTurn(GetUpcomingPhaseTurnNumberForNextMoveSelection()) && ShouldShowStrengthBuffIntentForUpcomingMove());
		phaseTwoCadenceSelector.AddState(phaseTwoDebuffState, () => ShouldQueuePhaseDebuffOnTurn(GetUpcomingPhaseTurnNumberForNextMoveSelection()));
		phaseTwoCadenceSelector.AddState(phaseTwoAttackStrengthRandomState, () => ShouldShowStrengthBuffIntentForUpcomingMove());
		phaseTwoCadenceSelector.AddState(phaseTwoAttackRandomState, () => true);
		phaseThreeCadenceSelector.AddState(phaseThreeCombinedState, () => ShouldQueuePhaseCombinedOnTurn(GetUpcomingPhaseTurnNumberForNextMoveSelection()));
		phaseThreeCadenceSelector.AddState(phaseThreeAttackStrengthRandomState, () => ShouldShowStrengthBuffIntentForUpcomingMove());
		phaseThreeCadenceSelector.AddState(phaseThreeAttackRandomState, () => true);
		reviveBranchState.AddState(phaseFourAttackRandomState, () => IsPhaseFour);
		reviveBranchState.AddState(phaseThreeCadenceSelector, () => IsPhaseThree);
		reviveBranchState.AddState(phaseTwoCadenceSelector, () => IsPhaseTwo && !IsPhaseThree);
		reviveBranchState.AddState(phaseOneCadenceSelector, () => !IsPhaseTwo);

		phaseOneAttackRandomState.AddBranch(phaseOneHeavyState, 1, (MoveRepeatType)2);
		phaseOneAttackRandomState.AddBranch(phaseOneMultiState, 1, (MoveRepeatType)2);
		phaseTwoAttackRandomState.AddBranch(phaseTwoHeavyState, 1, (MoveRepeatType)2);
		phaseTwoAttackRandomState.AddBranch(phaseTwoMultiState, 1, (MoveRepeatType)2);
		phaseThreeAttackRandomState.AddBranch(phaseThreeHeavyState, 1, (MoveRepeatType)2);
		phaseThreeAttackRandomState.AddBranch(phaseThreeMultiState, 1, (MoveRepeatType)2);
		phaseFourAttackRandomState.AddBranch(phaseFourHeavyState, 1, (MoveRepeatType)2);
		phaseFourAttackRandomState.AddBranch(phaseFourMultiState, 1, (MoveRepeatType)2);
		phaseOneAttackStrengthRandomState.AddBranch(phaseOneHeavyStrengthState, 1, (MoveRepeatType)2);
		phaseOneAttackStrengthRandomState.AddBranch(phaseOneMultiStrengthState, 1, (MoveRepeatType)2);
		phaseTwoAttackStrengthRandomState.AddBranch(phaseTwoHeavyStrengthState, 1, (MoveRepeatType)2);
		phaseTwoAttackStrengthRandomState.AddBranch(phaseTwoMultiStrengthState, 1, (MoveRepeatType)2);
		phaseThreeAttackStrengthRandomState.AddBranch(phaseThreeHeavyStrengthState, 1, (MoveRepeatType)2);
		phaseThreeAttackStrengthRandomState.AddBranch(phaseThreeMultiStrengthState, 1, (MoveRepeatType)2);

		_strengthComboMap = new Dictionary<MoveState, MoveState>
		{
			{ phaseOneHeavyState, phaseOneHeavyStrengthState },
			{ phaseOneMultiState, phaseOneMultiStrengthState },
			{ phaseOneDebuffState, phaseOneDebuffStrengthState },
			{ phaseTwoStunnedState, phaseTwoStunnedStrengthState },
			{ phaseTwoHeavyState, phaseTwoHeavyStrengthState },
			{ phaseTwoMultiState, phaseTwoMultiStrengthState },
			{ phaseTwoRetaliationMultiState, phaseTwoRetaliationMultiStrengthState },
			{ phaseTwoDebuffState, phaseTwoDebuffStrengthState },
			{ phaseTwoSummonState, phaseTwoSummonStrengthState },
			{ phaseThreeStunnedState, phaseThreeStunnedStrengthState },
			{ phaseThreeHeavyState, phaseThreeHeavyStrengthState },
			{ phaseThreeMultiState, phaseThreeMultiStrengthState },
			{ phaseThreeDebuffState, phaseThreeDebuffStrengthState },
			{ phaseThreeSummonState, phaseThreeSummonStrengthState },
		};

		return new MonsterMoveStateMachine(new MonsterState[50]
		{
			phaseOneHeavyState, phaseOneMultiState, phaseOneBuffState, phaseOneDebuffState, phaseOneAttackRandomState, phaseOneCadenceSelector,
			phaseTwoHeavyState, phaseTwoMultiState, phaseTwoRetaliationMultiState, phaseTwoStunnedState, phaseTwoAttackRandomState, phaseTwoCadenceSelector,
			phaseThreeHeavyState, phaseThreeMultiState, phaseThreeStunnedState, phaseThreeAttackRandomState, phaseThreeCadenceSelector,
			phaseTwoDebuffState, phaseTwoBuffState, phaseTwoSummonState, phaseThreeSummonState, phaseThreeDebuffState, phaseThreeBuffState,
			phaseThreeEmergencyBuffState, phaseThreeCombinedState, phaseFourHeavyState, phaseFourMultiState, phaseFourBlockState,
			phaseFourBuffState, phaseFourOblivionState, phaseFourAttackRandomState, reviveMoveState, reviveBranchState,
			phaseOneAttackStrengthRandomState, phaseTwoAttackStrengthRandomState, phaseThreeAttackStrengthRandomState,
			phaseOneHeavyStrengthState, phaseOneMultiStrengthState, phaseOneDebuffStrengthState, phaseTwoStunnedStrengthState,
			phaseTwoHeavyStrengthState, phaseTwoMultiStrengthState, phaseTwoRetaliationMultiStrengthState, phaseTwoDebuffStrengthState,
			phaseTwoSummonStrengthState, phaseThreeStunnedStrengthState, phaseThreeHeavyStrengthState, phaseThreeMultiStrengthState,
			phaseThreeDebuffStrengthState, phaseThreeSummonStrengthState,
		}, phaseOneHeavyState);
	}

	private int GetUpcomingPhaseTurnNumberForNextMoveSelection()
	{
		// EN: Follow-up states are chosen before AfterTurnEnd increments _phaseTurnCount.
		//     If we are still on the enemy side, +2 means "the next enemy turn players are about to preview".
		// ZH: 后续状态会在 AfterTurnEnd 给 _phaseTurnCount 加一之前选出。
		//     如果此刻还处于敌方回合侧，就要用 +2 才能对应玩家马上会看到的下一个敌方回合。
		return _phaseTurnCount + ((((MonsterModel)this).CombatState?.CurrentSide == CombatSide.Enemy) ? 2 : 1);
	}

	private static bool ShouldQueuePhaseCombinedOnTurn(int phaseTurnNumber)
	{
		return phaseTurnNumber >= 3 && (phaseTurnNumber - 3) % 6 == 0;
	}

	private static bool ShouldQueuePhaseDebuffOnTurn(int phaseTurnNumber)
	{
		return phaseTurnNumber >= 3 && (phaseTurnNumber - 3) % 4 == 0;
	}

	private static bool ShouldQueuePhaseBuffOnTurn(int phaseTurnNumber)
	{
		return phaseTurnNumber >= 5 && (phaseTurnNumber - 5) % 4 == 0;
	}

	/// EN: Handle end-of-enemy-turn passive scaling. Move cadence is now planned inside the state machine.
	/// ZH: 处理敌方回合结束后的被动成长。招式节奏现在直接在状态机里规划，不再回头补改。
	public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		await base.AfterTurnEnd(choiceContext, side);
		LogArchitect($"AfterTurnEnd side={side} hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp} phase={PhaseNumber} pending={PendingPhaseNumber} awaiting={IsAwaitingPhaseTransition} enemyTurns={_enemyTurnCount} phaseTurns={_phaseTurnCount}");
		if (side != (CombatSide)2 || ((MonsterModel)this).Creature.IsDead || IsAwaitingPhaseTransition)
		{
			LogArchitect("AfterTurnEnd:skipped");
			return;
		}
		_enemyTurnCount++;
		if (IsPhaseFour)
		{
			LogArchitect("AfterTurnEnd:phase-four-skip-strength");
			return;
		}
		_phaseTurnCount++;
		StrengthPower power = ((MonsterModel)this).Creature.GetPower<StrengthPower>();
		int currentStrength = (power != null) ? ((PowerModel)power).Amount : 0;
		int strengthCap = GetCurrentStrengthCap();
		int strengthGain = 0;
		int underCapCadence = GetUnderCapStrengthCadence();
		if (currentStrength < strengthCap && (underCapCadence <= 1 || _enemyTurnCount % underCapCadence == 1))
		{
			strengthGain = Math.Min(GetPassiveStrengthGainPerTrigger(), strengthCap - currentStrength);
			await PowerCmd.Apply<StrengthPower>(((MonsterModel)this).Creature, (decimal)strengthGain, ((MonsterModel)this).Creature, (CardModel)null, false);
			currentStrength += strengthGain;
		}
		else if (currentStrength >= strengthCap && _enemyTurnCount % GetOverCapStrengthCadence() == 0)
		{
			strengthGain = 1;
			await PowerCmd.Apply<StrengthPower>(((MonsterModel)this).Creature, 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
			currentStrength += 1;
		}
		LogArchitect($"AfterTurnEnd:strength current={currentStrength} cap={strengthCap} gain={strengthGain}");
	}

	/// EN: Mark the boss as transitioning and force the revive bridge move.
	/// ZH: 标记Boss进入转阶段，并强制切到复活过桥招式。
	public void BeginAwaitingPhaseTransition(int nextPhaseNumber)
	{
		PendingPhaseNumber = nextPhaseNumber;
		_armPhaseTwoAllOrNothingOnEnemyTurnStart = false;
		_armPhaseThreeJudgmentOnEnemyTurnStart = false;
		// Clear all retaliation state so a concurrent retaliation + death doesn't
		// leave stale flags that block the next turn from starting.
		_isRetaliationEndingTurn = false;
		_hasQueuedRetaliationForNextPlayerTurn = false;
		_hasQueuedRetaliationActionRequested = false;
		if (nextPhaseNumber == 3)
		{
			_pendingPhaseThreeCarriedStrength = ((MonsterModel)this).Creature.GetPower<StrengthPower>()?.Amount ?? 0;
			LogArchitect($"BeginAwaitingPhaseTransition:snapshotted-phase-three-strength value={_pendingPhaseThreeCarriedStrength}");
		}
		LogArchitect($"BeginAwaitingPhaseTransition nextPhase={nextPhaseNumber} hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp}");
		// EN: Every inter-phase down now uses the downed tint + stun VFX so the transition reads clearly.
		// ZH: 现在所有阶段切换前的倒下都使用倒下着色+击晕特效，确保玩家能清晰感知。
		Color transitionTint = ArchitectDownedTint;
		ApplyArchitectVisuals(transitionTint, GetCurrentVisualScale(), preservePosition: true);
		NPowerUpVfx.CreateGhostly(((MonsterModel)this).Creature);
		// EN: Stun VFX  -  the floating "STUNNED" text signals to all players that the boss is downed.
		// ZH: 击晕特效——浮现的"STUNNED"文字提示所有玩家Boss正处于倒下状态。
		AddCombatVfx(NStunnedVfx.Create(((MonsterModel)this).Creature));
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
		if (_reviveState != null)
		{
			((MonsterModel)this).SetMoveImmediate(_reviveState, true);
		}
	}

	/// EN: Resolve the pending phase transition into the concrete phase entry routine.
	/// ZH: 将待处理的转阶段请求落到具体阶段入口流程。
	private async Task ReviveMove(IReadOnlyList<Creature> _)
	{
		LogArchitect($"ReviveMove pendingPhase={PendingPhaseNumber} hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp}");
		if (PendingPhaseNumber == 2)
		{
			await EnterPhaseTwoAsync();
		}
		else if (PendingPhaseNumber == 3)
		{
			await EnterPhaseThreeAsync();
		}
		else if (PendingPhaseNumber == 4)
		{
			await EnterPhaseFourAsync();
		}
	}

	/// EN: Enter phase 2 and rebuild its combat state, buffs, and opener.
	/// ZH: 进入二阶段，并重建对应状态、增益与开场动作。
	public async Task EnterPhaseTwoAsync()
	{
		int phaseTwoMaxHp = Math.Max(1, (int)Math.Ceiling((decimal)PhaseOneMaxHpSnapshot * Act4Config.ArchitectP2HpMultiplier));
		LogArchitect($"EnterPhaseTwo:start hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp} targetMaxHp={phaseTwoMaxHp}");
		PendingPhaseNumber = 0;
		PhaseNumber = 2;
		HasTriggeredPhaseTwoSummon = false;
		HasTriggeredPhaseTwoEmergencyFogmog = false;
		HasTriggeredPhaseTwoRetaliation = false;
		_phaseTurnCount = 0;
		_isHandlingAfterSideTurnStart = false;
		_isRetaliationEndingTurn = false;
		_hasQueuedRetaliationForNextPlayerTurn = false;
		_hasQueuedRetaliationActionRequested = false;
		_armPhaseTwoAllOrNothingOnEnemyTurnStart = false;
		_armPhaseThreeJudgmentOnEnemyTurnStart = false;
		_phaseFourOpeningOblivionPending = false;
		_phaseThreeJudgmentWasAttackedByCard = false;
		_phaseThreeJudgmentTriggeredAttackers.Clear();
		_currentPlayerRoundDamageTaken = 0;
		_lastCompletedPlayerRoundDamagePercent = 20;
		ExternalStunUsedThisPhase = false;
		if (((MonsterModel)this).Creature.GetPower<ArchitectAllOrNothingPower>() != null)
		{
			await PowerCmd.Remove<ArchitectAllOrNothingPower>(((MonsterModel)this).Creature);
		}
		if (((MonsterModel)this).Creature.GetPower<ArchitectJudgmentPower>() != null)
		{
			await PowerCmd.Remove<ArchitectJudgmentPower>(((MonsterModel)this).Creature);
		}
		RemovePhaseThreeJudgmentAuraVfx();
		await SetMaxHpCompatAsync(((MonsterModel)this).Creature, (decimal)phaseTwoMaxHp);
		await CreatureCmd.Heal(((MonsterModel)this).Creature, ((MonsterModel)this).Creature.MaxHp - ((MonsterModel)this).Creature.CurrentHp, playAnim: false);
		((MonsterModel)this).Creature.GetPower<Act4ArchitectRevivalPower>()?.FinishRevive();
		Act4AudioHelper.PlayTmp("plasma_orb_channel.mp3");
		RestoreArchitectReviveUi();
		LogArchitect($"EnterPhaseTwo:healed hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp}");
		await TryApplyPhaseTransitionVisualsAsync("EnterPhaseTwo", ArchitectPhaseTwoTint, 1.1f, movingRightwards: true, ArchitectPhaseTwoSkeletonDataPath);
		UpdateTorchGradient(PurpleFireGradient);
		await SetStrengthAmountAsync(Act4Config.ArchitectP2OpeningStrength);
		await SyncAdaptiveResistancePowerAsync();
		await SyncRetaliationCounterAsync();
		// Intangible intentionally not applied here ? 1 stack vanishes before players
		// can experience it (decrements at end of enemy turn that triggered the transition).
		await EnsurePersistentShieldAsync();
		// Compensating buff (c): Phase 2 Artifact+Slippery (suppressed by Holy Book).
		if (!IsHolyBookChosen())
		{
			int phaseTwoProtectionStacks = GetProtectionStacksForPlayers(1);
			// Pass null applier to bypass MultiplayerScalingModel.ModifyPowerAmountGiven,
			// which would otherwise multiply ArtifactPower/SlipperyPower by (playerCount-1)*2+1.
			await PowerCmd.SetAmount<ArtifactPower>(((MonsterModel)this).Creature, (decimal)phaseTwoProtectionStacks, null, null);
			await PowerCmd.SetAmount<SlipperyPower>(((MonsterModel)this).Creature, (decimal)phaseTwoProtectionStacks, null, null);
		}
		await ApplyBookPhaseStartEffectsAsync();
		LogArchitect($"EnterPhaseTwo:buffs artifact={((MonsterModel)this).Creature.GetPower<ArtifactPower>()?.Amount ?? 0} slippery={((MonsterModel)this).Creature.GetPower<SlipperyPower>()?.Amount ?? 0} strength={((MonsterModel)this).Creature.GetPower<StrengthPower>()?.Amount ?? 0}");
		NPowerUpVfx.CreateGhostly(((MonsterModel)this).Creature);
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
		await CardPileCmd.AddToCombatAndPreview<Dazed>(CombatState.Players.Select(p => p.Creature), PileType.Discard, 8, addedByPlayer: false);
		await Cmd.Wait(2f, false);
		ShowArchitectSpeech("The blueprint changes.\nYou do not.", VfxColor.Purple, 3.2);
		LogArchitect("EnterPhaseTwo:speech-fired");
		if (_phaseTwoBuffState != null)
		{
			LogArchitect("EnterPhaseTwo:set-immediate-buff");
			((MonsterModel)this).SetMoveImmediate(_phaseTwoBuffState, true);
		}
	}

	/// EN: Enter phase 3 and apply its summon/thorns/judgment setup.
	/// ZH: 进入三阶段，并配置召唤、荆棘与审判相关状态。
	public async Task EnterPhaseThreeAsync()
	{
		int phaseThreeMaxHp = Math.Max(1, (int)Math.Ceiling((decimal)PhaseOneMaxHpSnapshot * Act4Config.ArchitectP3HpMultiplier));
		int liveStrength = ((MonsterModel)this).Creature.GetPower<StrengthPower>()?.Amount ?? 0;
		// Prefer the snapshot taken at the moment of phase transition (BeginAwaitingPhaseTransition),
		// because by the time EnterPhaseThreeAsync runs the boss HP is 0 and live powers may differ.
		int carriedStrength = _pendingPhaseThreeCarriedStrength > 0 ? _pendingPhaseThreeCarriedStrength : liveStrength;
		LogArchitect($"EnterPhaseThree:start hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp} targetMaxHp={phaseThreeMaxHp}");
		PendingPhaseNumber = 0;
		PhaseNumber = 3;
		HasTriggeredPhaseThreeSummon = false;
		HasTriggeredPhaseThreeSecondSummon = false;
		HasTriggeredPhaseThreeEmergencyFogmog = false;
		HasTriggeredPhaseThreeRetaliation = false;
		HasTriggeredPhaseThreeHalfHpThorns = false;
		_hasTemporaryPhaseThreeThorns = false;
		_hasPersistentSummonThorns = false;
		_persistentSummonThornsAmount = 0;
		_pendingSummonLinkedThornsSync = true;
		_phaseTurnCount = 0;
		_isHandlingAfterSideTurnStart = false;
		_isRetaliationEndingTurn = false;
		_hasQueuedRetaliationForNextPlayerTurn = false;
		_hasQueuedRetaliationActionRequested = false;
		_armPhaseTwoAllOrNothingOnEnemyTurnStart = false;
		_armPhaseThreeJudgmentOnEnemyTurnStart = false;
		_phaseFourOpeningOblivionPending = false;
		_phaseThreeJudgmentWasAttackedByCard = false;
		_phaseThreeJudgmentTriggeredAttackers.Clear();
		_pendingPhaseThreeCarriedStrength = 0;
		ExternalStunUsedThisPhase = false;
		if (((MonsterModel)this).Creature.GetPower<ArchitectAllOrNothingPower>() != null)
		{
			await PowerCmd.Remove<ArchitectAllOrNothingPower>(((MonsterModel)this).Creature);
		}
		if (((MonsterModel)this).Creature.GetPower<ArchitectJudgmentPower>() != null)
		{
			await PowerCmd.Remove<ArchitectJudgmentPower>(((MonsterModel)this).Creature);
		}
		RemovePhaseThreeJudgmentAuraVfx();
		PlayArchitectFakeDeathAnim();
		ShowPlayerReactionSpeechForPhaseFour();
		await Cmd.Wait(4.5f, false);
		NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal, 180f);
		await SetMaxHpCompatAsync(((MonsterModel)this).Creature, (decimal)phaseThreeMaxHp);
		await CreatureCmd.Heal(((MonsterModel)this).Creature, ((MonsterModel)this).Creature.MaxHp - ((MonsterModel)this).Creature.CurrentHp, playAnim: false);
		((MonsterModel)this).Creature.GetPower<Act4ArchitectRevivalPower>()?.FinishRevive();
		Act4AudioHelper.PlayTmp("plasma_orb_channel.mp3");
		Act4AudioHelper.PlayModBgm("res://Act4Placeholder/audio/spirit_citadel.ogg", 0.6f);
		RestoreArchitectReviveUi();
		LogArchitect($"EnterPhaseThree:healed hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp}");
		await TryApplyPhaseTransitionVisualsAsync("EnterPhaseThree", ArchitectPhaseThreeTint, 1.2f, movingRightwards: false, ArchitectPhaseThreeSkeletonDataPath);
		UpdateTorchGradient(BlackFireGradient);
		EnsurePhaseThreeAura();
		LogArchitect("EnterPhaseThree:aura-ready");
		await SyncAdaptiveResistancePowerAsync();
		// Phase 3: Perpetual Adaptive Armor  -  caps all incoming hits at 999 and retains block between turns.
		await PowerCmd.SetAmount<ArchitectAdaptiveArmorPower>(((MonsterModel)this).Creature, 999m, ((MonsterModel)this).Creature, (CardModel)null);
		await SyncRetaliationCounterAsync();
		await SetStrengthAmountAsync(carriedStrength);
		// Cursed Tome: Architect does not gain Block Piercer.
		if (!IsCursedBookChosen())
		{
			await PowerCmd.SetAmount<ArchitectBlockPiercerPower>(((MonsterModel)this).Creature, (decimal)Act4Config.ArchitectP3BlockPiercerStacks, ((MonsterModel)this).Creature, (CardModel)null);
		}
		int phaseThreeProtectionStacks = GetProtectionStacksForPlayers(4);
		if (!IsHolyBookChosen())
		{
			// Remove first so SetAmount always lands at exactly phaseThreeProtectionStacks,
			// even if Phase 2 accumulated stacks above this target value.
			if (((MonsterModel)this).Creature.GetPower<ArtifactPower>() != null)
				await PowerCmd.Remove<ArtifactPower>(((MonsterModel)this).Creature);
			if (((MonsterModel)this).Creature.GetPower<SlipperyPower>() != null)
				await PowerCmd.Remove<SlipperyPower>(((MonsterModel)this).Creature);
			// Pass null applier to bypass MultiplayerScalingModel.ModifyPowerAmountGiven,
			// which would otherwise multiply ArtifactPower/SlipperyPower by (playerCount-1)*2+1.
			await PowerCmd.SetAmount<ArtifactPower>(((MonsterModel)this).Creature, (decimal)phaseThreeProtectionStacks, null, null);
			await PowerCmd.SetAmount<SlipperyPower>(((MonsterModel)this).Creature, (decimal)phaseThreeProtectionStacks, null, null);
		}
		await EnsurePersistentShieldAsync();
		await ApplyBookPhaseStartEffectsAsync();
		LogArchitect($"EnterPhaseThree:buffs artifact={((MonsterModel)this).Creature.GetPower<ArtifactPower>()?.Amount ?? 0} slippery={((MonsterModel)this).Creature.GetPower<SlipperyPower>()?.Amount ?? 0} strength={((MonsterModel)this).Creature.GetPower<StrengthPower>()?.Amount ?? 0} carriedStrength={carriedStrength} liveStrength={liveStrength}");
		NPowerUpVfx.CreateGhostly(((MonsterModel)this).Creature);
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
		await Cmd.Wait(2f, false);
		ShowArchitectSpeech("Now watch the final draft.", VfxColor.Black, 3.2);
		await PhaseThreeMultiMove(Array.Empty<Creature>());
		LogArchitect("EnterPhaseThree:opening-laser-fired");
		LogArchitect("EnterPhaseThree:speech-fired");
		if (_phaseThreeBuffState != null)
		{
			LogArchitect("EnterPhaseThree:set-immediate-buff");
			((MonsterModel)this).SetMoveImmediate(_phaseThreeBuffState, true);
		}
	}

	/// EN: Enter the final phase, revive players, and switch to the Oblivion ruleset.
	/// ZH: 进入最终阶段，复活玩家，并切换到“湮灭”规则。
	public async Task EnterPhaseFourAsync()
	{
		int phaseFourMaxHp = Math.Max(1, (int)Math.Ceiling((decimal)PhaseOneMaxHpSnapshot * Act4Config.ArchitectP4HpMultiplier));
		LogArchitect($"EnterPhaseFour:start hp={((MonsterModel)this).Creature.CurrentHp}/{((MonsterModel)this).Creature.MaxHp} targetMaxHp={phaseFourMaxHp}");
		PendingPhaseNumber = 0;
		PhaseNumber = 4;
		_phaseTurnCount = 0;
		_isHandlingAfterSideTurnStart = false;
		_isRetaliationEndingTurn = false;
		_hasQueuedRetaliationForNextPlayerTurn = false;
		_hasQueuedRetaliationActionRequested = false;
		_phaseFourRotationIndex = 0;
		_phaseFourOblivionCastCount = 0;
		_phaseFourOpeningOblivionPending = true;
		_phaseFourEightySpeechTriggered = false;
		_phaseFourHalfSpeechTriggered = false;
		_phaseFourDeathSpeechTriggered = false;
		_phaseFourOblivionChargeSpeechTriggered = false;
		_phaseFourOblivionUnlocked = false;
		_phaseFourOblivionCountdownBuffsApplied = false;
		_phaseThreeJudgmentTriggeredAttackers.Clear();
		_armPhaseTwoAllOrNothingOnEnemyTurnStart = false;
		_armPhaseThreeJudgmentOnEnemyTurnStart = false;
		_phaseThreeJudgmentWasAttackedByCard = false;
		ExternalStunUsedThisPhase = false;
		if (((MonsterModel)this).Creature.GetPower<ArchitectAllOrNothingPower>() != null)
		{
			await PowerCmd.Remove<ArchitectAllOrNothingPower>(((MonsterModel)this).Creature);
		}
		if (((MonsterModel)this).Creature.GetPower<ArchitectJudgmentPower>() != null)
		{
			await PowerCmd.Remove<ArchitectJudgmentPower>(((MonsterModel)this).Creature);
		}
		RemovePhaseThreeJudgmentAuraVfx();
		await SetMaxHpCompatAsync(((MonsterModel)this).Creature, (decimal)phaseFourMaxHp);
		await CreatureCmd.Heal(((MonsterModel)this).Creature, ((MonsterModel)this).Creature.MaxHp - ((MonsterModel)this).Creature.CurrentHp, playAnim: false);
		((MonsterModel)this).Creature.GetPower<Act4ArchitectRevivalPower>()?.FinishRevive();
		Act4AudioHelper.PlayTmp("plasma_orb_channel.mp3");
		SetPhaseFourBackgroundDarkened(true);
		// Lerp players toward screen center and Architect further right,
		// matching the Kaiser Crab boss arena feel (centered players, spread combat).
		RepositionCreaturesForPhaseFour();
		await Cmd.Wait(0.35f, false);
		RestoreArchitectReviveUi();
		await TryApplyPhaseTransitionVisualsAsync("EnterPhaseFour", ArchitectPhaseFourTint, 1.2f, movingRightwards: false, ArchitectPhaseThreeSkeletonDataPath);
		UpdateTorchGradient(BlackFireGradient);
		EnsurePhaseThreeAura();
		EnsureMushroomVfx();
		EnsurePhaseFourOblivionAuraVfx();
		await SetStrengthAmountAsync(0);
		await RemoveArchitectPositivePowersForPhaseFourAsync();
		await SyncAdaptiveResistancePowerAsync();
		// Phase 4: Ensure Adaptive Armor amount is correct (persists from Phase 3).
		await PowerCmd.SetAmount<ArchitectAdaptiveArmorPower>(((MonsterModel)this).Creature, 999m, ((MonsterModel)this).Creature, (CardModel)null);
		await PowerCmd.SetAmount<ArchitectOblivionPower>(((MonsterModel)this).Creature, (decimal)GetPhaseFourStartingOblivionStacks(), ((MonsterModel)this).Creature, (CardModel)null);
		UpdatePhaseFourBackgroundOverlayOpacity();
		await CreatureCmd.LoseBlock(((MonsterModel)this).Creature, ((MonsterModel)this).Creature.Block);
		await SyncArchitectBarricadeAsync();
		await SyncRetaliationCounterAsync();
		// Compensating buff (c): Phase 4 Artifact+Slippery (suppressed by Holy Book).
		if (!IsHolyBookChosen())
		{
			int phaseFourProtectionStacks = GetProtectionStacksForPlayers(1);
			// Pass null applier to bypass MultiplayerScalingModel.ModifyPowerAmountGiven,
			// which would otherwise multiply ArtifactPower/SlipperyPower by (playerCount-1)*2+1.
			await PowerCmd.SetAmount<ArtifactPower>(((MonsterModel)this).Creature, (decimal)phaseFourProtectionStacks, null, null);
			await PowerCmd.SetAmount<SlipperyPower>(((MonsterModel)this).Creature, (decimal)phaseFourProtectionStacks, null, null);
		}
		await ApplyBookPhaseStartEffectsAsync();
		NPowerUpVfx.CreateGhostly(((MonsterModel)this).Creature);
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
		// Surrounded: Architect gets → arrow (right side), players get the flanked debuff.
		// The 5 shadows get ← arrows in Phase4LinkedShadow.AfterAddedToRoom.
		if (!((MonsterModel)this).Creature.HasPower<BackAttackRightPower>())
			await PowerCmd.Apply<BackAttackRightPower>(((MonsterModel)this).Creature, 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		foreach (Player p4player in CombatState.Players)
		{
			if (p4player?.Creature != null && !p4player.Creature.HasPower<SurroundedPower>())
				await PowerCmd.Apply<SurroundedPower>(p4player.Creature, 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		}
		// Entry speech first  -  the Architect's defiant reaction to being forced into Phase 4.
		ShowArchitectSpeech(GetPhaseFourEntrySpeech(), VfxColor.Black, 3.2);
		await Cmd.Wait(1.2f, false);
		// Summon all 5 Linked Shadows  -  creating the dire situation the Merchant reacts to.
		await SummonPhaseFourLinkedShadowsAsync();
		await Cmd.Wait(0.3f, false);
		// As shadows materialize: Architect taunts.
		ShowArchitectSpeech(GetPhaseFourShadowSummonSpeech(), VfxColor.Black, 2.8);
		await Cmd.Wait(0.5f, false);
		// Revive dead co-op players to the HP floor before the merchant check.
		await RevivePlayersForPhaseFourAsync();
		// After shadows rise: Merchant NPC appears and heals/revives qualifying players.
		bool merchantOffered = ShouldMerchantHeal();
		await ShowMerchantHealSequenceAsync();
		// Architect dismisses the intervention (only if the merchant actually appeared).
		if (merchantOffered)
		{
			await Cmd.Wait(1.0f, false);
			ShowArchitectSpeech(GetPhaseFourPostMerchantSpeech(), VfxColor.Black, 3.8);
			await Cmd.Wait(1.4f, false);
		}
		if (_phaseFourOblivionState != null)
		{
			((MonsterModel)this).SetMoveImmediate(_phaseFourOblivionState, true);
		}
	}

	private async Task PhaseOneHeavyMove(IReadOnlyList<Creature> _)
	{
		NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short, 180f);
		await DamageCmd.Attack((decimal)PhaseOneHeavyDamage).FromMonster(this).WithAttackerAnim("Attack", 0.5f, (Creature)null)
			.WithAttackerFx((string)null, "event:/sfx/enemy/enemy_attacks/test_subject/test_subject_bite", (string)null)
			.WithHitFx("vfx/vfx_attack_lightning", (string)null, (string)null)
			.Execute((PlayerChoiceContext)null);
	}

	private async Task PhaseOneMultiMove(IReadOnlyList<Creature> _)
	{
		NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short, 180f);
		await DamageCmd.Attack((decimal)PhaseOneMultiDamage).WithHitCount(PhaseOneMultiHits).FromMonster(this)
			.WithAttackerAnim("Attack", 0.35f, (Creature)null)
			.WithWaitBeforeHit(0.12f, 0.16f)
			.WithHitFx("vfx/vfx_attack_lightning", MultiAttackSfx, (string)null)
			.OnlyPlayAnimOnce()
			.Execute((PlayerChoiceContext)null);
	}

	private async Task PhaseOneBuffMove(IReadOnlyList<Creature> _)
	{
		ShowArchitectSpeech("Observe the pattern.", VfxColor.Blue, 3.2);
		if (!IsHolyBookChosen())
		{
			int protectionStacks = GetProtectionStacksForPlayers(1);
			// Use SetAmount(current + delta) instead of Apply so all co-op clients
			// converge to the same value (Apply fires once per client = N× stacks).
			int curArtifact = ((MonsterModel)this).Creature.GetPower<ArtifactPower>()?.Amount ?? 0;
			int curSlippery = ((MonsterModel)this).Creature.GetPower<SlipperyPower>()?.Amount ?? 0;
			await PowerCmd.SetAmount<ArtifactPower>(((MonsterModel)this).Creature, (decimal)(curArtifact + GetSelfArtifactStacks(protectionStacks)), ((MonsterModel)this).Creature, (CardModel)null);
			await PowerCmd.SetAmount<SlipperyPower>(((MonsterModel)this).Creature, (decimal)(curSlippery + protectionStacks), ((MonsterModel)this).Creature, (CardModel)null);
		}
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
	}

	private async Task PhaseOneDebuffMove(IReadOnlyList<Creature> _)
	{
		ShowArchitectSpeech("Hold still.\nI am refining the sketch.", VfxColor.Blue, 3.2);
		if (IsCursedBookChosen())
		{
			await DamageCmd.Attack((decimal)PhaseOneHeavyDamage).FromMonster(this)
				.WithAttackerAnim("Attack", 0.5f, (Creature)null)
				.WithAttackerFx((string)null, HeavyAttackSfx, (string)null)
				.WithHitFx("vfx/vfx_starry_impact", (string)null, (string)null)
				.Execute((PlayerChoiceContext)null);
			return;
		}
		await PowerCmd.Apply<VulnerablePower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		await PowerCmd.Apply<WeakPower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		// EN: Dexterity debuff disabled  -  too punishing; kept for easy re-enable.
		// ZH: 敏捷减益已禁用——惩罚过重；保留代码便于恢复。
		// await PowerCmd.Apply<DexterityPower>(CombatState.Players.Select(p => p.Creature), -1m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}

	private async Task PhaseTwoHeavyMove(IReadOnlyList<Creature> _)
	{
		NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal, 180f);
		await DamageCmd.Attack((decimal)PhaseTwoHeavyDamage).FromMonster(this).WithAttackerAnim("Attack", 0.5f, (Creature)null)
			.WithAttackerFx((string)null, "event:/sfx/enemy/enemy_attacks/test_subject/test_subject_bite", (string)null)
			.WithHitFx("vfx/vfx_starry_impact", (string)null, (string)null)
			.Execute((PlayerChoiceContext)null);
		NGame.Instance?.DoHitStop(ShakeStrength.Weak, ShakeDuration.Short);
	}

	private async Task PhaseTwoMultiMove(IReadOnlyList<Creature> _)
	{
		NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short, 180f);
		SfxCmd.Play("event:/sfx/characters/defect/defect_lightning_channel");
		float originalHyperbeamLaserDuration = NHyperbeamVfx.hyperbeamLaserDuration;
		TrySetHyperbeamLaserDuration(StandardHyperbeamLaserDuration);
		try
		{
			foreach (Player player in ((MonsterModel)this).CombatState.Players)
			{
				if (player?.Creature != null && player.Creature.IsAlive)
				{
					AddMagentaHyperbeamVfx(player.Creature);
				}
			}
			await Cmd.Wait(NHyperbeamVfx.hyperbeamAnticipationDuration + 0.04f, false);
			foreach (Player player2 in ((MonsterModel)this).CombatState.Players)
			{
				if (player2?.Creature != null && player2.Creature.IsAlive)
				{
					AddMagentaHyperbeamImpactVfx(player2.Creature);
				}
			}
			await DamageCmd.Attack((decimal)PhaseTwoMultiDamage).WithHitCount(PhaseTwoMultiHits).FromMonster(this)
				.WithAttackerAnim("Attack", 0.35f, (Creature)null)
				.WithWaitBeforeHit(0.06f, 0.12f)
				.WithHitFx("vfx/vfx_attack_lightning", "event:/sfx/characters/defect/defect_lightning_evoke", MultiAttackSfx)
				.OnlyPlayAnimOnce()
				.Execute((PlayerChoiceContext)null);
		}
		finally
		{
			TrySetHyperbeamLaserDuration(originalHyperbeamLaserDuration);
		}
	}

	private async Task PhaseTwoRetaliationMultiMove(IReadOnlyList<Creature> _)
	{
		NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short, 180f);
		SfxCmd.Play("event:/sfx/characters/defect/defect_lightning_channel");
		float originalHyperbeamLaserDuration = NHyperbeamVfx.hyperbeamLaserDuration;
		TrySetHyperbeamLaserDuration(StandardHyperbeamLaserDuration);
		try
		{
			foreach (Player player in ((MonsterModel)this).CombatState.Players)
			{
				if (player?.Creature != null && player.Creature.IsAlive)
				{
					AddMagentaHyperbeamVfx(player.Creature);
				}
			}
			await Cmd.Wait(NHyperbeamVfx.hyperbeamAnticipationDuration + 0.04f, false);
			foreach (Player player2 in ((MonsterModel)this).CombatState.Players)
			{
				if (player2?.Creature != null && player2.Creature.IsAlive)
				{
					AddMagentaHyperbeamImpactVfx(player2.Creature);
				}
			}
			await DamageCmd.Attack((decimal)PhaseTwoMultiDamage).WithHitCount(Act4Config.ArchitectP2RetaliationHits).FromMonster(this)
				.WithAttackerAnim("Attack", 0.35f, (Creature)null)
				.WithWaitBeforeHit(0.06f, 0.14f)
				.WithHitFx("vfx/vfx_attack_lightning", "event:/sfx/characters/defect/defect_lightning_evoke", MultiAttackSfx)
				.OnlyPlayAnimOnce()
				.Execute((PlayerChoiceContext)null);
		}
		finally
		{
			TrySetHyperbeamLaserDuration(originalHyperbeamLaserDuration);
		}
	}

	private async Task PhaseTwoStunnedMove(IReadOnlyList<Creature> _)
	{
		await PowerCmd.Remove<ArchitectStunnedPower>(((MonsterModel)this).Creature);
		ShowArchitectSpeech("The frame slips.", VfxColor.Purple, 2.4);
		await Cmd.Wait(0.7f, false);
	}

	private async Task PhaseThreeStunnedMove(IReadOnlyList<Creature> _)
	{
		await PowerCmd.Remove<ArchitectStunnedPower>(((MonsterModel)this).Creature);
		ShowArchitectSpeech("The verdict recoils.", VfxColor.Black, 2.4);
		await Cmd.Wait(0.7f, false);
	}

	private async Task PhaseTwoDebuffMove(IReadOnlyList<Creature> _)
	{
		// Counter is intentionally NOT reset here ? it continues ticking toward 4 so
		// PhaseTwoBuffMove still fires on schedule (2 turns after this debuff).
		ShowArchitectSpeech("Stand still.\nI need cleaner measurements.", VfxColor.Purple, 3.2);
		if (IsCursedBookChosen())
		{
			// Cursed Book: forbidden knowledge converts debuffs into raw aggression.
			await DamageCmd.Attack((decimal)PhaseTwoHeavyDamage).FromMonster(this)
				.WithAttackerAnim("Attack", 0.5f, (Creature)null)
				.WithAttackerFx((string)null, "event:/sfx/enemy/enemy_attacks/test_subject/test_subject_bite", (string)null)
				.WithHitFx("vfx/vfx_starry_impact", (string)null, (string)null)
				.Execute((PlayerChoiceContext)null);
			return;
		}
		await PowerCmd.Apply<VulnerablePower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		await PowerCmd.Apply<WeakPower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		await PowerCmd.Apply<ChainsOfBindingPower>(CombatState.Players.Select(p => p.Creature), 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}

	private async Task PhaseThreeDebuffMove(IReadOnlyList<Creature> _)
	{
		ShowArchitectSpeech("Kneel.\nYour form is failing.", VfxColor.Black, 3.2);
		if (IsCursedBookChosen())
		{
			await DamageCmd.Attack((decimal)PhaseThreeHeavyDamage).FromMonster(this)
				.WithAttackerAnim("Attack", 0.5f, (Creature)null)
				.WithAttackerFx((string)null, HeavyAttackSfx, (string)null)
				.WithHitFx("vfx/monsters/kaiser_crab_boss_explosion", (string)null, (string)null)
				.Execute((PlayerChoiceContext)null);
			return;
		}
		await PowerCmd.Apply<VulnerablePower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		await PowerCmd.Apply<WeakPower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		await PowerCmd.Apply<ChainsOfBindingPower>(CombatState.Players.Select(p => p.Creature), 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}

	private async Task PhaseTwoBuffMove(IReadOnlyList<Creature> _)
	{
		ShowArchitectSpeech("I reinforce the frame.\nYou crack first.", VfxColor.Purple, 3.2);
		if (!IsHolyBookChosen())
		{
			int protectionStacks = GetProtectionStacksForPlayers(1);
			int curArtifact = ((MonsterModel)this).Creature.GetPower<ArtifactPower>()?.Amount ?? 0;
			int curSlippery = ((MonsterModel)this).Creature.GetPower<SlipperyPower>()?.Amount ?? 0;
			await PowerCmd.SetAmount<ArtifactPower>(((MonsterModel)this).Creature, (decimal)(curArtifact + GetSelfArtifactStacks(protectionStacks)), ((MonsterModel)this).Creature, (CardModel)null);
			await PowerCmd.SetAmount<SlipperyPower>(((MonsterModel)this).Creature, (decimal)(curSlippery + protectionStacks), ((MonsterModel)this).Creature, (CardModel)null);
		}
		await GainArchitectBlockCappedAsync((decimal)Math.Max(6, (int)Math.Ceiling((decimal)((MonsterModel)this).Creature.MaxHp / 24m)));
		await PowerCmd.Apply<WeakPower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		await PowerCmd.Apply<ShrinkPower>(CombatState.Players.Select(p => p.Creature), -1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		await PowerCmd.Apply<ChainsOfBindingPower>(CombatState.Players.Select(p => p.Creature), 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
	}

	private async Task PhaseTwoSummonMove(IReadOnlyList<Creature> _)
	{
		ShowArchitectSpeech(IsPhaseTwo ? "Knights, to me.\nHold the line." : "Bot drones.\nKeep them busy.", VfxColor.Purple, 3.2);
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
		VfxCmd.PlayOnCreatureCenter(((MonsterModel)this).Creature, "vfx/vfx_gaze");
		Act4AudioHelper.PlayTmp("doom_apply.mp3");
		SfxCmd.Play("event:/sfx/enemy/enemy_attacks/two_tail_rats/two_tail_rats_summon");
		if (IsPhaseTwo)
		{
			await SummonPhaseTwoKnightsAsync();
		}
		else
		{
			await SummonPhaseOneBotsAsync();
		}
	}

	private async Task PhaseThreeHeavyMove(IReadOnlyList<Creature> _)
	{
		NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal, 180f);
		await DamageCmd.Attack((decimal)PhaseThreeHeavyDamage).FromMonster(this).WithAttackerAnim("Attack", 0.5f, (Creature)null)
			.WithAttackerFx((string)null, "event:/sfx/enemy/enemy_attacks/test_subject/test_subject_bite", (string)null)
			.WithHitFx("vfx/monsters/kaiser_crab_boss_explosion", (string)null, (string)null)
			.Execute((PlayerChoiceContext)null);
		NGame.Instance?.DoHitStop(ShakeStrength.Weak, ShakeDuration.Short);
	}

	private async Task PhaseThreeMultiMove(IReadOnlyList<Creature> _)
	{
		NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Normal, 180f);
		SfxCmd.Play("event:/sfx/characters/defect/defect_lightning_channel");
		float originalHyperbeamLaserDuration = NHyperbeamVfx.hyperbeamLaserDuration;
		TrySetHyperbeamLaserDuration(StandardHyperbeamLaserDuration);
		try
		{
			foreach (Player player in ((MonsterModel)this).CombatState.Players)
			{
				if (player?.Creature != null && player.Creature.IsAlive)
				{
					AddBlackHyperbeamVfx(player.Creature);
				}
			}
			await Cmd.Wait(NHyperbeamVfx.hyperbeamAnticipationDuration + 0.04f, false);
			foreach (Player player2 in ((MonsterModel)this).CombatState.Players)
			{
				if (player2?.Creature != null && player2.Creature.IsAlive)
				{
					AddBlackHyperbeamImpactVfx(player2.Creature);
				}
			}
			await DamageCmd.Attack((decimal)PhaseThreeMultiDamage).WithHitCount(PhaseThreeMultiHits).FromMonster(this)
				.WithAttackerAnim("Attack", 0.35f, (Creature)null)
				.WithWaitBeforeHit(0.06f, 0.1f)
				.WithHitFx("vfx/vfx_starry_impact", "event:/sfx/characters/defect/defect_lightning_evoke", "heavy_attack.mp3")
				.OnlyPlayAnimOnce()
				.Execute((PlayerChoiceContext)null);
		}
		finally
		{
			TrySetHyperbeamLaserDuration(originalHyperbeamLaserDuration);
		}
		NGame.Instance?.DoHitStop(ShakeStrength.Weak, ShakeDuration.Short);
	}

	private async Task PhaseThreeSummonMove(IReadOnlyList<Creature> _)
	{
		ShowArchitectSpeech("Does this shadow feel familiar?", VfxColor.Black, 3.2);
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
		VfxCmd.PlayOnCreatureCenter(((MonsterModel)this).Creature, "vfx/vfx_gaze");
		Act4AudioHelper.PlayTmp("doom_apply.mp3");
		SfxCmd.Play("event:/sfx/enemy/enemy_attacks/two_tail_rats/two_tail_rats_summon");
		await SummonPhaseThreeShadowsAsync(2);
		await SyncSummonLinkedThornsAsync();
	}

	private async Task PhaseThreeBuffMove(IReadOnlyList<Creature> _)
	{
		ShowArchitectSpeech("Touch this design,\nand bleed for it.", VfxColor.Black, 3.2);
		await PowerCmd.Apply<WeakPower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		int curArtifact = ((MonsterModel)this).Creature.GetPower<ArtifactPower>()?.Amount ?? 0;
		int curSlippery = ((MonsterModel)this).Creature.GetPower<SlipperyPower>()?.Amount ?? 0;
		await PowerCmd.SetAmount<ArtifactPower>(((MonsterModel)this).Creature, (decimal)(curArtifact + GetSelfArtifactStacks(1)), ((MonsterModel)this).Creature, (CardModel)null);
		await PowerCmd.SetAmount<SlipperyPower>(((MonsterModel)this).Creature, (decimal)(curSlippery + 1), ((MonsterModel)this).Creature, (CardModel)null);
		await GainArchitectBlockCappedAsync((decimal)Math.Max(1, (int)Math.Ceiling((decimal)((MonsterModel)this).Creature.MaxHp / 40m)));
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
	}

	private async Task PhaseThreeCombinedMove(IReadOnlyList<Creature> _)
	{
		ShowArchitectSpeech("Kneel and bleed for it.\nThe design holds.", VfxColor.Black, 3.2);
		if (IsCursedBookChosen())
		{
			await DamageCmd.Attack((decimal)PhaseThreeHeavyDamage).FromMonster(this)
				.WithAttackerAnim("Attack", 0.5f, (Creature)null)
				.WithAttackerFx((string)null, HeavyAttackSfx, (string)null)
				.WithHitFx("vfx/monsters/kaiser_crab_boss_explosion", (string)null, (string)null)
				.Execute((PlayerChoiceContext)null);
			await PowerCmd.Apply<WeakPower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		}
		else
		{
			await PowerCmd.Apply<VulnerablePower>(CombatState.Players.Select(p => p.Creature), 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
			await PowerCmd.Apply<WeakPower>(CombatState.Players.Select(p => p.Creature), 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
			await PowerCmd.Apply<ChainsOfBindingPower>(CombatState.Players.Select(p => p.Creature), 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
		}
		int curArtifact = ((MonsterModel)this).Creature.GetPower<ArtifactPower>()?.Amount ?? 0;
		int curSlippery = ((MonsterModel)this).Creature.GetPower<SlipperyPower>()?.Amount ?? 0;
		await PowerCmd.SetAmount<ArtifactPower>(((MonsterModel)this).Creature, (decimal)(curArtifact + GetSelfArtifactStacks(1)), ((MonsterModel)this).Creature, (CardModel)null);
		await PowerCmd.SetAmount<SlipperyPower>(((MonsterModel)this).Creature, (decimal)(curSlippery + 1), ((MonsterModel)this).Creature, (CardModel)null);
		await GainArchitectBlockCappedAsync((decimal)Math.Max(1, (int)Math.Ceiling((decimal)((MonsterModel)this).Creature.MaxHp / 40m)));
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
	}

	private async Task PhaseThreeEmergencyBuffMove(IReadOnlyList<Creature> _)
	{
		int stacks = GetSelfArtifactStacks(GetProtectionStacksForPlayers(10));
		ShowArchitectSpeech("Ignore your shadows,\nand I keep the profit.", VfxColor.Black, 3.6);
		await PowerCmd.Apply<ArtifactPower>(((MonsterModel)this).Creature, (decimal)stacks, ((MonsterModel)this).Creature, (CardModel)null, false);
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
	}

	private async Task PhaseFourHeavyMove(IReadOnlyList<Creature> _)
	{
		NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal, 180f);
		await DamageCmd.Attack((decimal)PhaseFourHeavyDamage).FromMonster(this).WithAttackerAnim("Attack", 0.5f, (Creature)null)
			.WithAttackerFx((string)null, HeavyAttackSfx, (string)null)
			.WithHitFx("vfx/monsters/kaiser_crab_boss_explosion", (string)null, (string)null)
			.Execute((PlayerChoiceContext)null);
		NGame.Instance?.DoHitStop(ShakeStrength.Weak, ShakeDuration.Short);
	}

	private async Task PhaseFourMultiMove(IReadOnlyList<Creature> _)
	{
		if (!_phaseFourTurn1SpeechFired)
		{
			_phaseFourTurn1SpeechFired = true;
			ShowArchitectSpeech(GetPhaseFourTurn1Speech(), VfxColor.Black, 2.8);
		}
		NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Normal, 180f);
		SfxCmd.Play("event:/sfx/characters/defect/defect_lightning_channel");
		float originalHyperbeamLaserDuration = NHyperbeamVfx.hyperbeamLaserDuration;
		TrySetHyperbeamLaserDuration(StandardHyperbeamLaserDuration);
		try
		{
			foreach (Player player in ((MonsterModel)this).CombatState.Players)
			{
				if (player?.Creature != null && player.Creature.IsAlive)
				{
					AddBlackHyperbeamVfx(player.Creature);
				}
			}
			await Cmd.Wait(NHyperbeamVfx.hyperbeamAnticipationDuration + 0.04f, false);
			foreach (Player player2 in ((MonsterModel)this).CombatState.Players)
			{
				if (player2?.Creature != null && player2.Creature.IsAlive)
				{
					AddBlackHyperbeamImpactVfx(player2.Creature);
				}
			}
			await DamageCmd.Attack((decimal)PhaseFourMultiDamage).WithHitCount(PhaseFourMultiHits).FromMonster(this)
				.WithAttackerAnim("Attack", 0.35f, (Creature)null)
				.WithWaitBeforeHit(0.06f, 0.1f)
				.WithHitFx("vfx/vfx_starry_impact", "event:/sfx/characters/defect/defect_lightning_evoke", "heavy_attack.mp3")
				.OnlyPlayAnimOnce()
				.Execute((PlayerChoiceContext)null);
		}
		finally
		{
			TrySetHyperbeamLaserDuration(originalHyperbeamLaserDuration);
		}
		NGame.Instance?.DoHitStop(ShakeStrength.Weak, ShakeDuration.Short);
	}

	private async Task PhaseFourBuffMove(IReadOnlyList<Creature> _)
	{
		int protectionStacks = GetProtectionStacksForPlayers(1);
		ShowArchitectSpeech("Only dust remains.", VfxColor.Black, 2.8);
		int curArtifact = ((MonsterModel)this).Creature.GetPower<ArtifactPower>()?.Amount ?? 0;
		int curSlippery = ((MonsterModel)this).Creature.GetPower<SlipperyPower>()?.Amount ?? 0;
		await PowerCmd.SetAmount<ArtifactPower>(((MonsterModel)this).Creature, (decimal)(curArtifact + protectionStacks), ((MonsterModel)this).Creature, (CardModel)null);
		await PowerCmd.SetAmount<SlipperyPower>(((MonsterModel)this).Creature, (decimal)(curSlippery + protectionStacks), ((MonsterModel)this).Creature, (CardModel)null);
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
	}

	private async Task PhaseFourOblivionMove(IReadOnlyList<Creature> _)
	{
		bool isOpeningTasteTest = _phaseFourOpeningOblivionPending;
		if (!isOpeningTasteTest)
		{
			_phaseFourOblivionUnlocked = true;
		}
		ShowArchitectSpeech("OBLIVION.", VfxColor.Black, 3.2);
		NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal, 180f);
		SfxCmd.Play("event:/sfx/characters/defect/defect_dark_channel");
		int oblivionDamage = GetPhaseFourOblivionDamage();
		float originalHyperbeamLaserDuration = NHyperbeamVfx.hyperbeamLaserDuration;
		TrySetHyperbeamLaserDuration(OblivionHyperbeamLaserDuration);
		try
		{
			foreach (Player player in ((MonsterModel)this).CombatState.Players)
			{
				if (player?.Creature != null && player.Creature.IsAlive)
				{
					AddOblivionHyperbeamVfx(player.Creature);
				}
			}
			await Cmd.Wait(NHyperbeamVfx.hyperbeamAnticipationDuration + 0.2f, false);
			foreach (Player player2 in ((MonsterModel)this).CombatState.Players)
			{
				if (player2?.Creature != null && player2.Creature.IsAlive)
				{
					AddOblivionHyperbeamImpactVfx(player2.Creature);
				}
			}
			await DamageCmd.Attack((decimal)oblivionDamage).WithHitCount(8).FromMonster(this)
				.WithAttackerAnim("Attack", 0.3f, (Creature)null)
				.WithAttackerFx((string)null, "event:/sfx/characters/defect/defect_lightning_channel", (string)null)
				.WithWaitBeforeHit(0.06f, 0.1f)
				.WithHitFx("vfx/vfx_starry_impact", "event:/sfx/characters/defect/defect_lightning_evoke", "heavy_attack.mp3")
				.OnlyPlayAnimOnce()
				.Execute((PlayerChoiceContext)null);
			if (isOpeningTasteTest)
			{
				_phaseFourOpeningOblivionPending = false;
			}
			else
			{
				_phaseFourOblivionCastCount++;
			}
		}
		finally
		{
			TrySetHyperbeamLaserDuration(originalHyperbeamLaserDuration);
		}
	}

}
