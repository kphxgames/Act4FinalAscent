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
		if (ShouldOfferWeakestBonus())
		{
			ModSupport.GrantAct4WeakestBuff(((EventModel)this).Owner);
			// EN: Show an extra event screen only for the weakest player. Because non-shared events
			//     run per-player on BOTH machines, this SetEventState and the subsequent option click
			//     are processed identically on host and client, no desync. Map navigation already
			//     waits for all players, so the extra click causes no stalling.
			// ZH: 仅为输出最弱的玩家显示额外事件界面。由于非共享事件在双方机器上逐玩家运行，
			//     此SetEventState及后续选项点击在主机和客户端上处理结果相同——不会引发同步问题。
			//     地图导航本来就等待所有玩家，额外的点击不会造成卡顿。
			string buffKey = "ACT4_EMPYREAL_CACHE_WEAKEST_BUFF_NOTIFY";
			string buffTitle = ModLoc.T("Understood", "\u660e\u767d\u4e86", fra: "Compris", deu: "Verstanden", jpn: "\u4e86\u89e3\u3057\u307e\u3057\u305f", kor: "\uc774\ud574\ud588\uc2b5\ub2c8\ub2e4", por: "Entendido", rus: "\u041f\u043e\u043d\u044f\u0442\u043d\u043e", spa: "Entendido");
			string buffDesc = ModLoc.T("+10 Strength and +5 Dexterity will be applied at the start of every Act 4 combat.", "+10\u529b\u91cf\u548c+5\u654f\u6377\u5c06\u5728\u6bcf\u573a\u7b2c\u56db\u5e55\u6218\u6597\u5f00\u59cb\u65f6\u751f\u6548\u3002", fra: "+10 Force et +5 Dexterite seront appliques au debut de chaque combat de l'Acte 4.", deu: "+10 Stärke und +5 Geschicklichkeit werden zu Beginn jedes Kampfes in Akt 4 angewendet.", jpn: "\u7b2c4\u5e55\u306e\u5404\u6226\u95d8\u958b\u59cb\u6642\u306b+10\u529b\u3068+5\u654f\u6377\u6027\u304c\u9069\u7528\u3055\u308c\u307e\u3059\u3002", kor: "4\ub9c9\uc758 \uac01 \uc804\ud22c \uc2dc\uc791 \uc2dc +10 \ud798\uacfc +5 \ubbfc\uccab\uc131\uc774 \uc801\uc6a9\ub429\ub2c8\ub2e4.", por: "+10 Forca e +5 Destreza serao aplicados no inicio de cada combate do Ato 4.", rus: "+10 \u0421\u0438\u043b\u0430 \u0438 +5 \u041b\u043e\u0432\u043a\u043e\u0441\u0442\u044c \u0431\u0443\u0434\u0443\u0442 \u043f\u0440\u0438\u043c\u0435\u043d\u044f\u0442\u044c\u0441\u044f \u0432 \u043d\u0430\u0447\u0430\u043b\u0435 \u043a\u0430\u0436\u0434\u043e\u0433\u043e \u0431\u043e\u044f \u0410\u043a\u0442\u0430 4.", spa: "+10 Fuerza y +5 Destreza se aplicaran al inicio de cada combate del Acto 4.");
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
				"Your allies outpaced you in the struggle. The stranger notes the gap and presses something extra into your hands. \"You dealt the least damage of your party,\" they say plainly. \"Take this, it should even the odds.\" You will enter every Act 4 battle with +10 Strength and +5 Dexterity.",
				"\u4f60\u7684\u961f\u53cb\u5728\u6218\u6597\u4e2d\u8d85\u8fc7\u4e86\u4f60\u3002\u8bb2\u8005\u6ce8\u610f\u5230\u5dee\u8ddd\uff0c\u5c06\u989d\u5916\u7684\u4e1c\u897f\u4ea4\u5230\u4f60\u624b\u4e2d\u3002\u201c\u4f60\u5728\u961f\u4f0d\u4e2d\u9020\u6210\u7684\u4f24\u5bb3\u6700\u5c11\uff0c\u201d\u4ed6\u76f4\u63a5\u8bf4\u9053\uff0c\u201c\u63a5\u53d7\u8fd9\u4e2a\u2014\u2014\u5e94\u80fd\u8865\u5e73\u5dee\u8ddd\u3002\u201d\u4f60\u5c06\u5728\u6bcf\u573a\u7b2c\u56db\u5e55\u6218\u6597\u4e2d\u83b7\u5f97+10\u529b\u91cf\u548c+5\u654f\u6377\u3002",
				fra: "Vos allies vous ont depasse dans la lutte. L'etranger remarque l'ecart et vous glisse quelque chose. \"Vous avez inflige le moins de degats de votre groupe\", dit-il franchement. \"Prenez ceci, cela devrait egaler les chances.\" Vous entrerez chaque combat de l'Acte 4 avec +10 Force et +5 Dexterite.",
				deu: "Ihre Verbündeten übertrafen Sie im Kampf. Der Fremde bemerkt den Unterschied und drückt Ihnen etwas in die Hand. \"Sie haben am wenigsten Schaden in Ihrer Gruppe verursacht\", sagt er offen. \"Nehmen Sie dies, es sollte die Chancen ausgleichen.\" Sie werden jeden Kampf in Akt 4 mit +10 Stärke und +5 Geschicklichkeit beginnen.",
				jpn: "\u304a\u4f9b\u306e\u4ef2\u9593\u304c\u6226\u3044\u3067\u3042\u306a\u305f\u3092\u4e0a\u56de\u308a\u307e\u3057\u305f\u3002\u8b1b\u8a93\u4eba\u306f\u305d\u306e\u5dee\u3092\u898b\u3066\u3001\u624b\u306b\u4f59\u5206\u306a\u3082\u306e\u3092\u6e21\u3057\u307e\u3059\u3002\u300c\u3042\u306a\u305f\u306f\u30d1\u30fc\u30c6\u30a3\u3067\u6700\u3082\u5c11\u306a\u3044\u30c0\u30e1\u30fc\u30b8\u3092\u4e0e\u3048\u307e\u3057\u305f\u300d\u3068\u5f7c\u306f\u7387\u76f4\u306b\u8a00\u3044\u307e\u3059\u3002\u300c\u3053\u308c\u3092\u53d7\u3051\u53d6\u3063\u3066\u304f\u3060\u3055\u3044\u2014\u2014\u5747\u8861\u304c\u53d6\u308c\u308b\u306f\u305a\u3067\u3059\u3002\u300d\u7b2c4\u5e55\u306e\u5404\u6226\u95d8\u306b+10\u529b\u3068+5\u654f\u6377\u6027\u3067\u81e8\u307f\u307e\u3059\u3002",
				kor: "\ub2f9\uc2e0\uc758 \ub3d9\ub8cc\ub4e4\uc774 \uc804\ud22c\uc5d0\uc11c \uc55e\uc11c \ub098\uac14\uc2b5\ub2c8\ub2e4. \ub099\uc120\uc778\uc740 \uaca9\ucc28\ub97c \uc54c\uc544\ubcf4\uace0 \uc190\uc5d0 \ubb36\uac00\ub97c \ub354 \uc950\uc5b4\uc90d\ub2c8\ub2e4. \"\ub2f9\uc2e0\uc774 \ud30c\ud2f0\uc5d0\uc11c \uac00\uc7a5 \uc801\uc740 \ud53c\ud574\ub97c \uc785\ud600\ub2e4\uad70\uc694\"\ub77c\uace0 \uadf8\uac00 \uc194\uc9c1\ud558\uac8c \ub9d0\ud569\ub2c8\ub2e4. \"\uc774\uac83\uc744 \ubc1b\uc73c\uc138\uc694 \u2014 \uade0\ud615\uc744 \ub9de\ucdb0\ub4dc\ub9b4 \uac81\ub2c8\ub2e4.\" 4\ub9c9\uc758 \ubaa8\ub4e0 \uc804\ud22c\ub97c +10 \ud798\uacfc +5 \ubbfc\uccab\uc131\uc73c\ub85c \uc2dc\uc791\ud569\ub2c8\ub2e4.",
				por: "Seus aliados superaram voce na luta. O estranho nota a diferenca e pressiona algo extra em suas maos. \"Voce causou o menor dano do grupo\", ele diz diretamente. \"Pegue isto, deve equilibrar as chances.\" Voce entrara em cada combate do Ato 4 com +10 Forca e +5 Destreza.",
				rus: "\u0412\u0430\u0448\u0438 \u0441\u043e\u044e\u0437\u043d\u0438\u043a\u0438 \u043f\u0440\u0435\u0432\u0437\u043e\u0448\u043b\u0438 \u0432\u0430\u0441 \u0432 \u0431\u043e\u044e. \u041d\u0435\u0437\u043d\u0430\u043a\u043e\u043c\u0435\u0446 \u0437\u0430\u043c\u0435\u0447\u0430\u0435\u0442 \u0440\u0430\u0437\u0440\u044b\u0432 \u0438 \u0432\u043a\u043b\u0430\u0434\u044b\u0432\u0430\u0435\u0442 \u0447\u0442\u043e-\u0442\u043e \u0434\u043e\u043f\u043e\u043b\u043d\u0438\u0442\u0435\u043b\u044c\u043d\u043e\u0435 \u0432 \u0432\u0430\u0448\u0438 \u0440\u0443\u043a\u0438. \u00ab\u0412\u044b \u043d\u0430\u043d\u0435\u0441\u043b\u0438 \u043d\u0430\u0438\u043c\u0435\u043d\u044c\u0448\u0438\u0439 \u0443\u0449\u0435\u0440\u0431 \u0432 \u0433\u0440\u0443\u043f\u043f\u0435\u00bb, \u2014 \u043f\u0440\u044f\u043c\u043e \u0433\u043e\u0432\u043e\u0440\u0438\u0442 \u043e\u043d. \u00ab\u0412\u043e\u0437\u044c\u043c\u0438\u0442\u0435 \u044d\u0442\u043e \u2014 \u0434\u043e\u043b\u0436\u043d\u043e \u0432\u044b\u0440\u043e\u0432\u043d\u044f\u0442\u044c \u0448\u0430\u043d\u0441\u044b.\u00bb \u0412\u044b \u0431\u0443\u0434\u0435\u0442\u0435 \u0432\u0445\u043e\u0434\u0438\u0442\u044c \u0432 \u043a\u0430\u0436\u0434\u044b\u0439 \u0431\u043e\u0439 \u0410\u043a\u0442\u0430 4 \u0441 +10 \u0421\u0438\u043b\u043e\u0439 \u0438 +5 \u041b\u043e\u0432\u043a\u043e\u0441\u0442\u044c\u044e.",
				spa: "Sus aliados lo superaron en la batalla. El extrano nota la brecha y le presiona algo en las manos. \"Usted infligio el menor dano de su grupo\", dice llanamente. \"Tome esto, deberia equilibrar las probabilidades.\" Entrara en cada combate del Acto 4 con +10 Fuerza y +5 Destreza."
			)), buffOptions);
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
