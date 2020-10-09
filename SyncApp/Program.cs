using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Graph;
using System.Linq;

namespace ClickSync
{
    class Program
    {
        public static GraphServiceClient graphServiceClient;
        public static string userPrincipalNameSuffix;
        public static bool disableUsers = false;
        public static bool debug = false;
        public static bool error = false;
        public static int usersUpdated = 0;
        public static int usersCreated = 0;
        public static int errors = 0;

        static async Task Main(string[] args)
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

            string strLicenseGroups = GetValue("licenseGroups");
            var licenseGroups = strLicenseGroups.Split(',').ToList();
            #endregion
            
            graphServiceClient = await GraphHelper.GetGraphApiClient();

            DBHelper db = new DBHelper(sqlConnString);
            await db.Connect();
            db.WriteLog("INFORMATION", $"Starting synchronization");
            

            var numberOfRetirements = await db.GetNumberOfRetirementsFromDB();
            bool allowGroupRemoval = numberOfRetirements < maxRetirements ? true : false;

            if(allowGroupRemoval){
                await HandleGroupRemoval(db,rowsPerCycle,licenseGroups);
                db.WriteLog("INFORMATION", $"Group removal process finished in {watch.ElapsedMilliseconds * 0.001} seconds.");
            }else{
                error = true;
                Program.errors++;
                db.WriteLog("ERROR", $"There are {numberOfRetirements} retirements which is over the allowed number ({maxRetirements}).");
            }
            
            var numberOfChanges = await db.GetNumberOfChangesFromDB();
            if(numberOfChanges < maxChanges)
                await HandleUserUpdates(db,rowsPerCycle);
            else{
                error = true;
                Program.errors++;
                db.WriteLog("ERROR", $"There are {numberOfChanges} changes which is over the allowed number ({maxChanges}).");
            }
                

            watch.Stop();
            string message, subject;
            if(error){
                subject = "Click synchronization finished with errors";
                message = $"Synchronization finished with errors please check the log table,\n" +
                $"Created {usersCreated} users, updated {usersUpdated} in {watch.ElapsedMilliseconds * 0.001} seconds.\n" +
                $"Errors: {errors}";
            }else{
                subject = "Click synchronization finished";
                message = $"Synchronization finished,\n" +
                $"Created {usersCreated} users, updated {usersUpdated} in {watch.ElapsedMilliseconds * 0.001} seconds.";
            }
            db.WriteLog("INFORMATION", message);
            db.Disconnect();
            WriteLog("i",message);
            await GraphHelper.SendMail(message, subject);
        }

        public static async Task HandleUserUpdates(DBHelper db, int rowsPerCycle){
            List<ClickUser> clickUsers = new List<ClickUser>();
            while (true)
            {
                clickUsers = await db.GetUsersFromDB(rowsPerCycle);
                if (clickUsers.Count == 0)
                    break;
                foreach (ClickUser clickUser in clickUsers)
                    await GraphHelper.UpdateUserInGraph(clickUser, db);
            }
        }

        public static async Task HandleGroupRemoval(DBHelper db, int rowsPerCycle, List<string> licenseGroups){
            List<ClickUser> clickUsers = new List<ClickUser>();
            while(true){
                clickUsers = await db.GetRetirementsFromDB(rowsPerCycle);
                if (clickUsers.Count == 0)
                    break;

                foreach (ClickUser clickUser in clickUsers){
                    var groups2Remove = await GraphHelper.CheckUserGroupsInGraph(licenseGroups,clickUser, db);
                    foreach(var group in groups2Remove)
                        await GraphHelper.RemoveUserFromGroupInGraph(clickUser,group,db);
                }
            }
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
