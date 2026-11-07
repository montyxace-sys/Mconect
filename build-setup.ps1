param(
    [string]$Configuration = "Release",
    [switch]$SkipAppPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

$publishDir = Join-Path $root "publish"
$stageDir = Join-Path $root "obj\publish-stage"

try {
    if (-not $SkipAppPublish) {
        Write-Host "Publicando Mnet..." -ForegroundColor Cyan
        if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }

        # IncludeNativeLibrariesForSelfExtract mete las librerías nativas de WPF dentro
        # de Mnet.exe: el ejecutable funciona aunque se copie solo.
        dotnet publish Mnet.App/Mnet.App.csproj -c $Configuration -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $stageDir
        if ($LASTEXITCODE -ne 0) { throw "No se pudo publicar Mnet." }

        New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
        Get-ChildItem $publishDir -File |
            Where-Object { $_.Name -ne "MnetSetup.exe" } |
            Remove-Item -Force
        Get-ChildItem $stageDir -File |
            Where-Object { $_.Extension -ne ".pdb" } |
            ForEach-Object { Copy-Item $_.FullName (Join-Path $publishDir $_.Name) -Force }
    }

    $appExe = Join-Path $publishDir "Mnet.exe"
    if (-not (Test-Path $appExe)) {
        throw "No se encontró publish\Mnet.exe. Compila Mnet antes de generar el instalador."
    }

    Write-Host "Comprimiendo la aplicación..." -ForegroundColor Cyan
    $payloadSource = Join-Path $stageDir "payload"
    if (Test-Path $payloadSource) { Remove-Item $payloadSource -Recurse -Force }
    New-Item -ItemType Directory -Path $payloadSource | Out-Null
    Get-ChildItem $publishDir -File |
        Where-Object { $_.Extension -ne ".pdb" -and $_.Name -ne "MnetSetup.exe" -and $_.Name -ne "MnetSetupPayload.zip" } |
        ForEach-Object { Copy-Item $_.FullName (Join-Path $payloadSource $_.Name) }

    $payloadZip = Join-Path $publishDir "MnetSetupPayload.zip"
    if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $payloadSource,
        $payloadZip,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    $payloadCheck = [System.IO.Compression.ZipFile]::OpenRead($payloadZip)
    try {
        $payloadNames = @($payloadCheck.Entries | ForEach-Object { $_.FullName })
    }
    finally {
        $payloadCheck.Dispose()
    }
    Remove-Item $payloadSource -Recurse -Force
    if ($payloadNames -notcontains "Mnet.exe") {
        throw "El payload no contiene Mnet.exe. Revisa publish\."
    }
    Write-Host ("Payload: {0}" -f ($payloadNames -join ", ")) -ForegroundColor DarkGray

    Write-Host "Compilando el desinstalador..." -ForegroundColor Cyan
    dotnet build "Installer\Mnet.Uninstall\Mnet.Uninstall.csproj" -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "No se pudo compilar el desinstalador." }

    Write-Host "Compilando el instalador..." -ForegroundColor Cyan
    dotnet build "Installer\Mnet.Setup\Mnet.Setup.csproj" -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "No se pudo compilar el instalador." }

    $setupExe = Join-Path $root "Installer\Mnet.Setup\bin\$Configuration\net48\Mnet.Setup.exe"
    $target = Join-Path $publishDir "MnetSetup.exe"
    Copy-Item $setupExe $target -Force

    Remove-Item $payloadZip -Force

    $appMb = [math]::Round((Get-Item $appExe).Length / 1MB, 1)
    $setupMb = [math]::Round((Get-Item $target).Length / 1MB, 1)
    Write-Host ""
    Write-Host ("Mnet.exe       {0} MB" -f $appMb) -ForegroundColor Green
    Write-Host ("MnetSetup.exe  {0} MB" -f $setupMb) -ForegroundColor Green
    Write-Host "Publicados en publish\" -ForegroundColor Green
}
finally {
    Pop-Location
}
