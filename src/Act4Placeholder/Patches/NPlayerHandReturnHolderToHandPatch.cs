//=============================================================================
// NPlayerHandReturnHolderToHandPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Guards NPlayerHand.ReturnHolderToHand against stale queued indices during
//     rapid discard/draw or cancel-play races. Vanilla can try to MoveChild to
//     an index that no longer exists, which only throws a UI error but looks ugly.
//     Also extends CancelAllCardPlay to cancel active in-hand selection mode
//     (e.g. Prepared's discard picker) so it can't get stuck when retaliation fires.
// ZH: 为 NPlayerHand.ReturnHolderToHand 增加防护，处理快速弃牌/抽牌或取消出牌
//     时的过期索引。原版偶尔会把卡牌移回一个已失效的位置，结果只会报 UI 错误，
//     但看起来很糟。
//     同时扩展 CancelAllCardPlay，取消进行中的手牌选择模式
//     （如准备牌的弃牌选择器），防止报复触发时卡死。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
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

/// <summary>
/// EN: Extends CancelAllCardPlay to also cancel any active in-hand card selection
///     (e.g. Prepared's discard picker). CancelAllCardPlay only handles card drag and
///     queued holders; it leaves _currentMode == SimpleSelect and the pending
///     _selectionCompletionSource alive, which can soft-lock the hand if retaliation
///     fires mid-selection. Calling the private CancelHandSelectionIfNecessary fixes this.
/// ZH: 扩展 CancelAllCardPlay，同时取消进行中的手牌卡牌选择
///     （如准备牌的弃牌选择器）。CancelAllCardPlay 只处理拖拽和排队的持有者，
///     不会清除 _currentMode == SimpleSelect 和待处理的 _selectionCompletionSource，
///     报复触发时会导致手牌卡死。调用私有的 CancelHandSelectionIfNecessary 修复此问题。
/// </summary>
[HarmonyPatch(typeof(NPlayerHand), nameof(NPlayerHand.CancelAllCardPlay))]
internal static class NPlayerHandCancelSelectionOnCancelAllPatch
{
	private static readonly MethodInfo _cancelHandSelection =
		AccessTools.Method(typeof(NPlayerHand), "CancelHandSelectionIfNecessary");

	private static void Postfix(NPlayerHand __instance)
	{
		try
		{
			_cancelHandSelection?.Invoke(__instance, null);
		}
		catch (Exception ex)
		{
			Act4Logger.Info($"CancelHandSelectionIfNecessary failed: {ex.Message}");
		}
	}
}