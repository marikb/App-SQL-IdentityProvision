using System;
using System.Data.SqlClient;

namespace ClickSync
{
    class ClickUser{
        public string tz { get; set; }
        public string firstName { get; set; }
        public string lastName { get; set; }
        public string mobilePhone { get; set; }
        public DateTime retirementDate {get; set; } 
        public bool isActive {get;set;}
        public bool clickSynced {get;set;}
        public string clickObjectID { get; set; }

        public ClickUser(SqlDataReader reader)
        {
            this.tz = reader["TZ"].ToString();
            this.firstName = reader["FirstName"].ToString();
            this.lastName = reader["LastName"].ToString();
            this.mobilePhone = reader["MobilePhone"].ToString();
            this.clickObjectID = reader["ClickObjectID"].ToString();

            int colIndex = reader.GetOrdinal("RetirementDate");
            if(!reader.IsDBNull(colIndex))
                this.retirementDate = reader.GetDateTime(colIndex);

            colIndex = reader.GetOrdinal("isActive");
            if(!reader.IsDBNull(colIndex))
                this.isActive = reader.GetBoolean(colIndex);

            colIndex = reader.GetOrdinal("ClickSynced");
            if(!reader.IsDBNull(colIndex))
                this.clickSynced = reader.GetBoolean(colIndex);

        }
    }
}