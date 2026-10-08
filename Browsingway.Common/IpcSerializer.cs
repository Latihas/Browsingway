using System;
using System.IO;
using System.Text;

namespace Browsingway.Common;

internal static class IpcSerializer {
	private const byte ProtocolVersion = 1;
	private const int MaxFieldLength = 16 * 1024 * 1024;

	private enum MessageKind : byte {
		NewOverlay = 1,
		Navigate = 2,
		ResizeOverlay = 3,
		InjectUserCss = 4,
		Zoom = 5,
		Mute = 6,
		Debug = 7,
		RemoveOverlay = 8,
		MouseButton = 9,
		KeyEvent = 10,
		RendererReady = 11,
		UpdateTexture = 12,
		SetCursor = 13
	}

	public static byte[] SerializeRpcCall(RpcCall call) {
		ArgumentNullException.ThrowIfNull(call);

		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
		writer.Write(ProtocolVersion);

		var kind = GetMessageKind(call);
		writer.Write((byte)kind);
		switch (kind) {
			case MessageKind.NewOverlay:
				WriteNewOverlay(writer, call.NewOverlay!);
				break;
			case MessageKind.Navigate:
				WriteNavigate(writer, call.Navigate!);
				break;
			case MessageKind.ResizeOverlay:
				WriteResizeOverlay(writer, call.ResizeOverlay!);
				break;
			case MessageKind.InjectUserCss:
				WriteInjectUserCss(writer, call.InjectUserCss!);
				break;
			case MessageKind.Zoom:
				WriteZoom(writer, call.Zoom!);
				break;
			case MessageKind.Mute:
				WriteMute(writer, call.Mute!);
				break;
			case MessageKind.Debug:
				WriteGuid(writer, call.Debug!.Guid);
				break;
			case MessageKind.RemoveOverlay:
				WriteGuid(writer, call.RemoveOverlay!.Guid);
				break;
			case MessageKind.MouseButton:
				WriteMouseButton(writer, call.MouseButton!);
				break;
			case MessageKind.KeyEvent:
				WriteKeyEvent(writer, call.KeyEvent!);
				break;
			case MessageKind.RendererReady:
				writer.Write(call.RendererReady!.HasDxSharedTexturesSupport);
				break;
			case MessageKind.UpdateTexture:
				WriteGuid(writer, call.UpdateTexture!.Guid);
				writer.Write(call.UpdateTexture.TextureHandle);
				break;
			case MessageKind.SetCursor:
				WriteGuid(writer, call.SetCursor!.Guid);
				writer.Write((byte)call.SetCursor.Cursor);
				break;
			default:
				throw new InvalidOperationException($"Unsupported IPC message kind: {kind}.");
		}

		writer.Flush();
		return stream.ToArray();
	}

	public static RpcCall DeserializeRpcCall(ReadOnlySpan<byte> data) {
		using var stream = new MemoryStream([.. data], false);
		using var reader = new BinaryReader(stream, Encoding.UTF8, true);
		ReadVersion(reader);

		var kind = ReadMessageKind(reader);
		RpcCall call = new();
		switch (kind) {
			case MessageKind.NewOverlay:
				call.NewOverlay = ReadNewOverlay(reader);
				break;
			case MessageKind.Navigate:
				call.Navigate = ReadNavigate(reader);
				break;
			case MessageKind.ResizeOverlay:
				call.ResizeOverlay = ReadResizeOverlay(reader);
				break;
			case MessageKind.InjectUserCss:
				call.InjectUserCss = ReadInjectUserCss(reader);
				break;
			case MessageKind.Zoom:
				call.Zoom = ReadZoom(reader);
				break;
			case MessageKind.Mute:
				call.Mute = ReadMute(reader);
				break;
			case MessageKind.Debug:
				call.Debug = new DebugMessage { Guid = ReadGuid(reader) };
				break;
			case MessageKind.RemoveOverlay:
				call.RemoveOverlay = new RemoveOverlayMessage { Guid = ReadGuid(reader) };
				break;
			case MessageKind.MouseButton:
				call.MouseButton = ReadMouseButton(reader);
				break;
			case MessageKind.KeyEvent:
				call.KeyEvent = ReadKeyEvent(reader);
				break;
			case MessageKind.RendererReady:
				call.RendererReady = new RendererReadyMessage {
					HasDxSharedTexturesSupport = reader.ReadBoolean()
				};
				break;
			case MessageKind.UpdateTexture:
				call.UpdateTexture = new UpdateTextureMessage {
					Guid = ReadGuid(reader),
					TextureHandle = reader.ReadUInt64()
				};
				break;
			case MessageKind.SetCursor:
				call.SetCursor = new SetCursorMessage {
					Guid = ReadGuid(reader),
					Cursor = (Cursor)reader.ReadByte()
				};
				break;
			default:
				throw new FormatException($"Unsupported IPC message kind: {(byte)kind}.");
		}

		return call;
	}

