-- Gives the web app and function app managed identities least-privilege access to the PlantOps database.
-- Run as the SQL Entra admin (the deploy identity), against the PlantOps database (not master):
--   sqlcmd -S <server>.database.windows.net -d PlantOps -G -b -i deploy/azure/sql/grant-app-identities.sql \
--          -v WEB_APP_NAME=<web app> WEB_OBJECT_ID=<principal id> FUNC_APP_NAME=<function app> FUNC_OBJECT_ID=<principal id>
-- A system-assigned identity's name is the app's name; its object ID is the "principalId" in the Bicep outputs
-- (webPrincipalId / functionPrincipalId). WITH OBJECT_ID avoids a Microsoft Graph lookup by the SQL server, which would
-- otherwise need extra directory permissions. Safe to re-run: every statement checks first or is naturally idempotent.
-- Schema changes (DDL) are NOT granted: migrations run from the pipeline's bundles as the admin (ADR-0004).

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(WEB_APP_NAME)')
BEGIN
    CREATE USER [$(WEB_APP_NAME)] FROM EXTERNAL PROVIDER WITH OBJECT_ID = '$(WEB_OBJECT_ID)';
END;
ALTER ROLE db_datareader ADD MEMBER [$(WEB_APP_NAME)];
ALTER ROLE db_datawriter ADD MEMBER [$(WEB_APP_NAME)];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(FUNC_APP_NAME)')
BEGIN
    CREATE USER [$(FUNC_APP_NAME)] FROM EXTERNAL PROVIDER WITH OBJECT_ID = '$(FUNC_OBJECT_ID)';
END;
ALTER ROLE db_datareader ADD MEMBER [$(FUNC_APP_NAME)];
ALTER ROLE db_datawriter ADD MEMBER [$(FUNC_APP_NAME)];
GO
