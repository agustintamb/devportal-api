# devportal-api

**Portal API**: API de prueba en ASP.NET Core (.NET 10, minimal APIs) para probar el playground
del portal de desarrolladores hecho con Fumadocs. Expone recursos del mismo dominio que el portal
(catálogo de APIs, suscripciones, dashboard) y sirve su propia especificación OpenAPI.

Estado actual: salud (`GET /health`), CORS, formato único de errores y documento OpenAPI.

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download) (`global.json` exige como mínimo la 10.0.403 y acepta
  versiones posteriores, por `rollForward: latestFeature`). Se verifica con `dotnet --list-sdks`.

## Comandos

| Comando | Para qué |
|---|---|
| `dotnet run --project src/DevportalApi` | Levanta la API en `http://localhost:5080` |
| `dotnet test` | Corre los tests de integración |
| `dotnet build` | Compila la solución (los warnings cuentan como errores) |
| `dotnet format` | Formatea el código según `.editorconfig` |
| `dotnet format --verify-no-changes` | Falla si hay algo sin formatear (para CI) |

Con la API levantada:

- `curl.exe http://localhost:5080/health` (en Windows PowerShell 5.1 `curl` es un alias de `Invoke-WebRequest`, por eso se usa `curl.exe`) → `{"status":"ok"}`
- `http://localhost:5080/openapi/v1.json` → especificación OpenAPI generada desde el código.

## Configuración

La configuración se lee de `appsettings.json` y de variables de entorno con el mismo nombre; la
variable de entorno pisa al archivo.

| Clave | Uso | Valor por defecto (local) |
|---|---|---|
| `ALLOWED_ORIGINS` | Orígenes permitidos por CORS, separados por comas | `http://localhost:3000,http://localhost:3100,https://agustintamb.github.io` |

En Render, `ALLOWED_ORIGINS` debe definirse como variable de entorno: los valores por defecto de
`appsettings.json` incluyen orígenes `localhost`.

Ningún secreto se guarda en el repo.

## Formato de errores

Todas las respuestas de error usan el mismo cuerpo, con mensajes en español:

```json
{ "error": { "code": "not_found", "message": "El recurso solicitado no existe." } }
```

| Situación | Status | `code` |
|---|---|---|
| Ruta inexistente | 404 | `not_found` |
| Método no permitido en una ruta existente | 405 | `method_not_allowed` |
| Excepción no controlada (sin detalles internos ni stack) | 500 | `internal_error` |
| Otro status de error sin cuerpo | 4xx/5xx | `http_error` |

## Cómo está organizado el repo

```text
devportal-api/
├── DevportalApi.sln                  # Solución: agrupa los proyectos (como un workspace de npm)
├── global.json                       # Fija la versión del SDK de .NET (como "engines" o .nvmrc)
├── .editorconfig                     # Reglas de estilo que aplican el IDE y `dotnet format`
├── .gitignore                        # Incluye bin/, obj/ y data/ (base SQLite local)
├── src/
│   └── DevportalApi/                 # El proyecto web (la API)
│       ├── DevportalApi.csproj       # Proyecto: framework, opciones del compilador y paquetes NuGet
│       ├── Program.cs                # Punto de entrada: registra servicios, arma el pipeline y mapea endpoints
│       ├── appsettings.json          # Configuración por defecto (se puede pisar con variables de entorno)
│       ├── Properties/
│       │   └── launchSettings.json   # Perfil de `dotnet run` en local: puerto 5080 y entorno Development
│       ├── Endpoints/
│       │   └── HealthEndpoints.cs    # Un archivo por recurso, con sus rutas agrupadas con MapGroup
│       └── Infrastructure/
│           └── ApiErrors.cs          # Formato único de errores y su manejo global
└── tests/
    └── DevportalApi.Tests/           # Proyecto de tests (xUnit)
        ├── DevportalApi.Tests.csproj
        ├── ApiFactory.cs             # Levanta la API en memoria con configuración de test
        ├── HealthTests.cs            # GET /health y documento OpenAPI
        ├── ErrorTests.cs             # 404, 405 y 500 con el formato de error
        └── CorsTests.cs              # Preflight desde orígenes permitidos y no permitidos
```

