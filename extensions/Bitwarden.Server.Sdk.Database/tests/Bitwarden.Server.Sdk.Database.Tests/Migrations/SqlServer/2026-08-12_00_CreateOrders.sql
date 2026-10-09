IF OBJECT_ID('[dbo].[Orders]', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Orders]
    (
        [Id] INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Name] NVARCHAR(200) NOT NULL
    );
END
