using File_test_client.workers;
using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace File_Client_Gui.workers
{

    public enum hash_receive_state
    {
        waiting_for_info,
        waiting_for_hashes,
        done
    }
    public struct raw_data_packet
    {
        public byte[] data;
        public int size;
    }


    public class Transfer_hash_receive_worker : Worker_base
    {

        public BlockingCollection<raw_data_packet> raw_packets;
        public byte[] file_buffer;
        public EndPoint ed_trash;
        public long received_packets;
        public long expected_packets;

        public List<string> received_hashes;
        public bool write_started;
        public hash_receive_state state;

        public Transfer_hash_receive_worker(Client_main cm) : base(cm)
        {
            raw_packets = new BlockingCollection<raw_data_packet>();
            file_buffer = new byte[65536];
            state = hash_receive_state.waiting_for_info;
            ed_trash = new IPEndPoint(IPAddress.Any, 0);
            received_hashes= new List<string>();
        }

        public void  receive_hash_info()
        {
            byte[] info =  udp_agent.Receive(ref trash);
            Dictionary<string, object>? info_dict = Client_main.bytes_to_dict(info);

            if(info_dict != null && info_dict.ContainsKey("packets"))
            {
                expected_packets = Convert.ToInt64(info_dict["packets"]);
                log_message("Expected packets to receive: " + expected_packets);

                byte[] ack = Encoding.ASCII.GetBytes("INF");
                udp_agent.Send(ack,target);

                state = hash_receive_state.waiting_for_hashes;
            }
            else
            {
                log_message("Failed to receive hash info, giving up...");
                state = hash_receive_state.done;
                
            }
        }
        public void read_raw_data()
        {
            const int sha_256_hash_size = 32;
            foreach (var item in raw_packets.GetConsumingEnumerable())
            {
                int reps = item.size / sha_256_hash_size;

                for (int i = 0; i < reps; i++)
                {
                    string st = Convert.ToHexString(item.data, i * sha_256_hash_size, sha_256_hash_size);
                    received_hashes.Add(st);

                    

                }

                ArrayPool<byte>.Shared.Return(item.data);
            }
        }

        public void receive_raw_data()
        {
            int ps = 0;
            bool hit = false;



            try
            {
                ps = udp_agent.Client.ReceiveFrom(file_buffer, SocketFlags.None, ref ed_trash);
                hit = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error in receive_raw_data");
                Debug.WriteLine(ex.Message);
            }

            if (hit == true && ps > 0)
            {
                byte[] raw_data = ArrayPool<byte>.Shared.Rent(ps);

                Array.Copy(file_buffer, 0, raw_data, 0, ps);

                raw_data_packet rdp = new();
                rdp.data = raw_data;
                rdp.size = ps;

                raw_packets.Add(rdp);
               
                received_packets++;





            }


            if (received_packets == expected_packets)
            {
                log_message("Hash transfer has concluded");
               
                raw_packets.CompleteAdding();
                state = hash_receive_state.done;
                owner.urgent_worker_queque.Enqueue(this);
                log_message("Hashes found: " + received_hashes.Count);
            }

            else
            {
                raw_packets.CompleteAdding();
                log_message("Hash transfer has failed, giving up...");
            }




        }

        public bool start_read_raw_data()
        {
            log_message("Starting to read raw data");
            if (write_started) { return false; }
            write_started = true;
            Task.Factory.StartNew(() => { read_raw_data(); });
            return true;

        }

        
        public void transfer_hash_receive_worker_loop()
        {
            while (true)
            {
               switch(state)
                {

                    case hash_receive_state.waiting_for_info:
                        {
                            receive_hash_info();
                            break;
                        }
                    case hash_receive_state.waiting_for_hashes:
                        {
                            
                            bool start = start_read_raw_data();
                            receive_raw_data();
                            break;
                        }
                        
                    case hash_receive_state.done:
                        {
                            break;
                        }
                       
                }
                
                Thread.Sleep(0);
            }
        }


        public override void begin()
        {
            Debug.WriteLine("Starting hash transfer receive");
            log_message("Starting hash transfer receive");
            transfer_hash_receive_worker_loop();

        }

        //Inverse pair-up procedure, since the client is the one receiving
        /*public override void pair_up()
        {
            bool paired = false;
            string ack = "ACK";
            var json_dict = JsonConvert.SerializeObject(ack);
            var j_bytes = Encoding.ASCII.GetBytes(json_dict);
            //udp_agent.Connect(target);

            log_message("Attempting to pair-up with server...");
            while (!paired)
            {

                try
                {
                    udp_agent.Send(j_bytes, target);
                    log_message("ACK sent to server");

                    //var resp = udp_agent.Receive(ref trash);
                    var resp = udp_agent.Receive(ref trash);
                    log_message("Response received from server");
                    if (resp != null && resp.Length > 0)
                    {
                        paired = true;
                        break;
                    }
                }
                catch (Exception e)
                {
                    log_message("Error ocurred in pair-up:");
                    log_message(e.Message);

                }

                Thread.Sleep(0);
            }

            log_message("Pair-up with server successful !");
            begin();
        }*/
    }
}
