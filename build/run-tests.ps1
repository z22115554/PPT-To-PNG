<#
.SYNOPSIS
    執行全部單元測試。
#>
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
dotnet test (Join-Path $repoRoot 'PptPngExporter.sln') -c Release --nologo
