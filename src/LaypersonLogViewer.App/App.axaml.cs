using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LaypersonLogViewer.App.Views;

namespace LaypersonLogViewer.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                await window.InitializeSettingsAsync(new Core.AppSettingsStore(Core.AppSettingsStore.DefaultPath));
                await window.StartFromArgumentsAsync(desktop.Args ?? []);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
