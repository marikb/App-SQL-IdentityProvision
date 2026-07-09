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

namespace SyncApp
{
    class GraphHelper{
        public static GraphServiceClient GetGraphApiClient()
        {
            return new GraphServiceClient(Program.credential, new[] { "https://graph.microsoft.com/.default" });
        }

        public static async Task UpdateUserInGraph(SyncUser syncUser, DBHelper db)
        {
            User user = new User();
            if(!string.IsNullOrEmpty(syncUser.firstName))
                user.GivenName = syncUser.firstName;
            if(!string.IsNullOrEmpty(syncUser.lastName))
                user.Surname = syncUser.lastName;
            if(!string.IsNullOrEmpty(syncUser.mobilePhone))
                user.MobilePhone = syncUser.mobilePhone;
            string displayName = $"{syncUser.firstName} {syncUser.lastName}".Trim();
            if(displayName != "")
                user.DisplayName = displayName;
            user.State = "SyncApp";

            if(Program.disableUsers && syncUser.isActive != null)
                user.AccountEnabled = syncUser.isActive;

            try
            {
                Program.WriteLog("d",$"updating user {syncUser.tz}{Program.userPrincipalNameSuffix}");
                await Program.graphServiceClient.Users[$"{syncUser.tz}{Program.userPrincipalNameSuffix}"].PatchAsync(user);
                await db.MarkSynced(syncUser.tz);
                Program.WriteLog("d",$"updated user {syncUser.tz}{Program.userPrincipalNameSuffix}");
                Program.usersUpdated++;
            }
            catch (Exception ex)
            {
                Program.errors++;
                string str = $"error updating user {syncUser.tz}{Program.userPrincipalNameSuffix} Error: {ex.Message}";
                Program.WriteLog("e", str);
                db.WriteLog("ERROR", str);
                await db.MarkSynced(syncUser.tz);
            }
        }

        public static async Task<List<string>> CheckUserGroupsInGraph(List<string> groupIds, SyncUser syncUser, DBHelper db){
            try
            {
                var memberships = new List<string>();
                for(int i = 0; i < groupIds.Count; i += 20){
                    var chunk = groupIds.Skip(i).Take(20).ToList();
                    var result = await Program.graphServiceClient.Users[$"{syncUser.tz}{Program.userPrincipalNameSuffix}"]
                        .CheckMemberGroups
                        .PostAsCheckMemberGroupsPostResponseAsync(new CheckMemberGroupsPostRequestBody { GroupIds = chunk });
                    if (result != null && result.Value != null)
                        memberships.AddRange(result.Value.Select(g => g.ToString()));
                }
                return memberships;
            }
            catch (System.Exception ex)
            {
                Program.errors++;
                string str = $"error checking user {syncUser.tz}{Program.userPrincipalNameSuffix} groups Error: {ex.Message}";
                Program.WriteLog("e", str);
                db.WriteLog("ERROR", str);
                await db.MarkSynced(syncUser.tz);
                return new List<string>();
            }
        }

        public static async Task RemoveUserFromGroupInGraph(SyncUser syncUser, string groupID, DBHelper db){
            try{
                Program.WriteLog("d",$"removing user {syncUser.tz}{Program.userPrincipalNameSuffix} from group {groupID}");
                await Program.graphServiceClient.Groups[groupID].Members[syncUser.aadObjectID].Ref.DeleteAsync();
                await db.MarkSynced(syncUser.tz);
                Program.WriteLog("d",$"removed user {syncUser.tz}{Program.userPrincipalNameSuffix} from group {groupID}");
            }catch(Exception ex){
                Program.errors++;
                string str = $"error removing user {syncUser.tz}{Program.userPrincipalNameSuffix} from group {groupID} Error: {ex.Message}";
                Program.WriteLog("e", str);
                db.WriteLog("ERROR", str);
                await db.MarkSynced(syncUser.tz);
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
            if (token.Payload.TryGetValue("roles", out var roles))
                Console.WriteLine(roles);
            else
                Console.WriteLine("No roles are assigned to this identity.");
            Environment.Exit(0);
        }


    }
}