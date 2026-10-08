namespace Browsingway;

internal enum PluginLanguage {
	Auto,
	English,
	Chinese
}

internal sealed class I18n(PluginLanguage language) {
	private readonly Dictionary<string, (string English, string Chinese)> _texts = new() {
		["settings.title"] = ("Browsingway Settings", "Browsingway 设置"),
		["settings.general"] = ("General", "常规"),
		["settings.overlays"] = ("- Overlays -", "- 悬浮窗 -"),
		["settings.selectOverlay"] = ("Select an overlay on the left to edit its settings.", "请从左侧选择一个悬浮窗来编辑设置。"),
		["settings.commandHelp"] = ("Command Help", "命令帮助"),
		["settings.openConfig"] = ("Open this configuration window.", "打开此配置窗口。"),
		["settings.changeOverlay"] = (
			"Change a setting for an overlay.\n" +
			"\toverlayCommandName: The overlay to edit. Use the 'Command Name' shown in its config.\n" +
			"\tsetting: Value to change. Accepted settings are:\n" +
			"\t\turl: string\n" +
			"\t\tdisabled: boolean\n" +
			"\t\tmuted: boolean\n" +
			"\t\tact: boolean\n" +
			"\t\tlocked: boolean\n" +
			"\t\thidden: boolean\n" +
			"\t\ttypethrough: boolean\n" +
			"\t\tclickthrough: boolean\n" +
			"\t\tfullscreen: boolean\n" +
			"\t\treload: -\n" +
			"\tvalue: Value to set for the setting. Accepted values are:\n" +
			"\t\tstring: any string value\n\t\tboolean: on, off, toggle",
			"修改悬浮窗设置。\n" +
			"\toverlayCommandName: 要编辑的悬浮窗。使用其配置中显示\"命令名称\"。\n" +
			"\tsetting: 要修改的设置项。可接受的设置项有：\n" +
			"\t\turl: string\n" +
			"\t\tdisabled: boolean\n" +
			"\t\tmuted: boolean\n" +
			"\t\tact: boolean\n" +
			"\t\tlocked: boolean\n" +
			"\t\thidden: boolean\n" +
			"\t\ttypethrough: boolean\n" +
			"\t\tclickthrough: boolean\n" +
			"\t\tfullscreen: boolean\n" +
			"\t\treload: -\n" +
			"\tvalue: 要为该设置项设定的值。可接受的值有：\n" +
			"\t\tstring: 任意字符串值\n\t\t布尔值: on、off、toggle"),
		["settings.commandName"] = ("Command Name", "命令名称"),
		["settings.name"] = ("Name", "名称"),
		["settings.url"] = ("URL", "网址"),
		["settings.zoom"] = ("Zoom", "缩放"),
		["settings.opacity"] = ("Opacity", "不透明度"),
		["settings.framerate"] = ("Framerate", "帧率"),
		["settings.disabled"] = ("Disabled", "禁用"),
		["settings.muted"] = ("Muted", "静音"),
		["settings.actOptimizations"] = ("ACT/IINACT optimizations", "ACT/IINACT 优化"),
		["settings.locked"] = ("Locked", "锁定"),
		["settings.hidden"] = ("Hidden", "隐藏"),
		["settings.typeThrough"] = ("Type Through", "键盘穿透"),
		["settings.clickThrough"] = ("Click Through", "鼠标穿透"),
		["settings.hideOutOfCombat"] = ("Hide out of combat", "脱战时隐藏"),
		["settings.hideInPvp"] = ("Hide in PvP", "PvP 区域隐藏"),
		["settings.hideDelay"] = ("Hide Delay", "隐藏延迟"),
		["settings.experimental"] = ("Experimental / Unsupported", "实验性 / 不受支持"),
		["settings.fullscreen"] = ("Fullscreen", "全屏"),
		["settings.customCss"] = ("Custom CSS code:", "自定义 CSS 代码："),
		["settings.reload"] = ("Reload", "重新加载"),
		["settings.devTools"] = ("Open Dev Tools", "打开开发者工具"),
		["settings.newOverlay"] = ("New overlay", "新建悬浮窗"),
		["settings.language"] = ("Language", "语言"),
		["settings.languageAuto"] = ("Follow game language", "跟随游戏语言"),
		["settings.languageEnglish"] = ("English", "English"),
		["settings.languageChinese"] = ("简体中文", "简体中文"),
		["settings.languageHint"] = ("Select the language used by the Browsingway settings window.", "选择 Browsingway 设置窗口使用的语言。"),
		["tooltip.disabled"] = ("Disables the overlay. Unlike hiding it, this setting stops it from being created.", "禁用悬浮窗。与隐藏不同，启用后不会创建悬浮窗。"),
		["tooltip.muted"] = ("Enables or disables audio playback.", "启用或禁用音频播放。"),
		["tooltip.act"] = ("Enables ACT/IINACT-specific optimizations. The overlay is automatically disabled when ACT/IINACT is not running.\n\nThis does not disable the overlay when the websocket is not reporting data.",
			"启用 ACT/IINACT 专用优化。ACT/IINACT 未运行时会自动禁用悬浮窗。\n\n当 websocket 未上报数据时，此选项不会禁用悬浮窗。"),
		["tooltip.locked"] = ("Prevents the overlay from being resized or moved. This is implicitly enabled by Click Through and Fullscreen.", "防止调整悬浮窗大小或移动悬浮窗。启用鼠标穿透或全屏时会自动启用。"),
		["tooltip.hidden"] = ("Hides the overlay without stopping it from executing.", "隐藏悬浮窗，但不会停止其运行。"),
		["tooltip.typeThrough"] = ("Prevents the overlay from intercepting keyboard events. Implicitly enabled by Click Through.", "防止悬浮窗拦截键盘事件。启用鼠标穿透时会自动启用。"),
		["tooltip.clickThrough"] = ("Prevents the overlay from intercepting mouse events. Also enables Locked and Type Through.", "防止悬浮窗拦截鼠标事件，同时启用锁定和键盘穿透。"),
		["tooltip.hideOutOfCombat"] = ("Hides this overlay when out of combat.", "脱战时隐藏此悬浮窗。"),
		["tooltip.hideInPvp"] = ("Hides this overlay in PvP areas.", "在 PvP 区域隐藏此悬浮窗。"),
		["tooltip.hideDelay"] = ("Delay before hiding the overlay after leaving combat, in seconds.", "脱战后隐藏悬浮窗的延迟时间，单位为秒。"),
		["tooltip.fullscreen"] = ("Automatically makes this overlay cover the entire screen when enabled.", "启用后自动使悬浮窗覆盖整个屏幕。")
	};

	public PluginLanguage Language { get; set; } = language;

	public string Get(string key) => _texts.TryGetValue(key, out var text)
		? IsChinese ? text.Chinese : text.English
		: key;

	private bool IsChinese => Language switch {
		PluginLanguage.Chinese => true,
		PluginLanguage.English => false,
		_ => Plugin.ClientState.ClientLanguage.ToString() == "ChineseSimplified"
	};
}