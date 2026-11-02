# Guía de instalación de DocuCentral en servidores separados — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Producir una guía operativa completa en español y scripts PowerShell seguros para instalar DocuCentral sin Docker con IIS y SQL Server en servidores separados, usando almacenamiento local o UNC.

**Architecture:** La documentación tendrá una guía maestra con decisiones explícitas por escenario y un checklist independiente. Los scripts estarán separados por responsabilidad: publicación, preparación SQL, instalación IIS, validación y actualización; todos aceptarán parámetros y evitarán secretos fijos.

**Tech Stack:** Markdown, PowerShell 5.1+, IIS 10/WebAdministration, ASP.NET Core 8 Hosting Bundle, SQL Server/sqlcmd, React 19/Vite, .NET 8.

## Global Constraints

- IIS y SQL Server se ejecutan en servidores separados.
- El repositorio documental puede ser local en IIS o una ruta UNC en un servidor de archivos.
- La guía incluye instalación manual y automatizada.
- HTTPS es obligatorio para producción.
- Los secretos no se guardan en Git ni se escriben en texto claro dentro de los scripts.
- El App Pool usa una cuenta de dominio cuando SQL Server o el almacenamiento están en otro servidor.
- WebDAV se deshabilita solo en el sitio DocuCentral.
- La base de datos y el repositorio documental se respaldan como una unidad lógica.
- No se cubren Docker, Kubernetes, alta disponibilidad ni migración desde sistemas anteriores.

## Estructura de archivos

- Crear `docs/GUIA_INSTALACION_SERVIDOR.md`: manual maestro y referencias oficiales.
- Crear `docs/CHECKLIST_INSTALACION_SERVIDOR.md`: checklist imprimible y hoja de datos.
- Crear `deploy/server/appsettings.Production.template.json`: configuración segura sin secretos.
- Crear `deploy/server/Publicar-DocuCentral.ps1`: compilación reproducible.
- Crear `deploy/server/Preparar-SqlDocuCentral.ps1`: base, login y permisos iniciales.
- Crear `deploy/server/Instalar-DocuCentralIIS.ps1`: carpetas, ACL, App Pool, sitio y HTTPS.
- Crear `deploy/server/Probar-DocuCentral.ps1`: pruebas no destructivas de aceptación.
- Crear `deploy/server/Actualizar-DocuCentral.ps1`: respaldo de binarios, despliegue y reversión.
- Modificar `README.md`: enlazar la guía y el checklist.

---

### Task 1: Redactar la guía maestra

**Files:**
- Create: `docs/GUIA_INSTALACION_SERVIDOR.md`
- Reference: `README.md`
- Reference: `backend/DocuCentral.Api/appsettings.Production.ejemplo.json`
- Reference: `backend/DocuCentral.Api/web.config`

**Interfaces:**
- Consumes: estructura real de DocuCentral, parámetros de configuración y scripts existentes.
- Produces: procedimiento manual completo que sirve como contrato para los scripts de las tareas siguientes.

- [ ] **Step 1: Crear el encabezado, audiencia y arquitectura**

Incluir literalmente una matriz con estos nodos:

```markdown
| Nodo | Responsabilidad | Software mínimo |
|---|---|---|
| BUILD01 | Compilar y publicar | .NET SDK 8+, Node.js 20+, npm |
| WEB01 | Hospedar frontend/API | Windows Server, IIS, Hosting Bundle 8 |
| SQL01 | Persistencia | SQL Server 2019+ o versión corporativa soportada |
| FILE01 | Opcional: documentos UNC | SMB, NTFS, respaldo y antivirus |
```

- [ ] **Step 2: Documentar requisitos, puertos y cuentas**

La tabla mínima de comunicaciones será:

```markdown
| Origen | Destino | Puerto | Uso |
|---|---|---:|---|
| Usuarios | WEB01 | TCP 443 | HTTPS |
| WEB01 | SQL01 | TCP 1433 o puerto fijo de instancia | SQL Server |
| WEB01 | FILE01 | TCP 445 | SMB/UNC |
| WEB01 | DC/DNS | TCP/UDP 53 y puertos corporativos LDAP | DNS/AD opcional |
```

- [ ] **Step 3: Documentar instalación manual de SQL Server**

