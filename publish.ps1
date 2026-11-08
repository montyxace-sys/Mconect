param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    $publishDir = Join-Path $root "publish"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    dotnet publish "App\Mconect.App.csproj" -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "No se pudo publicar Mconect." }

    $exe = Join-Path $publishDir "Mconect.exe"
    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host ""
    Write-Host ("Mconect.exe  {0} MB" -f $sizeMb) -ForegroundColor Green
    Write-Host "Publicado en publish\" -ForegroundColor Green
}
finally {
    Pop-Location
}
