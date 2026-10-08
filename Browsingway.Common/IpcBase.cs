using System;
using System.Threading.Tasks;
using SharedMemory;

namespace Browsingway.Common;

public class IpcBase : IDisposable {
	private readonly RpcBuffer _buffer;

	protected IpcBase(string name) => 
		_buffer = new RpcBuffer(name, (_, data) => HandleCall(IpcSerializer.DeserializeRpcCall(data)));

	protected async Task SendCall(RpcCall msg) => await _buffer.RemoteRequestAsync(IpcSerializer.SerializeRpcCall(msg));

	protected virtual void HandleCall(RpcCall call) {
	}

	public void Dispose() {
		_buffer.Dispose();
		GC.SuppressFinalize(this);
	}
}