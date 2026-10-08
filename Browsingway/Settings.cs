using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Browsingway;

// ReSharper disable once ClassNeverInstantiated.Global
internal partial class Settings {
	public event EventHandler<InlayConfiguration>? OverlayAdded;
	public event EventHandler<InlayConfiguration>? OverlayNavigated;
	public event EventHandler<InlayConfiguration>? OverlayDebugged;
	public event EventHandler<InlayConfiguration>? OverlayRemoved;
	public event EventHandler<InlayConfiguration>? OverlayZoomed;
	public event EventHandler<InlayConfiguration>? OverlayMuted;
	public event EventHandler<InlayConfiguration>? OverlayUserCssChanged;
	private readonly Configuration Config;
	private readonly I18n _i18n;
	private bool _actAvailable;

#if DEBUG
	private bool _open = true;
#else
	private bool _open;
#endif

	private InlayConfiguration? _selectedOverlay;
	private Timer? _saveDebounceTimer;

	public Settings() {
		Plugin.PluginInterface.UiBuilder.OpenConfigUi += () => _open = true;
		Config = Plugin.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
		_i18n = new I18n(Config.Language);
	}

	public void OnActAvailabilityChanged(bool available) {
		_actAvailable = available;
		foreach (var overlayConfig in Config.Inlays)
			if (overlayConfig is { ActOptimizations: true, Disabled: false })
				(_actAvailable ? OverlayAdded : OverlayRemoved)?.Invoke(this, overlayConfig);
	}

	public void HandleConfigCommand() {
		_open = true;
		// TODO: Add further config handling if required here.
	}

	public void HandleOverlayCommand(string rawArgs) {
		var args = rawArgs.Split(null as char[], 3, StringSplitOptions.RemoveEmptyEntries);

		// Ensure there's enough arguments
		if (args.Length < 2 || args[1] != "reload" && args.Length < 3) {
			Plugin.Chat.PrintError("Invalid overlay command. Supported syntax: '[overlayCommandName] [setting] [value]'");
			return;
		}

		// Find the matching overlay config
		var targetConfig = Config.Inlays.Find(overlay => GetOverlayCommandName(overlay) == args[0]);
		if (targetConfig == null) {
			Plugin.Chat.PrintError($"Unknown overlay '{args[0]}'.");
			return;
		}

		switch (args[1]) {
			case "url":
				CommandSettingString(args[2], ref targetConfig.Url);
				// TODO: This call is duped with imgui handling. DRY.
				NavigateOverlay(targetConfig);
				break;
			case "locked":
				CommandSettingBoolean(args[2], ref targetConfig.Locked);
				break;
			case "hidden":
				CommandSettingBoolean(args[2], ref targetConfig.Hidden);
				break;
			case "typethrough":
				CommandSettingBoolean(args[2], ref targetConfig.TypeThrough);
				break;
			case "fullscreen":
				CommandSettingBoolean(args[2], ref targetConfig.Fullscreen);
				break;
			case "clickthrough":
				CommandSettingBoolean(args[2], ref targetConfig.ClickThrough);
				break;
			case "muted":
				CommandSettingBoolean(args[2], ref targetConfig.Muted);
				break;
			case "disabled":
				CommandSettingBoolean(args[2], ref targetConfig.Disabled);
				break;
			case "act":
				CommandSettingBoolean(args[2], ref targetConfig.ActOptimizations);
				break;
			case "reload":
				ReloadOverlay(targetConfig);
				break;

			default:
				Plugin.Chat.PrintError($"Unknown setting '{args[1]}. Valid settings are: url,hidden,locked,fullscreen,clickthrough,typethrough,muted,disabled,act.");
				return;
		}

		SaveSettings();
	}

	[SuppressMessage("ReSharper", "RedundantAssignment")]
	private static void CommandSettingString(string value, ref string target) {
		target = value;
	}

	private static void CommandSettingBoolean(string value, ref bool target) {
		switch (value) {
			case "on":
				target = true;
				break;
			case "off":
				target = false;
				break;
			case "toggle":
				target = !target;
				break;
			default:
				Plugin.Chat.PrintError(
					$"Unknown boolean value '{value}. Valid values are: on,off,toggle.");
				break;
		}
	}

	public void HydrateOverlays() {
		// Hydrate any overlays in the config
		foreach (var overlayConfig in Config.Inlays
			         .Where(overlayConfig => !overlayConfig.Disabled && (!overlayConfig.ActOptimizations || _actAvailable)))
			OverlayAdded?.Invoke(this, overlayConfig);
	}

