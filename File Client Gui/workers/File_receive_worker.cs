using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
using File_Client_Gui.data;

using Avalonia.Metadata;
using System.Security.Cryptography;
using File_Client_Gui.ViewModels;
using File_Client_Gui.Views;
using File_Client_Gui;

namespace File_test_client.workers
{

    public enum received_state : int
    {
        start = 0,
        file_requested = 1,
        sever_answered = 2,
        file_data_ready = 3,
        file_received = 4,
        file_done = 5,
        file_not_found = 6,
        file_error = 7,

    }



    public struct w_packet
    {
        public byte[] data;
        public long order;
        public long sum;
        public int write_size;
    }

    public class packet_range
    {
        public long begin;
        public long end;
    }

    public class File_receive_worker : Worker_base
    {

        public string wanted_file_name;
        public string wanted_file_ext;
        public string full_file_name;
        public int received_count;

        public bool ready_for_file;
        public int filesize;
        public int received_filesize;
        public BlockingCollection<w_packet> packets_to_write;
        
        public received_state r_state;
        public long packets_needed;
        public EndPoint ep;
        public byte[] file_buffer;
        private delegate void file_status(string st);
        public Worker_task_info wti;
        Worker_task_info wtx;
        public List<packet_range> packets_received;
        public Stopwatch last_received_time;
        public bool build_started;
        public bool skip_detected;
        private object lock_x;
        public Task builder_task;
        public IncrementalHash gen_hash;
        public string target_hash;
        public string download_folder;
        



        public void add_r_packet(long l)
        {
            //Debug.WriteLine("Number to check:");
            //Debug.WriteLine(l);
           
            if (packets_received.Any() ==false)
            {
                //Debug.WriteLine("First range");
                
                packets_received.Add(new packet_range { begin=l,end=l});
            }

            else
            {

                //Debug.WriteLine("First packet:");
                //Debug.WriteLine(packets_received[0]);

                int i = 0;
                bool added = false;
                bool done =false;

                while (i < packets_received.Count) 
                { 
                    packet_range p = packets_received[i];

                    if(l>p.end)
                    {
                        //Debug.WriteLine("New number is bigger");
                        //Debug.WriteLine(l-p.Item2);
                        if (l - p.end==1)
                        {
                            //Debug.WriteLine("Incrementing p.Item2");
                            p.end = p.end+1;
                            //packets_received[i] = p;
                            added =true;
                            done = true;
                            break;
                        }

                        else
                        {
                            //Debug.WriteLine("Moving to new range");
                            i++;
                            skip_detected=true;
                            continue;
                        }
                    }

                    if (done) { break; }

                    //Debug.WriteLine("Break failed");

                    if (p.begin > 0 && l < p.begin)
                    {
                        if (p.begin - l == 1)
                        {
                            p.begin = p.begin - 1;
                            packets_received[i] = p;
                            added = true;
                            done = true;
                            break;
                        }

                        else
                        {
                            done = true;
                            skip_detected = true;
                            break;
                        }
                    }

                    
                    added=true;
                    break;
                }

                if(added == false)
                {
                    //Debug.WriteLine("Creating new range");
                    packet_range p = new packet_range { begin = l, end = l };
                    packets_received.Insert(i,p);
                }

                merge_packets();
            }
        }


        public void merge_packets()
        {
            int i = 0;

            if(packets_received.Count <= 1)
            {
                return;
            }

            while (i < packets_received.Count-1) 
            {
                packet_range p = packets_received[i];
                packet_range p2 = packets_received[i+1];

                if (p.end == p2.begin || p.end +1  == p2.begin) 
                { 
                    p.end = Math.Max(p2.end,p.end);

                   
                    //packets_received[i] = p;
                    packets_received.RemoveAt(i+1);
                    continue;

                
                }

                i++;
            }
        }

        public bool received_all_packets()
        {


            if(!packets_received.Any())
            {
                return false;
            }


            //Debug.WriteLine(packets_received[0].end);
            if (packets_received.Count == 1 ) 
            {
               
                
                return packets_received[0].end +1 == packets_needed; 
            }

            return false;
        }

        public string get_hash_string()
        {
            byte[] raw_hash = gen_hash.GetCurrentHash();
            string st = Convert.ToHexString(raw_hash, 0, raw_hash.Length);
            return st;
        }

