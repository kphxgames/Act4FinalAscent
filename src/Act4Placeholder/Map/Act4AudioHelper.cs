//=============================================================================
// Act4AudioHelper.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Static utility for playing sound effects in Act 4; tries NDebugAudioManager first, then falls back to a dynamically-created AudioStreamPlayer node attached to the game root.
// ZH: 第四幕音效播放的静态工具，优先使用NDebugAudioManager，不可用时动态创建AudioStreamPlayer节点挂载至游戏根节点。
//=============================================================================
using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Saves;

namespace Act4Placeholder;

internal static class Act4AudioHelper
{
	private static readonly StringName SfxBus = new StringName("SFX");

	private static readonly StringName MasterBus = new StringName("Master");

	private static AudioStreamPlayer? _modBgmPlayer;

	private static float _modBgmVolumeScale = 0.5f;

	/// Refresh timer so in-game BGM volume slider changes are picked up every 3s.
	private static Timer? _modBgmRefreshTimer; // EN: timer for delayed step, ZH: 延迟步骤计时器

	/// EN: Play a looping custom BGM from the mod pack (OGG or MP3 at the given res:// path).
	/// Stops any active FMOD music first so the custom track plays clean.
	/// Volume is scaled by the requested fraction of the game's BGM setting (same curve as FMOD uses) so it
	/// blends naturally with the rest of the soundtrack.  A background timer keeps the
	/// volume in sync if the player adjusts the BGM slider while the track is playing.
	/// ZH: 从模组打包文件播放循环自定义 BGM，先停止 FMOD 音乐再播放。
	///     音量按传入比例缩放游戏 BGM 设置值（与 FMOD 使用相同的平方曲线），
	///     并通过定时器持续同步，以响应游戏内音量滑块的实时调整。
	public static void PlayModBgm(string resPath, float volume = 1f)
	{
		StopModBgm();
		NRunMusicController.Instance?.StopMusic();
		if (NGame.Instance == null)
			return;
		AudioStream? stream = GD.Load<AudioStream>(resPath);
		if (stream == null)
			return;
		if (stream is AudioStreamOggVorbis ogg)
			ogg.Loop = true;
		else if (stream is AudioStreamMP3 mp3)
			mp3.Loop = true;
		_modBgmVolumeScale = Mathf.Clamp(volume, 0f, 1f);
		_modBgmPlayer = new AudioStreamPlayer();
		_modBgmPlayer.Stream = stream;
		_modBgmPlayer.Bus = MasterBus;
		// The Godot Master bus already scales by VolumeMaster^2 (set by NDebugAudioManager).
		// FMOD music's effective level ≈ VolumeBgm^2 × VolumeMaster^2.
		// Our player on the Master bus applies VolumeMaster^2 automatically, so we only
		// need to replicate the VolumeBgm curve and scale it by the requested amount.
		_modBgmPlayer.VolumeLinear = GetModBgmVolumeLinear();
		NGame.Instance.AddChildSafely(_modBgmPlayer);
		_modBgmPlayer.Play();

		// Refresh volume every 3 s so slider changes during gameplay are reflected.
		_modBgmRefreshTimer = new Timer();
		_modBgmRefreshTimer.WaitTime = 3.0;
		_modBgmRefreshTimer.Autostart = true;
		_modBgmRefreshTimer.Timeout += RefreshModBgmVolume;
		NGame.Instance.AddChildSafely(_modBgmRefreshTimer);
	}

	/// Compute the correct VolumeLinear for the custom BGM player.
	/// Mirrors FMOD's VolumeBgm^2 curve at the requested intensity.
	private static float GetModBgmVolumeLinear()
	{
		float bgmVol = SaveManager.Instance?.SettingsSave?.VolumeBgm ?? 0.5f;
		return _modBgmVolumeScale * Mathf.Pow(bgmVol, 2f);
	}

	private static void RefreshModBgmVolume()
	{
		if (_modBgmPlayer == null || !GodotObject.IsInstanceValid(_modBgmPlayer))
			return;
		_modBgmPlayer.VolumeLinear = GetModBgmVolumeLinear();
	}

	/// EN: Stop and free the custom mod BGM player if active.
	/// ZH: 停止并释放自定义 BGM 播放器（如已激活）。
	public static void StopModBgm()
	{
		_modBgmRefreshTimer?.QueueFreeSafely();
		_modBgmRefreshTimer = null;

		if (_modBgmPlayer == null)
			return;
		_modBgmPlayer.Stop();
		_modBgmPlayer.QueueFreeSafely();
		_modBgmPlayer = null;
		_modBgmVolumeScale = 0.5f;
	}

	public static void PlayTmp(string streamName, float volume = 1f)
	{
		try
		{
			if (NDebugAudioManager.Instance != null)
			{
				NDebugAudioManager.Instance.Play(streamName, volume);
				return;
			}
		}
		catch
		{
		}
		if (NGame.Instance == null)
		{
			return;
		}
		AudioStream stream = GD.Load<AudioStream>(TmpSfx.GetPath(streamName));
		if (stream == null)
		{
			return;
		}
		AudioStreamPlayer audioStreamPlayer = new AudioStreamPlayer();
		audioStreamPlayer.Stream = stream;
		audioStreamPlayer.VolumeLinear = volume;
		audioStreamPlayer.Bus = SfxBus;
		audioStreamPlayer.Finished += delegate
		{
			audioStreamPlayer.QueueFreeSafely();
		};
		NGame.Instance.AddChildSafely(audioStreamPlayer);
		audioStreamPlayer.Play();
	}
}
