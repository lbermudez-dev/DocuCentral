# Guía de instalación de DocuCentral en servidores Windows

Guía operativa para instalar DocuCentral sin Docker en una infraestructura con IIS y SQL Server separados. Cubre instalación manual y automatizada, almacenamiento local o UNC, HTTPS, seguridad, pruebas, actualización, reversión y respaldos.

## 1. Alcance

Esta guía asume:

- un servidor de compilación o estación administrativa;
- un servidor web Windows con IIS;
- un servidor SQL Server independiente;
- opcionalmente, un servidor de archivos para el repositorio documental;
- una cuenta de servicio de dominio para el proceso de IIS;
- un nombre DNS y un certificado TLS válido.

No cubre Docker, Kubernetes, alta disponibilidad, balanceo de múltiples nodos ni migraciones desde otros gestores documentales.

## 2. Arquitectura

| Nodo | Responsabilidad | Software mínimo |
|---|---|---|
| BUILD01 | Compilar y publicar | .NET SDK 8+, Node.js 20+, npm |
| WEB01 | Hospedar frontend/API | Windows Server, IIS, Hosting Bundle 8 |
| SQL01 | Persistencia | SQL Server 2019+ o versión corporativa soportada |
| FILE01 | Opcional: documentos UNC | SMB, NTFS, respaldo y antivirus |

Flujo principal:

```text
Usuarios ──HTTPS──> WEB01 / IIS / DocuCentral.Api
                         │
                         ├──SQL/TCP──> SQL01 / DocuCentral
                         │
                         └──SMB──────> FILE01 / Repositorio (opcional)
```

El frontend React se compila dentro de `wwwroot` y es servido por la misma aplicación ASP.NET Core. No se necesita Node.js en WEB01.

## 3. Puertos y comunicaciones

| Origen | Destino | Puerto | Uso |
|---|---|---:|---|
| Usuarios | WEB01 | TCP 443 | HTTPS |
| WEB01 | SQL01 | TCP 1433 o puerto fijo de instancia | SQL Server |
| WEB01 | FILE01 | TCP 445 | SMB/UNC |
| WEB01 | DC/DNS | TCP/UDP 53 y puertos corporativos LDAP | DNS/AD opcional |

Recomendaciones:

- asigne un puerto TCP fijo a la instancia SQL;
- limite la regla de SQL al origen WEB01;
- no publique SQL Server ni SMB hacia Internet;
- redirija HTTP a HTTPS o cierre TCP 80 una vez validado el certificado.

Pruebas desde WEB01:

```powershell
Resolve-DnsName SQL01.midominio.local
Test-NetConnection SQL01.midominio.local -Port 1433
Test-NetConnection FILE01.midominio.local -Port 445
```

## 4. Cuentas necesarias

| Cuenta | Uso | Privilegios |
|---|---|---|
| Administrador de compilación | Publicar | Lectura del código y ejecución de npm/dotnet |
| Administrador de WEB01 | Instalar IIS y sitio | Administrador local durante instalación |
| DBA | Crear base y login | `sysadmin` o permisos delegados equivalentes durante instalación |
| `DOMINIO\svc_docucentral` | Identidad del App Pool | Log on as batch job, acceso SQL y carpetas |

Use una cuenta de dominio o gMSA cuando SQL Server o los documentos estén en otro equipo. La identidad virtual `IIS APPPOOL\DocuCentral` solo es adecuada cuando todos los recursos protegidos son locales a WEB01.

La cuenta de servicio no debe ser administradora local ni `sysadmin` de SQL Server.

## 5. Requisitos del equipo de compilación

Instale:

- Git;
- Node.js 20 o superior;
- .NET SDK 8 o superior;
- acceso al registro npm y NuGet.

Validación:

```powershell
git --version
node --version
npm --version
dotnet --info
```

Clone el repositorio:

