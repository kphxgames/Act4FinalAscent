//=============================================================================
// Act4EmpyrealCache.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Act 4 Empyreal Cache reward event offering a curated pool of 14 relic/gold/potion choices followed by a final bonus-bundle selection from themed preset packages.
// ZH: 第四幕「帝国宝库」奖励事件，提供14种圣物/金币/药水选项，结束时从预设主题礼包中再选一个额外奖励包。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

public sealed class Act4EmpyrealCache : Act4RewardEventBase
{
	private enum EmpyrealFinishBundle
	{
		PotionBeltAndFairy,
		WhiteStar,
		SealOfGoldAndGold,
		Glitter
	}

	private bool _hasShownFinalBonusChoice;

	private bool _weakestBonusEligibilityResolved;

	private bool _weakestBonusEligible;

	private List<EmpyrealFinishBundle>? _rolledFinalBundles;

	protected override string EventPrefix => "ACT4_EMPYREAL_CACHE";

	protected override int TotalStages
	{
		get
		{
			return 2 + (Act4Settings.ExtraRewardsActiveForCurrentRun ? 1 : 0);
		}
	}

	protected override IReadOnlyList<RewardOfferDefinition> OfferPool { get; } = new RewardOfferDefinition[13]
	{
		Act4RewardEventBase.CreateFairyOffer("ACT4_DYNAMIC_FAIRY"),
		Act4RewardEventBase.CreateBloodPotionOffer("ACT4_DYNAMIC_BLOOD_POTION"),
		Act4RewardEventBase.CreateGoldOffer("ACT4_DYNAMIC_GOLD_777", 777),
		Act4RewardEventBase.CreateRelicOffer<LoomingFruit>("ACT4_DYNAMIC_LOOMING_FRUIT"),
		Act4RewardEventBase.CreateRelicOffer<Candelabra>("ACT4_DYNAMIC_CANDELABRA"),
		Act4RewardEventBase.CreateRelicOffer<Chandelier>("ACT4_DYNAMIC_CHANDELIER"),
		Act4RewardEventBase.CreateRelicOffer<MeatCleaver>("ACT4_DYNAMIC_MEAT_CLEAVER"),
		Act4RewardEventBase.CreateRelicOffer<MummifiedHand>("ACT4_DYNAMIC_MUMMIFIED_HAND"),
		Act4RewardEventBase.CreateRelicOffer<MusicBox>("ACT4_DYNAMIC_MUSIC_BOX"),
		Act4RewardEventBase.CreateRelicOffer<OldCoin>("ACT4_DYNAMIC_OLD_COIN"),
		Act4RewardEventBase.CreateRelicOffer<PaelsFlesh>("ACT4_DYNAMIC_PAELS_FLESH"),
		Act4RewardEventBase.CreateRelicOffer<StoneCalendar>("ACT4_DYNAMIC_STONE_CALENDAR"),
		Act4RewardEventBase.CreateRelicOffer<TuningFork>("ACT4_DYNAMIC_TUNING_FORK")
	};

	private EmpyrealFinishBundle? _chosenBundle;

	protected override async Task FinishBonusAsync()
	{
		if (_chosenBundle == null) return;
		switch (_chosenBundle.Value)
		{
			case EmpyrealFinishBundle.PotionBeltAndFairy:
				await ObtainUniqueRelicOrGoldAsync<PotionBelt>(200m);
				await GainFairyPotionAsync();
				break;
			case EmpyrealFinishBundle.WhiteStar:
				await ObtainUniqueRelicOrGoldAsync<WhiteStar>(200m);
				break;
			case EmpyrealFinishBundle.SealOfGoldAndGold:
				await ObtainUniqueRelicOrGoldAsync<SealOfGold>(200m);
				await GainGoldAsync(100m);
				break;
			case EmpyrealFinishBundle.Glitter:
				await ObtainUniqueRelicOrGoldAsync<Glitter>(200m);
				break;
		}
	}

