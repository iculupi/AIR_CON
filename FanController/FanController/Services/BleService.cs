using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace FanController.Services;

public class BleService : IAsyncDisposable
{
    private const string ServiceUuid    = "4fafc201-1fb5-459e-8fcc-c5c9c331914b";
    private const string SpeedCharUuid  = "beb5483e-36e1-4688-b7f5-ea07361b26a8";
    private const string RpmCharUuid    = "beb5483e-36e1-4688-b7f5-ea07361b26a9";
    private const string DeviceName     = "Fan Controller ESP32C3";

    private readonly IBluetoothLE _ble;
    private readonly IAdapter _adapter;
    private IDevice? _device;
    private ICharacteristic? _speedChar;
    private ICharacteristic? _rpmChar;
    private CancellationTokenSource? _scanCts;

    public event EventHandler<byte>? SpeedReceived;
    public event EventHandler<ushort>? RpmReceived;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<string>? StatusChanged;

    public bool IsConnected => _device?.State == DeviceState.Connected;
    public bool IsScanning  => _adapter.IsScanning;

    public BleService()
    {
        _ble     = CrossBluetoothLE.Current;
        _adapter = CrossBluetoothLE.Current.Adapter;

        _adapter.ScanTimeout         = 12_000;
        _adapter.DeviceDiscovered   += OnDeviceDiscovered;
        _adapter.DeviceConnected    += OnDeviceConnected;
        _adapter.DeviceDisconnected += OnDeviceDisconnected;
        _adapter.DeviceConnectionLost += OnDeviceConnectionLost;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    public async Task ScanAndConnectAsync(CancellationToken externalCt = default)
    {
        if (_ble.State != BluetoothState.On)
            throw new InvalidOperationException("Bluetooth jest wyłączony. Włącz BT i spróbuj ponownie.");

        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        StatusChanged?.Invoke(this, "Skanowanie BLE…");

        try
        {
            await _adapter.StartScanningForDevicesAsync(cancellationToken: _scanCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Skanowanie zatrzymane ręcznie lub przez znalezienie urządzenia
        }
    }

    public async Task StopScanAsync()
    {
        _scanCts?.Cancel();
        if (_adapter.IsScanning)
            await _adapter.StopScanningForDevicesAsync();
    }

    public async Task SetSpeedAsync(byte speed)
    {
        if (_speedChar == null || !IsConnected) return;

        try
        {
            await _speedChar.WriteAsync([speed]);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Błąd zapisu: {ex.Message}");
        }
    }

    public async Task DisconnectAsync()
    {
        if (_device != null)
        {
            try { await _adapter.DisconnectDeviceAsync(_device); }
            catch { /* ignoruj błąd rozłączania */ }
        }
    }

    // ── Adapter events ──────────────────────────────────────────────────────

    private async void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
    {
        if (!string.Equals(e.Device.Name, DeviceName, StringComparison.OrdinalIgnoreCase))
            return;

        await StopScanAsync();
        StatusChanged?.Invoke(this, "Znaleziono urządzenie – łączenie…");

        try
        {
            await _adapter.ConnectToDeviceAsync(e.Device);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Błąd połączenia: {ex.Message}");
            ConnectionChanged?.Invoke(this, false);
        }
    }

    private async void OnDeviceConnected(object? sender, DeviceEventArgs e)
    {
        _device = e.Device;
        StatusChanged?.Invoke(this, "Połączono – konfigurowanie BLE…");

        try
        {
            await SetupCharacteristicsAsync();
            StatusChanged?.Invoke(this, $"Połączono: {DeviceName}");
            ConnectionChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Błąd konfiguracji BLE: {ex.Message}");
        }
    }

    private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
    {
        CleanupCharacteristics();
        StatusChanged?.Invoke(this, "Rozłączono");
        ConnectionChanged?.Invoke(this, false);
    }

    private void OnDeviceConnectionLost(object? sender, DeviceErrorEventArgs e)
    {
        CleanupCharacteristics();
        StatusChanged?.Invoke(this, $"Utracono połączenie: {e.ErrorMessage}");
        ConnectionChanged?.Invoke(this, false);
    }

    // ── BLE characteristics ─────────────────────────────────────────────────

    private async Task SetupCharacteristicsAsync()
    {
        if (_device == null) return;

        var service = await _device.GetServiceAsync(Guid.Parse(ServiceUuid))
            ?? throw new InvalidOperationException("Nie znaleziono usługi BLE na urządzeniu.");

        _speedChar = await service.GetCharacteristicAsync(Guid.Parse(SpeedCharUuid));
        _rpmChar   = await service.GetCharacteristicAsync(Guid.Parse(RpmCharUuid));

        if (_speedChar is not null)
        {
            _speedChar.ValueUpdated += OnSpeedValueUpdated;
            await _speedChar.StartUpdatesAsync();
        }

        if (_rpmChar is not null)
        {
            _rpmChar.ValueUpdated += OnRpmValueUpdated;
            await _rpmChar.StartUpdatesAsync();
        }
    }

    private void CleanupCharacteristics()
    {
        if (_speedChar is not null)
        {
            _speedChar.ValueUpdated -= OnSpeedValueUpdated;
            _speedChar = null;
        }
        if (_rpmChar is not null)
        {
            _rpmChar.ValueUpdated -= OnRpmValueUpdated;
            _rpmChar = null;
        }
        _device = null;
    }

    private void OnSpeedValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        var bytes = e.Characteristic.Value;
        if (bytes?.Length > 0)
            SpeedReceived?.Invoke(this, bytes[0]);
    }

    private void OnRpmValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        var bytes = e.Characteristic.Value;
        if (bytes?.Length >= 2)
        {
            ushort rpm = (ushort)(bytes[0] | (bytes[1] << 8));
            RpmReceived?.Invoke(this, rpm);
        }
    }

    // ── Cleanup ─────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _adapter.DeviceDiscovered       -= OnDeviceDiscovered;
        _adapter.DeviceConnected        -= OnDeviceConnected;
        _adapter.DeviceDisconnected     -= OnDeviceDisconnected;
        _adapter.DeviceConnectionLost   -= OnDeviceConnectionLost;
        _scanCts?.Dispose();
    }
}
