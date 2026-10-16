-- PostgreSQL dialect: neither INSERT OR REPLACE nor ON DUPLICATE KEY exists here.
INSERT INTO "Widgets" ("Id", "Name") VALUES (1, 'from-postgresql-helper-script')
ON CONFLICT ("Id") DO UPDATE SET "Name" = EXCLUDED."Name";
