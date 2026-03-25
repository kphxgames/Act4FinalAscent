//=============================================================================
// EventModelSetEventStatePatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches EventModel.SetEventState on TheArchitect event to inject Normal and Brutal Act 4 difficulty-selection options when the event fires during Act 3.
// ZH: 补丁修改EventModel.SetEventState，在第三幕触发建筑师事件时注入「普通第四幕」和「残酷第四幕」两个难度选择选项。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

[HarmonyPatch(typeof(EventModel), "SetEventState")]
internal static class EventModelSetEventStatePatch
{
	private static readonly LocString NormalAct4OptionTitle = new LocString("ancients", "ACT4_PLACEHOLDER.ACT4_OPTION.NORMAL.title");

	private static readonly LocString NormalAct4OptionDescription = new LocString("ancients", "ACT4_PLACEHOLDER.ACT4_OPTION.NORMAL.description");

	private static readonly LocString BrutalAct4OptionTitle = new LocString("ancients", "ACT4_PLACEHOLDER.ACT4_OPTION.BRUTAL.title");

	private static readonly LocString BrutalAct4OptionDescription = new LocString("ancients", "ACT4_PLACEHOLDER.ACT4_OPTION.BRUTAL.description");

	private const string NormalAct4OptionKey = "ACT4_PLACEHOLDER.ACT4_OPTION.NORMAL";

	private const string BrutalAct4OptionKey = "ACT4_PLACEHOLDER.ACT4_OPTION.BRUTAL";

	private const string FirstArchitectDialogueOptionKeySuffix = ".dialogue.0";

	private const string ProceedSpeechText = "I'll spare you for now...";

