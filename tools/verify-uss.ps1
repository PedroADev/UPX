# Run in a fresh PowerShell process. Uses Unity's installed parser; downloads nothing.
# Example: pwsh -File tools/verify-uss.ps1 -CaminhoEditor 'C:/Program Files/Unity/Hub/Editor/6000.0.83f1/Editor/Unity.exe'
# Validates USS syntax. Importing and rendering the UI still require Unity Editor.
[CmdletBinding()]
param(
    [Alias('UnityEditorPath')]
    [string]$CaminhoEditor,

    [Alias('StyleSheetPath')]
    [string]$ArquivoUss = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets/Aprender/Resources/Aprender.uss')
)

$ErrorActionPreference = 'Stop'

try {
    if ([string]::IsNullOrWhiteSpace($CaminhoEditor)) {
        $projectRoot = Split-Path -Parent $PSScriptRoot
        $versionFile = Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt'
        $versionLine = Get-Content -LiteralPath $versionFile | Where-Object { $_ -match '^m_EditorVersion: ' } | Select-Object -First 1
        if (-not $versionLine) { throw "Unity version not found in $versionFile." }
        $editorVersion = ($versionLine -replace '^m_EditorVersion:\s*', '').Trim()
        $CaminhoEditor = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$editorVersion/Editor/Unity.exe"
    }

    $editorItem = Get-Item -LiteralPath $CaminhoEditor
    $editorDirectory = if ($editorItem.PSIsContainer) { $editorItem.FullName } else { $editorItem.DirectoryName }
    if (Test-Path -LiteralPath (Join-Path $editorDirectory 'Editor/Data/Managed/ExCSS.Unity.dll')) {
        $editorDirectory = Join-Path $editorDirectory 'Editor'
    }
    $parserPath = Join-Path $editorDirectory 'Data/Managed/ExCSS.Unity.dll'
    if (-not (Test-Path -LiteralPath $parserPath -PathType Leaf)) {
        throw "Unity parser not found at $parserPath. Pass the Unity.exe path or its Editor directory with -CaminhoEditor."
    }

    $styleSheetFile = Get-Item -LiteralPath $ArquivoUss
    if ($styleSheetFile.PSIsContainer) { throw "USS path must be a file: $ArquivoUss" }
    $parserAssembly = [Reflection.Assembly]::LoadFrom($parserPath)
    $parser = [Activator]::CreateInstance($parserAssembly.GetType('ExCSS.Parser', $true))
    $styleSheet = $parser.Parse([IO.File]::ReadAllText($styleSheetFile.FullName))

    Write-Output ('Unity USS parser: ' + $parserPath)
    Write-Output ('USS: ' + $styleSheetFile.FullName)
    Write-Output ('Rules: ' + $styleSheet.Rules.Count + '; errors: ' + $styleSheet.Errors.Count)
    if ($styleSheet.Errors.Count -gt 0) {
        foreach ($parseError in $styleSheet.Errors) {
            Write-Output ('Line ' + $parseError.Line + ', column ' + $parseError.Column + ': ' + $parseError.Message)
        }
        throw 'USS syntax validation failed.'
    }
}
catch {
    Write-Error $_
    exit 1
}
