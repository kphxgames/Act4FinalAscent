//=============================================================================
// ArchitectShadowChampion.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Abstract base class for dark-tinted Shadow Champion copies of player characters summoned by the Architect, with configurable HP/damage multipliers and shared buff/hex move scaffolding.
// ZH: 建筑师召唤的「影子冠军」系列角色副本的抽象基类，提供可配置的HP/伤害倍率及共用的增益/诅咒行动框架。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Act4Placeholder;

public abstract class ArchitectShadowChampion : MonsterModel
{
	private const string ShadowHeavyAttackSfx = "event:/sfx/enemy/enemy_attacks/test_subject/test_subject_bite";

	private const string ShadowMultiAttackSfx = "event:/sfx/enemy/enemy_attacks/test_subject/test_subject_slash";

	private static readonly Color ShadowTint = new Color(0.04f, 0.04f, 0.04f, 1f);

	public decimal BonusHpMultiplier { get; set; } = 1m;

	public decimal BonusDamageMultiplier { get; set; } = 1m;

	// Flat damage reductions applied by the Shadow Book choice (set externally before the shadow enters combat).
	public int FlatMultiDamagePenalty { get; set; } = 0;
	public int FlatHeavyDamagePenalty { get; set; } = 0;

	// Flat damage bonuses applied for co-op player-count scaling (set externally before the shadow enters combat).
	public int FlatMultiDamageBonus { get; set; } = 0;
	public int FlatHeavyDamageBonus { get; set; } = 0;

	protected abstract string ShadowVisualsPath { get; }

	protected abstract int BaseMultiDamage { get; }

	protected abstract int MultiHits { get; }

	protected abstract int BaseHeavyDamage { get; }

	protected int MultiDamage => Math.Max(1, (int)Math.Ceiling((decimal)BaseMultiDamage * BonusDamageMultiplier) - FlatMultiDamagePenalty + FlatMultiDamageBonus);

	protected int HeavyDamage => Math.Max(1, (int)Math.Ceiling((decimal)BaseHeavyDamage * BonusDamageMultiplier) - FlatHeavyDamagePenalty + FlatHeavyDamageBonus);

	protected override string VisualsPath => ShadowVisualsPath;

	public override int MinInitialHp => 170;

