using Dalamud.Configuration;

namespace Browsingway;

[Serializable]
internal class Configuration : IPluginConfiguration {
	public List<InlayConfiguration> Inlays = [];
	public PluginLanguage Language = PluginLanguage.Auto;
	public int Version { get; set; }
}

[Serializable]
internal class InlayConfiguration {
	public bool ClickThrough;
	public int Framerate = 60;
	public Guid Guid;
	public bool Hidden;
	public bool Locked;
	public string Name = null!;
	public float Opacity = 100f;
	public bool TypeThrough;
	public string Url = null!;
	public float Zoom = 100f;
	public bool Disabled;
	public string CustomCss = "";
	public bool Muted;
	public bool ActOptimizations;
	public bool Fullscreen;
	public bool HideOutOfCombat;
	public bool HideInPvP;
	public int HideDelay;
}