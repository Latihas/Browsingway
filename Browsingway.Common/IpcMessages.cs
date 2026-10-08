using System;
using System.Diagnostics.CodeAnalysis;

namespace Browsingway.Common;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public enum Cursor : byte {
	Default = 0,
	None = 1,
	ContextMenu = 2,
	Help = 3,
	Pointer = 4,
	Progress = 5,
	Wait = 6,
	Cell = 7,
	Crosshair = 8,
	Text = 9,
	VerticalText = 10,
	Alias = 11,
	Copy = 12,
	Move = 13,
	NoDrop = 14,
	NotAllowed = 15,
	Grab = 16,
	Grabbing = 17,
	AllScroll = 18,
	ColResize = 19,
	RowResize = 20,
	NResize = 21,
	EResize = 22,
	SResize = 23,
	WResize = 24,
	NeResize = 25,
	NwResize = 26,
	SeResize = 27,
	SwResize = 28,
	EwResize = 29,
	NsResize = 30,
	NeswResize = 31,
	NwseResize = 32,
	ZoomIn = 33,
	ZoomOut = 34,
	BrowsingwayNoCapture = 35
}

[Flags]
public enum InputModifier : byte {
	None = 0,
	Shift = 1,
	Control = 2,
	Alt = 4
}

[Flags]
public enum MouseButton : uint {
	None = 0,
	Primary = 1,
	Secondary = 2,
	Tertiary = 4,
	Fourth = 8,
	Fifth = 16
}

public enum KeyEventType : byte {
	KeyDown = 0,
	KeyUp = 1,
	Character = 2
}

// public sealed class BitmapFrame {
// 	public int Length { get; set; }
// 	public int Width { get; set; }
// 	public int Height { get; set; }
// 	public int DirtyX { get; set; }
// 	public int DirtyY { get; set; }
// 	public int DirtyWidth { get; set; }
// 	public int DirtyHeight { get; set; }
// }
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class RenderParams {
	public string WebView2UserDataDir { get; set; } = string.Empty;
	public string DalamudAssemblyDir { get; set; } = string.Empty;
	public int DxgiAdapterLuidHigh { get; set; }
	public uint DxgiAdapterLuidLow { get; set; }
	public string IpcChannelName { get; set; } = string.Empty;
	public string KeepAliveHandleName { get; set; } = string.Empty;
	public int ParentPid { get; set; }
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class RendererReadyMessage {
	public bool HasDxSharedTexturesSupport { get; set; }
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class UpdateTextureMessage {
	public byte[] Guid { get; set; } = [];
	public ulong TextureHandle { get; set; }
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class SetCursorMessage {
	public Cursor Cursor { get; set; }
	public byte[] Guid { get; set; } = [];
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class NewOverlayMessage {
	public int Framerate { get; set; }
	public byte[] Guid { get; set; } = [];
	public string Id { get; set; } = string.Empty;
	public int Height { get; set; }
	public string Url { get; set; } = string.Empty;
	public int Width { get; set; }
	public float Zoom { get; set; }
	public bool Muted { get; set; }
	public string CustomCss { get; set; } = string.Empty;
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class NavigateMessage {
	public byte[] Guid { get; set; } = [];
	public string Url { get; set; } = string.Empty;
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class ResizeOverlayMessage {
	public byte[] Guid { get; set; } = [];
	public int Height { get; set; }
	public int Width { get; set; }
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class InjectUserCssMessage {
	public byte[] Guid { get; set; } = [];
	public string Css { get; set; } = string.Empty;
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class ZoomMessage {
	public byte[] Guid { get; set; } = [];
	public float Zoom { get; set; }
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class MuteMessage {
	public byte[] Guid { get; set; } = [];
	public bool Mute { get; set; }
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class DebugMessage {
	public byte[] Guid { get; set; } = [];
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class RemoveOverlayMessage {
	public byte[] Guid { get; set; } = [];
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class MouseButtonMessage {
	public byte[] Guid { get; set; } = [];
	public MouseButton Double { get; set; }
	public MouseButton Down { get; set; }
	public bool Leaving { get; set; }
	public InputModifier Modifier { get; set; }
	public MouseButton Up { get; set; }
	public float WheelX { get; set; }
	public float WheelY { get; set; }
	public float X { get; set; }
	public float Y { get; set; }
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public sealed class KeyEventMessage {
	public byte[] Guid { get; set; } = [];
	public int Msg { get; set; }
	public int WParam { get; set; }
	public int LParam { get; set; }
}

public sealed class RpcCall {
	public NewOverlayMessage? NewOverlay { get; set; }
	public NavigateMessage? Navigate { get; set; }
	public ResizeOverlayMessage? ResizeOverlay { get; set; }
	public InjectUserCssMessage? InjectUserCss { get; set; }
	public ZoomMessage? Zoom { get; set; }
	public MuteMessage? Mute { get; set; }
	public DebugMessage? Debug { get; set; }
	public RemoveOverlayMessage? RemoveOverlay { get; set; }
	public MouseButtonMessage? MouseButton { get; set; }
	public KeyEventMessage? KeyEvent { get; set; }
	public RendererReadyMessage? RendererReady { get; set; }
	public UpdateTextureMessage? UpdateTexture { get; set; }
	public SetCursorMessage? SetCursor { get; set; }
}