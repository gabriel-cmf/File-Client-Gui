using File_test_client.workers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
using File_Client_Gui.Views;
using File_Client_Gui.data;
using File_Client_Gui.ViewModels;
using SkiaSharp;
using System.Diagnostics;
using LiteDB;
using File_test_server.workers;
using File_Client_Gui.workers;
using Avalonia;
using System.Linq;


public enum server_message_type
{
    none = 0,
    file_exists = 1,
    server_has_file = 2,
    file_listing_request=3,
    server_ip_changed=4,
}


public class Client_main
{

    private const int listenPort = 3335;
    UdpClient udp_agent;
    //public List<Server_registry> know_servers;
    public Dictionary<IPAddress, Server_registry> know_servers;
    public IPAddress ips;
    public IPEndPoint main_server;
    public List<Task> tasks;
    public HashSet<IPAddress> ip_set;
    public MainWindow main_window;
    public static readonly string[] size_suff = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
    ILiteCollection<File_registry> file_registry;
    public Dictionary<string, File_registry> file_dict;
    public long client_code;
    public Logger_worker log_work;
    //public JsonSerializer serializer;


    public Client_main()
    {

        udp_agent = new UdpClient(AddressFamily.InterNetworkV6);
        udp_agent.Client.DualMode = true;
        know_servers = new();
        /*var ips = Dns.GetHostAddresses(Dns.GetHostName())[0];
        main_server = new IPEndPoint(ips, listenPort + 1);
        know_servers.Add(main_server);*/
        //Debug.WriteLine("main server:");
        //Debug.WriteLine(main_server.ToString());
        tasks = new();
        Debug.WriteLine("Opening db");
        client_code = DateTime.Now.Ticks;
        file_dict = new();
        string day =  DateTime.Now.ToString("yyyy-MM-dd");
        ip_set = new HashSet<IPAddress>();
        log_work = new(this,"logs\\" + day + ".txt") ;
        using (var db = new LiteDatabase("mydb.db"))
        {
            file_registry = db.GetCollection<File_registry>("files");
            var servers = db.GetCollection<Server_registry>("servers");

            var todasColecoes = db.GetCollectionNames();

            foreach (var nome in todasColecoes)
            {
                Debug.WriteLine("Colection found: " + nome);
            }

            foreach (var server in servers.FindAll())
            {
                ip_set.Add(server.get_endpoint().Address);
                //know_servers.Add(server);
                know_servers[server.get_endpoint().Address] = server;
            }


            foreach (var file in file_registry.FindAll())
            {
                //main_window.add_file_registry(file.fr_info);
                file.update_fr_info();
                file_dict[file.file_hash] = file;
                Debug.WriteLine(file.file_hash);
            }
        }
        //main_window = mw;




    }

    public void init_fr_info()
    {
        using (var db = new LiteDatabase("mydb.db"))
        {
            file_registry = db.GetCollection<File_registry>("files");

            foreach (var file in file_registry.FindAll())
            {
                file.set_file_status();
                main_window.add_file_registry(file.fr_info);
                //Debug.WriteLine(file.file_hash);
            }

        }
    }


    public string[] get_valid_hashes()
    {
        var dict_keys =  file_dict.Keys;
        return dict_keys.Where(x => file_dict[x].fr_info.Status == "Valid").ToArray();
    }

    public void update_db()
    {
        using (var db = new LiteDatabase("mydb.db"))
        {
           
        }
    }

