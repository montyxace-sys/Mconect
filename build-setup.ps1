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
        Write-Host "Publicando Mconect..." -ForegroundColor Cyan
        if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }

        # IncludeNativeLibrariesForSelfExtract mete las librerías nativas de WPF dentro
        # de Mconect.exe: el ejecutable funciona aunque se copie solo.
        dotnet publish App\Mconect.App.csproj -c $Configuration -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $stageDir
        if ($LASTEXITCODE -ne 0) { throw "No se pudo publicar Mconect." }

        New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
        Get-ChildItem $publishDir -File |
            Where-Object { $_.Name -ne "MconectSetup.exe" } |
            Remove-Item -Force
        Get-ChildItem $stageDir -File |
            Where-Object { $_.Extension -ne ".pdb" } |
            ForEach-Object { Copy-Item $_.FullName (Join-Path $publishDir $_.Name) -Force }
    }

    $appExe = Join-Path $publishDir "Mconect.exe"
    if (-not (Test-Path $appExe)) {
        throw "No se encontró publish\Mconect.exe. Compila Mconect antes de generar el instalador."
    }

    Write-Host "Comprimiendo la aplicación..." -ForegroundColor Cyan
    $payloadSource = Join-Path $stageDir "payload"
    if (Test-Path $payloadSource) { Remove-Item $payloadSource -Recurse -Force }
    New-Item -ItemType Directory -Path $payloadSource | Out-Null
    Get-ChildItem $publishDir -File |
        Where-Object { $_.Extension -ne ".pdb" -and $_.Name -ne "MconectSetup.exe" -and $_.Name -ne "MconectSetupPayload.zip" } |
        ForEach-Object { Copy-Item $_.FullName (Join-Path $payloadSource $_.Name) }

    $payloadZip = Join-Path $publishDir "MconectSetupPayload.zip"
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
    if ($payloadNames -notcontains "Mconect.exe") {
        throw "El payload no contiene Mconect.exe. Revisa publish\."
    }
    Write-Host ("Payload: {0}" -f ($payloadNames -join ", ")) -ForegroundColor DarkGray

    Write-Host "Compilando el desinstalador..." -ForegroundColor Cyan
    dotnet build "Installer\Mconect.Uninstall\Mconect.Uninstall.csproj" -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "No se pudo compilar el desinstalador." }

    Write-Host "Compilando el instalador..." -ForegroundColor Cyan
    dotnet build "Installer\Mconect.Setup\Mconect.Setup.csproj" -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "No se pudo compilar el instalador." }

    $setupExe = Join-Path $root "Installer\Mconect.Setup\bin\$Configuration\net48\Mconect.Setup.exe"
    $target = Join-Path $publishDir "MconectSetup.exe"
    Copy-Item $setupExe $target -Force

    # ProductVersion llega como 0.1.0+<commit>; para el nombre del MSI sobra el sufijo.
    $version = (Get-Item $appExe).VersionInfo.ProductVersion.Split('+')[0]
    $msi = Join-Path $publishDir ("Mconect-{0}-x64.msi" -f $version)
    Write-Host "Generando el instalador MSI..." -ForegroundColor Cyan
    wix build installer.wxs -arch x64 -o $msi
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo generar el MSI. Instala WiX con: dotnet tool install --global wix"
    }
    Get-ChildItem $publishDir -Filter *.wixpdb | Remove-Item -Force

    Remove-Item $payloadZip -Force

    $appMb = [math]::Round((Get-Item $appExe).Length / 1MB, 1)
    $setupMb = [math]::Round((Get-Item $target).Length / 1MB, 1)
    $msiMb = [math]::Round((Get-Item $msi).Length / 1MB, 1)
    Write-Host ""
    Write-Host ("Mconect.exe            {0} MB" -f $appMb) -ForegroundColor Green
    Write-Host ("MconectSetup.exe       {0} MB" -f $setupMb) -ForegroundColor Green
    Write-Host ("Mconect-{0}-x64.msi  {1} MB" -f $version, $msiMb) -ForegroundColor Green
    Write-Host "Publicados en publish\" -ForegroundColor Green
}
finally {
    Pop-Location
}
