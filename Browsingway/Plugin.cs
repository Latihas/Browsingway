using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Browsingway;

[SuppressMessage("ReSharper", "UnusedType.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public class Plugin : IDalamudPlugin {
	[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Local")]
	[PluginService] private static ICommandManager CommandManager { get; set; } = null!;
	[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Local")]
	[PluginService] internal static IChatGui Chat { get; private set; } = null!;
	[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Local")]
	[PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
	[PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
	[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Local")]
	[PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
	[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Local")]
	[PluginService] private static IFramework Framework { get; set; } = null!;
	[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
	[PluginService] internal static IClientState ClientState { get; set; } = null!;
	[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
	[PluginService] internal static IObjectTable ObjectTable { get; set; } = null!;
	private const string _command = "/bw";

	private readonly Dictionary<Guid, Overlay> _overlays = new();
	private readonly string _pluginConfigDir;
	private readonly string _pluginDir;

	private RenderProcess? _renderProcess;
	private readonly ActHandler _actHandler;
	private Settings? _settings;

	public Plugin(IDalamudPluginInterface pluginInterface) {
		// init services

		_pluginDir = pluginInterface.AssemblyLocation.DirectoryName ?? "";
		if (string.IsNullOrEmpty(_pluginDir)) {
			throw new Exception("Could not determine plugin directory");
		}

		_pluginConfigDir = pluginInterface.GetPluginConfigDirectory();

		_actHandler = new ActHandler(pluginInterface);

		InitialiseRuntime();

		// Hook up render hook
		pluginInterface.UiBuilder.Draw += Render;
	}

	public void Dispose() {
		foreach (var overlay in _overlays.Values) { overlay.Dispose(); }
		_overlays.Clear();
		_renderProcess?.Dispose();
		CommandManager.RemoveHandler(_command);
		WndProcHandler.Shutdown();
		DxHandler.Shutdown();
		GC.SuppressFinalize(this);
	}

	private void InitialiseRuntime() {
		// Spin up DX handling from the plugin interface
		DxHandler.Initialise(PluginInterface);

		// Spin up WndProc hook
		WndProcHandler.Initialise(DxHandler.WindowHandle);
		WndProcHandler.WndProcMessage += OnWndProc;

		// Boot the render process. This has to be done before initialising settings to prevent a
		// race condition overlays receiving a null reference.
		var pid = Environment.ProcessId;
		_renderProcess = new RenderProcess(pid, _pluginDir, _pluginConfigDir);
		_renderProcess.Rpc!.RendererReady += msg => {
			if (!msg.HasDxSharedTexturesSupport) {
				PluginLog.Error("Could not initialize shared textures transport. Browsingway will not work.");
				return;
			}

			Framework.RunOnFrameworkThread(() => {
				_settings?.HydrateOverlays();
			});
		};
		_renderProcess.Rpc.SetCursor += msg => {
			Framework.RunOnFrameworkThread(() => {
				Guid guid = new(msg.Guid);
				var overlay = _overlays.Values.FirstOrDefault(overlay => overlay.RenderGuid == guid);
				overlay?.SetCursor(msg.Cursor);
			});
		};
		_renderProcess.Rpc.UpdateTexture += msg => {
			Framework.RunOnFrameworkThread(() => {
				Guid guid = new(msg.Guid);
				if (_overlays.TryGetValue(guid, out var overlay)) {
					overlay.SetTexture((IntPtr)msg.TextureHandle);
				} else {
					PluginLog.Error("Overlay Id not found");
				}
			});
		};
		_renderProcess.Start();

		// Prep settings
		_settings = new Settings();
		if (_settings is not null) {
			_settings.OverlayAdded += OnOverlayAdded;
			_settings.OverlayNavigated += OnOverlayNavigated;
			_settings.OverlayDebugged += OnOverlayDebugged;
			_settings.OverlayRemoved += OnOverlayRemoved;
			_settings.OverlayZoomed += OnOverlayZoomed;
			_settings.OverlayMuted += OnOverlayMuted;
			_actHandler.AvailabilityChanged += OnActAvailabilityChanged;
			_settings.OverlayUserCssChanged += OnUserCssChanged;
		}

		// Hook up the main BW command
		CommandManager.AddHandler(_command,
			new CommandInfo(HandleCommand) { HelpMessage = "Control Browsingway from the chat line! Type '/bw config' or open the settings for more info.", ShowInHelp = true });
	}

	private (bool, long) OnWndProc(WindowsMessage msg, ulong wParam, long lParam) {
		// Notify all the overlays of the wndproc, respond with the first capturing response (if any)
		// TODO: Yeah this ain't great but realistically only one will capture at any one time for now.
		var responses = _overlays.Select(pair => pair.Value.WndProcMessage(msg, wParam, lParam));
		return responses.FirstOrDefault(pair => pair.Item1);
	}

	private void OnActAvailabilityChanged(object? sender, bool e) {
		_settings?.OnActAvailabilityChanged(e);
	}

	private void OnOverlayAdded(object? sender, InlayConfiguration overlayConfig) {
		if (_renderProcess is null || _settings is null) {
			return;
		}

		Overlay overlay = new(_renderProcess, overlayConfig, _pluginDir);
		_overlays.TryAdd(overlayConfig.Guid, overlay);
	}

	private void OnOverlayNavigated(object? sender, InlayConfiguration config) {
		if (_overlays.TryGetValue(config.Guid, out var overlay))
			overlay.Navigate(config.Url);
	}

	private void OnOverlayDebugged(object? sender, InlayConfiguration config) {
		if (_overlays.TryGetValue(config.Guid, out var overlay))
			overlay.Debug();
	}

	private void OnOverlayRemoved(object? sender, InlayConfiguration config) {
		if (_overlays.Remove(config.Guid, out var overlay)) {
			overlay.Dispose();
		}
	}

	private void OnOverlayZoomed(object? sender, InlayConfiguration config) {
		if (_overlays.TryGetValue(config.Guid, out var overlay))
			overlay.Zoom(config.Zoom);
	}

	private void OnOverlayMuted(object? sender, InlayConfiguration config) {
		if (_overlays.TryGetValue(config.Guid, out var overlay))
			overlay.Mute(config.Muted);
	}

	private void OnUserCssChanged(object? sender, InlayConfiguration config) {
		var overlay = _overlays[config.Guid];
		overlay.InjectUserCss(config.CustomCss);
	}

	private void Render() {
		_settings?.Render();

		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));

		_renderProcess?.EnsureRenderProcessIsAlive();
		_actHandler.Check();

		foreach (var overlay in _overlays.Values) { overlay.Render(); }

		ImGui.PopStyleVar();
	}

	private void HandleCommand(string command, string rawArgs) {
		// Docs complain about perf of multiple splits.
		// I'm not convinced this is a sufficiently perf-critical path to care.
		var args = rawArgs.Split(null as char[], 2, StringSplitOptions.RemoveEmptyEntries);

		if (args.Length == 0) {
			Chat.PrintError(
				"No subcommand specified. Valid subcommands are: config,overlay.");
			return;
		}

		var subcommandArgs = args.Length > 1 ? args[1] : "";

		switch (args[0]) {
			case "config":
				_settings?.HandleConfigCommand();
				break;
			case "inlay":
			case "overlay":
				_settings?.HandleOverlayCommand(subcommandArgs);
				break;
			default:
				Chat.PrintError(
					$"Unknown subcommand '{args[0]}'. Valid subcommands are: config,overlay,inlay.");
				break;
		}
	}
}