	protected override async Task FinishStageAsync()
	{
		StageIndex++;
		if (StageIndex >= TotalStages)
		{
			if (_hasShownFinalBonusChoice)
			{
				await FinishBonusAsync();
				await TryHandleWeakestBonusOrFinishAsync();
			}
			else
			{
				_hasShownFinalBonusChoice = true;
				ShowFinalBundleChoice();
			}
		}
		else
			SetEventState(L10NLookup($"{EventPrefix}.pages.STAGE_{StageIndex + 1}.description"), BuildStageOptions());
	}

	private void ShowFinalBundleChoice()
	{
		List<EmpyrealFinishBundle> rolled = GetRolledFinalBundles();
		var options = new List<EventOption>();
		for (int i = 0; i < Math.Min(2, rolled.Count); i++)
		{
			EmpyrealFinishBundle bundle = rolled[i];
			string key = $"ACT4_EMPYREAL_CACHE_FINAL_{bundle}";
			string desc = BuildBundleDescription(bundle);
			string title = ModLoc.T("Take the supplies", "领取补给", fra: "Prendre les fournitures", deu: "Vorräte nehmen", jpn: "補給を受け取る", kor: "보급품 받기", por: "Pegar os suprimentos", rus: "Взять припасы", spa: "Tomar los suministros");
			var capturedBundle = bundle;
			IEnumerable<IHoverTip> hoverTips = BuildBundleHoverTips(bundle);
			options.Add(Act4RewardEventBase.CreateSimpleOption(this, async () =>
			{
				_chosenBundle = capturedBundle;
				await FinishFinalBundleChoiceAsync();
			}, key, title, desc, hoverTips));
		}
		SetEventState(Act4RewardEventBase.PlainText(ModLoc.T(
			"The stranger opens a final compartment and sets aside a parting cache. Choose your reward.",
			"陌生人打开最后一个隔间，将临别礼物放在一旁。请选择你的奖励。",
			fra: "L'etranger ouvre un dernier compartiment et met de cote une cache d'adieu. Choisissez votre recompense.",
			deu: "Der Fremde öffnet ein letztes Fach und stellt einen Abschiedsvorrat beiseite. Wähle deine Belohnung.",
			jpn: "見知らぬ人が最後の仕切りを開け、別れの荷物を脇に置きます。報酬を選んでください。",
			kor: "낯선 이가 마지막 칸을 열고 작별 보따리를 옆에 내려놓습니다. 당신의 보상을 선택하세요.",
			por: "O estranho abre um último compartimento e separa um cache de despedida. Escolha sua recompensa.",
			rus: "Незнакомец открывает последний отсек и откладывает прощальный тайник. Выберите свою награду.",
			spa: "El extraño abre un último compartimento y aparta un alijo de despedida. Elige tu recompensa.")), options);
	}

	private async Task FinishFinalBundleChoiceAsync()
	{
		await FinishBonusAsync();
		await TryHandleWeakestBonusOrFinishAsync();
	}