```powershell
Set-Location C:\Fuentes
git clone https://github.com/lbermudez-dev/DocuCentral.git
Set-Location C:\Fuentes\DocuCentral
```

## 6. Preparación de SQL Server

### 6.1 Configurar conectividad

En SQL Server Configuration Manager:

1. Habilite TCP/IP para la instancia.
2. Defina un puerto estático.
3. Reinicie el servicio SQL Server.
4. Cree una regla de firewall limitada a WEB01.
5. Compruebe desde WEB01 con `Test-NetConnection`.

### 6.2 Crear base y login de dominio

Ejecute en SQL01 con SSMS o `sqlcmd`:

```sql
USE [master];
GO
IF DB_ID(N'DocuCentral') IS NULL
    CREATE DATABASE [DocuCentral];
GO
IF SUSER_ID(N'DOMINIO\svc_docucentral') IS NULL
    CREATE LOGIN [DOMINIO\svc_docucentral] FROM WINDOWS;
GO
USE [DocuCentral];
GO
IF USER_ID(N'DOMINIO\svc_docucentral') IS NULL
    CREATE USER [DOMINIO\svc_docucentral]
    FOR LOGIN [DOMINIO\svc_docucentral];
GO
ALTER ROLE [db_owner] ADD MEMBER [DOMINIO\svc_docucentral];
GO
```

`db_owner` es temporal: la primera ejecución usa `EnsureCreated` para generar el esquema. Se retira después de comprobar la creación.

### 6.3 Alternativa con autenticación SQL

Si la organización no permite autenticación integrada, cree un login SQL exclusivo con una contraseña administrada por un almacén de secretos. No use `sa` y no incluya la contraseña en Git.

Ejemplo de cadena:

```text
Server=SQL01.midominio.local,1433;Database=DocuCentral;User Id=docucentral_app;Password=<secreto>;Encrypt=True;TrustServerCertificate=False
```

## 7. Preparación del almacenamiento

DocuCentral requiere cuatro ubicaciones:

| Carpeta | Uso |
|---|---|
| Repositorio | Versiones definitivas de documentos |
| Entrada | Carpeta vigilada |
| Procesados | Copia opcional de originales importados |
| Errores | Rechazados y explicación `.error.txt` |

### 7.1 Escenario A: almacenamiento local en WEB01

```powershell
$root = 'D:\DocuCentral'
'Repositorio','Entrada','Procesados','Errores' | ForEach-Object {
    New-Item -ItemType Directory -Path (Join-Path $root $_) -Force
}
icacls $root /grant "DOMINIO\svc_docucentral:(OI)(CI)M" /T /C
```

### 7.2 Escenario B: almacenamiento UNC en FILE01

En FILE01:

```powershell
$root = 'D:\Compartidos\DocuCentral'
'Repositorio','Entrada','Procesados','Errores' | ForEach-Object {
    New-Item -ItemType Directory -Path (Join-Path $root $_) -Force
}
icacls $root /grant "DOMINIO\svc_docucentral:(OI)(CI)M" /T /C
```

Cree un recurso compartido, por ejemplo `\\FILE01\DocuCentral$`, y otorgue a la cuenta de servicio:

- recurso compartido: `Change` y `Read`;
- NTFS: `Modify`, aplicado a carpetas, subcarpetas y archivos.

Compruebe el acceso ejecutando PowerShell bajo la cuenta de servicio o usando una tarea programada temporal:

```powershell
$test = '\\FILE01\DocuCentral$\Repositorio\.prueba_permiso.txt'
Set-Content -LiteralPath $test -Value 'ok'
Remove-Item -LiteralPath $test
```

No use unidades mapeadas como `Z:` en la configuración del servicio; use rutas UNC.

## 8. Compilación y publicación

### 8.1 Frontend

```powershell
Set-Location C:\Fuentes\DocuCentral\frontend
npm ci
npm run build:iis
```

