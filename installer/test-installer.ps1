[CmdletBinding()]
param([Parameter(Mandatory = $true)][string] $Path)

$ErrorActionPreference = 'Stop'
$packagePath = [IO.Path]::GetFullPath($Path)
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($packagePath, 0)

function Read-Rows([string] $query, [string[]] $columns) {
    $view = $database.OpenView($query)
    [void]$view.Execute()
    try {
        while ($record = $view.Fetch()) {
            $row = [ordered]@{}
            for ($index = 0; $index -lt $columns.Count; $index++) {
                $row[$columns[$index]] = $record.StringData($index + 1)
            }
            [pscustomobject]$row
        }
    }
    finally { [void]$view.Close() }
}

function Assert-That([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
}

$feature = @(Read-Rows 'SELECT `Level`, `Display`, `Attributes` FROM `Feature` WHERE `Feature` = ''ExplorerIntegration''' @('Level', 'Display', 'Attributes'))
Assert-That ($feature.Count -eq 1 -and $feature.Level -eq '1' -and $feature.Display -eq '0' -and (([int]$feature.Attributes -band 16) -eq 0)) 'Explorer integration must be optional and selected by default, with its own checkbox.'
$owners = @(Read-Rows 'SELECT `Feature_` FROM `FeatureComponents` WHERE `Component_` = ''ExplorerContextMenu''' @('Feature'))
Assert-That ($owners.Count -eq 1 -and $owners.Feature -eq 'ExplorerIntegration') 'The mandatory application feature must not install the Explorer menu.'
$checkbox = @(Read-Rows 'SELECT `Type`, `Property`, `Text`, `Attributes` FROM `Control` WHERE `Dialog_` = ''ExplorerOptionsDlg'' AND `Control` = ''ExplorerMenu''' @('Type', 'Property', 'Text', 'Attributes'))
Assert-That ($checkbox.Count -eq 1 -and $checkbox.Type -eq 'CheckBox' -and $checkbox.Property -eq 'ADD_EXPLORER_MENU' -and $checkbox.Text -match 'LP LogViewer' -and (([int]$checkbox.Attributes -band 3) -eq 3)) 'The menu checkbox must be visible and enabled.'
$checkedValue = @(Read-Rows 'SELECT `Value` FROM `CheckBox` WHERE `Property` = ''ADD_EXPLORER_MENU''' @('Value'))
$defaultValue = @(Read-Rows 'SELECT `Value` FROM `Property` WHERE `Property` = ''ADD_EXPLORER_MENU''' @('Value'))
Assert-That ($checkedValue.Value -eq '1' -and $defaultValue.Value -eq '1') 'A new installation must start with the checkbox checked.'

$registry = @(Read-Rows 'SELECT `Root`, `Key`, `Name`, `Value` FROM `Registry` WHERE `Component_` = ''ExplorerContextMenu''' @('Root', 'Key', 'Name', 'Value'))
$key = 'Software\Classes\*\shell\LPLogViewer'
Assert-That ($registry.Count -ge 4 -and @($registry | Where-Object Root -ne '1').Count -eq 0) 'Explorer integration must be per-user.'
$label = $registry | Where-Object { $_.Key -eq $key -and $_.Name -eq 'MUIVerb' }
$command = $registry | Where-Object { $_.Key -eq "$key\command" -and -not $_.Name }
Assert-That ($label.Value -eq 'Открыть в LP LogViewer…') 'Incorrect menu label.'
Assert-That ($command.Value -eq '"[INSTALLFOLDER]LaypersonLogViewer.App.exe" --open "%1"') 'The executable and file argument must be quoted.'
Assert-That (@($registry | Where-Object { $_.Key -eq $key -and $_.Name -eq '-' }).Count -eq 1) 'Uninstall must remove the menu registry key.'

$events = @(Read-Rows 'SELECT `Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering` FROM `ControlEvent`' @('Dialog', 'Control', 'Event', 'Argument', 'Condition', 'Order'))

# Open a planning-only session that ignores installed machine state. Never run
# InstallExecute, InstallFinalize, or any action that installs files or registry keys.
$session = $installer.OpenPackage($packagePath, 1)
foreach ($action in @('CostInitialize', 'FileCost', 'CostFinalize')) {
    Assert-That ($session.DoAction($action) -eq 1) "Planning action failed: $action"
}

function Get-SessionValue([string] $property, [string] $name) {
    $session.GetType().InvokeMember($property, [Reflection.BindingFlags]::GetProperty, $null, $session, @($name))
}
function Set-SessionValue([string] $property, [string] $name, [object] $value) {
    [void]$session.GetType().InvokeMember($property, [Reflection.BindingFlags]::SetProperty, $null, $session, @($name, $value))
}
function Invoke-Button([string] $dialog, [string] $control) {
    $destination = $null
    foreach ($event in ($events | Where-Object { $_.Dialog -eq $dialog -and $_.Control -eq $control } | Sort-Object { [int]$_.Order })) {
        if ($session.EvaluateCondition($event.Condition) -ne 1) { continue }
        switch ($event.Event) {
            'AddLocal' { Set-SessionValue 'FeatureRequestState' $event.Argument 3 }
            'Remove' { Set-SessionValue 'FeatureRequestState' $event.Argument 2 }
            'NewDialog' { $destination = $event.Argument }
            default {
                if ($event.Event -match '^\[(.+)\]$') {
                    $value = if ($event.Argument -eq '{}') { '' } else { $event.Argument }
                    Set-SessionValue 'Property' $Matches[1] $value
                }
            }
        }
    }
    return $destination
}

Assert-That ((Get-SessionValue 'FeatureRequestState' 'ExplorerIntegration') -eq 3) 'Default planning must install Explorer integration.'
Assert-That ((Invoke-Button 'CustomizeDlg' 'Next') -eq 'ExplorerOptionsDlg') 'Setup must show the checkbox before confirmation.'
Assert-That ((Get-SessionValue 'Property' 'ADD_EXPLORER_MENU') -eq '1') 'Default planned feature must initialize a checked checkbox.'
foreach ($checked in @($true, $false, $true)) {
    Set-SessionValue 'Property' 'ADD_EXPLORER_MENU' $(if ($checked) { '1' } else { '' })
    Assert-That ((Invoke-Button 'ExplorerOptionsDlg' 'Next') -eq 'VerifyReadyDlg') 'The checkbox page must proceed to confirmation.'
    $expected = if ($checked) { 3 } else { 2 }
    Assert-That ((Get-SessionValue 'FeatureRequestState' 'ExplorerIntegration') -eq $expected) "Incorrect planned menu state for checked=$checked."
    Assert-That ((Get-SessionValue 'FeatureRequestState' 'MainApplication') -eq 3) 'Toggling the menu must leave the application selected.'
    Assert-That ((Invoke-Button 'VerifyReadyDlg' 'Back') -eq 'ExplorerOptionsDlg') 'Back must return to the checkbox page.'
    Assert-That ((Invoke-Button 'ExplorerOptionsDlg' 'Back') -eq 'CustomizeDlg') 'Back must return to component selection.'
    [void](Invoke-Button 'CustomizeDlg' 'Next')
    Assert-That ((Get-SessionValue 'Property' 'ADD_EXPLORER_MENU') -eq $(if ($checked) { '1' } else { '' })) 'Returning to the page must preserve the chosen state.'
}

# Exercise the same navigation with maintenance/upgrade feature states.
Set-SessionValue 'Property' 'Installed' '1'
Set-SessionValue 'Property' 'WixUI_InstallMode' 'Change'
Set-SessionValue 'FeatureRequestState' 'ExplorerIntegration' 2
Assert-That ((Invoke-Button 'CustomizeDlg' 'Next') -eq 'ExplorerOptionsDlg') 'Change mode must offer Explorer integration.'
Assert-That (-not (Get-SessionValue 'Property' 'ADD_EXPLORER_MENU')) 'An absent migrated feature must initialize an unchecked checkbox.'
Assert-That ((Invoke-Button 'VerifyReadyDlg' 'Back') -eq 'ExplorerOptionsDlg') 'Change mode must return to the options page.'
foreach ($mode in @('Repair', 'Remove')) {
    Set-SessionValue 'Property' 'WixUI_InstallMode' $mode
    Assert-That ((Invoke-Button 'VerifyReadyDlg' 'Back') -eq 'MaintenanceTypeDlg') "$mode navigation must remain available."
}

$checksum = (Get-Content -LiteralPath "$packagePath.sha256" -Raw).Trim() -split '\s+', 2
Assert-That ($checksum[0] -eq (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash) 'MSI checksum mismatch.'
Write-Output 'Passed MSI checks: checkbox defaults, on/off/on planning, preserved choices, Change/Repair/Remove navigation, optional ownership, quoted per-user menu command, uninstall cleanup and checksum. No installation performed.'