	private async Task TryHandleWeakestBonusOrFinishAsync()
	{
		Player? owner = ((EventModel)this).Owner;
		RunState? runState = owner?.RunState as RunState;
		bool isSolo = runState != null && runState.Players.Count <= 1;

		if (isSolo && owner != null)
		{
			// EN: Single-player: unconditional Empyreal Cache buff. Reuses the weakest-buff
			//     persistence so ApplyAct4WeakestPlayerBuffAsync applies +4/+4 each combat.
			// ZH: 单人模式：帝国宝库无条件增益。复用最弱增益持久化机制，
			//     ApplyAct4WeakestPlayerBuffAsync 每场战斗施加 +4/+4。
			ModSupport.GrantAct4WeakestBuff(owner);

			string classBonusSuffix = BuildClassBonusSuffix(owner, 2);
			string buffKey = "ACT4_EMPYREAL_CACHE_SOLO_BUFF_NOTIFY";
			string buffTitle = ModLoc.T("Understood", "明白了", fra: "Compris", deu: "Verstanden", jpn: "了解しました", kor: "이해했습니다", por: "Entendido", rus: "Понятно", spa: "Entendido");
			string buffDescBase = ModLoc.T("+4 Strength and +4 Dexterity", "+4力量和+4敏捷", fra: "+4 Force et +4 Dextérité", deu: "+4 Stärke und +4 Geschicklichkeit", jpn: "+4力と+4敏捷性", kor: "+4 힘과 +4 민첩성", por: "+4 Força e +4 Destreza", rus: "+4 Сила и +4 Ловкость", spa: "+4 Fuerza y +4 Destreza");
			string buffDescTrail = ModLoc.T(" will be applied at the start of every Act 4 combat.", "将在每场第四幕战斗开始时生效。", fra: " seront appliqués au début de chaque combat de l'Acte 4.", deu: " werden zu Beginn jedes Kampfes in Akt 4 angewendet.", jpn: "が第4幕の各戦闘開始時に適用されます。", kor: "이(가) 4막의 각 전투 시작 시 적용됩니다.", por: " serão aplicados no início de cada combate do Ato 4.", rus: " будут применяться в начале каждого боя Акта 4.", spa: " se aplicarán al inicio de cada combate del Acto 4.");
			string buffDesc = buffDescBase + classBonusSuffix + buffDescTrail;

			string bodyBase = ModLoc.T(
				"The stranger studies you for a long moment, as though measuring what you carry alone. \"To walk this road with no companion takes a different kind of resolve,\" they say quietly. \"Here, something to help you endure what lies ahead.\" You will enter every Act 4 battle with +4 Strength and +4 Dexterity",
				"陌生人端详了你良久，仿佛在衡量你独自承受了什么。“独行此路需要另一种意志，”他们低声说道。“拿着这个——帮你撑过前方的考验。”你将在每场第四幕战斗中获得+4力量和+4敏捷",
				fra: "L'étranger vous observe longuement, comme s'il mesurait ce que vous portez seul. \"Parcourir cette route sans compagnon demande une autre sorte de détermination\", dit-il calmement. \"Tenez, pour vous aider à endurer ce qui vous attend.\" Vous entrerez chaque combat de l'Acte 4 avec +4 Force et +4 Dextérité",
				deu: "Der Fremde betrachtet Sie lange, als würde er abschätzen, was Sie allein tragen. \"Diesen Weg ohne Gefährten zu gehen erfordert eine andere Art von Entschlossenheit\", sagt er leise. \"Hier, etwas, das Ihnen hilft zu bestehen, was vor Ihnen liegt.\" Sie werden jeden Kampf in Akt 4 mit +4 Stärke und +4 Geschicklichkeit beginnen",
				jpn: "見知らぬ人があなたをじっと見つめ、一人で背負うものを量るかのように言います。「仲間なしにこの道を歩くには別の覚悟が必要だ」と静かに言います。「これを持って——これから先を耐え抜くために。」第4幕の各戦闘に+4力と+4敏捷性で臨みます",
				kor: "낯선 이가 한참 동안 당신을 살피며, 혼자 짊어진 무게를 가늠하는 듯합니다. \"동료 없이 이 길을 걷는 건 다른 종류의 각오가 필요하지요\"라고 조용히 말합니다. \"여기요, 앞으로의 시련을 견디는 데 도움이 될 겁니다.\" 4막의 모든 전투를 +4 힘과 +4 민첩성으로 시작합니다",
				por: "O estranho observa você por um longo momento, como se medisse o que você carrega sozinho. \"Trilhar este caminho sem companheiro exige um tipo diferente de determinação\", diz ele calmamente. \"Tome, algo para ajudá-lo a suportar o que está por vir.\" Você entrará em cada combate do Ato 4 com +4 Força e +4 Destreza",
				rus: "Незнакомец долго изучает вас, словно оценивая то, что вы несёте в одиночку. «Идти по этому пути без спутника требует иной решимости», тихо говорит он. «Вот, кое-что, чтобы вы выстояли в грядущих испытаниях.» Вы будете входить в каждый бой Акта 4 с +4 Силой и +4 Ловкостью",
				spa: "El extraño lo estudia largamente, como si midiera lo que carga solo. \"Recorrer este camino sin compañero requiere otra clase de resolución\", dice en voz baja. \"Tome, algo que le ayude a soportar lo que viene.\" Entrará en cada combate del Acto 4 con +4 Fuerza y +4 Destreza"
			);
			string bodyText = bodyBase + classBonusSuffix + ".";

			var buffOptions = new System.Collections.Generic.List<EventOption>
			{
				Act4RewardEventBase.CreateSimpleOption(this, async () =>
				{
					SetEventFinished(L10NLookup(EventPrefix + ".pages.FINISH.description"));
				}, buffKey, buffTitle, buffDesc)
			};
			SetEventState(Act4RewardEventBase.PlainText(bodyText), buffOptions);
		}
		else if (ShouldOfferWeakestBonus())
		{
			ModSupport.GrantAct4WeakestBuff(((EventModel)this).Owner);
			// EN: Show an extra event screen only for the weakest player. Because non-shared events
			//     run per-player on BOTH machines, this SetEventState and the subsequent option click
			//     are processed identically on host and client, no desync. Map navigation already
			//     waits for all players, so the extra click causes no stalling.
			// ZH: 仅为输出最弱的玩家显示额外事件界面。由于非共享事件在双方机器上逐玩家运行，
			//     此SetEventState及后续选项点击在主机和客户端上处理结果相同——不会引发同步问题。
			//     地图导航本来就等待所有玩家，额外的点击不会造成卡顿。
			string classBonusSuffix = BuildClassBonusSuffix(((EventModel)this).Owner, 4);
			string buffKey = "ACT4_EMPYREAL_CACHE_WEAKEST_BUFF_NOTIFY";
			string buffTitle = ModLoc.T("Understood", "明白了", fra: "Compris", deu: "Verstanden", jpn: "了解しました", kor: "이해했습니다", por: "Entendido", rus: "Понятно", spa: "Entendido");
			string buffDescBase = ModLoc.T("+8 Strength and +8 Dexterity", "+8力量和+8敏捷", fra: "+8 Force et +8 Dextérité", deu: "+8 Stärke und +8 Geschicklichkeit", jpn: "+8力と+8敏捷性", kor: "+8 힘과 +8 민첩성", por: "+8 Força e +8 Destreza", rus: "+8 Сила и +8 Ловкость", spa: "+8 Fuerza y +8 Destreza");
			string buffDescTrail = ModLoc.T(" will be applied at the start of every Act 4 combat.", "将在每场第四幕战斗开始时生效。", fra: " seront appliqués au début de chaque combat de l'Acte 4.", deu: " werden zu Beginn jedes Kampfes in Akt 4 angewendet.", jpn: "が第4幕の各戦闘開始時に適用されます。", kor: "이(가) 4막의 각 전투 시작 시 적용됩니다.", por: " serão aplicados no início de cada combate do Ato 4.", rus: " будут применяться в начале каждого боя Акта 4.", spa: " se aplicarán al inicio de cada combate del Acto 4.");
			string buffDesc = buffDescBase + classBonusSuffix + buffDescTrail;
			// EN: Build the color-coded damage summary to append to the dialogue text.
			// ZH: 构建带颜色标记的伤害摘要，追加到对话文本中。
			string damageSummary = ModSupport.BuildDamageSummaryForWeakestDialogue(((EventModel)this).Owner);
			var buffOptions = new System.Collections.Generic.List<EventOption>
			{
				Act4RewardEventBase.CreateSimpleOption(this, async () =>
				{
					// EN: Grant the weakest player 2 card reward drafts:
					//     - 1 normal encounter selection (regular rarity odds, 3 choices)
					//     - 1 higher-rarity selection (boss encounter odds, 3 choices)
					// ZH: 给予最弱玩家2次卡牌奖励选择：普通遭遇稀有度（3选1）+ Boss遭遇稀有度（3选1）。
					Player? cardOwner = ((EventModel)this).Owner;
					if (cardOwner != null)
					{
						var normalOpts = new CardCreationOptions(
							new List<CardPoolModel> { cardOwner.Character.CardPool },
							CardCreationSource.Encounter,
							CardRarityOddsType.RegularEncounter);
						var rareOpts = new CardCreationOptions(
							new List<CardPoolModel> { cardOwner.Character.CardPool },
							CardCreationSource.Encounter,
							CardRarityOddsType.BossEncounter);
						var cardReward1 = new CardReward(normalOpts, 3, cardOwner);
						var cardReward2 = new CardReward(rareOpts, 3, cardOwner);
						await RewardsCmd.OfferCustom(cardOwner, new List<Reward> { cardReward1, cardReward2 });
					}
					SetEventFinished(L10NLookup(EventPrefix + ".pages.FINISH.description"));
				}, buffKey, buffTitle, buffDesc)
			};
			SetEventState(Act4RewardEventBase.PlainText(ModLoc.T(
				"Your allies outpaced you in the struggle. The stranger notes the gap and presses something extra into your hands. \"You dealt the least damage of your party,\" they say plainly. \"Take this, it should even the odds.\" You will enter every Act 4 battle with +8 Strength and +8 Dexterity.",
				"你的队友在战斗中超过了你。讲者注意到差距，将额外的东西交到你手中。“你在队伍中造成的伤害最少，”他直接说道，“接受这个——应能补平差距。”你将在每场第四幕战斗中获得+8力量和+8敏捷。",
				fra: "Vos alliés vous ont dépassé dans la lutte. L'étranger remarque l'écart et vous glisse quelque chose. \"Vous avez infligé le moins de dégâts de votre groupe\", dit-il franchement. \"Prenez ceci, cela devrait égaler les chances.\" Vous entrerez chaque combat de l'Acte 4 avec +8 Force et +8 Dextérité.",
				deu: "Ihre Verbündeten übertrafen Sie im Kampf. Der Fremde bemerkt den Unterschied und drückt Ihnen etwas in die Hand. \"Sie haben am wenigsten Schaden in Ihrer Gruppe verursacht\", sagt er offen. \"Nehmen Sie dies, es sollte die Chancen ausgleichen.\" Sie werden jeden Kampf in Akt 4 mit +8 Stärke und +8 Geschicklichkeit beginnen.",
				jpn: "お供の仲間が戦いであなたを上回りました。講誓人はその差を見て、手に余分なものを渡します。「あなたはパーティで最も少ないダメージを与えました」と彼は率直に言います。「これを受け取ってください——均衡が取れるはずです。」第4幕の各戦闘に+8力と+8敏捷性で臨みます。",
				kor: "당신의 동료들이 전투에서 앞서 나갔습니다. 낙선인은 격차를 알아보고 손에 묶가를 더 쥐어줍니다. \"당신이 파티에서 가장 적은 피해를 입혀다군요\"라고 그가 솔직하게 말합니다. \"이것을 받으세요, 균형을 맞춰드릴 겁니다.\" 4막의 모든 전투를 +8 힘과 +8 민첫성으로 시작합니다.",
				por: "Seus aliados superaram você na luta. O estranho nota a diferença e pressiona algo extra em suas mãos. \"Você causou o menor dano do grupo\", ele diz diretamente. \"Pegue isto, deve equilibrar as chances.\" Você entrará em cada combate do Ato 4 com +8 Força e +8 Destreza.",
				rus: "Ваши союзники превзошли вас в бою. Незнакомец замечает разрыв и вкладывает что-то дополнительное в ваши руки. «Вы нанесли наименьший ущерб в группе», прямо говорит он. «Возьмите это, должно выровнять шансы.» Вы будете входить в каждый бой Акта 4 с +8 Силой и +8 Ловкостью.",
				spa: "Sus aliados lo superaron en la batalla. El extraño nota la brecha y le presiona algo en las manos. \"Usted infligió el menor daño de su grupo\", dice llanamente. \"Tome esto, debería equilibrar las probabilidades.\" Entrará en cada combate del Acto 4 con +8 Fuerza y +8 Destreza."
			) + damageSummary), buffOptions);
		}
		else
		{
			SetEventFinished(L10NLookup(EventPrefix + ".pages.FINISH.description"));
		}
		await Task.CompletedTask;
	}