	private static void Prefix(EventModel __instance, ref IEnumerable<EventOption> eventOptions)
	{
		try
		{
			TheArchitect architect = __instance as TheArchitect;
			if (architect == null)
			{
				return;
			}

			// Diagnostic: always log when TheArchitect's SetEventState fires
			int actIndex = ((EventModel)architect).Owner?.RunState?.CurrentActIndex ?? -1;
			int actCount = ((EventModel)architect).Owner?.RunState?.Acts != null
				? ((IReadOnlyCollection<ActModel>)((EventModel)architect).Owner.RunState.Acts).Count
				: -1;
			Log.Info($"[Act4Placeholder] Architect.SetEventState - ActIndex={actIndex}, ActCount={actCount}", 2);

			if (((EventModel)architect).Owner == null || actIndex != 2 || actCount > 3)
			{
				Log.Info($"[Act4Placeholder]   Skipped: Owner={((EventModel)architect).Owner != null}, ActIndex={actIndex} (need 2), ActCount={actCount} (need <=3)", 2);
				return;
			}

			IEnumerable<EventOption> obj = eventOptions;
			List<EventOption> val = ((obj != null) ? obj.ToList() : null) ?? new List<EventOption>();

			Log.Info($"[Act4Placeholder]   Options count: {val.Count}", 2);
			// EN: The Architect event goes through a few dialogue states before the real Act 4 fork.
			//     This guard is intentionally picky so we only inject on the one-option
			//     "ready to proceed" page, not every page that happens to reuse SetEventState.
			// ZH: 建筑师事件会先经过几页对白，真正的第四幕分支不是一开始就出现。
			//     所以这里故意写得很挑，只在那个单选项的“准备继续”页面才注入。

			if (val.Count != 1 || val.Any(option => option.TextKey == "ACT4_PLACEHOLDER.ACT4_OPTION.NORMAL" || option.TextKey == "ACT4_PLACEHOLDER.ACT4_OPTION.BRUTAL"))
			{
				if (val.Count > 0)
				{
					Log.Info($"[Act4Placeholder]   Skipped: Count={val.Count}, TextKeys=[{string.Join(", ", val.Select(o => o.TextKey ?? "(null)"))}]", 2);
				}
				return;
			}

			EventOption val2 = val[0];
			bool flag = val2.TextKey?.StartsWith(((AbstractModel)architect).Id.Entry + ".dialogue.", (StringComparison)4) ?? false;
			bool flag2 = val2.TextKey?.EndsWith(".dialogue.0", (StringComparison)4) ?? false;
			bool isProceed = val2.IsProceed;
			bool isTextKeyProceed = !(val2.TextKey != "PROCEED");
			bool isLocked = val2.IsLocked;

			Log.Info($"[Act4Placeholder]   Option[0]: TextKey='{val2.TextKey}', IsProceed={isProceed}, IsLocked={isLocked}, isDialogueKey={flag}, isFirstDialogue={flag2}", 2);

			if (!isLocked && !flag2 && (isProceed || isTextKeyProceed || flag))
			{
				val.Add(new EventOption(architect, (Func<Task>)(() => ProceedToAct4PlaceholderAsync(architect, brutal: false)), NormalAct4OptionTitle, NormalAct4OptionDescription, "ACT4_PLACEHOLDER.ACT4_OPTION.NORMAL", Array.Empty<IHoverTip>()));
				val.Add(new EventOption(architect, (Func<Task>)(() => ProceedToAct4PlaceholderAsync(architect, brutal: true)), BrutalAct4OptionTitle, BrutalAct4OptionDescription, "ACT4_PLACEHOLDER.ACT4_OPTION.BRUTAL", Array.Empty<IHoverTip>()));
				eventOptions = val;
				Log.Info($"[Act4Placeholder]   SUCCESS: Injected Act 4 Normal + Brutal options.", 2);
			}
			else
			{
				Log.Info($"[Act4Placeholder]   Skipped injection: IsLocked={isLocked}, isFirstDialogue={flag2}, IsProceed={isProceed}, TextKey=='PROCEED': {isTextKeyProceed}, isDialogueKey={flag}", 2);
			}
		}
		catch (Exception ex)
		{
			Log.Error($"[Act4Placeholder] EXCEPTION in Architect SetEventState Prefix: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
		}
	}

	private static async Task ProceedToAct4PlaceholderAsync(TheArchitect architect, bool brutal)
	{
		if (((EventModel)architect).Owner == null)
		{
			return;
		}
		RunState runState = ((EventModel)architect).Owner.RunState as RunState;
		if (runState == null || RunManager.Instance == null)
		{
			return;
		}
		// EN: Shared events are sneaky here, this callback can fire once per local event copy.
		//     The duplicate guard stops co-op from sending a second ready vote and trying to
		//     shove the run into Act 4 twice. That bug is exactly as ugly as it sounds.
		// ZH: 共享事件这里很阴，回调可能会按本地事件副本各触发一次。
		//     这个防重就是为了拦住第二次 ready 投票，避免联机里把跑图推进第四幕两次。
		if (ModSupport.Act4TransitionPending || runState.Acts.Count > 3)
		{
			Log.Info("[Act4Placeholder] ProceedToAct4PlaceholderAsync: skipping duplicate event option fire (transition already in progress or complete)", 2);
			return;
		}
		await ShowProceedSpeechAsync();
		await ModSupport.ProceedToAct4Async(runState, brutal);
		if (((IReadOnlyCollection<Player>)runState.Players).Count > 1)
		{
			NCombatRoom instance = NCombatRoom.Instance;
			if (instance != null)
			{
				instance.SetWaitingForOtherPlayersOverlayVisible(true);
			}
		}
		RunManager.Instance.ActChangeSynchronizer.SetLocalPlayerReady();
	}

	private static async Task ShowProceedSpeechAsync()
	{
		Creature speaker = NCombatRoom.Instance?.CreatureNodes?.FirstOrDefault((NCreature node) => node?.Entity?.Side == CombatSide.Enemy)?.Entity;
		if (speaker == null)
		{
			return;
		}
		NSpeechBubbleVfx bubble = NSpeechBubbleVfx.Create(ProceedSpeechText, speaker, 5.0, VfxColor.Blue);
		if (bubble != null)
		{
			NCombatRoom.Instance?.CombatVfxContainer?.AddChild(bubble);
		}
		await Task.CompletedTask;
	}
}
