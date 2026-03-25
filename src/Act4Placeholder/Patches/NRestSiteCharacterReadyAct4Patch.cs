//=============================================================================
// NRestSiteCharacterReadyAct4Patch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches NRestSiteCharacter._Ready with a HarmonyFinalizer to suppress the InvalidOperationException thrown in Act 4, then completes rest-site setup with glory_loop animations and hover signals.
// ZH: 对NRestSiteCharacter._Ready应用HarmonyFinalizer，捕获第四幕中抛出的InvalidOperationException，并用glory_loop动画和悬停信号完成休息点角色初始化。
//=============================================================================
using System;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Random;

namespace Act4Placeholder;

// NRestSiteCharacter._Ready() throws InvalidOperationException("Unexpected act") for
// CurrentActIndex == 3 because its switch only handles acts 0-2.
// We use a [HarmonyFinalizer] to swallow that specific exception and finish the work
// that was aborted: set spine animations and connect hover signals.
[HarmonyPatch(typeof(NRestSiteCharacter), "_Ready")]
internal static class NRestSiteCharacterReadyAct4Patch
{
	[HarmonyFinalizer]
	private static Exception? Finalizer(NRestSiteCharacter __instance, Exception? __exception)
	{
		if (__exception is not InvalidOperationException)
			return __exception;

		int actIndex = __instance.Player?.RunState?.CurrentActIndex ?? -1;
		if (actIndex != 3)
			return __exception;

		// The GetNode<> field assignments above the switch ran successfully.
		// Finish setup: animations + hover signals.
		try
		{
			// Use "glory_loop" (Act 3 idle) as the Act 4 rest-site stand-in.
			foreach (Node2D spineNode in __instance.GetChildren().OfType<Node2D>()
			                                       .Where(n => n.GetClass() == "SpineSprite"))
			{
				MegaTrackEntry entry = new MegaSprite(spineNode).GetAnimationState().SetAnimation("glory_loop");
				entry?.SetTrackTime(entry.GetAnimationEnd() * Rng.Chaotic.NextFloat());
			}

			// Necrobinder-specific fire shader randomization (cosmetic).
			if (__instance.Player.Character is Necrobinder)
			{
				try
				{
					var randomizeFire = AccessTools.MethodDelegate<Action<ShaderMaterial>>(
						AccessTools.Method(typeof(NRestSiteCharacter), "RandomizeFire"), __instance);
					randomizeFire((ShaderMaterial)__instance.GetNode<Sprite2D>("%NecroFire").Material);
					randomizeFire((ShaderMaterial)__instance.GetNode<Sprite2D>("%OstyFire").Material);
				}
				catch (Exception ex)
				{
					Act4Logger.Info($"[NRestSiteCharacterReadyAct4Patch] Necrobinder fire skipped: {ex.Message}");
				}
			}

			// Connect hover signals so the character highlights on mouse-over.
			Action onFocus = AccessTools.MethodDelegate<Action>(
				AccessTools.Method(typeof(NRestSiteCharacter), "OnFocus"), __instance);
			Action onUnfocus = AccessTools.MethodDelegate<Action>(
				AccessTools.Method(typeof(NRestSiteCharacter), "OnUnfocus"), __instance);
			Control hitbox = __instance.Hitbox;
			hitbox.Connect(Control.SignalName.FocusEntered, Callable.From(onFocus));
			hitbox.Connect(Control.SignalName.FocusExited, Callable.From(onUnfocus));
			hitbox.Connect(Control.SignalName.MouseEntered, Callable.From(onFocus));
			hitbox.Connect(Control.SignalName.MouseExited, Callable.From(onUnfocus));
		}
		catch (Exception ex)
		{
			Act4Logger.Error($"[NRestSiteCharacterReadyAct4Patch] Act 4 rest-site character setup failed: {ex}");
		}

		return null; // swallow the "Unexpected act" exception
	}
}
