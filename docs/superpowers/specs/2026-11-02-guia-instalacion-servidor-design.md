# Diseño de la guía de instalación de DocuCentral en servidores separados

## Objetivo

Crear una guía maestra en español para instalar, asegurar, validar, actualizar y recuperar DocuCentral sin Docker en una infraestructura Windows con IIS y SQL Server separados. La guía debe servir tanto para una primera instalación como para despliegues repetibles y auditables.

## Audiencia

Administradores de sistemas, infraestructura y bases de datos con acceso administrativo a Windows Server, IIS y SQL Server. El documento explicará cada responsabilidad por servidor y proporcionará comandos listos para adaptar, sin asumir conocimiento previo del código de DocuCentral.

## Arquitectura cubierta

- Servidor web Windows Server con IIS y ASP.NET Core 8 Hosting Bundle.
- Servidor SQL Server independiente.
- Equipo de compilación con .NET SDK 8 o superior y Node.js 20 o superior.
- Acceso mediante DNS y HTTPS.
- Autenticación local de DocuCentral y configuración opcional de Active Directory/LDAP.
- Dos alternativas de almacenamiento documental:
  - carpetas locales en el servidor IIS;
  - recurso compartido UNC en un servidor de archivos.

Cuando el almacenamiento sea UNC, el App Pool utilizará una cuenta de servicio de dominio con permisos sobre el recurso compartido y NTFS. Para almacenamiento local podrá utilizarse la identidad virtual del App Pool.

## Estructura de la guía

1. Propósito, alcance y arquitectura.
2. Inventario y requisitos previos.
3. Diagrama lógico y matriz de comunicaciones.
4. Preparación de cuentas, DNS y certificados.
5. Instalación y configuración de SQL Server.
6. Creación de la base de datos, login y permisos iniciales.
7. Preparación de IIS y ASP.NET Core Hosting Bundle.
8. Compilación y publicación de React y ASP.NET Core.
9. Configuración de `appsettings.Production.json` y secretos.
10. Configuración del almacenamiento local o UNC.
11. Creación del App Pool, sitio, bindings HTTPS y exclusión de WebDAV.
12. Primera ejecución, creación del esquema y reducción de permisos SQL.
13. Pruebas funcionales y de seguridad.
14. Instalación automatizada mediante PowerShell.
15. Actualización, reversión y conservación de configuración.
16. Respaldo coordinado de SQL Server y documentos.
17. Monitoreo y mantenimiento.
18. Solución de problemas.
19. Checklist de aceptación y hoja de datos del ambiente.

## Métodos de instalación

La guía explicará dos métodos equivalentes:

### Instalación manual

Procedimiento paso a paso mediante SQL Server Management Studio o `sqlcmd`, Administrador de IIS y PowerShell. Cada paso incluirá su objetivo, el servidor donde se ejecuta, la evidencia esperada y una prueba de validación.

### Instalación automatizada

Scripts pequeños, parametrizados y auditables:

- `Publicar-DocuCentral.ps1`: restaura dependencias, compila el frontend y publica la API.
- `Preparar-SqlDocuCentral.ps1`: crea la base, el login y los permisos iniciales.
- `Instalar-DocuCentralIIS.ps1`: configura carpetas, permisos, App Pool, sitio, HTTPS y WebDAV.
- `Probar-DocuCentral.ps1`: valida sitio, API, SQL Server, permisos y almacenamiento.
- `Actualizar-DocuCentral.ps1`: crea respaldo, despliega una versión y admite reversión.

Los scripts detendrán la ejecución ante errores, validarán prerrequisitos y no contendrán contraseñas fijas. Los secretos se solicitarán de forma segura o se recibirán mediante mecanismos protegidos.

## Seguridad

- HTTPS obligatorio para usuarios.
- Clave JWT generada criptográficamente con al menos 64 bytes de entropía.
- Contraseña administrativa inicial temporal y cambio forzado.
- Cuenta de aplicación dedicada y principio de mínimo privilegio.
- Permisos SQL de definición solo durante la creación inicial; posteriormente, acceso de lectura y escritura de datos.
- Cuenta de dominio para almacenamiento UNC, con permisos limitados al recurso de DocuCentral.
- Secretos fuera del control de versiones.
- WebDAV deshabilitado en el sitio para permitir `PUT` y `DELETE` sin desinstalarlo globalmente.
- Swagger deshabilitado en producción salvo necesidad operativa controlada.
- Reglas de firewall limitadas a los servidores y puertos necesarios.

## Flujo de datos y dependencias

El navegador accede al servidor IIS mediante HTTPS. IIS hospeda la aplicación ASP.NET Core y sirve el frontend React. La aplicación se conecta al SQL Server remoto y lee o escribe documentos en almacenamiento local o UNC. La carpeta vigilada se ejecuta dentro del proceso de la aplicación, por lo que el App Pool debe permanecer iniciado y disponer de permisos sobre entrada, procesados, errores y repositorio.

La guía dejará claro que la base de datos y los archivos forman una sola unidad lógica de respaldo y recuperación.

## Manejo de errores y reversión

- Cada etapa incluirá comprobaciones antes de continuar.
- Las actualizaciones conservarán `appsettings.Production.json`, documentos y secretos.
- Antes de reemplazar binarios se detendrá el App Pool y se respaldará la versión publicada.
- La reversión restaurará binarios y configuración compatibles.
- Los cambios de esquema futuros deberán administrarse con migraciones de EF Core; no se tratará `EnsureCreated` como estrategia permanente de evolución.
- La sección de diagnóstico cubrirá errores 401, 403, 405/WebDAV, 500.30, 500.31, 502.5, conexión SQL, permisos UNC, archivos bloqueados y límites de carga.

## Validación y aceptación

La instalación se considerará aceptada cuando se demuestre:

- compilación limpia del frontend y backend;
- respuesta HTTPS correcta y certificado válido;
- conexión a SQL Server y esquema creado;
- permisos SQL reducidos después de la inicialización;
- inicio de sesión y cambio de contraseña inicial;
- creación, visualización y descarga de un documento de prueba;
- cambio de configuración sin error 405;
- procesamiento de un archivo mediante la carpeta vigilada;
- registro de auditoría;
- persistencia después de reiniciar el App Pool y el servidor;
- respaldo y restauración de prueba de base y repositorio.

## Entregables

- Guía maestra en Markdown dentro de `docs/`.
- Scripts PowerShell en `deploy/server/`.
- Plantillas de configuración sin secretos reales.
- Checklist imprimible de aceptación.
- Referencias a documentación oficial de Microsoft para IIS, ASP.NET Core, SQL Server, certificados, cuentas de servicio y copias de seguridad.

## Fuera de alcance

- Despliegue con Docker o Kubernetes.
- Alta disponibilidad, clústeres o balanceo de múltiples instancias.
- Compra o emisión de certificados por una autoridad específica.
- Diseño de políticas corporativas de Active Directory.
- Migración de documentos desde sistemas anteriores.
