[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ServidorSql,

    [string]$BaseDatos = 'DocuCentral',

    [Parameter(Mandatory)]
    [string]$CuentaAplicacion,

    [switch]$ReducirPermisos
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd no está instalado o no está en PATH.'
}
if ($BaseDatos -notmatch '^[A-Za-z0-9_]+$') {
    throw 'BaseDatos solo puede contener letras, números y guion bajo.'
}
if ($CuentaAplicacion -notmatch '^[^\\]+\\[^\\]+$') {
    throw 'CuentaAplicacion debe usar el formato DOMINIO\cuenta.'
}

$dbIdentificador = $BaseDatos.Replace(']', ']]')
$cuentaIdentificador = $CuentaAplicacion.Replace(']', ']]')
$cuentaLiteral = $CuentaAplicacion.Replace("'", "''")

function Ejecutar-Sql([string]$Consulta) {
    & sqlcmd -b -S $ServidorSql -E -Q $Consulta
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd finalizó con código $LASTEXITCODE."
    }
}

if ($ReducirPermisos) {
    Write-Host "Reduciendo permisos de $CuentaAplicacion en $BaseDatos..." -ForegroundColor Cyan
    $sql = @"
USE [$dbIdentificador];
IF IS_ROLEMEMBER(N'db_owner', N'$cuentaLiteral') = 1
    ALTER ROLE [db_owner] DROP MEMBER [$cuentaIdentificador];
IF IS_ROLEMEMBER(N'db_datareader', N'$cuentaLiteral') <> 1
    ALTER ROLE [db_datareader] ADD MEMBER [$cuentaIdentificador];
IF IS_ROLEMEMBER(N'db_datawriter', N'$cuentaLiteral') <> 1
    ALTER ROLE [db_datawriter] ADD MEMBER [$cuentaIdentificador];
"@
    Ejecutar-Sql $sql
    Write-Host 'Permisos reducidos a lectura y escritura de datos.' -ForegroundColor Green
    return
}

Write-Host "Creando la base $BaseDatos si no existe..." -ForegroundColor Cyan
$sqlServidor = @"
IF DB_ID(N'$dbIdentificador') IS NULL
    CREATE DATABASE [$dbIdentificador];
IF SUSER_ID(N'$cuentaLiteral') IS NULL
    CREATE LOGIN [$cuentaIdentificador] FROM WINDOWS;
"@
Ejecutar-Sql $sqlServidor

# Se ejecuta en un segundo lote: SQL Server resuelve USE antes de ejecutar CREATE DATABASE.
$sqlBase = @"
USE [$dbIdentificador];
IF USER_ID(N'$cuentaLiteral') IS NULL
    CREATE USER [$cuentaIdentificador] FOR LOGIN [$cuentaIdentificador];
IF IS_ROLEMEMBER(N'db_owner', N'$cuentaLiteral') <> 1
    ALTER ROLE [db_owner] ADD MEMBER [$cuentaIdentificador];
"@
Ejecutar-Sql $sqlBase

Write-Host 'Base y permisos iniciales preparados.' -ForegroundColor Green
Write-Warning 'Después del primer arranque ejecute nuevamente con -ReducirPermisos.'