`build:iis` ejecuta TypeScript y Vite y coloca el resultado en:

```text
backend\DocuCentral.Api\wwwroot
```

### 8.2 Backend

```powershell
Set-Location C:\Fuentes\DocuCentral
dotnet publish .\backend\DocuCentral.Api\DocuCentral.Api.csproj `
  -c Release `
  -o C:\Publicar\DocuCentral
```

Valide:

```powershell
Test-Path C:\Publicar\DocuCentral\DocuCentral.Api.dll
Test-Path C:\Publicar\DocuCentral\wwwroot\index.html
Test-Path C:\Publicar\DocuCentral\web.config
```

Los tres resultados deben ser `True`.

También puede usar:

```powershell
.\deploy\server\Publicar-DocuCentral.ps1 `
  -RaizFuente C:\Fuentes\DocuCentral `
  -Destino C:\Publicar\DocuCentral
```

## 9. Instalar IIS y Hosting Bundle

En WEB01, instale el rol Web Server (IIS) y estas características:

- Web Server;
- Static Content;
- Default Document;
- HTTP Errors;
- Request Filtering;
- IIS Management Console;
- Application Initialization.

Instale el ASP.NET Core 8 Hosting Bundle después de IIS. Si el Hosting Bundle se instaló antes, repárelo después de habilitar IIS. Reinicie el servidor o los servicios WAS/W3SVC.

Validación:

```powershell
Test-Path "$env:windir\System32\inetsrv\appcmd.exe"
Test-Path 'C:\Program Files\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
```

## 10. Configuración de producción

Copie la plantilla:

```powershell
Copy-Item `
  C:\Fuentes\DocuCentral\deploy\server\appsettings.Production.template.json `
  C:\Publicar\DocuCentral\appsettings.Production.json
```

Use una cadena con cifrado SQL:

```json
"Default": "Server=SQL01.midominio.local,1433;Database=DocuCentral;Integrated Security=True;Encrypt=True;TrustServerCertificate=False"
```

Si SQL01 usa un certificado interno no confiable todavía, `TrustServerCertificate=True` puede servir durante una prueba controlada, pero debe instalarse la cadena de confianza y volver a `False` para producción.

Genere la clave JWT fuera del repositorio. Ejemplo en PowerShell 7:

```powershell
$bytes = New-Object byte[] 64
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
[Convert]::ToBase64String($bytes)
```

Reemplace los dos marcadores de la plantilla:

- `Jwt:Key`: clave aleatoria de 64 bytes;
- `Seed:AdminPassword`: contraseña temporal robusta.

Establezca las rutas locales o UNC. Mantenga:

```json
"Database": {
  "Provider": "SqlServer",
  "AutoCreate": true
}
```

solo para el primer arranque.

## 11. Copiar la aplicación a WEB01

Ruta recomendada:

```text
D:\Sitios\DocuCentral
```

```powershell
New-Item -ItemType Directory -Path D:\Sitios\DocuCentral -Force
Copy-Item C:\Publicar\DocuCentral\* D:\Sitios\DocuCentral -Recurse -Force
icacls D:\Sitios\DocuCentral /grant "DOMINIO\svc_docucentral:(OI)(CI)RX" /T /C
```

La cuenta necesita lectura/ejecución sobre los binarios y modificación sobre las carpetas de documentos.

## 12. Configurar App Pool y sitio IIS

Abra PowerShell como administrador:

```powershell
Import-Module WebAdministration

New-WebAppPool -Name 'DocuCentral'
Set-ItemProperty IIS:\AppPools\DocuCentral -Name managedRuntimeVersion -Value ''
Set-ItemProperty IIS:\AppPools\DocuCentral -Name managedPipelineMode -Value Integrated
Set-ItemProperty IIS:\AppPools\DocuCentral -Name startMode -Value AlwaysRunning
Set-ItemProperty IIS:\AppPools\DocuCentral -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty IIS:\AppPools\DocuCentral -Name processModel.identityType -Value SpecificUser
Set-ItemProperty IIS:\AppPools\DocuCentral -Name processModel.userName -Value 'DOMINIO\svc_docucentral'
```

