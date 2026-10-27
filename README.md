# devportal-api

**Portal API**: API de prueba en ASP.NET Core (.NET 10, minimal APIs) para probar el playground
del portal de desarrolladores hecho con Fumadocs. Expone recursos del mismo dominio que el portal
(catálogo de APIs, suscripciones, dashboard) y sirve su propia especificación OpenAPI.

Estado actual: salud (`GET /health`), catálogo de APIs (`GET /apis`, `GET /apis/{id}`), clientes
(`GET /clients`), token JWT (`POST /auth/token`), dashboard privado (`GET /dashboard/stats`,
`GET /dashboard/activity`), suscripciones privadas del usuario (CRUD en `/subscriptions`), base
SQLite sembrada al arrancar, CORS, formato único de errores y especificación OpenAPI generada desde
el código: se sirve en `/openapi/v1.json` y cada `dotnet build` la escribe en `openapi/portal-api.json`
(el archivo que copia el portal).

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download) (`global.json` exige como mínimo la 10.0.403 y acepta
  versiones posteriores, por `rollForward: latestFeature`). Se verifica con `dotnet --list-sdks`.

## Comandos

Antes del primer `dotnet run`, una sola vez, se guarda el secreto con el que se firman los JWT
(al menos 32 caracteres; sin él la API no arranca):

```powershell
dotnet user-secrets set JWT_SECRET "<un texto aleatorio de 32 caracteres o más>" --project src/DevportalApi
```

| Comando | Para qué |
|---|---|
| `dotnet run --project src/DevportalApi` | Levanta la API en `http://localhost:5080` |
| `dotnet user-secrets list --project src/DevportalApi` | Muestra los secretos locales guardados |
| `dotnet test` | Corre los tests de integración |
| `dotnet build` | Compila la solución (los warnings cuentan como errores) y regenera `openapi/portal-api.json` |
| `dotnet format` | Formatea el código según `.editorconfig` |
| `dotnet format --verify-no-changes` | Falla si hay algo sin formatear (para CI) |

Con la API levantada:

- `curl.exe http://localhost:5080/health` (en Windows PowerShell 5.1 `curl` es un alias de `Invoke-WebRequest`, por eso se usa `curl.exe`) → `{"status":"ok"}`
- `curl.exe "http://localhost:5080/apis?q=blog"` → solo la Blog API.
- `http://localhost:5080/openapi/v1.json` → especificación OpenAPI generada desde el código (la
  misma que `openapi/portal-api.json`; ver "Especificación OpenAPI").

Para llamar a un endpoint privado, primero se pide un token con el usuario demo (en PowerShell 5.1
conviene mandar el JSON desde un archivo, porque las comillas dentro de `-d` se pierden al pasar a
`curl.exe`):

```powershell
'{"username":"demo","password":"demo1234"}' | Set-Content -NoNewline token-body.json
$token = (curl.exe -s -X POST http://localhost:5080/auth/token -H "Content-Type: application/json" --data-binary "@token-body.json" | ConvertFrom-Json).accessToken
curl.exe http://localhost:5080/dashboard/stats -H "Authorization: Bearer $token"
Remove-Item token-body.json
```

Con el mismo `$token`, una suscripción de punta a punta (el `id` sale de la respuesta del `POST` y
del header `Location`):

```powershell
'{"apiId":"blog","plan":"free"}' | Set-Content -NoNewline sub-body.json
curl.exe -i -X POST http://localhost:5080/subscriptions -H "Authorization: Bearer $token" -H "Content-Type: application/json" --data-binary "@sub-body.json"
curl.exe http://localhost:5080/subscriptions -H "Authorization: Bearer $token"
curl.exe -i -X DELETE http://localhost:5080/subscriptions/<id> -H "Authorization: Bearer $token"
Remove-Item sub-body.json
```

## Endpoints

| Método y ruta | Qué hace |
|---|---|
| `GET /health` | Estado de la API |
| `GET /apis?q=&category=` | Catálogo. `q` busca en nombre, descripción y tags sin distinguir mayúsculas ni acentos (`autenticacion` encuentra "autenticación"); `category` filtra por categoría exacta sin distinguir mayúsculas. Ambos son opcionales y se combinan. |
| `GET /apis/{id}` | Una API del catálogo; 404 `not_found` si el id no existe |
| `GET /clients` | Los 8 clientes del portal, en el mismo orden |
| `POST /auth/token` | Recibe `{ "username", "password" }`. Con el usuario demo (`demo` / `demo1234`, públicos a propósito) devuelve `{ accessToken, tokenType: "Bearer", expiresIn: 3600 }`; si no, 401 `invalid_credentials` |
| `GET /dashboard/stats` | **Privado.** Las 4 métricas del dashboard del portal (valores fijos en el código) |
| `GET /dashboard/activity?status=` | **Privado.** Actividad reciente; `status` (opcional) filtra por `success`, `error` o `pending`; otro valor da 400 `validation_error` |
| `GET /subscriptions` | **Privado.** Suscripciones del usuario del token (`sub`), en orden de creación: `[{ id, apiId, plan, createdAt }]` |
| `POST /subscriptions` | **Privado.** Recibe `{ "apiId", "plan" }` (`plan` es `free` o `pro`, exacto). 201 con la suscripción y el header `Location: /subscriptions/{id}`; 400 `validation_error` si falta un campo o el plan no es válido; 422 `unknown_api` si la API no está en el catálogo; 409 `already_subscribed` si el usuario ya está suscripto a esa API |
| `GET /subscriptions/{id}` | **Privado.** Una suscripción del usuario |
| `PATCH /subscriptions/{id}` | **Privado.** Recibe `{ "plan" }` y devuelve la suscripción con el plan cambiado; 400 si el plan no es válido |
| `DELETE /subscriptions/{id}` | **Privado.** Cancela la suscripción: 204 sin cuerpo |

