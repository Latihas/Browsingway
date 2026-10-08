using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Browsingway.Renderer;

internal static partial class NativeCapture {
	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool IsWindow(IntPtr hWnd);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool GetClientRect(IntPtr hWnd, out NativeRect rect);

	private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial void EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);

	public static IntPtr FindWebViewWindow(IntPtr parent) {
		if (parent == IntPtr.Zero) return IntPtr.Zero;
		var best = IntPtr.Zero;
		var bestRank = 0;
		long bestArea = 0;
		EnumChildWindows(parent, (child, __) => {
			var className = new StringBuilder(256);
			_ = GetClassName(child, className, className.Capacity);
			var name = className.ToString();
			var rank = name.StartsWith("Chrome_RenderWidgetHostHWND", StringComparison.Ordinal)
				? 2
				: name.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal)
					? 1
					: 0;
			if (rank > 0 && GetClientRect(child, out var rect)) {
				var area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
				if (IsWindowVisible(child) &&
				    (rank > bestRank || rank == bestRank && area > bestArea)) {
					best = child;
					bestRank = rank;
					bestArea = area;
				}
			}
			return true;
		}, IntPtr.Zero);

		return best;
	}

	public static bool IsValidWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd);

	public static Size GetClientSize(IntPtr hwnd) {
		if (!IsValidWindow(hwnd) || !GetClientRect(hwnd, out var rect))
			return Size.Empty;

		return new Size(Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool IsWindowVisible(IntPtr hWnd);

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeRect {
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}
}