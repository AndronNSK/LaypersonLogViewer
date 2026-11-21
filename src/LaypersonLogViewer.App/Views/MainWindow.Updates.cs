using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

public partial class MainWindow
{
    private AppSettings _settings = new();
    private AppSettingsStore? _settingsStore;
    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private readonly HttpClient _updateClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private bool _checkingUpdate;
    private readonly UpdateCheckSchedule _updateSchedule = new(Path.Combine(
        Path.GetDirectoryName(AppSettingsStore.DefaultPath)!, "update-check.json"));

    // Desktop startup supplies the store. Headless tests do not access user settings or the network.
    public async Task InitializeSettingsAsync(AppSettingsStore store)
    {
        _settingsStore = store;
        try { _settings = await store.LoadAsync(); ViewModel.Live.RetainedLines = _settings.LineLimit; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { ViewModel.ReportError("Не удалось прочитать настройки. " + error.Message); }
        _updateTimer.Tick += async (_, _) => await CheckUpdatesAsync(false);
        _updateTimer.Start();
        Dispatcher.UIThread.Post(async () => await CheckUpdatesAsync(false), DispatcherPriority.Background);
    }

    private async Task CheckUpdatesAsync(bool manual)
    {
        if (_checkingUpdate || _streamWindowClosed || (!manual && !_settings.CheckForUpdates)) return;
        if (!manual && OwnedWindows.Count > 0) return;
        _checkingUpdate = true;
        var downloadRequested = false;
        try
        {
            var platform = RuntimeInformation.ProcessArchitecture != Architecture.X64 ? ""
                : OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsLinux() ? "linux-x64" : "";
            var version = typeof(MainWindow).Assembly.GetName().Version!;
            var current = new Version(version.Major, version.Minor, version.Build);
            var service = new UpdateService(_updateClient);
            using var checkTimeout = CancellationTokenSource.CreateLinkedTokenSource(_updateCancellation.Token);
            checkTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            var result = await _updateSchedule.CheckAsync(service, current, platform, manual, checkTimeout.Token);
            if (!result.Checked)
            {
                if (manual && result.RetryAfter is { } retryAt && !_streamWindowClosed)
                    await ShowMenuInfoAsync("Обновления", $"Проверка временно отложена. Повторить после {retryAt.LocalDateTime:g}.");
                return;
            }
            var update = result.Update;
            if (_streamWindowClosed || OwnedWindows.Count > 0) return;
            if (update is null)
            {
                if (manual) await ShowMenuInfoAsync("Обновления", "Доступных обновлений нет.");
                return;
            }
            var directory = Path.Combine(Path.GetDirectoryName(AppSettingsStore.DefaultPath)!, "updates");
            using var downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_updateCancellation.Token);
            Window? progress = null;
            string? file;
            try
            {
                file = await service.DownloadAsync(update, directory, async available =>
                {
                    if (!await ConfirmUpdateAsync("Обновление",
                        $"Доступна версия {available.Version}. Скачать {available.FileName} ({available.Size / 1_000_000.0:F1} МБ)?",
                        "Скачать")) return false;
                    downloadRequested = true;
                    var cancel = new Button { Content = "Отмена" };
                    progress = new Window
                    {
                        Title = "Загрузка обновления", Width = 360, SizeToContent = SizeToContent.Height,
                        CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Content = new StackPanel { Margin = new Thickness(12), Spacing = 8, Children =
                        {
                            new TextBlock { Text = "Загрузка и проверка установщика…" },
                            new ProgressBar { IsIndeterminate = true },
                            cancel
                        } }
                    };
                    cancel.Click += (_, _) => downloadCancellation.Cancel();
                    progress.Closed += (_, _) => downloadCancellation.Cancel();
                    progress.Show(this);
                    return true;
                }, downloadCancellation.Token);
            }
            finally { progress?.Close(); }
            if (file is null || _streamWindowClosed) return;
            if (await ConfirmUpdateAsync("Обновление загружено",
                $"Загружен файл {file}\nОткрыть установщик?", "Открыть установщик"))
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException
            or JsonException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            if (error is UpdateRateLimitException limited)
            {
                try { await _updateSchedule.DeferAsync(limited.RetryAt); }
                catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException or JsonException) { }
            }
            if (!_streamWindowClosed && error is OperationCanceledException)
            {
                if (manual || downloadRequested) await ShowMenuInfoAsync("Обновления", "Проверка или загрузка отменена либо истекло время ожидания.");
            }
            else if (!_streamWindowClosed && (manual || downloadRequested))
                await ShowMenuInfoAsync("Обновления", "Не удалось проверить или загрузить обновление. " + error.Message);
        }
        finally { _checkingUpdate = false; }
    }

    private Task<bool> ConfirmUpdateAsync(string title, string message, string action)
    {
        var accept = new Button { Content = action, IsDefault = true };
        var cancel = new Button { Content = "Отмена", IsCancel = true };
        var dialog = new Window
        {
            Title = title, Width = 460, SizeToContent = SizeToContent.Height,
            CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(12), Spacing = 8,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 6, Children = { cancel, accept } }
                }
            }
        };
        accept.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        return dialog.ShowDialog<bool>(this);
    }
}
