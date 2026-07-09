using System;
using System.Collections.Generic;
using Azure.Core;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace ClickSync
{
    class DBHelper{
        private string _sqlConnString;
        private SqlConnection _conn;
        private int _maxSyncRetries;

        public DBHelper(string sqlConnString, int maxSyncRetries){
            this._sqlConnString = sqlConnString;
            this._maxSyncRetries = maxSyncRetries;
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
                await GraphHelper.SendMail($"Error connecting to SQL\n error: {ex.Message}", "There was an error in Click synchronization process");
                Environment.Exit(-1);
            }
        }

        public void Disconnect(){
            _conn.Close();
        }

        public async Task<int> GetNumberOfRetirementsFromDB(){
            try
            {
                var sqlCommand = "SELECT COUNT(*) FROM [dbo].[Pratim_pp] WHERE ClickObjectID IS NOT NULL AND RetirementProcessed=0 AND RetirementDate <= GETDATE()";
                SqlCommand cmd = new SqlCommand(sqlCommand, _conn);
                return (int)cmd.ExecuteScalar();
            }
            catch (System.Exception ex)
            {
                string message = $"Error getting number of retirements from SQL!\n error: {ex.Message}";
                Program.WriteLog("e",message);
                await GraphHelper.SendMail(message, "There was an error in Click synchronization process");
                Environment.Exit(-1);
                return 0;
            }
        }

        public async Task<int> GetNumberOfChangesFromDB(){
            try
            {
                var sqlCommand = "SELECT COUNT(*) FROM [dbo].[Pratim_pp] WHERE ClickObjectID IS NOT NULL AND ClickSynced=0 AND SyncErrorCount<@maxRetries AND (NOT RetirementDate <= GETDATE() OR RetirementDate IS NULL)";
                SqlCommand cmd = new SqlCommand(sqlCommand, _conn);
                cmd.Parameters.AddWithValue("@maxRetries", _maxSyncRetries);
                return (int)cmd.ExecuteScalar();
             }
            catch (System.Exception ex)
            {
                string message = $"Error getting number of changes from SQL!\n error: {ex.Message}";
                Program.WriteLog("e",message);
                await GraphHelper.SendMail(message, "There was an error in Click synchronization process");
                Environment.Exit(-1);
                return 0;
            }
        }

        public async Task<int> GetNumberOfSkippedChangesFromDB(){
            try
            {
                var sqlCommand = "SELECT COUNT(*) FROM [dbo].[Pratim_pp] WHERE ClickObjectID IS NOT NULL AND ClickSynced=0 AND SyncErrorCount>=@maxRetries AND (NOT RetirementDate <= GETDATE() OR RetirementDate IS NULL)";
                SqlCommand cmd = new SqlCommand(sqlCommand, _conn);
                cmd.Parameters.AddWithValue("@maxRetries", _maxSyncRetries);
                return (int)cmd.ExecuteScalar();
             }
            catch (System.Exception ex)
            {
                string message = $"Error getting number of skipped users from SQL!\n error: {ex.Message}";
                Program.WriteLog("e",message);
                await GraphHelper.SendMail(message, "There was an error in Click synchronization process");
                Environment.Exit(-1);
                return 0;
            }
        }

        public async Task<List<ClickUser>> GetRetirementsFromDB(int numberOfUsersToGet, string afterTZ){
            try
            {
                var sqlCommand = "SELECT TOP (@rows) * FROM [dbo].[Pratim_pp] WHERE ClickObjectID IS NOT NULL AND RetirementProcessed=0 AND RetirementDate <= GETDATE() AND TZ>@afterTZ ORDER BY TZ";
                List<ClickUser> clickUsers = new List<ClickUser>();
                SqlCommand cmd = new SqlCommand(sqlCommand, _conn);
                cmd.Parameters.AddWithValue("@rows", numberOfUsersToGet);
                cmd.Parameters.AddWithValue("@afterTZ", afterTZ);
                SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                    clickUsers.Add(new ClickUser(reader));
                reader.Close();
                await reader.DisposeAsync();
                return clickUsers;
            }
            catch (Exception ex)
            {
                string message = $"Error getting retirement users from SQL!\n error: {ex.Message}";
                Program.WriteLog("e",message);
                await GraphHelper.SendMail(message, "There was an error in Click synchronization process");
                Environment.Exit(-1);
                return new List<ClickUser>();
            }
        }

        public async Task<List<ClickUser>> GetUsersFromDB(int numberOfUsersToGet, string afterTZ)
        {
            try
            {
                List<ClickUser> clickUsers = new List<ClickUser>();
                var sqlCommand = "SELECT TOP (@rows) * FROM [dbo].[Pratim_pp] WHERE ClickObjectID IS NOT NULL AND ClickSynced=0 AND SyncErrorCount<@maxRetries AND (NOT RetirementDate <= GETDATE() OR RetirementDate IS NULL) AND TZ>@afterTZ ORDER BY TZ";
                SqlCommand cmd = new SqlCommand(sqlCommand, _conn);
                cmd.Parameters.AddWithValue("@rows", numberOfUsersToGet);
                cmd.Parameters.AddWithValue("@maxRetries", _maxSyncRetries);
                cmd.Parameters.AddWithValue("@afterTZ", afterTZ);
                SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                    clickUsers.Add(new ClickUser(reader));
                reader.Close();
                await reader.DisposeAsync();
                return clickUsers;
            }
            catch (Exception ex)
            {
                Program.WriteLog("e",$"Error getting users from SQL!\n error: {ex.Message}");
                await GraphHelper.SendMail($"Error getting users from SQL!\n error: {ex.Message}", "There was an error in Click synchronization process");
                Environment.Exit(-1);
                return new List<ClickUser>();
            }
        }

        public async Task UpdateClickSynced(string TZ, byte[] rowVer)
        {
            await UpdateRow("UPDATE [dbo].[Pratim_pp] SET ClickSynced=1, SyncErrorCount=0, RetirementProcessed=0 WHERE TZ=@tz AND RowVer=@rowVer", TZ, rowVer);
        }

        public async Task MarkRetirementProcessed(string TZ)
        {
            await UpdateRow("UPDATE [dbo].[Pratim_pp] SET RetirementProcessed=1, SyncErrorCount=0 WHERE TZ=@tz", TZ);
        }

        public async Task IncrementSyncErrorCount(string TZ)
        {
            await UpdateRow("UPDATE [dbo].[Pratim_pp] SET SyncErrorCount=SyncErrorCount+1 WHERE TZ=@tz", TZ);
        }

        private async Task UpdateRow(string sqlCommand, string TZ, byte[] rowVer = null)
        {
            try
            {
                SqlCommand cmd = new SqlCommand(sqlCommand, _conn);
                cmd.Parameters.AddWithValue("@tz", TZ);
                if (rowVer != null)
                    cmd.Parameters.AddWithValue("@rowVer", rowVer);
                cmd.ExecuteNonQuery();
                cmd.Dispose();
            }
            catch (System.Exception ex)
            {
                string message = $"Error updating record in SQL command: {sqlCommand} error: {ex.Message}";
                Program.WriteLog("e", message);
                WriteLog("ERROR", message);
                await GraphHelper.SendMail(message, "There was an error in Click synchronization process");
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