# Checklist de instalación y aceptación de DocuCentral

Use este documento junto con [GUIA_INSTALACION_SERVIDOR.md](GUIA_INSTALACION_SERVIDOR.md). Adjunte capturas, salidas de comandos o tickets como evidencia cuando la política de la organización lo requiera.

## Datos del ambiente

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
| Ruta de procesados | |
| Ruta de errores | |
| Responsable operativo | |
| Fecha de instalación | |
| Versión/commit instalado | |

## Infraestructura

- [ ] WEB01 y SQL01 cumplen las versiones aprobadas de Windows/SQL Server.
- [ ] El nombre DNS de DocuCentral resuelve a WEB01.
- [ ] TCP 443 está permitido desde las redes de usuarios.
- [ ] WEB01 puede conectarse al puerto TCP fijo de SQL01.
- [ ] WEB01 puede conectarse a TCP 445 de FILE01 cuando se usa UNC.
- [ ] SQL Server y SMB no están publicados hacia Internet.
- [ ] IIS y Application Initialization están instalados.
- [ ] ASP.NET Core 8 Hosting Bundle está instalado después de IIS.
- [ ] WEB01 y SQL01 tienen sincronización horaria.
- [ ] Existe capacidad de disco y alerta de espacio libre.

## Seguridad

- [ ] Existe una cuenta dedicada o gMSA para el App Pool.
- [ ] La cuenta del App Pool no es administradora local.
- [ ] La cuenta no pertenece a `sysadmin` en SQL Server.
- [ ] El certificado TLS está instalado en `LocalMachine\My`.
- [ ] CN/SAN del certificado coincide con el DNS publicado.
- [ ] La cadena del certificado es confiable desde los clientes.
- [ ] La clave JWT fue generada con 64 bytes aleatorios o más.
- [ ] Los secretos no están almacenados en Git.
- [ ] Swagger está deshabilitado en producción.
- [ ] WebDAV está removido únicamente en el sitio DocuCentral.
- [ ] Las reglas de firewall están limitadas por origen y destino.

## Base de datos

- [ ] La base `DocuCentral` existe en SQL01.
- [ ] Existe login y usuario para la cuenta de aplicación.
- [ ] `db_owner` se otorgó solo para la inicialización.
- [ ] El primer arranque creó 13 tablas.
- [ ] Existe el usuario inicial `admin`.
- [ ] `Database:AutoCreate` quedó en `false` después del primer arranque.
- [ ] La cuenta de aplicación quedó solo en `db_datareader` y `db_datawriter`.
- [ ] La cuenta de aplicación no es propietaria de la base.
- [ ] Se definió mantenimiento de índices/estadísticas según política DBA.
- [ ] Existe trabajo de backup y alerta ante fallos.

## Almacenamiento

- [ ] Existen Repositorio, Entrada, Procesados y Errores.
- [ ] Las rutas configuradas son absolutas o UNC; no usan unidades mapeadas.
- [ ] La cuenta del App Pool tiene `Modify` sobre las cuatro carpetas.
- [ ] En UNC, la cuenta tiene permisos de recurso compartido y NTFS.
- [ ] Se probó crear y eliminar un archivo bajo la identidad del servicio.
- [ ] El antivirus analiza Entrada según la política corporativa.
- [ ] Las exclusiones de antivirus necesarias fueron aprobadas.
- [ ] El repositorio forma parte del plan de respaldo.

## Aplicación e IIS

- [ ] La publicación contiene `DocuCentral.Api.dll`.
- [ ] La publicación contiene `wwwroot\index.html`.
- [ ] La publicación contiene `web.config`.
- [ ] Existe `appsettings.Production.json` fuera de Git.
- [ ] El App Pool usa `No Managed Code` e Integrated pipeline.
- [ ] El App Pool usa la cuenta de servicio correcta.
- [ ] Start Mode está en `AlwaysRunning`.
- [ ] Idle Timeout está en 0.
- [ ] `preloadEnabled` está en `true`.
- [ ] El binding HTTPS usa el host y certificado correctos.
- [ ] El sitio y el App Pool están iniciados.

## Pruebas funcionales

- [ ] La URL HTTPS responde HTTP 200.
- [ ] `/api/auth/config` responde correctamente.
- [ ] El administrador inicia sesión.
- [ ] La contraseña temporal fue cambiada.
- [ ] Se creó un área de prueba.
- [ ] Se creó un tipo documental de prueba.
- [ ] Se cargó un documento de prueba.
- [ ] El documento se visualiza y descarga.
- [ ] Una operación PUT sin token devuelve 401 y no 405.
- [ ] Se guardó una modificación de configuración.
- [ ] La carpeta vigilada importó un archivo.
- [ ] Los eventos aparecen en Auditoría.
- [ ] El sistema continúa operativo después de reciclar el App Pool.
- [ ] El sistema continúa operativo después de reiniciar WEB01.

## Respaldo y recuperación

- [ ] Se creó un backup completo de `DocuCentral` con `CHECKSUM`.
- [ ] `RESTORE VERIFYONLY` finalizó correctamente.
- [ ] Se respaldaron las carpetas documentales del mismo punto operativo.
- [ ] Existe una relación identificable entre backup SQL y backup de archivos.
- [ ] La retención cumple la política corporativa.
- [ ] Se realizó una restauración de prueba en un ambiente aislado.
- [ ] Se documentaron RPO y RTO.

## Operación y monitoreo

- [ ] Se monitorean sitio, App Pool y HTTP 5xx.
- [ ] Se monitorean espacio de disco y crecimiento SQL.
- [ ] Se monitorean Entrada y Errores.
- [ ] Se monitorea vencimiento del certificado.
- [ ] Se monitorean backups y restauraciones de prueba.
- [ ] Existe un responsable para actualizaciones del Hosting Bundle/.NET.
- [ ] El procedimiento de actualización y reversión fue probado.
- [ ] La documentación y secretos están bajo control de acceso.

## Aprobaciones

| Rol | Nombre | Resultado | Fecha | Firma/Ticket |
|---|---|---|---|---|
| Infraestructura | | | | |
| DBA | | | | |
| Seguridad | | | | |
| Dueño de aplicación | | | | |
| Operaciones | | | | |
