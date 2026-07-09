using System;
using System.Collections.Generic;
using Azure.Core;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SyncApp
{
    class DBHelper{
        private string _sqlConnString;
        private SqlConnection _conn;

        public DBHelper(string sqlConnString){
            this._sqlConnString = sqlConnString;
        }

        public async Task Connect(){
            try
            {
                var accessToken = await Program.credential.GetTokenAsync(new TokenRequestContext(new[] { "https://database.windows.net/.default" }));
                this._conn = new SqlConnection(_sqlConnString);
                _conn.AccessToken = accessToken.Token;
                _conn.Open();
            }
            catch (Exception ex)
            {
                Program.WriteLog("e",$"Error connecting to SQL\n error: {ex.Message}");
                await GraphHelper.SendMail($"Error connecting to SQL\n error: {ex.Message}", "There was an error in the synchronization process");
                Environment.Exit(-1);
            }
        }

        public void Disconnect(){
            _conn.Close();
        }

        public async Task<int> GetNumberOfRetirementsFromDB(){
            try
            {
                var sqlCommand = "SELECT COUNT(*) FROM [dbo].[Pratim_pp] WHERE AADObjectID IS NOT NULL AND Synced=0 AND RetirementDate <= GETDATE()";
                SqlCommand cmd = new SqlCommand(sqlCommand, _conn);
                return (int)cmd.ExecuteScalar();
            }
            catch (System.Exception ex)
            {
                string message = $"Error getting number of retirements from SQL!\n error: {ex.Message}";
                Program.WriteLog("e",message);
                await GraphHelper.SendMail(message, "There was an error in the synchronization process");
                Environment.Exit(-1);
                return 0;
            }
        }

        public async Task<int> GetNumberOfChangesFromDB(){
            try
            {
                var sqlCommand = "SELECT COUNT(*) FROM [dbo].[Pratim_pp] WHERE AADObjectID IS NOT NULL AND Synced=0 AND (NOT RetirementDate <= GETDATE() OR RetirementDate IS NULL)";
                SqlCommand cmd = new SqlCommand(sqlCommand, _conn);
                return (int)cmd.ExecuteScalar();
             }
            catch (System.Exception ex)
            {
                string message = $"Error getting number of changes from SQL!\n error: {ex.Message}";
                Program.WriteLog("e",message);
                await GraphHelper.SendMail(message, "There was an error in the synchronization process");
                Environment.Exit(-1);
                return 0;
            }
        }

        public async Task<List<SyncUser>> GetRetirementsFromDB(int numberOfUsersToGet){
            try
            {
                var sqlCommand = "SELECT TOP (@rows) * FROM [dbo].[Pratim_pp] WHERE AADObjectID IS NOT NULL AND Synced=0 AND RetirementDate <= GETDATE()";
                List<SyncUser> syncUsers = new List<SyncUser>();
                SqlCommand cmd = new SqlCommand(sqlCommand, _conn);
                cmd.Parameters.AddWithValue("@rows", numberOfUsersToGet);
                SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                    syncUsers.Add(new SyncUser(reader));
                reader.Close();
                await reader.DisposeAsync();
                return syncUsers;
            }
            catch (Exception ex)
            {
                string message = $"Error getting retirement users from SQL!\n error: {ex.Message}";
                Program.WriteLog("e",message);
                await GraphHelper.SendMail(message, "There was an error in the synchronization process");
                Environment.Exit(-1);
                return new List<SyncUser>();
            }
        }

        public async Task<List<SyncUser>> GetUsersFromDB(int numberOfUsersToGet)
        {
            try
            {
                List<SyncUser> syncUsers = new List<SyncUser>();
                var sqlCommand = "SELECT TOP (@rows) * FROM [dbo].[Pratim_pp] WHERE AADObjectID IS NOT NULL AND Synced=0 AND (NOT RetirementDate <= GETDATE() OR RetirementDate IS NULL)";
                SqlCommand cmd = new SqlCommand(sqlCommand, _conn);
                cmd.Parameters.AddWithValue("@rows", numberOfUsersToGet);
                SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                    syncUsers.Add(new SyncUser(reader));
                reader.Close();
                await reader.DisposeAsync();
                return syncUsers;
            }
            catch (Exception ex)
            {
                Program.WriteLog("e",$"Error getting users from SQL!\n error: {ex.Message}");
                await GraphHelper.SendMail($"Error getting users from SQL!\n error: {ex.Message}", "There was an error in the synchronization process");
                Environment.Exit(-1);
                return new List<SyncUser>();
            }
        }

        public async Task MarkSynced(string TZ)
        {
            var sqlCommand = "UPDATE [dbo].[Pratim_pp] SET Synced=1 WHERE TZ=@tz";

            try
            {
                SqlCommand cmd = new SqlCommand(sqlCommand, _conn);
                cmd.Parameters.AddWithValue("@tz", TZ);
                cmd.ExecuteNonQuery();
                cmd.Dispose();
            }
            catch (System.Exception ex)
            {
                string message = $"Error updating record in SQL command: {sqlCommand} error: {ex.Message}";
                Program.WriteLog("e", message);
                WriteLog("ERROR", message);
                await GraphHelper.SendMail(message, "There was an error in the synchronization process");
                Environment.Exit(-1);
            }
        }

        public void WriteLog(string type, string description)
        {
            var sqlCommand = "INSERT INTO Sync_log (date,type,description) VALUES(GETDATE(),@type,@description)";
            try
            {
                SqlCommand cmd = new SqlCommand(sqlCommand, _conn);
                cmd.Parameters.AddWithValue("@type", type);
                cmd.Parameters.AddWithValue("@description", description);
                cmd.ExecuteNonQuery();
                cmd.Dispose();
            }
            catch (System.Exception ex)
            {
                Program.WriteLog("e",$"Error writing to SQL {sqlCommand} error: {ex.Message}");
            }
        }
    }
}