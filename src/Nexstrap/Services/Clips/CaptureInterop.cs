using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Nexstrap.Services.Clips;

/// <summary>
/// The few native calls Windows' screen capture needs from a desktop app: a Direct3D device for the capture
/// to draw into, and a capture item for one window. Everything else is regular Windows Runtime.
/// </summary>
internal static class CaptureInterop
{
    private static readonly Guid DxgiDeviceIid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private const int DriverTypeHardware = 1;
    private const uint BgraSupport = 0x20;
    private const uint SdkVersion = 7;

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels,
        uint featureLevelCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    /// <summary>A hardware Direct3D 11 device, wrapped the way the capture and media classes take it.</summary>
    public static IDirect3DDevice CreateDevice()
    {
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, DriverTypeHardware, IntPtr.Zero, BgraSupport, IntPtr.Zero, 0, SdkVersion,
            out var d3d, out _, out var context));
        IntPtr dxgi = IntPtr.Zero, inspectable = IntPtr.Zero;
        try
        {
            var iid = DxgiDeviceIid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, in iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            if (inspectable != IntPtr.Zero) Marshal.Release(inspectable);
            if (dxgi != IntPtr.Zero) Marshal.Release(dxgi);
            if (context != IntPtr.Zero) Marshal.Release(context);
            Marshal.Release(d3d);
        }
    }

    /// <summary>A capture item for one window (here, Roblox's), so only that window is recorded.</summary>
    public static GraphicsCaptureItem CreateItemForWindow(IntPtr window)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = GraphicsCaptureItemIid;
        var pointer = interop.CreateForWindow(window, ref iid);
        try
        {
            return GraphicsCaptureItem.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }
}
