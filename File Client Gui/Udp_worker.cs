using File_test_client.workers;
using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using File_test_server.workers;

public enum worker_tasks
{
    none = 0,
    receive_file = 1,
    transfer_file = 2,
}



public class Udp_worker : IDisposable
{
    public IPEndPoint target;

    public UdpClient udp_agent;

    public worker_tasks objective;
    public bool task_done;
    public string wanted_file_name;
    public string wanted_file_ext;
    public string full_file_name;
    public int received_count;

    public bool ready_for_file;
    public int filesize;
    public int received_filesize;
    public byte[] file_bytes;
    public byte[] file_buffer;

    public long packets_needed;
    IPEndPoint trash;
    public received_state r_state;
    public Dictionary<string, object>?[] received_packets;
    public Stopwatch stopwatch;

    public static int max_udp_size = 65535;
    public List<Dictionary<string, object>> packets_to_send;
    public EndPoint ep;
    private object fileLock;

    public BlockingCollection<w_packet> packets_to_write;


    public Udp_worker()
    {
        var ips = Dns.GetHostAddresses(Dns.GetHostName())[0];
        target = new IPEndPoint(ips, 1);
        udp_agent = new UdpClient();
        task_done = false;
        r_state = received_state.start;
        full_file_name = wanted_file_name + wanted_file_ext;
        //received_packets = new();
        packets_to_send = new List<Dictionary<string, object>>();
        stopwatch = new Stopwatch();

        udp_agent.Client.SendTimeout = 5000;
        udp_agent.Client.ReceiveTimeout = 5000;
        udp_agent.Client.ReceiveBufferSize = 1024 * 1024 * 2;
        //udp_agent.Client.ReceiveBufferSize = 1024 * 1024 * 2;
        file_buffer = new byte[65536];
        ep = new IPEndPoint(ips, 1);
        fileLock = new();
        received_filesize = 0;

    }

