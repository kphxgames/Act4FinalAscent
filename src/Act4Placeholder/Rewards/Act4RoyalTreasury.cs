//=============================================================================
// Act4RoyalTreasury.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Act 4 Royal Treasury reward event offering a sequence of relic, gold, card, and potion choices with a final themed bonus bundle selection.
// ZH: 第四幕「皇家金库」奖励事件，按顺序提供圣物、金币、卡牌和药水选项，并以一个主题奖励包作为最终选择。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

public sealed class Act4RoyalTreasury : Act4RewardEventBase
{
	private const decimal FinalMaxHpBonusRatio = 0.4m; // EN: ratio tuning value, ZH: 比例参数

	private enum RoyalFinalBonus
	{
		PrismaticGem,
		TheCourier,
		BloodPotion
	}

	private sealed class RoyalExtraRelicChoice
	{
		public ModelId RelicId { get; init; }

		public string Name { get; init; } = string.Empty;

		public RoyalExtraRelicGrant Grant { get; init; }
	}

	private enum RoyalExtraRelicGrant
	{
		PrismaticGem,
		BeautifulBracelet,
		BeatingRemnant,
		BiiigHug,
		BlessedAntler,
		BrilliantScarf,
		ChoicesParadox,
		DiamondDiadem
	}

	private bool _hasShownFinalBonusChoice;

	private bool _weakestBonusEligibilityResolved;

	private bool _weakestBonusEligible;

	// NOTE: Intentionally disconnected - part of the extra relic draft flow that may be reconnected in a future update.
	private string? _selectedFinalBlessingTitle;

	// NOTE: Intentionally disconnected - part of the extra relic draft flow that may be reconnected in a future update.
	private string? _selectedFinalBlessingDescription;

	private List<RoyalFinalBonus>? _selectedFinalBonuses;

	private RoyalExtraRelicGrant? _selectedExtraRelicGrant;

	private string? _selectedExtraRelicName;

	protected override string EventPrefix => "ACT4_ROYAL_TREASURY";

	protected override int TotalStages => (ModSupport.IsBrutalAct4(((EventModel)this).Owner?.RunState as RunState) ? 1 : 3) + (Act4Settings.ExtraRewardsActiveForCurrentRun ? 1 : 0);

	protected override IReadOnlyList<RewardOfferDefinition> OfferPool { get; } = new RewardOfferDefinition[16]
	{
		Act4RewardEventBase.CreateGoldOffer("ACT4_DYNAMIC_GOLD_333", 333),
		Act4RewardEventBase.CreateBloodPotionOffer("ACT4_DYNAMIC_BLOOD_POTION"),
		Act4RewardEventBase.CreateRelicOffer<Mango>("ACT4_DYNAMIC_MANGO"),
		Act4RewardEventBase.CreateRelicOffer<BingBong>("ACT4_DYNAMIC_BING_BONG"),
		Act4RewardEventBase.CreateRelicOffer<WarPaint>("ACT4_DYNAMIC_WAR_PAINT"),
		Act4RewardEventBase.CreateRelicOffer<Whetstone>("ACT4_DYNAMIC_WHETSTONE"),
		Act4RewardEventBase.CreateRelicOffer<MeatCleaver>("ACT4_DYNAMIC_MEAT_CLEAVER"),
		Act4RewardEventBase.CreateRelicOffer<NutritiousOyster>("ACT4_DYNAMIC_NUTRITIOUS_OYSTER"),
		Act4RewardEventBase.CreateRelicOffer<Gorget>("ACT4_DYNAMIC_GORGET"),
		Act4RewardEventBase.CreateRelicOffer<GoldenPearl>("ACT4_DYNAMIC_GOLDEN_PEARL"),
		Act4RewardEventBase.CreateRelicOffer<LizardTail>("ACT4_DYNAMIC_LIZARD_TAIL"),
		Act4RewardEventBase.CreateRelicOffer<DistinguishedCape>("ACT4_DYNAMIC_DISTINGUISHED_CAPE"),
		Act4RewardEventBase.CreateRelicOffer<PrismaticGem>("ACT4_DYNAMIC_PRISMATIC_GEM"),
		Act4RewardEventBase.CreateRelicOffer<TheCourier>("ACT4_DYNAMIC_THE_COURIER"),
		Act4RewardEventBase.CreateRelicOffer<Candelabra>("ACT4_DYNAMIC_CANDELABRA"),
		Act4RewardEventBase.CreateRelicOffer<MusicBox>("ACT4_DYNAMIC_MUSIC_BOX")
	};

