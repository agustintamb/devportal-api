-- Copied by hand from the portal mocks (fumadocs-api-portal/src/data). Runs only when apis is empty.

INSERT INTO apis (id, name, description, status, category, tags_json, version) VALUES
  ('petstore', 'Petstore API',
   'El clásico ejemplo Swagger Petstore. Operaciones CRUD de mascotas, inventario de la tienda y gestión de usuarios.',
   'stable', 'Ejemplo', '["pets","store","users"]', 'v1.0.19'),
  ('blog', 'Blog API',
   'Gestión de posts, comentarios y perfiles de autores. Permite paginar y filtrar por tag o autor. Versionada: v1 y v2 disponibles.',
   'stable', 'Contenido', '["posts","comments","authors"]', 'v1 / v2'),
  ('auth', 'Auth API',
   'Servicio de autenticación con JWT. Registro, login, renovación de token, recuperación de contraseña y gestión del perfil.',
   'stable', 'Seguridad', '["jwt","oauth","users"]', 'v2.0.0');

INSERT INTO clients (slug, name, position) VALUES
  ('google', 'Google', 1),
  ('spotify', 'Spotify', 2),
  ('netflix', 'Netflix', 3),
  ('airbnb', 'Airbnb', 4),
  ('uber', 'Uber', 5),
  ('stripe', 'Stripe', 6),
  ('shopify', 'Shopify', 7),
  ('github', 'GitHub', 8);

INSERT INTO activity (id, action, api, time, status) VALUES
  (1, 'GET /api/pets', 'Petstore', 'hace 2 min', 'success'),
  (2, 'POST /auth/login', 'Auth API', 'hace 5 min', 'success'),
  (3, 'GET /blog-api/posts', 'Blog API', 'hace 12 min', 'success'),
  (4, 'DELETE /api/pet/5', 'Petstore', 'hace 1 h', 'error'),
  (5, 'POST /blog-api/posts', 'Blog API', 'hace 2 h', 'pending');