    public void ask_for_file()
    {
        //Console.WriteLine(Dns.GetHostAddresses(Dns.GetHostName())[0]);


        Dictionary<string, object> data = new();
        data["type"] = 2;
        data["filename"] = wanted_file_name;
        data["file_ext"] = wanted_file_ext;

        var json_dict = JsonConvert.SerializeObject(data);

        //Console.WriteLine(json_dict);

        var j_bytes = Encoding.ASCII.GetBytes(json_dict);

        //Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        //Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        //s.SendTo(j_bytes, groupEP);
        udp_agent.Send(j_bytes, target);
        r_state = received_state.file_requested;




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

    public void file_setup()
    {
        Console.WriteLine("File setup, Waiting for broadcast");

        byte[] bytes = udp_agent.Receive(ref trash);



        Console.WriteLine($"Received broadcast from {trash} :");
        string b_st = Encoding.ASCII.GetString(bytes, 0, bytes.Length);
        Console.WriteLine($"Size: {bytes.Length}");
        Console.WriteLine($" {b_st}");

        Dictionary<string, object>? dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(b_st);

        if (dict == null || !((bool)dict["found"]))
        {
            r_state = received_state.start;
            Console.WriteLine("File not found or invalid answer!");
            return;
        }


        /*Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        s.Bind(groupEP);
        s.Listen();*/

        long filesize64 = Convert.ToInt64(dict["filesize"]);
        filesize = (int)filesize64;

        //file_bytes = new byte[filesize64];
        //received_packets = new Dictionary<string, object>?[filesize64];


        packets_needed = Convert.ToInt64(dict["packets"]);
        int pn = (int)packets_needed;

        //udp_agent.Client.ReceiveBufferSize = 1024*1024*8;
        //udp_agent.Client.ReceiveBufferSize = pn*64000;

        received_count = 0;
        //Console.WriteLine($" {b_st}");

        byte[] ack_bytes = Encoding.ASCII.GetBytes("ACK");

        //IPEndPoint server_EP = new IPEndPoint(ips, listenPort + 1);
        //Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        udp_agent.Send(ack_bytes, target);

        packets_to_write = new();

        r_state = received_state.sever_answered;
    }

    public void pair_up()
    {

        Console.WriteLine("Pairing up with server");
        bool paired = false;


        while (!paired)
        {
            var resp = udp_agent.Receive(ref target);
            if (resp != null && resp.Length > 0)
            {
                paired = true;
                break;
            }
        }

        Console.WriteLine("Out of paired loop");


        string ack = "ACK";
        var json_dict = JsonConvert.SerializeObject(ack);
        var j_bytes = Encoding.ASCII.GetBytes(json_dict);
        udp_agent.Send(j_bytes, target);
        Console.WriteLine("Pair up done !");
        begin();


    }


    public void split_packets(string filepath)
    {
        Dictionary<string, object> ex_packet = new();
        ex_packet["order"] = 1;
        ex_packet["sum"] = 1;
        ex_packet["bytes"] = new byte[4];

        byte[] file_bytes = System.IO.File.ReadAllBytes(filepath);


        var json_dict = JsonConvert.SerializeObject(ex_packet);

        var j_bytes = Encoding.ASCII.GetBytes(json_dict);
        //long header_size = bytesum(j_bytes);
        //long file_size = bytesum(file_bytes);
        long header_size = j_bytes.Length;
        long file_size = file_bytes.Length;
        /*Console.WriteLine("Header size:");
        Console.WriteLine(header_size);

        Console.WriteLine("File data length");
        Console.WriteLine(file_bytes.Length);*/

        long max_data_size = max_udp_size - header_size;

        long b4_size = file_size;
        b4_size += (b4_size * 34) / 100;
        long packet_num = (b4_size / max_data_size) + 1;


        /*Console.WriteLine("Number of packets:");
        Console.WriteLine(packet_num);

        Console.WriteLine("Max data size");
        Console.WriteLine(max_data_size);*/

        int i = 0;
        for (int a = 0; a < packet_num; a++)
        {
            Dictionary<string, object> packet = new();
            packet["order"] = a;
            packet["sum"] = 1;
            packet["bytes"] = 0;

            long max_data = max_data_size;
            List<byte> file_data = new();

            //Console.WriteLine("Byte values");
            //while (i < file_bytes.Length && max_data > 0 && max_data >= file_bytes[i])
            while (i < file_bytes.Length && file_data.Count + ((file_data.Count * 34) / 100) < max_data)
            {
                //Console.WriteLine(file_bytes[i]);
                file_data.Add(file_bytes[i]);
                //max_data -= file_bytes[i];
                //max_data -= 1;
                i++;

            }

            byte[] file_array = file_data.ToArray();
            packet["bytes"] = JsonConvert.SerializeObject(file_array);
            packet["sum"] = bytesum(file_array);
            //packet["bytes"] = file_data.ToArray();
            packets_to_send.Add(packet);


        }

        var pjson_dict = JsonConvert.SerializeObject(packets_to_send[0]);
        var p_bytes = Encoding.ASCII.GetBytes(pjson_dict);

        long p_size = p_bytes.Length;


        /*Console.WriteLine("Packet list size:");
        Console.WriteLine(packets_to_send.Count);


        Console.WriteLine("Packet size:");
        Console.WriteLine(p_size);*/
        //Console.WriteLine(packets_to_send[0]["bytes"]);




    }


    public void send_file_found(byte[] msg, IPEndPoint target)
    {
        udp_agent.Send(msg, target);

    }

    public void pass()
    {

    }

    public void send_file_data(int start, long end)
    {
        //IPEndPoint return_ip = new IPEndPoint(Dns.GetHostAddresses(Dns.GetHostName())[0], listenPort - 1);
        //Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        /*foreach (Dictionary<string, object> packet in packets_to_send)
        {

            var json_dict = JsonConvert.SerializeObject(packet);
            var j_bytes = Encoding.ASCII.GetBytes(json_dict);
            udp_agent.Send(j_bytes, target);
            Thread.Sleep(100);
        }*/

        for (int a = start; a < end; a++)
        {
            Console.WriteLine("Sending packet " + $"{a}");
            Dictionary<string, object> packet = packets_to_send[a];
            var json_dict = JsonConvert.SerializeObject(packet);
            var j_bytes = Encoding.ASCII.GetBytes(json_dict);
            udp_agent.Send(j_bytes, target);
            Thread.Sleep(100);
        }


        //sv_file_state = server_file_state.idle;
        packets_to_send.Clear();
        //client_upd_ports.Clear();
        //s.Close();
    }

    public void prepare_data_for_transfer()
    {
        Console.WriteLine("Preparing data for transfer");
        var proms = udp_agent.ReceiveAsync();
        split_packets(full_file_name);

        proms.Wait();
        byte[] bytes = proms.Result.Buffer;
        //string b_st = Encoding.ASCII.GetString(bytes, 0, bytes.Length);
        //Dictionary<string, object>? dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(b_st);
        Dictionary<string, object>? dict = Client_main.bytes_to_dict(bytes);

        if (dict != null)
        {
            long end_p = (long)dict["end"];
            long b = (long)dict["begin"];
            int begin = (int)b;
            string st_ipe = (string)dict["target"];
            var ips = Dns.GetHostAddresses(Dns.GetHostName())[0];
            IPEndPoint ipe_test = new(ips, 1);

            if (IPEndPoint.TryParse(st_ipe, out ipe_test) && ipe_test != null)
            {
                target = ipe_test;
                send_file_data(begin, end_p);
            }

        }


    }


    public void receive_file_data(Task file_write)
    {

        stopwatch.Start();

        //byte[] packet = udp_agent.Receive(ref trash);


        int ps = udp_agent.Client.ReceiveFrom(file_buffer, SocketFlags.None, ref ep);

        //string b_st = Encoding.ASCII.GetString(file_buffer, 0, file_buffer.Length);
        //Console.WriteLine(b_st);
        //Dictionary<string, object>? dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(b_st);
        //Dictionary<string, object>? dict = Client_main.bytes_to_dict(file_buffer);
        //Dictionary<string, object>? dict = Client_main.process_file_dict(file_buffer);

        if (file_buffer != null && file_buffer.Length > 0)
        {

            //long l_order = Convert.ToInt64(dict["order"]);
            long l_order = BitConverter.ToInt64(file_buffer, 0);
            int index = (int)Convert.ToInt32(l_order);

            int chunk_size = ps - 16;
            int pos = index;

            //byte[] file_data  = new byte[chunk_size];
            byte[] file_data = ArrayPool<byte>.Shared.Rent(chunk_size);

            Array.Copy(file_buffer, 16, file_data, 0, chunk_size);

            w_packet wp = new w_packet();
            wp.data = file_data;
            wp.order = index;
            wp.write_size = chunk_size;


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
            Console.WriteLine("Receiving file:");

        }


        //Console.WriteLine($"Size: {packet.Length}");


        if (received_count >= packets_needed)
        {
            stopwatch.Stop();
            packets_to_write.CompleteAdding();
            file_write.Wait();
            FileInfo fileInfo = new FileInfo(full_file_name);


            Console.WriteLine("Transfer time (s): ");
            Console.WriteLine(stopwatch.ElapsedMilliseconds / 1000);
            Console.WriteLine("Bytes received is filesize");
            Console.WriteLine(received_filesize == filesize);
            Console.WriteLine(fileInfo.Length == filesize);
            Console.WriteLine(filesize - received_filesize);



            r_state = received_state.file_data_ready;
            Console.WriteLine("FIle will be built");
            received_filesize = 0;



        }
    }


    public void build_file2()
    {
        int tries = 0;
        //Console.WriteLine($"[DEBUG] Tentando abrir o arquivo para escrita: {DateTime.Now}"); // <--- Adicione isso
        try
        {
            using (FileStream fs = new FileStream(full_file_name, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
            {

                tries = 0;
                foreach (w_packet wp in packets_to_write.GetConsumingEnumerable())
                {
                    long pos = wp.order;

                    fs.Position = pos;

                    fs.Write(wp.data, 0, wp.write_size);

                    if (wp.order % 20 == 0)
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
                Console.WriteLine("File built in build2 !");



            }

        }
        catch (IOException)
        {
            if (tries > 5)
            {
                Console.WriteLine("file acess failed!");
                return;
            }
            Thread.Sleep(500);
            tries++;
        }
    }

    public void build_file()
    {
        received_count = 0;

        int i = 0;
        foreach (Dictionary<string, object>? packet in received_packets)
        {


            if (packet == null) { break; }
            //Console.WriteLine("Packet size:");
            //Console.WriteLine( packet["bytes"] );

            byte[] packet_data = (byte[])packet["bytes"];





            for (int a = 0; a < packet_data.Length; a++)
            {

                //Console.WriteLine(packet_data[a]);
                file_bytes[i] = packet_data[a];
                i++;
            }
        }


        received_packets = null;
        File.WriteAllBytes(full_file_name, file_bytes);

        Console.WriteLine("File written");


        r_state = received_state.file_received;


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

                        Task write = Task.Factory.StartNew(() => { build_file2(); });
                        receive_file_data(write);

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
            }

            Thread.Sleep(0);
        }

    }


    public void check_if_file_exists()
    {
        Dictionary<string, object> dict = new Dictionary<string, object>();

    }

    public void begin()
    {

        Console.WriteLine();
        switch (objective)
        {
            case worker_tasks.none:
                {
                    break;
                }

            case worker_tasks.receive_file:
                {
                    file_receive_state_machine();
                    break;
                }
        }
    }


    public void Dispose()
    {
        udp_agent.Dispose();

    }
}