	protected override async Task FinishBonusAsync()
	{
		await GainFinalMaxHpBonusAsync();
		await GainFinalFullHealAsync();
	}

	protected override Task FinishStageAsync()
	{
		StageIndex++;
		if (StageIndex >= TotalStages)
		{
			if (!_hasShownFinalBonusChoice)
			{
				_hasShownFinalBonusChoice = true;
				SetEventState(Act4RewardEventBase.PlainText(ModLoc.T("The trader offers one final blessing before you depart.", "离开之前，商人又给出了最后一份祝福。", fra: "Le marchand offre une derniere benediction avant votre depart.", deu: "Der Handler bietet einen letzten Segen an, bevor Sie aufbrechen.", jpn: "商人は出発前に最後の祝福を提供します。", kor: "상인이 출발하기 전에 마지막 축복을 제공합니다.", por: "O comerciante oferece uma ultima bencao antes de voce partir.", rus: "Торговец предлагает последнее благословение перед вашим уходом.", spa: "El comerciante ofrece una ultima bendicion antes de que partas.")), new EventOption[1]
				{
					Act4RewardEventBase.CreateSimpleOption(this, FinishFinalDuplicateChoiceAsync, "ACT4_ROYAL_TREASURY_FINAL_BLESSING",
						ModLoc.T("Claim the final blessing", "领取最终祝福", fra: "Reclamer la derniere benediction", deu: "Die letzte Segnung annehmen", jpn: "最後の祝福を受け取る", kor: "마지막 축복을 받기", por: "Reivindicar a bencao final", rus: "Получить последнее благословение", spa: "Reclamar la bendicion final"),
						ModLoc.T("Gain +40% Max HP and fully heal.", "最大生命值 +40%，并完全恢复生命。", fra: "Gagnez +40% de PV max et recuperez tous vos PV.", deu: "Erhalte +40% Max. LP und heile vollstandig.", jpn: "最大HP +40%、完全回復。", kor: "최대 HP +40%, 완전 회복.", por: "Obtenha +40% de HP max. e cure completamente.", rus: "+40% к макс. ОЗ и полное лечение.", spa: "Gana +40% de HP max. y cura completamente."))
				});
			}
		}
		else
		{
			SetEventState(L10NLookup($"{EventPrefix}.pages.STAGE_{StageIndex + 1}.description"), BuildStageOptions());
		}
		return Task.CompletedTask;
	}

