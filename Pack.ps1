param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
& dotnet pack (Join-Path $PSScriptRoot 'Ak.AutoTest.slnx') -c Release -o $outputPath --nologo
if ($LASTEXITCODE -ne 0) { throw "Не удалось собрать пакеты: код $LASTEXITCODE." }
Write-Host "Пакеты: $outputPath"
