# DocuCentral — Sistema de gestión documental

Repositorio central de documentos digitalizados para una institución. Todos los archivos se guardan en una ruta configurable, se indexan con datos de búsqueda y se agrupan por **identificador del titular** (cédula, número de socio, expediente…) para obtener en un solo lugar todo lo que pertenece a una persona o caso.

| Capa | Tecnología |
|---|---|
| Frontend | React 19 + TypeScript + Vite |
| API | ASP.NET Core 8 (C#) + Entity Framework Core 8 |
| Base de datos | SQL Server, PostgreSQL o MySQL (se elige por configuración) |
| Autenticación | Usuarios locales (BCrypt) y/o Active Directory (LDAP), con JWT |
| Despliegue | IIS (un solo sitio) o Docker |

---

## Funciones

**Documentos**
- Carga web de uno o varios archivos con sus datos; detección de archivos duplicados por huella SHA-256.
- **Carpeta vigilada**: lo que el escáner o los usuarios dejen en la carpeta de entrada se importa solo al repositorio y queda en la **bandeja de entrada** para que alguien capture sus datos (indexación manual). Subcarpetas con el código de un área (`Entrada\RRHH\…`) asignan el área automáticamente. Los archivos rechazados van a la carpeta de errores con un `.txt` que explica el motivo.
- **Tipos de documento configurables**: el administrador crea tipos (Cédula, Contrato, Pagaré…) y define sus campos (texto, número, fecha, lista, sí/no; obligatorios u opcionales) sin programar.
- **Búsqueda** por texto libre (título, identificador, titular, etiquetas, descripción y cualquier campo), filtros por tipo, área, fechas y campos propios del tipo.
- **Expedientes**: todos los documentos de un identificador, agrupados por tipo, con visor lateral y **descarga del expediente completo en ZIP**.
- **Visor en línea** de PDF, imágenes y texto (el archivo se pide con el token; nunca queda en una URL pública).
- **Versiones**: subir una nueva versión conserva todas las anteriores, consultables y descargables.
- Eliminación lógica con motivo obligatorio (el archivo y su historial se conservan).

**Seguridad**
- Inicio de sesión local o con la cuenta de dominio; alta automática de usuarios de AD (opcional), con grupo de AD requerido (opcional).
- Bloqueo tras intentos fallidos, límite de intentos por IP, política de contraseñas y cambio obligatorio en el primer ingreso.
- **Roles con permisos por acción** (ver, cargar, editar, descargar, eliminar, indexar, administrar…).
- **Alcance por área**: cada usuario ve los documentos de su área y de las áreas adicionales que se le asignen; el permiso "todas las áreas" da acceso completo.
- Al cambiar roles, contraseña o estado de un usuario, sus sesiones abiertas se invalidan.
- **Bitácora de auditoría**: inicios de sesión (exitosos y fallidos), consultas, descargas, cargas, ediciones, versiones, eliminaciones y cambios de configuración, con usuario, fecha e IP.

---

## Estructura

```
DocuCentral/
├── backend/DocuCentral.Api/
│   ├── Controllers/     Auth, Documentos, Expedientes, Bandeja, catálogos y administración
│   ├── Data/            DbContext (multi-motor) y datos iniciales
│   ├── Domain/          Entidades y catálogo de permisos
│   ├── Security/        JWT, LDAP, permisos por política, usuario actual
│   ├── Services/        Almacenamiento, carpeta vigilada, búsqueda, auditoría, parámetros
│   └── appsettings.json
├── frontend/            React (src/pages, src/components, src/api)
├── deploy/              docker-compose por motor de BD y script de publicación para IIS
└── Dockerfile
```

Los archivos se guardan como `{Repositorio}/{año}/{mes}/{idDocumento}/v001.pdf`. En la base de datos solo se guarda la ruta **relativa**, así el repositorio se puede mover cambiando únicamente la configuración (y copiando la carpeta).

---

## Desarrollo local

Requisitos: .NET 8 SDK, Node.js 20+ y uno de los motores de base de datos.

```bash
# 1. API (http://localhost:5080, Swagger en /swagger)
cd backend/DocuCentral.Api
#    Edite appsettings.json: Database:Provider, ConnectionStrings:Default, Jwt:Key y rutas de Almacenamiento
dotnet run

# 2. Frontend (http://localhost:5173, redirige /api al puerto 5080)
cd frontend
npm install
npm run dev
```

Al primer arranque se crean las tablas y los datos iniciales:

- Usuario **admin** con la contraseña de `Seed:AdminPassword` (por defecto `Admin123*`); el sistema pide cambiarla al entrar.
- Área **GEN – General**.
- Roles **Administrador** (todo), **Archivista** (ver, cargar, editar, descargar, indexar) y **Consulta** (ver y descargar).
- Tipos de ejemplo: Cédula de identidad, Contrato y Otro documento.

### Motor de base de datos

| `Database:Provider` | Ejemplo de `ConnectionStrings:Default` |
|---|---|
| `SqlServer` | `Server=SQL01;Database=DocuCentral;User Id=app;Password=…;TrustServerCertificate=True` |
| `PostgreSQL` | `Host=pg01;Database=docucentral;Username=app;Password=…` |
| `MySQL` | `Server=my01;Database=docucentral;User=app;Password=…` (y `Database:ServerVersion`, ej. `8.4.0-mysql`) |

Toda la configuración se puede sobreescribir con variables de entorno usando `__` (ej. `Jwt__Key`, `Database__Provider`).

---

## Despliegue en IIS (Windows Server)

1. En el servidor: instale el **ASP.NET Core 8 Hosting Bundle** y reinicie IIS (`iisreset`).
2. En la máquina de compilación:
   ```powershell
   powershell -ExecutionPolicy Bypass -File deploy\publicar-iis.ps1 -Destino C:\Publicar\DocuCentral
   ```
   Esto compila el frontend dentro de `wwwroot` y publica la API: **un solo sitio** sirve ambas cosas.
3. Copie la carpeta al servidor (ej. `D:\Sitios\DocuCentral`) y cree `appsettings.Production.json` a partir de `appsettings.Production.ejemplo.json`.
4. En IIS: nuevo sitio apuntando a esa carpeta; grupo de aplicaciones con **Sin código administrado**. Configure HTTPS.
5. **Permisos de las carpetas**: la identidad del grupo de aplicaciones (`IIS AppPool\DocuCentral`) necesita *Modificar* sobre el repositorio, la entrada y la carpeta de errores. Si las rutas son de red (`\\servidor\...`), ejecute el grupo de aplicaciones con una **cuenta de servicio de dominio** que tenga permisos en el recurso compartido (la identidad virtual del AppPool no tiene acceso a la red).
6. Para que la carpeta vigilada funcione aunque nadie entre al sitio, en el grupo de aplicaciones ponga *Modo de inicio = AlwaysRunning* y *Tiempo de espera de inactividad = 0*, y en el sitio *Precarga habilitada = True* (requiere el módulo Application Initialization).

Como alternativa a IIS, la misma publicación puede instalarse como **servicio de Windows**:
`sc create DocuCentral binPath= "D:\Sitios\DocuCentral\DocuCentral.Api.exe" start= auto`

## Despliegue con Docker

```bash
cp .env.example .env          # cambie JWT_KEY, ADMIN_PASSWORD, DB_PASSWORD y las rutas
docker compose -f deploy/docker-compose.postgres.yml --env-file .env up -d --build
# o docker-compose.sqlserver.yml / docker-compose.mysql.yml
```

El sistema queda en `http://servidor:8080`. Las rutas `RUTA_REPOSITORIO`, `RUTA_ENTRADA` y `RUTA_ERRORES` del `.env` son carpetas del servidor que se montan dentro del contenedor; para un recurso compartido de Windows, móntelo primero en el host (CIFS) y apunte ahí. Ponga un proxy inverso con HTTPS delante (nginx, Traefik, IIS ARR).

---

## Configuración de rutas

Las rutas se definen en `appsettings.json` (sección `Almacenamiento`) y luego un administrador puede cambiarlas desde **Administración → Configuración**, que verifica que el servicio pueda escribir en cada una antes de guardar.

| Parámetro | Uso |
|---|---|
| Repositorio | Donde quedan todos los archivos. Si se cambia, **copie antes** el contenido de la ruta anterior a la nueva. |
| Entrada | Carpeta vigilada. Subcarpetas con el código de un área asignan el área. |
| Procesados (opcional) | Si se indica, guarda una copia del original importado; si no, el original se borra de la entrada. |
| Errores | Archivos rechazados + `.error.txt` con el motivo. |
| Extensiones / tamaño | Lista blanca de extensiones y tamaño máximo por archivo. |
| Intervalo | Cada cuántos segundos se revisa la entrada (mínimo 5). |

Un archivo se importa cuando lleva al menos 10 segundos sin modificarse y nadie lo tiene abierto, así no se toman escaneos a medio copiar.

## Active Directory

En `appsettings`, sección `Ldap`: `Habilitado`, `Servidor`, `Puerto` (636 con `UsarSsl`), `Dominio`, `BaseDn`. La contraseña se valida contra AD en cada inicio de sesión y nunca se guarda.

- Usuario de dominio creado a mano: en **Usuarios**, elija *Active Directory* y use su nombre de red.
- `AutoCrearUsuarios = true`: el primer inicio de sesión exitoso crea el usuario con `RolPorDefecto` y `AreaPorDefecto`.
- `GrupoRequerido`: DN de un grupo de AD; solo sus miembros pueden entrar.

---

## Seguridad en producción — lista de verificación

- [ ] `Jwt:Key` aleatoria de 64+ caracteres, distinta por ambiente y fuera del control de versiones.
- [ ] Contraseña del `admin` cambiada en el primer ingreso.
- [ ] HTTPS obligatorio (el token viaja en cada petición).
- [ ] Usuario de base de datos propio de la aplicación, sin permisos de administrador.
- [ ] Respaldos coordinados de **base de datos y repositorio de archivos** (uno sin el otro no sirve).
- [ ] Antivirus del servidor excluyendo la carpeta temporal de la aplicación, pero escaneando la carpeta de entrada.

## Esquema de base de datos y cambios futuros

En el primer arranque el esquema se crea con `EnsureCreated`, que funciona igual en los tres motores. Para evolucionar el esquema más adelante sin perder datos, cambie a migraciones de EF Core para el motor que use en producción:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add Inicial
# y en appsettings: "Database": { "AutoCreate": false }, aplicando con: dotnet ef database update
```

## API

Con `Swagger:Habilitado = true` (activo en desarrollo) la documentación interactiva queda en `/swagger`. Endpoints principales:

| Método y ruta | Permiso |
|---|---|
| `POST /api/auth/login` · `GET /api/auth/me` · `POST /api/auth/cambiar-password` | — |
| `POST /api/documentos/buscar` · `GET /api/documentos/{id}` | documentos.ver |
| `GET /api/documentos/{id}/archivo?inline=true\|false&version=n` | ver / descargar |
| `POST /api/documentos` (multipart: `archivo` + `metadatos` JSON) | documentos.cargar |
| `PUT /api/documentos/{id}` · `POST /api/documentos/{id}/versiones` | documentos.editar |
| `DELETE /api/documentos/{id}?motivo=` | documentos.eliminar |
| `GET /api/expedientes?q=` · `GET /api/expedientes/{identificador}` | documentos.ver |
| `GET /api/expedientes/{identificador}/zip` | documentos.descargar |
| `GET /api/bandeja` · `POST /api/bandeja/{id}/indexar` | bandeja.indexar |
| `/api/tipos-documento`, `/api/areas`, `/api/usuarios`, `/api/roles`, `/api/configuracion`, `/api/auditoria` | permisos de administración |
