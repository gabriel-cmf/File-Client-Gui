using LiteDB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace File_Client_Gui.data
{
    public class Server_registry
    {
        [BsonId]
        public string ip_ad { get; set; }

        public DateTime first_contact { get; set; }

        [BsonIgnore]
        public IPEndPoint? server_endpoint;

        public Server_registry() 
        { 
            ip_ad = "";
            first_contact = DateTime.Now;
            
        }

        public Server_registry(IPEndPoint iep)
        {
            ip_ad = iep.ToString();
            server_endpoint = iep;
            first_contact = DateTime.Now;

        }

        public IPEndPoint get_endpoint()
        { 
             if(server_endpoint == null)
             {
                  server_endpoint = IPEndPoint.Parse(ip_ad);
             }

             return server_endpoint;
        }
    }
}