Asigne la contraseña de la cuenta mediante IIS Manager o un mecanismo administrativo protegido; no la escriba en un script almacenado.

Cree el sitio inicialmente con un puerto interno temporal:

```powershell
New-Website `
  -Name 'DocuCentral' `
  -PhysicalPath 'D:\Sitios\DocuCentral' `
  -ApplicationPool 'DocuCentral' `
  -Port 8080 `
  -IPAddress '*'
```

Habilite precarga:

```powershell
Set-WebConfigurationProperty `
  -PSPath 'MACHINE/WEBROOT/APPHOST' `
  -Filter "system.applicationHost/sites/site[@name='DocuCentral']/application[@path='/']" `
  -Name preloadEnabled `
  -Value $true
```

## 13. Deshabilitar WebDAV para DocuCentral

El `web.config` incluido en el proyecto contiene:

```xml
<modules>
  <remove name="WebDAVModule" />
</modules>
<handlers>
  <remove name="WebDAV" />
  <add name="aspNetCore" path="*" verb="*"
       modules="AspNetCoreModuleV2" resourceType="Unspecified" />
</handlers>
```

Esto es necesario para que IIS no intercepte `PUT` y `DELETE` con HTTP 405. No es necesario desinstalar WebDAV globalmente.

## 14. Configurar HTTPS

Antes de continuar:

1. cree el registro DNS de la aplicación;
2. importe el certificado y su clave privada en `Cert:\LocalMachine\My`;
3. confirme que el CN/SAN contiene el nombre DNS;
4. asegure que la cadena de confianza sea válida.

```powershell
$site = 'DocuCentral'
$hostName = 'docucentral.midominio.local'
$thumbprint = 'THUMBPRINT_DEL_CERTIFICADO'

New-WebBinding `
  -Name $site `
  -Protocol https `
  -Port 443 `
  -IPAddress '*' `
  -HostHeader $hostName `
  -SslFlags 1

(Get-WebBinding -Name $site -Protocol https -Port 443).AddSslCertificate($thumbprint, 'my')
```

Después de validar HTTPS, elimine el binding temporal o configure redirección HTTP a HTTPS según la política corporativa.

## 15. Primera ejecución

```powershell
Start-WebAppPool -Name DocuCentral
Start-Website -Name DocuCentral
```

Compruebe:

```powershell
Invoke-WebRequest https://docucentral.midominio.local/
Invoke-RestMethod https://docucentral.midominio.local/api/auth/config
```

En SQL01:

```sql
USE [DocuCentral];
SELECT COUNT(*) AS Tablas FROM sys.tables;
SELECT COUNT(*) AS Usuarios FROM dbo.usuarios;
```

La versión actual crea 13 tablas y un usuario `admin`. Inicie sesión con la contraseña temporal y cámbiela inmediatamente.

## 16. Reducir permisos después del primer arranque

Modifique:

```json
"AutoCreate": false
```

Luego ejecute en SQL01:

```sql
USE [DocuCentral];
ALTER ROLE [db_owner] DROP MEMBER [DOMINIO\svc_docucentral];
ALTER ROLE [db_datareader] ADD MEMBER [DOMINIO\svc_docucentral];
ALTER ROLE [db_datawriter] ADD MEMBER [DOMINIO\svc_docucentral];
```

Recicle la aplicación:

```powershell
Restart-WebAppPool -Name DocuCentral
```

## 17. Validación funcional

Complete estas pruebas:

1. HTTPS responde 200 y el certificado es válido.
2. El administrador inicia sesión y cambia la contraseña temporal.
3. Se crea un área y un tipo documental de prueba.
4. Se carga un PDF pequeño.
5. Se visualiza y descarga el documento.
6. Se modifica la configuración de rutas sin HTTP 405.
7. Se deposita un archivo en Entrada y aparece en Bandeja.
8. La auditoría registra login, carga, consulta y descarga.
9. La aplicación sigue funcionando después de reciclar el App Pool.
10. La cuenta de aplicación no pertenece a `db_owner` ni `sysadmin`.

Prueba no autenticada de verbos: debe devolver 401, no 405.

```powershell
curl.exe -i -X PUT `
  https://docucentral.midominio.local/api/configuracion `
  -H "Content-Type: application/json" `
  --data "{}"
```

