using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Graph;
using System.Net.Http.Headers;
using Microsoft.Azure.Services.AppAuthentication;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;

namespace ClickSync
{
    class GraphHelper{
        public static async Task<GraphServiceClient> GetGraphApiClient()
        {
            try{
                var azureServiceTokenProvider = new AzureServiceTokenProvider();
                string accessToken = await azureServiceTokenProvider
                    .GetAccessTokenAsync("https://graph.microsoft.com/");

                var graphServiceClient = new GraphServiceClient(
                    new DelegateAuthenticationProvider((requestMessage) =>
                {
                    requestMessage
                            .Headers
                            .Authorization = new AuthenticationHeaderValue("bearer", accessToken);

                    return Task.CompletedTask;
                }));

                return graphServiceClient;
            }catch(Exception ex){
                Program.WriteLog("e",$"Error connecting Microsoft Graph.\n error: {ex.Message}");
                Environment.Exit(-1);
                return null;
            }  
        }

        public static async Task UpdateUserInGraph(ClickUser clickUser, DBHelper db)
        {
            User user = new User();
            user.GivenName = clickUser.firstName;
            user.Surname = clickUser.lastName;
            user.MobilePhone = clickUser.mobilePhone;
            user.DisplayName = $"{clickUser.firstName} {clickUser.lastName}";
            user.State = "ClickSync";
            
            if(Program.disableUsers)
                user.AccountEnabled = clickUser.isActive;

            try
            {
                Program.WriteLog("d",$"updating user {clickUser.tz}{Program.userPrincipalNameSuffix}");
                var result =  await Program.graphServiceClient.Users[$"{clickUser.tz}{Program.userPrincipalNameSuffix}"].Request().UpdateAsync(user);
                string graphObjectId = clickUser.clickObjectID;               
                await db.UpdateClickSynced(clickUser.tz);
                Program.WriteLog("d",$"updated user {clickUser.tz}{Program.userPrincipalNameSuffix}");
                Program.usersUpdated++;
            }
            catch (Exception ex)
            {
                Program.error = true;
                Program.errors++;
                string str = $"error updating user {clickUser.tz}{Program.userPrincipalNameSuffix} Error: {ex.Message}";
                Program.WriteLog("e", str);
                db.WriteLog("ERROR", str);
                await db.UpdateClickSynced(clickUser.tz);
            }
        }

        public static async Task<List<string>> CheckUserGroupsInGraph(List<string> groupIds, ClickUser clickUser, DBHelper db){
            try
            {
                var result = await Program.graphServiceClient.Users[$"{clickUser.tz}{Program.userPrincipalNameSuffix}"]
                    .CheckMemberGroups(groupIds)
                    .Request().PostAsync();
                return result.ToList();
            }
            catch (System.Exception ex)
            {
                Program.error = true;
                Program.errors++;
                string str = $"error checking user {clickUser.tz}{Program.userPrincipalNameSuffix} groups Error: {ex.Message}";
                Program.WriteLog("e", str);
                db.WriteLog("ERROR", str);
                await db.UpdateClickSynced(clickUser.tz);
                return new List<string>();
            }
        }

        public static async Task RemoveUserFromGroupInGraph(ClickUser clickUser, string groupID, DBHelper db){
            try{
                Program.WriteLog("d",$"removing user {clickUser.tz}{Program.userPrincipalNameSuffix} from group {groupID}");
                await Program.graphServiceClient.Groups[groupID].Members[clickUser.clickObjectID].Reference.Request().DeleteAsync();
                await db.UpdateClickSynced(clickUser.tz);
                Program.WriteLog("d",$"removed user {clickUser.tz}{Program.userPrincipalNameSuffix} from group {groupID}");
            }catch(Exception ex){
                Program.error = true;
                string str = $"error removing user {clickUser.tz}{Program.userPrincipalNameSuffix} from group {groupID} Error: {ex.Message}";
                Program.WriteLog("e", str);
                db.WriteLog("ERROR", str);
                await db.UpdateClickSynced(clickUser.tz);
            }
        }

        public static async Task SendMail(string body, string subject)
        {
            bool sendMailNotification;
            string strSendMailNotification = Program.GetValue("sendMailNotification");
            bool.TryParse(strSendMailNotification, out sendMailNotification);
            if(!sendMailNotification)
                return;
                
            string to = Program.GetValue("mailNotificationTo");
            string from = Program.GetValue("mailNotificationFrom");

            var message = new Message
            {
                Subject = subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = body
                },
                ToRecipients = new List<Recipient>()
                {
                    new Recipient
                    {
                        EmailAddress = new EmailAddress
                        {
                            Address = to
                        }
                    }
                }
            };

            var saveToSentItems = true;
            try{
                await Program.graphServiceClient.Users[from]
                .SendMail(message, saveToSentItems)
                .Request()
                .PostAsync();
            }catch(Exception ex){
                string str = $"Error cannot send email, error: {ex.Message}";
                Program.WriteLog("e",str);
            }
            
        }

        public static async Task PrintRoles(){
            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            string accessToken = await azureServiceTokenProvider.GetAccessTokenAsync("https://graph.microsoft.com/");
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadToken(accessToken) as JwtSecurityToken;
            Console.WriteLine(token.Payload["roles"]);
            Environment.Exit(0);
        }

        
    }
}