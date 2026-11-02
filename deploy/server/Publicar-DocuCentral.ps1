[CmdletBinding()]
param(
    [string]$RaizFuente = (Join-Path $PSScriptRoot '..\..'),
    [Parameter(Mandatory)]
    [string]$Destino
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Ejecutar-Comando([scriptblock]$Comando, [string]$Descripcion) {
    & $Comando
    if ($LASTEXITCODE -ne 0) {
        throw "$Descripcion finalizó con código $LASTEXITCODE."
    }
}

$RaizFuente = (Resolve-Path -LiteralPath $RaizFuente).Path
$frontend = Join-Path $RaizFuente 'frontend'
$proyecto = Join-Path $RaizFuente 'backend\DocuCentral.Api\DocuCentral.Api.csproj'

if (-not (Test-Path -LiteralPath (Join-Path $frontend 'package-lock.json'))) {
    throw "No se encontró frontend\package-lock.json en $RaizFuente."
}
if (-not (Test-Path -LiteralPath $proyecto)) {
    throw "No se encontró el proyecto de la API: $proyecto."
}
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    throw 'npm no está disponible. Instale Node.js 20 o superior.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet no está disponible. Instale .NET SDK 8 o superior.'
}

Write-Host '1/2 Restaurando y compilando el frontend...' -ForegroundColor Cyan
Push-Location $frontend
try {
    Ejecutar-Comando { npm ci } 'npm ci'
    Ejecutar-Comando { npm run build:iis } 'npm run build:iis'
}
finally {
    Pop-Location
}

Write-Host '2/2 Publicando la API...' -ForegroundColor Cyan
New-Item -ItemType Directory -Path $Destino -Force | Out-Null
Ejecutar-Comando { dotnet publish $proyecto -c Release -o $Destino } 'dotnet publish'

$dll = Join-Path $Destino 'DocuCentral.Api.dll'
$index = Join-Path $Destino 'wwwroot\index.html'
$webConfig = Join-Path $Destino 'web.config'
foreach ($archivo in @($dll, $index, $webConfig)) {
    if (-not (Test-Path -LiteralPath $archivo)) {
        throw "La publicación está incompleta. Falta: $archivo"
    }
}

Write-Host "Publicación validada: $Destino" -ForegroundColor Green
