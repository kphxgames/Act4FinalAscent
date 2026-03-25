//=============================================================================
// Act4ArchitectBossPresentation.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Presentation, dialogue, merchant cameo, and late Phase 4 helper logic for the Architect.
// ZH: 建筑师的表现层、对白、商人演出，以及后段四阶段辅助逻辑。
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
	private static readonly Color ArchitectBaseTint = new Color(1f, 0.99f, 0.92f, 1f);

	private static readonly FieldInfo? HyperbeamLaserDurationField = typeof(NHyperbeamVfx).GetField("hyperbeamLaserDuration", BindingFlags.Public | BindingFlags.Static);

	private static readonly Color[] BlackHyperbeamParticleGradient = new Color[4]
	{
		new Color(0.01f, 0.01f, 0.01f, 0f),
		new Color(0.08f, 0.07f, 0.1f, 0.9f),
		new Color(0.22f, 0.2f, 0.24f, 1f),
		new Color(0.62f, 0.62f, 0.68f, 0f)
	};

	private static readonly Color[] BlackHyperbeamLineGradient = new Color[4]
	{
		new Color(0.02f, 0.02f, 0.02f, 1f),
		new Color(0.08f, 0.08f, 0.1f, 1f),
		new Color(0.2f, 0.2f, 0.24f, 1f),
		new Color(0.72f, 0.72f, 0.78f, 1f)
	};

	private static readonly Color BlackHyperbeamCanvasTint = new Color(0.12f, 0.11f, 0.14f, 1f);

	private static readonly Color[] MagentaHyperbeamParticleGradient = new Color[4]
	{
		new Color(0.4f, 0.0f, 0.5f, 0f),
		new Color(0.75f, 0.0f, 0.95f, 0.85f),
		new Color(0.95f, 0.2f, 1.0f, 1f),
		new Color(1.0f, 0.65f, 1.0f, 0f)
	};

	private static readonly Color[] MagentaHyperbeamLineGradient = new Color[4]
	{
		new Color(0.5f, 0.0f, 0.7f, 1f),
		new Color(0.8f, 0.0f, 0.95f, 1f),
		new Color(0.95f, 0.2f, 1.0f, 1f),
		new Color(1.0f, 0.6f, 1.0f, 1f)
	};

	private static readonly Color MagentaHyperbeamCanvasTint = new Color(0.55f, 0.0f, 0.75f, 1f);

	private static readonly Color[] OblivionHyperbeamParticleGradient = new Color[4]
	{
		new Color(0.08f, 0.01f, 0.01f, 0f),
		new Color(0.34f, 0.04f, 0.04f, 0.94f),
		new Color(0.86f, 0.13f, 0.13f, 1f),
		new Color(1.0f, 0.56f, 0.56f, 0f)
	};

	private static readonly Color[] OblivionHyperbeamLineGradient = new Color[4]
	{
		new Color(0.12f, 0.01f, 0.01f, 1f),
		new Color(0.38f, 0.03f, 0.03f, 1f),
		new Color(0.92f, 0.15f, 0.15f, 1f),
		new Color(1.0f, 0.62f, 0.62f, 1f)
	};

	private static readonly Color OblivionHyperbeamCanvasTint = new Color(0.68f, 0.08f, 0.08f, 1f);

	private Node2D? _phaseThreePowerUpFrontVfx;

	private Node2D? _phaseFourMushroomVfx;   // shown during Architect (enemy) turns

	private Node2D? _phaseFourOblivionAuraVfx;

	private bool _phaseFourOblivionAuraPostOblivion;

	private Node2D? _playerTurnVfx;           // shown during Player turns

	private Vector2 _merchantPanelPos;

	private Node2D? _phaseThreeJudgmentAuraVfx;

	private void ShowArchitectSpeech(string text, VfxColor color, double seconds)
	{
		try
		{
			NSpeechBubbleVfx bubble = CreateArchitectSpeechBubble(text, color, seconds, preserveArchitectAnchorWhenDead: false);
			if (bubble == null)
			{
				LogArchitect("ShowArchitectSpeech:skipped-no-speaker");
				return;
			}
			AddCombatVfx(bubble);
		}
		catch (Exception ex)
		{
			LogArchitect($"ShowArchitectSpeech:failed error={ex.Message}");
		}
	}

	private NSpeechBubbleVfx? CreateArchitectSpeechBubble(string text, VfxColor color, double seconds, bool preserveArchitectAnchorWhenDead)
	{
		// EN: During fake-death and phase swaps the Architect may be "dead" to combat code
		//     while we still want dialogue to come from roughly the right spot on screen.
		//     We try the real creature first, then a preserved anchor, and only then a living player fallback.
		// ZH: 假死和转阶段时，战斗逻辑里建筑师可能已经算“死了”，
		//     但台词还是得从差不多正确的位置冒出来，所以这里会按本体、锚点、玩家兜底的顺序去找。
		Creature architect = ((MonsterModel)this).Creature;
		if (architect != null && !architect.IsDead)
		{
			return NSpeechBubbleVfx.Create(text, architect, seconds, color);
		}
		if (preserveArchitectAnchorWhenDead && TryGetArchitectSpeechAnchor(out Vector2 architectSpeechPosition))
		{
			return NSpeechBubbleVfx.Create(text, DialogueSide.Right, architectSpeechPosition, seconds, color);
		}
		Creature fallbackSpeaker = ((MonsterModel)this).CombatState?.Players?.Select((Player p) => p.Creature).FirstOrDefault((Creature c) => c != null && c.IsAlive);
		if (fallbackSpeaker == null)
		{
			return null;
		}
		return NSpeechBubbleVfx.Create(text, fallbackSpeaker, seconds, color);
	}

	private bool TryGetArchitectSpeechAnchor(out Vector2 speechPosition)
	{
		speechPosition = Vector2.Zero;
		try
		{
			NCombatRoom combatRoom = NCombatRoom.Instance;
			NCreature creatureNode = combatRoom?.GetCreatureNode(((MonsterModel)this).Creature);
			if (creatureNode == null)
			{
				return false;
			}
			if (creatureNode.Visuals?.TalkPosition != null)
			{
				speechPosition = creatureNode.Visuals.TalkPosition.GlobalPosition;
				return true;
			}
			if (creatureNode.Hitbox != null)
			{
				speechPosition = creatureNode.VfxSpawnPosition + new Vector2((0f - creatureNode.Hitbox.Size.X) * 0.75f, (0f - creatureNode.Hitbox.Size.Y) * 0.5f * 0.75f);
				return true;
			}
			speechPosition = creatureNode.GlobalPosition;
			return true;
		}
		catch (Exception ex)
		{
			LogArchitect($"TryGetArchitectSpeechAnchor:failed error={ex.Message}");
			return false;
		}
	}

	private static void TrySetHyperbeamLaserDuration(float seconds)
	{
		try
		{
			HyperbeamLaserDurationField?.SetValue(null, seconds);
		}
		catch
		{
		}
	}

	/// EN: Spawn a black-tinted hyperbeam for phase 3/4 visuals.
	/// ZH: 为三/四阶段生成黑色调超光束表现。
	private void AddBlackHyperbeamVfx(Creature target)
	{
		NHyperbeamVfx create = NHyperbeamVfx.Create(((MonsterModel)this).Creature, target);
		ApplyBlackHyperbeamTheme(create);
		AddCombatVfx(create);
	}

	/// EN: Spawn a black-tinted beam impact to match the laser.
	/// ZH: 生成与光束匹配的黑色落点特效。
	private void AddBlackHyperbeamImpactVfx(Creature target)
	{
		NHyperbeamImpactVfx create = NHyperbeamImpactVfx.Create(((MonsterModel)this).Creature, target);
		ApplyBlackHyperbeamTheme(create);
		AddCombatVfx(create);
	}

	/// EN: Spawn a magenta-tinted hyperbeam for phase 2 visuals.
	/// ZH: 为二阶段生成洋红色超光束表现。
	private void AddMagentaHyperbeamVfx(Creature target)
	{
		NHyperbeamVfx create = NHyperbeamVfx.Create(((MonsterModel)this).Creature, target);
		ApplyMagentaHyperbeamTheme(create);
		AddCombatVfx(create);
	}

	/// EN: Spawn a magenta-tinted beam impact to match the Phase 2 laser.
	/// ZH: 生成与二阶段光束匹配的洋红色落点特效。
	private void AddMagentaHyperbeamImpactVfx(Creature target)
	{
		NHyperbeamImpactVfx create = NHyperbeamImpactVfx.Create(((MonsterModel)this).Creature, target);
		ApplyMagentaHyperbeamTheme(create);
		AddCombatVfx(create);
	}

	/// EN: Recolor every beam child node so the whole effect stays phase-black.
	/// ZH: 重着色光束整棵子节点，让整套特效都保持黑色阶段风格。
	private static void ApplyBlackHyperbeamTheme(Node? root)
	{
		if (root == null)
		{
			return;
		}
		ApplyHyperbeamThemeRecursive(root, BlackHyperbeamCanvasTint, BlackHyperbeamParticleGradient, BlackHyperbeamLineGradient);
	}

	/// EN: Recolor every beam child node for a magenta/purple Phase 2 laser.
	/// ZH: 重着色光束整棵子节点，呈现二阶段洋红色激光风格。
	private static void ApplyMagentaHyperbeamTheme(Node? root)
	{
		if (root == null)
		{
			return;
		}
		ApplyHyperbeamThemeRecursive(root, MagentaHyperbeamCanvasTint, MagentaHyperbeamParticleGradient, MagentaHyperbeamLineGradient);
	}

	/// EN: Spawn an Oblivion red/black hyperbeam for Phase 4 OBLIVION.
	/// ZH: 为四阶段 OBLIVION 生成红黑色超光束。
	private void AddOblivionHyperbeamVfx(Creature target)
	{
		NHyperbeamVfx create = NHyperbeamVfx.Create(((MonsterModel)this).Creature, target);
		ApplyOblivionHyperbeamTheme(create);
		AddCombatVfx(create);
	}

	/// EN: Spawn an Oblivion beam impact with a red/black tint.
	/// ZH: 生成 OBLIVION 落点特效，采用红黑色调。
	private void AddOblivionHyperbeamImpactVfx(Creature target)
	{
		NHyperbeamImpactVfx create = NHyperbeamImpactVfx.Create(((MonsterModel)this).Creature, target);
		ApplyOblivionHyperbeamTheme(create);
		AddCombatVfx(create);
	}

	/// EN: Apply a full red/black theme so every beam node overrides the default blue.
	/// ZH: 对整棵特效节点施加完整红黑主题，彻底覆盖默认蓝色。
	private static void ApplyOblivionHyperbeamTheme(Node? root)
	{
		if (root == null)
		{
			return;
		}
		ApplyHyperbeamThemeRecursive(root, OblivionHyperbeamCanvasTint, OblivionHyperbeamParticleGradient, OblivionHyperbeamLineGradient);
	}

	private static void ApplyHyperbeamThemeRecursive(Node node, Color canvasTint, Color[] particleGradient, Color[] lineGradient)
	{
		if (node is CanvasItem canvasItem)
		{
			canvasItem.SelfModulate = canvasTint;
		}
		if (node is GpuParticles2D particles)
		{
			UpdateParticleGradient(particles, particleGradient);
		}
		if (node is Line2D line)
		{
			UpdateHyperbeamLineGradient(line, lineGradient);
		}
		foreach (Node child in node.GetChildren())
		{
			ApplyHyperbeamThemeRecursive(child, canvasTint, particleGradient, lineGradient);
		}
	}

	private static void UpdateHyperbeamLineGradient(Line2D line, Color[] colors)
	{
		line.Gradient = CreateGradient(colors);
		line.SelfModulate = colors[Math.Min(1, colors.Length - 1)];
		if (line.Material is ShaderMaterial shaderMaterial)
		{
			ShaderMaterial val = (ShaderMaterial)shaderMaterial.Duplicate(true);
			val.SetShaderParameter("lut", CreateGradientTexture(colors));
			line.Material = val;
		}
	}

	private Node2D? CreatePhaseTwoEndOfDaysMissileVfx()
	{
		Vector2? sideCenterFloor = VfxCmd.GetSideCenterFloor(CombatSide.Player, ((MonsterModel)this).CombatState);
		if (!sideCenterFloor.HasValue)
		{
			return null;
		}
		return NLargeMagicMissileVfx.Create(sideCenterFloor.Value, new Color("8c2447"));
	}

	private string GetArchitectOpeningSpeech()
	{
		// If the players stole a book from the Grand Library, the Architect opens
		// with rage directed at whichever tome they took.
		if (IsHolyBookChosen())
		{
			return ModLoc.T(
				"You stole my ward!\nYou will pay for that.",
				"你们偷走了我的神圣庇护！你们会为此付出代价。",
				fra: "Vous avez volé ma protection divine !\nVous le paierez.",
				deu: "Ihr habt meinen göttlichen Schutz gestohlen!\nIhr werdet dafür bezahlen.",
				jpn: "我が神聖な守護を盗んだな！\nその代償を払え。",
				kor: "내 신성한 수호를 훔쳤다!\n대가를 치르게 될 것이다.",
				por: "Vocês roubaram minha proteção divina!\nVocês vão pagar por isso.",
				rus: "Вы украли мою божественную защиту!\nВы за это заплатите.",
				spa: "¡Robaron mi protección divina!\nPagarán por eso.");
		}
		if (IsShadowBookChosen())
		{
			return ModLoc.T(
				"The Tome of Shadows belongs to me.\nThose shadows will be your grave.",
				"阴影之典是我的。\n那些阴影将成为你们的坟墓。",
				fra: "Le Tome des Ombres m'appartient.\nCes ombres seront votre tombeau.",
				deu: "Das Schattenbuch gehört mir.\nDiese Schatten werden euer Grab sein.",
				jpn: "影の書は私のものだ。\nその影がお前たちの墓となる。",
				kor: "그림자의 서는 나의 것이다.\n그 그림자들이 너희의 무덤이 될 것이다.",
				por: "O Tomo das Sombras é meu.\nEssas sombras serão o vosso túmulo.",
				rus: "Том Теней принадлежит мне.\nЭти тени станут вашей могилой.",
				spa: "El Tomo de las Sombras es mío.\nEsas sombras serán vuestra tumba.");
		}
		if (IsSilverBookChosen())
		{
			return ModLoc.T(
				"You cracked my aegis. Clever.\nNow face what lies beneath it.",
				"你们破开了我的护盾。聪明。\n那就来直面护盾之下的真相吧。",
				fra: "Vous avez fissuré mon égide. Habile.\nAffronte maintenant ce qui se cache dessous.",
				deu: "Ihr habt meine Aegis gebrochen. Klug.\nNun seht, was darunter liegt.",
				jpn: "我が盾を砕いたか。賢い。\n今度はその下に何があるか見せてやろう。",
				kor: "내 방패를 깨뜨렸군. 영리하다.\n이제 그 아래 무엇이 있는지 보거라.",
				por: "Vocês racharam minha égide. Esperto.\nAgora enfrentem o que há por baixo dela.",
				rus: "Вы разбили мою эгиду. Умно.\nТеперь взгляните на то, что скрывается под ней.",
				spa: "Agrietaron mi égida. Hábil.\nAhora enfrenten lo que yace bajo ella.");
		}
		if (IsCursedBookChosen())
		{
			return ModLoc.T(
				"You opened that tome?!\nYou have no idea what you've done.",
				"你们打开了那本书？！\n你们根本不明白自己干了什么。",
				fra: "Vous avez ouvert ce tome ?!\nVous ne savez pas ce que vous avez fait.",
				deu: "Ihr habt dieses Buch geöffnet?!\nIhr habt keine Ahnung, was ihr angerichtet habt.",
				jpn: "その典を開いたのか？！\nお前たちは何をしたかわかっていない。",
				kor: "저 책을 연 거냐?!\n너희는 자신이 무슨 짓을 한 건지 모른다.",
				por: "Vocês abriram esse tomo?!\nVocês não têm ideia do que fizeram.",
				rus: "Вы открыли эту книгу?!\nВы понятия не имеете, что сделали.",
				spa: "¿Abrieron ese tomo?!\nNo tienen idea de lo que han hecho.");
		}

		// No book stolen  -  generic randomised opening.
		uint seed = ((MonsterModel)this).Creature?.CombatState?.RunState?.Rng?.Seed ?? 0u;
		uint combatId = ((MonsterModel)this).Creature?.CombatId ?? 0u;
		int speechIndex = (int)((seed + combatId) % 3u);
		return speechIndex switch
		{
			0 => ModLoc.T("A futile resistance.", "徒劳的抵抗。",
				fra: "Une résistance futile.",
				deu: "Ein sinnloser Widerstand.",
				jpn: "無駄な抵抗だ。",
				kor: "무의미한 저항이다.",
				por: "Uma resistência inútil.",
				rus: "Тщетное сопротивление.",
				spa: "Una resistencia inútil."),
			1 => ModLoc.T("You were never built to win this.", "你从一开始就不是为了胜利而生。",
				fra: "Vous n'avez jamais été conçus pour gagner.",
				deu: "Ihr wart nie dazu erschaffen, dies zu gewinnen.",
				jpn: "お前たちはこれに勝つために生まれてきたのではない。",
				kor: "너희는 애초에 이길 수 없도록 만들어졌다.",
				por: "Vocês nunca foram feitos para vencer isso.",
				rus: "Вы никогда не были созданы для победы.",
				spa: "Nunca estuvieron hechos para ganar esto."),
			_ => ModLoc.T("Kneel. The design is already finished.", "跪下吧，这份设计早已完成。",
				fra: "À genoux. La conception est déjà achevée.",
				deu: "Kniet. Das Design ist längst vollendet.",
				jpn: "跪け。設計はすでに完成している。",
				kor: "무릎을 꿧어라. 설계는 이미 완성되었다.",
				por: "Ajoelhem. O projeto já foi concluído.",
				rus: "Падите на колени. Замысел уже завершён.",
				spa: "Arrodíllense. El diseño ya está concluido.")
		};
	}

	private const string MushroomVfxScenePath = "res://scenes/vfx/events/hungry_for_mushrooms_vfx.tscn";

	private const string LegendVfxScenePath = "res://scenes/vfx/events/the_legends_were_true_vfx.tscn";

	private void EnsureMushroomVfx()
	{
		// Remove any stale instance before (re)creating so phase transitions don't stack copies.
		RemovePhaseFourMushroomVfx();
		PackedScene scene = GD.Load<PackedScene>(MushroomVfxScenePath);
		if (scene == null) return;
		NCombatRoom combatRoom = NCombatRoom.Instance;
		NCreature creatureNode = combatRoom?.GetCreatureNode(((MonsterModel)this).Creature);
		if (creatureNode?.Visuals == null) return;
		Node2D vfxNode = scene.Instantiate<Node2D>(PackedScene.GenEditState.Disabled);
		vfxNode.ZIndex = 2;
		vfxNode.ZAsRelative = false; // absolute z-layer so it draws above the creature
		// The scene particles are authored at large screen-space positions (~648,488 for the glow
		// centre). Translate the root so that centre lands on the creature visual origin.
		// Additional -50px left/up nudge to align glows on the Architect sprite.
		vfxNode.Position = new Vector2(-698f, -550f);
		// Scale dark_overlay child up so it's more visible on a creature-sized anchor.
		Node2D darkOverlay = vfxNode.GetNodeOrNull<Node2D>("dark_overlay");
		if (darkOverlay != null)
		{
			darkOverlay.Scale = new Vector2(1.8f, 1.8f);
			// Shift up ~200px and left ~640px (~33% of 1920) relative to scene-authored position.
			darkOverlay.Position += new Vector2(-640f, -200f);
			// Cap dark_overlay's peak opacity so the darkening is subtle.
			darkOverlay.SelfModulate = new Color(1f, 1f, 1f, 0.16f);
		}
		_phaseFourMushroomVfx = vfxNode;
		// Fade in from transparent so the swap isn't jarring.
		vfxNode.Modulate = new Color(1f, 1f, 1f, 0f);
		GodotTreeExtensions.AddChildSafely(creatureNode.Visuals, vfxNode);
		Tween fadeIn = creatureNode.Visuals.CreateTween();
		fadeIn.TweenProperty(vfxNode, "modulate:a", 1f, 0.6);
	}

	/// EN: Create (or re-create) the red absorbing-particle aura shown while Oblivion counts down.
	///     Particles start large and shrink to 0 over their lifetime (absorbing inward).
	///     Node scale starts at 0.5x and grows per-turn to 2.0x (4x total growth).
	/// ZH: 湮灭倒计时期间在建筑师身上创建（或重建）红色向内吸收粒子光环。
	///     每个粒子从较大尺寸缩小至0（吸收内敛效果）；节点整体从0.5倍逐渐增大至2.0倍。
	private void EnsurePhaseFourOblivionAuraVfx()
	{
		RemovePhaseFourOblivionAuraVfx();
		PackedScene? auraScene = GD.Load<PackedScene>("res://Act4Placeholder/vfx/oblivion_aura_vfx.tscn");
		if (auraScene == null) return;
		NCombatRoom? auraRoom = NCombatRoom.Instance;
		NCreature? auraCreatureNode = auraRoom?.GetCreatureNode(((MonsterModel)this).Creature);
		if (auraCreatureNode?.Visuals == null) return;
		Node2D? particles = auraScene.Instantiate<Node2D>(PackedScene.GenEditState.Disabled);
		if (particles == null) return;
		// Start at 0.5x scale; grows to 2.0x by the end of the countdown.
		particles.Scale = new Vector2(0.5f, 0.5f);
		// Position slightly above body center.
		// MoveChild(particles, 0) below ensures it renders before (behind) the Body/spine siblings
		// within the same Visuals node  -  no ZIndex override needed or wanted, since a negative
		// ZIndex would push it behind the room background and make it invisible.
		particles.Position = new Vector2(0f, -275f);
		_phaseFourOblivionAuraVfx = particles;
		// Fade in to the alpha matching the current Oblivion stacks (55% opaque at 0-1 stacks, -5% per stack).
		int initStacks = GetPhaseFourStartingOblivionStacks();
		float initialAlpha = Math.Clamp(0.55f - 0.05f * initStacks, 0.30f, 0.55f);
		particles.Modulate = new Color(1f, 1f, 1f, 0f); // start transparent
		GodotTreeExtensions.AddChildSafely(auraCreatureNode.Visuals, particles);
		// Move to first child so it renders behind already-existing Visuals children.
		auraCreatureNode.Visuals.MoveChild(particles, 0);
		Tween auraFadeIn = auraCreatureNode.Visuals.CreateTween();
		auraFadeIn.TweenProperty(particles, "modulate:a", initialAlpha, 0.8);
	}

	/// EN: Scale the Oblivion aura from 0.5x (first tick) to 2.0x (countdown exhausted)  -  4x total.
	/// ZH: 将湮灭光环从0.5倍（首次跳动）线性插值到2.0倍（倒计时结束），总共放大4倍。
	private void UpdatePhaseFourOblivionAuraScale()
	{
		if (!GodotObject.IsInstanceValid(_phaseFourOblivionAuraVfx) || _phaseFourOblivionAuraVfx == null)
			return;
		// Don't overwrite scale/colour once the aura has transitioned to its post-Oblivion black state.
		if (_phaseFourOblivionAuraPostOblivion) return;
		int auraStartingStacks = GetPhaseFourStartingOblivionStacks();
		int auraCurrentAmount = ((MonsterModel)this).Creature.GetPower<ArchitectOblivionPower>()?.Amount ?? auraStartingStacks;
		int auraRoundsCharged = Math.Max(0, auraStartingStacks - auraCurrentAmount);
		// t=0 at start of countdown, t=1 when stacks hit 0.
		float auraT = auraStartingStacks > 0 ? Math.Clamp((float)auraRoundsCharged / auraStartingStacks, 0f, 1f) : 1f;
		float auraScale = 0.5f + 1.5f * auraT; // 0.5 → 2.0
		_phaseFourOblivionAuraVfx.Scale = new Vector2(auraScale, auraScale);
		// Update alpha: 55% opaque at 0-1 stacks remaining, -5% per extra stack (min 30%).
		float auraAlpha = Math.Clamp(0.55f - 0.05f * auraCurrentAmount, 0.30f, 0.55f);
		_phaseFourOblivionAuraVfx.Modulate = new Color(1f, 1f, 1f, auraAlpha);
	}

	private void RemovePlayerTurnVfx()
	{
		Node2D? old = _playerTurnVfx;
		_playerTurnVfx = null;
		if (!GodotObject.IsInstanceValid(old) || old == null) return;
		Node? parent = old.GetParent();
		if (parent == null) { old.QueueFree(); return; }
		Tween t = parent.CreateTween();
		t.TweenProperty(old, "modulate:a", 0f, 0.6);
		t.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(old)) old.QueueFree(); }));
	}

	private void EnsureLegendVfx(CombatState combatState)
	{
		RemovePlayerTurnVfx();
		PackedScene scene = GD.Load<PackedScene>(LegendVfxScenePath);
		if (scene == null) return;
		// Attach to the local player's creature so it appears on co-op player's side.
		Creature? playerCreature = LocalContext.GetMe(combatState)?.Creature;
		if (playerCreature == null) return;
		NCreature playerNode = NCombatRoom.Instance?.GetCreatureNode(playerCreature);
		if (playerNode?.Visuals == null) return;
		Node2D vfxNode = scene.Instantiate<Node2D>(PackedScene.GenEditState.Disabled);
		vfxNode.ZIndex = 2;
		vfxNode.ZAsRelative = false;
		// orb_glow centre is at ~(556,544) in scene space; offset so it sits on the player.
		vfxNode.Position = new Vector2(-556f, -544f);
		Node2D darkOverlay = vfxNode.GetNodeOrNull<Node2D>("dark_overlay");
		if (darkOverlay != null)
		{
			darkOverlay.Scale = new Vector2(1.8f, 1.8f);
			// Shift up ~200px relative to scene-authored position.
			darkOverlay.Position += new Vector2(0f, -200f);
			// Use SelfModulate alpha so the animation's own fade-in/out is preserved but
			// scaled down in intensity - SelfModulate multiplies with the animated Modulate.
			darkOverlay.SelfModulate = new Color(1f, 1f, 1f, 0.16f);
		}
		_playerTurnVfx = vfxNode;
		// Fade in from transparent so the swap isn't jarring.
		vfxNode.Modulate = new Color(1f, 1f, 1f, 0f);
		GodotTreeExtensions.AddChildSafely(playerNode.Visuals, vfxNode);
		Tween fadeIn = playerNode.Visuals.CreateTween();
		fadeIn.TweenProperty(vfxNode, "modulate:a", 1f, 0.6);
	}

	private void EnsurePhaseThreeAura()
	{
		if (GodotObject.IsInstanceValid(_phaseThreePowerUpFrontVfx))
		{
			return;
		}
		PackedScene val = GD.Load<PackedScene>("res://scenes/vfx/vfx_power_up/vfx_power_up_2d_front.tscn");
		if (val == null)
		{
			return;
		}
		NCombatRoom instance = NCombatRoom.Instance;
		NCreature creatureNode = ((instance != null) ? instance.GetCreatureNode(((MonsterModel)this).Creature) : null);
		if (creatureNode == null)
		{
			return;
		}
		Node2D val2 = val.Instantiate<Node2D>(PackedScene.GenEditState.Disabled);
		val2.Position = new Vector2(0f, -10f);
		_phaseThreePowerUpFrontVfx = val2;
		GodotTreeExtensions.AddChildSafely(creatureNode.Visuals, val2);
	}

	private void EnsurePhaseThreeJudgmentAuraVfx()
	{
		if (GodotObject.IsInstanceValid(_phaseThreeJudgmentAuraVfx))
		{
			return;
		}
		PackedScene? auraScene = GD.Load<PackedScene>("res://Act4Placeholder/vfx/oblivion_aura_vfx.tscn");
		if (auraScene == null) return;
		NCombatRoom? room = NCombatRoom.Instance;
		NCreature? creatureNode = room?.GetCreatureNode(((MonsterModel)this).Creature);
		if (creatureNode?.Visuals == null) return;
		Node2D? aura = auraScene.Instantiate<Node2D>(PackedScene.GenEditState.Disabled);
		if (aura == null) return;
		aura.Scale = new Vector2(1.6f, 1.6f);
		aura.Position = new Vector2(0f, -275f);
		aura.Modulate = new Color(0.95f, 0.76f, 0.32f, 0f);
		_phaseThreeJudgmentAuraVfx = aura;
		GodotTreeExtensions.AddChildSafely(creatureNode.Visuals, aura);
		creatureNode.Visuals.MoveChild(aura, 0);
		Tween fadeIn = creatureNode.Visuals.CreateTween();
		fadeIn.TweenProperty(aura, "modulate:a", 0.62f, 0.25);
	}

	private void RemovePhaseThreeJudgmentAuraVfx()
	{
		Node2D? oldAura = _phaseThreeJudgmentAuraVfx;
		_phaseThreeJudgmentAuraVfx = null;
		if (!GodotObject.IsInstanceValid(oldAura) || oldAura == null) return;
		oldAura.Set("emitting", false);
		Node? parent = oldAura.GetParent();
		if (parent == null)
		{
			oldAura.QueueFree();
			return;
		}
		Tween fade = parent.CreateTween();
		fade.TweenProperty(oldAura, "modulate:a", 0f, 0.25);
		fade.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(oldAura)) oldAura.QueueFree(); }));
	}

	private async Task RemoveArchitectPositivePowersForPhaseFourAsync()
	{
		await RemovePreAttackTrackerPowersAsync();
		if (((MonsterModel)this).Creature.GetPower<ArchitectRetaliationPower>() != null)
		{
			await PowerCmd.Remove<ArchitectRetaliationPower>(((MonsterModel)this).Creature);
		}
		if (((MonsterModel)this).Creature.GetPower<ArtifactPower>() != null)
		{
			await PowerCmd.Remove<ArtifactPower>(((MonsterModel)this).Creature);
		}
		if (((MonsterModel)this).Creature.GetPower<SlipperyPower>() != null)
		{
			await PowerCmd.Remove<SlipperyPower>(((MonsterModel)this).Creature);
		}
		if (((MonsterModel)this).Creature.GetPower<ArchitectBlockPiercerPower>() != null)
		{
			await PowerCmd.Remove<ArchitectBlockPiercerPower>(((MonsterModel)this).Creature);
		}
		if (((MonsterModel)this).Creature.GetPower<BarricadePower>() != null)
		{
			await PowerCmd.Remove<BarricadePower>(((MonsterModel)this).Creature);
		}
		if (((MonsterModel)this).Creature.GetPower<IntangiblePower>() != null)
		{
			await PowerCmd.Remove<IntangiblePower>(((MonsterModel)this).Creature);
		}
		if (((MonsterModel)this).Creature.GetPower<ArchitectSummonThornsPower>() != null)
		{
			await PowerCmd.Remove<ArchitectSummonThornsPower>(((MonsterModel)this).Creature);
		}
		if (((MonsterModel)this).Creature.GetPower<ThornsPower>() != null)
		{
			await PowerCmd.Remove<ThornsPower>(((MonsterModel)this).Creature);
		}
		_hasTemporaryPhaseThreeThorns = false;
		_hasPersistentSummonThorns = false;
		_persistentSummonThornsAmount = 0;
	}

	/// EN: True when the Merchant should appear: solo player under 90% HP, or any co-op member dead / under 90% HP.
	/// ZH: 商人出现条件：单人HP低于90%，或多人模式任意成员死亡或HP低于90%。
	private bool ShouldMerchantHeal()
	{
		var players = ((MonsterModel)this).CombatState?.Players;
		if (players == null || players.Count == 0) return false;
		bool isMultiplayer = players.Count > 1;
		if (isMultiplayer)
		{
			return players.Any(p => p?.Creature != null &&
				(p.Creature.IsDead || (p.Creature.MaxHp > 0 && (decimal)p.Creature.CurrentHp / (decimal)p.Creature.MaxHp < 0.90m)));
		}
		else
		{
			var solo = players.FirstOrDefault();
			if (solo?.Creature == null || solo.Creature.IsDead) return false;
			return solo.Creature.MaxHp > 0 && (decimal)solo.Creature.CurrentHp / (decimal)solo.Creature.MaxHp < 0.90m;
		}
	}

	/// EN: Merchant cameo between Phase 3 -> 4.
	///     He tosses 2 free potion heals (20% each, 1s apart) for +40% total.
	///     Dead co-op teammates are revived to 35% max HP first, then join the healing rounds.
	/// ZH: 三至四阶段过渡时的商人登场。
	///     免费投掷2次药水治疗（每次20%，间隔1秒，总计+40%）。
	///     阵亡联机队友先复活到35%最大生命，再参与后续治疗轮次。
	private async Task ShowMerchantHealSequenceAsync()
	{
		if (!ShouldMerchantHeal()) return;

		bool isMultiplayer = ((MonsterModel)this).CombatState?.Players?.Count > 1;

		//=============================================================================
		// EN: Merchant staging.
		//     Slightly up + slightly smaller so he reads as helper cameo, not another boss.
		// ZH: 商人出场定位。
		//     位置略上移、体型略缩小，更像支援嘉宾而不是新BOSS。
		//=============================================================================
		// --- Compute merchant screen position (top-center-left, ~35 % width, 28 % height) ---
		Rect2 visRect = NGame.Instance?.GetViewport()?.GetVisibleRect() ?? new Rect2(0f, 0f, 1920f, 1080f);
		Vector2 merchantScreenPos = new Vector2(
			visRect.Position.X + visRect.Size.X * 0.35f,
			visRect.Position.Y + visRect.Size.Y * 0.28f + 150f);
		Vector2 merchantVisualPos = merchantScreenPos + new Vector2(0f, 85f);

		// Store for GetOfferPanelPosition (used later during the potion purchase UI).
		_merchantPanelPos = merchantScreenPos;

		// --- Arrival VFX: play the merchant welcome sound instead of the generic confirm ---
		SfxCmd.Play("event:/sfx/npcs/merchant/merchant_welcome");
		NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short, 0f);

		// --- Try to spawn the FakeMerchant visual scene as a non-combat Node2D ---
		Node2D? merchantNode = null;
		const string merchantScenePath = "res://scenes/creature_visuals/fake_merchant_monster.tscn";
		try
		{
			if (ResourceLoader.Exists(merchantScenePath))
			{
				var packedScene = ResourceLoader.Load<PackedScene>(merchantScenePath, null, ResourceLoader.CacheMode.Reuse);
				if (packedScene != null)
				{
					merchantNode = packedScene.Instantiate<Node2D>(PackedScene.GenEditState.Disabled);
					merchantNode.GlobalPosition = merchantVisualPos;
					merchantNode.Scale = Vector2.One * 1.425f;
					NCombatRoom.Instance?.AddChild(merchantNode);
					// Gold flash + horizontal lines signal the merchant's arrival visually.
					NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(NAdditiveOverlayVfx.Create(VfxColor.Gold));
					NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(NHorizontalLinesVfx.Create(new Color("FFD700FF"), 1.2, movingRightwards: true));
					LogArchitect("ShowMerchantHealSequenceAsync:merchant-visual-spawned");
				}
			}
		}
		catch (Exception ex)
		{
			LogArchitect($"ShowMerchantHealSequenceAsync:merchant-visual-failed ex={ex.Message}");
		}

		// --- Speech bubble as a styled CanvasLayer label (NSpeechBubbleVfx requires a Creature) ---
		string merchantSpeech = isMultiplayer
			? GetMerchantHealSpeechMultiplayer()
			: GetMerchantHealSpeechSolo();

		CanvasLayer? speechLayer = null;
		if (NGame.Instance != null)
		{
			speechLayer = new CanvasLayer();
			speechLayer.Layer = 150;
			NGame.Instance.AddChild(speechLayer);

			// Bubble panel  -  dark purple, positioned above the merchant figure.
			var bubblePanel = new ColorRect();
			bubblePanel.Color = new Color(0.08f, 0.04f, 0.18f, 0.93f);
			bubblePanel.Size = new Vector2(320f, 70f);
			// Position to the right and above the merchant visual (keeps confirm UI unchanged).
			bubblePanel.Position = new Vector2(merchantVisualPos.X - 160f, merchantVisualPos.Y - 120f);
			speechLayer.AddChild(bubblePanel);

			var speechLabel = new Label();
			speechLabel.Text = merchantSpeech;
			speechLabel.HorizontalAlignment = HorizontalAlignment.Center;
			speechLabel.VerticalAlignment = VerticalAlignment.Center;
			speechLabel.AutowrapMode = TextServer.AutowrapMode.Word;
			speechLabel.Position = new Vector2(8f, 6f);
			speechLabel.Size = new Vector2(304f, 58f);
			bubblePanel.AddChild(speechLabel);

			// Auto-dismiss after 5.0 s.
			var tween = speechLayer.CreateTween();
			tween.TweenInterval(6.0);
			tween.TweenCallback(Callable.From(() => speechLayer?.QueueFree()));
		}

		await Cmd.Wait(2.25f, false);

		//=============================================================================
		// EN: Two-potion sequence.
		//     Every round throws VFX to each player so the co-op moment reads clearly.
		// ZH: 双药水流程。
		//     每轮都给所有玩家投掷VFX，联机时反馈更清楚。
		//=============================================================================
		// --- Throw 2 free potions (1s apart): each heals 20% max HP ---
		Texture2D? potionTex = ModelDb.Potion<BloodPotion>()?.Image;
		HashSet<Creature> revivedThisSequence = new HashSet<Creature>();
		for (int potionRound = 0; potionRound < 2; potionRound++)
		{
			foreach (Player player in ((MonsterModel)this).CombatState?.Players ?? Enumerable.Empty<Player>())
			{
				Creature? creature = player?.Creature;
				if (creature == null) continue;

				// Potion-throw VFX: fly from merchant position to the player's creature node.
				NCreature? playerCreatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
				if (playerCreatureNode != null)
				{
					Vector2 targetPos = playerCreatureNode.VfxSpawnPosition;
					NItemThrowVfx? throwVfx = NItemThrowVfx.Create(merchantVisualPos, targetPos, potionTex);
					NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(throwVfx);
				}

				if (creature.IsDead && !revivedThisSequence.Contains(creature))
				{
					// Revive to 35% max HP (deck restore mirrors RevivePlayersForPhaseFourAsync).
					int reviveHp = Math.Max(1, (int)Math.Ceiling((decimal)creature.MaxHp * 0.10m));
					await CreatureCmd.SetCurrentHp(creature, (decimal)reviveHp);
					revivedThisSequence.Add(creature);
					var deckCards = player!.Deck.Cards.ToList();
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
					try
					{
						await CardPileCmd.Shuffle(null, player);
						await CardPileCmd.Draw(null, 5m, player, fromHandDraw: false);
					}
					catch (Exception ex)
					{
						LogArchitect($"ShowMerchantHealSequenceAsync:revive-draw-error ex={ex.Message}");
					}
				}

				// Each free potion heals for 20% max HP (two rounds total = 40%).
				if (!creature.IsDead)
				{
					int healAmt = Math.Max(1, (int)Math.Ceiling((decimal)creature.MaxHp * 0.20m));
					await CreatureCmd.Heal(creature, (decimal)healAmt, playAnim: true);
				}

				await Cmd.Wait(0.18f, false);
			}

			if (potionRound == 0)
				await Cmd.Wait(1.0f, false);
		}

		await Cmd.Wait(0.8f, false);
		// Allow qualifying players to purchase a potion before the Merchant departs.
		await ShowMerchantPotionOfferAsync();

		// Merchant departs after the transaction window closes.
		merchantNode?.QueueFree();
	}

	private string GetMerchantHealSpeechSolo()
	{
		return ModLoc.T("Risky business, but you've been loyal. Take this.",
			"风险不小，但你是我的老顾客。收好了。",
			fra: "Affaire risquée, mais vous êtes fidèle. Prenez ça.",
			deu: "Riskant, aber du bist ein treuer Kunde. Nimm das.",
			jpn: "危な橋だが、あなたは常連だ。これを受け取れ。",
			kor: "위험한 장사지만, 단골손님이니까요. 받으세요.",
			por: "Negócio arriscado, mas é um cliente leal. Tome isso.",
			rus: "Рискованно, но вы постоянный клиент. Возьмите это.",
			spa: "Negocio arriesgado, pero eres un cliente fiel. Toma esto.");
	}

	private string GetMerchantHealSpeechMultiplayer()
	{
		return ModLoc.T("Risky business, but you've all been loyal customers. Take this.",
			"风险不小，但你们都是老顾客。收好了。",
			fra: "Affaire risquée, mais vous êtes tous des clients fidèles. Prenez ça.",
			deu: "Riskant, aber ihr seid alle treue Kunden. Nehmt das.",
			jpn: "危な橋だが、あなた方は皆、常連客だ。これを受け取れ。",
			kor: "위험한 장사지만, 여러분 모두 단골손님이니까요. 받으세요.",
			por: "Negócio arriscado, mas vocês são clientes leais. Tomem isso.",
			rus: "Рискованно, но вы все  -  постоянные клиенты. Возьмите это.",
			spa: "Negocio arriesgado, pero todos son clientes fieles. Tomen esto.");
	}

	/// EN: Offers each player with 300g+ a BloodPotion purchase after the free merchant heal.
	///     Per-player decision; synchronized across host and client via PlayerChoiceSynchronizer.
	/// ZH: 对持有300金或以上的玩家提供购买血瓶的选项（免费回血后）。每名玩家独立决策，经PlayerChoiceSynchronizer跨端同步。
	private async Task ShowMerchantPotionOfferAsync()
	{
		var players = ((MonsterModel)this).CombatState?.Players;
		if (players == null || players.Count == 0) return;
		// Only proceed if at least one player can afford the offer.
		if (!players.Any(p => p != null && p.Gold >= 300)) return;

		await Cmd.Wait(0.5f, false);

		// Collect eligible players and start all their choice UIs in parallel so the game
		// resumes as soon as every player has responded (or timed out), not after each
		// player's full 15-second window in sequence.
		var eligiblePlayers = players
			.Where(p => p?.Creature != null && p.Gold >= 300)
			.ToList();

		var choiceTasks = eligiblePlayers
			.Select(player => GetPotionOfferChoiceAsync(player))
			.ToList();

		bool[] results = await Task.WhenAll(choiceTasks);

		for (int i = 0; i < eligiblePlayers.Count; i++)
		{
			if (!results[i]) continue;
			Player player = eligiblePlayers[i];
			await PlayerCmd.LoseGold(300m, player, GoldLossType.Spent);
			var procureResult = await PotionCmd.TryToProcure<BloodPotion>(player);
			// If the potion bar is full, apply the BloodPotion's healing effect directly.
			if (!procureResult.success && procureResult.failureReason == PotionProcureFailureReason.TooFull)
			{
				Creature? creature = player.Creature;
				if (creature != null && creature.IsAlive)
				{
					int healAmt = Math.Max(1, (int)Math.Ceiling((decimal)creature.MaxHp * 0.20m));
					await CreatureCmd.Heal(creature, (decimal)healAmt, playAnim: true);
				}
			}
		}
	}

	/// EN: Synchronizes the potion purchase decision across host and remote clients.
	///     Local player sees the interactive UI; remote players are awaited via the network synchronizer.
	/// ZH: 通过PlayerChoiceSynchronizer在主机与客户端间同步药水购买决策。本地玩家看到交互UI；远端玩家通过网络同步器等待。
	private async Task<bool> GetPotionOfferChoiceAsync(Player player)
	{
		uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(player);
		if (LocalContext.IsMe(player))
		{
			bool accepted = await ShowLocalPotionOfferUiAsync(player);
			RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
				player, choiceId, PlayerChoiceResult.FromIndex(accepted ? 1 : 0));
			return accepted;
		}
		else
		{
			// Remote player  -  wait for them to send their decision.
			var remoteResult = await RunManager.Instance.PlayerChoiceSynchronizer
				.WaitForRemoteChoice(player, choiceId);
			return remoteResult.AsIndex() == 1;
		}
	}

	/// EN: Shows the merchant potion offer panel for the local player with Yes/No buttons and a 15-second timer bar.
	///     Returns true if the player accepted, false if they declined or the timer expired.
	/// ZH: 向本地玩家显示商人药水购买弹窗（是/否按钮 + 15秒倒计时条）。接受返回true，拒绝或超时返回false。
	private async Task<bool> ShowLocalPotionOfferUiAsync(Player player)
	{
		if (NGame.Instance == null) return false;

		var tcs = new TaskCompletionSource<bool>();

		// Root canvas layer so the UI renders above all combat elements.
		var canvas = new CanvasLayer();
		canvas.Layer = 200;
		NGame.Instance.AddChild(canvas);

		// Dark translucent background panel.
		var panel = new ColorRect();
		panel.Color = new Color(0.08f, 0.04f, 0.18f, 0.93f);
		panel.Size = new Vector2(340f, 107f);
		panel.Position = GetOfferPanelPosition(player);
		canvas.AddChild(panel);

		// Offer text.
		var label = new Label();
		label.Text = GetMerchantPotionOfferText();
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.Position = new Vector2(8f, 8f);
		label.Size = new Vector2(324f, 40f);
		label.AutowrapMode = TextServer.AutowrapMode.Word;
		panel.AddChild(label);

		// YES button (left).
		var yesBtn = new Button();
		yesBtn.Text = "YES  (300g)";
		yesBtn.Position = new Vector2(14f, 52f);
		yesBtn.Size = new Vector2(140f, 34f);
		panel.AddChild(yesBtn);

		// NO button (right).
		var noBtn = new Button();
		noBtn.Text = "NO";
		noBtn.Position = new Vector2(186f, 52f);
		noBtn.Size = new Vector2(140f, 34f);
		panel.AddChild(noBtn);

		// Timer bar track (grey background).
		var timerBg = new ColorRect();
		timerBg.Color = new Color(0.25f, 0.25f, 0.25f, 0.80f);
		timerBg.Position = new Vector2(8f, 92f);
		timerBg.Size = new Vector2(324f, 8f);
		panel.AddChild(timerBg);

		// Timer bar fill (gold; shrinks to zero over 7 seconds).
		var timerFg = new ColorRect();
		timerFg.Color = new Color(0.95f, 0.75f, 0.15f, 1.0f);
		timerFg.Position = new Vector2(8f, 92f);
		timerFg.Size = new Vector2(324f, 8f);
		panel.AddChild(timerFg);

		yesBtn.Pressed += () => { if (tcs.TrySetResult(true))  canvas.QueueFree(); };
		noBtn.Pressed  += () => { if (tcs.TrySetResult(false)) canvas.QueueFree(); };

		// Animate the fill bar from full width to zero over 7 seconds, then auto-decline.
		var tween = canvas.CreateTween();
		tween.TweenProperty(timerFg, "size", new Vector2(0f, 8f), 15.0);
		tween.TweenCallback(Callable.From(() =>
		{
			if (tcs.TrySetResult(false)) canvas.QueueFree();
		}));

		return await tcs.Task;
	}

	/// EN: Returns the screen position for the potion offer panel, anchored near the merchant figure.
	///     _merchantPanelPos is set at the start of ShowMerchantHealSequenceAsync so all players
	///     see the panel in the same place (below the merchant, not staggered by player index).
	/// ZH: 返回药水购买弹窗的屏幕位置，锚定在商人图标附近。
	///     _merchantPanelPos在ShowMerchantHealSequenceAsync开始时设置，所有玩家看到的弹窗位置相同（不按玩家索引错开）。
	private Vector2 GetOfferPanelPosition(Player player)
	{
		// If the merchant cameo has set a position, place the panel just below the figure.
		if (_merchantPanelPos != Vector2.Zero)
			return new Vector2(_merchantPanelPos.X - 170f, _merchantPanelPos.Y + 60f);

		// Fallback: fixed center-left position if called outside the merchant sequence.
		var viewport = NGame.Instance?.GetViewport();
		Rect2 visRect = viewport?.GetVisibleRect() ?? new Rect2(0f, 0f, 1920f, 1080f);
		return new Vector2(
			visRect.Position.X + visRect.Size.X * 0.35f - 170f,
			visRect.Position.Y + visRect.Size.Y * 0.35f);
	}

	/// EN: Localized merchant potion offer prompt.
	/// ZH: 本地化的商人药水购买提示文本。
	private string GetMerchantPotionOfferText()
	{
		return ModLoc.T("Another potion for 300g?",
			"再来一瓶药水，只要300金？",
			fra: "Encore une potion pour 300 or ?",
			deu: "Noch ein Trank für 300 Gold?",
			jpn: "ポーション、300ゴールドでいかがですか？",
			kor: "포션 하나 더, 300골드에 어때요?",
			por: "Outra poção por 300 de ouro?",
			rus: "Ещё одно зелье за 300 золота?",
			spa: "¿Otra poción por 300 de oro?");
	}

}