	public override int MaxInitialHp => ((MonsterModel)this).MinInitialHp;

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		NCombatRoom instance = NCombatRoom.Instance;
		NCreature creatureNode = ((instance != null) ? instance.GetCreatureNode(((MonsterModel)this).Creature) : null);
		creatureNode?.SetDefaultScaleTo(0.95f, 0.15f);
		if (creatureNode?.Visuals != null)
		{
			Vector2 visualScale = ((Node2D)creatureNode.Visuals).Scale;
			visualScale.X = Math.Abs(visualScale.X);
			visualScale.Y = Math.Abs(visualScale.Y);
			((Node2D)creatureNode.Visuals).Scale = visualScale;
			((CanvasItem)creatureNode.Visuals).Modulate = ShadowTint;
		}
		CanvasGroup canvasGroup = ((Node)creatureNode)?.GetNodeOrNull<CanvasGroup>(new NodePath("%CanvasGroup"));
		if (canvasGroup != null)
		{
			((CanvasItem)canvasGroup).SetSelfModulate(ShadowTint);
		}
		creatureNode?.ScaleTo(1f, 0f);
		if (creatureNode?.Body != null && creatureNode.Body.Scale.X > 0f)
		{
			Node2D body = creatureNode.Body;
			body.Scale *= new Vector2(-1f, 1f);
		}
	}

	public override void BeforeRemovedFromRoom()
	{
		base.BeforeRemovedFromRoom();
		Act4ArchitectBoss architect = (((MonsterModel)this).CombatState?.Enemies ?? Array.Empty<Creature>()).FirstOrDefault(c => c.Monster is Act4ArchitectBoss)?.Monster as Act4ArchitectBoss;
		if (architect != null)
		{
			TaskHelper.RunSafely(architect.SyncSummonLinkedThornsAsync());
		}
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState val = new MoveState("HEAVY_PRE_HEX", (Func<IReadOnlyList<Creature>, Task>)HeavyMove, new AbstractIntent[1] { new SingleAttackIntent((Func<decimal>)(() => (decimal)HeavyDamage)) });
		MoveState val2 = new MoveState("MULTI_OPEN", (Func<IReadOnlyList<Creature>, Task>)MultiMove, new AbstractIntent[1] { new DynamicMultiAttackIntent(() => (decimal)MultiDamage, () => MultiHits) });
		MoveState val3 = new MoveState("BUFF", (Func<IReadOnlyList<Creature>, Task>)BuffMove, GetBuffIntents());
		MoveState val4 = new MoveState("MULTI_PRE_HEX", (Func<IReadOnlyList<Creature>, Task>)MultiMove, new AbstractIntent[1] { new DynamicMultiAttackIntent(() => (decimal)MultiDamage, () => MultiHits) });
		MoveState val5 = new MoveState("HEX", (Func<IReadOnlyList<Creature>, Task>)HexMove, new AbstractIntent[2]
		{
			new DebuffIntent(false),
			new BuffIntent()
		});
		MoveState val6 = new MoveState("HEAVY_LOOP", (Func<IReadOnlyList<Creature>, Task>)HeavyMove, new AbstractIntent[1] { new SingleAttackIntent((Func<decimal>)(() => (decimal)HeavyDamage)) });
		MoveState val7 = new MoveState("MULTI_LOOP", (Func<IReadOnlyList<Creature>, Task>)MultiMove, new AbstractIntent[1] { new DynamicMultiAttackIntent(() => (decimal)MultiDamage, () => MultiHits) });
		val2.FollowUpState = val3;
		val3.FollowUpState = val;
		val.FollowUpState = val4;
		val4.FollowUpState = val5;
		val5.FollowUpState = val6;
		val6.FollowUpState = val7;
		val7.FollowUpState = val6;
		return new MonsterMoveStateMachine(new MonsterState[7]
		{
			val,
			val2,
			val3,
			val4,
			val5,
			val6,
			val7
		}, val2);
	}

	protected virtual AbstractIntent[] GetBuffIntents()
	{
		return new AbstractIntent[1]
		{
			new BuffIntent()
		};
	}

	protected virtual async Task HeavyMove(IReadOnlyList<Creature> _)
	{
		AttackCommand attackCommand = await DamageCmd.Attack((decimal)HeavyDamage).FromMonster(this).WithAttackerAnim("Attack", 0.2f, (Creature)null)
			.WithAttackerFx((string)null, "event:/sfx/enemy/enemy_attacks/test_subject/test_subject_bite", (string)null)
			.WithHitFx("vfx/vfx_giant_horizontal_slash", (string)null, (string)null)
			.Execute((PlayerChoiceContext)null);
		await AfterHeavyAttackAsync(attackCommand);
	}

	protected virtual async Task BuffMove(IReadOnlyList<Creature> _)
	{
		await PowerCmd.SetAmount<ArchitectBlockPiercerPower>(((MonsterModel)this).Creature, 5m, ((MonsterModel)this).Creature, (CardModel)null);
	}

	protected virtual Task MultiMove(IReadOnlyList<Creature> _)
	{
		return MultiMoveImpl();
	}

	private async Task MultiMoveImpl()
	{
		AttackCommand attackCommand = await DamageCmd.Attack((decimal)MultiDamage).WithHitCount(MultiHits).FromMonster(this)
			.WithAttackerAnim("Attack", 0.15f, (Creature)null)
			.WithAttackerFx((string)null, "event:/sfx/enemy/enemy_attacks/test_subject/test_subject_slash", (string)null)
			.WithHitFx("vfx/vfx_scratch", (string)null, (string)null)
			.OnlyPlayAnimOnce()
			.Execute((PlayerChoiceContext)null);
		await AfterMultiAttackAsync(attackCommand);
	}

	protected virtual async Task HexMove(IReadOnlyList<Creature> _)
	{
		await PowerCmd.Apply<WeakPower>(CombatState.Players.Select(p => p.Creature), 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
		await PowerCmd.Apply<StrengthPower>(((MonsterModel)this).Creature, 2m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}

	protected virtual Task AfterHeavyAttackAsync(AttackCommand command)
	{
		return Task.CompletedTask;
	}

	protected virtual Task AfterMultiAttackAsync(AttackCommand command)
	{
		return Task.CompletedTask;
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		AnimState val = new AnimState("idle_loop", true);
		AnimState val2 = new AnimState("cast", false);
		AnimState val3 = new AnimState("attack", false);
		AnimState val4 = new AnimState("hurt", false);
		AnimState val5 = new AnimState("die", false);
		val2.NextState = val;
		val3.NextState = val;
		val4.NextState = val;
		CreatureAnimator val6 = new CreatureAnimator(val, controller);
		val6.AddAnyState("Idle", val, (Func<bool>)null);
		val6.AddAnyState("Cast", val2, (Func<bool>)null);
		val6.AddAnyState("Attack", val3, (Func<bool>)null);
		val6.AddAnyState("Hit", val4, (Func<bool>)null);
		val6.AddAnyState("Dead", val5, (Func<bool>)null);
		return val6;
	}
}
