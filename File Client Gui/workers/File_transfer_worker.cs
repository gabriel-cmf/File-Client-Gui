using File_test_client.workers;
using File_Client_Gui.Views;

using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using File_Client_Gui.data;


namespace File_test_server.workers
{

    public enum server_file_state
    {
        idle = 0,
        file_found = 1,
        transfer_file = 2,
        wait_confirmation = 3,
        done = 4,

    }

    public struct s_packet
    {
        public byte[] data;
        public int size;
    }


    public class File_transfer_worker : Worker_base
    {
        public static int max_udp_size = 65535;
        public long packet_num;
        public server_file_state sv_file_state;
        public int max_transfer;
        public bool send_started;
        public Task send_task;
        public string complete_file_path;
        public string received_hash;
        public Dictionary<string, object> data;
        public BlockingCollection<s_packet> packets_to_send;
        public long begin_packet_index;
        public long end_packet_index;
       

        public File_transfer_worker(Client_main  ow) : base(ow)
        {
            max_transfer = 1024 * 1024;
            sv_file_state = server_file_state.idle;
            send_started = false;
            packets_to_send = new BlockingCollection<s_packet>(1000);
            received_hash = "";
            begin_packet_index = 0;
            end_packet_index = 0;
            udp_agent.Client.ReceiveTimeout = 5000;
            
        }

        public int get_max_packet()
        {
            int max_size = 1400;
            int val = max_size;

            if (target.AddressFamily == AddressFamily.InterNetwork)
            {
                //val -= 28;
                val -= 40;
            }

            if (target.AddressFamily == AddressFamily.InterNetworkV6)
            {
                val -= 40;
            }


            return val;
        }

        public long bytesum(byte[] bytes)
        {
            long sum = 0;

            for (int i = 0; i < bytes.Length; i++)
            {
                sum += bytes[i];

            }


            return sum;

        }

        public long packets_needed(string filepath)
        {
            long ex1 = long.MaxValue;
            //int shave = 28;
            byte[] long_size = BitConverter.GetBytes(ex1);
            Console.WriteLine("long size");
            Console.WriteLine(long_size.Length);
            long data_space = get_max_packet() - (long_size.Length * 2);


            byte[] file_bytes = System.IO.File.ReadAllBytes(filepath);

            packet_num = (file_bytes.Length / data_space) + 1;

            return packet_num;
        }

        public void send_file_found(byte[] msg, IPEndPoint target)
        {

            log_message("Sending file found packet");
            udp_agent.Send(msg, target);

        }


        public void prepare_transfer_data2()
        {
            log_message("Creating client querier");
            //var qr = owner.create_querier_worker(data, complete_file_path);
            byte[] bytes = Array.Empty<byte>();

            try
            {
                bytes = udp_agent.Receive(ref trash);
                sv_file_state = server_file_state.transfer_file;
            }
            catch (TimeoutException e) 
            {
                sv_file_state = server_file_state.done;
            }



            //split_packets2(complete_file_path);

            //Thread.Sleep(200);
            //qr.Wait();
            
        }


        public void send_packets()
        {
            bool linux = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
            int reps = 0;
            foreach (s_packet packet in packets_to_send.GetConsumingEnumerable())
            {
                udp_agent.Send(packet.data, packet.size, target);
                ArrayPool<byte>.Shared.Return(packet.data);
                if (linux && reps % 25 == 0)
                {
                    Thread.Sleep(1);
                }
                reps++;
            }

            log_message("All packets have been sent !");


        }

        public bool start_send_packets()
        {
            if (send_started) { return false; }
            send_started = true;
            log_message("Starting send_packets");
            send_task = Task.Run(() => { send_packets(); });
            
            return true;
        }

