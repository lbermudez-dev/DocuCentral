#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OrigenPublicado,
    [Parameter(Mandatory)][string]$RutaSitio,
    [string]$NombreSitio = 'DocuCentral',
    [Parameter(Mandatory)][uri]$UrlPrueba
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Validar-RutaSitio([string]$Ruta) {
    $full = [IO.Path]::GetFullPath($Ruta).TrimEnd('\')
    $root = [IO.Path]::GetPathRoot($full).TrimEnd('\')
    if ($full -eq $root -or $full.Length -lt ($root.Length + 4)) { throw "RutaSitio no es suficientemente específica: $full" }
    return $full
}

function Esperar-Detenido([string]$Pool) {
    for ($i = 1; $i -le 30; $i++) {
        if ((Get-WebAppPoolState -Name $Pool).Value -eq 'Stopped') { return }
        Start-Sleep -Milliseconds 500
    }
    throw "El App Pool $Pool no se detuvo."
}

$OrigenPublicado = (Resolve-Path -LiteralPath $OrigenPublicado).Path
$RutaSitio = Validar-RutaSitio $RutaSitio
if (-not (Test-Path -LiteralPath (Join-Path $OrigenPublicado 'DocuCentral.Api.dll'))) { throw 'Publicación nueva inválida: falta DocuCentral.Api.dll.' }
if (-not (Test-Path -LiteralPath (Join-Path $OrigenPublicado 'wwwroot\index.html'))) { throw 'Publicación nueva inválida: falta wwwroot\index.html.' }
if (-not (Test-Path -LiteralPath $RutaSitio)) { throw "No existe el sitio actual: $RutaSitio" }

Import-Module WebAdministration
$timestamp = Get-Date -Format 'yyyyMMddHHmmss'
$respaldo = "${RutaSitio}.bak.$timestamp"
$fallida = "${RutaSitio}.failed.$timestamp"

Stop-Website -Name $NombreSitio -ErrorAction SilentlyContinue
Stop-WebAppPool -Name $NombreSitio -ErrorAction SilentlyContinue
Esperar-Detenido $NombreSitio

try {
    Move-Item -LiteralPath $RutaSitio -Destination $respaldo
    New-Item -ItemType Directory -Path $RutaSitio -Force | Out-Null
    Copy-Item -Path (Join-Path $OrigenPublicado '*') -Destination $RutaSitio -Recurse -Force

    $configAnterior = Join-Path $respaldo 'appsettings.Production.json'
    if (-not (Test-Path -LiteralPath $configAnterior)) { throw 'El respaldo no contiene appsettings.Production.json.' }
    Copy-Item -LiteralPath $configAnterior -Destination (Join-Path $RutaSitio 'appsettings.Production.json') -Force

    Start-WebAppPool -Name $NombreSitio
    Start-Website -Name $NombreSitio
    $respuesta = $null
    for ($i = 1; $i -le 30; $i++) {
        try {
            $respuesta = Invoke-WebRequest -Uri $UrlPrueba -UseBasicParsing -TimeoutSec 5
            if ($respuesta.StatusCode -eq 200) { break }
        } catch { Start-Sleep -Seconds 2 }
    }
    if (-not $respuesta -or $respuesta.StatusCode -ne 200) { throw 'La versión nueva no respondió HTTP 200.' }
    Write-Host "Actualización completada. Respaldo: $respaldo" -ForegroundColor Green
}
catch {
    $errorActualizacion = $_
    Stop-Website -Name $NombreSitio -ErrorAction SilentlyContinue
    Stop-WebAppPool -Name $NombreSitio -ErrorAction SilentlyContinue
    Esperar-Detenido $NombreSitio
    if (Test-Path -LiteralPath $RutaSitio) { Move-Item -LiteralPath $RutaSitio -Destination $fallida }
    if (Test-Path -LiteralPath $respaldo) { Move-Item -LiteralPath $respaldo -Destination $RutaSitio }
    Start-WebAppPool -Name $NombreSitio
    Start-Website -Name $NombreSitio
    throw "La actualización falló y se restauró la versión anterior. Error: $($errorActualizacion.Exception.Message)"
}