## 18. Instalación automatizada

Orden recomendado:

```powershell
# BUILD01
.\deploy\server\Publicar-DocuCentral.ps1 `
  -RaizFuente C:\Fuentes\DocuCentral `
  -Destino C:\Publicar\DocuCentral

# SQL01 o estación con sqlcmd y permisos DBA
.\deploy\server\Preparar-SqlDocuCentral.ps1 `
  -ServidorSql SQL01.midominio.local,1433 `
  -BaseDatos DocuCentral `
  -CuentaAplicacion 'DOMINIO\svc_docucentral'

# WEB01, PowerShell como administrador
.\deploy\server\Instalar-DocuCentralIIS.ps1 `
  -OrigenPublicado C:\Publicar\DocuCentral `
  -RutaSitio D:\Sitios\DocuCentral `
  -NombreSitio DocuCentral `
  -CuentaAplicacion 'DOMINIO\svc_docucentral' `
  -HostHeader docucentral.midominio.local `
  -CertificadoThumbprint THUMBPRINT `
  -RutaDatos D:\DocuCentral

.\deploy\server\Probar-DocuCentral.ps1 `
  -Url https://docucentral.midominio.local `
  -ServidorSql SQL01.midominio.local `
  -PuertoSql 1433 `
  -NombreSitio DocuCentral
```

Los scripts no generan ni almacenan contraseñas de dominio. Configure la identidad específica del App Pool mediante una herramienta segura aprobada por la organización.

## 19. Actualización

Antes de actualizar:

1. respalde SQL Server;
2. respalde o tome snapshot coherente del repositorio;
3. compile la nueva versión;
4. revise cambios de esquema;
5. programe ventana de mantenimiento.

Actualización automatizada:

```powershell
.\deploy\server\Actualizar-DocuCentral.ps1 `
  -OrigenPublicado C:\Publicar\DocuCentralNueva `
  -RutaSitio D:\Sitios\DocuCentral `
  -NombreSitio DocuCentral `
  -UrlPrueba https://docucentral.midominio.local/
