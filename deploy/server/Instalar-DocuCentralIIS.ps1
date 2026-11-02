#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OrigenPublicado,
    [Parameter(Mandatory)][string]$RutaSitio,
    [Parameter(Mandatory)][string]$NombreSitio,
    [Parameter(Mandatory)][string]$CuentaAplicacion,
    [Parameter(Mandatory)][string]$HostHeader,
    [Parameter(Mandatory)][string]$CertificadoThumbprint,
    [string]$RutaDatos = 'D:\DocuCentral',
    [PSCredential]$CredencialAplicacion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Esperar-AppPoolDetenido([string]$Nombre) {
    for ($i = 1; $i -le 30; $i++) {
        if ((Get-WebAppPoolState -Name $Nombre).Value -eq 'Stopped') { return }
        Start-Sleep -Milliseconds 500
    }
    throw "El App Pool $Nombre no se detuvo dentro del tiempo esperado."
}

$OrigenPublicado = (Resolve-Path -LiteralPath $OrigenPublicado).Path
if (-not [IO.Path]::IsPathRooted($RutaSitio)) { throw 'RutaSitio debe ser absoluta.' }
if (-not [IO.Path]::IsPathRooted($RutaDatos)) { throw 'RutaDatos debe ser absoluta o UNC.' }
if (-not (Test-Path -LiteralPath (Join-Path $OrigenPublicado 'DocuCentral.Api.dll'))) { throw 'Falta DocuCentral.Api.dll.' }
if (-not (Test-Path -LiteralPath (Join-Path $OrigenPublicado 'wwwroot\index.html'))) { throw 'Falta wwwroot\index.html.' }
if (-not (Test-Path -LiteralPath (Join-Path $OrigenPublicado 'appsettings.Production.json'))) { throw 'Falta appsettings.Production.json.' }
if (-not (Test-Path -LiteralPath "$env:windir\System32\inetsrv\appcmd.exe")) { throw 'IIS no está instalado.' }
if (-not (Test-Path -LiteralPath 'C:\Program Files\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll')) { throw 'Falta ASP.NET Core Hosting Bundle.' }

$configText = Get-Content -Raw -LiteralPath (Join-Path $OrigenPublicado 'appsettings.Production.json')
if ($configText -match 'REEMPLAZAR_MEDIANTE_SECRETO_SEGURO') { throw 'appsettings.Production.json aún contiene marcadores de secretos.' }
$null = $configText | ConvertFrom-Json

$thumb = $CertificadoThumbprint.Replace(' ', '').ToUpperInvariant()
$certificado = Get-Item -LiteralPath "Cert:\LocalMachine\My\$thumb" -ErrorAction SilentlyContinue
if (-not $certificado) { throw "No existe el certificado $thumb en LocalMachine\My." }
if ($certificado.NotAfter -le (Get-Date)) { throw 'El certificado indicado está vencido.' }

Import-Module WebAdministration

if (-not $CredencialAplicacion -and -not $CuentaAplicacion.EndsWith('$')) {
    $CredencialAplicacion = Get-Credential -UserName $CuentaAplicacion -Message 'Credenciales para el App Pool DocuCentral'
}
if ($CredencialAplicacion -and $CredencialAplicacion.UserName -ne $CuentaAplicacion) {
    throw 'La credencial no corresponde a CuentaAplicacion.'
}

Write-Host 'Creando carpetas de datos y permisos...' -ForegroundColor Cyan
$carpetas = @('Repositorio','Entrada','Procesados','Errores') | ForEach-Object { Join-Path $RutaDatos $_ }
foreach ($carpeta in $carpetas) {
    New-Item -ItemType Directory -Path $carpeta -Force | Out-Null
}
& icacls $RutaDatos /grant "${CuentaAplicacion}:(OI)(CI)M" /T /C | Out-Null
if ($LASTEXITCODE -ne 0) { throw "No se pudieron asignar permisos sobre $RutaDatos." }