	private bool ShouldOfferWeakestBonus()
	{
		if (_weakestBonusEligibilityResolved)
		{
			return _weakestBonusEligible;
		}
		_weakestBonusEligibilityResolved = true;
		Player? owner = ((EventModel)this).Owner;
		RunState? runState = owner?.RunState as RunState;
		_weakestBonusEligible = owner != null && runState != null && runState.Players.Count >= 2 && ModSupport.IsOwnerWeakestRunDamageContributor(owner);
		return _weakestBonusEligible;
	}

	/// <summary>
	/// EN: Returns a localized suffix like ", and +N Focus" or ", and +N Envenom" based on the player's character.
	///     Returns "" for characters with no special bonus (Ironclad, Watcher, etc.).
	/// ZH: 根据玩家角色返回本地化后缀（如"，以及+N集中"或"，以及+N淬毒"），
	///     无特殊加成的角色返回空字符串。
	/// </summary>
	private static string BuildClassBonusSuffix(Player? player, int amount)
	{
		if (player == null) return "";
		if (player.Character is Defect)
			return ModLoc.T($", and +{amount} Focus", $", +{amount} Focus", fra: $", et +{amount} Focus", deu: $", und +{amount} Fokus", jpn: $", +{amount} Focus", kor: $", +{amount} Focus", por: $", e +{amount} Foco", rus: $", +{amount} Fokus", spa: $", y +{amount} Enfoque");
		if (player.Character is Silent)
			return ModLoc.T($", and +{amount} Envenom", $", +{amount} Envenom", fra: $", et +{amount} Venin", deu: $", und +{amount} Vergiftung", jpn: $", +{amount} Envenom", kor: $", +{amount} Envenom", por: $", e +{amount} Envenenar", rus: $", +{amount} Envenom", spa: $", y +{amount} Envenenar");
		return "";
	}

