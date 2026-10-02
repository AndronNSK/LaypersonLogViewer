# Layperson Log Viewer

A first iteration of a C# desktop log viewer using **Avalonia UI** and .NET 10.
The interface is in Russian. Avalonia is the assumed meaning of “Avalon UI”.

## Run

Install the .NET 10 SDK, then run these commands from the repository folder:

```powershell
dotnet restore LaypersonLogViewer.slnx
dotnet run --project src/LaypersonLogViewer.App
```

Open `samples/example.log` using **Открыть файл…**.

## First iteration

- One main window with a large, scrollable log pane and a smaller filter pane.
- Drag the divider to adjust the pane sizes.
- **Показать строки...** opens a dialog to add an include filter.
- **Скрыть строки** opens a dialog to add an exclude filter.
- Each filter searches for literal text and can optionally match case.
- Add multiple filters, remove individual filters, or reset all filters.
- Original line numbers and file order are preserved. Duplicate and empty lines are retained when they match the rules.
- The switch above the log chooses how to display filtered-out lines: hide them (default)
  or keep them greyed out for context. Matching lines remain at normal contrast. The status
  shows both the displayed count and the match count; switching modes keeps all filters.
- Opening another file keeps the filters. A failed read keeps the previous file and shows an error.
- Local files are opened read-only with `FileShare.ReadWrite | FileShare.Delete`, so a logger
  can keep writing or rotate the file while it is being read. The logger must itself allow
  readers; an exclusive lock cannot be bypassed. Reopen the file to load newer lines.

### How filters combine

A line is shown if **there are no include filters, or at least one include filter matches**,
and **no exclude filter matches**. An exclude filter wins over an include filter.

Example: add include filters `ERROR` and `WARN`, then exclude `localhost`.
The sample file shows lines **4 and 5**. Resetting filters restores all eight lines.

Matching ignores case by default, including Cyrillic case. Spaces around a term are significant.
Empty or whitespace-only terms cannot be added. Characters such as `.` and `*` are literal,
not regular expressions.

## Tests

```powershell
dotnet test LaypersonLogViewer.slnx
dotnet build LaypersonLogViewer.slnx --configuration Release
```

Tests use xUnit. They cover filter combinations, case sensitivity, Cyrillic, line numbers,
duplicates, blank lines, encoding, read failures, filter removal/reset, loading another file,
and empty states. Avalonia headless tests create the actual windows, activate the two filter
buttons, validate and submit dialogs, cancel a dialog, and check the log-list binding.
Headless means no desktop window needs to appear while tests run.

## Code map and C# refresher

### 1. Core: plain C# rules

`src/LaypersonLogViewer.Core` has no UI dependency:

- `LogLine.cs`: one line and its original number. A **record** is a concise data type with
  generated value equality. Two `LogLine` values with the same contents compare equal.
- `LogFilter.cs`: a search term, include/exclude kind, and case option. Its constructor
  rejects invalid input, so callers cannot create an empty filter.
- `LogFilterEngine.cs`: evaluates the rules. LINQ's `Where` selects matching items and
  `Any` asks whether at least one item matches a condition.
- `LogFileReader.cs`: reads lines from a stream using `async`/`await`. The caller owns the
  stream; the reader deliberately leaves it open.

### 2. App: layout, state, and interaction

`src/LaypersonLogViewer.App` contains the desktop app:

- `Program.cs` starts Avalonia. `App.axaml` chooses the visual theme.
- `Views/MainWindow.axaml` describes the layout in **XAML**, an XML-based language for UI.
  It is similar in spirit to WPF XAML, but these are Avalonia controls.
- `ViewModels/MainWindowViewModel.cs` stores the loaded lines, visible lines, filters,
  busy state, and errors. This is the **view model** in the Model–View–ViewModel pattern.
- `ViewModels/LogLineRow.cs` adds display state to an original line: whether the filters
  reject it and how strongly to dim it. The view model caches all rows and matching rows,
  so the display switch can change lists without rereading or refiltering the file.
- A binding such as `ItemsSource="{Binding DisplayLines}"` connects a control to a
  property on its `DataContext` (here, the view model). `INotifyPropertyChanged` tells
  bindings to refresh after state changes. `ObservableCollection` also reports additions
  and removals, so the list of filters updates automatically.
- `Views/MainWindow.axaml.cs` contains the short UI event handlers: open the native file
  picker, open a filter dialog, and call the view model. These files are called code-behind.
- `Views/FilterDialog.axaml` and its code-behind collect and validate a new filter. Confirming
  returns a `LogFilter`; cancelling returns `null`.

`async Task` methods return work that the caller can await. `await` lets the UI process events
while that work is unfinished. `Task.Run` moves the file-reading/filtering work off the UI thread;
the continuation updates bound properties back on that thread. Controls are disabled during
processing to prevent overlapping changes. Only UI event handlers use `async void` because
event signatures require it; the underlying operations return testable `Task` values.

Nullable reference types are enabled. A type like `string?` can be `null`; `string` is intended
to be non-null. The compiler checks many accidental null uses. `using` / `await using`
dispose resources when their scope ends, including on errors.

The solution uses the newer XML `.slnx` format. `Directory.Build.props` shares the .NET target,
nullable settings, and compiler checks across all three projects. NuGet dependencies and their
versions are listed explicitly in each `.csproj`. There is no extra MVVM framework in this
iteration, so the binding and notification code is visible and easy to follow.

### 3. Tests: behavior you can change safely

`tests/LaypersonLogViewer.Tests` references the app and core. `[Fact]` is one test;
`[Theory]` runs a test with several `[InlineData]` inputs. `[AvaloniaFact]` and
`[AvaloniaTheory]` additionally provide Avalonia's UI thread and a headless platform.

## Current boundaries

- The file is a snapshot held in memory. The list virtualizes visible rows, but this is not
  yet a streaming or multi-gigabyte file viewer. Loading and filtering run in the background;
  operations cannot yet be cancelled from the UI.
- UTF-8 is the default. UTF-8, UTF-16, and UTF-32 byte-order marks are detected. Legacy
  encodings such as Windows-1251 need a future encoding selector.
- Filters live only for the current session. Regex, time/level filters, saved presets,
  live tailing, and filter editing are possible follow-up iterations.
- The current interface does not parse log formats; each physical line is independent.

## Framework references

- [Avalonia getting started](https://docs.avaloniaui.net/docs/get-started)
- [Avalonia headless xUnit testing](https://docs.avaloniaui.net/docs/testing/headless-xunit)
- [Avalonia storage provider](https://docs.avaloniaui.net/api/avalonia/platform/storage/istorageprovider)