    public static bool can_read_file(string file_path)
    {
        try
        {
            using (var stream = new FileStream(file_path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                return true;
            }
        }
        catch (IOException)
        {
            
            return false;
        }
    }


    public void wakeup_notify()
    {
        Dictionary<string, object> data = new();
        data["type"] = 1;
        data["client_code"] = client_code;


        var json_dict = JsonConvert.SerializeObject(data);

        //Console.WriteLine(json_dict);

        Debug.WriteLine("Sending wakeup notifications");

        var j_bytes = Encoding.ASCII.GetBytes(json_dict);

        try
        {
            foreach (Server_registry server in know_servers.Values)
            {
                udp_agent.SendAsync(j_bytes, server.get_endpoint());
            }
        }
        catch(Exception ex) 
        {
            Debug.WriteLine(ex);
        }
    }

    public void shutdown_notify()
    {
        Dictionary<string, object> data = new();
        data["type"] = 3;
        data["client_code"] = client_code;


        var json_dict = JsonConvert.SerializeObject(data);

        //Console.WriteLine(json_dict);

        var j_bytes = Encoding.ASCII.GetBytes(json_dict);

        foreach (Server_registry server in know_servers.Values)
        {
            udp_agent.SendAsync(j_bytes, server.get_endpoint());
        }

    }

    public static Dictionary<string, object>? bytes_to_dict(byte[] bytes)
    {
        Newtonsoft.Json.JsonSerializer serializer = new Newtonsoft.Json.JsonSerializer();
        using (var stream = new MemoryStream(bytes))
        {
            using (var reader = new StreamReader(stream, Encoding.UTF8, bufferSize: 64 * 1024))
            {
                using (var jsonReader = new JsonTextReader(reader))
                {
                    var dict = serializer.Deserialize<Dictionary<string, object>>(jsonReader);
                    return dict;
                }
            }
        }

    }


    public static Dictionary<string, object>? process_reader(ref Utf8JsonReader read)
    {
        bool empty = true;
        int order = -1;
        long sum = 0;
        //string bytes = "";
        byte[] bytes = null;

        while (read.Read())
        {
            if (read.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (read.TokenType != JsonTokenType.PropertyName) { continue; }


            if (read.TokenType == JsonTokenType.PropertyName)
            {
                if (read.ValueTextEquals("order"))
                {
                    empty = false;
                    read.Read();
                    order = read.GetInt32();
                }
                else if (read.ValueTextEquals("bytes"))
                {
                    empty = false;
                    read.Read();
                    Console.WriteLine(read.TokenType);
                    if (read.TokenType == JsonTokenType.String)
                    {

                        //Console.WriteLine(read.GetString().Length % 4);
                        bytes = read.GetBytesFromBase64();
                    }
                    else
                    {

                    }


                }

                else if (read.ValueTextEquals("sum"))
                {
                    empty = false;
                    read.Read();
                    sum = read.GetInt64();
                }
            }
        }

        if (empty) { return null; }

        Dictionary<string, object> dict = new Dictionary<string, object>();
        dict["bytes"] = bytes;
        dict["order"] = order;
        dict["sum"] = sum;

        return dict;


    }

    public static Dictionary<string, object>? process_file_dict(byte[] bytes)
    {
        ReadOnlySpan<byte> json = bytes;

        var reader = new Utf8JsonReader(json, isFinalBlock: true, state: default);


        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                return process_reader(ref reader);
            }

        }

        return null;
    }

    public static byte[] dict_to_bytes(Dictionary<string,object> data)
    {
        var json_dict = JsonConvert.SerializeObject(data);



        var j_bytes = Encoding.ASCII.GetBytes(json_dict);

        return j_bytes;
    }


    public static string  bytesize_to_str(long sz)
    {
        if(sz == 0)
        {
            return "0 B";
        }


        if(sz < 0)
        {
            return "-" + bytesize_to_str(-sz);
        }

        int mag = (int)Math.Log(sz,1024);
        mag = Math.Min(mag,size_suff.Length-1);

        double dec_size = sz/Math.Pow(1024, mag);

        Span<char> buffer = stackalloc char[64];

        int chars_written = 0;

       
        bool success = dec_size.TryFormat(buffer, out int number_length, "0.##");

        if (success) 
        {
            chars_written += number_length;
            buffer[chars_written++] = ' ';
            ReadOnlySpan<char> suff_span = size_suff[mag].AsSpan();
            suff_span.CopyTo(buffer.Slice(chars_written));
            chars_written += suff_span.Length;
        }

        return new string(buffer.Slice(0, chars_written));
    }


