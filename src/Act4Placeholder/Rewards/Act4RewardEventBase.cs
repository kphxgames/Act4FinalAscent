//=============================================================================
// Act4RewardEventBase.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Abstract base class shared by Act4EmpyrealCache and Act4RoyalTreasury; provides async helpers for building reward offer lists and procuring potions, gold, relics, and cards.
// ZH: 帝国宝库和皇家金库共用的抽象基类，提供异步辅助方法用于构建奖励选项列表，以及获取药水、金币、圣物和卡牌。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

public abstract class Act4RewardEventBase : EventModel
{
			protected sealed class RewardOfferDefinition
		{
			public string Key { get; init; }

			public Func<Act4RewardEventBase, bool> CanOffer { get; init; }

			public Func<Act4RewardEventBase, Func<Task>, EventOption> CreateOption { get; init; }

			// Optional: auto-grant this offer's item without showing a button (used for the extra-rewards bonus).
			// Null means this offer type can't be auto-granted (e.g., interactive card-remove).
			public Func<Act4RewardEventBase, Task>? AutoGrant { get; init; }
		}

	private static readonly Dictionary<string, HashSet<string>> OfferedRewardKeysByRun = new Dictionary<string, HashSet<string>>();

	private int _stageIndex; // EN: index used by flow, ZH: 流程使用的索引

	private Dictionary<int, IReadOnlyList<RewardOfferDefinition>> _rolledOffersByStage = new Dictionary<int, IReadOnlyList<RewardOfferDefinition>>();

	// Ensure each ToMutable() clone gets its own offer cache so co-op players
	// don't share the first player's rolled offers (MemberwiseClone is shallow).
	protected override void DeepCloneFields()
	{
		base.DeepCloneFields();
		_rolledOffersByStage = new Dictionary<int, IReadOnlyList<RewardOfferDefinition>>();
	}

	private static readonly IReadOnlyList<RewardOfferDefinition> FallbackOfferPool = new RewardOfferDefinition[3]
	{
		CreateGoldOffer("ACT4_DYNAMIC_GOLD_777", 777),
		CreateGoldOffer("ACT4_DYNAMIC_GOLD_300", 300),
		CreateFairyOffer("ACT4_DYNAMIC_FAIRY")
	};

	public override bool IsShared => false;

	protected int StageIndex
	{
		get
		{
			return _stageIndex;
		}
		set
		{
			((AbstractModel)this).AssertMutable();
			_stageIndex = value;
		}
	}

	protected abstract string EventPrefix { get; }

	protected abstract IReadOnlyList<RewardOfferDefinition> OfferPool { get; }

	protected virtual int TotalStages
	{
		get
		{
			Player owner = ((EventModel)this).Owner;
			int? obj;
			if (owner == null)
			{
				obj = null;
			}
			else
			{
				IRunState runState = owner.RunState;
				obj = ((runState != null) ? new int?(((IReadOnlyCollection<Player>)((IPlayerCollection)runState).Players).Count) : ((int?)null));
			}
			return ((obj ?? 1) > 1) ? 3 : 4;
		}
	}

	protected override IReadOnlyList<EventOption> GenerateInitialOptions()
	{
		StageIndex = 0;
		return BuildStageOptions();
	}

	protected async Task DuplicateChosenCardAsync()
	{
		if (((EventModel)this).Owner == null) return;
		CardSelectorPrefs prefs = new CardSelectorPrefs(PlainText("Choose a card to Duplicate"), 1);
		IEnumerable<CardModel> cards = await CardSelectCmd.FromDeckGeneric(((EventModel)this).Owner, prefs, (c => (int)c.Type != 6), (Func<CardModel, int>)null);
		CardModel? card = cards.FirstOrDefault();
		if (card == null) return;
		CardModel copy = ((ICardScope)((EventModel)this).Owner.RunState).CloneCard(card);
		CardPileAddResult result = await CardPileCmd.Add(copy, (PileType)6, (CardPilePosition)1, this, false);
		CardCmd.PreviewCardPileAdd(result, 1.2f, (CardPreviewStyle)1);
	}

	protected async Task GainDuplicateCardOrGoldAsync()
	{
		if (((EventModel)this).Owner == null || !PileTypeExtensions.GetPile((PileType)6, ((EventModel)this).Owner).Cards.Any(c => (int)c.Type != 6))
			await GainGoldAsync(300m);
		else
			await DuplicateChosenCardAsync();
	}

	protected async Task GainFairyPotionAsync()
	{
		if (((EventModel)this).Owner == null) return;
		PotionProcureResult result = await PotionCmd.TryToProcure<FairyInABottle>(((EventModel)this).Owner);
		if (!result.success)
			await PlayerCmd.GainGold(90m, ((EventModel)this).Owner, false);
	}