	private void ShowFinalSuppliesChoice()
	{
		SetEventState(Act4RewardEventBase.PlainText(ModLoc.T("The trader nods, then opens the final cache so you can see exactly what is being pressed into your hands.", "商人点了点头，随后打开最后的宝藏，让你亲眼确认即将交到手中的东西。", fra: "Le marchand acquiesce, puis ouvre le cache final pour que vous puissiez voir exactement ce qui vous est remis.", deu: "Der Handler nickt, dann offnet er den letzten Vorrat, damit Sie genau sehen konnen, was Ihnen überreicht wird.", jpn: "商人は頷き、最後の宝箱を開けて、あなたの手に渡るものを確認させます。", kor: "상인이 고개를 끄덕이고, 마지막 보관함을 열어 당신의 손에 쥐어질 것을 보여줍니다.", por: "O comerciante acena, depois abre o alijo final para que voce possa ver exatamente o que esta sendo colocado em suas maos.", rus: "Торговец кивает, затем открывает последний тайник, чтобы вы могли видеть exactly то, что вкладывается в ваши руки.", spa: "El comerciante asiente, luego abre el alijo final para que puedas ver exactamente lo que se coloca en tus manos.")), new EventOption[1] { Act4RewardEventBase.CreateSimpleOption(this, FinishFinalDuplicateChoiceAsync, "ACT4_ROYAL_TREASURY_FINAL_TAKE", ModLoc.T("Take the royal cache", "领取皇家宝藏", fra: "Prendre le butin royal", deu: "Den koniglichen Vorrat nehmen", jpn: "王室の宝物を受け取る", kor: "왕실 보물 가져가기", por: "Pegar o alijo real", rus: "Взять королевский тайник", spa: "Tomar el alijo real"), ModLoc.T("Receive: +40% Max HP, Full Heal.", "获得：最大生命值 +40%，完全恢复生命。", fra: "Recu: +40% PV max, Soin complet.", deu: "Erhalten: +40% Max. LP, Volles Heilen.", jpn: "獲得：最大HP +40%、完全回復。", kor: "획득: 최대 HP +40%, 완전 회복.", por: "Receber: +40% HP max., Cura Completa.", rus: "Получено: +40% макс. ОЗ, полное лечение.", spa: "Recibir: +40% HP max., Curacion Completa.")) });
	}

	private async Task FinishFinalDuplicateChoiceAsync()
	{
		await FinishBonusAsync();
		await TryHandleWeakestBonusOrFinishAsync();
	}

