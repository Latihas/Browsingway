using System.Diagnostics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Browsingway;

public sealed class ActHandler(IDalamudPluginInterface pluginInterface) {
	private bool IsRunning { get; set; }
	public event EventHandler<bool>? AvailabilityChanged;
	private int _ticksSinceCheck = 2000;
	private int _notify = -1;
	private readonly ICallGateSubscriber<bool> _iinactIpc = pluginInterface.GetIpcSubscriber<bool>("IINACT.Server.Listening");

	public void Check() {
		if (Interlocked.CompareExchange(ref _notify, -1, 1) == 1)
			OnAvailabilityChanged(true);
		else if (Interlocked.CompareExchange(ref _notify, -1, 0) == 0)
			OnAvailabilityChanged(false);

		if (_ticksSinceCheck < 2000) {
			_ticksSinceCheck++;
			return;
		}
		_ticksSinceCheck = 0;

		try {
			if (_iinactIpc.InvokeFunc()) {
				if (!IsRunning) {
					IsRunning = true;
					Interlocked.Exchange(ref _notify, 1);
				}

				return;
			}

			if (IsRunning) {
				IsRunning = false;
				Interlocked.Exchange(ref _notify, 0);
			}

			return;
		} catch {
			// ignored
		}

		Task.Run(() => {
			var proc = Process.GetProcessesByName("Advanced Combat Tracker").FirstOrDefault();
			if (proc is not null) {
				// check if the main window is up and we aren't loading
				if (proc.MainWindowTitle.Contains("Advanced Combat Tracker") || (DateTime.Now - proc.StartTime).TotalSeconds >= 5) {
					if (!IsRunning) {
						IsRunning = true;
						Interlocked.Exchange(ref _notify, 1);
					}

					return;
				}
			} else {
				// check for IINACT
				proc = Process.GetProcessesByName("IINACT").FirstOrDefault();
				if (proc is not null && (DateTime.Now - proc.StartTime).TotalSeconds >= 5) {
					if (!IsRunning) {
						IsRunning = true;
						Interlocked.Exchange(ref _notify, 1);
					}

					return;
				}
			}

			if (IsRunning) {
				IsRunning = false;
				Interlocked.Exchange(ref _notify, 0);
			}
		});
	}

	private void OnAvailabilityChanged(bool e) {
		AvailabilityChanged?.Invoke(this, e);
	}
}