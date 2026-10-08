using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Browsingway.Renderer;

internal static class WebView2Handler {
	private static ApplicationContext? _context;
	private static Form? _messageWindow;
	private static Thread? _thread;
	private static TaskCompletionSource<bool>? _ready;
	private static string _userDataDir = null!;
	private static Task<CoreWebView2Environment>? _environmentTask;
	private static int _uiThreadId;

	public static void Initialise(string userDataDir) {
		_userDataDir = userDataDir;
		_ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_thread = new Thread(ThreadMain) { IsBackground = true };
		_thread.SetApartmentState(ApartmentState.STA);
		_thread.Start();
		_ready.Task.GetAwaiter().GetResult();
	}

	public static void Shutdown() {
		var context = _context;
		var messageWindow = _messageWindow;
		if (context is not null) {
			if (IsUiThread) {
				context.ExitThread();
			} else if (messageWindow?.IsHandleCreated == true) {
				try {
					messageWindow.BeginInvoke(context.ExitThread);
				} catch (InvalidOperationException) {
					context.ExitThread();
				}
			} else {
				context.ExitThread();
			}
		}

		_thread?.Join(TimeSpan.FromSeconds(5));
		_context = null;
		_messageWindow = null;
		_thread = null;
		_uiThreadId = 0;
		_environmentTask = null;
	}

	public static Task<CoreWebView2Environment> CreateEnvironmentAsync() =>
		_environmentTask ??= CreateEnvironmentCoreAsync();

	private static Task<CoreWebView2Environment> CreateEnvironmentCoreAsync() {
		var options = new CoreWebView2EnvironmentOptions {
			AdditionalBrowserArguments =
				"--autoplay-policy=no-user-gesture-required " +
				"--disable-backgrounding-occluded-windows " +
				"--disable-renderer-backgrounding " +
				"--disable-features=CalculateNativeWinOcclusion"
		};

		return CoreWebView2Environment.CreateAsync(null, _userDataDir, options);
	}

	public static void Invoke(Action action) {
		if (IsUiThread) {
			action();
			return;
		}

		var messageWindow = GetMessageWindow();
		messageWindow.BeginInvoke(action);
	}

	public static Task<T> InvokeAsync<T>(Func<Task<T>> action) {
		if (IsUiThread) return action();

		var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		try {
			GetMessageWindow().BeginInvoke(async void () => {
				try {
					completion.TrySetResult(await action().ConfigureAwait(true));
				} catch (Exception ex) {
					completion.TrySetException(ex);
				}
			});
		} catch (Exception ex) {
			completion.TrySetException(ex);
		}

		return completion.Task;
	}

	private static bool IsUiThread =>
		_uiThreadId != 0 && Environment.CurrentManagedThreadId == _uiThreadId;

	private static Form GetMessageWindow() {
		if (_context is null || _messageWindow?.IsHandleCreated != true)
			throw new InvalidOperationException("WebView2 message loop is not initialized.");

		return _messageWindow;
	}

	private static void ThreadMain() {
		try {
			_uiThreadId = Environment.CurrentManagedThreadId;
			Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
			_context = new ApplicationContext();
			_messageWindow = new MessageWindow {
				ShowInTaskbar = false,
				FormBorderStyle = FormBorderStyle.None,
				Opacity = 0,
				Width = 1,
				Height = 1
			};
			_context.MainForm = _messageWindow;
			_messageWindow.CreateControl();
			_ready!.SetResult(true);
			Application.Run(_context);
		} catch (Exception ex) {
			_ready!.SetException(ex);
		}
	}

	private sealed class MessageWindow : Form {
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
}
