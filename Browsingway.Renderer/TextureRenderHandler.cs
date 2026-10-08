using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using Cursor = Browsingway.Common.Cursor;
using Size = System.Drawing.Size;

namespace Browsingway.Renderer;

internal unsafe class TextureRenderHandler : IDisposable {
	private readonly Lock _renderLock = new();
	private ID3D11Texture2D* _sharedTexture;
	private ID3D11Texture2D* _viewTexture;
	private IntPtr _sharedTextureHandle;
	private Size _size;
	private byte[] _alpha;
	private Cursor _cursor = Cursor.Default;
	private bool _cursorOnBackground;
	private bool _disposed;
	private bool _firstUploadLogged;
	private readonly List<RetiredTexture> _retiredTextures = [];

	public TextureRenderHandler(Size size) {
		_size = NormalizeSize(size);
		_sharedTexture = BuildTexture(_size, true);
		_viewTexture = BuildTexture(_size, false);
		_alpha = new byte[_size.Width * _size.Height * 4];
		ClearTextures();
	}

	public IntPtr SharedTextureHandle {
		get {
			lock (_renderLock) {
				if (_disposed || _sharedTexture == null) return IntPtr.Zero;
				if (_sharedTextureHandle != IntPtr.Zero) return _sharedTextureHandle;
				IDXGIResource* resource;
				var guid = typeof(IDXGIResource).GUID;
				if (((IUnknown*)_sharedTexture)->QueryInterface(&guid, (void**)&resource).SUCCEEDED) {
					HANDLE handle;
					resource->GetSharedHandle(&handle);
					_sharedTextureHandle = (IntPtr)handle.Value;
					resource->Release();
				}
				return _sharedTextureHandle;
			}
		}
	}

	public event EventHandler<Cursor>? CursorChanged;
	public int Width {
		get {
			lock (_renderLock) return _size.Width;
		}
	}

	public int Height {
		get {
			lock (_renderLock) return _size.Height;
		}
	}

