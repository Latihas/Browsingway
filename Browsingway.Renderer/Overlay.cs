using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Browsingway.Common;
using Microsoft.Web.WebView2.Core;
using Size = System.Drawing.Size;
using Cursor = Browsingway.Common.Cursor;

namespace Browsingway.Renderer;

internal sealed class Overlay(
	Guid id, string url, float zoom, bool muted, int framerate, string customCss,
	TextureRenderHandler renderHandler)
	: IDisposable {
	private readonly string _profileName = $"overlay-{id:N}";
	private readonly int _framerate = Math.Clamp(framerate, 1, 300);
	private CoreWebView2? _webView;
	private CoreWebView2Controller? _controller;
	private CoreWebView2CompositionController? _compositionController;
	private Form? _form;
	private string _url = url;
	private float _zoom = zoom;
	private bool _muted = muted;
	private string _customCss = customCss;
	private volatile bool _disposed;
	private bool _ready;
	private bool _captureSourceLogged;
	private bool _captureFailureLogged;
	private bool _devToolsReady;
	private bool _previewCaptureUnavailable;
	private IntPtr _contentWindow;
	private bool _contentLoaded;
	private MouseButton _heldMouseButtons;
	private CancellationTokenSource? _captureCancellation;
	private Task? _captureLoop;
	private readonly SemaphoreSlim _captureGate = new(1, 1);
	private int _disposeStarted;

	public TextureRenderHandler RenderHandler { get; } = renderHandler;

	public void Initialise() {
		WebView2Handler.Invoke(InitialiseOnUiThread);
	}

	private void InitialiseOnUiThread() {
		_form = new BrowserForm {
			AutoScaleMode = AutoScaleMode.None,
			ShowInTaskbar = false,
			FormBorderStyle = FormBorderStyle.None,
			StartPosition = FormStartPosition.Manual,
			Location = new Point(-32000, -32000),
			ClientSize = new Size(Math.Max(1, RenderHandler.Width), Math.Max(1, RenderHandler.Height))
		};
		_form.Show();
		_ = InitialiseWebViewAsync();
	}

	private async Task InitialiseWebViewAsync() {
		try {
			var environment = await WebView2Handler.CreateEnvironmentAsync();
			if (_disposed || _form is null)
				return;

			var controllerOptions =
				environment.CreateCoreWebView2ControllerOptions();
			controllerOptions.ProfileName = _profileName;

			_controller = await environment.CreateCoreWebView2ControllerAsync(
				_form.Handle,
				controllerOptions);
			if (_disposed) {
				_controller.Close();
				_controller = null;
				return;
			}

			_controller.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
			_controller.ShouldDetectMonitorScaleChanges = false;
			_controller.RasterizationScale = 1.0;
			_controller.Bounds = new Rectangle(
				Point.Empty,
				new Size(Math.Max(1, RenderHandler.Width), Math.Max(1, RenderHandler.Height)));
			_controller.DefaultBackgroundColor = Color.Transparent;
			_controller.ZoomFactor = ScaleZoom(_zoom);
			_controller.IsVisible = true;
			_compositionController = await environment.CreateCoreWebView2CompositionControllerAsync(
				_form.Handle,
				controllerOptions);
			if (_disposed) {
				_compositionController.Close();
				_compositionController = null;
				_controller.Close();
				_controller = null;
				return;
			}

			_compositionController.CursorChanged += (_, _) =>
				RenderHandler.SetCursor(EncodeCursor(_compositionController.SystemCursorId));
			_webView = _controller.CoreWebView2;
			_webView.IsMuted = _muted;
			_webView.Settings.AreDefaultContextMenusEnabled = false;
			_webView.Settings.IsStatusBarEnabled = false;
			_webView.NavigationStarting += (_, _) => {
				if (_disposed)
					return;

				_contentLoaded = false;
				_devToolsReady = false;
				_previewCaptureUnavailable = false;
				_captureFailureLogged = false;
			};
			_webView.ContentLoading += (_, _) => {
				if (!_disposed)
					_contentLoaded = false;
			};
			_webView.NavigationCompleted += async (_, args) => {
				if (_disposed)
					return;

				if (!args.IsSuccess) {
					await Console.Error.WriteLineAsync($"WebView2 navigation failed: url={_webView.Source}, status={args.WebErrorStatus}");
					return;
				}

				_contentLoaded = true;
				try {
					await InjectUserCssAsync(_customCss);
				} catch (Exception ex) {
					if (!_disposed)
						await Console.Error.WriteLineAsync($"WebView2 CSS injection failed: {ex}");
				}

				await WaitForFirstPaintAsync();
				if (_disposed)
					return;

				StartCaptureLoop();
				Console.WriteLine(
					$"WebView2 navigation completed: source={_webView.Source}, " +
					$"controller={_controller.Bounds.Width}x{_controller.Bounds.Height}, " +
					$"visible={_controller.IsVisible}, zoom={_controller.ZoomFactor:0.###}, " +
					$"rasterization={_controller.RasterizationScale:0.###}");
			};
			_webView.ProcessFailed += (_, args) =>
				Console.Error.WriteLine($"WebView2 process failed: {args.ProcessFailedKind}");

			_webView.Navigate(_url);
			Console.WriteLine($"WebView2 initialized: {_url} ({RenderHandler.Width}x{RenderHandler.Height})");
		} catch (Exception ex) {
			if (!_disposed)
				await Console.Error.WriteLineAsync($"WebView2 initialization failed: {ex}");
		}
	}

	private void StartCaptureLoop() {
		if (_captureLoop is not null || _disposed)
			return;

		_ready = true;
		_captureCancellation = new CancellationTokenSource();
		_captureLoop = CaptureLoopAsync(_captureCancellation.Token);
	}

	private async Task CaptureLoopAsync(CancellationToken cancellationToken) {
		while (!cancellationToken.IsCancellationRequested && !_disposed) {
			CapturedFrame? frame = null;
			Bitmap? bitmap = null;
			try {
				if (!_ready || !_contentLoaded || _webView is null) {
					await Task.Delay(32, cancellationToken);
					continue;
				}

				if (!await _captureGate.WaitAsync(0, cancellationToken)) {
					await Task.Delay(1, cancellationToken);
					continue;
				}

				try {
					frame = await CaptureOnUiThreadAsync(cancellationToken);
				} finally {
					_captureGate.Release();
				}

				if (frame is not null && !_disposed && !cancellationToken.IsCancellationRequested) {
					bitmap = frame.TakeBitmap();
					if (bitmap is not null &&
					    !_disposed &&
					    !cancellationToken.IsCancellationRequested) {
						RenderHandler.Update(bitmap);
						if (!_captureSourceLogged) {
							_captureSourceLogged = true;
							Console.WriteLine(
								$"WebView2 first frame: source={frame.Source}, texture={RenderHandler.Width}x{RenderHandler.Height}, " +
								$"capture={bitmap.Width}x{bitmap.Height}, bytes={frame.ByteCount}");
						}
					}

					bitmap?.Dispose();
					bitmap = null;
				}
			} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
				break;
			} catch (COMException ex) when (ex.HResult == unchecked((int)0x8007139F)) {
				// WebView2 can briefly reject a preview while a navigation or
				// renderer transition is in progress. Keep the loop alive.
			} catch (InvalidOperationException ex) when (IsInvalidCaptureState(ex)) {
				// The managed WebView2 wrapper reports the same native state
				// error as InvalidOperationException in some runtime builds.
			} catch (Exception ex) {
				if (_disposed || cancellationToken.IsCancellationRequested)
					break;

				if (!_captureFailureLogged) {
					_captureFailureLogged = true;
					await Console.Error.WriteLineAsync($"WebView2 capture retry: {ex}");
				}
			} finally {
				// The bitmap is atomically transferred to this scope before the
				// texture update. CapturedFrame.Dispose is then only a fallback.
				bitmap?.Dispose();
				frame?.Dispose();
			}

			try {
				await Task.Delay(Math.Max(16, 1000 / _framerate), cancellationToken);
			} catch (OperationCanceledException) {
				break;
			}
		}
	}

	private Task<CapturedFrame?> CaptureOnUiThreadAsync(CancellationToken cancellationToken) {
		return WebView2Handler.InvokeAsync(async () => {
			if (_disposed || !_contentLoaded || _webView is null)
				return null;

			cancellationToken.ThrowIfCancellationRequested();

			CapturedFrame? previewFrame = null;

			try {
				if (!_previewCaptureUnavailable) {
					try {
						previewFrame = await CapturePreviewAsync(cancellationToken);
						if (previewFrame is not null && !LooksLikePlaceholderFrame(previewFrame.Bitmap)) {
							var result = previewFrame;
							previewFrame = null;
							return result;
						}
					} catch (Exception ex) when (!_disposed) {
						_previewCaptureUnavailable = true;
						if (!_captureFailureLogged) {
							_captureFailureLogged = true;
							await Console.Error.WriteLineAsync($"WebView2 preview capture unavailable: {ex}");
						}
					}
				}

				if (previewFrame is not null) {
					var result = previewFrame;
					previewFrame = null;
					return result;
				}

				try {
					return await CaptureDevToolsScreenshotAsync(cancellationToken);
				} catch (Exception ex) when (!_disposed) {
					if (!_captureFailureLogged) {
						_captureFailureLogged = true;
						await Console.Error.WriteLineAsync($"WebView2 screenshot capture unavailable: {ex}");
					}
					return null;
				}
			} finally {
				previewFrame?.Dispose();
			}
		});
	}

	private async Task WaitForFirstPaintAsync() {
		if (_webView is null || _disposed)
			return;

		const string script = """
		                      (()=> {
		                      	const html = document.documentElement;
		                      	const body = document.body;
		                      	return {
		                      		readyState: document.readyState,
		                      		innerWidth: window.innerWidth,
		                      		innerHeight: window.innerHeight,
		                      		htmlWidth: html?.scrollWidth ?? 0,
		                      		htmlHeight: html?.scrollHeight ?? 0,
		                      		bodyTextLength: body?.innerText?.length ?? 0,
		                      		bodyChildren: body?.children?.length ?? 0
		                      	};
		                      })()
		                      """;

		var lastState = string.Empty;
		for (var attempt = 0; attempt < 20 && !_disposed; attempt++) {
			try {
				lastState = await _webView.ExecuteScriptAsync(script);
				using var document = JsonDocument.Parse(lastState);
				var state = document.RootElement;
				var readyState = state.GetProperty("readyState").GetString() ?? string.Empty;
				var innerWidth = state.GetProperty("innerWidth").GetInt32();
				var innerHeight = state.GetProperty("innerHeight").GetInt32();
				if (readyState is "interactive" or "complete" &&
				    innerWidth > 0 &&
				    innerHeight > 0) {
					Console.WriteLine(
						$"WebView2 page ready: state={readyState}, viewport={innerWidth}x{innerHeight}, " +
						$"dom={lastState}");
					return;
				}
			} catch (Exception ex) when (!_disposed) {
				if (attempt == 19)
					await Console.Error.WriteLineAsync($"WebView2 page state unavailable: {ex.Message}");
			}

			await Task.Delay(50);
		}

		if (!string.IsNullOrEmpty(lastState))
			Console.WriteLine($"WebView2 page state timeout: {lastState}");
	}

	private static bool LooksLikePlaceholderFrame(Bitmap bitmap) {
		if (bitmap.Width <= 1 || bitmap.Height <= 1) return true;
		var first = bitmap.GetPixel(0, 0);
		var stepX = Math.Max(1, bitmap.Width / 8);
		var stepY = Math.Max(1, bitmap.Height / 8);
		var hasDifferentPixel = false;
		for (var y = 0; y < bitmap.Height; y += stepY) {
			for (var x = 0; x < bitmap.Width; x += stepX) {
				if (bitmap.GetPixel(x, y) == first) continue;
				hasDifferentPixel = true;
				break;
			}
			if (hasDifferentPixel) break;
		}

		if (hasDifferentPixel) return false;

		// CapturePreview can return a fully transparent surface while a hidden
		// controller is still waiting for its first compositor frame. A solid
		// white surface is the other common placeholder from an unpainted page.
		return first.A == 0 || first is { R: >= 248, G: >= 248, B: >= 248, A: >= 248 };
	}

	private async Task<CapturedFrame?> CaptureDevToolsScreenshotAsync(CancellationToken cancellationToken) {
		if (_webView is null || _disposed)
			return null;

		if (!_devToolsReady) {
			await _webView.CallDevToolsProtocolMethodAsync("Page.enable", "{}");
			_devToolsReady = true;
		}

		cancellationToken.ThrowIfCancellationRequested();
		var response = await _webView.CallDevToolsProtocolMethodAsync(
			"Page.captureScreenshot",
			"""{"format":"png","fromSurface":true,"captureBeyondViewport":false,"omitBackground":false}""");
		cancellationToken.ThrowIfCancellationRequested();

		using var document = JsonDocument.Parse(response);
		if (!document.RootElement.TryGetProperty("data", out var dataElement))
			return null;

		var encoded = dataElement.GetString();
		if (string.IsNullOrEmpty(encoded))
			return null;

		var bytes = Convert.FromBase64String(encoded);
		return CreateCapturedFrame(bytes, "devtools");
	}

	private async Task<CapturedFrame?> CapturePreviewAsync(CancellationToken cancellationToken) {
		if (_webView is null || _disposed)
			return null;

		using var stream = new MemoryStream();
		await _webView.CapturePreviewAsync(
			CoreWebView2CapturePreviewImageFormat.Png,
			stream);
		cancellationToken.ThrowIfCancellationRequested();

		if (_disposed || stream.Length == 0)
			return null;

		return CreateCapturedFrame(stream.ToArray(), "preview");
	}

	private static CapturedFrame? CreateCapturedFrame(byte[] bytes, string source) {
		if (bytes.Length == 0)
			return null;

		using var stream = new MemoryStream(bytes, false);
		using var captured = new Bitmap(stream);
		return new CapturedFrame(new Bitmap(captured), source, bytes.Length);
	}

	private static bool IsInvalidCaptureState(Exception exception) {
		for (var current = exception; current is not null; current = current.InnerException)
			if (current is COMException { HResult: unchecked((int)0x8007139F) })
				return true;
		return false;
	}

	public void InjectUserCss(string css) {
		if (css.Length == 0 && _customCss.Length == 0)
			return;

		_customCss = css;
		WebView2Handler.Invoke(() => _ = InjectUserCssAsync(css));
	}

	private async Task InjectUserCssAsync(string css) {
		if (_webView is null) return;
		var encoded = JsonSerializer.Serialize(css);
		await _webView.ExecuteScriptAsync(
			$"(()=>{{const s=document.getElementById('user-css')||document.createElement('style');s.id='user-css';s.textContent={encoded};document.head.append(s)}})()");
	}

	public void Navigate(string url) {
		_url = url;
		WebView2Handler.Invoke(() => {
			if (_webView is null) return;
			if (_webView.Source == url) _webView.Reload();
			else _webView.Navigate(url);
		});
	}

	public void Zoom(float zoom) {
		_zoom = zoom;
		WebView2Handler.Invoke(() => {
			if (_controller is not null && !_disposed)
				_controller.ZoomFactor = ScaleZoom(zoom);
		});
	}

	public void Mute(bool mute) {
		_muted = mute;
		WebView2Handler.Invoke(() => {
			if (_webView is not null && !_disposed)
				_webView.IsMuted = mute;
		});
	}

	public void Debug() => WebView2Handler.Invoke(() => _webView?.OpenDevToolsWindow());

	public void HandleMouseEvent(MouseButtonMessage msg) {
		var viewPoint = new Point(
			(int)Math.Round(msg.X),
			(int)Math.Round(msg.Y));
		RenderHandler.SetMousePosition(viewPoint.X, viewPoint.Y);
		if (msg.Leaving) {
			WebView2Handler.Invoke(() => {
				if (_disposed) return;
				if (!NativeCapture.IsValidWindow(_contentWindow) && _form is not null)
					_contentWindow = NativeCapture.FindWebViewWindow(_form.Handle);
				if (NativeCapture.IsValidWindow(_contentWindow))
					NativeMouse.Leave(_contentWindow);
				_heldMouseButtons = MouseButton.None;
			});
			return;
		}

		WebView2Handler.Invoke(() => {
			if (_controller is null || _form is null || _disposed) return;
			if (!NativeCapture.IsValidWindow(_contentWindow))
				_contentWindow = NativeCapture.FindWebViewWindow(_form.Handle);
			if (!NativeCapture.IsValidWindow(_contentWindow))
				return;

			var inputSize = NativeCapture.GetClientSize(_contentWindow);
			var targetWidth = Math.Max(1, inputSize.Width);
			var targetHeight = Math.Max(1, inputSize.Height);
			var x = Math.Clamp(
				(int)Math.Round(viewPoint.X * targetWidth / (double)Math.Max(1, RenderHandler.Width)),
				0,
				targetWidth - 1);
			var y = Math.Clamp(
				(int)Math.Round(viewPoint.Y * targetHeight / (double)Math.Max(1, RenderHandler.Height)),
				0,
				targetHeight - 1);

			NativeMouse.Move(_contentWindow, x, y, msg.Modifier);
			NativeMouse.SendButtons(_contentWindow, x, y, msg, _heldMouseButtons);
			_heldMouseButtons = (_heldMouseButtons | msg.Down) & ~msg.Up;
		});
	}

	public void HandleKeyEvent(KeyEventMessage request) {
		WebView2Handler.Invoke(() => {
			if (_controller is null || _form is null || _disposed) return;
			if (!NativeCapture.IsValidWindow(_contentWindow))
				_contentWindow = NativeCapture.FindWebViewWindow(_form.Handle);
			if (!NativeCapture.IsValidWindow(_contentWindow))
				return;

			NativeKeyboard.Send(_contentWindow, request);
		});
	}

	public void Resize(Size size) {
		RenderHandler.Resize(size);
		WebView2Handler.Invoke(() => {
			_form?.ClientSize = size;
			if (_controller is null || _disposed) return;
			_controller.Bounds = new Rectangle(Point.Empty, size);
			_contentWindow = IntPtr.Zero;
		});
	}

	public void Dispose() {
		if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
		_disposed = true;
		_ready = false;
		_contentLoaded = false;
		_devToolsReady = false;
		_captureCancellation?.Cancel();
		FinishDisposeAsync(_captureLoop ?? Task.CompletedTask).GetAwaiter().GetResult();
	}

	private async Task FinishDisposeAsync(Task captureLoop) {
		try {
			await captureLoop.ConfigureAwait(false);
		} catch (Exception ex) {
			if (!_disposed)
				await Console.Error.WriteLineAsync($"WebView2 capture shutdown failed: {ex}");
		}

		try {
			await WebView2Handler.InvokeAsync(async () => {
				_compositionController?.Close();
				_controller?.Close();
				_form?.Close();
				_form?.Dispose();
				_webView = null;
				_compositionController = null;
				_controller = null;
				_form = null;
				await Task.CompletedTask;
				return true;
			}).ConfigureAwait(false);
		} catch (Exception ex) {
			if (!_disposed)
				await Console.Error.WriteLineAsync($"WebView2 control shutdown failed: {ex}");
		} finally {
			RenderHandler.Dispose();
			_captureCancellation?.Dispose();
			_captureGate.Dispose();
		}
	}

	private static double ScaleZoom(float zoom) => Math.Clamp(zoom / 100f, 0.25, 5);

	private static Cursor EncodeCursor(uint cursor) => cursor switch {
		0 => Cursor.Default,
		1 => Cursor.Crosshair,
		2 => Cursor.Text,
		3 => Cursor.Wait,
		4 => Cursor.Pointer,
		5 => Cursor.NResize,
		6 => Cursor.SResize,
		7 => Cursor.WResize,
		8 => Cursor.EResize,
		9 => Cursor.NwResize,
		10 => Cursor.NeResize,
		11 => Cursor.SwResize,
		12 => Cursor.SeResize,
		13 => Cursor.NsResize,
		14 => Cursor.EwResize,
		15 => Cursor.NeswResize,
		16 => Cursor.NwseResize,
		17 => Cursor.ColResize,
		18 => Cursor.RowResize,
		19 => Cursor.Move,
		20 => Cursor.NotAllowed,
		21 => Cursor.Help,
		22 => Cursor.Progress,
		23 => Cursor.None,
		24 => Cursor.ContextMenu,
		25 => Cursor.Cell,
		26 => Cursor.VerticalText,
		27 => Cursor.Alias,
		28 => Cursor.Copy,
		29 => Cursor.NoDrop,
		30 => Cursor.AllScroll,
		31 => Cursor.ZoomIn,
		32 => Cursor.ZoomOut,
		33 => Cursor.Grab,
		34 => Cursor.Grabbing,
		_ => Cursor.Default
	};

	private sealed class BrowserForm : Form {
		private const int WsExAppWindow = 0x00040000;
		private const int WsExNoActivate = 0x08000000;
		private const int WsExToolWindow = 0x00000080;

		protected override bool ShowWithoutActivation => true;

		protected override CreateParams CreateParams {
			get {
				var parameters = base.CreateParams;
				parameters.ExStyle &= ~WsExAppWindow;
				parameters.ExStyle |= WsExNoActivate | WsExToolWindow;
				return parameters;
			}
		}
	}

	private sealed class CapturedFrame(Bitmap bitmap, string source, int byteCount) : IDisposable {
		private Bitmap? _bitmap = bitmap;

		public Bitmap Bitmap =>
			Volatile.Read(ref _bitmap) ??
			throw new ObjectDisposedException(nameof(CapturedFrame));
		public string Source { get; } = source;
		public int ByteCount { get; } = byteCount;

		public Bitmap? TakeBitmap() => Interlocked.Exchange(ref _bitmap, null);

		public void Dispose() => Interlocked.Exchange(ref _bitmap, null)?.Dispose();
	}
}
