using System.Numerics;
using Browsingway.Common;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;

namespace Browsingway;

internal class Overlay : IDisposable {
	private readonly InlayConfiguration _overlayConfig;

	private readonly RenderProcess _renderProcess;
	private bool _captureCursor;
	private ImGuiMouseCursor _cursor;

	private bool _mouseInWindow;

	private bool _resizing;
	private Vector2 _size;
	private bool _hasRenderError;
	private SharedTextureHandler? _textureHandler;
	private Exception? _textureRenderException;
	private bool _windowFocused;
	private long _timeLastInCombat;
	private readonly ISharedImmediateTexture? _texErrorIcon;

	public Overlay(RenderProcess renderProcess, InlayConfiguration overlayConfig, string pluginDir) {
		_renderProcess = renderProcess;
		// TODO: handle that the correct way
		_renderProcess.Crashed += (_, _) => {
			_size = Vector2.Zero;
			_hasRenderError = true;
		};

		_overlayConfig = overlayConfig;
		_texErrorIcon = Plugin.TextureProvider.GetFromFile(Path.Combine(pluginDir, "dead.png"));
	}

	public Guid RenderGuid => _overlayConfig.Guid;

	public void Dispose() {
		_textureHandler?.Dispose();
		_ = _renderProcess.Rpc?.RemoveOverlay(RenderGuid);
	}

	public void Navigate(string newUrl) {
		_ = _renderProcess.Rpc?.Navigate(RenderGuid, newUrl);
	}

	public void InjectUserCss(string css) {
		_ = _renderProcess.Rpc?.InjectUserCss(RenderGuid, css);
	}

	public void Zoom(float zoom) {
		_ = _renderProcess.Rpc?.Zoom(RenderGuid, zoom);
	}

	public void Mute(bool mute) {
		_ = _renderProcess.Rpc?.Mute(RenderGuid, mute);
	}

	public void Debug() {
		_ = _renderProcess.Rpc?.Debug(RenderGuid);
	}

	public void SetCursor(Cursor cursor) {
		_captureCursor = cursor != Cursor.BrowsingwayNoCapture;
		_cursor = DecodeCursor(cursor);
	}

	public (bool, long) WndProcMessage(WindowsMessage msg, ulong wParam, long lParam) {
		if (msg == WindowsMessage.WM_LBUTTONDOWN) {
			// this message is only generated when someone clicked on an non ImGui window, meaning we want to loose focus here
			_windowFocused = false;
		}
		// Bail if we're not focused or we're typethrough
		// TODO: Revisit the focus check for UI stuff, might not hold
		if (!_windowFocused || _overlayConfig.TypeThrough) { return (false, 0); }
		KeyEventType? eventType = msg switch {
			WindowsMessage.WM_KEYDOWN => KeyEventType.KeyDown,
			WindowsMessage.WM_SYSKEYDOWN => KeyEventType.KeyDown,
			WindowsMessage.WM_KEYUP => KeyEventType.KeyUp,
			WindowsMessage.WM_SYSKEYUP => KeyEventType.KeyUp,
			WindowsMessage.WM_CHAR => KeyEventType.Character,
			WindowsMessage.WM_SYSCHAR => KeyEventType.Character,
			_ => null
		};
		// If the event isn't something we're tracking, bail early with no capture
		if (eventType == null) return (false, 0);
		_ = _renderProcess.Rpc?.KeyEvent(RenderGuid, (int)msg, (int)wParam, (int)lParam);
		// We've handled the input, signal. For these message types, `0` signals a capture.
		return (true, 0);
	}