        public void make_single_packet(string filepath, long i)
        {
            int mp = get_max_packet();


            long data_space = mp - (8 * 2);
            long offset = data_space * (i);
            int remaining_bytes = 0;
            byte[] buffer = new byte[data_space];
            Console.WriteLine("Single packet offset:");
            Console.WriteLine(offset);

            using (FileStream fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            {

                fs.Seek(offset, SeekOrigin.Begin);

                if ((remaining_bytes = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    Console.WriteLine(fs.Position);

                    int ps = Math.Min(mp, remaining_bytes + 16);
                    byte[] packet = ArrayPool<byte>.Shared.Rent(ps);
                    byte[] order_bytes = BitConverter.GetBytes(offset);
                    byte[] sum_bytes = BitConverter.GetBytes(i);


                    int data_min = Math.Min(buffer.Length, ps);


                    Array.Copy(order_bytes, 0, packet, 0, 8);
                    Array.Copy(sum_bytes, 0, packet, 8, 8);
                    Array.Copy(buffer, 0, packet, 16, data_min);


                    
                    /*s_packet sp = new s_packet();
                    sp.size = ps;
                    sp.data = packet;

                    packets_to_send.Add(sp);*/

                    udp_agent.Send(packet, ps, target);

                    ArrayPool<byte>.Shared.Return(packet);



                }
            }
        }

        public void split_packets(string filepath, long begin_pos=0,long end_pos = long.MaxValue)
        {
            long ex1 = long.MaxValue;

            byte[] long_size = BitConverter.GetBytes(ex1);
            log_message("long size");
            log_message(long_size.Length.ToString());
            int mp = get_max_packet();
            long data_space = mp - (long_size.Length * 2);
            long offset = data_space * (begin_pos);
            log_message("Data space length: " + data_space);
            //byte[] file_bytes = System.IO.File.ReadAllBytes(filepath);
            byte[] buffer = new byte[data_space];

            Debug.WriteLine(begin_pos);
            Debug.WriteLine(end_pos);

            int remaining_bytes = 0;
            using (FileStream fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            {


                long a = 0;
                long file_pos = 0;
                if (offset > 0) 
                {
                    fs.Seek(offset, SeekOrigin.Begin);
                }
                

                while ((remaining_bytes = fs.Read(buffer, 0, buffer.Length)) > 0)
                {

                    Console.WriteLine(remaining_bytes);
                    int ps = Math.Min(mp, remaining_bytes + 16);
                    byte[] packet = ArrayPool<byte>.Shared.Rent(ps);
                    byte[] order_bytes = BitConverter.GetBytes(file_pos);
                    byte[] sum_bytes = BitConverter.GetBytes(a);
                    int i = 0;
                    for (int b = 0; b < order_bytes.Length; b++)
                    {
                        packet[b] = order_bytes[b];
                        i++;

                    }

                    //Console.WriteLine("i:" + $"{i}");

                    for (int b = 0; b < sum_bytes.Length; b++)
                    {
                        packet[i] = sum_bytes[b];
                        i++;

                    }

                    //Console.WriteLine("i:" + $"{i}");

                    int bi = 0;
                    while (i < ps && bi < buffer.Length)
                    {
                        packet[i] = buffer[bi];
                        i++;
                        bi++;
                    }



                    s_packet sp = new s_packet();
                    sp.size = ps;
                    sp.data = packet;


                    packets_to_send.Add(sp);

                    /*if (a!= 33)
                    {
                        packets_to_send.Add(sp);
                    }
                    else
                    {
                        Console.WriteLine("33 offset:");
                        Console.WriteLine(file_pos);
                        Console.WriteLine(fs.Position);
                    }*/

                    Debug.WriteLine(a);
                    a++;
                    file_pos += remaining_bytes;

                    if(a>=end_pos)
                    {
                        Debug.WriteLine("End position exited");
                        Debug.WriteLine(a);
                        Debug.WriteLine(end_pos);
                        break;
                    }


                    //packets_to_send2.Add(packet);
                    //remaining_bytes -= (packet.Length - 16);
                }

                log_message("File read done");
            }


            log_message("All packets have been made !");
            packets_to_send.CompleteAdding();
            send_task.Wait();

            log_message("Remaining_bytes");
            log_message(remaining_bytes.ToString());

            sv_file_state = server_file_state.wait_confirmation;

            //Console.WriteLine(packets_to_send2.Count);

        }

        public void make_packet_range(string filepath, long start, long end)
        {
            for (long i = start; i < end; i++)
            {
                Debug.WriteLine(i);
                make_single_packet(filepath, i);
            }
        }

        public void prepare_data_for_transfer(ref int tries)
        {
            log_message("Preparing data for transfer");
            
            byte[] bytes = Array.Empty<byte>();

            try
            {
                bytes = udp_agent.Receive(ref trash);
            }
            catch (Exception ex) 
            { 
                 tries++;
            }

            if (tries >=6 )
            {
                log_message("Server did not request transfer to start");
                sv_file_state = server_file_state.done;
                return;
            }

            IPEndPoint old_target = target;


            //string b_st = Encoding.ASCII.GetString(bytes, 0, bytes.Length);
            //Dictionary<string, object>? dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(b_st);
            Dictionary<string, object>? dict = Client_main.bytes_to_dict(bytes);

            if (dict != null)
            {
                end_packet_index = (long)dict["end"];
                begin_packet_index = (long)dict["begin"];
                string st_ipe = (string)dict["target"];
                var ips = Dns.GetHostAddresses(Dns.GetHostName())[0];
                IPEndPoint ipe_test = new(ips, 1);

                log_message("Recived order to send packets " + begin_packet_index.ToString() + " to " + end_packet_index.ToString());

                if (IPEndPoint.TryParse(st_ipe, out ipe_test) && ipe_test != null)
                {
                    target = ipe_test;
                    target = new IPEndPoint(target.Address.MapToIPv4(),target.Port);
                    sv_file_state = server_file_state.transfer_file;
                    //begin();
                    
                }

            }

            Dictionary<string, object> conf = new();
            conf["donex"] = true;
            byte[] conf_bytes = Client_main.dict_to_bytes(conf);
            udp_agent.Send(conf_bytes,target);


        }

        public void wait_confirm()
        {
            log_message("Waiting for server confirmation");
            bool finish = false;
            udp_agent.Client.ReceiveTimeout = 5000;
            while (!finish)
            {
                byte[] bytes= Array.Empty<byte>();
                try
                {
                   bytes = udp_agent.Receive(ref trash);
                }
                catch
                {
                    log_message("Error in transfer wait_confirm");
                    continue;
                }
                Dictionary<string, object>? dict = Client_main.bytes_to_dict(bytes);
                if (dict != null)
                {
                    log_message("Preparing to resend packet");
                    bool state = (bool)dict["done"];
                    if (state) { finish = true; break; }

                    //long pos = (long)dict["pos"];
                    long start = (long)dict["start"];
                    long end = (long)dict["end"];

                    //make_single_packet(complete_file_path, pos);
                    make_packet_range(complete_file_path, start, end);
                    //send_packets();


                }

                Thread.Sleep(0);

            }

            log_message("Server has confirmed sucessful file transfer !");

            sv_file_state = server_file_state.done;
            //packets_to_send.Clear();
        }

        public void file_transfer_state_machine()
        {
            bool finish = false;
            while (!finish)
            {

                switch (sv_file_state)
                {
                    case (server_file_state.idle):
                        {
                            //answer_file_query(data);
                            sv_file_state = server_file_state.file_found;
                            break;
                        }

                    case (server_file_state.file_found):
                        {
                            int tries = 0;
                            prepare_data_for_transfer(ref tries);
                            break;
                        }

                    case (server_file_state.transfer_file):
                        {
                            var st = start_send_packets();
                            if (st)
                            {
                                split_packets(complete_file_path,begin_packet_index,end_packet_index);
                            }

                            Console.WriteLine(send_task == null);


                            break;
                        }

                    case (server_file_state.wait_confirmation):
                        {


                            wait_confirm();
                            break;
                        }

                    case (server_file_state.done):
                        {
                            finish = true;
                            break;
                        }
                }
                Thread.Sleep(0);
            }
            Dispose();
        }


        public override void begin()
        {
            file_transfer_state_machine();
        }
    }


}