Carpetas generadas (no se versionan): `bin/` contiene lo compilado (como `dist/`) y `obj/` los
archivos intermedios del build y de la restauración de paquetes (parecido a una caché de build).

### Recorrido de un request

Un request pasa por una cadena de middlewares (como en Express) antes de llegar al endpoint:

1. **Ruteo**: ASP.NET decide qué endpoint corresponde a la URL y al método. No tiene una línea en
   `Program.cs`: `WebApplication` agrega implícitamente los middlewares de ruteo y de ejecución de
   endpoints.
2. **Manejo de errores** (`app.UseApiErrors()` en `Program.cs`): si algo más adelante lanza una
   excepción, responde 500 `internal_error`; si la respuesta termina en 4xx/5xx sin cuerpo (por
   ejemplo, ninguna ruta coincidió), escribe el cuerpo de error con el formato único.
3. **CORS** (`app.UseCors()`): responde los preflight `OPTIONS` y agrega los headers
   `Access-Control-*` solo para los orígenes de `ALLOWED_ORIGINS`.
4. **Endpoint**: la función del endpoint (por ejemplo, la de `GET /health` en
   `Endpoints/HealthEndpoints.cs`) devuelve un resultado que ASP.NET serializa a JSON en camelCase.

## C# y .NET para quien viene de TypeScript

| .NET | Equivalente en TypeScript | Dónde se ve |
|---|---|---|
| `.csproj` + paquetes NuGet (`dotnet add package`) | `package.json` + npm (`npm install`) | `src/DevportalApi/DevportalApi.csproj` |
| `dotnet` CLI (`run`, `test`, `build`, `format`) | Scripts de npm (`npm run dev`, `npm test`, …) | Sección "Comandos" |
| `Program.cs` con top-level statements | El `index.ts` de un server Express | `src/DevportalApi/Program.cs` |
| `namespace` + `using` | Los módulos y los `import`, pero sin relación con archivos: `using` importa un namespace (no un archivo) y la visibilidad se define con `public`/`internal`, no con `export` | `using DevportalApi.Endpoints;` en `Program.cs` |
| Primary constructor `class CorsTests(ApiFactory factory)` | Constructor con parameter properties (`constructor(private factory: ApiFactory)`) | `CorsTests.cs`, `HealthTests.cs`, `ErrorTests.cs` |
| Collection expression `[]` | Un array literal `[]` | `?? []` en `Program.cs` |
| `record` | Un `type` inmutable para datos | `HealthResponse`, `ErrorResponse` |
| Inyección de dependencias (`builder.Services.Add…`) | Registrar dependencias en un contenedor en vez de importarlas a mano | `AddCors`, `AddOpenApi` en `Program.cs` |
| Middlewares (`app.Use…`) | `app.use(...)` de Express | `UseApiErrors`, `UseCors` |
| Extension methods | Agregar "métodos" a un tipo existente sin modificarlo | `app.UseApiErrors()`, `app.MapHealthEndpoints()` |
| `Task` + `async`/`await` | `Promise` + `async`/`await` | Los tests (`async Task`) |
| Nullable (`<Nullable>enable</Nullable>`, `string?`, `?.`, `??`) | `strictNullChecks`, `?.`, `??` | Lectura de `ALLOWED_ORIGINS` en `Program.cs` |
| Lambdas `x => …` | Arrow functions | Endpoints y configuración |
| `switch` expression | Un objeto mapa o un `switch` que devuelve valor | `FromStatusCode` en `ApiErrors.cs` |
| `appsettings.json` + variables de entorno | `.env` + `process.env` | `ALLOWED_ORIGINS` |
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
  de prueba, que usarán las próximas funcionalidades). Cada clase de tests la recibe con
  `IClassFixture<ApiFactory>`, parecido a un `beforeAll` compartido.
