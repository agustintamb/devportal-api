-- Runs on every startup: IF NOT EXISTS keeps it idempotent.

CREATE TABLE IF NOT EXISTS apis (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  description TEXT NOT NULL,
  status TEXT NOT NULL,
  visibility TEXT NOT NULL,
  category TEXT NOT NULL,
  tags_json TEXT NOT NULL,
  version TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS clients (
  slug TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  position INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS activity (
  id INTEGER PRIMARY KEY,
  action TEXT NOT NULL,
  api TEXT NOT NULL,
  time TEXT NOT NULL,
  status TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS subscriptions (
  id TEXT PRIMARY KEY,
  user_name TEXT NOT NULL,
  api_id TEXT NOT NULL REFERENCES apis (id),
  plan TEXT NOT NULL,
  created_at TEXT NOT NULL,
  UNIQUE (user_name, api_id)
);
