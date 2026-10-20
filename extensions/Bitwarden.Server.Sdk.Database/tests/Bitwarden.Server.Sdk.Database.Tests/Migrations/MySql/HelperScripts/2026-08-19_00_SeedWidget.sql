-- Same file name under a different provider, so resolution has to choose. Never runs in these
-- tests; if it ever did, the asserted value would be wrong rather than merely absent.
INSERT INTO Widgets (Id, Name) VALUES (1, 'from-mysql-helper-script')
ON DUPLICATE KEY UPDATE Name = VALUES(Name);
