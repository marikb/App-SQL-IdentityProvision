using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Azure.Identity;
using Microsoft.Graph;
using System.Linq;

namespace ClickSync
{
    class Program
    {
        public static DefaultAzureCredential credential = new DefaultAzureCredential();
        public static GraphServiceClient graphServiceClient;
        public static string userPrincipalNameSuffix;
        public static bool disableUsers = false;
        public static bool debug = false;
        public static int usersUpdated = 0;
        public static int retirementsProcessed = 0;
        public static int errors = 0;

        static async Task<int> Main(string[] args)
        {
            if(args.Length > 0 && args[0].ToLower() == "printroles")
                await GraphHelper.PrintRoles();

            var watch = System.Diagnostics.Stopwatch.StartNew();

            #region Collect values
            string sqlConnString = GetValue("sqldb_connection");
            userPrincipalNameSuffix = GetValue("userPrincipalNameSuffix");
            string strRowsPerCycle = GetValue("rowsPerCycle");

            string strDebug = GetValue("debug");
            bool.TryParse(strDebug, out debug);

            string strDisableUsers = GetValue("disableUsers");
            bool.TryParse(strDisableUsers, out disableUsers);

            bool sendMailNotification;
            bool.TryParse(GetValue("sendMailNotification"), out sendMailNotification);

            int rowsPerCycle;
            bool parsed = int.TryParse(strRowsPerCycle, out rowsPerCycle);
            if(!parsed)
                rowsPerCycle = 100;

            string strMaxRetirements = GetValue("maxRetirements");
            int maxRetirements;
            parsed = int.TryParse(strMaxRetirements, out maxRetirements);
            if(!parsed)
                maxRetirements = 500;

            string strMaxChanges = GetValue("maxChanges");
            int maxChanges;
            parsed = int.TryParse(strMaxChanges, out maxChanges);
            if(!parsed)
                maxChanges = 500;

            string strMaxSyncRetries = GetValue("maxSyncRetries");
            int maxSyncRetries;
            parsed = int.TryParse(strMaxSyncRetries, out maxSyncRetries);
            if(!parsed || maxSyncRetries < 1)
                maxSyncRetries = 3;

            string strLicenseGroups = GetValue("licenseGroups");
            #endregion

            graphServiceClient = GraphHelper.GetGraphApiClient();

            var licenseGroups = new List<string>();
            if(!string.IsNullOrEmpty(strLicenseGroups))
                licenseGroups = strLicenseGroups.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            string missingValues = "";
            if(string.IsNullOrEmpty(sqlConnString))
                missingValues += " sqldb_connection";
            if(string.IsNullOrEmpty(userPrincipalNameSuffix))
                missingValues += " userPrincipalNameSuffix";
            if(licenseGroups.Count == 0)
                missingValues += " licenseGroups";
            if(sendMailNotification && string.IsNullOrEmpty(GetValue("mailNotificationTo")))
                missingValues += " mailNotificationTo";
            if(sendMailNotification && string.IsNullOrEmpty(GetValue("mailNotificationFrom")))
                missingValues += " mailNotificationFrom";
            if(missingValues != ""){
                WriteLog("e",$"Missing required configuration values:{missingValues}");
                await GraphHelper.SendMail($"Missing required configuration values:{missingValues}", "There was an error in Click synchronization process");
                return 1;
            }

            DBHelper db = new DBHelper(sqlConnString, maxSyncRetries);
            await db.Connect();
            db.WriteLog("INFORMATION", $"Starting synchronization");


            var numberOfRetirements = await db.GetNumberOfRetirementsFromDB();
            if(numberOfRetirements < maxRetirements){
                await HandleGroupRemoval(db,rowsPerCycle,licenseGroups);
                db.WriteLog("INFORMATION", $"Group removal process finished in {watch.ElapsedMilliseconds * 0.001} seconds.");
            }else{
                errors++;
                db.WriteLog("ERROR", $"There are {numberOfRetirements} retirements which is at or over the allowed number ({maxRetirements}).");
            }

            var numberOfChanges = await db.GetNumberOfChangesFromDB();
            if(numberOfChanges < maxChanges)
                await HandleUserUpdates(db,rowsPerCycle);
            else{
                errors++;
                db.WriteLog("ERROR", $"There are {numberOfChanges} changes which is at or over the allowed number ({maxChanges}).");
            }

            var numberOfSkipped = await db.GetNumberOfSkippedChangesFromDB();
            if(numberOfSkipped > 0)
                db.WriteLog("WARNING", $"{numberOfSkipped} users are skipped after failing {maxSyncRetries} times, reset SyncErrorCount to retry them.");

            watch.Stop();
            string message, subject;
            if(errors > 0){
                subject = "Click synchronization finished with errors";
                message = $"Synchronization finished with errors please check the log table,\n" +
                $"Updated {usersUpdated} users, processed {retirementsProcessed} retirements in {watch.ElapsedMilliseconds * 0.001} seconds.\n" +
                $"Errors: {errors}";
            }else{
                subject = "Click synchronization finished";
                message = $"Synchronization finished,\n" +
                $"Updated {usersUpdated} users, processed {retirementsProcessed} retirements in {watch.ElapsedMilliseconds * 0.001} seconds.";
            }
            if(numberOfSkipped > 0)
                message += $"\nSkipped users: {numberOfSkipped} (failed {maxSyncRetries} times, reset SyncErrorCount to retry them)";
            db.WriteLog("INFORMATION", message);
            db.Disconnect();
            WriteLog("i",message);
            await GraphHelper.SendMail(message, subject);
            return errors > 0 ? 1 : 0;
        }

