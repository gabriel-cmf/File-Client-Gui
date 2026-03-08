using Avalonia.Controls.Shapes;
using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace File_Client_Gui.data
{
    public class File_registry
    {
        //public int Id { get; set; }
        [BsonId]
        public string file_hash { get; set; }

        public string registry_name {  get; set; }
        public string file_name { get; set; }
        public string file_type { get; set; }
        public string full_file_name { get; set; }
        public string folder { get; set; }
        public string complete_path { get; set; }
        public long filesize { get; set; }

        [BsonIgnore]
        public IPAddress? origin_server_ip {get; set; }

        [BsonIgnore]
        public Server_registry? origin_server;

        [BsonIgnore]
        public File_registry_info fr_info;


        public File_registry()
        {
            registry_name = string.Empty;
            file_name = string.Empty;
            filesize = 0;
            full_file_name = string.Empty;
            folder = string.Empty; 
            complete_path = string.Empty;
            file_type = string.Empty;
            file_hash = string.Empty;
            fr_info = new File_registry_info();
            origin_server_ip = null;
            update_fr_info();

        }


        public void set_file_status()
        {

            if(!File.Exists(complete_path))
            {
                update_fr_info("Missing");
            }
            else
            {
                try
                {
                    using(var stream = File.OpenRead(complete_path))
                    {
                        update_fr_info("Valid");
                    }
                }
                catch
                {
                    update_fr_info("Unreadable");
                }
            }
        }

        public void update_fr_info()
        {
            fr_info.File_name = file_name;
            fr_info.File_size = Client_main.bytesize_to_str(filesize);
            fr_info.Path = complete_path;
            fr_info.Status = "Valid";
            is_valid();
        }

        public void update_fr_info(string file_status)
        {
            fr_info.File_name = file_name;
            fr_info.File_size = Client_main.bytesize_to_str(filesize);
            fr_info.Path = complete_path;
            fr_info.Status = file_status;
            is_valid();
        }

        public bool is_valid()
        {
            bool valid = false;
            try
            {
                using (FileStream fs = new FileStream(complete_path, FileMode.Open, FileAccess.Read))
                {

                }
                valid = true;
                fr_info.Status = "Valid";
            }
            catch
            {
                fr_info.Status = "File not found";
            }
            return valid;
        }

    }
}
