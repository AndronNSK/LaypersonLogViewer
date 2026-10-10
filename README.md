# Layperson Log Viewer

A first iteration of a C# desktop log viewer using **Avalonia UI** and .NET 10.
The interface is in Russian. Avalonia is the assumed meaning of “Avalon UI”.

[Спецификация требований на русском языке](docs/Спецификация.md)

## Run

For a Windows PC without .NET installed, use the self-contained installer.
See [installer build and distribution instructions](installer/README.md).

Install the .NET 10 SDK, then run these commands from the repository folder:

```powershell
dotnet restore LaypersonLogViewer.slnx
dotnet run --project src/LaypersonLogViewer.App
```

Open `samples/example.log` using **Открыть файл…**, or drop one file anywhere in the
main window. Folders and multiple-file drops are ignored. Existing filters and statistics
patterns apply to the newly opened file.

## Streaming (0.2.0)

Pipe a command's UTF-8 output into the app using `--stdin`:

```powershell
cmd /d /c '.\MyApplication.exe 2>&1 | .\LaypersonLogViewer.App.exe --stdin'
```

On Linux with the installed package:

```bash
my-command 2>&1 | layperson-log-viewer --stdin
```

- **Следить за файлом…** reads an existing UTF-8 file and follows appended text.
  It detects truncation and rename/replacement, and waits if the file temporarily disappears.
  Incomplete lines wait for their newline; stopping or rotation retains the final fragment.
- **Запустить команду…** accepts an executable, arguments, and working directory. Both stdout
  and stderr are captured concurrently as UTF-8. There is no interactive stdin. Shell syntax
  requires explicitly launching a shell (for example `powershell.exe` or `/bin/sh`).
- **Стоп** stops capture; for a command started by the viewer it terminates that process and
  its descendants. Closing the window or replacing the source also stops that command.
  Piped producers are external processes and are not terminated by the viewer.
- **Прокрутка** follows the newest displayed lines without changing horizontal scroll.
  Clicking a log line or scrolling up turns it off. Scrolling back to the bottom resumes it;
  the checkbox also lets you resume manually.
- **Пауза показа** freezes displayed lines and their statistics, while capture continues.
  Resuming displays the current retained data.
- **Лимит строк** defaults to 10,000 (100–1,000,000). Oldest received lines are discarded;
  original receipt numbers remain. This limit applies to streams, not ordinary file opening.
  Evicting part of a multiline entry can leave its beginning outside the retained data.
- **Сохранить журнал…** saves a UTF-8 snapshot of all retained lines, including excluded
  lines and arrivals during pause. The snapshot is taken when the button is pressed.

The UI processes snapshots approximately every 500 ms, when no other operation or editor
is active. Existing rows and text controls are retained while new rows are inserted and
expired rows are removed, so text selection continues during incoming batches.
Filters, timestamp grouping, and statistics use those snapshots; statistics cover
retained data, not the entire history of the stream. EOF or process exit leaves captured
lines available for viewing and saving. Source errors and exit codes appear above the log.
Output buffering in the source program can delay delivery. Ordering between stdout and
stderr reflects arrival order; it cannot reconstruct a total order across the two streams.

## Fake log generator

`LaypersonLogViewer.FakeLogs` is a separate .NET 10 console app with no Avalonia dependency.
It produces timestamped INFO/DEBUG/WARN/ERROR entries, Russian text, searchable categories,
numeric `duration`/`size` fields, multiline errors, and occasional long lines. It makes no
network requests. Build from the repository root:

```powershell
dotnet build src/LaypersonLogViewer.FakeLogs -c Release
```

Generate 20 entries, one every 100 ms:

```powershell
.\src\LaypersonLogViewer.FakeLogs\bin\Release\net10.0\LaypersonLogViewer.FakeLogs.exe --count 20 --interval-ms 100
```

Pipe into the viewer (build the full solution first). Use the built executable so build
messages from `dotnet run` do not enter your log:

```powershell
cmd /d /c '.\src\LaypersonLogViewer.FakeLogs\bin\Release\net10.0\LaypersonLogViewer.FakeLogs.exe --interval-ms 100 2>&1 | .\src\LaypersonLogViewer.App\bin\Release\net10.0\LaypersonLogViewer.App.exe --stdin'
```

The `cmd /d /c` wrapper runs the native pipe directly, avoiding PowerShell pipeline
buffering/encoding differences. It works from PowerShell; keep the whole pipeline inside
the single quotes. Press Ctrl+C to stop the generator.

