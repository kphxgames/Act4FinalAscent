//=============================================================================
// ClientLoadJoinSyncPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Appends brutal-mode flag and weakest-buff player IDs to ClientLoadJoinResponseMessage
//     so that reconnecting clients receive the host's authoritative Act 4 state and don't
//     diverge on IsBrutalAct4 / HasAct4WeakestBuff checks that read host-only local files.
// ZH: 将残暴模式标志和最弱增益玩家ID附加到ClientLoadJoinResponseMessage，
//     使重连客户端能接收主机权威的第四幕状态，避免在读取仅主机拥有的本地文件时产生分歧。
//=============================================================================
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Act4Placeholder;

/// <summary>
/// EN: HOST side - after the normal payload has been written, append a version-tagged Act4 block:
///     [ushort 0xA4C4] [bool isBrutal] [int(8) count] [ulong id] * count
/// ZH: 主机侧——在正常负载写入完毕后，附加一个带版本标记的Act4数据块。
/// </summary>
[HarmonyPatch(typeof(ClientLoadJoinResponseMessage), nameof(ClientLoadJoinResponseMessage.Serialize))]
internal static class ClientLoadJoinResponseMessageSerializePatch
{
	private static void Postfix(ref ClientLoadJoinResponseMessage __instance, PacketWriter writer)
	{
		try
		{
			bool isBrutal = ModSupport.GetBrutalFlagForRun(__instance.serializableRun);
			List<ulong> ids = ModSupport.GetWeakestBuffPlayerIdsForRun(__instance.serializableRun);
			// Magic sentinel lets the client skip this block if the host is on an older mod version.
			writer.WriteUShort(0xA4C4);
			writer.WriteBool(isBrutal);
			writer.WriteInt(ids.Count, 8);
			foreach (ulong id in ids)
			{
				writer.WriteULong(id);
			}
		}
		catch
		{
			// Never crash the join handshake. Clients will fall back to local-file reads.
		}
	}
}

/// <summary>
/// EN: CLIENT side - after the normal payload has been read, attempt to read the version-tagged
///     Act4 block. Stores data in ModSupport pending statics so RestoreAct4FlagsFromSave can
///     consume them when RunState.FromSerializable fires.
/// ZH: 客户端侧——在正常负载读取完毕后，尝试读取带版本标记的Act4数据块，
///     将数据存入ModSupport待处理静态字段，供RunState.FromSerializable触发时的RestoreAct4FlagsFromSave使用。
/// </summary>
[HarmonyPatch(typeof(ClientLoadJoinResponseMessage), nameof(ClientLoadJoinResponseMessage.Deserialize))]
internal static class ClientLoadJoinResponseMessageDeserializePatch
{
	private static void Postfix(ref ClientLoadJoinResponseMessage __instance, PacketReader reader)
	{
		try
		{
			int bitsRemaining = reader.Buffer.Length * 8 - reader.BitPosition;
			if (bitsRemaining < 16)
			{
				return;
			}
			ushort magic = reader.ReadUShort();
			if (magic != 0xA4C4)
			{
				return;
			}
			bool isBrutal = reader.ReadBool();
			int count = reader.ReadInt(8);
			var ids = new List<ulong>(count);
			for (int i = 0; i < count; i++)
			{
				ids.Add(reader.ReadULong());
			}
			ModSupport.SetPendingJoinSyncState(__instance.serializableRun.StartTime, isBrutal, ids);
		}
		catch
		{
			// Gracefully degrade if the host is running an older mod version without this block.
		}
	}
}
