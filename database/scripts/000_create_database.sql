-- Creates the VMSGlobal database (SQL Server LocalDB: (localdb)\MSSQLLocalDB). Safe to re-run.
IF DB_ID(N'VMSGlobal') IS NULL
    CREATE DATABASE [VMSGlobal];
GO
