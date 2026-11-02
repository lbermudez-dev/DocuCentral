# Genera la carpeta lista para copiar al servidor IIS (API + frontend en un solo sitio).
# Requisitos en la máquina de compilación: .NET 8 SDK y Node.js 20+.
# Uso:  powershell -ExecutionPolicy Bypass -File deploy\publicar-iis.ps1 -Destino C:\Publicar\DocuCentral
param([string]$Destino = "$PSScriptRoot\..\publicado")

$ErrorActionPreference = 'Stop'
$raiz = Resolve-Path "$PSScriptRoot\.."

Write-Host "1/2 Compilando frontend (queda en backend\DocuCentral.Api\wwwroot)..." -ForegroundColor Cyan
Push-Location "$raiz\frontend"
npm ci
npm run build:iis
Pop-Location

Write-Host "2/2 Publicando API..." -ForegroundColor Cyan
dotnet publish "$raiz\backend\DocuCentral.Api\DocuCentral.Api.csproj" -c Release -o $Destino

Write-Host "Listo: $Destino" -ForegroundColor Green
Write-Host "Copie la carpeta al servidor, cree appsettings.Production.json (ver appsettings.Production.ejemplo.json) y configure el sitio en IIS (ver README)."