	private async Task TryHandleWeakestBonusOrFinishAsync()
	{
		// EN: No extra screen for the weakest player, the buff is recorded silently in Act4EmpyrealCache
		//     and applied deterministically at Architect boss fight start on both machines.
		// ZH: 最弱玩家无额外界面——增益已在Act4EmpyrealCache中静默记录，
		//     并将在双方机器上于建筑师Boss战开始时同步生效。
		SetEventFinished(L10NLookup(EventPrefix + ".pages.FINISH.description"));
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

	private async Task GainFinalMaxHpBonusAsync()
	{
		Player owner = ((EventModel)this).Owner;
		if (owner?.Creature != null)
		{
			await CreatureCmd.GainMaxHp(owner.Creature, GetRoundedFinalMaxHpBonus(owner.Creature.MaxHp));
		}
	}

	private async Task GainFinalFullHealAsync()
	{
		Player owner = ((EventModel)this).Owner;
		Creature creature = owner?.Creature;
		if (creature == null)
		{
			return;
		}
		decimal missingHp = creature.MaxHp - creature.CurrentHp;
		if (missingHp > 0m)
		{
			await CreatureCmd.Heal(creature, missingHp, false);
		}
	}

	private static decimal GetRoundedFinalMaxHpBonus(decimal maxHp)
	{
		return Math.Max(1m, Math.Ceiling(maxHp * FinalMaxHpBonusRatio));
	}

	// NOTE: Intentionally disconnected - part of the extra relic draft flow that may be reconnected in a future update.
	private List<RoyalFinalBonus> GetSelectedFinalBonuses()
	{
		if (_selectedFinalBonuses != null)
		{
			return _selectedFinalBonuses;
		}
		List<RoyalFinalBonus> obj = new List<RoyalFinalBonus>();
		obj.Add(RoyalFinalBonus.TheCourier);
		obj.Add(RoyalFinalBonus.BloodPotion);
		List<RoyalFinalBonus> val = obj;
		Rng val2 = GetStableRunRng("royal_final_bonuses");
		for (int num = val.Count - 1; num > 0; num--)
		{
			int num2 = val2.NextInt(num + 1);
			List<RoyalFinalBonus> val3 = val;
			int num3 = num;
			int num4 = num2;
			RoyalFinalBonus royalFinalBonus = val[num2];
			RoyalFinalBonus royalFinalBonus2 = val[num];
			val3[num3] = royalFinalBonus;
			val[num4] = royalFinalBonus2;
		}
		Player owner2 = ((EventModel)this).Owner;
		int? obj3;
		if (owner2 == null)
		{
			obj3 = null;
		}
		else
		{
			IRunState runState2 = owner2.RunState;
			obj3 = ((runState2 != null) ? new int?(((IReadOnlyCollection<Player>)((IPlayerCollection)runState2).Players).Count) : ((int?)null));
		}
		RunState val4 = ((EventModel)this).Owner?.RunState as RunState;
		int num5 = (ModSupport.IsBrutalAct4(val4) ? 1 : (((obj3 ?? 1) > 1) ? 1 : 2));
		num5 = Math.Clamp(num5, 1, 2);
		_selectedFinalBonuses = val.Take(num5).ToList();
		return _selectedFinalBonuses;
	}

	// NOTE: Intentionally disconnected - ShowFinalRelicDraftChoice is never called; part of the extra relic draft flow that may be reconnected in a future update.
	private void ShowFinalRelicDraftChoice()
	{
		List<RoyalExtraRelicChoice> finalRelicDraftChoices = GetFinalRelicDraftChoices();
		if (finalRelicDraftChoices.Count == 0)
		{
			_selectedExtraRelicGrant = null;
			_selectedExtraRelicName = null;
			ShowFinalSuppliesChoice();
			return;
		}
		List<EventOption> list = new List<EventOption>();
		foreach (RoyalExtraRelicChoice choice in finalRelicDraftChoices)
		{
			switch (choice.Grant)
			{
			case RoyalExtraRelicGrant.PrismaticGem:
				list.Add(CreateFinalRelicDraftOption<PrismaticGem>(choice));
				break;
			case RoyalExtraRelicGrant.BeautifulBracelet:
				list.Add(CreateFinalRelicDraftOption<BeautifulBracelet>(choice));
				break;
			case RoyalExtraRelicGrant.BeatingRemnant:
				list.Add(CreateFinalRelicDraftOption<BeatingRemnant>(choice));
				break;
			case RoyalExtraRelicGrant.BiiigHug:
				list.Add(CreateFinalRelicDraftOption<BiiigHug>(choice));
				break;
			case RoyalExtraRelicGrant.BlessedAntler:
				list.Add(CreateFinalRelicDraftOption<BlessedAntler>(choice));
				break;
			case RoyalExtraRelicGrant.BrilliantScarf:
				list.Add(CreateFinalRelicDraftOption<BrilliantScarf>(choice));
				break;
			case RoyalExtraRelicGrant.ChoicesParadox:
				list.Add(CreateFinalRelicDraftOption<ChoicesParadox>(choice));
				break;
			case RoyalExtraRelicGrant.DiamondDiadem:
				list.Add(CreateFinalRelicDraftOption<DiamondDiadem>(choice));
				break;
			}
		}
		SetEventState(Act4RewardEventBase.PlainText(ModLoc.T("Choose one additional relic to accompany your final cache.", "再选择一件遗物，与最终宝藏一同带走。", fra: "Choisissez une relique supplementaire pour accompagner votre cache final.", deu: "Wahle eine zusatzliche Reliquie fur deinen finalen Vorrat.", jpn: "最終宝箱と共に追加の遺物を1つ選んでください。", kor: "마지막 보물에 함께할 추가 유물을 하나 선택하세요.", por: "Escolha uma reliquia adicional para acompanhar seu alijo final.", rus: "Выберите одну дополнительную реликвию для последнего тайника.", spa: "Elige una reliquia adicional para acompanar tu alijo final.")), list);
	}

	private EventOption CreateFinalRelicDraftOption<T>(RoyalExtraRelicChoice choice) where T : RelicModel
	{
		RelicModel relicModel = ((RelicModel)ModelDb.Relic<T>()).ToMutable();
		if (((EventModel)this).Owner != null)
		{
			relicModel.Owner = ((EventModel)this).Owner;
		}
		string key = $"ACT4_ROYAL_TREASURY_EXTRA_{choice.Grant}";
		return EventOption.FromRelic(relicModel, this, async delegate
		{
			_selectedExtraRelicGrant = choice.Grant;
			_selectedExtraRelicName = choice.Name;
			ShowFinalSuppliesChoice();
			await Task.CompletedTask;
		}, key);
	}

	// NOTE: Intentionally disconnected - GrantSelectedExtraRelicAsync is never called; part of the extra relic draft flow that may be reconnected in a future update.
	private async Task GrantSelectedExtraRelicAsync()
	{
		if (_selectedExtraRelicGrant == null)
		{
			return;
		}
		RoyalExtraRelicGrant value = _selectedExtraRelicGrant.Value;
		_selectedExtraRelicGrant = null;
		switch (value)
		{
		case RoyalExtraRelicGrant.PrismaticGem:
			await ObtainUniqueRelicAsync<PrismaticGem>();
			break;
		case RoyalExtraRelicGrant.BeautifulBracelet:
			await ObtainUniqueRelicAsync<BeautifulBracelet>();
			break;
		case RoyalExtraRelicGrant.BeatingRemnant:
			await ObtainUniqueRelicAsync<BeatingRemnant>();
			break;
		case RoyalExtraRelicGrant.BiiigHug:
			await ObtainUniqueRelicAsync<BiiigHug>();
			break;
		case RoyalExtraRelicGrant.BlessedAntler:
			await ObtainUniqueRelicAsync<BlessedAntler>();
			break;
		case RoyalExtraRelicGrant.BrilliantScarf:
			await ObtainUniqueRelicAsync<BrilliantScarf>();
			break;
		case RoyalExtraRelicGrant.ChoicesParadox:
			await ObtainUniqueRelicAsync<ChoicesParadox>();
			break;
		case RoyalExtraRelicGrant.DiamondDiadem:
			await ObtainUniqueRelicAsync<DiamondDiadem>();
			break;
		}
	}

	private List<RoyalExtraRelicChoice> GetFinalRelicDraftChoices()
	{
		List<RoyalExtraRelicChoice> list = new List<RoyalExtraRelicChoice>
		{
			new RoyalExtraRelicChoice
			{
				RelicId = (ModelDb.Relic<PrismaticGem>()).Id,
				Name = ModLoc.T("Prismatic Gem", "棱彩宝石", fra: "Gemme Prismatique", deu: "Prismatisches Juwel", jpn: "プリズムの宝石", kor: "프리즘 보석", por: "Gema Prismática", rus: "Призматический Камень", spa: "Gema Prismática"),
				Grant = RoyalExtraRelicGrant.PrismaticGem
			},
			new RoyalExtraRelicChoice
			{
				RelicId = (ModelDb.Relic<BeautifulBracelet>()).Id,
				Name = ModLoc.T("Beautiful Bracelet", "华美手镐", fra: "Beau Bracelet", deu: "Wunderschönes Armband", jpn: "美しいブレスレット", kor: "아름다운 팔찌", por: "Bracelete Belo", rus: "Прекрасный Браслет", spa: "Brazalete Hermoso"),
				Grant = RoyalExtraRelicGrant.BeautifulBracelet
			},
			new RoyalExtraRelicChoice
			{
				RelicId = (ModelDb.Relic<BeatingRemnant>()).Id,
				Name = ModLoc.T("Beating Remnant", "跃动残片", fra: "Vestige Battant", deu: "Schlagendes Überbleibsel", jpn: "鼓動する残片", kor: "두근거리는 잔해", por: "Vestígio Palpitante", rus: "Пульсирующий Остаток", spa: "Vestigio Latiente"),
				Grant = RoyalExtraRelicGrant.BeatingRemnant
			},
			new RoyalExtraRelicChoice
			{
				RelicId = (ModelDb.Relic<BiiigHug>()).Id,
				Name = ModLoc.T("Biiig Hug", "超大拥抱", fra: "Tres Grand Calin", deu: "Rieeeesen Umarmung", jpn: "超でかいハグ", kor: "엄청 큰 포옹", por: "Abraco Muito Grande", rus: "Огромное Объятие", spa: "Abrazo Grandisimo"),
				Grant = RoyalExtraRelicGrant.BiiigHug
			},
			new RoyalExtraRelicChoice
			{
				RelicId = (ModelDb.Relic<BlessedAntler>()).Id,
				Name = ModLoc.T("Blessed Antler", "赐福鹿角", fra: "Bois de Cerf Beni", deu: "Gesegnetes Geweih", jpn: "祝福された角", kor: "축복받은 뿔", por: "Chifre Abencado", rus: "Благословенный Рог", spa: "Asta Bendecida"),
				Grant = RoyalExtraRelicGrant.BlessedAntler
			},
			new RoyalExtraRelicChoice
			{
				RelicId = (ModelDb.Relic<BrilliantScarf>()).Id,
				Name = ModLoc.T("Brilliant Scarf", "璊硒围巾", fra: "Echarpe Brillante", deu: "Glanzender Schal", jpn: "輝くスカーフ", kor: "화려한 스카프", por: "Cachecol Brilhante", rus: "Блестящий Шарф", spa: "Bufanda Brillante"),
				Grant = RoyalExtraRelicGrant.BrilliantScarf
			},
			new RoyalExtraRelicChoice
			{
				RelicId = (ModelDb.Relic<ChoicesParadox>()).Id,
				Name = ModLoc.T("Choices Paradox", "抉择悲论", fra: "Paradoxe des Choix", deu: "Entscheidungsparadoxon", jpn: "選択の逆説", kor: "선택의 역설", por: "Paradoxo das Escolhas", rus: "Парадокс Выбора", spa: "Paradoja de las Elecciones"),
				Grant = RoyalExtraRelicGrant.ChoicesParadox
			},
			new RoyalExtraRelicChoice
			{
				RelicId = (ModelDb.Relic<DiamondDiadem>()).Id,
				Name = ModLoc.T("Diamond Diadem", "钒石凕冠", fra: "Diademe de Diamant", deu: "Diamant-Diadem", jpn: "ダイヤのティアラ", kor: "다이아몬드 왕관", por: "Diadema de Diamante", rus: "Алмазная Диадема", spa: "Diadema de Diamante"),
				Grant = RoyalExtraRelicGrant.DiamondDiadem
			}
		};
		list = list.Where((RoyalExtraRelicChoice c) => !OwnerHasRelic(c.RelicId)).ToList();
		Rng finalRelicRng = GetFinalRelicRng();
		for (int num = list.Count - 1; num > 0; num--)
		{
			int num2 = finalRelicRng.NextInt(num + 1);
			RoyalExtraRelicChoice value = list[num2];
			list[num2] = list[num];
			list[num] = value;
		}
		return list.Take(3).ToList();
	}

	private bool OwnerHasRelic(ModelId relicId)
	{
		return ((EventModel)this).Owner != null && ((IEnumerable<RelicModel>)((EventModel)this).Owner.Relics).Any((RelicModel relic) => ((AbstractModel)relic).Id == relicId);
	}

	private Rng GetFinalRelicRng()
	{
		RunState runState = ((EventModel)this).Owner?.RunState as RunState;
		uint num = runState?.Rng?.Seed ?? 0u;
		ulong ownerNetId = ((EventModel)this).Owner?.NetId ?? 0uL;
		return new Rng(StableHash32($"royal_final_relic:{num}:{ownerNetId}"));
	}
}