Incluir T-SQL parametrizable para una cuenta de dominio:

```sql
CREATE DATABASE [DocuCentral];
GO
CREATE LOGIN [DOMINIO\svc_docucentral] FROM WINDOWS;
GO
USE [DocuCentral];
CREATE USER [DOMINIO\svc_docucentral] FOR LOGIN [DOMINIO\svc_docucentral];
ALTER ROLE [db_owner] ADD MEMBER [DOMINIO\svc_docucentral];
GO
```

Explicar que `db_owner` es temporal durante `EnsureCreated` y se sustituye después por:

```sql
ALTER ROLE [db_owner] DROP MEMBER [DOMINIO\svc_docucentral];
ALTER ROLE [db_datareader] ADD MEMBER [DOMINIO\svc_docucentral];
ALTER ROLE [db_datawriter] ADD MEMBER [DOMINIO\svc_docucentral];
```

- [ ] **Step 4: Documentar compilación y publicación**

Incluir comandos exactos:

```powershell
Set-Location C:\Fuentes\DocuCentral\frontend
npm ci
npm run build:iis

Set-Location C:\Fuentes\DocuCentral
dotnet publish .\backend\DocuCentral.Api\DocuCentral.Api.csproj `
  -c Release `
  -o C:\Publicar\DocuCentral
```

- [ ] **Step 5: Documentar configuración de IIS y almacenamiento**

Cubrir ambos escenarios con comandos `icacls`:

```powershell
# Local
icacls D:\DocuCentral /grant "DOMINIO\svc_docucentral:(OI)(CI)M" /T /C

# UNC: ejecutar permisos NTFS en FILE01 y conceder también Change en el recurso compartido
icacls D:\Compartidos\DocuCentral /grant "DOMINIO\svc_docucentral:(OI)(CI)M" /T /C
```

Incluir App Pool `No Managed Code`, `AlwaysRunning`, tiempo de inactividad 0, `preloadEnabled=true`, WebDAV removido y binding HTTPS.

- [ ] **Step 6: Documentar primera ejecución y reducción de privilegios**

Describir esta secuencia exacta: `AutoCreate=true` → iniciar sitio → comprobar 13 tablas y login → `AutoCreate=false` → retirar `db_owner` → reciclar App Pool.

- [ ] **Step 7: Documentar actualización, reversión, respaldo y diagnóstico**

Incluir errores 401, 403, 405/WebDAV, 500.30, 500.31, 502.5, conexión SQL, permisos UNC, límites de carga y archivos bloqueados.

- [ ] **Step 8: Añadir referencias oficiales**

Usar enlaces directos de Microsoft Learn:

```markdown
- [Hospedar ASP.NET Core en IIS](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/)
- [Publicar ASP.NET Core en IIS](https://learn.microsoft.com/aspnet/core/tutorials/publish-to-iis)
- [Application Initialization](https://learn.microsoft.com/iis/configuration/system.webserver/applicationinitialization/)
- [New-WebBinding](https://learn.microsoft.com/powershell/module/webadministration/new-webbinding)
- [Crear un backup completo de SQL Server](https://learn.microsoft.com/sql/relational-databases/backup-restore/create-a-full-database-backup-sql-server)
```

- [ ] **Step 9: Validar estructura de la guía**

Run:

```powershell
rg -n "^## " docs\GUIA_INSTALACION_SERVIDOR.md
rg -n "TBD|TODO|POR DEFINIR|contraseña-real|clave-real" docs\GUIA_INSTALACION_SERVIDOR.md
```

Expected: aparecen todas las secciones aprobadas; la segunda búsqueda no devuelve resultados.

- [ ] **Step 10: Commit**

```powershell
git add docs/GUIA_INSTALACION_SERVIDOR.md
git commit -m "docs: add server installation guide"
```

### Task 2: Crear plantilla segura y scripts de publicación/SQL

**Files:**
- Create: `deploy/server/appsettings.Production.template.json`
- Create: `deploy/server/Publicar-DocuCentral.ps1`
- Create: `deploy/server/Preparar-SqlDocuCentral.ps1`

**Interfaces:**
- Consumes: raíz del repositorio, nombre DNS SQL, base y cuenta de dominio.
- Produces: carpeta publicada y base preparada para el primer arranque.

