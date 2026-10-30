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
        unpaired=8,
        conection_error=9,
        file_repair = 10,


    }

    public enum server_file_rejection : int
    {
        not_found= 0,
        lacking_credits=1,
        lacking_ratio=2,
        banned=3,

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
        public long filesize;
        public long received_filesize;
        public BlockingCollection<w_packet> packets_to_write;
        
        public received_state r_state;
        public long packets_needed;
        public EndPoint ep;
        public byte[] file_buffer;
        private delegate void file_status(string st);
        public Worker_task_info wti;
        
        public List<packet_range> packets_received;
        public Stopwatch last_received_time;
        public bool build_started;
        public bool skip_detected;
        private object lock_x;
        public Task builder_task;
        public IncrementalHash gen_hash;
        public string target_hash;
        public string download_folder;

        public Partial_file_registry? prf;

        public Dictionary<string, object> packets_per_ip;

        




        public void add_r_packet(long l)
        {
            //Debug.WriteLine("Number to check:");
            //Debug.WriteLine(l);
           
            if (packets_received.Any() ==false)
            {
                Debug.WriteLine("First range");
                
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
            byte[] raw_hash = gen_hash.GetHashAndReset();
            string st = Convert.ToHexString(raw_hash, 0, raw_hash.Length);
            return st;
        }

        public File_receive_worker(Client_main ow) : base(ow)
        {



            r_state = received_state.unpaired;
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
            if (OperatingSystem.IsWindows())
            {
                udp_agent.AllowNatTraversal(true);
            }
                
            udp_agent.Client.ReceiveBufferSize = 1024 * 1024 * 16;
            udp_agent.Client.ReceiveTimeout = 3000;
            packets_received = new();
            last_received_time = new();
            build_started=false;
            skip_detected=false;
            lock_x = new();
            gen_hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            download_folder = String.Empty;
            full_file_name = String.Empty;
            download_folder = String.Empty ;
            prf = null;
            packets_per_ip = new();

        }

        public void create_wti(Worker_task_info wt)
        {
            wti = wt;
        }

        /*public void ask_for_file()
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
            
           




        }*/

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
                log_message("An error has ocurred in ask_for_file2:");
                log_message("Error:");
                log_message(e.Message);
                change_file_status("Request could not be sent to the server");
                r_state = received_state.conection_error;
                return;
                
            }

            if (hit)
            {
                log_message("Message was sent to server");
                change_file_status("Request was sent to the server");
                r_state = received_state.file_requested;
            }
        }


       
        public void create_partial_file_registry()
        {
            prf = new Partial_file_registry(target_hash, download_folder + full_file_name, filesize);
            prf.complete_filename = full_file_name;
            prf.total_packets = packets_needed;
            owner.add_partial_file(prf);
            prf.update_pfi();
        }


        public void partial_file_setup()
        {
            if(prf == null) { return; }
            target_hash = prf.hash;
            filesize = prf.expected_size;

            Dictionary<string, object> data = new();
            data["type"] = 2;
            data["hash"] = target_hash;
            data["client_code"] = owner.client_code;
            data["partial"] = true;

            byte[] dataq  = Client_main.dict_to_bytes(data);

            udp_agent.Send(dataq, target);


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
                log_message("An error has ocurred in file_setup:");
                r_state = received_state.file_error;
                log_message(e.Message);
                return;
                
            }



            log_message($"Received broadcast from {trash} :");
            string b_st = Encoding.ASCII.GetString(bytes, 0, bytes.Length);
            log_message($"Size: {bytes.Length}");
            log_message($" {b_st}");

            Dictionary<string, object>? dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(b_st);


            //Rejection cases
            if (dict == null || !((bool)dict["found"]))
            {
                if(dict==null)
                {
                    log_message("File not found or invalid answer!");
                    return;
                }
                r_state = received_state.file_not_found;

                server_file_rejection sfr = (server_file_rejection)(long)dict["reason"];

                switch(sfr)
                {
                    case server_file_rejection.not_found:
                        change_file_status("File was not found !");
                        break;
                    case server_file_rejection.lacking_credits:
                        change_file_status("Insufficient credit to download this file !");
                        break;
                    case server_file_rejection.lacking_ratio:
                        change_file_status("Insufficient ratio to download this file !");
                        break;
                    case server_file_rejection.banned:
                        change_file_status("You are banned from connecting with the server !");
                        break;
                }

                log_message("File not found, query rejected or invalid answer!");
                //change_file_status("File was not found !");
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


            bool do_download = false;
            Task<bool> download_task = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new File_confirm();
                var vm = new File_confirm_Model();
                vm.Packets = packets_needed;
                vm.File_name = full_file_name;
                vm.File_size = Client_main.bytesize_to_str(filesize);
                dialog.DataContext = vm;

                bool res =  await dialog.ShowDialog<bool>(owner.main_window);
                

                if(res)
                {
                    download_folder = vm.Folder_path + "/";
                }

                return res;
            });

            //udp_agent.Client.ReceiveTimeout = 1000;
            while (!download_task.IsCompleted)
            {
                try
                {
                    //var trashx = udp_agent.Receive(ref trash);
                    //Debug.WriteLine("Received message while waiting for download confirmation");
                }
                catch (SocketException)
                {
                    // Timeout occurred, continue waiting for the download task to complete
                }

                Thread.Sleep(0);
            }

            if(download_task.IsCompleted)
            {
                do_download = download_task.GetAwaiter().GetResult();
            }
           

            //.Result

            if (do_download)
            {
                create_partial_file_registry();
                byte[] ack_bytes = Encoding.ASCII.GetBytes("ACK");
                log_message("Sending confirmation message to: ");
                log_message(target.ToString());

                try
                {
                    udp_agent.Send(ack_bytes, target);
                }

                catch(Exception e)
                {
                    log_message("Error sending confirmation message:");
                    log_message(e.Message);
                    r_state = received_state.file_error;
                    return;
                }

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
            if(packets_received.Any() && packets_received.Count <= 1 && received_all_packets()) 
            {
                log_message("Not enough packets received to request missing ones");
                return; 
            }

            Dictionary<string, object> dict = new();
            dict["done"] = false;
            dict["start"] = 0;
            dict["end"] = 1;

            log_message("Packets_received Count: " + packets_received.Count);

            if(!packets_received.Any())
            {
                dict["done"] = false;
                dict["start"] = 0;
                dict["end"] = packets_needed;
                return;
            }

            
            if(packets_received[0].begin > 0)
            {
                log_message("Requesting first packet");
                byte[] resp = Client_main.dict_to_bytes(dict);
                udp_agent.SendAsync(resp, target);
            }

            if (packets_received.Last().end < packets_needed - 1)
            {
                log_message("Requesting last packet");
                dict["start"] = packets_needed - 1;
                dict["end"] = packets_needed;
                byte[] resp = Client_main.dict_to_bytes(dict);
                udp_agent.SendAsync(resp, target);
            }



            if(packets_received.Count == 1)
            {
                if(packets_received[0].begin > 1)
                {
                    dict["start"] = 1;
                    dict["end"] = packets_received[0].begin;
                    byte[] resp = Client_main.dict_to_bytes(dict);
                    udp_agent.SendAsync(resp, target);
                }

                if (packets_received[0].end < packets_needed - 1)
                {
                    dict["start"] = packets_received[0].end + 1;
                    dict["end"] = packets_needed;
                    byte[] resp = Client_main.dict_to_bytes(dict);
                    udp_agent.SendAsync(resp, target);
                }
                return;
            }


            log_message("Requesting gap packets");
            log_message((packets_received.Count - 1).ToString());
            for (int a=0;a<packets_received.Count-1;a++)
            {
                packet_range p= packets_received[a];
                packet_range p2 = packets_received[a+1];

                if(p2.begin - p.end > 1)
                {
                    /*for(long b = p.end +1; b < p2.begin; b++)
                    {
                        dict["pos"] = b;
                        byte[] resp = Client_main.dict_to_bytes(dict);
                        if (resp != null) 
                        {
                            udp_agent.SendAsync(resp,target);
                        }
                    }*/

                    dict["start"] = p.end + 1;
                    dict["end"] = p2.begin;
                    byte[] resp = Client_main.dict_to_bytes(dict);
                    if (resp != null) 
                    {
                        udp_agent.SendAsync(resp,target);
                    }
                }

                
            }

            /*foreach(packet_range p in packets_received)
            {
                log_message($"Tuple: {p.begin} - {p.end}");
            }*/
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

        public void add_ip_addrs(string ip_addr)
        {
            
            try
            {
                packets_per_ip[ip_addr] = (int)packets_per_ip[ip_addr]+1;
            }
            catch
            {
                packets_per_ip[ip_addr] = 0;
                log_message("Adding ip addr: " + ip_addr);
            }
        }

        public void receive_file_data(Task file_write)
        {

            stopwatch.Start();

            //byte[] packet = udp_agent.Receive(ref trash);

            bool hit = false;
            int ps = 0;
            udp_agent.Client.ReceiveTimeout = 3000;

            

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

                //log_message("Received packet with pos: " + l_pos);




                int chunk_size = ps - 16;
                //int pos = index;

                if(chunk_size <= 0)
                {
                    log_message("Received packet with invalid chunk size, ignoring");
                    return;
                }

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


                if(!ep.Equals(target) && !target.Equals(ep))
                {
                    string? adr = ep.ToString();
                    if(adr!=null)
                    {
                        add_ip_addrs(adr);
                    }
                    
                }

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
                FileInfo? fileInfo = null;
                if (download_folder == String.Empty && full_file_name == String.Empty)
                {
                    if(prf == null) { throw new Exception("No file path available for hash generation!"); }
                    fileInfo = new FileInfo(prf.path);
                }
                else
                {
                    fileInfo = new FileInfo(download_folder + full_file_name);
                }
                

                if(fileInfo == null)
                {
                    throw new Exception("FileInfo was null, this should never be possible");
                }

                log_message("All packets were received!");
                log_message("Transfer time (s): ");
                log_message((stopwatch.ElapsedMilliseconds / 1000).ToString());
                log_message("Bytes received is filesize");
                log_message((received_filesize == filesize).ToString());
                log_message((fileInfo.Length == filesize).ToString());
                log_message((filesize - received_filesize).ToString());

                log_message("Generated hash");

                //string h_st = get_hash_string();
                string h_st = Client_main.generate_incremental_hash(download_folder + full_file_name);
                Debug.WriteLine(h_st);
                wti.Hash_match = (h_st==target_hash);

                Dictionary<string,object> confm = new Dictionary<string,object>();
                confm["done"] = true;
                confm["pos"] = 0;

                var jbytes = Client_main.dict_to_bytes(confm);
                log_message("File transfer is done!");

                bool server_knows = false;
               
               
                //udp_agent.Send(jbytes, target);


                register_file();

                r_state = received_state.file_data_ready;
                
                received_filesize = 0;
                
                last_received_time.Restart();
                if(prf!= null) { owner.remove_partial_file(prf); }
                prf = null;

                log_message("Telling server");
                byte[] repsq = Array.Empty<byte>();
                int attempts = 0;
                while (!server_knows && attempts < 20)
                {
                    try
                    {
                        udp_agent.Send(jbytes, target);
                        repsq = udp_agent.Receive(ref trash);
                        if (repsq.Length > 0)
                        {
                            break;
                        }
                    }
                    catch (Exception e) 
                    {
                        log_message("Server transfer confirm timeout");
                    }
                    Thread.Sleep(200);
                    attempts++;
                }

                log_message("Server has received confirmation of the transfer end");

                if (packets_per_ip.Count > 0 && attempts < 20)
                {
                    log_message("Informing server of packets sent per ip");
                    //Debug.WriteLine(packets_per_ip.Keys);
                    try
                    {
                        udp_agent.Receive(ref trash);
                        byte[] pp_ip = Client_main.dict_to_bytes(packets_per_ip);
                        udp_agent.Send(pp_ip, target);
                    }
                    catch (Exception e)
                    {
                        log_message("Error ocurred at end of file receive");
                        log_message(e.ToString());
                        
                    }
                }




            }
            else
            {
                bool missing_in_order = false;
                if(last_received_time.ElapsedMilliseconds >= 2000)
                {
                    if (!received_all_packets())
                    {
                        missing_in_order =true;
                    }
                }
                //Debug.WriteLine(last_received_time.Elapsed);
                if ((missing_in_order||skip_detected) && last_received_time.ElapsedMilliseconds >= 2000)
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
                    string full_path = download_folder + full_file_name;
                    if(prf != null)
                    {
                        full_path = prf.path;
                    }
                    using (FileStream fs = new FileStream(full_path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
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
            log_message("Starting file builder in File_receive");
            build_started = true;
            builder_task = Task.Run(() => { build_file(); });
            last_received_time.Start();
        }


        public string generate_chunk_hash(long start,long end)
        {
            gen_hash.GetHashAndReset();
            long remaining = end - start + 1;
            using (FileStream fs = new FileStream(prf.path, FileMode.Open, FileAccess.Read))
            {
                fs.Position = start;
                
                
                while (remaining > 0)
                {
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(1440);
                    int read_size = (int)Math.Min(1440, remaining);
                    int read = fs.Read(buffer, 0, read_size);
                    if (read <= 0)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        break;
                    }
                    gen_hash.AppendData(buffer, 0, read);
                    remaining -= read;
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            byte[] raw_hash = gen_hash.GetHashAndReset();
            string st = Convert.ToHexString(raw_hash, 0, raw_hash.Length);
            return st;
        }


        public void repair_request()
        {
            if(prf == null) { return; }
            Dictionary<string, object> dict = new();
            dict["type"] = 7;
            dict["hash"] = prf.hash;
            dict["packets"] = packets_per_chunk();
            dict["client_code"] = owner.client_code;

            byte[] send = Client_main.dict_to_bytes(dict);
            udp_agent.Send(send, target);
        }

        public long packets_per_chunk()
        {
            return Math.Min(1000, packets_needed / 16);
        }

        public void partial_file_repair()
        {

            log_message("Starting partial file repair for file with hash: " + prf.hash);
            if(prf == null) { return; }
            long packets_per_chunk = Math.Min(1000, packets_needed/16);
            long reps = (packets_needed / 16);
            reps += (packets_needed % 16 == 0) ? 0 : 1;
            long remain = prf.expected_size;
            long start = 0;

            packets_received.Clear();

            log_message("Checking " + reps + " chunks");

            long correct_bytes = 0;

            for (long a = 0; a < reps; a++)
            {
                long end = Math.Min(packets_per_chunk * 1440*(a+1), remain);
                string hash = generate_chunk_hash(start, end);
                remain = prf.expected_size - (packets_per_chunk * 1440);

                Dictionary<string,object> dict = new();
                dict["done"] = false;
                dict["hash"] = hash;
                dict["start"] = start; 
                dict["end"]=end;

                byte[] send_data = Client_main.dict_to_bytes(dict);
                udp_agent.Send(send_data, target);


                byte[] resp = udp_agent.Receive(ref trash);
                Dictionary<string,object>? res_dict = Client_main.bytes_to_dict(resp);

                if(res_dict != null && res_dict.ContainsKey("match") && (bool)res_dict["match"])
                {
                    //Chunk is correct, mark packets as received
                    correct_bytes += end - start;
                    long first_packet = a * packets_per_chunk;
                    long last_packet = Math.Min((a + 1) * packets_per_chunk - 1, packets_needed - 1);
                    last_packet += (end % 1440 == 0) ? 0 : 1;
                    log_message($"Chunk is correct, marking packets from {first_packet} to {last_packet} as received");
                    for (long p = first_packet; p <= last_packet; p++)
                    {
                        add_r_packet(p);
                        
                    }
                }

                start += end;

            }

            log_message("Packet counting done");

            Dictionary<string, object> dictx = new();
            dictx["done"] = true;
            byte[] send_data2 = Client_main.dict_to_bytes(dictx);

            udp_agent.Send(send_data2, target);

            wti.Progress = (correct_bytes /((float)prf.expected_size) * 100);
            wti.Progress = 0.3f;

            r_state = received_state.sever_answered;

            log_message("Requesting missing packets");
            ask_for_missing();

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
                            if(prf==null)
                            {
                                file_setup();
                            }
                           
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

                    case (received_state.unpaired):
                        {
                            change_file_status("Error ocurred in pairing! It will stop");
                            log_message("Server pairing stopped because of an error");
                            done = true;
                            break;
                        }

                    case(received_state.conection_error):
                        {
                            change_file_status("Error ocurred in connection! It will stop");
                            log_message("Connection stopped because of an error");
                            done = true;
                            break;
                        }

                    case(received_state.file_repair):
                        {
                            partial_file_repair();
                            break;
                        }
                }

                Thread.Sleep(0);
            }

            log_message("Receive worker State machine has finished it's work");
            Dispose();

        }

        public override void begin()
        {
            if(r_state==received_state.unpaired)
            {
                r_state = received_state.file_requested;
            }
            
            file_receive_state_machine();

        }

        public override void pair_up_failed()
        {
            file_receive_state_machine();
        }

        public void change_file_status(string st)
        {
            //Dispatcher.UIThread.Post(() => owner.set_filestatus(st));
            wti.Status = st;
        }

    }
}
