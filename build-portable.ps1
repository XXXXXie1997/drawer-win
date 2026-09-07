$ErrorActionPreference = 'Stop'
$drawerProject = Join-Path $PSScriptRoot 'src/Drawer.Windows/Drawer.Windows.csproj'
$drawerProjectXml = [xml](Get-Content -LiteralPath $drawerProject -Raw)
$drawerVersion = $drawerProjectXml.Project.PropertyGroup.Version
$drawerOutput = Join-Path $PSScriptRoot ('artifacts/portable/drawer-' + $drawerVersion + '-win-x64')
dotnet publish $drawerProject -p:PublishProfile=Portable-win-x64 -p:DebugType=embedded --output $drawerOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
Write-Output (Join-Path $drawerOutput 'drawer.exe')
