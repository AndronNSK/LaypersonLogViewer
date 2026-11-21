using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

public partial class MainWindow
{
    private bool _navigatingSearch;
    private long _searchNavigation;

    private void Search_Click(object? sender, RoutedEventArgs e) => OpenSearch();
    private async void SearchNext_Click(object? sender, RoutedEventArgs e) => await NavigateSearchAsync();
    private async void SearchPrevious_Click(object? sender, RoutedEventArgs e) => await NavigateSearchAsync(true);
    private void CloseSearch_Click(object? sender, RoutedEventArgs e) => CloseSearch();

    private void OpenSearch()
    {
        var selection = SingleLineSelection;
        if (!string.IsNullOrEmpty(selection)) ViewModel.Search.Text = selection;
        ViewModel.Search.IsVisible = true;
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void CloseSearch()
    {
        _searchNavigation++;
        ViewModel.Search.IsVisible = false;
        ActiveLogText?.Focus();
    }

    private async void Search_KeyDown(object? sender, KeyEventArgs e)
    {
        if (OwnedWindows.Count > 0 || MainMenuButton.Flyout is { IsOpen: true }) return;
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        { e.Handled = true; OpenSearch(); }
        else if ((e.Key == Key.F3 && (e.KeyModifiers & ~KeyModifiers.Shift) == 0)
            || (e.Key == Key.Enter && ReferenceEquals(e.Source, SearchTextBox)))
        {
            e.Handled = true;
            ViewModel.Search.IsVisible = true;
            if (ViewModel.Search.Text.Length == 0) SearchTextBox.Focus();
            else await NavigateSearchAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }
        else if (e.Key == Key.Escape && ViewModel.Search.IsVisible)
        { e.Handled = true; CloseSearch(); }
    }

    private async Task NavigateSearchAsync(bool backwards = false)
    {
        var navigation = ++_searchNavigation;
        var query = ViewModel.Search.Text;
        var caseSensitive = ViewModel.Search.CaseSensitive;
        ViewModel.Live.FollowLatest = false;
        var match = await ViewModel.Search.FindAsync(backwards);
        if (match is null || _streamWindowClosed || navigation != _searchNavigation) return;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_streamWindowClosed || navigation != _searchNavigation || !ViewModel.Search.IsVisible
                || ViewModel.Search.Text != query || ViewModel.Search.CaseSensitive != caseSensitive) return;
            var lines = ViewModel.ShowFilteredOut ? ViewModel.AllLines : ViewModel.VisibleLines;
            var index = LogSearch.IndexOfLine(lines, match.Line);
            if (index < 0) return;
            _navigatingSearch = true;
            try
            {
                LogLines.SelectedIndex = index;
                LogLines.ScrollIntoView(index);
                LogLines.UpdateLayout();
                if (LogLines.ContainerFromIndex(index) is not { } container) return;
                var text = container.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                if (text is null) return;
                ClearLogTextRange();
                ActivateLogText(text);
                var row = (ViewModels.LogLineRow)text.DataContext!;
                var start = row.DisplayOffset(match.Start, ViewModel.FormatJson);
                var end = row.DisplayOffset(match.Start + match.Length, ViewModel.FormatJson);
                text.CaretIndex = end;
                text.SelectionStart = start;
                text.SelectionEnd = end;
                // Explicit search navigation may reveal text outside the horizontal viewport.
                var presenter = text.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
                if (presenter is not null)
                {
                    var caret = presenter.TextLayout.HitTestTextPosition(start);
                    presenter.BringIntoView(new Rect(caret.X, caret.Y, 1, caret.Height));
                }
            }
            finally { ViewModel.Live.FollowLatest = false; _navigatingSearch = false; }
        }, DispatcherPriority.Loaded);
    }
}