	private InlayConfiguration AddNewOverlay() {
		InlayConfiguration overlayConfig = new() { Guid = Guid.NewGuid(), Name = _i18n.Get("settings.newOverlay"), Url = "about:blank" };
		Config.Inlays.Add(overlayConfig);
		OverlayAdded?.Invoke(this, overlayConfig);
		SaveSettings();

		return overlayConfig;
	}

	private void NavigateOverlay(InlayConfiguration overlayConfig) {
		if (overlayConfig.Url == "") { overlayConfig.Url = "about:blank"; }

		OverlayNavigated?.Invoke(this, overlayConfig);
	}

	private void UpdateZoomOverlay(InlayConfiguration overlayConfig) {
		OverlayZoomed?.Invoke(this, overlayConfig);
	}

	private void UpdateMuteOverlay(InlayConfiguration overlayConfig) {
		OverlayMuted?.Invoke(this, overlayConfig);
	}

	private void UpdateUserCss(InlayConfiguration overlayConfig) {
		OverlayUserCssChanged?.Invoke(this, overlayConfig);
	}

	private void ReloadOverlay(InlayConfiguration overlayConfig) { NavigateOverlay(overlayConfig); }

	private void DebugOverlay(InlayConfiguration overlayConfig) {
		OverlayDebugged?.Invoke(this, overlayConfig);
	}

	private void RemoveOverlay(InlayConfiguration overlayConfig) {
		OverlayRemoved?.Invoke(this, overlayConfig);
		Config.Inlays.Remove(overlayConfig);
		SaveSettings();
	}

	private void DebouncedSaveSettings() {
		_saveDebounceTimer?.Dispose();
		_saveDebounceTimer = new Timer(_ => SaveSettings(), null, 1000, Timeout.Infinite);
	}

	private void SaveSettings() {
		_saveDebounceTimer?.Dispose();
		_saveDebounceTimer = null;
		Plugin.PluginInterface.SavePluginConfig(Config);
	}

	private static string GetOverlayCommandName(InlayConfiguration overlayConfig) => overlayCommandNameRegex.Replace(overlayConfig.Name, "").ToLower();

	public void Render() {
		if (!_open) { return; }

		// Primary window container
		ImGui.SetNextWindowSizeConstraints(new Vector2(400, 300), new Vector2(9001, 9001));
		const ImGuiWindowFlags windowFlags = ImGuiWindowFlags.None | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse;
		ImGui.Begin(_i18n.Get("settings.title"), ref _open, windowFlags);

		RenderPaneSelector();

		// Pane details
		var dirty = false;
		ImGui.SameLine();
		ImGui.BeginChild("details");
		if (_selectedOverlay == null) {
			dirty |= RenderGeneralSettings();
		} else {
			dirty |= RenderOverlaySettings(_selectedOverlay);
		}

		ImGui.EndChild();

		if (dirty) { DebouncedSaveSettings(); }

		ImGui.End();
	}