Write-Host 'Configurando App Pool...' -ForegroundColor Cyan
if (-not (Test-Path "IIS:\AppPools\$NombreSitio")) { New-WebAppPool -Name $NombreSitio | Out-Null }
Set-ItemProperty "IIS:\AppPools\$NombreSitio" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$NombreSitio" -Name managedPipelineMode -Value Integrated
Set-ItemProperty "IIS:\AppPools\$NombreSitio" -Name startMode -Value AlwaysRunning
Set-ItemProperty "IIS:\AppPools\$NombreSitio" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$NombreSitio" -Name processModel.identityType -Value SpecificUser
Set-ItemProperty "IIS:\AppPools\$NombreSitio" -Name processModel.userName -Value $CuentaAplicacion
if ($CredencialAplicacion) {
    Set-ItemProperty "IIS:\AppPools\$NombreSitio" -Name processModel.password -Value $CredencialAplicacion.GetNetworkCredential().Password
} else {
    Set-ItemProperty "IIS:\AppPools\$NombreSitio" -Name processModel.password -Value ''
}

if (Test-Path "IIS:\Sites\$NombreSitio") {
    Stop-Website -Name $NombreSitio -ErrorAction SilentlyContinue
    Stop-WebAppPool -Name $NombreSitio -ErrorAction SilentlyContinue
    Esperar-AppPoolDetenido $NombreSitio
}

if (Test-Path -LiteralPath $RutaSitio) {
    $contenido = Get-ChildItem -LiteralPath $RutaSitio -Force -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($contenido) {
        $respaldo = "${RutaSitio}.bak.$(Get-Date -Format 'yyyyMMddHHmmss')"
        Move-Item -LiteralPath $RutaSitio -Destination $respaldo
        Write-Host "Versión anterior respaldada en $respaldo" -ForegroundColor Yellow
    }
}
New-Item -ItemType Directory -Path $RutaSitio -Force | Out-Null
Copy-Item -Path (Join-Path $OrigenPublicado '*') -Destination $RutaSitio -Recurse -Force
& icacls $RutaSitio /grant "${CuentaAplicacion}:(OI)(CI)RX" /T /C | Out-Null
if ($LASTEXITCODE -ne 0) { throw "No se pudieron asignar permisos sobre $RutaSitio." }

Write-Host 'Configurando sitio y HTTPS...' -ForegroundColor Cyan
if (-not (Test-Path "IIS:\Sites\$NombreSitio")) {
    New-Website -Name $NombreSitio -PhysicalPath $RutaSitio -ApplicationPool $NombreSitio -Port 8080 -IPAddress '*' | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$NombreSitio" -Name physicalPath -Value $RutaSitio
    Set-ItemProperty "IIS:\Sites\$NombreSitio" -Name applicationPool -Value $NombreSitio
}
Get-WebBinding -Name $NombreSitio | Remove-WebBinding
New-WebBinding -Name $NombreSitio -Protocol https -Port 443 -IPAddress '*' -HostHeader $HostHeader -SslFlags 1
(Get-WebBinding -Name $NombreSitio -Protocol https -Port 443).AddSslCertificate($thumb, 'my')
Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "system.applicationHost/sites/site[@name='$NombreSitio']/application[@path='/']" -Name preloadEnabled -Value $true

Start-WebAppPool -Name $NombreSitio
Start-Website -Name $NombreSitio
$url = "https://$HostHeader/"
$respuesta = $null
for ($i = 1; $i -le 30; $i++) {
    try {
        $respuesta = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
        if ($respuesta.StatusCode -eq 200) { break }
    } catch { Start-Sleep -Seconds 2 }
}
if (-not $respuesta -or $respuesta.StatusCode -ne 200) { throw "El sitio no respondió HTTP 200 en $url." }

Write-Host "DocuCentral instalado: $url" -ForegroundColor Green
