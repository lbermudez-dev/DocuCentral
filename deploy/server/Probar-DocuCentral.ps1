[CmdletBinding()]
param(
    [Parameter(Mandatory)][uri]$Url,
    [Parameter(Mandatory)][string]$ServidorSql,
    [int]$PuertoSql = 1433,
    [string]$NombreSitio = 'DocuCentral',
    [string]$RutaRepositorio,
    [string]$RutaUnc
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$fallos = [Collections.Generic.List[string]]::new()

function Comprobar([string]$Nombre, [scriptblock]$Prueba) {
    try {
        & $Prueba
        Write-Host "[OK] $Nombre" -ForegroundColor Green
    } catch {
        $fallos.Add("${Nombre}: $($_.Exception.Message)")
        Write-Host "[ERROR] $Nombre" -ForegroundColor Red
    }
}

Comprobar 'DNS del sitio' {
    if (-not (Resolve-DnsName $Url.DnsSafeHost -ErrorAction Stop)) { throw 'Sin respuesta DNS.' }
}
Comprobar 'Puerto SQL' {
    if (-not (Test-NetConnection $ServidorSql -Port $PuertoSql -InformationLevel Quiet)) { throw 'Puerto cerrado.' }
}
if ($RutaUnc) {
    Comprobar 'Puerto SMB' {
        $hostUnc = ([uri]$RutaUnc).Host
        if (-not (Test-NetConnection $hostUnc -Port 445 -InformationLevel Quiet)) { throw 'Puerto SMB cerrado.' }
    }
    Comprobar 'Lectura de ruta UNC' {
        if (-not (Test-Path -LiteralPath $RutaUnc)) { throw 'La ruta UNC no es accesible.' }
    }
}
if ($RutaRepositorio) {
    Comprobar 'Lectura del repositorio' {
        if (-not (Test-Path -LiteralPath $RutaRepositorio)) { throw 'El repositorio no es accesible.' }
    }
}
Comprobar 'HTTPS y certificado' {
    if ($Url.Scheme -ne 'https') { throw 'La URL no usa HTTPS.' }
    $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 15
    if ($r.StatusCode -ne 200) { throw "HTTP $($r.StatusCode)." }
}
Comprobar 'API pública' {
    $endpoint = [uri]::new($Url, '/api/auth/config')
    $null = Invoke-RestMethod -Uri $endpoint -TimeoutSec 15
}

if (Get-Module -ListAvailable WebAdministration) {
    Import-Module WebAdministration
    Comprobar 'Estado del sitio IIS' {
        if ((Get-WebsiteState -Name $NombreSitio).Value -ne 'Started') { throw 'Sitio detenido.' }
    }
    Comprobar 'Estado del App Pool' {
        if ((Get-WebAppPoolState -Name $NombreSitio).Value -ne 'Started') { throw 'App Pool detenido.' }
    }
    Comprobar 'WebDAV removido' {
        $path = (Get-Website -Name $NombreSitio).PhysicalPath
        $webConfig = Get-Content -Raw -LiteralPath (Join-Path $path 'web.config')
        if ($webConfig -notmatch 'remove name="WebDAVModule"') { throw 'Falta remover WebDAVModule.' }
        if ($webConfig -notmatch 'remove name="WebDAV"') { throw 'Falta remover el handler WebDAV.' }
    }
}

if ($fallos.Count -gt 0) {
    Write-Error ("Pruebas fallidas:`n- " + ($fallos -join "`n- "))
    exit 1
}
Write-Host 'Todas las comprobaciones finalizaron correctamente.' -ForegroundColor Green