```

El script conserva `appsettings.Production.json`, respalda los binarios anteriores y revierte si la URL no responde 200.

## 20. Respaldos y restauración

La base y los documentos forman una unidad lógica. Un respaldo de solo uno de ellos es incompleto.

Secuencia recomendada:

1. detener temporalmente la ingesta o programar una ventana;
2. ejecutar backup completo de SQL;
3. respaldar el repositorio, Entrada, Procesados y Errores;
4. registrar fecha y relación entre ambos respaldos;
5. reanudar el servicio;
6. probar restauración periódicamente.

Ejemplo SQL:

```sql
BACKUP DATABASE [DocuCentral]
TO DISK = N'E:\Backups\DocuCentral_full.bak'
WITH CHECKSUM, COMPRESSION, INIT, STATS = 10;
GO
RESTORE VERIFYONLY
FROM DISK = N'E:\Backups\DocuCentral_full.bak'
WITH CHECKSUM;
GO
```

La cuenta del servicio SQL Server necesita escritura sobre la carpeta del backup.

## 21. Monitoreo y mantenimiento

Supervise:

- estado del sitio y App Pool;
- códigos HTTP 5xx;
- Visor de eventos: Application y System;
- espacio libre en SQL01, WEB01 y FILE01;
- crecimiento de base, logs y auditoría;
- archivos acumulados en Entrada y Errores;
- expiración del certificado;
- resultado de respaldos;
- actualizaciones del Hosting Bundle y .NET 8.

Comandos útiles:

```powershell
Get-Website -Name DocuCentral
Get-WebAppPoolState -Name DocuCentral
Get-ChildItem Cert:\LocalMachine\My | Sort-Object NotAfter
```

## 22. Solución de problemas

### HTTP 401

- token ausente, vencido o invalidado;
- usuario desactivado o contraseña/roles cambiados;
- iniciar sesión nuevamente.

### HTTP 403

- el usuario está autenticado pero no posee el permiso requerido;
- revisar roles, permisos y alcance por área.

### HTTP 405 en PUT o DELETE

- comprobar que el detalle de IIS no indique `WebDAVModule`;
- verificar que el `web.config` publicado contiene los dos `<remove>` de WebDAV;
- reciclar el App Pool.

### HTTP 500.30

- la aplicación no pudo iniciar;
- revisar Event Viewer, cadena SQL, clave JWT y permisos de carpetas;
- habilitar temporalmente `stdoutLogEnabled="true"`, crear `logs` y otorgar escritura; deshabilitarlo después.

### HTTP 500.31

- runtime o Hosting Bundle faltante/incompatible;
- instalar o reparar ASP.NET Core 8 Hosting Bundle después de IIS.

### HTTP 502.5

- proceso ASP.NET Core no inicia o termina;
- revisar runtime, arquitectura, permisos y logs del módulo ASP.NET Core.

### Error de conexión SQL

- validar DNS, puerto, firewall y TCP/IP;
- comprobar login/usuario y cadena de confianza del certificado SQL;
- ejecutar `Test-NetConnection` desde WEB01.

### Acceso denegado a UNC

- confirmar que el App Pool usa la cuenta de dominio;
- revisar permisos del recurso compartido y NTFS;
- probar escritura bajo la misma identidad;
- no usar unidades mapeadas.

### Archivos grandes rechazados

- revisar `Almacenamiento:TamanoMaximoMB`;
- revisar `requestFiltering/requestLimits` de IIS si se supera el límite del servidor;
- confirmar espacio temporal y del repositorio.

### Archivos no procesados

- comprobar que la carpeta vigilada está activa;
- revisar Entrada y Errores;
- verificar `AlwaysRunning`, `preloadEnabled` e idle timeout 0;
- confirmar que el archivo terminó de copiarse y lleva al menos 10 segundos estable.

## 23. Referencias oficiales

- [Hospedar ASP.NET Core en IIS](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/)
- [Publicar ASP.NET Core en IIS](https://learn.microsoft.com/aspnet/core/tutorials/publish-to-iis)
- [Application Initialization](https://learn.microsoft.com/iis/configuration/system.webserver/applicationinitialization/)
- [Application Pool Defaults](https://learn.microsoft.com/iis/configuration/system.applicationhost/applicationpools/applicationpooldefaults/)
- [New-WebBinding](https://learn.microsoft.com/powershell/module/webadministration/new-webbinding)
- [Configurar cuentas de servicio de SQL Server](https://learn.microsoft.com/sql/database-engine/configure-windows/configure-windows-service-accounts-and-permissions)
- [Crear un backup completo de SQL Server](https://learn.microsoft.com/sql/relational-databases/backup-restore/create-a-full-database-backup-sql-server)
- [Restaurar una base de datos](https://learn.microsoft.com/sql/relational-databases/backup-restore/restore-database-general-page)

## 24. Siguiente paso

Complete [CHECKLIST_INSTALACION_SERVIDOR.md](CHECKLIST_INSTALACION_SERVIDOR.md) y conserve las evidencias de aceptación junto con la documentación operativa del ambiente.