        public static async Task HandleUserUpdates(DBHelper db, int rowsPerCycle){
            string lastTZ = "";
            while (true)
            {
                var clickUsers = await db.GetUsersFromDB(rowsPerCycle, lastTZ);
                if (clickUsers.Count == 0)
                    break;
                foreach (ClickUser clickUser in clickUsers){
                    if (await GraphHelper.UpdateUserInGraph(clickUser, db))
                        await db.UpdateClickSynced(clickUser.tz, clickUser.rowVer);
                    else
                        await db.IncrementSyncErrorCount(clickUser.tz);
                }
                lastTZ = clickUsers[clickUsers.Count - 1].tz;
            }
        }

        public static async Task HandleGroupRemoval(DBHelper db, int rowsPerCycle, List<string> licenseGroups){
            string lastTZ = "";
            while(true){
                var clickUsers = await db.GetRetirementsFromDB(rowsPerCycle, lastTZ);
                if (clickUsers.Count == 0)
                    break;

                foreach (ClickUser clickUser in clickUsers){
                    if (await ProcessRetirementInGraph(clickUser, db, licenseGroups)){
                        await db.MarkRetirementProcessed(clickUser.tz);
                        retirementsProcessed++;
                    }else
                        await db.IncrementSyncErrorCount(clickUser.tz);
                }
                lastTZ = clickUsers[clickUsers.Count - 1].tz;
            }
        }

        public static async Task<bool> ProcessRetirementInGraph(ClickUser clickUser, DBHelper db, List<string> licenseGroups){
            if (disableUsers && !await GraphHelper.DisableUserInGraph(clickUser, db))
                return false;

            var groups2Remove = await GraphHelper.CheckUserGroupsInGraph(licenseGroups, clickUser, db);
            if (groups2Remove == null)
                return false;

            bool processed = true;
            foreach(var group in groups2Remove){
                if (!await GraphHelper.RemoveUserFromGroupInGraph(clickUser, group, db))
                    processed = false;
            }
            return processed;
        }

        public static string GetValue(string valueName){
            return Environment.GetEnvironmentVariable(valueName) != null ? Environment.GetEnvironmentVariable(valueName) : System.AppContext.GetData(valueName) as string;
        }

        public static void WriteLog(string level, string str){
            switch (level)
            {
                case "i":
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("INFORMATION: ");
                    Console.ResetColor();
                    Console.Write(str);
                    Console.WriteLine();
                    break;
                case "w":
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("WARNING: ");
                    Console.ResetColor();
                    Console.Write(str);
                    Console.WriteLine();
                    break;
                case "e":
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("ERROR: ");
                    Console.ResetColor();
                    Console.Write(str);
                    Console.WriteLine();
                    break;
                case "d":
                    if(debug){
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.Write("DEBUG: ");
                        Console.ResetColor();
                        Console.Write(str);
                        Console.WriteLine();
                    }
                    break;
            }
        }
    }
}