	protected async Task GainBloodPotionAsync()
	{
		if (((EventModel)this).Owner == null) return;
		PotionProcureResult result = await PotionCmd.TryToProcure<BloodPotion>(((EventModel)this).Owner);
		if (!result.success)
			await PlayerCmd.GainGold(90m, ((EventModel)this).Owner, false);
	}

	protected async Task GainMaxHpAsync(decimal amount)
	{
		if (((EventModel)this).Owner != null)
			await CreatureCmd.GainMaxHp(((EventModel)this).Owner.Creature, amount);
	}

	protected async Task GainGoldAsync(decimal amount)
	{
		if (((EventModel)this).Owner != null)
			await PlayerCmd.GainGold(amount, ((EventModel)this).Owner, false);
	}

	protected async Task ObtainUniqueRelicAsync<T>() where T : RelicModel
	{
		await ObtainUniqueRelicOrGoldAsync<T>(300m);
	}

	protected async Task ObtainUniqueRelicOrGoldAsync<T>(decimal fallbackGold) where T : RelicModel
	{
		if (((EventModel)this).Owner == null) return;
		ModelId relicId = (ModelDb.Relic<T>()).Id;
		if (((EventModel)this).Owner.Relics.Any(relic => relic.Id == relicId))
			await PlayerCmd.GainGold(fallbackGold, ((EventModel)this).Owner, false);
		else
			await RelicCmd.Obtain<T>(((EventModel)this).Owner);
	}

	protected virtual Task FinishBonusAsync()
	{
		return Task.CompletedTask;
	}

	protected virtual async Task FinishStageAsync()
	{
		StageIndex++;
		if (StageIndex >= TotalStages)
		{
			await FinishBonusAsync();
			SetEventFinished(L10NLookup(EventPrefix + ".pages.FINISH.description"));
		}
		else
			SetEventState(L10NLookup($"{EventPrefix}.pages.STAGE_{StageIndex + 1}.description"), BuildStageOptions());
	}

	protected bool OwnerHasRelic<T>() where T : RelicModel
	{
		return ((EventModel)this).Owner != null && ((EventModel)this).Owner.Relics.Any(relic => relic.Id == (ModelDb.Relic<T>()).Id);
	}

	protected IReadOnlyList<EventOption> BuildStageOptions()
	{
		IReadOnlyList<RewardOfferDefinition> offers = GetOrRollOffersForStage(StageIndex);
		EventOption[] options = new EventOption[Math.Min(2, offers.Count)];
		for (int i = 0; i < options.Length; i++)
			options[i] = CreateRolledOption(offers[i]);
		return options;
	}

	protected void RefreshCurrentStageOptions()
	{
		SetEventState(L10NLookup($"{EventPrefix}.pages.STAGE_{StageIndex + 1}.description"), BuildStageOptions());
	}

	private EventOption CreateRolledOption(RewardOfferDefinition offer)
	{
		return offer.CreateOption.Invoke(this, FinishStageAsync);
	}

	private IReadOnlyList<RewardOfferDefinition> GetOrRollOffersForStage(int stageIndex)
	{
		IReadOnlyList<RewardOfferDefinition> result = default(IReadOnlyList<RewardOfferDefinition>);
		if (_rolledOffersByStage.TryGetValue(stageIndex, out result))
		{
			return result;
		}
		List<RewardOfferDefinition> val = RollOfferDefinitions(stageIndex);
		_rolledOffersByStage[stageIndex] = (IReadOnlyList<RewardOfferDefinition>)val;
		return (IReadOnlyList<RewardOfferDefinition>)val;
	}

	private List<RewardOfferDefinition> RollOfferDefinitions(int stageIndex)
	{
		int targetCount = 2;
		HashSet<string> offeredKeys = GetOfferedRewardKeysForRun();
		List<RewardOfferDefinition> val = OfferPool.Where(offer => offer.CanOffer.Invoke(this) && !offeredKeys.Contains(offer.Key)).ToList();
		if (val.Count < 2)
		{
			val = OfferPool.Where(offer => offer.CanOffer.Invoke(this)).ToList();
		}
		if (val.Count < 2)
		{
			val = FallbackOfferPool.Where(offer => offer.CanOffer.Invoke(this)).ToList();
		}
		List<RewardOfferDefinition> val2 = new List<RewardOfferDefinition>(targetCount);
		Rng stageRng = GetStageOfferRng(stageIndex);
		while (val2.Count < targetCount && val.Count > 0)
		{
			int num = stageRng.NextInt(val.Count);
			RewardOfferDefinition rewardOfferDefinition = val[num];
			if (val2.Count > 0 && IsGoldLikeOffer(rewardOfferDefinition) && val2.Any((RewardOfferDefinition offer) => IsGoldLikeOffer(offer)))
			{
				val.RemoveAt(num);
				continue;
			}
			val.RemoveAt(num);
			val2.Add(rewardOfferDefinition);
			offeredKeys.Add(rewardOfferDefinition.Key);
		}
		while (val2.Count < targetCount)
		{
			val2.Add(CreateGoldOffer($"ACT4_DYNAMIC_GOLD_FILL_{EventPrefix}_{stageIndex}_{val2.Count}", 333));
		}
		return val2;
	}

