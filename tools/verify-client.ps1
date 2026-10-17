# Requires PowerShell 7. Run in a fresh process: pwsh -File tools/verify-client.ps1
# These checks compile and exercise the client core with small Unity API stubs,
# including deterministic API responses for lifecycle and retry regressions.
# They do not replace importing, playing or building the project in Unity 6.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$taskDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('ilha-client-checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskDirectory | Out-Null

try {
    $sources = @(
        (Join-Path $PSScriptRoot 'client/UnityStubs.cs'),
        (Join-Path $PSScriptRoot 'client/CoreChecks.cs')
    ) + @(Get-ChildItem (Join-Path $projectRoot 'Assets/Aprender/Runtime/Core/*.cs') | ForEach-Object FullName)
    Add-Type -Path $sources -IgnoreWarnings -WarningAction SilentlyContinue

    $syntaxErrors = @()
    $files = @(Get-ChildItem (Join-Path $projectRoot 'Assets/Aprender') -Recurse -Filter '*.cs')
    foreach ($file in $files) {
        $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([System.IO.File]::ReadAllText($file.FullName))
        foreach ($diagnostic in $tree.GetDiagnostics()) {
            if ($diagnostic.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error) {
                $syntaxErrors += $file.FullName + ': ' + $diagnostic.ToString()
            }
        }
    }
    if ($syntaxErrors.Count -gt 0) { throw ($syntaxErrors -join [Environment]::NewLine) }
    Write-Output ('C# syntax: ' + $files.Count + ' Aprender files passed (syntax only outside Core).')

    $fixture = Join-Path $projectRoot 'Assets/Aprender/Resources/quiz-catalog.json'
    Write-Output ([CoreChecks]::Run($fixture, (Join-Path $taskDirectory 'progress')))
    Write-Output ('Test snapshots: ' + $taskDirectory)
    Write-Output 'Unity rendering, animation, input, network transport and builds require verification in Unity 6.'
}
catch {
    Write-Error $_
    exit 1
}
