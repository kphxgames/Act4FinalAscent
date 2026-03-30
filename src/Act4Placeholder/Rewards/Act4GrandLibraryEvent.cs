//=============================================================================
// Act4GrandLibraryEvent.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Act 4 pre-boss voting event, "The Architect's Grand Library". Players vote to steal
//     one of four ancient books, each sealing one Architect mechanic and granting a player
//     benefit for the upcoming Architect boss fight. Supports co-op shared voting via IsShared.
// ZH: 第四幕Boss前投票事件——「建筑师的秘典馆」。玩家投票窃取四本古典之一，每本封印建筑师的一种机制并赋予玩家收益。
//     支持联机共享投票。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

public sealed class Act4GrandLibraryEvent : EventModel
{
	private static readonly string TrialStartedPath = ImageHelper.GetImagePath("events/trial_started.png");
	private static readonly string TrialVfxPath = SceneHelper.GetScenePath("vfx/events/trial_vfx");
	// architect-spell-books.png lives at images/ root in the mod pack (not images/atlases/)
	private static readonly string BooksPath = ImageHelper.GetImagePath("architect-spell-books.png");
	// Text keys for co-op vote resolution (EventSynchronizerGrandLibraryChoicePatch uses these)
	internal const string TextKeyHoly   = "ACT4_GRAND_LIBRARY.HOLY_BOOK";
	internal const string TextKeyShadow = "ACT4_GRAND_LIBRARY.SHADOW_BOOK";
	internal const string TextKeySilver = "ACT4_GRAND_LIBRARY.SILVER_BOOK";
	internal const string TextKeyCursed = "ACT4_GRAND_LIBRARY.CURSED_BOOK";

	// Use IsShared so both players see the vote in co-op.
	// The EventSynchronizerGrandLibraryChoicePatch handles host vote resolution.
	public override bool IsShared => true;

	public override IEnumerable<string> GetAssetPaths(IRunState runState)
	{
		var list = new List<string>(base.GetAssetPaths(runState));
		list.Add(TrialStartedPath);
		list.Add(TrialVfxPath);
		list.Add(BooksPath);
		return list;
	}