    public void listen_for_server()
    {
        while (true)
        {

            
            IPEndPoint groupEP = new(IPAddress.Any, 1);
            Console.WriteLine("Waiting for server broadcast");
            byte[] bytes = Array.Empty<byte>();
            try
            {
                bytes = udp_agent.Receive(ref groupEP);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Thread.Sleep(0);
                switch (ex.HResult)
                {
                    case 10054:
                        {
                            break;


                        }
                }
                return;


            }


            string b_st = Encoding.ASCII.GetString(bytes, 0, bytes.Length);
            Dictionary<string, object>? dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(b_st);

            if (dict != null)
            {
                long ti = (long)dict["type"];
                server_message_type type = (server_message_type)ti;
                add_server(groupEP);
                switch (type)
                {
                    case (server_message_type.none):
                        {
                            break;
                        }

                    case (server_message_type.file_exists):
                        {

                            Debug.WriteLine("Server asked for file:");

                            string comp_file = (string)dict["hash"];
                            Debug.WriteLine(comp_file);
                            Debug.WriteLine("Looking for it");
                            IPEndPoint ex_iep = new(groupEP.Address, groupEP.Port);
                            Dictionary<string, object> resp = new Dictionary<string, object>();
                            //bool ffound = File.Exists(comp_file);
                            bool ffound = false;

                            try
                            {
                                var e = file_dict[comp_file];
                                ffound = e.is_valid();
                            }
                            catch (Exception ex) 
                            { 
                            
                            }



                            resp["found"] = ffound;
                            resp["client_code"] = client_code;

                            var json_dict = JsonConvert.SerializeObject(resp);

                            //Console.WriteLine(json_dict);

                            var j_bytes = Encoding.ASCII.GetBytes(json_dict);

                            if (ffound)
                            {
                                File_transfer_worker uw = new(this);
                                uw.target = ex_iep;


                                
                                uw.complete_file_path = comp_file;
                                uw.send_file_found(j_bytes, ex_iep);
                                Task worker_task = Task.Factory.StartNew(() => { uw.begin(); });
                                tasks.Add(worker_task);
                                Debug.WriteLine("File transfer worker has started");
                            }
                            else
                            {
                                Debug.WriteLine("File was not found");
                                udp_agent.Send(j_bytes, ex_iep);
                            }

                            break;
                        }


                    case (server_message_type.file_listing_request):
                        {

                            IPEndPoint ex_iep = new(groupEP.Address, groupEP.Port);

                            if (file_dict.Count <= 0)
                            {
                                Debug.WriteLine("No files where found");
                                Dictionary<string, object> data = new();
                                data["count"] = 0;
                                data["type"] = 3;
                                data["client_code"] = client_code;
                                byte[] to_send = dict_to_bytes(data);
                                udp_agent.Send(to_send, ex_iep);
                            }
                            else
                            {

                                Debug.WriteLine("Files where found");
                                Dictionary<string, object> data = new();
                                data["count"] = file_dict.Count;
                                data["type"] = 4;
                                data["client_code"] = client_code;
                                byte[] to_send = dict_to_bytes(data);

                                File_storage_worker uw = new(this);
                                uw.target = ex_iep;

                                uw.send_first_msg(to_send);

                                Task worker_task = Task.Factory.StartNew(() => { uw.pair_up(); });
                                tasks.Add(worker_task);
                            }
                             break;
                        }


                    case (server_message_type.server_ip_changed):
                        {
                            string? st_ip = dict["old_ip"] as string;
                            if(st_ip != null)
                            {
                                IPEndPoint old_ipe = IPEndPoint.Parse(st_ip);
                                var sv = know_servers[old_ipe.Address];
                                sv.ip_ad = groupEP.Address.ToString();
                                sv.server_endpoint = groupEP;
                                know_servers[groupEP.Address]  =sv;


                            }
                            break;
                        }
                }
            }


            foreach (Task t in tasks)
            {
                if (t.IsCompleted)
                {
                    t.Dispose();

                }
            }

            //log_work.write_to_log();

            Thread.Sleep(0);
        }
    }


    public void add_file_to_db(File_registry fr)
    {

        using (var db = new LiteDatabase("mydb.db"))
        {
            
            var files = db.GetCollection<File_registry>("files");
            if (!files.Exists(x => x.file_hash == fr.file_hash))
            {
                files.Insert(fr);
            }
               
        }
            
        //file_registry.Update(fr);
    }

    public void add_server_to_db(Server_registry sr)
    {

        using (var db = new LiteDatabase("mydb.db"))
        {
            var svs = db.GetCollection<Server_registry>("servers");
            if(!svs.Exists(x => x.ip_ad == sr.ip_ad))
            {
                svs.Insert(sr);
            }
        }

        //file_registry.Update(fr);
    }


    /*public void create_file_receive_worker()
    {
        File_receive_worker uw = new File_receive_worker(this);
        uw.target = main_server;
        uw.wanted_file_name = "RelatorioFinal-INF491";
        uw.wanted_file_ext = ".pdf";
        uw.full_file_name = uw.wanted_file_name + uw.wanted_file_ext;
        //uw.objective = worker_tasks.receive_file;
        uw.ask_for_file();
        Task worker_task = Task.Factory.StartNew(() => { uw.pair_up(); });
        tasks.Add(worker_task);
        Console.WriteLine("udp worker has started");
    }

    public void create_file_receive_worker(string filename,string file_ext)
    {
        File_receive_worker uw = new File_receive_worker(this);
        uw.target = main_server;
        uw.wanted_file_name = filename;
        uw.wanted_file_ext = file_ext;
        uw.full_file_name = uw.wanted_file_name + uw.wanted_file_ext;
        //uw.objective = worker_tasks.receive_file;
        Worker_task_info wti = new();

        uw.create_wti(wti);
        var mwvm = main_window.DataContext as MainWindowViewModel;
        mwvm.Ob_tasks.Add(wti);

        uw.ask_for_file();
        Task worker_task = Task.Factory.StartNew(() => { uw.pair_up(); });
        tasks.Add(worker_task);
        Console.WriteLine("udp worker has started");
    }*/