En `/subscriptions/{id}`, un id inexistente, uno que no es un GUID o uno que pertenece a otro
usuario dan el mismo 404 `not_found`, y la suscripción ajena no se lee, modifica ni borra: todas
las consultas filtran por `user_name = sub`.

Flujo de prueba de un endpoint privado (desde el playground del portal o con `curl.exe`, ver
"Comandos"): `POST /auth/token` con `{ "username": "demo", "password": "demo1234" }` → copiar el
`accessToken` de la respuesta → pegarlo en la sección **Autorización** del playground (o en el header
`Authorization`) → llamar, por ejemplo, a `GET /dashboard/stats` o `POST /subscriptions`.

Los endpoints privados exigen el header `Authorization: Bearer <accessToken>`. El token es un JWT
HS256 firmado con `JWT_SECRET`, con emisor `devportal-api`, audiencia `devportal-portal`, `sub` = el
usuario y vencimiento a la hora (sin tolerancia: vencido es vencido).

## Base de datos

SQLite en un archivo. Al arrancar, antes de atender requests, la API crea la carpeta de `DB_PATH`
si falta, corre `Data/schema.sql` (crea las tablas si no existen) y, si la tabla `apis` está vacía,
corre `Data/seed.sql`. Arrancar de nuevo no duplica datos. En local el archivo sobrevive a los
reinicios; para volver a los datos iniciales alcanza con borrarlo. En Render el disco no es
persistente, así que la base se vuelve a sembrar en cada reinicio o deploy (y se pierden las
suscripciones creadas); la descripción de la spec lo avisa.

Tablas: `apis`, `clients`, `activity` (la lee `GET /dashboard/activity`) y `subscriptions` (vacía
al arrancar; la llenan los endpoints de `/subscriptions`, con una fila por usuario y API gracias a
`UNIQUE (user_name, api_id)`). `id` es un GUID y `created_at` una fecha ISO 8601 en UTC, ambos
guardados como texto. Las métricas de `GET /dashboard/stats` no están en la base: son una
constante en `Endpoints/DashboardEndpoints.cs`. Los datos del seed se copiaron a mano de los mocks del portal
(`fumadocs-api-portal/src/data/`). Los tags se guardan como texto JSON en `apis.tags_json`.

## Configuración

La configuración se lee de `appsettings.json` y de variables de entorno con el mismo nombre; la
variable de entorno pisa al archivo.

| Clave | Uso | Valor por defecto (local) |
|---|---|---|
| `ALLOWED_ORIGINS` | Orígenes permitidos por CORS, separados por comas | `http://localhost:3000,http://localhost:3100,https://agustintamb.github.io` |
| `DB_PATH` | Archivo de la base SQLite; si es relativo, se resuelve contra la carpeta del proyecto (content root) | `db/portal.db` |
| `JWT_SECRET` | Clave para firmar y validar los JWT (HS256). Obligatoria y de al menos 32 bytes: si falta o es corta, la API no arranca y el error lo explica | Ninguno: en local va en user-secrets (ver "Comandos"); en Render, como variable de entorno |
| `PROD_URL` | URL del servidor "Producción" en `servers` de la spec | `https://devportal-api.onrender.com` (placeholder hasta conocer la URL final de Render) |

Con `dotnet run`, la base por defecto queda en `src/DevportalApi/db/portal.db`, carpeta ignorada
por git (`db/` en `.gitignore`).

En Render, `ALLOWED_ORIGINS` debe definirse como variable de entorno: los valores por defecto de
`appsettings.json` incluyen orígenes `localhost`.

Ningún secreto se guarda en el repo. `dotnet user-secrets` guarda `JWT_SECRET` en un archivo del
perfil del usuario (`%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`), fuera del
repo; el `<UserSecretsId>` del `.csproj` solo dice qué carpeta usar. ASP.NET lo carga solo en el
entorno `Development` (el de `dotnet run` y el de los tests).