	public override Task AfterEventStarted()
	{
		try
		{
			if (NEventRoom.Instance != null)
			{
				// Replace the auto-loaded portrait with the wide trial_started.png background.
				var portrait = PreloadManager.Cache.GetTexture2D(TrialStartedPath);
				if (portrait != null)
					NEventRoom.Instance.Layout.SetPortrait(portrait);

				// Animated atmosphere VFX (light beam, dust particles).
				var vfxScene = PreloadManager.Cache.GetScene(TrialVfxPath);
				if (vfxScene != null)
				{
					var vfx = vfxScene.Instantiate<Node2D>(PackedScene.GenEditState.Disabled);
					vfx.Position = new Vector2(1280f, 600f);
					NEventRoom.Instance.Layout.AddVfxAnchoredToPortrait(vfx);
				}

				// Books sprite: right +50% of image width (482px→+241), up 100% of image height (251px→-251), scale up 50% (0.63→0.945).
				var booksTexture = PreloadManager.Cache.GetTexture2D(BooksPath);
				if (booksTexture != null)
				{
					var books = new Sprite2D();
					books.Texture = booksTexture;
					books.Position = new Vector2(901f, 714f);
					books.Scale = new Vector2(0.945f, 0.945f);
					NEventRoom.Instance.Layout.AddVfxAnchoredToPortrait(books);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[Act4GrandLibraryEvent] AfterEventStarted failed: {ex.Message}");
		}
		return Task.CompletedTask;
	}

	private static LocString PlainText(string text)
	{
		ModSupport.EnsureAct4DynamicTextLocalizationReady();
		LocString val = new LocString("events", "ACT4_DYNAMIC_TEXT");
		val.Add("text", text);
		return val;
	}

	private static LocString L(string en, string zhs = "", string fra = "", string deu = "", string jpn = "", string kor = "", string por = "", string rus = "", string spa = "")
		=> PlainText(ModLoc.T(en, zhs, fra: fra, deu: deu, jpn: jpn, kor: kor, por: por, rus: rus, spa: spa));

	private static IHoverTip Tip(
		string titleEn, string descEn,
		string titleZhs = "", string descZhs = "",
		string titleFra = "", string descFra = "",
		string titleDeu = "", string descDeu = "",
		string titleJpn = "", string descJpn = "",
		string titleKor = "", string descKor = "",
		string titlePor = "", string descPor = "",
		string titleRus = "", string descRus = "",
		string titleSpa = "", string descSpa = "")
	{
		return new HoverTip(
			L(titleEn, titleZhs, fra: titleFra, deu: titleDeu, jpn: titleJpn, kor: titleKor, por: titlePor, rus: titleRus, spa: titleSpa),
			L(descEn, descZhs, fra: descFra, deu: descDeu, jpn: descJpn, kor: descKor, por: descPor, rus: descRus, spa: descSpa));
	}

	protected override IReadOnlyList<EventOption> GenerateInitialOptions()
	{
		return new EventOption[]
		{
			new EventOption(
				this,
				(Func<Task>)TakeHolyBookAsync,
				L("The Holy Tome", "神圣宝典",
					fra: "Le Tome Sacré", deu: "Das Heilige Buch", jpn: "聖典", kor: "성서", por: "O Tomo Sagrado", rus: "Святой Том", spa: "El Tomo Sagrado"),
				L("Affects the Architect's resistance to debuffs.",
					"影响建筑师对减益的抗性。",
					fra: "Affecte la résistance de l'Architecte aux affaiblissements.",
					deu: "Beeinflusst die Schwächungsresistenz des Architekten.",
					jpn: "建築家のデバフ耐性に影響する。",
					kor: "건축가의 디버프 저항에 영향을 준다.",
					por: "Afeta a resistência do Arquiteto a enfraquecimentos.",
					rus: "Влияет на сопротивление Архитектора ослаблениям.",
					spa: "Afecta la resistencia del Arquitecto a los perjuicios."),
				TextKeyHoly,
				new IHoverTip[]
				{
					Tip(
						"Holy Tome Details",
						"Adaptive Resistance efficacy is halved. Expels only newly added Poison/Doom stacks. Turn-start debuff clear is 15%. Negative Strength can remain below 0.",
                        "神圣宝典",
                        "建筑师的自适应抗性效果减半。每次驱散[gold]剧毒[/gold]和[gold]灾厄[/gold]时不同时驱散建筑师身上的层数；建筑师回合开始时仅清除[blue]15%[/blue]的所有的负面效果且力量被降至负值时不再转移给所有玩家和重置力量。",
						titleFra: "Détails du Tome sacré",
						descFra: "L'efficacité de Résistance adaptative est divisée par deux. Expulse uniquement les charges nouvellement ajoutées de Poison/Condamnation. Purge des affaiblissements au début du tour: 15 %. La Force négative peut rester sous 0.",
						titleDeu: "Details des Heiligen Buchs",
						descDeu: "Die Wirksamkeit von Adaptiver Resistenz wird halbiert. Es werden nur neu hinzugefügte Gift/Verhängnis-Stapel ausgestoßen. Debuff-Bereinigung zu Zugbeginn: 15 %. Negative Stärke kann unter 0 bleiben.",
						titleJpn: "聖典の詳細",
						descJpn: "適応的耐性の効果は半減。新たに付与された毒/破滅スタックのみ追放。ターン開始時デバフ除去は15%。筋力は0未満のまま維持可能。",
						titleKor: "성서 상세",
						descKor: "적응형 저항 효율이 절반이 된다. 새로 추가된 독/파멸 중첩만 추방한다. 턴 시작 디버프 정리는 15%. 음수 힘은 0 이하로 유지될 수 있다.",
						titlePor: "Detalhes do Tomo Sagrado",
						descPor: "A eficácia da Resistência Adaptativa é reduzida à metade. Expulsa apenas os acúmulos recém-adicionados de Veneno/Perdição. Limpeza de debuffs no início do turno: 15%. Força negativa pode permanecer abaixo de 0.",
						titleRus: "Подробности Святого Тома",
						descRus: "Эффективность Адаптивного сопротивления уменьшена вдвое. Изгоняются только вновь добавленные стаки Яда/Гибели. Очистка ослаблений в начале хода: 15%. Отрицательная Сила может оставаться ниже 0.",
						titleSpa: "Detalles del Tomo Sagrado",
						descSpa: "La eficacia de Resistencia adaptativa se reduce a la mitad. Solo expulsa acumulaciones recién añadidas de Veneno/Perdición. Limpieza de perjuicios al inicio del turno: 15 %. La Fuerza negativa puede mantenerse por debajo de 0.")
				}),

			new EventOption(
				this,
				(Func<Task>)TakeShadowBookAsync,
				L("The Shadow Tome", "暗影宝典",
					fra: "Le Tome des Ombres", deu: "Das Schattenbuch", jpn: "影の典", kor: "그림자 서", por: "O Tomo das Sombras", rus: "Теневой Том", spa: "El Tomo de las Sombras"),
				L("Affects the strength of the Architect's minions.",
					"影响建筑师召唤物的强度。",
					fra: "Affecte la puissance des sbires de l'Architecte.",
					deu: "Beeinflusst die Stärke der Diener des Architekten.",
					jpn: "建築家の召喚眷属の強さに影響する。",
					kor: "건축가 소환 하수인의 전투력에 영향을 준다.",
					por: "Afeta a força dos lacaios invocados pelo Arquiteto.",
					rus: "Влияет на силу прислужников Архитектора.",
					spa: "Afecta la fuerza de los esbirros del Arquitecto."),
				TextKeyShadow,
				new IHoverTip[]
				{
					Tip(
						"Shadow Tome Details",
						"Player damage against Architect-summoned minions is doubled.",
                        "暗影宝典",
						"所有玩家对建筑师召唤物造成的伤害翻倍。",
						titleFra: "Détails du Tome des ombres",
						descFra: "Les dégâts des joueurs contre les sbires invoqués par l'Architecte sont doublés.",
						titleDeu: "Details des Schattenbuchs",
						descDeu: "Spielerschaden gegen vom Architekten beschworene Diener ist verdoppelt.",
						titleJpn: "影の典の詳細",
						descJpn: "建築家が召喚した手下へのプレイヤーダメージが2倍になる。",
						titleKor: "그림자 서 상세",
						descKor: "건축가가 소환한 하수인에게 주는 플레이어 피해가 2배가 된다.",
						titlePor: "Detalhes do Tomo das Sombras",
						descPor: "O dano dos jogadores contra lacaios invocados pelo Arquiteto é dobrado.",
						titleRus: "Подробности Теневого Тома",
						descRus: "Урон игроков по призванным Архитектором прислужникам удваивается.",
						titleSpa: "Detalles del Tomo de las Sombras",
						descSpa: "El daño de los jugadores contra esbirros invocados por el Arquitecto se duplica.")
				}),

			new EventOption(
				this,
				(Func<Task>)TakeSilverBookAsync,
				L("The Silver Tome", "白银宝典",
					fra: "Le Tome d'Argent", deu: "Das Silberbuch", jpn: "銀の典", kor: "은빛 서", por: "O Tomo de Prata", rus: "Серебряный Том", spa: "El Tomo de Plata"),
				L("Affects the Architect's defensive power.",
					"影响建筑师的防御能力。",
					fra: "Affecte la puissance défensive de l'Architecte.",
					deu: "Beeinflusst die Verteidigungskraft des Architekten.",
					jpn: "建築家の防御能力に影響する。",
					kor: "건축가의 방어 기제에 영향을 준다.",
					por: "Afeta o poder defensivo do Arquiteto.",
					rus: "Влияет на защитные механики Архитектора.",
					spa: "Afecta el poder defensivo del Arquitecto."),
				TextKeySilver,
				new IHoverTip[]
				{
					Tip(
						"Silver Tome Details",
						"Architect cannot retain Block and cannot gain Block from its mechanics. Each player gains Block equal to 5% max HP at player-turn start.",
                        "白银宝典",
						"建筑师无法保留格挡，且无法从其他机制获得格挡。所有玩家回合开始时获得等于最大生命值5%的格挡。",
						titleFra: "Détails du Tome d'argent",
						descFra: "L'Architecte ne peut ni conserver ni gagner de Défense via ses mécaniques. Chaque joueur gagne une Défense égale à 5 % de ses PV max au début de son tour.",
						titleDeu: "Details des Silberbuchs",
						descDeu: "Der Architekt kann weder Block behalten noch durch seine Mechaniken Block erhalten. Jeder Spieler erhält zu Beginn seines Zuges Block in Höhe von 5 % seiner max. LP.",
						titleJpn: "銀の典の詳細",
						descJpn: "建築家はブロックを維持できず、固有ギミックからもブロックを得られない。各プレイヤーは自分のターン開始時に最大HPの5%分のブロックを得る。",
						titleKor: "은빛 서 상세",
						descKor: "건축가는 방어도를 유지할 수 없고, 자신의 기믹으로 방어도를 얻을 수도 없다. 각 플레이어는 자신의 턴 시작 시 최대 HP의 5%만큼 방어도를 얻는다.",
						titlePor: "Detalhes do Tomo de Prata",
						descPor: "O Arquiteto não pode manter Bloqueio nem ganhar Bloqueio por suas mecânicas. Cada jogador ganha Bloqueio igual a 5% do HP máximo no início do próprio turno.",
						titleRus: "Подробности Серебряного Тома",
						descRus: "Архитектор не может сохранять Блок и не получает Блок от своих механик. Каждый игрок в начале своего хода получает Блок, равный 5% от макс. ОЗ.",
						titleSpa: "Detalles del Tomo de Plata",
						descSpa: "El Arquitecto no puede conservar Bloqueo ni obtener Bloqueo por sus mecánicas. Cada jugador gana Bloqueo igual al 5 % de su PV máximo al inicio de su turno.")
				}),

			new EventOption(
				this,
				(Func<Task>)TakeCursedBookAsync,
				L("The Cursed Tome", "诅咒宝典",
					fra: "Le Tome Maudit", deu: "Das Verfluchte Buch", jpn: "呪いの典", kor: "저주받은 서", por: "O Tomo Amaldiçoado", rus: "Проклятый Том", spa: "El Tomo Maldito"),
				L("Affects the Architect's damage mechanics.",
					"影响建筑师的伤害机制。",
					fra: "Affecte les mécaniques de dégâts de l'Architecte.",
					deu: "Beeinflusst die Schadensmechaniken des Architekten.",
					jpn: "建築家のダメージ機構に影響する。",
					kor: "건축가의 피해 메커니즘에 영향을 준다.",
					por: "Afeta as mecânicas de dano do Arquiteto.",
					rus: "Влияет на механики урона Архитектора.",
					spa: "Afecta las mecánicas de daño del Arquitecto."),
				TextKeyCursed,
				new IHoverTip[]
				{
					Tip(
						"Cursed Tome Details",
						"Architect loses Readings and Block Piercer. Players: +2 max Energy, +2 draw/turn, but gain 99 Vulnerable/Weak/Frail.",
                        "诅咒宝典",
                        "建筑师将失去解读与穿透格挡能力。所有玩家将在每回合开始时额外获得[blue]2[/blue]点[gold]能量[/gold]和抽取[blue]2[/blue]张卡牌，[red]建筑师的攻击欲望会更频繁且所有玩家获得99层易伤/虚弱/脆弱[/red]。",
						titleFra: "Détails du Tome maudit",
						descFra: "L'Architecte perd Lectures et Perce-bloc. Joueurs : +2 Énergie max, +2 pioche/tour, mais gagnent 99 Vulnérable/Faible/Fragile.",
						titleDeu: "Details des Verfluchten Buchs",
						descDeu: "Der Architekt verliert Lesungen und Blockbrecher. Spieler: +2 max. Energie, +2 Kartenziehen/Zug, erhalten aber 99 Verletzlich/Schwach/Gebrechlich.",
						titleJpn: "呪いの典の詳細",
						descJpn: "建築家は読解とブロック貫通を失う。プレイヤーは最大エナジー+2、毎ターン2枚追加ドローを得るが、脆弱/弱体/虚弱を99得る。",
						titleKor: "저주받은 서 상세",
						descKor: "건축가는 해석과 방어 관통을 잃는다. 플레이어: 최대 에너지 +2, 턴당 드로우 +2, 대신 취약/약화/허약 99를 얻는다.",
						titlePor: "Detalhes do Tomo Amaldiçoado",
						descPor: "O Arquiteto perde Leituras e Perfurar Bloqueio. Jogadores: +2 Energia máxima, +2 compra/turno, mas recebem 99 Vulnerável/Fraco/Frágil.",
						titleRus: "Подробности Проклятого Тома",
						descRus: "Архитектор теряет Анализ и Пробой блока. Игроки: +2 к макс. Энергии, +2 добора/ход, но получают 99 Уязвимости/Слабости/Хрупкости.",
						titleSpa: "Detalles del Tomo Maldito",
						descSpa: "El Arquitecto pierde Lecturas y Perfora Bloqueo. Jugadores: +2 de Energía máxima, +2 robo/turno, pero obtienen 99 Vulnerable/Débil/Frágil.")
				}),
		};
	}

	private Task TakeHolyBookAsync()
	{
		Act4Settings.HolyBookChosen = true;
		Act4Settings.ShadowBookChosen = false;
		Act4Settings.SilverBookChosen = false;
		Act4Settings.CursedBookChosen = false;
		ModSupport.PersistBookChoiceNow();
		SetEventFinished(L(
			"You pocket the Holy Tome. The Architect's divine ward is sealed.",
			"你收起了神圣宝典。建筑师的神圣庇护被封印了。",
			fra: "Vous empochez le Tome Sacré. La protection divine de l'Architecte est scellée.",
			deu: "Ihr steckt das Heilige Buch ein. Der göttliche Schutz des Architekten ist versiegelt.",
			jpn: "聖典をポケットに入れた。建築家の神聖な守護は封じられた。",
			kor: "성서를 집어넣었습니다. 건축가의 신성한 보호가 봉인되었습니다.",
			por: "Você embolsa o Tomo Sagrado. A proteção divina do Arquiteto está selada.",
			rus: "Вы кладете Святой Том в карман. Божественная защита Архитектора запечатана.",
			spa: "Guardas el Tomo Sagrado. La protección divina del Arquitecto está sellada."));
		return Task.CompletedTask;
	}

	private Task TakeShadowBookAsync()
	{
		Act4Settings.HolyBookChosen = false;
		Act4Settings.ShadowBookChosen = true;
		Act4Settings.SilverBookChosen = false;
		Act4Settings.CursedBookChosen = false;
		ModSupport.PersistBookChoiceNow();
		SetEventFinished(L(
			"You pocket the Shadow Tome. The Architect's shadow grows dim.",
			"你收起了暗影宝典。建筑师的暗影渐渐暗淡。",
			fra: "Vous empochez le Tome des Ombres. L'ombre de l'Architecte s'assombrit.",
			deu: "Ihr steckt das Schattenbuch ein. Der Schatten des Architekten wird schwächer.",
			jpn: "影の典をポケットに入れた。建築家の影は薄れていく。",
			kor: "그림자 서를 집어넣었습니다. 건축가의 그림자가 희미해집니다.",
			por: "Você embolsa o Tomo das Sombras. A sombra do Arquiteto escurece.",
			rus: "Вы кладете Теневой Том в карман. Тень Архитектора тускнеет.",
			spa: "Guardas el Tomo de las Sombras. La sombra del Arquitecto se oscurece."));
		return Task.CompletedTask;
	}

	private Task TakeSilverBookAsync()
	{
		Act4Settings.HolyBookChosen = false;
		Act4Settings.ShadowBookChosen = false;
		Act4Settings.SilverBookChosen = true;
		Act4Settings.CursedBookChosen = false;
		ModSupport.PersistBookChoiceNow();
		SetEventFinished(L(
			"You pocket the Silver Tome. The Architect's eternal shield shatters.",
			"你收起了白银宝典。建筑师的永恒护盾破碎了。",
			fra: "Vous empochez le Tome d'Argent. Le bouclier éternel de l'Architecte se brise.",
			deu: "Ihr steckt das Silberbuch ein. Der ewige Schild des Architekten zerbricht.",
			jpn: "銀の典をポケットに入れた。建築家の永遠の盾が砕け散った。",
			kor: "은빛 서를 집어넣었습니다. 건축가의 영원한 방어막이 산산조각 납니다.",
			por: "Você embolsa o Tomo de Prata. O escudo eterno do Arquiteto se rompe.",
			rus: "Вы кладете Серебряный Том в карман. Вечный щит Архитектора рассыпается.",
			spa: "Guardas el Tomo de Plata. El escudo eterno del Arquitecto se rompe."));
		return Task.CompletedTask;
	}

	private Task TakeCursedBookAsync()
	{
		Act4Settings.HolyBookChosen = false;
		Act4Settings.ShadowBookChosen = false;
		Act4Settings.SilverBookChosen = false;
		Act4Settings.CursedBookChosen = true;
		ModSupport.PersistBookChoiceNow();
		SetEventFinished(L(
			"You pocket the Cursed Tome. Forbidden knowledge seeps into your mind.",
			"你收起了诅咒宝典。禁忌知识渗入你的脑海。",
			fra: "Vous empochez le Tome Maudit. Des connaissances interdites s'infiltrent dans votre esprit.",
			deu: "Ihr steckt das Verfluchte Buch ein. Verbotenes Wissen dringt in euren Geist ein.",
			jpn: "呪いの典をポケットに入れた。禁断の知識が心に忍び込む。",
			kor: "저주받은 서를 집어넣었습니다. 금지된 지식이 마음속으로 스며듭니다.",
			por: "Você embolsa o Tomo Amaldiçoado. Conhecimento proibido permeia sua mente.",
			rus: "Вы кладете Проклятый Том в карман. Запретные знания проникают в ваш разум.",
			spa: "Guardas el Tomo Maldito. El conocimiento prohibido se filtra en tu mente."));
		return Task.CompletedTask;
	}
}