On Linux, after building the solution:

```bash
./src/LaypersonLogViewer.FakeLogs/bin/Release/net10.0/LaypersonLogViewer.FakeLogs --interval-ms 100 2>&1 | ./src/LaypersonLogViewer.App/bin/Release/net10.0/LaypersonLogViewer.App --stdin
```

For **Запустить команду…**, enter the full path to the generator executable and put
`--interval-ms 100` in the arguments field. The viewer captures stdout and stderr together.
For **Следить за файлом…**, run the generator with `--file fake.log`, then select that file.
Relative file paths are resolved against the generator's working directory.

Options: `--interval-ms N` (default 500, minimum 1), `--count N` (positive entry count;
omit to run continuously), `--seed N` (default 42), `--file PATH`, and `--help`.
Stop with Ctrl+C. ERROR entries go to stderr; other entries go to stdout. File mode writes
all entries only to the file, creates it if missing, and appends on subsequent runs.
Every entry is flushed immediately. Existing parent folders are required.
The seed repeats numeric values, while timestamps use the current local time; request
numbers restart at 1 for each run. Count means entries, including multiline entries.
The generator needs .NET 10 and is not included in the viewer installers.

## Viewer features

- One main window with a large, scrollable log pane and a smaller filter pane.
- Drag the divider to adjust the pane sizes.
- **Показать строки…** opens a dialog to add an include filter.
- **Скрыть строки…** opens a dialog to add an exclude filter.
- Each filter searches for literal text and can optionally match case.
- Select text within a log line, then right-click and choose **Показать строки…** or
  **Скрыть строки…** to open a filter dialog prefilled with that exact selection.
  Confirm with **Добавить**, or cancel without changing the filters. This works on greyed-out
  lines too. Empty or whitespace-only selections cannot create filters.
- Add multiple filters, remove individual filters, or reset all filters.
- Original line numbers and file order are preserved. Duplicate and empty lines are retained when they match the rules.
- The switch above the log chooses how to display filtered-out lines: hide them (default)
  or keep them greyed out for context. Matching lines remain at normal contrast. The status
  shows both the displayed count and the match count; switching modes keeps all filters.
- Opening another file keeps the filters. A failed read keeps the previous file and shows an error.
- Multi-line entries can be grouped by a timestamp selected in a line. Entry boundaries are
  marked by thin separators, and filters then include or exclude complete entries.
- Local files are opened read-only with `FileShare.ReadWrite | FileShare.Delete`, so a logger
  can keep writing or rotate the file while it is being read. The logger must itself allow
  readers; an exclusive lock cannot be bypassed. Reopen the file to load newer lines.

### How filters combine

A line is shown if **there are no include filters, or at least one include filter matches**,
and **no exclude filter matches**. An exclude filter wins over an include filter.

Each filter is a group: its original condition and all child conditions must match (**AND**).
Use **Добавить условие…** beside a filter to add a child. The dialog uses selected log text
when available, and lets you edit it and choose case sensitivity independently.
Children appear indented as **И содержит**. Deleting a child removes only that condition;
deleting its parent removes the whole group. The status counts groups.

For example, include `ERROR` with children `database` and `timeout`, include `WARN`
as a separate group, and exclude `healthcheck`:

```text
((ERROR AND database AND timeout) OR WARN) AND NOT healthcheck
```

In line mode all conditions must match the same line. With timestamp grouping, they may
match different lines within the same entry, but never different entries. Exclusion groups
also use AND: excluding `DEBUG` with child `heartbeat` hides only entries matching both.
Children have one level; nested groups are not supported.

Example: add include filters `ERROR` and `WARN`, then exclude `localhost`.
The sample file shows lines **4 and 5**. Resetting filters restores all eight lines.

Matching ignores case by default, including Cyrillic case. Spaces around a term are significant.
Empty or whitespace-only terms cannot be added. Characters such as `.` and `*` are literal,
not regular expressions.

### Multi-line log entries

1. Open `samples/multiline.log`.
2. Select just `2026-10-03 12:34:56` in line 2, then right-click and choose
   **Начало записи по времени…**.
3. The dialog previews `####-##-## ##:##:##` at position 1 and reports four matching
   entry starts in the full file. Each `#` generated from a digit represents any ASCII digit;
   separators, letters, spaces, and the selected starting position must match exactly.
4. Click **Применить**. Each matching line begins an entry; subsequent lines belong to
   it until the next match. The preamble before the first timestamp is a separate entry,
   so this example has five entries in total.
