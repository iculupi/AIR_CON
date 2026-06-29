using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FanController.Services;

namespace FanController.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly BleService _ble;
    private bool _suppressSpeedWrite; // zapobiega wysyłaniu PWM gdy slider aktualizowany z BLE

    // ── Observable state ────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleFanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetPresetCommand))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FanToggleText))]
    [NotifyPropertyChangedFor(nameof(FanToggleColor))]
    private bool _isFanOn = true;

    [ObservableProperty]
    private double _fanSpeed = 50;

    [ObservableProperty]
    private int _rpm;

    [ObservableProperty]
    private string _statusText = "Rozłączony";

    // ── Derived ─────────────────────────────────────────────────────────────

    public string ConnectButtonText => IsScanning ? "Skanowanie…" : (IsConnected ? "Połączono" : "Szukaj i połącz");
    public string FanToggleText     => IsFanOn ? "Wentylator: ON" : "Wentylator: OFF";
    public Color  FanToggleColor    => IsFanOn ? Color.FromArgb("#00FF88") : Color.FromArgb("#FF4444");

    // ── Constructor ─────────────────────────────────────────────────────────

    public MainViewModel(BleService ble)
    {
        _ble = ble;
        _ble.SpeedReceived      += OnSpeedReceived;
        _ble.RpmReceived        += OnRpmReceived;
        _ble.ConnectionChanged  += OnConnectionChanged;
        _ble.StatusChanged      += OnStatusChanged;
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        IsScanning = true;
        try
        {
            await _ble.ScanAndConnectAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool CanConnect() => !IsScanning && !IsConnected;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        await _ble.DisconnectAsync();
    }

    private bool CanDisconnect() => IsConnected;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task ToggleFanAsync()
    {
        IsFanOn = !IsFanOn;
        byte speed = IsFanOn ? (byte)Math.Round(FanSpeed) : (byte)0;
        await _ble.SetSpeedAsync(speed);
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task SetPresetAsync(string parameter)
    {
        if (!byte.TryParse(parameter, out byte preset)) return;
        IsFanOn = preset > 0;
        if (preset > 0) FanSpeed = preset;
        await _ble.SetSpeedAsync(preset);
    }

    // ── Slider value changed ────────────────────────────────────────────────

    partial void OnFanSpeedChanged(double value)
    {
        if (_suppressSpeedWrite || !IsConnected || !IsFanOn) return;
        _ = _ble.SetSpeedAsync((byte)Math.Round(value));
    }

    // ── BLE event handlers ──────────────────────────────────────────────────

    private void OnSpeedReceived(object? sender, byte speed)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _suppressSpeedWrite = true;
            if (speed == 0)
            {
                IsFanOn = false;
            }
            else
            {
                IsFanOn   = true;
                FanSpeed  = speed;
            }
            _suppressSpeedWrite = false;
        });
    }

    private void OnRpmReceived(object? sender, ushort rpm)
    {
        MainThread.BeginInvokeOnMainThread(() => Rpm = rpm);
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsConnected = connected;
            IsScanning  = false;
        });
    }

    private void OnStatusChanged(object? sender, string status)
    {
        MainThread.BeginInvokeOnMainThread(() => StatusText = status);
    }
}
