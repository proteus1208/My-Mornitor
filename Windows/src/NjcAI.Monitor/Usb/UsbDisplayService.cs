using System.Runtime.InteropServices;
using LibUsbDotNet;
using LibUsbDotNet.Info;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using NjcAI.Monitor.Core;

namespace NjcAI.Monitor.Usb;

internal sealed class UsbDisplayService : IDisposable
{
    private static readonly Lazy<(bool Available, string Error)> NativeRuntime = new(CheckNativeRuntime);
    private readonly object _gate = new();
    private UsbContext? _context;
    private IUsbDevice? _device;
    private UsbEndpointWriter? _writer;
    private UsbEndpointReader? _reader;
    private int _claimedInterface = -1;
    private DateTime _nextConnectAttempt;

    public bool IsConnected { get; private set; }
    public DeviceInfo? DeviceInfo { get; private set; }
    public string Status { get; private set; } = "正在查找 LCD";
    public long FramesSent { get; private set; }
    public int LastFrameBytes { get; private set; }

    public bool EnsureConnected(bool force = false)
    {
        lock (_gate)
        {
            if (IsConnected)
            {
                return true;
            }
            if (!force && DateTime.UtcNow < _nextConnectAttempt)
            {
                return false;
            }
            _nextConnectAttempt = DateTime.UtcNow.AddSeconds(3);
            DisconnectCore();
            if (!NativeRuntime.Value.Available)
            {
                Status = NativeRuntime.Value.Error;
                return false;
            }
            try
            {
                _context = new UsbContext();
                _context.SetDebugLevel(LogLevel.None);
                _device = _context.Find(device =>
                    device.VendorId == LyProtocol.VendorId &&
                    device.ProductId is LyProtocol.ProductLy or LyProtocol.ProductLy1);
                if (_device is null)
                {
                    Status = "未检测到 0416:5408/5409";
                    return false;
                }

                _device.Open();
                var config = _device.Configs.FirstOrDefault()
                    ?? throw new InvalidOperationException("LCD has no USB configuration");
                if (_device.Configuration == 0)
                {
                    _device.SetConfiguration(config.ConfigurationValue);
                }

                var usbInterface = SelectInterface(config);
                _claimedInterface = usbInterface.Number;
                if (!_device.ClaimInterface(_claimedInterface))
                {
                    throw new InvalidOperationException($"Unable to claim USB interface {_claimedInterface}");
                }

                var (outAddress, inAddress) = SelectEndpoints(usbInterface);
                _writer = _device.OpenEndpointWriter((WriteEndpointID)outAddress, EndpointType.Bulk);
                _reader = _device.OpenEndpointReader((ReadEndpointID)inAddress, 4096, EndpointType.Bulk);

                WriteAll(LyProtocol.CreateHandshake(), 1000);
                var response = Read(512, 1000);
                DeviceInfo = LyProtocol.ParseHandshake(response, _device.ProductId);
                if (!LyProtocol.IsTrofeoVision113(DeviceInfo))
                {
                    throw new NotSupportedException(
                        $"检测到 {_device.ProductId:X4} · {DeviceInfo.Width}×{DeviceInfo.Height}；" +
                        "本版本仅支持 Trofeo Vision 11.3（1920×400）");
                }
                IsConnected = true;
                Status = $"已连接 {LyProtocol.VendorId:X4}:{_device.ProductId:X4} · {DeviceInfo.Width}×{DeviceInfo.Height}";
                Log.Info($"USB connected: {Status}, OUT=0x{outAddress:X2}, IN=0x{inAddress:X2}");
                return true;
            }
            catch (Exception exception)
            {
                Status = FriendlyUsbError(exception);
                Log.Warn($"USB connect failed: {exception.Message}");
                DisconnectCore(keepStatus: true);
                return false;
            }
        }
    }

    public bool SendFrame(ReadOnlySpan<byte> jpeg)
    {
        lock (_gate)
        {
            if (!IsConnected || _device is null)
            {
                return false;
            }
            try
            {
                var transfer = LyProtocol.CreateFrameTransfer(jpeg, _device.ProductId);
                var position = 0;
                while (position < transfer.Length)
                {
                    var remaining = transfer.Length - position;
                    var writeSize = remaining >= 4096
                        ? 4096
                        : _device.ProductId == LyProtocol.ProductLy ? Math.Min(2048, remaining) : remaining;
                    WriteAll(transfer.AsSpan(position, writeSize), 5000);
                    position += writeSize;
                }
                _ = Read(512, 1000);
                FramesSent++;
                LastFrameBytes = jpeg.Length;
                return true;
            }
            catch (Exception exception)
            {
                Status = $"LCD 连接中断：{exception.Message}";
                Log.Warn(Status);
                DisconnectCore(keepStatus: true);
                _nextConnectAttempt = DateTime.UtcNow.AddSeconds(2);
                return false;
            }
        }
    }