5. Add an include filter for `ERROR`: lines 4–7 stay together, including the stack trace
   and blank line. Searching for `IOException` also keeps that entire entry. An exclude
   match on any continuation line removes the whole entry.

The grey-out switch also applies to complete entries. **Сбросить шаблон** returns to
line-by-line filtering while keeping your filters and display mode. Repeating the selection
workflow replaces the pattern. The pattern stays active when opening another file, but is
not saved between app launches. If it finds no timestamps, all lines form one entry and
a warning explains how to choose another pattern or reset it.

This is structural matching, not calendar-date validation. Choose a fixed-width numeric
timestamp; variable-width prefixes or textual month names that change are not generalized.
Only the selected segment is matched, so you can omit fractional seconds from the selection
when their length varies. Filter text is searched within each physical line, never across
line breaks. Other timestamps in the middle of a message do not start an entry unless they
match the same shape at the selected position.

### Statistics from regex captures

The lower pane has **Фильтры** and **Статистика** tabs. Open a log, right-click a line,
and choose **Создать статистику…**. The editor receives the complete line, even if only
a substring was selected. **Добавить шаблон…** on the statistics tab also opens the editor.

For `Request completed: duration=125.4 ms, size=2048 bytes`:

1. Mark `Request completed: duration=` as **Постоянный текст**.
2. Mark `125.4` as **Числовое значение…**, named `Duration`.
3. Mark ` ms, size=` as another constant and `2048` as a value named `Size`.
4. Check the preview and save. Anchors are blue; numeric ranges are green. Unmarked gaps
   may vary. Each numeric group has an editable display name.

Each pattern expands into separate value rows showing count, mean, minimum, maximum,
and exact median. Hover for full precision and skipped-value counts. Results use the
first regex match in each physical line. Invalid or missing numeric captures are skipped;
unmatched lines do not count. Signed integers, dot/comma decimals, and exponents are
supported, with no thousands separators or unit conversion.

**Редактировать regex** enables advanced editing using named groups such as
`duration=(?<duration>[0-9]+(?:[.,][0-9]+)?)`. Unnamed groups do not create statistics.
Returning to the visual builder asks before replacing manual edits. Matching stays
within a physical line; timestamp grouping affects which lines pass the filters.

Choose **После фильтрации** (default) or **Весь файл**. Filtered-out grey lines are excluded
from filtered statistics. Calculations refresh in the background after patterns, scope,
filters, grouping, or the loaded file change. Regexes have a 100 ms per-line timeout;
a failed pattern shows an error instead of partial results, while other patterns continue.

Both tabs have **Загрузить…** and **Сохранить…** buttons for separate JSON files.
Statistics files include pattern definitions, examples, markings, names, and scope;
filter files include show/hide groups, child conditions, and each condition's case setting.
Loading replaces the current set in that tab and recalculates the loaded log. Invalid files
and cancelled dialogs leave the current set unchanged. Local saves use a temporary file
and replacement to avoid incomplete files.

Each launch starts with empty filters and statistics. There is no automatic loading or
saving; save any definitions you want to reuse before closing. Results and captured values
are not saved. An older `%LOCALAPPDATA%/LaypersonLogViewer/statistics.json` can still be
loaded explicitly from the statistics tab.

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
- `TimestampPattern.cs`: remembers the selected timestamp and its character position;
  digits may change while other characters stay literal. It matches directly without regex.
- `LogEntryParser.cs` and `LogEntry.cs`: group physical lines into entries before filtering.
  `LogFilterEngine.ApplyEntries` evaluates include/exclude rules against the whole entry.

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
- `Views/TimestampPatternDialog.axaml` previews the selected entry-start pattern and match
  count. Confirming returns the pattern; cancellation leaves the current grouping unchanged.

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
- Filters live only for the current session. Regex-based filtering, time/level filters, saved filter presets,
  live tailing, and filter editing are possible follow-up iterations.
- Without a timestamp pattern, each physical line is independent. With a pattern, lines
  are grouped into entries; individual fields such as timestamp values and severity are not parsed.

## Framework references

- [Avalonia getting started](https://docs.avaloniaui.net/docs/get-started)
- [Avalonia headless xUnit testing](https://docs.avaloniaui.net/docs/testing/headless-xunit)
- [Avalonia storage provider](https://docs.avaloniaui.net/api/avalonia/platform/storage/istorageprovider)
