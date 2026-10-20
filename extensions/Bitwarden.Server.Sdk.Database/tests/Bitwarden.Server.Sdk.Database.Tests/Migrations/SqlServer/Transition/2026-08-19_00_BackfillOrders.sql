-- Unjournaled, so it re-applies on every transition run and has to be idempotent.
IF NOT EXISTS (SELECT 1 FROM [dbo].[Orders] WHERE [Name] = 'backfilled')
BEGIN
    INSERT INTO [dbo].[Orders] ([Name]) VALUES ('backfilled');
END
