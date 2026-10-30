using File_test_client.workers;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace File_Client_Gui.workers
{

    public struct hash_report
    {
        public byte[] data;
        public int size;
    }

    public enum hash_report_state
    {
        sending_info,
        sending_hashes,
        done
    }

    public class Hash_report_worker : Worker_base
    {
        public BlockingCollection<hash_report> packets_to_send;
        public List<string> hashes;
        public bool sender_started;
        public hash_report_state state;

        public Hash_report_worker(Client_main ow) : base(ow)
        {
            packets_to_send = new();
            sender_started = false;
            hashes = new List<string>();
            state = hash_report_state.sending_info;
        }

        public int calculate_total_packets()
        {
            const int max_packet_size = 1440;
            if (hashes.Count == 0) { return 0; }
            byte[] sample_bytes = Convert.FromHexString(hashes[0]);
            int sample_size = sample_bytes.Length;
            double double_total_reps = Math.Ceiling(((double)(sample_bytes.Length + 1) * hashes.Count) / max_packet_size);
            int total_reps = (int)double_total_reps;
            return total_reps;
        }

        public void inform_server()
        {
            log_message("Informing server about incoming hash transfer...");
            Dictionary<string, object> dict = new Dictionary<string, object>();
            dict["packets"] = calculate_total_packets();

            byte[] j_bytes = Client_main.dict_to_bytes(dict);
            udp_agent.Send(j_bytes, target);

            
            byte[] resp = udp_agent.Receive(ref trash);

            Debug.WriteLine("Server was informed");
            state = hash_report_state.sending_hashes;




        }

        public void write_packets()
        {
            log_message("Writing hash report packets...");
            if (hashes.Count == 0)
            {
                log_message("No hashes to send, skipping...");
                return;
            }


            int i = 0;
            const int max_packet_size = 1440;
            int packet_left = max_packet_size;

            byte[] sample_bytes = Convert.FromHexString(hashes[0]);
            int sample_size = sample_bytes.Length;
            //int total_reps = (sample_bytes.Length*hashes.Length)/max_packet_size;
            double double_total_reps = Math.Ceiling(((double)(sample_bytes.Length + 1) * hashes.Count) / max_packet_size);
            int total_reps = (int)double_total_reps;

            for (int a = 0; a < total_reps; a++)
            {
                byte[] data = ArrayPool<byte>.Shared.Rent(max_packet_size);
                int inner_reps = Math.Min(max_packet_size / sample_size, hashes.Count - i);
                for (int b = 0; b < inner_reps; b++)
                {
                    byte[] hex_bytes = Convert.FromHexString(hashes[b + i]);
                    byte hash_perc = owner.get_partial_file_progress(hashes[b + i]);


                    Array.Copy(hex_bytes, 0, data, i * (sample_size), sample_size);
                    data[(i * sample_size) + sample_size] = hash_perc;

                    

                }
                hash_report hr = new();
                hr.data = data;
                hr.size = (inner_reps * (sample_size+1));

                
                packets_to_send.Add(hr);
                i += inner_reps;
            }
            
            Debug.WriteLine("All packets where written");
            packets_to_send.CompleteAdding();
            state = hash_report_state.done;

        }

        public void start_send_packets()
        {
            if (!sender_started)
            {
                sender_started = true;
                Task.Run(() => { send_packets(); });
                
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

            Debug.WriteLine("All packets sent");


        }

        public void hash_transfer_state_machine()
        {
            
            while (state != hash_report_state.done)
            {
                switch (state)
                {
                    case hash_report_state.sending_info:
                        {
                            inform_server();
                            break;
                        }
                       
                    case hash_report_state.sending_hashes:
                        {
                            start_send_packets();
                            write_packets();
                           
                            
                            break;
                        }


                    case hash_report_state.done:
                        {
                            log_message("Hash report transfer complete.");
                            break;
                        }
                }
            }
        }

        public override void begin()
        {
            Debug.WriteLine("Beggining hash report transmition");
            //var send_task = Task.Factory.StartNew(() => { start_send_packets(); });
            //write_packets();
            hash_transfer_state_machine();
        }

    }

}