## Formato de errores

Todas las respuestas de error usan el mismo cuerpo, con mensajes en español:

```json
{ "error": { "code": "not_found", "message": "El recurso solicitado no existe." } }
```

| Situación | Status | `code` |
|---|---|---|
| Ruta inexistente | 404 | `not_found` |
| Recurso inexistente (por ejemplo, `GET /apis/nope`); el mensaje incluye el id | 404 | `not_found` |
| Suscripción inexistente, de otro usuario o con un id que no es GUID | 404 | `not_found` |
| Suscripción a una API que no está en el catálogo | 422 | `unknown_api` |
| Suscripción repetida (mismo usuario y misma API) | 409 | `already_subscribed` |
| Parámetro inválido (por ejemplo, `GET /dashboard/activity?status=otro`), campo obligatorio faltante o `plan` distinto de `free`/`pro` en `/subscriptions`, o cuerpo JSON vacío o mal formado (en `Development` ASP.NET lo lanza como `BadHttpRequestException` y `ApiErrors.cs` lo convierte en 400) | 400 | `validation_error` |
| Usuario o contraseña incorrectos en `POST /auth/token` | 401 | `invalid_credentials` |
| Endpoint privado sin token, con otro esquema (`Basic …`), con firma inválida, de otra audiencia o vencido; además lleva el header `WWW-Authenticate: Bearer` | 401 | `unauthorized` |
| Método no permitido en una ruta existente | 405 | `method_not_allowed` |
| Excepción no controlada (sin detalles internos ni stack) | 500 | `internal_error` |
| Otro status de error sin cuerpo | 4xx/5xx | `http_error` |

## Especificación OpenAPI

La spec se genera desde el código: rutas, DTOs y metadata de cada endpoint (`WithName` →
`operationId`, `WithSummary`, `WithTags`, `ProducesError`, `[Description]` en los parámetros), más
los transformers de `Infrastructure/OpenApiDocumentation.cs` (título, descripción con el flujo de
prueba, `servers`, descripciones de los tags, enums y textos en español) y de
`Infrastructure/OpenApiSecurity.cs` (esquema `bearerAuth`; `security` y 401 en los endpoints
privados). La API la sirve en `/openapi/v1.json`, y `dotnet build` la escribe en
`openapi/portal-api.json`, que se versiona y es la que copia el portal.

- **Generación en el build**: el paquete `Microsoft.Extensions.ApiDescription.Server` agrega un paso
  al build que arranca la API con la herramienta `GetDocument.Insider` y guarda el documento. La
  carpeta y el nombre del archivo salen de `OpenApiDocumentsDirectory` y
  `OpenApiGenerateDocumentsOptions` en el `.csproj`.
- **Sin secreto ni base en el build**: la herramienta ejecuta `Program.cs` solo hasta
  `builder.Build()`, así que la base no se crea. `JWT_SECRET` sí se lee antes, por eso `Program.cs`
  detecta la herramienta (`Assembly.GetEntryAssembly()` se llama `GetDocument.Insider`) y usa un
  secreto aleatorio descartable. El build funciona sin user-secrets y no deja archivos fuera de
  `openapi/`.
- **Mantenerla al día**: después de cambiar un endpoint o un DTO, `dotnet build` regenera el archivo
  y el cambio se commitea junto con el código. El test `CommittedSpecFile_MatchesServedDocument`
  compara el archivo con el documento servido. Como `dotnet test` también compila (y regenera),
  para detectar en CI un archivo sin commitear conviene correr
  `git diff --exit-code openapi/portal-api.json` después del build.
- **`servers`**: "Producción" usa `PROD_URL` (ver "Configuración") y "Local" es
  `http://localhost:5080`. Si se define `PROD_URL` durante el build, la URL también cambia en el
  archivo generado.
- **Saltos de línea**: `.gitattributes` fija `eol=lf` para `openapi/*.json`, así el archivo es igual
  en Windows y en Linux y git no lo marca como modificado.

## Cómo está organizado el repo