        public File_receive_worker(Client_main ow) : base(ow)
        {


            bool allows_IPv6 = System.Net.Sockets.Socket.OSSupportsIPv6;
            if (allows_IPv6)
            {
                log_message("System is ipv6");
                //udp_agent = new UdpClient(AddressFamily.InterNetworkV6);
                ep = new IPEndPoint(IPAddress.Any, 1);
            }
            else
            {
                log_message("System is ipv4 only");
                ep = new IPEndPoint(IPAddress.Any, 1);

            }



            packets_to_write = new BlockingCollection<w_packet>();
            packets_needed = 0;
            file_buffer = new byte[65536];
            var ips = Dns.GetHostAddresses(Dns.GetHostName())[0];
           
            ready_for_file = false;
            received_filesize = 0;
            udp_agent.AllowNatTraversal(true);
            udp_agent.Client.ReceiveBufferSize = 1024 * 1024 * 2;
            udp_agent.Client.ReceiveTimeout = 3000;
            packets_received = new();
            last_received_time = new();
            build_started=false;
            skip_detected=false;
            lock_x = new();
            gen_hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            download_folder = String.Empty;

        }

        public void create_wti(Worker_task_info wt)
        {
            wti = wt;
        }

        public void ask_for_file()
        {



            Dictionary<string, object> data = new();
            data["type"] = 2;
            data["filename"] = wanted_file_name;
            data["file_ext"] = wanted_file_ext;
            data["file_code"] = owner.client_code;

            var json_dict = JsonConvert.SerializeObject(data);



            var j_bytes = Encoding.ASCII.GetBytes(json_dict);
            bool hit = true;

            try
            {
                udp_agent.Send(j_bytes, target);
            }
            catch (Exception e) 
            {
                Debug.WriteLine(e);
                //change_file_status("Could not connect to the server");
                hit = false;
            }

            if(hit)
            {
                change_file_status("Connected to the server !");
                r_state = received_state.file_requested;
            }
            
           




        }

        public void ask_for_file2()
        {
            Dictionary<string, object> data = new();
            data["type"] = 2;
            data["hash"] = target_hash;
            data["client_code"] = owner.client_code;
            log_message("Asking for file");


            var json_dict = JsonConvert.SerializeObject(data);
            var j_bytes = Encoding.ASCII.GetBytes(json_dict);
            bool hit = true;

            log_message(udp_agent.Client.AddressFamily.ToString());
            log_message("Target:");
            log_message(target.ToString());


            try
            {
                udp_agent.Send(j_bytes, target);
            }
            catch (Exception e)
            {
                hit = false;
                log_message("Error:");
                log_message(e.Message);
                change_file_status("Request could not be sent to the server");
                
            }

            if (hit)
            {
                log_message("Message was sent to server");
                change_file_status("Request was sent to the server");
                r_state = received_state.file_requested;
            }
        }

        public void file_setup()
        {
            log_message("File setup, Waiting for broadcast");



            byte[] bytes = Array.Empty<byte>();

            try
            {
                bytes = udp_agent.Receive(ref trash);
            }
            catch(Exception e)
            {
                log_message("An error has ocurred in ask_for_file2:");
                r_state = received_state.file_error;
                log_message(e.Message);
                return;
                
            }



            log_message($"Received broadcast from {trash} :");
            string b_st = Encoding.ASCII.GetString(bytes, 0, bytes.Length);
            log_message($"Size: {bytes.Length}");
            log_message($" {b_st}");

            Dictionary<string, object>? dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(b_st);

            if (dict == null || !((bool)dict["found"]))
            {
                r_state = received_state.file_not_found;
                Console.WriteLine("File not found or invalid answer!");
                change_file_status("File was not found !");
                return;
            }

           




            long filesize64 = (long)dict["filesize"];
            filesize = (int)filesize64;


            packets_needed = (long)dict["packets"];
            int pn = (int)packets_needed;

            wanted_file_ext = (string)dict["file_ext"];
            wanted_file_name = (string)dict["file_name"];
            full_file_name = wanted_file_name + wanted_file_ext;


            received_count = 0;

            //target_hash = (string)dict["hash"];


            wti.Name = full_file_name;
            //wti.Filesize = packets_needed.ToString();
            wti.Filesize = Client_main.bytesize_to_str( filesize );


            bool do_download = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new File_confirm();
                var vm = new File_confirm_Model();
                vm.Packets = packets_needed;
                vm.File_name = full_file_name;
                dialog.DataContext = vm;

                bool res =  await dialog.ShowDialog<bool>(owner.main_window);
                

                if(res)
                {
                    download_folder = vm.Folder_path + "\\";
                }

                return res;
            }).Result;

