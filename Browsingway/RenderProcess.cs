using System.Diagnostics;
using Browsingway.Common;
using Dalamud.Plugin.Services;

namespace Browsingway;

internal class RenderProcess : IDisposable {
	public event EventHandler? Crashed;
	public BrowsingwayRpc? Rpc { get; private set; }

	private readonly string _configDir;
	private readonly string _ipcChannelName;

	private readonly string _keepAliveHandleName;
	private readonly int _parentPid;
	private readonly string _pluginDir;

	private DateTime _lastRenderCheck = DateTime.MinValue;
	private uint _restartCount;

	private const uint _maxRestarts = 5;
	private const uint _checkDelaySeconds = 1;
	private const uint _processOkAfterSeconds = 5;

	private Process _process;
	private bool _running;

	public RenderProcess(int pid, string pluginDir, string configDir) {
		_keepAliveHandleName = $"BrowsingwayRendererKeepAlive{pid}";
		_ipcChannelName = $"BrowsingwayRendererIpcChannel{pid}";
		_pluginDir = pluginDir;
		_configDir = configDir;
		_parentPid = pid;
		Rpc = new BrowsingwayRpc(_ipcChannelName);
		_process = SetupProcess();
	}

	public void Dispose() {
		Stop();
		_process.Dispose();
		Rpc?.Dispose();
	}

	public void Start() {
		if (_running) return;
		_process.Start();
		_process.BeginOutputReadLine();
		_process.BeginErrorReadLine();
		_running = true;
	}

	private int _restarting; // This needs to be a numeric type for Interlocked.Exchange

	public void EnsureRenderProcessIsAlive() {
		if (!_running) return;
		// only check every second, reduces stress on the render thread
		if (DateTime.Now - _lastRenderCheck < TimeSpan.FromSeconds(_checkDelaySeconds)) return;
		_lastRenderCheck = DateTime.Now;
		if (!HasProcessExited()) {
			// process is still running, reset restart counter if it ran for at least 5 seconds
			if (_restartCount > 0 && DateTime.Now - _process.StartTime > TimeSpan.FromSeconds(_processOkAfterSeconds))
				_restartCount = 0;
			return;
		}
		if (_restartCount >= _maxRestarts) {
			Plugin.PluginLog.Error("Render process is crashing in a loop - please check the logs. No further restarts will be attempted until Browsingway is restarted.");
			Stop();
			Rpc?.Dispose();
			Rpc = null;
			OnProcessCrashed();
			return;
		}
		Task.Run(() => {
			if (_hasExited && 0 == Interlocked.Exchange(ref _restarting, 1)) {
				try {
					// process crashed, restart
					_restartCount++;
					Plugin.PluginLog.Error($"Render process crashed - will restart asap (attempt {_restartCount}/{_maxRestarts}).");
					_process = SetupProcess();
					_process.Start();
					_process.BeginOutputReadLine();
					_process.BeginErrorReadLine();

					// notify everyone that we have to reinit
					OnProcessCrashed();

					// reset the process exit flag
					_hasExited = false;
				} catch (Exception e) {
					Plugin.PluginLog.Error(e, "Failed to restart render process");
				} finally {
					Interlocked.Exchange(ref _restarting, 0);
				}
			}
		});
	}

	private void Stop() {
		if (!_running) return;

		_running = false;

		// Grab the handle the process is waiting on and open it up
		EventWaitHandle handle = new(false, EventResetMode.ManualReset, _keepAliveHandleName);
		handle.Set();
		handle.Dispose();

		// Allow WebView2 and the capture loop to finish their UI-thread cleanup
		// before forcing the renderer down.
		if (!_process.WaitForExit(TimeSpan.FromSeconds(5))) {
			try {
				_process.Kill(true);
				_process.WaitForExit(TimeSpan.FromSeconds(2));
			} catch (InvalidOperationException) { }
		}
	}

	private bool _hasExited;
	private int _checkingExited; // This needs to be a numeric type for Interlocked.Exchange

	private bool HasProcessExited() {
		// Process.HasExited can be an expensive call (on some systems?), so it's
		// offloaded to a Task, here. This could be related to Riot's Vanguard
		// kernel anti-cheat. The performance bottleneck occurs in ntdll, so this
		// is difficult to isolate and debug.
		Task.Run(() => {
			if (!_hasExited && 0 == Interlocked.Exchange(ref _checkingExited, 1)) {
				try {
					_hasExited = _process.HasExited;
				} catch (Exception e) {
					Plugin.PluginLog.Error(e, "Failed to get process exit status");
				} finally {
					Interlocked.Exchange(ref _checkingExited, 0);
				}
			}
		});

		return _hasExited;
	}

	private Process SetupProcess() {
		RenderParams processArgs = new() {
			ParentPid = _parentPid,
			DalamudAssemblyDir = Path.GetDirectoryName(typeof(IPluginLog).Assembly.Location)!,
			WebView2UserDataDir = Path.Combine(_configDir, "webview2"),
			DxgiAdapterLuidLow = DxHandler.AdapterLuid.LowPart,
			DxgiAdapterLuidHigh = DxHandler.AdapterLuid.HighPart,
			KeepAliveHandleName = _keepAliveHandleName,
			IpcChannelName = _ipcChannelName
		};

		Process process = new();
		process.StartInfo = new ProcessStartInfo {
			FileName = Path.Combine(_pluginDir, "renderer", "Browsingway.Renderer.exe"),
			Arguments = RenderParamsSerializer.Serialize(processArgs),
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		process.OutputDataReceived += (_, args) => Plugin.PluginLog.Info($"[Render]: {args.Data}");
		process.ErrorDataReceived += (_, args) => {
			if(string.IsNullOrEmpty(args.Data))	Plugin.PluginLog.Warning($"[Render]: {args.Data}");
			else Plugin.PluginLog.Error($"[Render]: {args.Data}");
		};
		return process;
	}

	private void OnProcessCrashed() {
		Crashed?.Invoke(this, EventArgs.Empty);
	}
}