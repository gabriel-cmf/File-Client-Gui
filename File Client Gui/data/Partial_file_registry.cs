using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace File_Client_Gui.data
{
    /// <summary>
    /// Class that represents a partial file registry, which is used to store information about files that have to be downloaded in parts. 
    ///
    /// We assume this class will be created before the file transfer begins, so it can store information about the interrupted transfer incase the program is closed or crashes. This way, when the program is restarted, it can check the partial file registry and resume any incomplete downloads.
    
    
    public class Partial_file_registry
    {
        [BsonId]
        public string hash { get; set; }

        public string path { get; set; }
        public string complete_filename { get; set; }
        public long expected_size { get; set; }
        public long total_packets { get; set; }


        [BsonIgnore]
        public Partial_file_info? pfi;

        public Partial_file_registry() 
        { 
           hash = path = string.Empty;
           complete_filename = string.Empty;
           expected_size = 0;
           total_packets = 0;
           pfi = new();
        }

        public Partial_file_registry(string h, string p, long size)
        {
            hash = h;
            path = p;
            expected_size = size;
            pfi = new();
            pfi.registry = new WeakReference<Partial_file_registry>(this);
        }

        public byte get_progress()
        {
            if (expected_size <= 0) { return 0; }
            long current_size = new FileInfo(path).Length;
            double progress = (double)current_size / expected_size;
            return (byte)(progress * 100);
        }

        public void update_pfi()
        {
            if (pfi == null)
            {
                pfi = new();
            }
            pfi.File_name = Path.GetFileName(path);
            pfi.File_size = Client_main.bytesize_to_str(expected_size);
            pfi.Path = path;
            if(pfi.registry == null || !pfi.registry.TryGetTarget(out _))
            {
                pfi.registry = new WeakReference<Partial_file_registry>(this);

            }
                
        }

        public void toggle_selected()
        {
            pfi.Selected = true;
        }
    }
}