```text
devportal-api/
├── DevportalApi.sln                  # Solución: agrupa los proyectos (como un workspace de npm)
├── global.json                       # Fija la versión del SDK de .NET (como "engines" o .nvmrc)
├── .editorconfig                     # Reglas de estilo que aplican el IDE y `dotnet format`
├── .gitattributes                    # Normaliza los saltos de línea (LF en el repo; siempre LF en openapi/*.json)
├── .gitignore                        # Incluye bin/, obj/ y db/ (base SQLite local)
├── openapi/
│   └── portal-api.json               # Spec OpenAPI generada por `dotnet build` (se versiona; la copia el portal)
├── src/
│   └── DevportalApi/                 # El proyecto web (la API)
│       ├── DevportalApi.csproj       # Proyecto: framework, opciones del compilador, paquetes NuGet, recursos embebidos, UserSecretsId y generación de la spec
│       ├── Program.cs                # Punto de entrada: registra servicios (CORS, OpenAPI, JWT), inicializa la base, arma el pipeline y mapea endpoints
│       ├── Contracts.cs              # DTOs (records) de entrada y salida: ApiDto, ClientDto, TokenRequest, TokenResponse, StatDto, ActivityDto, SubscriptionDto y los cuerpos de /subscriptions
│       ├── appsettings.json          # Configuración por defecto (se puede pisar con variables de entorno)
│       ├── Properties/
│       │   └── launchSettings.json   # Perfil de `dotnet run` en local: puerto 5080 y entorno Development
│       ├── Data/
│       │   ├── Database.cs           # Abre conexiones SQLite y crea/siembra la base al arrancar
│       │   ├── schema.sql            # CREATE TABLE IF NOT EXISTS de las 4 tablas (recurso embebido)
│       │   └── seed.sql              # Datos iniciales copiados de los mocks del portal (recurso embebido)
│       ├── Endpoints/
│       │   ├── HealthEndpoints.cs    # Un archivo por recurso, con sus rutas agrupadas con MapGroup
│       │   ├── ApisEndpoints.cs      # GET /apis (búsqueda y filtro) y GET /apis/{id}
│       │   ├── ClientsEndpoints.cs   # GET /clients
│       │   ├── AuthEndpoints.cs      # POST /auth/token (usuario demo → JWT)
│       │   ├── DashboardEndpoints.cs # GET /dashboard/stats y /dashboard/activity (grupo con RequireAuthorization)
│       │   └── SubscriptionsEndpoints.cs # CRUD privado de /subscriptions, filtrado siempre por el usuario del token
│       └── Infrastructure/
│           ├── ApiErrors.cs          # Formato único de errores, su manejo global y `ProducesError` (documenta un error en la spec)
│           ├── Tokens.cs             # Valida JWT_SECRET al arrancar y emite los JWT (emisor, audiencia, 1 h)
│           ├── OpenApiDocumentation.cs # Transformers de OpenAPI: info, servers, descripciones de tags, enums y textos en español
│           └── OpenApiSecurity.cs    # Transformers de OpenAPI: esquema bearerAuth y security + 401 en los endpoints privados
└── tests/
    └── DevportalApi.Tests/           # Proyecto de tests (xUnit)
        ├── DevportalApi.Tests.csproj
        ├── ApiFactory.cs             # Levanta la API en memoria con configuración de test y una base temporal propia
        ├── TestTokens.cs             # Crea y valida JWT de prueba con el secreto de test (vencidos, de otra audiencia, mal firmados o de otro usuario)
        ├── HealthTests.cs            # GET /health y documento OpenAPI
        ├── ErrorTests.cs             # 404, 405 y 500 con el formato de error
        ├── CorsTests.cs              # Preflight (también a un endpoint privado) y header CORS en respuestas 404 y 401
        ├── DatabaseTests.cs          # Creación y seed de la base, sin duplicar al arrancar dos veces
        ├── ApisTests.cs              # Catálogo: listado, búsqueda, filtro por categoría y 404
        ├── ClientsTests.cs           # Clientes en el orden del portal
        ├── AuthTests.cs              # Token con credenciales demo, credenciales malas, cuerpo mal formado y JWT_SECRET faltante o corto
        ├── DashboardTests.cs         # 401 en cada caso de token inválido, stats, filtro de actividad, 400 y security en OpenAPI
        ├── SubscriptionsTests.cs     # CRUD de suscripciones, 400/409/422, 404 con id ajeno o inválido y 401 sin token
        └── OpenApiTests.cs           # Contenido de la spec (operationIds, security, enums, servers, tags) y archivo versionado igual al servido
```

Recursos embebidos: `schema.sql` y `seed.sql` se compilan dentro de la DLL (`<EmbeddedResource>` en
el `.csproj`), así la API no depende de que esos archivos existan en disco al desplegarse.
`Database.cs` los lee con `GetManifestResourceStream("schema.sql")`.

Carpetas generadas (no se versionan): `bin/` contiene lo compilado (como `dist/`) y `obj/` los
archivos intermedios del build y de la restauración de paquetes (parecido a una caché de build).
`openapi/` también la genera el build, pero se versiona a propósito (ver "Especificación OpenAPI").

### Recorrido de un request

Antes de atender el primer request, `Program.cs` llama a `await database.InitializeAsync()`, que
crea las tablas y siembra la base si está vacía.

Un request pasa por una cadena de middlewares (como en Express) antes de llegar al endpoint:

1. **Ruteo**: ASP.NET decide qué endpoint corresponde a la URL y al método. No tiene una línea en
   `Program.cs`: `WebApplication` agrega implícitamente los middlewares de ruteo y de ejecución de
   endpoints.