- [ ] **Step 1: Crear la plantilla de producción sin secretos**

La plantilla usará marcadores explícitos:

```json
{
  "Database": { "Provider": "SqlServer", "AutoCreate": true },
  "ConnectionStrings": {
    "Default": "Server=SQL01;Database=DocuCentral;Integrated Security=True;Encrypt=True;TrustServerCertificate=False"
  },
  "Jwt": { "Key": "REEMPLAZAR_MEDIANTE_SECRETO_SEGURO" },
  "Seed": { "AdminPassword": "REEMPLAZAR_MEDIANTE_SECRETO_SEGURO" },
  "Ldap": { "Habilitado": false },
  "Almacenamiento": {
    "RutaRepositorio": "D:\\DocuCentral\\Repositorio",
    "RutaEntrada": "D:\\DocuCentral\\Entrada",
    "RutaProcesados": "D:\\DocuCentral\\Procesados",
    "RutaErrores": "D:\\DocuCentral\\Errores"
  },
  "Cors": { "Origenes": [] },
  "Swagger": { "Habilitado": false }
}
```

- [ ] **Step 2: Implementar publicación reproducible**

`Publicar-DocuCentral.ps1` aceptará `-RaizFuente` y `-Destino`, ejecutará `npm ci`, `npm run build:iis` y `dotnet publish`, y terminará si falta `DocuCentral.Api.dll` o `wwwroot/index.html`.

- [ ] **Step 3: Implementar preparación SQL idempotente**

`Preparar-SqlDocuCentral.ps1` aceptará `-ServidorSql`, `-BaseDatos`, `-CuentaAplicacion` y `-ReducirPermisos`. Usará `sqlcmd -b` y separará `CREATE DATABASE` de `USE`, evitando el error de compilación de lotes observado durante la instalación local.

- [ ] **Step 4: Validar sintaxis PowerShell y JSON**

```powershell
$files = @(
  'deploy/server/Publicar-DocuCentral.ps1',
  'deploy/server/Preparar-SqlDocuCentral.ps1'
)
foreach ($file in $files) {
  $errors = $null
  [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path $file), [ref]$null, [ref]$errors) | Out-Null
  if ($errors) { throw ($errors | Out-String) }
}
Get-Content -Raw deploy/server/appsettings.Production.template.json | ConvertFrom-Json | Out-Null
```

Expected: salida limpia y exit code 0.

- [ ] **Step 5: Commit**

```powershell
git add deploy/server
git commit -m "feat: add publication and SQL deployment scripts"
```

### Task 3: Crear scripts IIS, validación y actualización

**Files:**
- Create: `deploy/server/Instalar-DocuCentralIIS.ps1`
- Create: `deploy/server/Probar-DocuCentral.ps1`
- Create: `deploy/server/Actualizar-DocuCentral.ps1`

**Interfaces:**
- Consumes: publicación, cuenta de dominio, rutas local/UNC, DNS, certificado y puerto SQL ya preparado.
- Produces: sitio IIS operativo, informe de pruebas y actualización reversible.

- [ ] **Step 1: Implementar instalador IIS parametrizado**

Parámetros obligatorios:

```powershell
param(
  [Parameter(Mandatory)][string]$OrigenPublicado,
  [Parameter(Mandatory)][string]$RutaSitio,
  [Parameter(Mandatory)][string]$NombreSitio,
  [Parameter(Mandatory)][string]$CuentaAplicacion,
  [Parameter(Mandatory)][string]$HostHeader,
  [Parameter(Mandatory)][string]$CertificadoThumbprint,
  [string]$RutaDatos = 'D:\DocuCentral'
)
```

Debe validar Hosting Bundle, crear App Pool con identidad específica, asignar ACL, respaldar instalación previa, copiar publicación, crear binding HTTPS, asociar certificado, habilitar preload y comprobar HTTP 200.

- [ ] **Step 2: Implementar prueba no destructiva**

`Probar-DocuCentral.ps1` comprobará DNS, TCP SQL, TCP SMB si aplica, HTTPS, certificado, `/api/auth/config`, presencia de WebDAV removido, rutas y estado IIS. No debe modificar configuración ni pedir contraseña de usuario.

- [ ] **Step 3: Implementar actualización reversible**

