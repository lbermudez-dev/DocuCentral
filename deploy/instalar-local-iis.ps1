#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$ServidorSql = 'DESKTOP-UVOAPDC',
    [string]$BaseDatos = 'DocuCentral',
    [string]$NombreSitio = 'DocuCentral',
    [int]$Puerto = 8080,
    [string]$RutaSitio = 'C:\inetpub\DocuCentral',
    [string]$RutaDatos = 'C:\DocuCentral',
    [string]$OrigenPublicado = (Join-Path $PSScriptRoot '..\artifacts\site'),
    [string]$RutaLog = (Join-Path $PSScriptRoot '..\artifacts\instalacion-iis.log')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Escribir-Paso([string]$Mensaje) {
    $linea = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $Mensaje"
    Write-Host $linea -ForegroundColor Cyan
    Add-Content -LiteralPath $RutaLog -Value $linea
}

function Ejecutar-Sql([string]$Consulta) {
    & sqlcmd -b -S $ServidorSql -E -Q $Consulta
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd finalizó con código $LASTEXITCODE." }
}

$OrigenPublicado = (Resolve-Path -LiteralPath $OrigenPublicado).Path
$directorioLog = Split-Path -Parent $RutaLog
New-Item -ItemType Directory -Path $directorioLog -Force | Out-Null
Set-Content -LiteralPath $RutaLog -Value "Inicio de instalación de $NombreSitio"

if (-not (Test-Path -LiteralPath (Join-Path $OrigenPublicado 'DocuCentral.Api.dll'))) {
    throw "La publicación no contiene DocuCentral.Api.dll: $OrigenPublicado"
}
if (-not (Test-Path -LiteralPath (Join-Path $OrigenPublicado 'appsettings.Production.json'))) {
    throw "Falta appsettings.Production.json en $OrigenPublicado"
}
if (-not (Test-Path -LiteralPath "$env:windir\System32\inetsrv\appcmd.exe")) {
    throw 'IIS no está instalado.'
}
if (-not (Test-Path -LiteralPath 'C:\Program Files\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll')) {
    throw 'Falta el ASP.NET Core Hosting Bundle para IIS.'
}

Escribir-Paso 'Validando que el enlace HTTP esté disponible.'
Import-Module WebAdministration
$conflicto = Get-Website | Where-Object {
    $_.Name -ne $NombreSitio -and
    $_.Bindings.Collection.bindingInformation -contains "*:${Puerto}:"
}
if ($conflicto) {
    throw "El puerto $Puerto ya está asignado al sitio '$($conflicto.Name)'."
}

Escribir-Paso 'Preparando la identidad y la base de datos SQL Server.'
$identidadPool = "IIS APPPOOL\$NombreSitio"
if (-not (Test-Path "IIS:\AppPools\$NombreSitio")) {
    New-WebAppPool -Name $NombreSitio | Out-Null
}
$sqlServidor = @"
IF DB_ID(N'$BaseDatos') IS NULL CREATE DATABASE [$BaseDatos];
IF SUSER_ID(N'$identidadPool') IS NULL CREATE LOGIN [$identidadPool] FROM WINDOWS;
"@
Ejecutar-Sql $sqlServidor
$sqlBase = @"
USE [$BaseDatos];
IF USER_ID(N'$identidadPool') IS NULL CREATE USER [$identidadPool] FOR LOGIN [$identidadPool];
IF IS_ROLEMEMBER(N'db_owner', N'$identidadPool') <> 1 ALTER ROLE [db_owner] ADD MEMBER [$identidadPool];
"@
Ejecutar-Sql $sqlBase

Escribir-Paso 'Creando las carpetas de documentos y permisos del App Pool.'
$carpetasDatos = @(
    $RutaDatos,
    (Join-Path $RutaDatos 'Repositorio'),
    (Join-Path $RutaDatos 'Entrada'),
    (Join-Path $RutaDatos 'Procesados'),
    (Join-Path $RutaDatos 'Errores')
)
foreach ($carpeta in $carpetasDatos) {
    New-Item -ItemType Directory -Path $carpeta -Force | Out-Null
}

Escribir-Paso 'Configurando el App Pool y el sitio IIS.'
Set-ItemProperty "IIS:\AppPools\$NombreSitio" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$NombreSitio" -Name managedPipelineMode -Value 'Integrated'
Set-ItemProperty "IIS:\AppPools\$NombreSitio" -Name startMode -Value 'AlwaysRunning'
Set-ItemProperty "IIS:\AppPools\$NombreSitio" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)

