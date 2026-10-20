-- Deliberately dialect-specific: MySQL has no INSERT OR REPLACE.
INSERT OR REPLACE INTO Widgets (Id, Name) VALUES (1, 'from-sqlite-helper-script');