`Actualizar-DocuCentral.ps1` debe:

```text
1. Validar publicación nueva.
2. Detener sitio y App Pool.
3. Esperar estado Stopped.
4. Mover la versión actual a <ruta>.bak.<timestamp>.
5. Copiar la nueva versión.
6. Restaurar appsettings.Production.json desde el respaldo.
7. Iniciar App Pool y sitio.
8. Comprobar HTTP 200.
9. Si falla, restaurar automáticamente el respaldo.
```

- [ ] **Step 4: Validar sintaxis de los tres scripts**

Ejecutar el mismo parser de PowerShell de Task 2 sobre los tres archivos. Expected: cero errores.

- [ ] **Step 5: Commit**

```powershell
git add deploy/server
git commit -m "feat: add IIS installation and update scripts"
```

### Task 4: Crear checklist y enlazar documentación

**Files:**
- Create: `docs/CHECKLIST_INSTALACION_SERVIDOR.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: criterios de aceptación de la especificación.
- Produces: hoja imprimible y acceso visible desde la raíz del repositorio.

- [ ] **Step 1: Crear checklist por responsable**

Incluir grupos `Infraestructura`, `DBA`, `Seguridad`, `Aplicación`, `Pruebas` y `Operación`, con casillas para DNS, certificado, puertos, cuenta, SQL, rutas, backups y evidencia.

- [ ] **Step 2: Añadir hoja de datos del ambiente**

```markdown
| Dato | Valor |
|---|---|
| Ambiente | |
| URL HTTPS | |
| Servidor IIS | |
| Servidor/instancia SQL | |
| Base de datos | DocuCentral |
| Cuenta del App Pool | |
| Ruta física del sitio | |
| Ruta del repositorio | |
| Ruta de entrada | |
| Responsable operativo | |
| Fecha de instalación | |
```

- [ ] **Step 3: Enlazar desde README**

Añadir una sección `Documentación de despliegue` con enlaces relativos a ambos documentos y a `deploy/server/`.

- [ ] **Step 4: Validar enlaces locales**

```powershell
Test-Path docs/GUIA_INSTALACION_SERVIDOR.md
Test-Path docs/CHECKLIST_INSTALACION_SERVIDOR.md
Test-Path deploy/server
```

Expected: los tres resultados son `True`.

- [ ] **Step 5: Commit**

```powershell
git add README.md docs/CHECKLIST_INSTALACION_SERVIDOR.md
git commit -m "docs: add deployment acceptance checklist"
```

### Task 5: Verificación integral y publicación en GitHub

**Files:**
- Verify: `docs/GUIA_INSTALACION_SERVIDOR.md`
- Verify: `docs/CHECKLIST_INSTALACION_SERVIDOR.md`
- Verify: `deploy/server/*`
- Verify: `README.md`

**Interfaces:**
- Consumes: todos los entregables anteriores.
- Produces: rama `main` compilada, documentada y sincronizada con GitHub.

- [ ] **Step 1: Escanear marcadores y secretos**

```powershell
rg -n "TBD|TODO|POR DEFINIR" docs deploy/server
rg -n -i "password\s*=|jwt.*[A-Za-z0-9+/]{40,}|api[_-]?key" docs deploy/server
```

Expected: ningún secreto real; solo marcadores seguros de plantilla y explicaciones.

- [ ] **Step 2: Compilar frontend**

```powershell
Set-Location frontend
npm run build:iis
```

Expected: `built` y exit code 0.

- [ ] **Step 3: Publicar backend**

```powershell
Set-Location ..
dotnet publish .\backend\DocuCentral.Api\DocuCentral.Api.csproj `
  -c Release -o .\artifacts\guide-final-verify --no-restore
```

Expected: `DocuCentral.Api.dll` publicado y exit code 0.

- [ ] **Step 4: Validar scripts y Markdown**

Ejecutar parser PowerShell sobre todos los `.ps1`, `ConvertFrom-Json` sobre la plantilla y `git diff --check`. Expected: cero errores.

- [ ] **Step 5: Revisar estado y sincronizar**

```powershell
git status --short --branch
git push origin main
git ls-remote origin refs/heads/main
```

Expected: árbol limpio y SHA remoto igual a `git rev-parse HEAD`.