foreach ($carpeta in $carpetasDatos) {
    & icacls $carpeta /grant "${identidadPool}:(OI)(CI)M" /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No se pudieron asignar permisos sobre $carpeta." }
}

if (Test-Path "IIS:\Sites\$NombreSitio") {
    Stop-Website -Name $NombreSitio -ErrorAction SilentlyContinue
    Stop-WebAppPool -Name $NombreSitio -ErrorAction SilentlyContinue
    for ($intento = 1; $intento -le 20; $intento++) {
        if ((Get-WebAppPoolState -Name $NombreSitio).Value -eq 'Stopped') { break }
        Start-Sleep -Milliseconds 500
    }
}

if (Test-Path -LiteralPath $RutaSitio) {
    $tieneContenido = Get-ChildItem -LiteralPath $RutaSitio -Force -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($tieneContenido) {
        $respaldo = "${RutaSitio}.bak.$(Get-Date -Format 'yyyyMMddHHmmss')"
        Move-Item -LiteralPath $RutaSitio -Destination $respaldo
        Escribir-Paso "La instalación anterior se conservó en $respaldo."
    }
}
New-Item -ItemType Directory -Path $RutaSitio -Force | Out-Null
Copy-Item -Path (Join-Path $OrigenPublicado '*') -Destination $RutaSitio -Recurse -Force
& icacls $RutaSitio /grant "${identidadPool}:(OI)(CI)RX" /T /C | Out-Null
if ($LASTEXITCODE -ne 0) { throw "No se pudieron asignar permisos de lectura sobre $RutaSitio." }

if (Test-Path "IIS:\Sites\$NombreSitio") {
    Set-ItemProperty "IIS:\Sites\$NombreSitio" -Name physicalPath -Value $RutaSitio
    Set-ItemProperty "IIS:\Sites\$NombreSitio" -Name applicationPool -Value $NombreSitio
    Get-WebBinding -Name $NombreSitio -Protocol http | Remove-WebBinding
    New-WebBinding -Name $NombreSitio -Protocol http -Port $Puerto -IPAddress '*'
} else {
    New-Website -Name $NombreSitio -PhysicalPath $RutaSitio -ApplicationPool $NombreSitio -Port $Puerto -IPAddress '*' | Out-Null
}
Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "system.applicationHost/sites/site[@name='$NombreSitio']/application[@path='/']" -Name preloadEnabled -Value $true

Escribir-Paso 'Iniciando el sitio y esperando la creación del esquema inicial.'
Start-WebAppPool -Name $NombreSitio -ErrorAction SilentlyContinue
Start-Website -Name $NombreSitio
$url = "http://localhost:$Puerto/"
$respuesta = $null
for ($intento = 1; $intento -le 30; $intento++) {
    try {
        $respuesta = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
        if ($respuesta.StatusCode -eq 200) { break }
    } catch {
        Start-Sleep -Seconds 2
    }
}
if (-not $respuesta -or $respuesta.StatusCode -ne 200) {
    throw "El sitio no respondió HTTP 200 en $url. Revise el Visor de eventos y $RutaLog."
}

Escribir-Paso 'Verificando el inicio de sesión inicial contra la API.'
$configPath = Join-Path $RutaSitio 'appsettings.Production.json'
$config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
$body = @{ usuario = 'admin'; password = $config.Seed.AdminPassword } | ConvertTo-Json
$login = Invoke-WebRequest -Uri "http://localhost:$Puerto/api/auth/login" -Method Post -ContentType 'application/json' -Body $body -UseBasicParsing -TimeoutSec 15
if ($login.StatusCode -ne 200) { throw 'La API no aceptó el usuario administrador inicial.' }

Escribir-Paso 'Retirando permisos SQL de definición y desactivando la creación automática.'
$config.Database.AutoCreate = $false
$config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $configPath -Encoding UTF8
$sqlFinal = @"
USE [$BaseDatos];
IF IS_ROLEMEMBER(N'db_owner', N'$identidadPool') = 1 ALTER ROLE [db_owner] DROP MEMBER [$identidadPool];
IF IS_ROLEMEMBER(N'db_datareader', N'$identidadPool') <> 1 ALTER ROLE [db_datareader] ADD MEMBER [$identidadPool];
IF IS_ROLEMEMBER(N'db_datawriter', N'$identidadPool') <> 1 ALTER ROLE [db_datawriter] ADD MEMBER [$identidadPool];
"@
Ejecutar-Sql $sqlFinal
Restart-WebAppPool -Name $NombreSitio

Escribir-Paso "Instalación terminada. URL: $url"
Get-Website -Name $NombreSitio | Select-Object Name, State, PhysicalPath, ApplicationPool
