using Avalonia.Controls.Shapes;
using File_test_client.workers;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace File_Client_Gui.workers
{
    public class Logger_worker : Worker_base
    {
        public Channel<string> msgs_to_write;


        public string file_path;

        


        public Logger_worker(Client_main ow,string fp) : base(ow) 
        {
            System.IO.Directory.CreateDirectory("logs");
            file_path = fp;
            msgs_to_write = Channel.CreateUnbounded<string>();

            Task.Run(write_to_log);
        }

        public void add_msg(string msg)
        {
            msgs_to_write.Writer.TryWrite($"{DateTime.Now:HH:mm:ss} - {msg}");
        }


        public async void write_to_log()
        {
            await foreach (var msg in msgs_to_write.Reader.ReadAllAsync())
            {
                try
                {
                    await File.AppendAllLinesAsync(file_path, new[] { msg });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error writing log:");
                    Debug.WriteLine(ex.Message);
                }
            }
        }


        public override void begin()
        {
            
        }


        public override void pair_up()
        {
            begin();
        }
    }
}