	public void Render() {
		if (_overlayConfig.Hidden || _overlayConfig.Disabled || HiddenByCombatFlags() ||
		    _overlayConfig.HideInPvP && Plugin.ClientState.IsPvP) {
			_mouseInWindow = false;
			return;
		}
		ImGui.SetNextWindowSize(new Vector2(640, 480), ImGuiCond.FirstUseEver);
		ImGui.Begin($"{_overlayConfig.Name}###{_overlayConfig.Guid}", GetWindowFlags());

		if (_overlayConfig.Fullscreen) {
			var screen = ImGui.GetMainViewport();
			// ImGui always leaves a 1px transparent border around the window, so we need to account for that.
			var fsPos = new Vector2(screen.WorkPos.X - 1, screen.WorkPos.Y - 1);
			var fsSize = new Vector2(screen.Size.X + 2 - fsPos.X, screen.Size.Y + 2 - fsPos.Y);
			if (ImGui.GetWindowPos() != fsPos) ImGui.SetWindowPos(fsPos, ImGuiCond.Always);
			// ReSharper disable twice CompareOfFloatsByEqualityOperator
			if (_size.X != fsSize.X || _size.Y != fsSize.Y) ImGui.SetWindowSize(fsSize, ImGuiCond.Always);
		}
		HandleWindowSize();
		// TODO: Browsingway.Renderer can take some time to spin up properly, should add a loading state.
		if (_textureHandler != null && !_hasRenderError) {
			ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _overlayConfig.Opacity / 100f);
			var imagePos = ImGui.GetCursorScreenPos();
			_textureHandler.Render();
			ImGui.PopStyleVar();
			HandleMouseEvent(imagePos, _textureHandler.Size);
		} else {
			if (_texErrorIcon is not null) {
				var lineHeight = ImGui.GetTextLineHeight();
				var size = float.Min(_size.X - lineHeight * 3, _size.Y - lineHeight * 3);
				ImGui.NewLine();
				ImGuiHelpers.CenterCursorFor(size);
				ImGui.Image(_texErrorIcon.GetWrapOrEmpty().Handle, new Vector2(size, size));
				ImGui.PushStyleColor(ImGuiCol.Text, 0xFF0000FF);
				if (_textureRenderException is not null) {
					ImGuiHelpers.CenteredText("An error occured while building the browser overlay texture:");
					ImGuiHelpers.CenteredText(_textureRenderException.ToString());
				} else
					ImGuiHelpers.CenteredText("An error occured while building the browser overlay texture. Check the log for more details.");
				ImGui.PopStyleColor();
			}
		}
		ImGui.End();
	}

	private ImGuiWindowFlags GetWindowFlags() {
		var flags = ImGuiWindowFlags.None
		            | ImGuiWindowFlags.NoTitleBar
		            | ImGuiWindowFlags.NoCollapse
		            | ImGuiWindowFlags.NoScrollbar
		            | ImGuiWindowFlags.NoScrollWithMouse
		            | ImGuiWindowFlags.NoBringToFrontOnFocus
		            | ImGuiWindowFlags.NoFocusOnAppearing;
		// ClickThrough / fullscreen is implicitly locked
		var locked = _overlayConfig.Locked || _overlayConfig.ClickThrough || _overlayConfig.Fullscreen;
		if (locked)
			flags |= ImGuiWindowFlags.None
			         | ImGuiWindowFlags.NoMove
			         | ImGuiWindowFlags.NoResize
			         | ImGuiWindowFlags.NoBackground;
		if (_overlayConfig.ClickThrough || !_captureCursor && locked)
			flags |= ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoNav;
		// don't think user wants a background when they decrease opacity
		if (_overlayConfig.Opacity < 100f)
			flags |= ImGuiWindowFlags.NoBackground;
		return flags;
	}

	public void SetTexture(IntPtr handle) {
		_resizing = false;
		_hasRenderError = false;
		var oldTextureHandler = _textureHandler;
		SharedTextureHandler newTextureHandler;
		try {
			newTextureHandler = new SharedTextureHandler(handle);
		} catch (Exception e) {
			_textureRenderException = e;
			return;
		}
		_textureRenderException = null;
		_textureHandler = newTextureHandler;
		oldTextureHandler?.Dispose();
	}

	private void HandleMouseEvent(Vector2 imagePos, Vector2 imageSize) {
		// Render proc won't be ready on first boot
		// Totally skip mouse handling for click through overlays, as well
		if (_overlayConfig.ClickThrough) return;
		var io = ImGui.GetIO();
		var displayMousePos = io.MousePos - imagePos;
		// Generally we want to use IsWindowHovered for hit checking, as it takes z-stacking into account -
		// but when cursor isn't being actively captured, imgui will always return false - so fall back
		// so a slightly more naive hover check, just to maintain a bit of flood prevention.
		// TODO: Need to test how this will handle overlaps... fully transparent _shouldn't_ be accepting
		//       clicks so shouuulllddd beee fineee???
		var hovered = ImGui.IsMouseHoveringRect(imagePos, imagePos + imageSize);
		// manage focus
		var down = EncodeMouseButtons(io.MouseClicked);
		var double_ = EncodeMouseButtons(io.MouseDoubleClicked);
		var up = EncodeMouseButtons(io.MouseReleased);
		var wheelX = io.MouseWheelH;
		var wheelY = io.MouseWheel;
		if (down.HasFlag(MouseButton.Primary) || down.HasFlag(MouseButton.Secondary) || down.HasFlag(MouseButton.Tertiary))
			_windowFocused = hovered;
		// If the cursor is outside the window, send a final mouse leave then noop
		if (!hovered) {
			if (_mouseInWindow) {
				_mouseInWindow = false;
				_ = _renderProcess.Rpc?.MouseButton(new MouseButtonMessage {
					Guid = RenderGuid.ToByteArray(),
					X = displayMousePos.X,
					Y = displayMousePos.Y,
					Leaving = true
				});
			}
			return;
		}

		Vector2 mousePos = new(
			Math.Clamp(displayMousePos.X, 0, Math.Max(0, imageSize.X - 1)),
			Math.Clamp(displayMousePos.Y, 0, Math.Max(0, imageSize.Y - 1)));
		_mouseInWindow = true;
		ImGui.SetMouseCursor(_cursor);
		// If the event boils down to no change, bail before sending
		if (io.MouseDelta == Vector2.Zero && down == MouseButton.None && double_ == MouseButton.None && up == MouseButton.None && wheelX == 0 && wheelY == 0) return;
		var modifier = InputModifier.None;
		if (io.KeyShift) modifier |= InputModifier.Shift;
		if (io.KeyCtrl) modifier |= InputModifier.Control;
		if (io.KeyAlt) modifier |= InputModifier.Alt;
		// TODO: Either this or the entire handler function should be asynchronous so we're not blocking the entire draw thread
		_ = _renderProcess.Rpc?.MouseButton(new MouseButtonMessage {
			Guid = RenderGuid.ToByteArray(),
			X = mousePos.X,
			Y = mousePos.Y,
			Down = down,
			Double = double_,
			Up = up,
			WheelX = wheelX,
			WheelY = wheelY,
			Modifier = modifier
		});
	}

	private void HandleWindowSize() {
		var currentSize = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();
		if (currentSize == _size || _resizing) { return; }
		if (_size == Vector2.Zero) {
			_ = _renderProcess.Rpc?.NewOverlay(new NewOverlayMessage {
				Guid = RenderGuid.ToByteArray(),
				Id = _overlayConfig.Name,
				Url = _overlayConfig.Url,
				Width = (int)currentSize.X,
				Height = (int)currentSize.Y,
				Zoom = _overlayConfig.Zoom,
				Framerate = _overlayConfig.Framerate,
				Muted = _overlayConfig.Muted,
				CustomCss = _overlayConfig.CustomCss
			});
		} else _ = _renderProcess.Rpc?.ResizeOverlay(RenderGuid, (int)currentSize.X, (int)currentSize.Y);
		_resizing = true;
		_size = currentSize;
	}

	#region serde

	private static MouseButton EncodeMouseButtons(Span<bool> buttons) {
		var result = MouseButton.None;
		if (buttons[0]) result |= MouseButton.Primary;
		if (buttons[1]) result |= MouseButton.Secondary;
		if (buttons[2]) result |= MouseButton.Tertiary;
		if (buttons[3]) result |= MouseButton.Fourth;
		if (buttons[4]) result |= MouseButton.Fifth;
		return result;
	}

	// ngl kinda disappointed at the lack of options here
	private static ImGuiMouseCursor DecodeCursor(Cursor cursor) => cursor switch {
		Cursor.Default => ImGuiMouseCursor.Arrow,
		Cursor.None => ImGuiMouseCursor.None,
		Cursor.Pointer => ImGuiMouseCursor.Hand,
		Cursor.Text or Cursor.VerticalText => ImGuiMouseCursor.TextInput,
		Cursor.NResize or Cursor.SResize or Cursor.NsResize => ImGuiMouseCursor.ResizeNs,
		Cursor.EResize or Cursor.WResize or Cursor.EwResize => ImGuiMouseCursor.ResizeEw,
		Cursor.NeResize or Cursor.SwResize or Cursor.NeswResize => ImGuiMouseCursor.ResizeNesw,
		Cursor.NwResize or Cursor.SeResize or Cursor.NwseResize => ImGuiMouseCursor.ResizeNwse,
		_ => ImGuiMouseCursor.Arrow
	};

	private bool HiddenByCombatFlags() {
		if (!_overlayConfig.HideOutOfCombat) return false;
		if (Plugin.ObjectTable.LocalPlayer == null) return true;
		if (Plugin.ObjectTable.LocalPlayer.StatusFlags.HasFlag(StatusFlags.InCombat)) {
			_timeLastInCombat = DateTimeOffset.Now.ToUnixTimeMilliseconds();
			return false;
		}
		if (!Plugin.ObjectTable.LocalPlayer.StatusFlags.HasFlag(StatusFlags.InCombat) && _overlayConfig.HideDelay > 0)
			return DateTimeOffset.Now.ToUnixTimeMilliseconds() >= _timeLastInCombat + _overlayConfig.HideDelay * 1000;
		return true;
	}

	#endregion
}