using File_test_client.workers;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace File_Client_Gui.workers
{
    public struct data_packet
    {
        public byte[] data;
        public int size;
        
    }

    public  class File_storage_worker: Worker_base
    {
        
        
        public BlockingCollection<data_packet> packets_to_send;
        public bool sender_started;


        public File_storage_worker(Client_main cm): base(cm) 
        { 
             packets_to_send = new BlockingCollection<data_packet>();
             sender_started= false;
        }


        public void write_packets()
        {
            Debug.WriteLine("Writing packets");
            var hashes = owner.get_valid_hashes();
            if (hashes.Length <= 0) { return; }

            int i =0;
            const int max_packet_size = 1440;
            int packet_left = max_packet_size;
            
            byte[] sample_bytes = Convert.FromHexString(hashes[0]);
            int sample_size = sample_bytes.Length;
            //int total_reps = (sample_bytes.Length*hashes.Length)/max_packet_size;
            double double_total_reps = Math.Ceiling( ((double)sample_bytes.Length * hashes.Length) / max_packet_size );
            int total_reps = (int)double_total_reps;

            for (int a=0; a<total_reps; a++)
            {
                byte[] data = ArrayPool<byte>.Shared.Rent(max_packet_size);
                int inner_reps = Math.Min(max_packet_size/ sample_size, hashes.Length-i);

                for(int b=0; b<inner_reps; b++)
                {
                    byte[] hex_bytes = Convert.FromHexString(hashes[b+i]);
                    Array.Copy(hex_bytes,0,data,i*sample_size,sample_size);
                    i++;
                }

                data_packet dp = new data_packet();
                dp.data = sample_bytes;
                dp.size = sample_size*inner_reps;
                packets_to_send.Add(dp);
            }

            packets_to_send.CompleteAdding();
        }


        public void start_send_packets()
        {
            if (!sender_started)
            {
                sender_started=true;
                send_packets();
            }
        }

        public void send_packets()
        {
            Debug.WriteLine("Sending packets");
            foreach (var packet in packets_to_send.GetConsumingEnumerable())
            {
                udp_agent.Send(packet.data, packet.size, target);
                ArrayPool<byte>.Shared.Return(packet.data);
            }
            

        }


        public void add_msg(string msg)
        {
            Debug.WriteLine(msg);
            owner.add_msg(msg);
        }


        public void send_first_msg(byte[] data)
        {
            Debug.WriteLine("Sending first message");
            try
            {
                udp_agent.SendAsync(data, target);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Error while sending first message:\n" + e.Message);
                log_message(e.Message);
            }
        }

        public override void begin()
        {
            Debug.WriteLine("Beggining hash transmition");
            var send_task = Task.Factory.StartNew(() => { start_send_packets(); });
            write_packets();
            
        }
    }
}
