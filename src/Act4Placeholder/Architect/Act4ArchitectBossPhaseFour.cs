//=============================================================================
// Act4ArchitectBossPhaseFour.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Phase 4 combat rotation, Oblivion countdown handling, and Phase 4-only speech/state helpers.
// ZH: 建筑师第四阶段的战斗轮转、湮灭倒计时处理，以及四阶段专属对白与状态逻辑。
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

namespace Act4Placeholder;

public sealed partial class Act4ArchitectBoss : MonsterModel
{
	private int _phaseFourRotationIndex;

	private int _phaseFourOblivionCastCount;

	private bool _phaseFourOpeningOblivionPending;

	private bool _phaseFourOblivionUnlocked;

	private bool _phaseFourOblivionCountdownBuffsApplied;

	private bool _phaseFourHalfSpeechTriggered;

	private bool _phaseFourEightySpeechTriggered;

	private bool _phaseFourDeathSpeechTriggered;

	private bool _phaseFourOblivionChargeSpeechTriggered;

	private bool _phaseFourTurn1SpeechFired;

	private ColorRect? _phaseFourBackgroundOverlay;

	private bool IsAnyNearShadowAlive()
	{
		return ((MonsterModel)this).CombatState?.Enemies?
			.Any(c => c.IsAlive && c.Monster is (LinkedShadowDefect or LinkedShadowRegent)) ?? false;
	}

	private async Task PhaseFourBlockMove(IReadOnlyList<Creature> _)
	{
		if (!_phaseFourTurn1SpeechFired)
		{
			_phaseFourTurn1SpeechFired = true;
			ShowArchitectSpeech(GetPhaseFourTurn1Speech(), VfxColor.Black, 2.8);
		}
		if (IsSilverBookChosen())
		{
			return;
		}
		// Block: 3% of max HP (the left side is attacking, so Architect gets a lighter buff block).
		int blockAmount = Math.Max(1, (int)Math.Ceiling((decimal)((MonsterModel)this).Creature.MaxHp * 0.03m));
		await CreatureCmd.GainBlock(((MonsterModel)this).Creature, (decimal)blockAmount, ValueProp.Move, null);
		// +1 Slippery, +1 Artifact while the left-side shadows handle the offense.
		int curSlippery = ((MonsterModel)this).Creature.GetPower<SlipperyPower>()?.Amount ?? 0;
		int curArtifact = ((MonsterModel)this).Creature.GetPower<ArtifactPower>()?.Amount ?? 0;
		await PowerCmd.SetAmount<SlipperyPower>(((MonsterModel)this).Creature, (decimal)(curSlippery + 1), ((MonsterModel)this).Creature, (CardModel)null);
		await PowerCmd.SetAmount<ArtifactPower>(((MonsterModel)this).Creature, (decimal)(curArtifact + 1), ((MonsterModel)this).Creature, (CardModel)null);
		NPowerUpVfx.CreateNormal(((MonsterModel)this).Creature);
	}

	private void ScheduleNextPhaseFourMove()
	{
		if (!IsPhaseFour || _phaseFourOblivionState == null || _phaseFourHeavyState == null || _phaseFourMultiState == null || _phaseFourBuffState == null || _phaseFourBlockState == null)
		{
			return;
		}
		if (_phaseFourOpeningOblivionPending)
		{
			((MonsterModel)this).SetMoveImmediate(_phaseFourOblivionState, true);
			return;
		}
		// After OBLIVION has been cast once, force it every enemy turn.
		if (_phaseFourOblivionUnlocked || _phaseFourOblivionCastCount > 0)
		{
			((MonsterModel)this).SetMoveImmediate(_phaseFourOblivionState, true);
			return;
		}
		ArchitectOblivionPower oblivionPower = ((MonsterModel)this).Creature.GetPower<ArchitectOblivionPower>();
		if (oblivionPower != null && ((PowerModel)oblivionPower).Amount <= 0)
		{
			((MonsterModel)this).SetMoveImmediate(_phaseFourOblivionState, true);
			return;
		}
		// Even-indexed turns: BLOCK when near-side shadows (Defect/Regent) are alive, else HEAVY.
		// Odd-indexed turns: MULTI always.
		// This creates an asymmetric rhythm: Left trio attacks when Architect blocks, and vice versa.
		bool isEvenTurn = _phaseFourRotationIndex % 2 == 0;
		MoveState nextState;
		if (isEvenTurn)
		{
			nextState = IsAnyNearShadowAlive() ? _phaseFourBlockState : _phaseFourHeavyState;
		}
		else
		{
			nextState = _phaseFourMultiState;
		}
		_phaseFourRotationIndex = (_phaseFourRotationIndex + 1) % 2;
		((MonsterModel)this).SetMoveImmediate(nextState, true);
	}