	private List<EmpyrealFinishBundle> GetRolledFinalBundles()
	{
		if (_rolledFinalBundles != null)
		{
			return _rolledFinalBundles;
		}
		List<EmpyrealFinishBundle> all = new List<EmpyrealFinishBundle>
		{
			EmpyrealFinishBundle.PotionBeltAndFairy,
			EmpyrealFinishBundle.WhiteStar,
			EmpyrealFinishBundle.SealOfGoldAndGold,
			EmpyrealFinishBundle.Glitter
		};
		Rng rng = GetPerPlayerBundleRng();
		for (int i = all.Count - 1; i > 0; i--)
		{
			int j = rng.NextInt(i + 1);
			EmpyrealFinishBundle tmp = all[i];
			all[i] = all[j];
			all[j] = tmp;
		}
		_rolledFinalBundles = all.Take(2).ToList();
		return _rolledFinalBundles;
	}

	private Rng GetPerPlayerBundleRng()
	{
		RunState? runState = ((EventModel)this).Owner?.RunState as RunState;
		uint seed = runState?.Rng?.Seed ?? 0u;
		ulong ownerNetId = ((EventModel)this).Owner?.NetId ?? 0uL;
		return new Rng(StableHash32($"empyreal_bundles:{seed}:{ownerNetId}"));
	}