	private static bool IsGoldLikeOffer(RewardOfferDefinition offer)
	{
		if (offer == null || string.IsNullOrEmpty(offer.Key))
		{
			return false;
		}
		return offer.Key.Contains("_GOLD_", StringComparison.OrdinalIgnoreCase) || offer.Key.Contains("OLD_COIN", StringComparison.OrdinalIgnoreCase) || offer.Key.Contains("SEAL_OF_GOLD", StringComparison.OrdinalIgnoreCase);
	}

	private HashSet<string> GetOfferedRewardKeysForRun()
	{
		string runKey = GetRunKey();
		HashSet<string> val = default(HashSet<string>);
		if (!OfferedRewardKeysByRun.TryGetValue(runKey, out val))
		{
			val = new HashSet<string>();
			OfferedRewardKeysByRun[runKey] = val;
			// Trim to keep only the 10 most recent run keys so the static dict doesn't grow unbounded.
			if (OfferedRewardKeysByRun.Count > 10)
			{
				string oldest = System.Linq.Enumerable.First(OfferedRewardKeysByRun.Keys);
				OfferedRewardKeysByRun.Remove(oldest);
			}
		}
		return val;
	}

	private string GetRunKey()
	{
		uint runSeed = ((EventModel)this).Owner?.RunState?.Rng?.Seed ?? 0u;
		ulong ownerNetId = ((EventModel)this).Owner?.NetId ?? 0uL;
		// Include owner NetId + event prefix so per-player non-shared events are
		// stable across host/client even if local player list ordering differs.
		return $"{runSeed}:{ownerNetId}:{EventPrefix}";
	}

	protected Rng GetStableRunRng(string streamName)
	{
		return new Rng(GetDeterministicFallbackSeed(streamName));
	}

	protected int GetOwnerPlayerSlotIndex()
	{
		Player owner = ((EventModel)this).Owner;
		if (owner == null)
		{
			return 0;
		}

		IPlayerCollection players = owner.RunState as IPlayerCollection;
		if (players == null)
		{
			return 0;
		}

		List<Player> allPlayers = players.Players.ToList();
		int indexByNetId = allPlayers.FindIndex((Player p) => p != null && p.NetId == owner.NetId);
		if (indexByNetId >= 0)
		{
			return indexByNetId;
		}

		int indexByReference = allPlayers.FindIndex((Player p) => p == owner);
		return (indexByReference >= 0) ? indexByReference : 0;
	}

	private Rng GetStageOfferRng(int stageIndex)
	{
		return GetStableRunRng($"offers_stage_{stageIndex}");
	}

	private uint GetDeterministicFallbackSeed(string streamName)
	{
		return StableHash32($"{GetRunKey()}:{EventPrefix}:{streamName}");
	}

	protected static uint StableHash32(string value)
	{
		const uint offset = 2166136261u;
		const uint prime = 16777619u;
		uint hash = offset;
		foreach (char c in value)
		{
			hash ^= c;
			hash *= prime;
		}
		return (hash == 0) ? 1u : hash;
	}

	protected static LocString PlainText(string text)
	{
		ModSupport.EnsureAct4DynamicTextLocalizationReady();
		LocString val = new LocString("events", "ACT4_DYNAMIC_TEXT");
		val.Add("text", text);
		return val;
	}

	protected static EventOption CreateSimpleOption(Act4RewardEventBase eventModel, Func<Task> onChosen, string key, string title, string description, IEnumerable<IHoverTip>? hoverTips = null)
	{
		return new EventOption(eventModel, onChosen, PlainText(title), PlainText(description), key, hoverTips ?? Array.Empty<IHoverTip>());
	}

