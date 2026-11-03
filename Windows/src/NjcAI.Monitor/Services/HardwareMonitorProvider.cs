using LibreHardwareMonitor.Hardware;
using NjcAI.Monitor.Core;

namespace NjcAI.Monitor.Services;

internal sealed class HardwareReading
{
    public double? CpuTotal { get; init; }
    public IReadOnlyList<double> CpuCores { get; init; } = [];
    public double? CpuTemperature { get; init; }
    public double? GpuTemperature { get; init; }
    public double? GpuLoad { get; init; }
    public double DiskReadBytesPerSecond { get; init; }
    public double DiskWriteBytesPerSecond { get; init; }
}

internal sealed class HardwareMonitorProvider : IDisposable
{
    private Computer? _computer;
    private bool _initializationAttempted;

    public HardwareReading Read()
    {
        EnsureOpen();
        if (_computer is null)
        {
            return new HardwareReading();
        }

        try
        {
            var cores = new List<(int Index, double Value)>();
            var cpuTemperatures = new List<double>();
            var gpuTemperatures = new List<double>();
            double? cpuTotal = null;
            double? gpuLoad = null;
            double diskRead = 0;
            double diskWrite = 0;

            foreach (var hardware in Walk(_computer.Hardware))
            {
                hardware.Update();
                foreach (var sensor in hardware.Sensors)
                {
                    if (!sensor.Value.HasValue)
                    {
                        continue;
                    }

                    var value = sensor.Value.Value;
                    var name = sensor.Name;
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        if (sensor.SensorType == SensorType.Load)
                        {
                            if (name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                            {
                                cpuTotal = value;
                            }
                            else if (name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                            {
                                cores.Add((ExtractTrailingNumber(name, cores.Count + 1), value));
                            }
                        }
                        else if (sensor.SensorType == SensorType.Temperature)
                        {
                            cpuTemperatures.Add(value);
                        }
                    }
                    else if (IsGpu(hardware.HardwareType))
                    {
                        if (sensor.SensorType == SensorType.Load &&
                            (name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase)))
                        {
                            gpuLoad = Math.Max(gpuLoad ?? 0, value);
                        }
                        else if (sensor.SensorType == SensorType.Temperature)
                        {
                            gpuTemperatures.Add(value);
                        }
                    }
                    else if (hardware.HardwareType == HardwareType.Storage &&
                             sensor.SensorType == SensorType.Throughput)
                    {
                        if (name.Contains("Read", StringComparison.OrdinalIgnoreCase))
                        {
                            diskRead += value;
                        }
                        else if (name.Contains("Write", StringComparison.OrdinalIgnoreCase))
                        {
                            diskWrite += value;
                        }
                    }
                }
            }

            return new HardwareReading
            {
                CpuTotal = cpuTotal,
                CpuCores = cores.OrderBy(x => x.Index).Select(x => Math.Clamp(x.Value, 0, 100)).ToArray(),
                CpuTemperature = cpuTemperatures.Count == 0 ? null : cpuTemperatures.Max(),
                GpuTemperature = gpuTemperatures.Count == 0 ? null : gpuTemperatures.Max(),
                GpuLoad = gpuLoad,
                DiskReadBytesPerSecond = Math.Max(0, diskRead),
                DiskWriteBytesPerSecond = Math.Max(0, diskWrite),
            };
        }
        catch (Exception exception)
        {
            Log.Warn($"Hardware sensor read failed: {exception.Message}");
            return new HardwareReading();
        }
    }

    private void EnsureOpen()
    {
        if (_initializationAttempted)
        {
            return;
        }

        _initializationAttempted = true;
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = false,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
            };
            _computer.Open();
            Log.Info("LibreHardwareMonitor initialized");
        }
        catch (Exception exception)
        {
            _computer = null;
            Log.Warn($"LibreHardwareMonitor unavailable: {exception.Message}");
        }
    }

    private static IEnumerable<IHardware> Walk(IEnumerable<IHardware> roots)
    {
        foreach (var hardware in roots)
        {
            yield return hardware;
            foreach (var child in Walk(hardware.SubHardware))
            {
                yield return child;
            }
        }
    }

    private static bool IsGpu(HardwareType type) =>
        type is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia;

    private static int ExtractTrailingNumber(string value, int fallback)
    {
        var digits = new string(value.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out var number) ? number : fallback;
    }

    public void Dispose()
    {
        try
        {
            _computer?.Close();
        }
        catch
        {
            // Best-effort shutdown.
        }
        _computer = null;
    }
}