	private IEnumerable<IHoverTip> BuildBundleHoverTips(EmpyrealFinishBundle bundle)
	{
		switch (bundle)
		{
			case EmpyrealFinishBundle.PotionBeltAndFairy:
				if (OwnerHasRelic<PotionBelt>())
					return new IHoverTip[] { HoverTipFactory.FromPotion<FairyInABottle>() };
				return HoverTipFactory.FromRelic<PotionBelt>()
					.Append(HoverTipFactory.FromPotion<FairyInABottle>());
			case EmpyrealFinishBundle.WhiteStar:
				return OwnerHasRelic<WhiteStar>() ? Array.Empty<IHoverTip>() : HoverTipFactory.FromRelic<WhiteStar>();
			case EmpyrealFinishBundle.SealOfGoldAndGold:
				return OwnerHasRelic<SealOfGold>() ? Array.Empty<IHoverTip>() : HoverTipFactory.FromRelic<SealOfGold>();
			case EmpyrealFinishBundle.Glitter:
				return OwnerHasRelic<Glitter>() ? Array.Empty<IHoverTip>() : HoverTipFactory.FromRelic<Glitter>();
			default:
				return Array.Empty<IHoverTip>();
		}
	}

	private string BuildBundleDescription(EmpyrealFinishBundle bundle)
	{
		List<string> val = new List<string>();
		switch (bundle)
		{
			case EmpyrealFinishBundle.PotionBeltAndFairy:
				val.Add(OwnerHasRelic<PotionBelt>() ? ModLoc.T("+200 Gold + Fairy in a Bottle", "+200 金币 + 瓶中仙灵", fra: "+200 Or + Fée en bouteille", deu: "+200 Gold + Fee in einer Flasche", jpn: "+200 ゴールド + 瓶の中の妖精", kor: "+200 골드 + 병 속의 요정", por: "+200 Ouro + Fada em uma Garrafa", rus: "+200 Злт + Фея в бутылке", spa: "+200 Oro + Hada en una Botella") : ModLoc.T("Potion Belt + Fairy in a Bottle", "药带 + 瓶中仙灵", fra: "Ceinture à potions + Fée en bouteille", deu: "Tränkegürtel + Fee in einer Flasche", jpn: "ポーションベルト + 瓶の中の妖精", kor: "물약 벨트 + 병 속의 요정", por: "Cinto de Poções + Fada em uma Garrafa", rus: "Пояс зелий + Фея в бутылке", spa: "Cinturón de Pociones + Hada en una Botella"));
				break;
			case EmpyrealFinishBundle.WhiteStar:
				val.Add(OwnerHasRelic<WhiteStar>() ? ModLoc.T("+200 Gold", "+200 金币", fra: "+200 Or", deu: "+200 Gold", jpn: "+200 ゴールド", kor: "+200 골드", por: "+200 Ouro", rus: "+200 Злт", spa: "+200 Oro") : ModLoc.T("White Star", "白星", fra: "Étoile Blanche", deu: "Weißer Stern", jpn: "白星", kor: "흰별", por: "Estrela Branca", rus: "Белая Звезда", spa: "Estrella Blanca"));
				break;
			case EmpyrealFinishBundle.SealOfGoldAndGold:
				val.Add(OwnerHasRelic<SealOfGold>() ? ModLoc.T("+300 Gold", "+300 金币", fra: "+300 Or", deu: "+300 Gold", jpn: "+300 ゴールド", kor: "+300 골드", por: "+300 Ouro", rus: "+300 Злт", spa: "+300 Oro") : ModLoc.T("Seal of Gold + 100 Gold", "黄金印章 + 100 金币", fra: "Sceau d'Or + 100 Or", deu: "Goldsiegel + 100 Gold", jpn: "黄金の印章 + 100 ゴールド", kor: "황금 인장 + 100 골드", por: "Selo de Ouro + 100 Ouro", rus: "Золотая Печать + 100 Злт", spa: "Sello de Oro + 100 Oro"));
				break;
			case EmpyrealFinishBundle.Glitter:
				val.Add(OwnerHasRelic<Glitter>() ? ModLoc.T("+200 Gold", "+200 金币", fra: "+200 Or", deu: "+200 Gold", jpn: "+200 ゴールド", kor: "+200 골드", por: "+200 Ouro", rus: "+200 Злт", spa: "+200 Oro") : ModLoc.T("Glitter", "闪光", fra: "Scintillement", deu: "Glitzern", jpn: "きらめき", kor: "빛나리", por: "Brilho", rus: "Блеск", spa: "Brillo"));
				break;
			}

		return ModLoc.T("Receive: ", "获得：", fra: "Réception : ", deu: "Erhalten: ", jpn: "獲得：", kor: "획득: ", por: "Receber: ", rus: "Получить: ", spa: "Recibir: ") + string.Join(ModLoc.T(", ", "，", jpn: "、"), val) + ModLoc.T(".", "。", jpn: "。");
	}
}