	public static RpcCall DeserializeRpcCall(byte[] data) =>
		DeserializeRpcCall(data.AsSpan());

	public static byte[] SerializeRenderParams(RenderParams renderParams) {
		ArgumentNullException.ThrowIfNull(renderParams);

		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
		writer.Write(ProtocolVersion);
		WriteString(writer, renderParams.WebView2UserDataDir, nameof(renderParams.WebView2UserDataDir));
		WriteString(writer, renderParams.DalamudAssemblyDir, nameof(renderParams.DalamudAssemblyDir));
		writer.Write(renderParams.DxgiAdapterLuidHigh);
		writer.Write(renderParams.DxgiAdapterLuidLow);
		WriteString(writer, renderParams.IpcChannelName, nameof(renderParams.IpcChannelName));
		WriteString(writer, renderParams.KeepAliveHandleName, nameof(renderParams.KeepAliveHandleName));
		writer.Write(renderParams.ParentPid);
		writer.Flush();
		return stream.ToArray();
	}

	public static RenderParams DeserializeRenderParams(ReadOnlySpan<byte> data) {
		using var stream = new MemoryStream([.. data], false);
		using var reader = new BinaryReader(stream, Encoding.UTF8, true);
		ReadVersion(reader);
		return new RenderParams {
			WebView2UserDataDir = ReadString(reader, nameof(RenderParams.WebView2UserDataDir)),
			DalamudAssemblyDir = ReadString(reader, nameof(RenderParams.DalamudAssemblyDir)),
			DxgiAdapterLuidHigh = reader.ReadInt32(),
			DxgiAdapterLuidLow = reader.ReadUInt32(),
			IpcChannelName = ReadString(reader, nameof(RenderParams.IpcChannelName)),
			KeepAliveHandleName = ReadString(reader, nameof(RenderParams.KeepAliveHandleName)),
			ParentPid = reader.ReadInt32()
		};
	}

	public static RenderParams DeserializeRenderParams(byte[] data) =>
		DeserializeRenderParams(data.AsSpan());

	private static MessageKind GetMessageKind(RpcCall call) {
		var count = 0;
		MessageKind kind = default;

		Consider(MessageKind.NewOverlay, call.NewOverlay is not null);
		Consider(MessageKind.Navigate, call.Navigate is not null);
		Consider(MessageKind.ResizeOverlay, call.ResizeOverlay is not null);
		Consider(MessageKind.InjectUserCss, call.InjectUserCss is not null);
		Consider(MessageKind.Zoom, call.Zoom is not null);
		Consider(MessageKind.Mute, call.Mute is not null);
		Consider(MessageKind.Debug, call.Debug is not null);
		Consider(MessageKind.RemoveOverlay, call.RemoveOverlay is not null);
		Consider(MessageKind.MouseButton, call.MouseButton is not null);
		Consider(MessageKind.KeyEvent, call.KeyEvent is not null);
		Consider(MessageKind.RendererReady, call.RendererReady is not null);
		Consider(MessageKind.UpdateTexture, call.UpdateTexture is not null);
		Consider(MessageKind.SetCursor, call.SetCursor is not null);

		return count switch {
			1 => kind,
			0 => throw new InvalidOperationException("An IPC call must contain one message."),
			_ => throw new InvalidOperationException("An IPC call cannot contain multiple messages.")
		};

		void Consider(MessageKind candidate, bool present) {
			if (!present) return;
			count++;
			kind = candidate;
		}
	}

	private static void WriteNewOverlay(BinaryWriter writer, NewOverlayMessage message) {
		writer.Write(message.Framerate);
		WriteGuid(writer, message.Guid);
		WriteString(writer, message.Id, nameof(message.Id));
		writer.Write(message.Height);
		WriteString(writer, message.Url, nameof(message.Url));
		writer.Write(message.Width);
		writer.Write(message.Zoom);
		writer.Write(message.Muted);
		WriteString(writer, message.CustomCss, nameof(message.CustomCss));
	}

	private static NewOverlayMessage ReadNewOverlay(BinaryReader reader) => new() {
		Framerate = reader.ReadInt32(),
		Guid = ReadGuid(reader),
		Id = ReadString(reader, nameof(NewOverlayMessage.Id)),
		Height = reader.ReadInt32(),
		Url = ReadString(reader, nameof(NewOverlayMessage.Url)),
		Width = reader.ReadInt32(),
		Zoom = reader.ReadSingle(),
		Muted = reader.ReadBoolean(),
		CustomCss = ReadString(reader, nameof(NewOverlayMessage.CustomCss))
	};

	private static void WriteNavigate(BinaryWriter writer, NavigateMessage message) {
		WriteGuid(writer, message.Guid);
		WriteString(writer, message.Url, nameof(message.Url));
	}

	private static NavigateMessage ReadNavigate(BinaryReader reader) => new() {
		Guid = ReadGuid(reader),
		Url = ReadString(reader, nameof(NavigateMessage.Url))
	};

