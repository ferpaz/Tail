[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string[]]$Runtime
)

$ErrorActionPreference = "Stop"
$supportedRuntimes = @(
    "win-x64", "win-x86", "win-arm64",
    "linux-x64", "linux-arm", "linux-arm64",
    "linux-musl-x64", "linux-musl-arm", "linux-musl-arm64"
)
if (-not $PSBoundParameters.ContainsKey("Runtime")) {
    $Runtime = $supportedRuntimes
}
foreach ($rid in $Runtime) {
    if ($rid -cnotin $supportedRuntimes) {
        throw "RID no admitido: '$rid'. Opciones: $($supportedRuntimes -join ', ')."
    }
}

$project = Join-Path $PSScriptRoot "src\Catl\Catl.csproj"
$versionOutput = & dotnet msbuild $project -nologo -getProperty:Version
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo leer la version de src\Directory.Build.props."
}
$version = ($versionOutput | Out-String).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version invalida en src\Directory.Build.props: '$version'. Use mayor.menor.parche."
}

$versionFolder = Join-Path (Join-Path $PSScriptRoot "releases") $version
$packagerProject = Join-Path $PSScriptRoot "src\Catl.Packaging\Catl.Packaging.csproj"
& dotnet build $packagerProject --configuration Release --nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar el empaquetador de publicaciones."
}
$packager = Join-Path $PSScriptRoot "src\Catl.Packaging\bin\Release\net10.0\Catl.Packaging.dll"
$staging = Join-Path ([System.IO.Path]::GetTempPath()) "catl-publish-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    foreach ($rid in ($Runtime | Select-Object -Unique)) {
        $executableName = if ($rid.StartsWith("win-")) { "catl.exe" } else { "catl" }
        $output = Join-Path $versionFolder $rid
        if (Test-Path -LiteralPath $output) {
            $unexpected = @(Get-ChildItem -LiteralPath $output -Force |
                Where-Object { $_.Name -cne $executableName -or $_.PSIsContainer })
            if ($unexpected.Count -ne 0) {
                throw "La carpeta '$output' contiene archivos ajenos a la publicacion. No se eliminaran automaticamente."
            }
        }

        $ridStaging = Join-Path $staging $rid
        Write-Host "Publicando catl $version para $rid..."
        & dotnet publish $project --configuration Release --runtime $rid --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
            --output $ridStaging --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "La publicacion de '$rid' fallo (codigo $LASTEXITCODE)."
        }

        $files = @(Get-ChildItem -LiteralPath $ridStaging -Force)
        if ($files.Count -ne 1 -or $files[0].Name -cne $executableName -or $files[0].PSIsContainer) {
            throw "La publicacion de '$rid' no produjo un unico ejecutable '$executableName'."
        }
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        Copy-Item -LiteralPath $files[0].FullName -Destination $output -Force
        Write-Host "Publicado: $(Join-Path $output $executableName)"
        $archiveExtension = if ($rid.StartsWith("win-")) { ".zip" } else { ".tar.gz" }
        $archive = Join-Path $versionFolder "catl-$version-$rid$archiveExtension"
        $stagedArchive = Join-Path $staging "catl-$version-$rid$archiveExtension"
        & dotnet $packager $files[0].FullName $stagedArchive
        if ($LASTEXITCODE -ne 0) {
            throw "No se pudo empaquetar la publicacion de '$rid'."
        }
        Copy-Item -LiteralPath $stagedArchive -Destination $archive -Force
        Write-Host "Paquete: $archive"
    }
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
Write-Host "Publicaciones de la version $version en: $versionFolder"