    public void create_file_receive_worker(string hash,string ip,string port)
    {
        File_receive_worker uw = new File_receive_worker(this);
        //uw.target = main_server;
        string targer_server = ip + ":" + port;
        Debug.WriteLine(targer_server);
        //Debug.WriteLine(main_server.ToString());
        uw.target = IPEndPoint.Parse(targer_server);
        Server_registry sr = new();
        sr.ip_ad = uw.target.ToString();
        add_server_to_db(sr);
        
        uw.target_hash = hash;
       
        Worker_task_info wti = new();

        uw.create_wti(wti);
        var mwvm = main_window.DataContext as MainWindowViewModel;
        mwvm.Ob_tasks.Add(wti);

        uw.ask_for_file2();
        Task worker_task = Task.Factory.StartNew(() => { uw.pair_up(); });
        tasks.Add(worker_task);
        Console.WriteLine("udp worker has started");
    }

    public void create_file_receive_worker(string hash, string ipe)
    {
        File_receive_worker uw = new File_receive_worker(this);
        //uw.target = main_server;
        //string targer_server = ip + ":" + port;
        Debug.WriteLine("Target endpoint:");
        Debug.WriteLine(ipe);
        //Debug.WriteLine(main_server.ToString());
        uw.target = IPEndPoint.Parse(ipe);
        Server_registry sr = new();
        sr.ip_ad = uw.target.ToString();
        add_server_to_db(sr);

        uw.target_hash = hash;

        Worker_task_info wti = new();

        uw.create_wti(wti);
        var mwvm = main_window.DataContext as MainWindowViewModel;
        mwvm.Ob_tasks.Add(wti);

        uw.ask_for_file2();
        Task worker_task = Task.Factory.StartNew(() => { uw.pair_up(); });
        tasks.Add(worker_task);
        Console.WriteLine("udp worker has started");
    }


    public void input_loop()
    {
        Console.WriteLine("Choose one: ");
        Console.WriteLine("(1) - Ask server for file");
        Console.WriteLine("(2) - Quit");
        Console.Write("Enter an integer: ");
        string input = Console.ReadLine();

        if (int.TryParse(input, out int number))
        {
            Console.WriteLine($"You entered {number}");
            switch (number)
            {


                case 1:
                    {
                        //create_file_receive_worker();
                        break;
                    }

                case 2:
                    {
                        //listener.client_state_machine();
                        break;
                    }
            }

            Console.WriteLine("Out of input loop");
            number = 0;
        }
        else
        {
            Console.WriteLine("Invalid input. Please enter an integer.");
        }
    }

    public void set_filestatus(string st)
    {

        main_window.set_file_status(st);
    }

    public void save_files()
    {
        using (var db = new LiteDatabase("mydb.db"))
        {
            var files = db.GetCollection<File_registry>("files");
            foreach (File_registry fr in file_dict.Values)
            {
                files.Upsert(fr);
            }

        }
    }

    public void save_servers()
    {
        using (var db = new LiteDatabase("mydb.db"))
        {
            var servers = db.GetCollection<Server_registry>("servers");
            foreach (Server_registry sr in know_servers.Values)
            {
                servers.Upsert(sr);
            }

        }
    }

    public bool add_server(string adress,string port)
    {
       
        IPEndPoint ipe = IPEndPoint.Parse(adress + ":" +port);

        bool newx = ip_set.Add(ipe.Address);

        if (newx) 
        { 
            Server_registry sr = new Server_registry(ipe);
            know_servers[sr.get_endpoint().Address] = sr;
        }


        return newx;
    }

    public bool add_server(string adress)
    {

        IPEndPoint ipe = IPEndPoint.Parse(adress);

        bool newx = ip_set.Add(ipe.Address);

        if (newx)
        {
            Server_registry sr = new Server_registry(ipe);
            know_servers[sr.get_endpoint().Address] = sr;
        }


        return newx;
    }

    public bool add_server(IPEndPoint ipe)
    {

       

        bool newx = ip_set.Add(ipe.Address);

        if (newx)
        {
            Server_registry sr = new Server_registry(ipe);
            know_servers[sr.get_endpoint().Address] = sr;
        }


        return newx;
    }

    public void add_msg(string msg) 
    {
        log_work.add_msg(msg);
    }

    
}