2. **Manejo de errores** (`app.UseApiErrors()` en `Program.cs`): si algo más adelante lanza una
   excepción, responde 500 `internal_error`; si la respuesta termina en 4xx/5xx sin cuerpo (por
   ejemplo, ninguna ruta coincidió), escribe el cuerpo de error con el formato único.
3. **CORS** (`app.UseCors()`): responde los preflight `OPTIONS` y agrega los headers
   `Access-Control-*` solo para los orígenes de `ALLOWED_ORIGINS`.
4. **Autenticación** (`app.UseAuthentication()`): responde "¿quién sos?". Si viene
   `Authorization: Bearer <token>`, JwtBearer valida firma, emisor, audiencia y vencimiento según
   los `TokenValidationParameters` de `Program.cs`, y deja el usuario en `HttpContext.User`. No
   rechaza nada: un request sin token sigue como anónimo.
5. **Autorización** (`app.UseAuthorization()`): responde "¿puede pasar?". Si el endpoint pide
   autorización (`RequireAuthorization()` en los grupos `/dashboard` y `/subscriptions`) y el usuario es anónimo, corta
   con un *challenge*: el evento `OnChallenge` de `Program.cs` escribe el 401 `unauthorized` con el
   formato único y el header `WWW-Authenticate: Bearer`.
   Estos dos middlewares van explícitos y después de `UseCors()`: si no se llaman, ASP.NET los
   agrega solo, pero **antes** de CORS, y los 401 salen sin `Access-Control-Allow-Origin` (el
   navegador no dejaría al portal leer el error). Lo cubre un test de `CorsTests.cs`.
6. **Endpoint**: ASP.NET llama a la función del endpoint y le pasa sus parámetros: los de la ruta
   (`{id}`), los de la query string (`q`, `category`), el cuerpo JSON (`TokenRequest`), los
   servicios registrados (`Database`, `Tokens`) y el usuario autenticado (`ClaimsPrincipal`).
   Si la ruta tiene una restricción como `{id:guid}` y el valor no la cumple, la ruta no coincide
   y el request termina en el 404 global del paso 2.
   Por ejemplo, la de `GET /apis` en `Endpoints/ApisEndpoints.cs`:
   1. **Dapper → SQLite**: abre una conexión, ejecuta el `SELECT` escrito a mano y Dapper convierte
      cada fila en un objeto C#.
   2. **Filtro en memoria**: la búsqueda `q` se resuelve en C# con LINQ sobre las filas leídas.
   3. **Respuesta**: devuelve un resultado (`TypedResults.Ok(...)` o un error con
      `ApiErrors.Problem(...)`) que ASP.NET serializa a JSON en camelCase.

## C# y .NET para quien viene de TypeScript