	public void Update(Bitmap bitmap) {
		lock (_renderLock) {
			if (_disposed) return;

			using var converted = new Bitmap(_size.Width, _size.Height, PixelFormat.Format32bppArgb);
			using (var graphics = Graphics.FromImage(converted)) {
				graphics.CompositingMode = CompositingMode.SourceCopy;
				graphics.DrawImage(
					bitmap,
					new Rectangle(0, 0, converted.Width, converted.Height),
					0,
					0,
					bitmap.Width,
					bitmap.Height,
					GraphicsUnit.Pixel);
			}

			var data = converted.LockBits(new Rectangle(0, 0, converted.Width, converted.Height),
				ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			try {
				var rowBytes = converted.Width * 4;
				var sourceStride = Math.Abs(data.Stride);
				var pixels = new byte[rowBytes * converted.Height];
				for (var row = 0; row < converted.Height; row++) {
					var sourceRow = data.Stride >= 0 ? row : converted.Height - 1 - row;
					Marshal.Copy(
						IntPtr.Add(data.Scan0, sourceRow * sourceStride),
						pixels,
						row * rowBytes,
						rowBytes);
				}

				_alpha = pixels;
				ID3D11DeviceContext* context;
				DxHandler.Device->GetImmediateContext(&context);
				try {
					fixed (byte* source = pixels) {
						context->UpdateSubresource(
							(ID3D11Resource*)_viewTexture,
							0,
							null,
							source,
							(uint)rowBytes,
							(uint)pixels.Length);
					}

					context->CopySubresourceRegion(
						(ID3D11Resource*)_sharedTexture,
						0,
						0,
						0,
						0,
						(ID3D11Resource*)_viewTexture,
						0,
						null);
					context->Flush();
				} finally {
					context->Release();
				}

				ReleaseRetiredTextures();

				if (!_firstUploadLogged) {
					_firstUploadLogged = true;
					var alphaSamples = 0;
					var colorSamples = 0;
					for (var sample = 3; sample < pixels.Length; sample += Math.Max(4, pixels.Length / 64)) {
						if (pixels[sample] != 0) alphaSamples++;
						if (pixels[sample - 1] != 0 || pixels[sample - 2] != 0 || pixels[sample - 3] != 0)
							colorSamples++;
					}

					var center = converted.Height / 2 * rowBytes + converted.Width / 2 * 4;
					Console.WriteLine(
						$"WebView2 texture upload: size={converted.Width}x{converted.Height}, " +
						$"firstPixel=rgba({pixels[2]},{pixels[1]},{pixels[0]},{pixels[3]}), " +
						$"centerPixel=rgba({pixels[center + 2]},{pixels[center + 1]},{pixels[center]},{pixels[center + 3]}), " +
						$"alphaSamples={alphaSamples}, colorSamples={colorSamples}, " +
						$"shared=0x{SharedTextureHandle.ToInt64():X}");
				}
			} finally {
				converted.UnlockBits(data);
			}
		}
	}

	public void Resize(Size size) {
		lock (_renderLock) {
			if (_disposed) return;

			size = NormalizeSize(size);
			var sharedTexture = BuildTexture(size, true);
			var viewTexture = BuildTexture(size, false);
			_retiredTextures.Add(new RetiredTexture(
				_sharedTexture,
				_viewTexture,
				DateTime.UtcNow.AddSeconds(2)));
			_size = size;
			_sharedTexture = sharedTexture;
			_viewTexture = viewTexture;
			_alpha = new byte[_size.Width * _size.Height * 4];
			_sharedTextureHandle = IntPtr.Zero;
			_firstUploadLogged = false;
			ClearTextures();
		}
	}

	public void SetMousePosition(int x, int y) {
		bool transparent;
		lock (_renderLock) {
			var offset = Math.Clamp(y, 0, Math.Max(0, _size.Height - 1)) * _size.Width * 4 +
			             Math.Clamp(x, 0, Math.Max(0, _size.Width - 1)) * 4 + 3;
			transparent = offset >= 0 && offset < _alpha.Length && _alpha[offset] == 0;
			if (transparent == _cursorOnBackground) return;
			_cursorOnBackground = transparent;
		}

		CursorChanged?.Invoke(this, transparent ? Cursor.BrowsingwayNoCapture : _cursor);
	}

	public void SetCursor(Cursor cursor) {
		bool changed;
		lock (_renderLock) {
			changed = _cursor != cursor;
			_cursor = cursor;
		}

		if (changed && !_cursorOnBackground)
			CursorChanged?.Invoke(this, cursor);
	}

	public void Dispose() {
		lock (_renderLock) {
			if (_disposed) return;
			_disposed = true;
			if (_sharedTexture != null) {
				_sharedTexture->Release();
				_sharedTexture = null;
			}

			if (_viewTexture != null) {
				_viewTexture->Release();
				_viewTexture = null;
			}

			foreach (var retired in _retiredTextures)
				retired.Release();
			_retiredTextures.Clear();
		}
	}

	private void ReleaseRetiredTextures() {
		var now = DateTime.UtcNow;
		for (var index = _retiredTextures.Count - 1; index >= 0; index--) {
			if (_retiredTextures[index].ReleaseAt > now)
				continue;

			_retiredTextures[index].Release();
			_retiredTextures.RemoveAt(index);
		}
	}

	private void ClearTextures() {
		var clear = new byte[_size.Width * _size.Height * 4];
		fixed (byte* source = clear) {
			ID3D11DeviceContext* context;
			DxHandler.Device->GetImmediateContext(&context);
			context->UpdateSubresource(
				(ID3D11Resource*)_viewTexture,
				0,
				null,
				source,
				(uint)(_size.Width * 4),
				(uint)clear.Length);
			context->CopySubresourceRegion(
				(ID3D11Resource*)_sharedTexture,
				0,
				0,
				0,
				0,
				(ID3D11Resource*)_viewTexture,
				0,
				null);
			context->Flush();
			context->Release();
		}
	}

	private static ID3D11Texture2D* BuildTexture(Size size, bool shared) {
		D3D11_TEXTURE2D_DESC desc = new() {
			Width = (uint)Math.Max(1, size.Width),
			Height = (uint)Math.Max(1, size.Height),
			MipLevels = 1,
			ArraySize = 1,
			Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
			SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
			Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
			BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
			MiscFlags = shared ? (uint)D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_SHARED : 0
		};
		ID3D11Texture2D* texture;
		var hr = DxHandler.Device->CreateTexture2D(&desc, null, &texture);
		return hr.FAILED ? throw new Exception($"Failed to create texture: {hr}") : texture;
	}

	private static Size NormalizeSize(Size size) =>
		new(Math.Max(1, size.Width), Math.Max(1, size.Height));

	private readonly struct RetiredTexture(ID3D11Texture2D* shared, ID3D11Texture2D* view, DateTime releaseAt) {
		private ID3D11Texture2D* Shared { get; } = shared;
		private ID3D11Texture2D* View { get; } = view;
		public DateTime ReleaseAt { get; } = releaseAt;

		public void Release() {
			if (Shared != null) Shared->Release();
			if (View != null) View->Release();
		}
	}
}