	private void RenderPaneSelector() {
		// Selector pane
		ImGui.BeginGroup();
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));

		const int selectorWidth = 100;
		ImGui.BeginChild("panes", new Vector2(selectorWidth, -ImGui.GetFrameHeightWithSpacing()), true);

		// General settings
		if (ImGui.Selectable(_i18n.Get("settings.general"), _selectedOverlay == null))
			_selectedOverlay = null;

		// Overlay selector list
		ImGui.Dummy(new Vector2(0, 5));
		ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
		ImGui.Text(_i18n.Get("settings.overlays"));
		ImGui.PopStyleVar();
		foreach (var overlayConfig in Config.Inlays) {
			if (ImGui.Selectable($"{overlayConfig.Name}##{overlayConfig.Guid}", _selectedOverlay == overlayConfig))
				_selectedOverlay = overlayConfig;
		}

		ImGui.EndChild();

		// Selector controls
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
		ImGui.PushFont(UiBuilder.IconFont);

		const int buttonWidth = selectorWidth / 2;
		if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString(), new Vector2(buttonWidth, 0))) {
			_selectedOverlay = AddNewOverlay();
		}

		ImGui.SameLine();
		if (_selectedOverlay != null) {
			if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString(), new Vector2(buttonWidth, 0))) {
				var toRemove = _selectedOverlay;
				_selectedOverlay = null;
				RemoveOverlay(toRemove);
			}
		} else {
			ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
			ImGui.Button(FontAwesomeIcon.Trash.ToIconString(), new Vector2(buttonWidth, 0));
			ImGui.PopStyleVar();
		}

		ImGui.PopFont();
		ImGui.PopStyleVar(2);

		ImGui.Separator();
		ImGui.Text(_i18n.Get("settings.language"));
		ImGui.SetNextItemWidth(selectorWidth);
		var languageLabel = Config.Language switch {
			PluginLanguage.English => _i18n.Get("settings.languageEnglish"),
			PluginLanguage.Chinese => _i18n.Get("settings.languageChinese"),
			_ => _i18n.Get("settings.languageAuto")
		};
		if (ImGui.BeginCombo("##language", languageLabel)) {
			if (ImGui.Selectable(_i18n.Get("settings.languageAuto"), Config.Language == PluginLanguage.Auto)) {
				Config.Language = PluginLanguage.Auto;
				_i18n.Language = Config.Language;
				SaveSettings();
			}
			if (ImGui.Selectable(_i18n.Get("settings.languageEnglish"), Config.Language == PluginLanguage.English)) {
				Config.Language = PluginLanguage.English;
				_i18n.Language = Config.Language;
				SaveSettings();
			}
			if (ImGui.Selectable(_i18n.Get("settings.languageChinese"), Config.Language == PluginLanguage.Chinese)) {
				Config.Language = PluginLanguage.Chinese;
				_i18n.Language = Config.Language;
				SaveSettings();
			}
			ImGui.EndCombo();
		}
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip(_i18n.Get("settings.languageHint")); }

		ImGui.EndGroup();
	}

	private bool RenderGeneralSettings() {
		const bool dirty = false;

		ImGui.Text(_i18n.Get("settings.selectOverlay"));

		if (ImGui.CollapsingHeader(_i18n.Get("settings.commandHelp"), ImGuiTreeNodeFlags.DefaultOpen)) {
			// TODO: If this ever gets more than a few options, should probably colocate help with the defintion. Attributes?
			ImGui.Text("/bw config");
			ImGui.Text(_i18n.Get("settings.openConfig"));
			ImGui.Dummy(new Vector2(0, 5));
			ImGui.Text("/bw overlay [overlayCommandName] [setting] [value]");
			ImGui.TextWrapped(_i18n.Get("settings.changeOverlay"));
		}

		return dirty;
	}

	private bool RenderOverlaySettings(InlayConfiguration overlayConfig) {
		var dirty = false;

		ImGui.PushID(overlayConfig.Guid.ToString());

		dirty |= ImGui.InputText(_i18n.Get("settings.name"), ref overlayConfig.Name, 100);

		ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
		var commandName = GetOverlayCommandName(overlayConfig);
		ImGui.InputText(_i18n.Get("settings.commandName"), ref commandName, 100);
		ImGui.PopStyleVar();

		dirty |= ImGui.InputText(_i18n.Get("settings.url"), ref overlayConfig.Url, 1000);
		if (ImGui.IsItemDeactivatedAfterEdit()) { NavigateOverlay(overlayConfig); }

		if (ImGui.InputFloat(_i18n.Get("settings.zoom"), ref overlayConfig.Zoom, 1f, 10f, "%.0f%%")) {
			// clamp to allowed range 
			overlayConfig.Zoom = overlayConfig.Zoom switch {
				< 10f => 10f,
				> 500f => 500f,
				_ => overlayConfig.Zoom
			};

			dirty = true;

			// notify of zoom change
			UpdateZoomOverlay(overlayConfig);
		}

		if (ImGui.InputFloat(_i18n.Get("settings.opacity"), ref overlayConfig.Opacity, 1f, 10f, "%.0f%%")) {
			// clamp to allowed range 
			overlayConfig.Opacity = overlayConfig.Opacity switch {
				< 10f => 10f,
				> 100f => 100f,
				_ => overlayConfig.Opacity
			};

			dirty = true;
		}

		if (ImGui.InputInt(_i18n.Get("settings.framerate"), ref overlayConfig.Framerate, 1, 10)) {
			// clamp to allowed range 
			overlayConfig.Framerate = overlayConfig.Framerate switch {
				< 1 => 1,
				> 300 => 300,
				_ => overlayConfig.Framerate
			};

			dirty = true;

			// framerate changes require the recreation of the browser instance
			// TODO: this is ugly as heck, fix once proper IPC is up and running
			OverlayRemoved?.Invoke(this, overlayConfig);
			OverlayAdded?.Invoke(this, overlayConfig);
		}

		ImGui.SetNextItemWidth(100);
		ImGui.Columns(2, "boolInlayOptions", false);

		if (ImGui.Checkbox(_i18n.Get("settings.disabled"), ref overlayConfig.Disabled)) {
			if (overlayConfig.Disabled)
				OverlayRemoved?.Invoke(this, overlayConfig);
			else
				OverlayAdded?.Invoke(this, overlayConfig);
			dirty = true;
		}

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip(_i18n.Get("tooltip.disabled")); }

		ImGui.NextColumn();
		ImGui.NextColumn();


		if (ImGui.Checkbox(_i18n.Get("settings.muted"), ref overlayConfig.Muted)) {
			UpdateMuteOverlay(overlayConfig);
			dirty = true;
		}

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip(_i18n.Get("tooltip.muted")); }

		ImGui.NextColumn();

		if (ImGui.Checkbox(_i18n.Get("settings.actOptimizations"), ref overlayConfig.ActOptimizations)) {
			if (!overlayConfig.Disabled) {
				if (overlayConfig.ActOptimizations) {
					if (!_actAvailable)
						OverlayRemoved?.Invoke(this, overlayConfig);
					else
						OverlayAdded?.Invoke(this, overlayConfig);
				} else {
					OverlayAdded?.Invoke(this, overlayConfig);
				}
			}

			dirty = true;
		}

		if (ImGui.IsItemHovered()) {
			ImGui.SetTooltip("Enables ACT/IINACT specific optimizations. This will automatically disable the overlay if ACT/IINACT is not running.\n\nNOTE: This does NOT disable the overlay if the websocket is not reporting data.");
		}

		ImGui.NextColumn();

		if (overlayConfig.ClickThrough || overlayConfig.Fullscreen) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }

		var true_ = true;
		var implicit_ = overlayConfig.ClickThrough || overlayConfig.Fullscreen;
		dirty |= ImGui.Checkbox(_i18n.Get("settings.locked"), ref implicit_ ? ref true_ : ref overlayConfig.Locked);
		if (overlayConfig.ClickThrough) { ImGui.PopStyleVar(); }

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip(_i18n.Get("tooltip.locked")); }

		ImGui.NextColumn();

		dirty |= ImGui.Checkbox(_i18n.Get("settings.hidden"), ref overlayConfig.Hidden);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip(_i18n.Get("tooltip.hidden")); }

		ImGui.NextColumn();

		if (overlayConfig.ClickThrough) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }

		dirty |= ImGui.Checkbox(_i18n.Get("settings.typeThrough"), ref overlayConfig.ClickThrough ? ref true_ : ref overlayConfig.TypeThrough);
		if (overlayConfig.ClickThrough || overlayConfig.Fullscreen) { ImGui.PopStyleVar(); }

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip(_i18n.Get("tooltip.typeThrough")); }

		ImGui.NextColumn();

		dirty |= ImGui.Checkbox(_i18n.Get("settings.clickThrough"), ref overlayConfig.ClickThrough);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip(_i18n.Get("tooltip.clickThrough")); }

		ImGui.NextColumn();

		dirty |= ImGui.Checkbox(_i18n.Get("settings.hideOutOfCombat"), ref overlayConfig.HideOutOfCombat);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip(_i18n.Get("tooltip.hideOutOfCombat")); }

		ImGui.NextColumn();

		dirty |= ImGui.Checkbox(_i18n.Get("settings.hideInPvp"), ref overlayConfig.HideInPvP);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip(_i18n.Get("tooltip.hideInPvp")); }

		ImGui.NextColumn();

		if (!overlayConfig.HideOutOfCombat) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }

		dirty |= ImGui.InputInt(_i18n.Get("settings.hideDelay"), ref overlayConfig.HideDelay);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip(_i18n.Get("tooltip.hideDelay")); }

		if (!overlayConfig.HideOutOfCombat) { ImGui.PopStyleVar(); }

		ImGui.Columns();

		ImGui.NewLine();
		if (ImGui.CollapsingHeader(_i18n.Get("settings.experimental"))) {
			ImGui.NewLine();
			dirty |= ImGui.Checkbox(_i18n.Get("settings.fullscreen"), ref overlayConfig.Fullscreen);
			ImGui.NewLine();
			if (ImGui.IsItemHovered()) { ImGui.SetTooltip(_i18n.Get("tooltip.fullscreen")); }
			ImGui.Text(_i18n.Get("settings.customCss"));
			if (ImGui.InputTextMultiline("Custom CSS code", ref overlayConfig.CustomCss, 1000000,
				    new Vector2(-1, ImGui.GetTextLineHeight() * 10))) {
				dirty = true;
			}

			if (ImGui.IsItemDeactivatedAfterEdit()) { UpdateUserCss(overlayConfig); }
		}

		ImGui.NewLine();
		if (ImGui.Button(_i18n.Get("settings.reload"))) { ReloadOverlay(overlayConfig); }

		ImGui.SameLine();
		if (ImGui.Button(_i18n.Get("settings.devTools"))) { DebugOverlay(overlayConfig); }

		ImGui.PopID();

		return dirty;
	}

	[GeneratedRegex(@"\s+")]
	private static partial Regex OverlayCommandNameRegex();

	private static readonly Regex overlayCommandNameRegex = OverlayCommandNameRegex();
}