| .NET | Equivalente en TypeScript | Dónde se ve |
|---|---|---|
| `.csproj` + paquetes NuGet (`dotnet add package`) | `package.json` + npm (`npm install`) | `src/DevportalApi/DevportalApi.csproj` |
| `dotnet` CLI (`run`, `test`, `build`, `format`) | Scripts de npm (`npm run dev`, `npm test`, …) | Sección "Comandos" |
| `Program.cs` con top-level statements | El `index.ts` de un server Express | `src/DevportalApi/Program.cs` |
| `namespace` + `using` | Los módulos y los `import`, pero sin relación con archivos: `using` importa un namespace (no un archivo) y la visibilidad se define con `public`/`internal`, no con `export` | `using DevportalApi.Endpoints;` en `Program.cs` |
| Primary constructor `class CorsTests(ApiFactory factory)` | Constructor con parameter properties (`constructor(private factory: ApiFactory)`) | `CorsTests.cs`, `HealthTests.cs`, `ErrorTests.cs` |
| Collection expression `[]` | Un array literal `[]` | `?? []` en `Program.cs` |
| `record` | Un `type` inmutable para datos | `HealthResponse`, `ErrorResponse`, `ApiDto` y `ClientDto` en `Contracts.cs` |
| Inyección de dependencias (`builder.Services.Add…`) | Registrar dependencias en un contenedor en vez de importarlas a mano | `AddCors`, `AddOpenApi`, `AddSingleton(database)` en `Program.cs` |
| Parámetros de un handler (`string? q`, `string id`, `Database database`) | `req.query.q`, `req.params.id` y un objeto importado, pero declarados como parámetros | `ListApisAsync` y `GetApiAsync` en `ApisEndpoints.cs` |
| Dapper (`QueryAsync<T>`, `ExecuteAsync`) | Un cliente SQL liviano (`pg`, `better-sqlite3`) que además mapea filas a objetos | `Endpoints/ApisEndpoints.cs`, `Data/Database.cs` |
| `SqliteConnection` (Microsoft.Data.Sqlite) | La conexión de `better-sqlite3` | `Database.OpenConnectionAsync()` |
| `using var` / `await using var` (dentro de un método; distinto del `using` de arriba del archivo) | `try { … } finally { conn.close() }` | `await using var connection = …` en los endpoints y en `Database.cs` |
| LINQ (`Select`, `Where`, `Any`, `ToList`) | `map`, `filter`, `some` y `[...iter]` | Búsqueda `q` en `ApisEndpoints.cs` |
| `CompareInfo.IndexOf(…, IgnoreCase \| IgnoreNonSpace)` | `a.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase().includes(…)` o `Intl.Collator` con `sensitivity: 'base'` | `ContainsText` en `ApisEndpoints.cs` |
| Recursos embebidos (`<EmbeddedResource>`) | Importar un archivo para que el bundler lo meta en el build (`import sql from './schema.sql?raw'`) | `DevportalApi.csproj`, `ReadSql` en `Database.cs` |
| Raw string literal `"""…"""` | Un template literal multilínea | `SelectApis` en `ApisEndpoints.cs` |
| Middlewares (`app.Use…`) | `app.use(...)` de Express | `UseApiErrors`, `UseCors` |
| Extension methods | Agregar "métodos" a un tipo existente sin modificarlo | `app.UseApiErrors()`, `app.MapHealthEndpoints()` |
| `Task` + `async`/`await` | `Promise` + `async`/`await` | Los tests (`async Task`) |
| Nullable (`<Nullable>enable</Nullable>`, `string?`, `?.`, `??`) | `strictNullChecks`, `?.`, `??` | Lectura de `ALLOWED_ORIGINS` en `Program.cs` |
| Lambdas `x => …` | Arrow functions | Endpoints y configuración |
| `switch` expression | Un objeto mapa o un `switch` que devuelve valor | `FromStatusCode` en `ApiErrors.cs` |
| `appsettings.json` + variables de entorno | `.env` + `process.env` | `ALLOWED_ORIGINS` |
| `dotnet user-secrets` | Un `.env.local` que nunca se commitea, pero guardado fuera del repo | `JWT_SECRET` (ver "Comandos" y "Configuración") |
| Autenticación + autorización (`UseAuthentication`, `UseAuthorization`) | Un middleware tipo `passport.authenticate('jwt')` que llena `req.user`, más un guard que corta con 401 | `Program.cs` |
| `AddJwtBearer` + `TokenValidationParameters` | `jwt.verify(token, secret, { issuer, audience, algorithms })` de `jsonwebtoken` | `Program.cs` |
| `RequireAuthorization()` en un `MapGroup` | Montar un router de Express detrás del middleware de auth (`router.use(requireAuth)`) | `DashboardEndpoints.cs` |
| `ClaimsPrincipal` / claims | El payload decodificado del JWT (`{ sub, aud, exp, … }`) | `HttpContext.User`; `sub` se crea en `Tokens.cs` |
| Validación explícita de un parámetro | Un `if (!['success', 'error', 'pending'].includes(status))` que responde 400 | `ListActivityAsync` en `DashboardEndpoints.cs` |
| Transformers de OpenAPI (documento, operación y schema) | Un plugin que post-procesa el objeto de la spec antes de servirla | `Infrastructure/OpenApiSecurity.cs`, `Infrastructure/OpenApiDocumentation.cs` |
| Generación de la spec en el build (`Microsoft.Extensions.ApiDescription.Server`) | Un paso de codegen en un script de build (`"prebuild": "generate-openapi"`) cuyo resultado se commitea | `DevportalApi.csproj`, `openapi/portal-api.json` |
| Atributo `[Description("…")]` en un parámetro | Un decorador o un comentario JSDoc que una herramienta lee para documentar | `ApisEndpoints.cs`, `DashboardEndpoints.cs`, `SubscriptionsEndpoints.cs` |
| `.gitattributes` (`eol=lf`) | La opción `endOfLine: "lf"` de Prettier, pero aplicada por git al hacer checkout y commit | `.gitattributes` |
| Restricción de ruta `{id:guid}` + parámetro `Guid id` | `app.get('/subscriptions/:id', …)` más validar a mano que `req.params.id` sea un UUID | `SubscriptionsEndpoints.cs` |
| Parámetro `ClaimsPrincipal user` en un handler | Leer `req.user` (el payload del JWT) dentro del handler | `UserName` en `SubscriptionsEndpoints.cs` |
| `Guid.NewGuid()` / tipo `Guid` | `crypto.randomUUID()` (en JSON viaja como string) | `CreateSubscriptionAsync` |
| `DateTimeOffset.UtcNow` y `ToString("O")` | `new Date().toISOString()` (fecha con zona, en ISO 8601) | `CreateSubscriptionAsync`, `ToDto` |
| Pattern matching `plan is not ("free" or "pro")` | Chequear un union de literales: `if (plan !== 'free' && plan !== 'pro')` para `'free' \| 'pro'` | `CreateSubscriptionAsync`, `UpdateSubscriptionAsync` |
| `TypedResults.Created(url, body)` / `TypedResults.NoContent()` | `res.status(201).location(url).json(body)` / `res.status(204).end()` | `SubscriptionsEndpoints.cs` |
| xUnit + `WebApplicationFactory` | Vitest + supertest | `tests/DevportalApi.Tests` |

