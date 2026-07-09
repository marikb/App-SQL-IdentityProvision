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

        public static async Task<bool> UpdateUserInGraph(ClickUser clickUser, DBHelper db)
        {
            User user = new User();
            if(!string.IsNullOrEmpty(clickUser.firstName))
                user.GivenName = clickUser.firstName;
            if(!string.IsNullOrEmpty(clickUser.lastName))
                user.Surname = clickUser.lastName;
            if(!string.IsNullOrEmpty(clickUser.mobilePhone))
                user.MobilePhone = clickUser.mobilePhone;
            string displayName = $"{clickUser.firstName} {clickUser.lastName}".Trim();
            if(displayName != "")
                user.DisplayName = displayName;
            user.State = "ClickSync";

            if(Program.disableUsers && clickUser.isActive != null)
                user.AccountEnabled = clickUser.isActive;

            try
            {
                Program.WriteLog("d",$"updating user {clickUser.upn}");
                await Program.graphServiceClient.Users[clickUser.upn].PatchAsync(user);
                Program.WriteLog("d",$"updated user {clickUser.upn}");
                Program.usersUpdated++;
                return true;
            }
            catch (Exception ex)
            {
                Program.errors++;
                string str = $"error updating user {clickUser.upn} Error: {ex.Message}";
                Program.WriteLog("e", str);
                db.WriteLog("ERROR", str);
                return false;
            }
        }

        public static async Task<List<string>> CheckUserGroupsInGraph(List<string> groupIds, ClickUser clickUser, DBHelper db){
            try
            {
                var memberships = new List<string>();
                for(int i = 0; i < groupIds.Count; i += 20){
                    var chunk = groupIds.Skip(i).Take(20).ToList();
                    var result = await Program.graphServiceClient.Users[clickUser.clickObjectID]
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
                string str = $"error checking user {clickUser.upn} groups Error: {ex.Message}";
                Program.WriteLog("e", str);
                db.WriteLog("ERROR", str);
                return null;
            }
        }

        public static async Task<bool> RemoveUserFromGroupInGraph(ClickUser clickUser, string groupID, DBHelper db){
            try{
                Program.WriteLog("d",$"removing user {clickUser.upn} from group {groupID}");
                await Program.graphServiceClient.Groups[groupID].Members[clickUser.clickObjectID].Ref.DeleteAsync();
                Program.WriteLog("d",$"removed user {clickUser.upn} from group {groupID}");
                return true;
            }catch(Exception ex){
                Program.errors++;
                string str = $"error removing user {clickUser.upn} from group {groupID} Error: {ex.Message}";
                Program.WriteLog("e", str);
                db.WriteLog("ERROR", str);
                return false;
            }
        }

        public static async Task<bool> DisableUserInGraph(ClickUser clickUser, DBHelper db){
            try{
                Program.WriteLog("d",$"disabling user {clickUser.upn}");
                User user = new User();
                user.AccountEnabled = false;
                await Program.graphServiceClient.Users[clickUser.clickObjectID].PatchAsync(user);
                Program.WriteLog("d",$"disabled user {clickUser.upn}");
                return true;
            }catch(Exception ex){
                Program.errors++;
                string str = $"error disabling user {clickUser.upn} Error: {ex.Message}";
                Program.WriteLog("e", str);
                db.WriteLog("ERROR", str);
                return false;
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