	protected static RewardOfferDefinition CreateRelicOffer<T>(string key) where T : RelicModel
	{
		return new RewardOfferDefinition
		{
			Key = key,
			CanOffer = (Act4RewardEventBase eventModel) => !eventModel.OwnerHasRelic<T>(),
			AutoGrant = (e) => e.ObtainUniqueRelicAsync<T>(),
			CreateOption = delegate(Act4RewardEventBase eventModel, Func<Task> onChosen)
			{
				RelicModel relic = ((RelicModel)ModelDb.Relic<T>()).ToMutable();
				if (((EventModel)eventModel).Owner != null)
				{
					relic.Owner = ((EventModel)eventModel).Owner;
				}
				return EventOption.FromRelic(relic, eventModel, async () =>
				{
					if (((EventModel)eventModel).Owner != null)
						await RelicCmd.Obtain(relic, ((EventModel)eventModel).Owner, -1);
					await onChosen.Invoke();
				}, key);
			}
		};
	}

	protected static RewardOfferDefinition CreateGoldOffer(string key, int amount)
	{
		return new RewardOfferDefinition
		{
			Key = key,
			CanOffer = (Act4RewardEventBase _) => true,
			AutoGrant = (e) => e.GainGoldAsync((decimal)amount),
			CreateOption = delegate(Act4RewardEventBase eventModel, Func<Task> onChosen)
			{
				return CreateSimpleOption(eventModel, async () =>
				{
					await eventModel.GainGoldAsync((decimal)amount);
					await onChosen();
				}, key, $"{amount} Gold", $"Gain {amount} gold.");
			}
		};
	}

	protected static RewardOfferDefinition CreateDuplicateCardOffer(string key)
	{
		return new RewardOfferDefinition
		{
			Key = key,
			CanOffer = (Act4RewardEventBase eventModel) => ((EventModel)eventModel).Owner != null && PileTypeExtensions.GetPile((PileType)6, ((EventModel)eventModel).Owner).Cards.Any(c => (int)c.Type != 6),
			AutoGrant = (e) => e.GainDuplicateCardOrGoldAsync(),
			CreateOption = delegate(Act4RewardEventBase eventModel, Func<Task> onChosen)
			{
				return CreateSimpleOption(eventModel, async () =>
				{
					await eventModel.DuplicateChosenCardAsync();
					await onChosen();
				}, key, "Duplicate a Card", "Choose a card in your deck to duplicate.");
			}
		};
	}

	protected static RewardOfferDefinition CreateBloodPotionOffer(string key)
	{
		return new RewardOfferDefinition
		{
			Key = key,
			CanOffer = (Act4RewardEventBase _) => true,
			AutoGrant = (e) => e.GainBloodPotionAsync(),
			CreateOption = (Act4RewardEventBase eventModel, Func<Task> onChosen) => CreateSimpleOption(eventModel, async delegate
			{
				await eventModel.GainBloodPotionAsync();
				await onChosen();
			}, key, "Blood Potion", "Gain a Blood Potion.")
		};
	}

	protected static RewardOfferDefinition CreateRemoveTwoCardsOffer(string key)
	{
		return new RewardOfferDefinition
		{
			Key = key,
			CanOffer = (Act4RewardEventBase eventModel) => ((EventModel)eventModel).Owner != null && PileType.Deck.GetPile(((EventModel)eventModel).Owner).Cards.Count((CardModel c) => c.IsRemovable) >= 2,
			CreateOption = delegate(Act4RewardEventBase eventModel, Func<Task> onChosen)
			{
				return CreateSimpleOption(eventModel, async delegate
				{
					Player owner = ((EventModel)eventModel).Owner;
					if (owner == null)
					{
						return;
					}
					CardSelectorPrefs cardSelectorPrefs = new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 2)
					{
						Cancelable = true,
						RequireManualConfirmation = true
					};
					IEnumerable<CardModel> enumerable = await CardSelectCmd.FromDeckForRemoval(owner, cardSelectorPrefs);
					if (!enumerable.Any())
					{
						eventModel.RefreshCurrentStageOptions();
						return;
					}
					foreach (CardModel item in enumerable)
					{
						await CardPileCmd.RemoveFromDeck(item);
					}
					await onChosen.Invoke();
				}, key, "Remove 2 Cards", "Choose 2 cards in your deck to remove.");
			}
		};
	}

	protected static RewardOfferDefinition CreateFairyOffer(string key)
	{
		return new RewardOfferDefinition
		{
			Key = key,
			CanOffer = (Act4RewardEventBase _) => true,
			AutoGrant = (e) => e.GainFairyPotionAsync(),
			CreateOption = delegate(Act4RewardEventBase eventModel, Func<Task> onChosen)
			{
				return CreateSimpleOption(eventModel, async () =>
				{
					await eventModel.GainFairyPotionAsync();
					await onChosen();
				}, key, "Fairy in a Bottle", "Gain a Fairy in a Bottle.", new IHoverTip[1] { HoverTipFactory.FromPotion<FairyInABottle>() });
			}
		};
	}
}