Notas:

- **Namespaces y `using`**: `HealthEndpoints.cs` y `ApiErrors.cs` no tienen `using` porque
  `<ImplicitUsings>enable</ImplicitUsings>` en el `.csproj` agrega automáticamente `using`
  globales para los namespaces más comunes (`System`, `System.Linq`, `Microsoft.AspNetCore.Builder`,
  etc.). Solo hacen falta `using` explícitos para el resto, como los de `Program.cs`.
- **Primary constructors**: en `public class CorsTests(ApiFactory factory)` el parámetro `factory`
  está disponible en todo el cuerpo de la clase (por ejemplo, para inicializar `_client`); no hace
  falta declarar un campo ni asignarlo en un constructor aparte.
- **Collection expressions**: `?? []` en `Program.cs` es un array vacío; el compilador infiere el
  tipo (`string[]`) por el contexto.
- **Records**: `public sealed record HealthResponse(string Status);` define en una línea un tipo con
  la propiedad `Status`, igualdad por valor y sin herencia (`sealed`). Al serializarse sale
  `{"status":"ok"}`.
- **Extension methods**: `MapHealthEndpoints(this IEndpointRouteBuilder app)` es un método
  `static` cuyo primer parámetro lleva `this`; por eso se puede llamar como
  `app.MapHealthEndpoints()`. Así `Program.cs` queda corto y cada recurso vive en su archivo.
- **`Program` en los tests**: `WebApplicationFactory<Program>` necesita la clase `Program`, que el
  compilador genera a partir de las top-level statements; en .NET 10 se genera pública, así que no
  hace falta declararla.
- **Tests de integración**: `ApiFactory` levanta la API real en memoria (sin abrir un puerto) y le
  pasa configuración de test (`ALLOWED_ORIGINS`, una base temporal en `DB_PATH` y un `JWT_SECRET`
  de prueba, con el que `TestTokens.cs` también fabrica tokens vencidos o de otra audiencia). Cada clase de tests la recibe con
  `IClassFixture<ApiFactory>`, parecido a un `beforeAll` compartido, así que cada clase tiene su
  propia base. Al terminar, `ApiFactory` llama a `SqliteConnection.ClearAllPools()` antes de borrar
  el archivo: Microsoft.Data.Sqlite reutiliza conexiones (pool) y en Windows una conexión abierta
  bloquea el archivo.
- **DTOs**: un DTO (*data transfer object*) es la forma pública de la respuesta. `ApiDto` expone
  `Tags` como lista aunque en la tabla sea texto JSON (`tags_json`); `ApiRow`, privado en
  `ApisEndpoints.cs`, es la forma de la fila tal como sale de SQLite, y `ToDto` convierte una en otra.
- **Columnas snake_case → propiedades PascalCase**: Dapper llena un record llamando a su constructor
  con las columnas en el mismo orden y nombre (sin distinguir mayúsculas). Como las columnas son
  `snake_case`, cada `SELECT` usa alias (`tags_json AS TagsJson`). Se eligió esto en vez de la
  opción global `DefaultTypeMap.MatchNamesWithUnderscores = true` porque el mapeo queda a la vista
  en el mismo SQL.
- **Parámetros de SQL**: `new { category }` le pasa a Dapper los valores de `@category` como
  parámetros; nunca se concatenan en el texto del SQL (evita inyección SQL, como los `$1` de `pg`).
- **`using` / `await using`**: `await using var connection = …` cierra la conexión al salir del
  método, aunque haya una excepción; equivale a un `try/finally` que llama a `close()`. Se usa
  `await using` cuando el cierre es asíncrono (conexiones, transacciones) y `using` cuando no
  (`StreamReader` en `ReadSql`).
- **Búsqueda sin acentos**: `CompareOptions.IgnoreNonSpace` ignora las marcas diacríticas
  (tildes, diéresis) al comparar y `IgnoreCase` ignora mayúsculas. Depende de los datos de
  globalización (ICU) del sistema: si algún día se despliega con
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` o `<InvariantGlobalization>true</InvariantGlobalization>`
  en el proyecto, o con imágenes sin ICU (Alpine, Ubuntu chiseled), la comparación deja de ignorar
  acentos. El Dockerfile debe usar una imagen `aspnet` completa (no chiseled ni Alpine).
- **Filtros opcionales**: `string? q` vale `null` si el parámetro no viene. Los endpoints tratan
  igual `null`, vacío y espacios (`string.IsNullOrWhiteSpace`), así `?q=` devuelve todo.
- **JWT de punta a punta**: `Tokens.cs` arma el token con `JsonWebTokenHandler.CreateToken` (claims
  `iss`, `aud`, `sub`, `exp`) y lo firma con HS256 usando `JWT_SECRET`; JwtBearer lo valida en cada
  request con los `TokenValidationParameters` de `Program.cs`. `ValidAlgorithms` acepta solo HS256
  y `ClockSkew = TimeSpan.Zero` saca los 5 minutos de tolerancia que JwtBearer da por defecto.
  `MapInboundClaims = false` deja los claims con su nombre del JWT (`sub`); sin eso, .NET los
  renombra a URIs largas (`http://schemas.xmlsoap.org/.../nameidentifier`).