	private static void WriteResizeOverlay(BinaryWriter writer, ResizeOverlayMessage message) {
		WriteGuid(writer, message.Guid);
		writer.Write(message.Height);
		writer.Write(message.Width);
	}

	private static ResizeOverlayMessage ReadResizeOverlay(BinaryReader reader) => new() {
		Guid = ReadGuid(reader),
		Height = reader.ReadInt32(),
		Width = reader.ReadInt32()
	};

	private static void WriteInjectUserCss(BinaryWriter writer, InjectUserCssMessage message) {
		WriteGuid(writer, message.Guid);
		WriteString(writer, message.Css, nameof(message.Css));
	}

	private static InjectUserCssMessage ReadInjectUserCss(BinaryReader reader) => new() {
		Guid = ReadGuid(reader),
		Css = ReadString(reader, nameof(InjectUserCssMessage.Css))
	};

	private static void WriteZoom(BinaryWriter writer, ZoomMessage message) {
		WriteGuid(writer, message.Guid);
		writer.Write(message.Zoom);
	}

	private static ZoomMessage ReadZoom(BinaryReader reader) => new() {
		Guid = ReadGuid(reader),
		Zoom = reader.ReadSingle()
	};

	private static void WriteMute(BinaryWriter writer, MuteMessage message) {
		WriteGuid(writer, message.Guid);
		writer.Write(message.Mute);
	}

	private static MuteMessage ReadMute(BinaryReader reader) => new() {
		Guid = ReadGuid(reader),
		Mute = reader.ReadBoolean()
	};

	private static void WriteMouseButton(BinaryWriter writer, MouseButtonMessage message) {
		WriteGuid(writer, message.Guid);
		writer.Write((uint)message.Double);
		writer.Write((uint)message.Down);
		writer.Write(message.Leaving);
		writer.Write((byte)message.Modifier);
		writer.Write((uint)message.Up);
		writer.Write(message.WheelX);
		writer.Write(message.WheelY);
		writer.Write(message.X);
		writer.Write(message.Y);
	}

	private static MouseButtonMessage ReadMouseButton(BinaryReader reader) => new() {
		Guid = ReadGuid(reader),
		Double = (MouseButton)reader.ReadUInt32(),
		Down = (MouseButton)reader.ReadUInt32(),
		Leaving = reader.ReadBoolean(),
		Modifier = (InputModifier)reader.ReadByte(),
		Up = (MouseButton)reader.ReadUInt32(),
		WheelX = reader.ReadSingle(),
		WheelY = reader.ReadSingle(),
		X = reader.ReadSingle(),
		Y = reader.ReadSingle()
	};

	private static void WriteKeyEvent(BinaryWriter writer, KeyEventMessage message) {
		WriteGuid(writer, message.Guid);
		writer.Write(message.Msg);
		writer.Write(message.WParam);
		writer.Write(message.LParam);
	}

	private static KeyEventMessage ReadKeyEvent(BinaryReader reader) => new() {
		Guid = ReadGuid(reader),
		Msg = reader.ReadInt32(),
		WParam = reader.ReadInt32(),
		LParam = reader.ReadInt32()
	};

	private static void WriteGuid(BinaryWriter writer, byte[]? value) {
		if (value is null || value.Length != 16)
			throw new ArgumentException("IPC GUID fields must contain exactly 16 bytes.", nameof(value));

		writer.Write(value);
	}

	private static byte[] ReadGuid(BinaryReader reader) {
		var value = reader.ReadBytes(16);
		return value.Length != 16 ? throw new FormatException("IPC GUID field is truncated.") : value;
	}

	private static void WriteString(BinaryWriter writer, string? value, string fieldName) {
		if (value is null)
			throw new ArgumentNullException(fieldName);

		var bytes = Encoding.UTF8.GetBytes(value);
		if (bytes.Length > MaxFieldLength)
			throw new ArgumentException($"IPC field '{fieldName}' is too large.", fieldName);

		writer.Write(bytes.Length);
		writer.Write(bytes);
	}

	private static string ReadString(BinaryReader reader, string fieldName) {
		var length = reader.ReadInt32();
		if (length is < 0 or > MaxFieldLength)
			throw new FormatException($"IPC field '{fieldName}' has an invalid length.");

		var bytes = reader.ReadBytes(length);
		return bytes.Length != length ? throw new FormatException($"IPC field '{fieldName}' is truncated.") : Encoding.UTF8.GetString(bytes);
	}

	private static void ReadVersion(BinaryReader reader) {
		var version = reader.ReadByte();
		if (version != ProtocolVersion)
			throw new FormatException($"Unsupported IPC protocol version: {version}.");
	}

	private static MessageKind ReadMessageKind(BinaryReader reader) {
		var raw = reader.ReadByte();
		if (raw is < (byte)MessageKind.NewOverlay or > (byte)MessageKind.SetCursor)
			throw new FormatException($"Unsupported IPC message kind: {raw}.");

		return (MessageKind)raw;
	}
}