            if(do_download)
            {
                byte[] ack_bytes = Encoding.ASCII.GetBytes("ACK");
                udp_agent.Send(ack_bytes, target);
                packets_to_write = new();
                r_state = received_state.sever_answered;
                log_message("File found");
                change_file_status("File was found!");
                log_message("Starting file builder");
                change_file_status("Starting file builder");

            }
            else
            {
                r_state = received_state.file_not_found;
                log_message("Download canceled!");
                change_file_status("Download canceled");
                return;
            }

            //conf.Wait();
        }


        public void ask_for_missing()
        {
            if(packets_received.Count <= 1) { return; }

            Dictionary<string, object> dict = new();
            dict["done"] = false;
            dict["pos"] = 0;

            for (int a=0;a<packets_received.Count-1;a++)
            {
                packet_range p= packets_received[a];
                packet_range p2 = packets_received[a+1];

                if(p2.begin - p.end > 1)
                {
                    for(long b = p.end+1; b < p2.begin; b++)
                    {
                        dict["pos"] = b;
                        byte[] resp = Client_main.dict_to_bytes(dict);
                        if (resp != null) 
                        {
                            udp_agent.SendAsync(resp,target);
                        }
                    }
                }
            }
        }

        public void register_file()
        {
            File_registry fr = new();
            fr.filesize = filesize;
            fr.file_name = wanted_file_name;
            fr.file_type = wanted_file_ext;
            fr.complete_path = download_folder + full_file_name;
            fr.folder = download_folder;
            fr.file_hash = target_hash;
            fr.origin_server_ip = target.Address;

            owner.add_file_to_db(fr);
        }

        

        public void receive_file_data(Task file_write)
        {

            stopwatch.Start();

            //byte[] packet = udp_agent.Receive(ref trash);

            bool hit = false;
            int ps = 0;

            

            try
            {
               
                
                ps = udp_agent.Client.ReceiveFrom(file_buffer, SocketFlags.None, ref ep);
                //ps = udp_agent.Receive();
                change_file_status("Packet received");
                hit=true;
            }
            catch (Exception e) 
            {
                //change_file_status(e.Message);
                //Debug.WriteLine(e.ToString());
                if(e is SocketException se && se.SocketErrorCode == SocketError.TimedOut)
                {
                    log_message("Received timed out");
                    
                }
                else
                {
                    log_message("Error ocurred in receive_file_data");
                    log_message(e.HResult.ToString());
                    log_message(e.Message);
                    r_state = received_state.file_error;

                    return;
                }

               
            }

          
            if (hit&&file_buffer != null && file_buffer.Length > 0)
            {
                

                
                last_received_time.Stop();
                //long l_order = Convert.ToInt64(dict["order"]);
                long l_order = BitConverter.ToInt64(file_buffer, 0);
               
                long l_pos = BitConverter.ToInt64(file_buffer, 8);

                int chunk_size = ps - 16;
                //int pos = index;

                //byte[] file_data  = new byte[chunk_size];
                byte[] file_data = ArrayPool<byte>.Shared.Rent(chunk_size);

                Array.Copy(file_buffer, 16, file_data, 0, chunk_size);

                w_packet wp = new w_packet();
                wp.data = file_data;
                wp.order = l_order;
                wp.write_size = chunk_size;


                gen_hash.AppendData(file_data, 0, chunk_size);


                add_r_packet(l_pos);
                packets_to_write.Add(wp);


                

                //byte[]? res = JsonConvert.DeserializeObject<byte[]>((string)dict["bytes"]);
                //byte[]? res = (byte[])dict["bytes"];
                //Console.WriteLine(res);
                /*if (res != null)
                {
                    dict["bytes"] = res;
                }*/
                //received_packets[index] = dict;
                received_count++;
                received_filesize += chunk_size;
                var pr = (received_filesize / (float)filesize) * 100;
                wti.Progress = MathF.Round(pr,2);

                last_received_time.Restart();

                //Console.WriteLine("Receiving file:");
                //Console.WriteLine("Receiving file:" + $"{l_pos}");
                //Debug.WriteLine("Receiving file:" + $"{l_pos}");

            }


            


            //Console.WriteLine($"Size: {packet.Length}");


            if (received_all_packets())
            {
                change_file_status("All packets were received!");
                stopwatch.Stop();
                packets_to_write.CompleteAdding();
                build_started = false;
                file_write.Wait();
                file_write.Dispose();
                FileInfo fileInfo = new FileInfo(download_folder + full_file_name);

                log_message("All packets were received!");
                log_message("Transfer time (s): ");
                log_message((stopwatch.ElapsedMilliseconds / 1000).ToString());
                log_message("Bytes received is filesize");
                log_message((received_filesize == filesize).ToString());
                log_message((fileInfo.Length == filesize).ToString());
                log_message((filesize - received_filesize).ToString());

                log_message("Generated hash");

                string h_st = get_hash_string();
                Debug.WriteLine(h_st);
                wti.Hash_match = (h_st==target_hash);

                Dictionary<string,object> confm = new Dictionary<string,object>();
                confm["done"] = true;
                confm["pos"] = 0;

                var jbytes = Client_main.dict_to_bytes(confm);
                log_message("File transfer is done!");
                udp_agent.Send(jbytes,target);

                register_file();

                r_state = received_state.file_data_ready;
                
                received_filesize = 0;
                
                last_received_time.Restart();



            }
            else
            {
                //Debug.WriteLine(last_received_time.Elapsed);
                if (skip_detected && last_received_time.ElapsedMilliseconds >= 2000)
                {
                    skip_detected = false;
                    last_received_time.Restart();
                    log_message("Searching for missing packets");
                    ask_for_missing();
                    
                }
            }
        }

        public void build_file()
        {
            int tries = 0;

            /*if(build_started)
            {
                return;
            }*/
            
            lock(lock_x)
            {
                try
                {
                    using (FileStream fs = new FileStream(download_folder + full_file_name, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                    {

                        tries = 0;
                        foreach (w_packet wp in packets_to_write.GetConsumingEnumerable())
                        {
                            long pos = wp.order;

                            fs.Position = pos;

                            fs.Write(wp.data, 0, wp.write_size);

                            if (wp.order % 100 == 0)
                            {
                                fs.Flush(true);
                            }


                            ArrayPool<byte>.Shared.Return(wp.data);
                            /*if(received_count >= packets_needed)
                            {
                                fs.Flush(true);
                                break;
                            }*/
                        }
                        fs.Flush(true);
                        log_message("File built in build2 !");



                    }

                }
                catch (IOException)
                {
                    if (tries > 5)
                    {

                        return;
                    }
                    Console.WriteLine("file acess failed!");
                    Thread.Sleep(500);
                    tries++;
                }
            }
        }

        public void start_file_builder()
        {
            if(build_started) { return; }
            build_started = true;
            builder_task = Task.Factory.StartNew(() => { build_file(); });
            last_received_time.Start();
        }

        public void file_receive_state_machine()
        {
            bool done = false;

            while (!done)
            {
                switch (r_state)
                {
                    case (received_state.file_requested):
                        {
                            file_setup();
                            break;
                        }

                    case (received_state.sever_answered):
                        {


                            
                            int availableWorkerThreads, availableCompletionPortThreads;
                            ThreadPool.GetAvailableThreads(out availableWorkerThreads, out availableCompletionPortThreads);

                            
                            //build_started = true;
                            start_file_builder();
                            //last_received_time.Start();
                            receive_file_data(builder_task);

                            break;
                        }

                    case (received_state.file_data_ready):
                        {
                            //build_file();
                            r_state = received_state.file_received;
                            break;
                        }

                    case (received_state.file_received):
                        {
                            //build_file();
                            
                            done = true;

                            break;
                        }

                    case (received_state.file_not_found):
                        {
                            done = true;
                            break;
                        }

                    case (received_state.file_error):
                        {
                            change_file_status("Error ocurred in file transfer! It will stop");
                            log_message("File trasnfer stopped because of an error");
                            done = true;
                            break;
                        }
                }

                Thread.Sleep(0);
            }

        }

        public override void begin()
        {
            file_receive_state_machine();
        }

        public void change_file_status(string st)
        {
            Dispatcher.UIThread.Post(() => owner.set_filestatus(st));
        }

    }
}
