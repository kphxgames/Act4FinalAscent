//=============================================================================
// NPlayerHandReturnHolderToHandPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Guards NPlayerHand.ReturnHolderToHand against stale queued indices during
//     rapid discard/draw or cancel-play races. Vanilla can try to MoveChild to
//     an index that no longer exists, which only throws a UI error but looks ugly.
// ZH: 为 NPlayerHand.ReturnHolderToHand 增加防护，处理快速弃牌/抽牌或取消出牌
//     时的过期索引。原版偶尔会把卡牌移回一个已失效的位置，结果只会报 UI 错误，
//     但看起来很糟。
//=============================================================================
using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Act4Placeholder;

[HarmonyPatch(typeof(NPlayerHand), "ReturnHolderToHand")]
internal static class NPlayerHandReturnHolderToHandPatch
{
	private static readonly AccessTools.FieldRef<NPlayerHand, Dictionary<NHandCardHolder, int>> HoldersAwaitingQueueRef = AccessTools.FieldRefAccess<NPlayerHand, Dictionary<NHandCardHolder, int>>("_holdersAwaitingQueue");

	private static bool Prefix(NPlayerHand __instance, NHandCardHolder holder)
	{
		try
		{
			if (holder == null)
			{
				return false;
			}
			Dictionary<NHandCardHolder, int> dictionary = HoldersAwaitingQueueRef(__instance);
			if (!dictionary.TryGetValue(holder, out var value))
			{
				return false;
			}
			dictionary.Remove(holder);
			holder.Reparent(__instance.CardHolderContainer);
			if (value >= 0)
			{
				int childCount = __instance.CardHolderContainer.GetChildCount();
				int num = Math.Clamp(value, 0, childCount);
				if (num != value)
				{
					Act4Logger.Info($"Clamped stale hand holder index {value} -> {num} during ReturnHolderToHand.");
				}
				__instance.CardHolderContainer.MoveChild(holder, num);
			}
			holder.SetDefaultTargets();
		}
		catch (Exception exception)
		{
			Act4Logger.Info($"ReturnHolderToHand guard failed, falling back to vanilla path: {exception.Message}");
			return true;
		}
		return false;
	}
}