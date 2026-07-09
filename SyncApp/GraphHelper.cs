using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.CheckMemberGroups;
using Microsoft.Graph.Users.Item.SendMail;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;

namespace ClickSync
{
    class GraphHelper{
        public static GraphServiceClient GetGraphApiClient()
        {
            return new GraphServiceClient(Program.credential, new[] { "https://graph.microsoft.com/.default" });
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
                await Program.graphServiceClient.Users[$"{clickUser.tz}{Program.userPrincipalNameSuffix}"].PatchAsync(user);
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
                    .CheckMemberGroups
                    .PostAsCheckMemberGroupsPostResponseAsync(new CheckMemberGroupsPostRequestBody { GroupIds = groupIds });
                return result.Value.Select(g => g.ToString()).ToList();
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
                await Program.graphServiceClient.Groups[groupID].Members[clickUser.clickObjectID].Ref.DeleteAsync();
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

            try{
                await Program.graphServiceClient.Users[from]
                .SendMail
                .PostAsync(new SendMailPostRequestBody { Message = message, SaveToSentItems = true });
            }catch(Exception ex){
                string str = $"Error cannot send email, error: {ex.Message}";
                Program.WriteLog("e",str);
            }

        }

        public static async Task PrintRoles(){
            var accessToken = await Program.credential.GetTokenAsync(new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" }));
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadToken(accessToken.Token) as JwtSecurityToken;
            Console.WriteLine(token.Payload["roles"]);
            Environment.Exit(0);
        }


    }
}