    public void Reconnect()
    {
        lock (_gate)
        {
            DisconnectCore();
            _nextConnectAttempt = DateTime.MinValue;
        }
        _ = EnsureConnected(true);
    }

    public string Diagnose()
    {
        lock (_gate)
        {
            if (!NativeRuntime.Value.Available)
            {
                return NativeRuntime.Value.Error;
            }
            try
            {
                using var context = new UsbContext();
                using var devices = context.List();
                var matching = devices.Where(device =>
                    device.VendorId == LyProtocol.VendorId &&
                    device.ProductId is LyProtocol.ProductLy or LyProtocol.ProductLy1).ToArray();
                if (matching.Length == 0)
                {
                    return "未检测到利民 LCD（VID 0416，PID 5408/5409）。请检查 USB 线和设备管理器。";
                }
                return string.Join(Environment.NewLine, matching.Select(device =>
                    $"检测到 {device.VendorId:X4}:{device.ProductId:X4}；驱动可见。"));
            }
            catch (Exception exception)
            {
                return $"USB 枚举失败：{exception.Message}{Environment.NewLine}" +
                       "请退出官方 TRCC，并确认设备使用 WinUSB/libusbK 驱动。";
            }
        }
    }

    private static UsbInterfaceInfo SelectInterface(UsbConfigInfo config) =>
        config.Interfaces.FirstOrDefault(item => item.Class == ClassCode.VendorSpec)
        ?? config.Interfaces.FirstOrDefault()
        ?? throw new InvalidOperationException("LCD has no USB interface");

    private static (byte Out, byte In) SelectEndpoints(UsbInterfaceInfo usbInterface)
    {
        var bulk = usbInterface.Endpoints
            .Where(endpoint => ((int)endpoint.Attributes & 0x03) == (int)EndpointType.Bulk)
            .ToArray();
        var output = bulk.FirstOrDefault(endpoint => (endpoint.EndpointAddress & 0x80) == 0)?.EndpointAddress
                     ?? 0x09;
        var input = bulk.FirstOrDefault(endpoint => (endpoint.EndpointAddress & 0x80) != 0)?.EndpointAddress
                    ?? 0x81;
        return ((byte)output, (byte)input);
    }

    private void WriteAll(ReadOnlySpan<byte> data, int timeout)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("USB OUT endpoint is not open");
        }
        var error = _writer.Write(data, timeout, out var transferred);
        if (error != Error.Success || transferred != data.Length)
        {
            throw new IOException($"USB write failed ({error}, {transferred}/{data.Length} bytes)");
        }
    }

    private byte[] Read(int size, int timeout)
    {
        if (_reader is null)
        {
            throw new InvalidOperationException("USB IN endpoint is not open");
        }
        var buffer = new byte[size];
        var error = _reader.Read(buffer, timeout, out var transferred);
        if (error != Error.Success)
        {
            throw new IOException($"USB read failed ({error})");
        }
        return transferred == buffer.Length ? buffer : buffer[..transferred];
    }

    private static string FriendlyUsbError(Exception exception)
    {
        if (exception is NotSupportedException)
        {
            return exception.Message;
        }
        var text = exception.Message;
        if (text.Contains("Access", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Busy", StringComparison.OrdinalIgnoreCase))
        {
            return "LCD 被其他程序占用；请退出官方 TRCC";
        }
        if (text.Contains("NotSupported", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("driver", StringComparison.OrdinalIgnoreCase))
        {
            return "LCD 驱动不兼容；需要 WinUSB/libusbK";
        }
        return $"LCD 连接失败：{text}";
    }

    private static (bool Available, string Error) CheckNativeRuntime()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "libusb-1.0.dll");
        if (!File.Exists(path))
        {
            return (false, "USB 运行库缺失：libusb-1.0.dll；请重新解压完整发布包");
        }

        try
        {
            if (!NativeLibrary.TryLoad(path, out var handle))
            {
                return (false, "USB 运行库无法加载；请确认程序包架构与 Windows 一致");
            }
            NativeLibrary.Free(handle);
            return (true, string.Empty);
        }
        catch (Exception exception)
        {
            return (false, $"USB 运行库无法加载：{exception.Message}");
        }
    }

    private void DisconnectCore(bool keepStatus = false)
    {
        IsConnected = false;
        DeviceInfo = null;
        _writer = null;
        _reader = null;
        if (_device is not null)
        {
            try
            {
                if (_claimedInterface >= 0)
                {
                    _device.ReleaseInterface(_claimedInterface);
                }
            }
            catch { }
            try { _device.Close(); } catch { }
            try { _device.Dispose(); } catch { }
        }
        _claimedInterface = -1;
        _device = null;
        try { _context?.Dispose(); } catch { }
        _context = null;
        if (!keepStatus)
        {
            Status = "正在查找 LCD";
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            DisconnectCore();
        }
    }
}
