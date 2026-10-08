using System;
using System.Runtime.InteropServices;
using Browsingway.Common;

namespace Browsingway.Renderer;

internal static partial class NativeMouse {
	private const uint WM_MOUSEMOVE = 0x0200;
	private const uint WM_LBUTTONDOWN = 0x0201;
	private const uint WM_LBUTTONUP = 0x0202;
	private const uint WM_LBUTTONDBLCLK = 0x0203;
	private const uint WM_RBUTTONDOWN = 0x0204;
	private const uint WM_RBUTTONUP = 0x0205;
	private const uint WM_RBUTTONDBLCLK = 0x0206;
	private const uint WM_MBUTTONDOWN = 0x0207;
	private const uint WM_MBUTTONUP = 0x0208;
	private const uint WM_MBUTTONDBLCLK = 0x0209;
	private const uint WM_MOUSEWHEEL = 0x020A;
	private const uint WM_MOUSEHWHEEL = 0x020E;
	private const uint MK_LBUTTON = 0x0001;
	private const uint MK_RBUTTON = 0x0002;
	private const uint MK_MBUTTON = 0x0010;
	private const uint MK_SHIFT = 0x0004;
	private const uint MK_CONTROL = 0x0008;
	private const uint MK_ALT = 0x0020;

	[LibraryImport("user32.dll")]
	private static partial void SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool ClientToScreen(IntPtr hWnd, ref NativePoint point);

	public static void Move(IntPtr hwnd, int x, int y, InputModifier modifier) {
		var keyState = 0;
		if ((modifier & InputModifier.Shift) != 0) keyState |= (int)MK_SHIFT;
		if ((modifier & InputModifier.Control) != 0) keyState |= (int)MK_CONTROL;
		if ((modifier & InputModifier.Alt) != 0) keyState |= (int)MK_ALT;
		SendMessageW(hwnd, WM_MOUSEMOVE, keyState, PackPoint(x, y));
	}

	public static void Leave(IntPtr hwnd)
		=> SendMessageW(hwnd, WM_MOUSEMOVE, IntPtr.Zero, PackPoint(-1, -1));

	public static void SendButtons(IntPtr hwnd, int x, int y, MouseButtonMessage msg, MouseButton heldBefore) {
		var heldDuringEvent = heldBefore | msg.Down;
		var keyState = 0;
		if ((heldDuringEvent & MouseButton.Primary) != 0) keyState |= (int)MK_LBUTTON;
		if ((heldDuringEvent & MouseButton.Secondary) != 0) keyState |= (int)MK_RBUTTON;
		if ((heldDuringEvent & MouseButton.Tertiary) != 0) keyState |= (int)MK_MBUTTON;
		if ((msg.Modifier & InputModifier.Shift) != 0) keyState |= (int)MK_SHIFT;
		if ((msg.Modifier & InputModifier.Control) != 0) keyState |= (int)MK_CONTROL;
		if ((msg.Modifier & InputModifier.Alt) != 0) keyState |= (int)MK_ALT;
		var point = PackPoint(x, y);
		SendButton(hwnd, point, MouseButton.Primary, WM_LBUTTONDOWN, WM_LBUTTONUP, WM_LBUTTONDBLCLK, keyState, msg);
		SendButton(hwnd, point, MouseButton.Secondary, WM_RBUTTONDOWN, WM_RBUTTONUP, WM_RBUTTONDBLCLK, keyState, msg);
		SendButton(hwnd, point, MouseButton.Tertiary, WM_MBUTTONDOWN, WM_MBUTTONUP, WM_MBUTTONDBLCLK, keyState, msg);
		var screenPoint = new NativePoint { X = x, Y = y };
		if (ClientToScreen(hwnd, ref screenPoint)) {
			var screenPos = PackPoint(screenPoint.X, screenPoint.Y);
			var wheelState = keyState;
			if ((msg.Up & MouseButton.Primary) != 0) wheelState &= ~(int)MK_LBUTTON;
			if ((msg.Up & MouseButton.Secondary) != 0) wheelState &= ~(int)MK_RBUTTON;
			if ((msg.Up & MouseButton.Tertiary) != 0) wheelState &= ~(int)MK_MBUTTON;
			if (msg.WheelY != 0)
				SendMessageW(hwnd, WM_MOUSEWHEEL, (WheelDelta(msg.WheelY) & 0xFFFF) << 16 | wheelState & 0xFFFF, screenPos);
			if (msg.WheelX != 0)
				SendMessageW(hwnd, WM_MOUSEHWHEEL, (WheelDelta(msg.WheelX) & 0xFFFF) << 16 | wheelState & 0xFFFF, screenPos);
		}
	}

	private static void SendButton(IntPtr hwnd, IntPtr point, MouseButton button,
		uint down, uint up, uint doubleClick, int keyState, MouseButtonMessage msg) {
		if ((msg.Down & button) != 0)
			SendMessageW(hwnd, (msg.Double & button) != 0 ? doubleClick : down, keyState, point);
		if ((msg.Up & button) != 0)
			SendMessageW(hwnd, up, (nint)(keyState & ~(button switch {
				MouseButton.Primary => MK_LBUTTON,
				MouseButton.Secondary => MK_RBUTTON,
				_ => MK_MBUTTON
			})), point);
	}

	private static int WheelDelta(float value)
		=> Math.Clamp((int)Math.Round(value * 100), short.MinValue, short.MaxValue) & 0xFFFF;

	private static IntPtr PackPoint(int x, int y) => (y & 0xFFFF) << 16 | x & 0xFFFF;

	[StructLayout(LayoutKind.Sequential)]
	private struct NativePoint {
		public int X;
		public int Y;
	}
}

internal static partial class NativeKeyboard {
	private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102;

	[LibraryImport("user32.dll")]
	private static partial void SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	public static void Send(IntPtr hwnd, KeyEventMessage msg)
		=> SendMessage(hwnd, msg.Msg switch {
			0x0102 or 0x0106 => WM_CHAR,
			0x0101 or 0x0105 => WM_KEYUP,
			_ => WM_KEYDOWN
		}, msg.WParam, msg.LParam);
}