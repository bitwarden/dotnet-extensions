IF NOT EXISTS (SELECT 1 FROM [dbo].[Orders] WHERE [Name] = 'initial')
BEGIN
    INSERT INTO [dbo].[Orders] ([Name]) VALUES ('initial');
END