	private int GetPhaseFourOblivionDamage()
	{
		const int baseDamagePerHit = 7;
		const int damageIncreasePerCast = 2;
		if (_phaseFourOpeningOblivionPending)
		{
			return Math.Max(1, (int)Math.Ceiling(baseDamagePerHit * Act4Config.ArchitectP4OblivionOpeningScale));
		}
		return Math.Max(1, baseDamagePerHit + _phaseFourOblivionCastCount * damageIncreasePerCast);
	}

	private void TryShowPhaseFourThresholdSpeech()
	{
		int hp = ((MonsterModel)this).Creature.CurrentHp;
		int maxHp = Math.Max(1, ((MonsterModel)this).Creature.MaxHp);
		if (!_phaseFourEightySpeechTriggered && hp <= maxHp * 4 / 5)
		{
			_phaseFourEightySpeechTriggered = true;
			ShowArchitectSpeech(GetPhaseFourSpeech(0), VfxColor.Black, 3.2);
		}
		if (!_phaseFourHalfSpeechTriggered && hp <= maxHp / 2)
		{
			_phaseFourHalfSpeechTriggered = true;
			ShowArchitectSpeech(GetPhaseFourSpeech(1), VfxColor.Black, 3.2);
		}
	}

	private async Task TickPhaseFourOblivionAsync()
	{
		ArchitectOblivionPower oblivionPower = ((MonsterModel)this).Creature.GetPower<ArchitectOblivionPower>();
		if (oblivionPower == null)
		{
			return;
		}
		int nextAmount = Math.Max(0, ((PowerModel)oblivionPower).Amount - 1);
		await PowerCmd.SetAmount<ArchitectOblivionPower>(((MonsterModel)this).Creature, (decimal)nextAmount, ((MonsterModel)this).Creature, (CardModel)null);
		UpdatePhaseFourBackgroundOverlayOpacity();
		UpdatePhaseFourOblivionAuraScale();
		int selfDamage = Math.Max(1, (int)Math.Ceiling((decimal)((MonsterModel)this).Creature.MaxHp * 0.05m));
		int nextHp = Math.Max(1, ((MonsterModel)this).Creature.CurrentHp - selfDamage);
		if (nextHp < ((MonsterModel)this).Creature.CurrentHp)
		{
			await CreatureCmd.SetCurrentHp(((MonsterModel)this).Creature, (decimal)nextHp);
			// Show floating damage number on the Architect identical to card-damage display.
			NDamageNumVfx? oblivionDmgVfx = NDamageNumVfx.Create(((MonsterModel)this).Creature, selfDamage, requireInteractable: false);
			if (oblivionDmgVfx != null)
				NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(oblivionDmgVfx);
		}
		NPowerUpVfx.CreateGhostly(((MonsterModel)this).Creature);
		VfxCmd.PlayOnCreatureCenter(((MonsterModel)this).Creature, "vfx/vfx_gaze");
		if (!_phaseFourOblivionChargeSpeechTriggered)
		{
			_phaseFourOblivionChargeSpeechTriggered = true;
			ShowArchitectSpeech(GetOblivionChargeSpeech(), VfxColor.Black, 3.2);
		}
		TryShowPhaseFourThresholdSpeech();
		if (nextAmount <= 0 && _phaseFourOblivionState != null)
		{
			if (!_phaseFourOblivionCountdownBuffsApplied)
			{
				_phaseFourOblivionCountdownBuffsApplied = true;
				// Cursed Tome: Architect does not gain Block Piercer.
				if (!IsCursedBookChosen())
				{
					await PowerCmd.Apply<ArchitectBlockPiercerPower>(((MonsterModel)this).Creature, 33m, ((MonsterModel)this).Creature, (CardModel)null, false);
				}
				await PowerCmd.Apply<ArtifactPower>(((MonsterModel)this).Creature, 33m, ((MonsterModel)this).Creature, (CardModel)null, false);

				// Oblivion countdown complete: empower all surviving linked shadows with +1 Strength.
				foreach (Creature shadow in ((MonsterModel)this).CombatState?.Enemies ?? Array.Empty<Creature>())
				{
					if (shadow.IsAlive && shadow.Monster is Phase4LinkedShadow)
					{
						await PowerCmd.Apply<StrengthPower>(shadow, 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
					}
				}
			}
			TransitionOblivionAuraToPostOblivion();
			_phaseFourOblivionUnlocked = true;
			((MonsterModel)this).SetMoveImmediate(_phaseFourOblivionState, true);
		}
	}

	private string GetPhaseFourSpeech(int stage)
	{
		uint seed = ((MonsterModel)this).Creature?.CombatState?.RunState?.Rng?.Seed ?? 0u;
		uint combatId = ((MonsterModel)this).Creature?.CombatId ?? 0u;
		int speechIndex = (int)((seed + combatId + (uint)stage) % 2u);
		return stage switch
		{
			0 => (speechIndex == 0)
				? ModLoc.T("The last light dims.", "最后一丝光也将熄灭。",
					fra: "La dernière lumière s'éteint.", deu: "Das letzte Licht erlischt.",
					jpn: "最後の光が薄れる。", kor: "마지막 빛이 사그라든다.",
					por: "A última luz se apaga.", rus: "Последний свет угасает.", spa: "La última luz se desvanece.")
				: ModLoc.T("You are still too slow.", "你还是太慢了。",
					fra: "Vous êtes encore trop lents.", deu: "Ihr seid immer noch zu langsam.",
					jpn: "まだ遅すぎる。", kor: "아직도 너무 느리다.",
					por: "Ainda são lentos demais.", rus: "Вы всё ещё слишком медленны.", spa: "Todavía son demasiado lentos."),
			1 => (speechIndex == 0)
				? ModLoc.T("Resistance... futile.", "抵抗……毫无意义。",
					fra: "Résistance... futile.", deu: "Widerstand... sinnlos.",
					jpn: "抵抗は……無意味。", kor: "저항은... 무의미하다.",
					por: "Resistência... inútil.", rus: "Сопротивление... бесполезно.", spa: "Resistencia... inútil.")
				: ModLoc.T("Soon, only quiet.", "很快，一切都将归于寂静。",
					fra: "Bientôt, le silence seulement.", deu: "Bald nur noch Stille.",
					jpn: "間もなく、静寂のみ。", kor: "곧 침묵만이 남으리라.",
					por: "Em breve, apenas silêncio.", rus: "Скоро останется лишь тишина.", spa: "Pronto, sólo silencio."),
			_ => (speechIndex == 0)
				? ModLoc.T("Then... nothing.", "随后……一切皆空。",
					fra: "Puis... rien.", deu: "Dann... nichts.",
					jpn: "そして……虚無。", kor: "그리고... 무(無).",
					por: "E então... o nada.", rus: "Затем... ничего.", spa: "Luego... nada.")
				: ModLoc.T("The design... fades.", "设计……正在消逝。",
					fra: "La conception... s'efface.", deu: "Das Design... verblasst.",
					jpn: "設計が……消えていく。", kor: "설계가... 사라진다.",
					por: "O projeto... se esvai.", rus: "Замысел... меркнет.", spa: "El diseño... se desvanece.")
		};
	}

	private string GetPhaseFourEntrySpeech()
	{
		uint seed = ((MonsterModel)this).Creature?.CombatState?.RunState?.Rng?.Seed ?? 0u;
		uint combatId = ((MonsterModel)this).Creature?.CombatId ?? 0u;
		return ((seed + combatId + 11u) % 2u == 0u)
			? ModLoc.T("No. Not yet.", "不，还没有。",
				fra: "Non. Pas encore.", deu: "Nein. Noch nicht.",
				jpn: "いや。まだだ。", kor: "아니. 아직.",
				por: "Não. Ainda não.", rus: "Нет. Ещё нет.", spa: "No. Todavía no.")
			: ModLoc.T("You thought it was over?", "你以为已经结束了吗？",
				fra: "Vous pensiez que c'était terminé ?", deu: "Ihr dachtet, es wäre vorbei?",
				jpn: "終わったと思ったか？", kor: "끝났다고 생각했는가?",
				por: "Acharam que tinha acabado?", rus: "Думали, что всё кончено?", spa: "¿Pensaron que había terminado?");
	}

	private string GetPhaseFourShadowSummonSpeech()
	{
		uint seed = ((MonsterModel)this).Creature?.CombatState?.RunState?.Rng?.Seed ?? 0u;
		uint combatId = ((MonsterModel)this).Creature?.CombatId ?? 0u;
		return ((seed + combatId + 12u) % 2u == 0u)
			? ModLoc.T("Rise. Every last one of you.", "起来，全部起来。",
				fra: "Levez-vous. Jusqu'au dernier.", deu: "Erhebt euch. Alle ohne Ausnahme.",
				jpn: "起きろ。残らず全員だ。", kor: "일어나라. 남김없이 모두.",
				por: "Levantai-vos. Até o último.", rus: "Восстаньте. Все до единого.", spa: "Levantaos. Hasta el último.")
			: ModLoc.T("You face... your own shadows.", "你们面对的……是自己的影。",
				fra: "Vous affrontez... vos propres ombres.", deu: "Ihr steht... euren eigenen Schatten gegenüber.",
				jpn: "貴様らは……己の影と向き合う。", kor: "그대들이 마주하는 것은... 자신의 그림자.",
				por: "Vocês enfrentam... as próprias sombras.", rus: "Вы сражаетесь... со своими тенями.", spa: "Se enfrentan... a sus propias sombras.");
	}

	private string GetPhaseFourPostMerchantSpeech()
	{
		uint seed = ((MonsterModel)this).Creature?.CombatState?.RunState?.Rng?.Seed ?? 0u;
		uint combatId = ((MonsterModel)this).Creature?.CombatId ?? 0u;
		return ModLoc.T("A little rat here? It will make no difference.", "这里有只小老鼠？这不会有任何区别。",
			fra: "Un petit rat ici ? Cela ne fera aucune différence.", deu: "Eine kleine Ratte hier? Das macht keinen Unterschied.",
			jpn: "ここに小さなネズミだと？何も変わらない。", kor: "여기에 작은 쥐 한 마리라고? 아무것도 달라지지 않는다.",
			por: "Um ratinho aqui? Isso não fará diferença.", rus: "Маленькая крыса здесь? Это ничего не изменит.", spa: "¿Una pequeña rata aquí? No marcará ninguna diferencia.");
	}

	private string GetPhaseFourTurn1Speech()
	{
		uint seed = ((MonsterModel)this).Creature?.CombatState?.RunState?.Rng?.Seed ?? 0u;
		uint combatId = ((MonsterModel)this).Creature?.CombatId ?? 0u;
		return ((seed + combatId + 23u) % 3u) switch
		{
			0u => ModLoc.T("They are part of me.", "他们都是我的一部分。",
				fra: "Ils font partie de moi.", deu: "Sie sind ein Teil von mir.",
				jpn: "彼らは私の一部だ。", kor: "그들은 나의 일부다.",
				por: "Eles são parte de mim.", rus: "Они  -  часть меня.", spa: "Ellos son parte de mí."),
			1u => ModLoc.T("Witness the complete design.", "见证完整的设计。",
				fra: "Contemplez le design complet.", deu: "Beobachtet das vollständige Design.",
				jpn: "完全な設計を目撃せよ。", kor: "완전한 설계를 목격하라.",
				por: "Testemunhem o design completo.", rus: "Узрите завершённый замысел.", spa: "Presenciad el diseño completo."),
			_ => ModLoc.T("You cannot break what is whole.", "你无法破坏完整的东西。",
				fra: "Vous ne pouvez briser ce qui est entier.", deu: "Ihr könnt nicht brechen, was ganz ist.",
				jpn: "完全なるものを砕くことはできない。", kor: "완전한 것은 부술 수 없다.",
				por: "Não podem quebrar o que é inteiro.", rus: "Вы не сломаете то, что цельно.", spa: "No pueden romper lo que es íntegro."),
		};
	}

	private string GetPlayerPhaseFourReactionSpeech()
	{
		uint seed = ((MonsterModel)this).Creature?.CombatState?.RunState?.Rng?.Seed ?? 0u;
		uint combatId = ((MonsterModel)this).Creature?.CombatId ?? 0u;
		return ((seed + combatId + 7u) % 2u == 0u)
			? ModLoc.T("Did we do it?...", "我们成功了吗？……",
				fra: "On a réussi ?...", deu: "Haben wir es geschafft?...",
				jpn: "やったのか？……", kor: "해낸 건가?...",
				por: "Conseguimos?...", rus: "Мы сделали это?...", spa: "¿Lo logramos?...")
			: ModLoc.T("Is it finally over?...", "终于结束了吗？……",
				fra: "C'est enfin fini ?...", deu: "Ist es endlich vorbei?...",
				jpn: "ついに終わったのか？……", kor: "드디어 끝난 건가?...",
				por: "Finalmente acabou?...", rus: "Это наконец-то кончилось?...", spa: "¿Por fin terminó?...");
	}

	private string GetOblivionChargeSpeech()
	{
		uint seed = ((MonsterModel)this).Creature?.CombatState?.RunState?.Rng?.Seed ?? 0u;
		uint combatId = ((MonsterModel)this).Creature?.CombatId ?? 0u;
		return ((seed + combatId + 19u) % 2u == 0u)
			? ModLoc.T("I will send you to OBLIVION.", "我会把你们送入湮灭。",
				fra: "Je vous enverrai dans L'OUBLI.", deu: "Ich schicke euch ins VERGESSEN.",
				jpn: "貴様らを奈落に送ってやろう。", kor: "너희를 망각의 심연으로 보내주겠다.",
				por: "Vou mandar vocês para o ESQUECIMENTO.", rus: "Я отправлю вас в ЗАБВЕНИЕ.", spa: "Los enviaré al OLVIDO.")
			: ModLoc.T("OBLIVION is all that waits.", "等待你们的，只有湮灭。",
				fra: "L'OUBLI est tout ce qui attend.", deu: "Das VERGESSEN ist alles, was wartet.",
				jpn: "待っているのは奈落だけだ。", kor: "기다리는 것은 오직 망각뿐.",
				por: "O ESQUECIMENTO é tudo que aguarda.", rus: "ЗАБВЕНИЕ - всё, что ждёт вас.", spa: "El OLVIDO es todo lo que aguarda.");
	}

	internal void ShowPhaseFourDeathSpeech()
	{
		if (!IsPhaseFour || _phaseFourDeathSpeechTriggered)
		{
			return;
		}
		_phaseFourDeathSpeechTriggered = true;
		try
		{
			NSpeechBubbleVfx bubble = CreateArchitectSpeechBubble(GetPhaseFourSpeech(2), VfxColor.Black, 3.2, preserveArchitectAnchorWhenDead: true);
			if (bubble == null)
			{
				LogArchitect("ShowPhaseFourDeathSpeech:skipped-no-anchor");
				return;
			}
			AddCombatVfx(bubble);
		}
		catch (Exception ex)
		{
			LogArchitect($"ShowPhaseFourDeathSpeech:failed error={ex.Message}");
		}
	}

	private void PlayArchitectFakeDeathAnim()
	{
		NCombatRoom instance = NCombatRoom.Instance;
		NCreature creatureNode = ((instance != null) ? instance.GetCreatureNode(((MonsterModel)this).Creature) : null);
		if (creatureNode == null)
		{
			return;
		}
		creatureNode.StartDeathAnim(false);
		Tween tween = creatureNode.CreateTween();
		tween.TweenProperty(creatureNode.Visuals, "modulate:a", 0f, 0.45);
	}

	/// EN: Show the allied reaction line when phase 4 begins.
	/// ZH: 在四阶段开始时显示友方反应台词。
	private void ShowPlayerReactionSpeechForPhaseFour()
	{
		if (RunManager.Instance?.NetService?.Type == NetGameType.Client)
		{
			return;
		}
		Player reactingPlayer = ((IEnumerable<Player>)((MonsterModel)this).CombatState.Players).FirstOrDefault((Player p) => p?.Creature != null && p.Creature.IsAlive);
		if (reactingPlayer?.Creature == null)
		{
			return;
		}
		NSpeechBubbleVfx create = NSpeechBubbleVfx.Create(GetPlayerPhaseFourReactionSpeech(), reactingPlayer.Creature, 3.8, VfxColor.Blue);
		AddCombatVfx(create);
	}

	/// EN: Revive dead players for phase 4 and rebuild their combat piles.
	/// ZH: 在四阶段复活已死亡玩家，并重建其战斗牌堆。
	private async Task RevivePlayersForPhaseFourAsync()
	{
		foreach (Player player in ((MonsterModel)this).CombatState.Players)
		{
			Creature creature = player?.Creature;
			if (creature == null)
			{
				continue;
			}

			int reviveFloorHp = Math.Max(1, (int)Math.Ceiling((decimal)creature.MaxHp * Act4Config.ArchitectP4ReviveHpFloor));
			if (!creature.IsDead)
			{
				if (creature.CurrentHp < reviveFloorHp)
				{
					await CreatureCmd.Heal(creature, reviveFloorHp - creature.CurrentHp, playAnim: false);
				}
				continue;
			}

			// Restore HP.
			await CreatureCmd.SetCurrentHp(creature, (decimal)reviveFloorHp);

			// When a player dies, HandlePlayerDeath removes ALL their combat cards
			// (hand, draw, discard, exhaust). Re-clone each deck card back into combat
			// so the player is not left with an empty draw pile.
			var deckCards = player.Deck.Cards.ToList();
			if (deckCards.Count > 0)
			{
				var combatCards = new List<CardModel>();
				foreach (var deckCard in deckCards)
				{
					var combatCard = ((MonsterModel)this).CombatState.CloneCard(deckCard);
					combatCard.DeckVersion = deckCard;
					combatCards.Add(combatCard);
				}
				await CardPileCmd.AddGeneratedCardsToCombat(combatCards, PileType.Draw, addedByPlayer: false);
			}

			// Shuffle the draw pile, then deal a starting hand.
			// Wrapped in try/catch: Hook.AfterShuffle can throw NullRef for dead
			// players in co-op before their creature state is fully restored.
			try
			{
				await CardPileCmd.Shuffle(null, player);
				await CardPileCmd.Draw(null, 5m, player, fromHandDraw: false);
			}
			catch (Exception ex)
			{
				LogArchitect($"RevivePlayersForPhaseFour:shuffle-draw-error ex={ex.Message}");
			}
		}
	}

	private void RemovePhaseFourMushroomVfx()
	{
		Node2D? old = _phaseFourMushroomVfx;
		_phaseFourMushroomVfx = null;
		if (!GodotObject.IsInstanceValid(old) || old == null) return;
		Node? parent = old.GetParent();
		if (parent == null) { old.QueueFree(); return; }
		Tween t = parent.CreateTween();
		t.TweenProperty(old, "modulate:a", 0f, 0.6);
		t.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(old)) old.QueueFree(); }));
	}

	/// EN: When the Oblivion countdown hits 0, keep the aura alive but shift it to a darker brown-red.
	///     Scale bumps from 2.0x to 3.2x to signal the countdown has expired.
	/// ZH: 湮灭倒计时归零时保留光环，但将其切换为更深的棕红色，缩放从2.0倍增至3.2倍以示警告。
	private void TransitionOblivionAuraToPostOblivion()
	{
		if (!GodotObject.IsInstanceValid(_phaseFourOblivionAuraVfx) || _phaseFourOblivionAuraVfx == null) return;
		_phaseFourOblivionAuraPostOblivion = true;
		Node2D aura = _phaseFourOblivionAuraVfx;
		// Tween scale and colour simultaneously  -  dark red/brown at high opacity, larger than countdown max size.
		Tween t = aura.CreateTween();
		t.SetParallel(true);
		t.TweenProperty(aura, "scale", new Vector2(3.2f, 3.2f), 0.8);
		t.TweenProperty(aura, "modulate", new Color(0.30f, 0.08f, 0.06f, 0.88f), 0.8);
	}

	/// EN: Fade out and free the Oblivion countdown aura particle VFX.
	/// ZH: 淡出并释放湮灭倒计时光环粒子特效节点。
	private void RemovePhaseFourOblivionAuraVfx()
	{
		Node2D? oldAura = _phaseFourOblivionAuraVfx;
		_phaseFourOblivionAuraVfx = null;
		_phaseFourOblivionAuraPostOblivion = false;
		if (!GodotObject.IsInstanceValid(oldAura) || oldAura == null) return;
		oldAura.Set("emitting", false); // stop new particles; existing ones finish naturally
		Node? auraParent = oldAura.GetParent();
		if (auraParent == null) { oldAura.QueueFree(); return; }
		Tween auraFade = auraParent.CreateTween();
		auraFade.TweenProperty(oldAura, "modulate:a", 0f, 0.9);
		auraFade.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(oldAura)) oldAura.QueueFree(); }));
	}

	private void SetPhaseFourBackgroundDarkened(bool isDarkened)
	{
		NCombatBackground background = NCombatRoom.Instance?.Background;
		if (background == null)
		{
			return;
		}
		if (!isDarkened)
		{
			background.Modulate = Colors.White;
			if (_phaseFourBackgroundOverlay != null)
			{
				_phaseFourBackgroundOverlay.QueueFree();
				_phaseFourBackgroundOverlay = null;
			}
			return;
		}
		if (_phaseFourBackgroundOverlay != null && GodotObject.IsInstanceValid(_phaseFourBackgroundOverlay))
		{
			background.Modulate = new Color(0.84f, 0.84f, 0.84f, 1f);
			UpdatePhaseFourBackgroundOverlayOpacity();
			return;
		}
		background.Modulate = new Color(0.84f, 0.84f, 0.84f, 1f);
		ColorRect overlay = new ColorRect();
		overlay.Name = "ArchitectPhaseFourOverlay";
		overlay.Color = new Color(0f, 0f, 0f, 0.06f);
		overlay.AnchorLeft = 0f;
		overlay.AnchorTop = 0f;
		overlay.AnchorRight = 1f;
		overlay.AnchorBottom = 1f;
		overlay.OffsetLeft = 0f;
		overlay.OffsetTop = 0f;
		overlay.OffsetRight = 0f;
		overlay.OffsetBottom = 0f;
		overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
		_phaseFourBackgroundOverlay = overlay;
		GodotTreeExtensions.AddChildSafely(background, overlay);
		UpdatePhaseFourBackgroundOverlayOpacity();
	}

	private void UpdatePhaseFourBackgroundOverlayOpacity()
	{
		if (_phaseFourBackgroundOverlay == null || !GodotObject.IsInstanceValid(_phaseFourBackgroundOverlay))
		{
			return;
		}
		int startingStacks = GetPhaseFourStartingOblivionStacks();
		int oblivionAmount = ((MonsterModel)this).Creature.GetPower<ArchitectOblivionPower>()?.Amount ?? startingStacks;
		int roundsCharged = Math.Max(0, startingStacks - oblivionAmount);
		float alpha = Math.Clamp(0.06f + roundsCharged * 0.06f, 0.06f, 0.45f);
		_phaseFourBackgroundOverlay.Color = new Color(0f, 0f, 0f, alpha);
	}

	private int GetPhaseFourStartingOblivionStacks()
	{
		int roundNumber = ((MonsterModel)this).Creature.CombatState?.RoundNumber ?? 1;
		if (CurrentPlayerCount() >= 2)
		{
			roundNumber = Math.Max(1, roundNumber - 1);
		}
		bool brutal = ModSupport.IsBrutalAct4(GetRunState());
		if (roundNumber < Act4Config.ArchitectP4OblivionLowThreshold)
		{
			int stacks3 = brutal ? Act4Config.ArchitectP4BrutalOblivionStacksLow : Act4Config.ArchitectP4NormalOblivionStacksLow;
			return (CurrentPlayerCount() >= 2) ? (stacks3 + 1) : stacks3;
		}
		if (roundNumber < Act4Config.ArchitectP4OblivionMidThreshold)
		{
			int stacks2 = brutal ? Act4Config.ArchitectP4BrutalOblivionStacksMid : Act4Config.ArchitectP4NormalOblivionStacksMid;
			return (CurrentPlayerCount() >= 2) ? (stacks2 + 1) : stacks2;
		}
		int stacks = brutal ? Act4Config.ArchitectP4BrutalOblivionStacksDefault : Act4Config.ArchitectP4NormalOblivionStacksDefault;
		return (CurrentPlayerCount() >= 2) ? (stacks + 1) : stacks;
	}

	private void UpdateTorchGradient(Color[] colors)
	{
		NCombatRoom instance = NCombatRoom.Instance;
		NCreature creatureNode = ((instance != null) ? instance.GetCreatureNode(((MonsterModel)this).Creature) : null);
		if (creatureNode == null || creatureNode.Visuals == null)
		{
			return;
		}
		UpdateParticleGradient(creatureNode.GetSpecialNode<GpuParticles2D>("Visuals/FireSlot/Fire_Particles/fire_vfx_big"), colors);
		UpdateParticleGradient(creatureNode.GetSpecialNode<GpuParticles2D>("Visuals/FireSlot/Fire_Particles/fire_vfx_small"), colors);
		UpdateParticleGradient(creatureNode.GetSpecialNode<GpuParticles2D>("Visuals/FireSlot/Fire_Particles/light_small"), colors);
	}

	private static void UpdateParticleGradient(GpuParticles2D? particles, Color[] colors)
	{
		if (particles == null || particles.ProcessMaterial is not ParticleProcessMaterial particleProcessMaterial)
		{
			return;
		}
		ParticleProcessMaterial val = (ParticleProcessMaterial)particleProcessMaterial.Duplicate(true);
		GradientTexture1D val2 = CreateGradientTexture(colors);
		val.Color = colors[Math.Min(1, colors.Length - 1)];
		val.ColorRamp = val2;
		val.ColorInitialRamp = val2;
		particles.ProcessMaterial = val;
		((CanvasItem)particles).SelfModulate = colors[Math.Min(1, colors.Length - 1)];
		particles.Restart();
	}

	private static GradientTexture1D CreateGradientTexture(Color[] colors)
	{
		GradientTexture1D val = new GradientTexture1D();
		val.Gradient = CreateGradient(colors);
		return val;
	}

	private static Gradient CreateGradient(Color[] colors)
	{
		Gradient val = new Gradient();
		for (int i = 0; i < colors.Length; i++)
		{
			float offset = (colors.Length > 1) ? (float)i / (float)(colors.Length - 1) : 0f;
			val.AddPoint(offset, colors[i]);
		}
		return val;
	}

}
