using System;

namespace Browsingway.Common;

public static class RenderParamsSerializer {
	public static string Serialize(RenderParams renderParams) =>
		Convert.ToBase64String(IpcSerializer.SerializeRenderParams(renderParams));

	public static RenderParams Deserialize(string base64) =>
		IpcSerializer.DeserializeRenderParams(Convert.FromBase64String(base64));
}