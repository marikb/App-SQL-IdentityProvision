CREATE USER [<your-vm-name>] FROM EXTERNAL PROVIDER
ALTER ROLE db_datareader ADD MEMBER [<your-vm-name>]
ALTER ROLE db_datawriter ADD MEMBER [<your-vm-name>]