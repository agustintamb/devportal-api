-- Same catalog as the portal (fumadocs-api-portal/src/data/apis.ts), in the same order. Runs only when apis is empty.

INSERT INTO apis (id, name, description, status, visibility, category, tags_json, version) VALUES
  ('portal', 'Portal API',
   'API propia del portal: catálogo de APIs, clientes, métricas del dashboard y suscripciones. Emite tokens JWT con un usuario demo para probar los endpoints privados.',
   'beta', 'internal', 'Plataforma', '["dashboard","subscriptions","jwt"]', 'v1 / v2'),
  ('petstore', 'Petstore API',
   'El clásico ejemplo de Swagger contra su servidor público: mascotas, tienda y usuarios, con escritura real (POST, PUT, DELETE) y sin credenciales. Versiones v2 y v3.',
   'stable', 'public', 'Ejemplo', '["pets","store","users"]', 'v2 / v3'),
  ('pokeapi', 'PokeAPI',
   'Datos de Pokémon, movimientos, ítems y juegos desde la API pública PokeAPI. Solo lectura y sin credenciales.',
   'stable', 'public', 'Datos abiertos', '["pokemon","juegos","items"]', 'v2.10.0');

INSERT INTO clients (slug, name, position) VALUES
  ('google', 'Google', 1),
  ('spotify', 'Spotify', 2),
  ('netflix', 'Netflix', 3),
  ('airbnb', 'Airbnb', 4),
  ('uber', 'Uber', 5),
  ('stripe', 'Stripe', 6),
  ('shopify', 'Shopify', 7),
  ('github', 'GitHub', 8);

-- The dashboard stays hardcoded (as in the portal's src/data/dashboard.ts), naming the catalog APIs.
INSERT INTO activity (id, action, api, time, status) VALUES
  (1, 'GET /api/v2/pokemon/pikachu', 'PokeAPI', 'hace 2 min', 'success'),
  (2, 'POST /auth/token', 'Portal API', 'hace 5 min', 'success'),
  (3, 'POST /pet', 'Petstore', 'hace 12 min', 'success'),
  (4, 'DELETE /pet/5', 'Petstore', 'hace 1 h', 'error'),
  (5, 'GET /v2/apis', 'Portal API', 'hace 2 h', 'pending');
