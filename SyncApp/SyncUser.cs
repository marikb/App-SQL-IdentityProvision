using System;
using Microsoft.Data.SqlClient;

namespace SyncApp
{
    class SyncUser{
        public string tz { get; set; }
        public string firstName { get; set; }
        public string lastName { get; set; }
        public string mobilePhone { get; set; }
        public DateTime retirementDate {get; set; }
        public bool? isActive {get;set;}
        public bool synced {get;set;}
        public string aadObjectID { get; set; }
        public byte[] rowVer { get; set; }
        public string upn { get { return $"{tz}{Program.userPrincipalNameSuffix}"; } }

        public SyncUser(SqlDataReader reader)
        {
            this.tz = reader["TZ"].ToString();
            this.firstName = reader["FirstName"].ToString();
            this.lastName = reader["LastName"].ToString();
            this.mobilePhone = reader["MobilePhone"].ToString();
            this.aadObjectID = reader["AADObjectID"].ToString();
            this.rowVer = (byte[])reader["RowVer"];

            int colIndex = reader.GetOrdinal("RetirementDate");
            if(!reader.IsDBNull(colIndex))
                this.retirementDate = reader.GetDateTime(colIndex);

            colIndex = reader.GetOrdinal("isActive");
            if(!reader.IsDBNull(colIndex))
                this.isActive = reader.GetBoolean(colIndex);

            colIndex = reader.GetOrdinal("Synced");
            if(!reader.IsDBNull(colIndex))
                this.synced = reader.GetBoolean(colIndex);

        }
    }
}