- **Claims**: dentro de un endpoint privado, `HttpContext.User` (un `ClaimsPrincipal`) es el
  payload validado del token; `user.FindFirst("sub")?.Value` sería el `payload.sub` de TypeScript.
- **401 con el formato único**: JwtBearer, por defecto, responde 401 sin cuerpo y solo con
  `WWW-Authenticate`. `OnChallenge` llama a `context.HandleResponse()` (le dice a JwtBearer "ya
  respondo yo"), vuelve a poner `WWW-Authenticate: Bearer` y escribe el cuerpo con `ApiErrors.Problem`.
- **Fallar al arrancar**: `new Tokens(builder.Configuration["JWT_SECRET"])` se ejecuta antes de
  `builder.Build()`; si el secreto falta o tiene menos de 32 bytes lanza una excepción y la API no
  levanta, en vez de firmar tokens con una clave vacía o débil. La única excepción es el generador de
  la spec del build (ver "Especificación OpenAPI"), que nunca atiende requests.
- **`status` como texto**: se valida con un `if` y no con un `enum` de C#, porque un `enum` mal
  enviado lo rechaza ASP.NET antes de llegar al endpoint, y esa respuesta no sería el 400
  `validation_error` del formato único.
- **Transformers de OpenAPI**: `AddDocumentTransformer` corre una vez sobre todo el documento
  (registra `components.securitySchemes.bearerAuth`; pone info, `servers` y descripciones de tags) y
  `AddOperationTransformer` corre por cada endpoint: si su metadata tiene `IAuthorizeData` (lo
  agrega `RequireAuthorization()`), le suma `security: [{ bearerAuth: [] }]` y la respuesta 401 con
  `$ref` a `ErrorResponse`. `AddSchemaTransformer` corre por cada schema (también por cada propiedad
  y cada parámetro) y agrega los `enum` de `plan` y `status`: como se validan con un `if` (no son
  `enum` de C#), ASP.NET no puede deducirlos solo.
- **Documentar los errores**: `ProducesError(404, "…")` (extension method en `ApiErrors.cs`) agrega
  a la metadata del endpoint una respuesta con status, descripción en español y schema
  `ErrorResponse`; la spec la toma de ahí, igual que `WithSummary` o `WithTags`.
- **Números estrictos**: por defecto el JSON de ASP.NET acepta números enviados como texto (`"3600"`)
  y la spec los declara `integer | string`. `JsonNumberHandling.Strict` en `Program.cs` deja solo
  números, en la API y en la spec.
- **`ReplaceLineEndings("\n")`**: un raw string literal toma los saltos de línea del archivo `.cs`
  (CRLF en un checkout de Windows); se normalizan para que la descripción de la spec no cambie según
  el sistema.
- **Cada usuario ve solo lo suyo**: `SubscriptionsEndpoints.cs` toma el usuario del claim `sub`
  (`UserName(user)`) y todas las consultas llevan `WHERE … user_name = @userName`. Una suscripción
  de otro usuario no se distingue de una inexistente: el `UPDATE`/`DELETE` afecta 0 filas y la
  respuesta es 404, sin revelar que el id existe.
- **409 sin carrera**: en vez de hacer un `SELECT` previo (otro request podría insertar en el medio)
  o atrapar la `SqliteException` del `UNIQUE`, el `INSERT` usa `ON CONFLICT (user_name, api_id) DO
  NOTHING`: si ya existe, SQLite no inserta nada y `ExecuteAsync` devuelve 0 filas afectadas → 409.
  `PATCH` usa `UPDATE … RETURNING …`, que actualiza y devuelve la fila en una sola consulta.
- **Cuerpos con campos opcionales a propósito**: `CreateSubscriptionRequest(string? ApiId, string? Plan)`
  usa `string?` para que un campo faltante llegue como `null` al endpoint y responda el 400
  `validation_error` con un mensaje claro. El `plan` se valida con un `if` (no con un `enum`) por
  el mismo motivo que `status` en el dashboard.
- **GUID y fecha como texto**: SQLite no tiene tipos `uuid` ni `timestamp`; `id` se guarda con
  `Guid.ToString()` (minúsculas con guiones) y `created_at` con `ToString("O")` (ISO 8601 con
  `+00:00`). `ToDto` los vuelve a convertir a `Guid` y `DateTimeOffset`, así el JSON y la spec los
  declaran como `uuid` y `date-time`.
