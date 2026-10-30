using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace File_test_client.workers
{
    public class Worker_base : IDisposable
    {
        public UdpClient udp_agent;
        public IPEndPoint target;
        public IPEndPoint trash;
        public Client_main owner;
        public static int max_udp_size = 65535;
        public Stopwatch stopwatch;



        public Worker_base(Client_main ow)
        {

            bool allows_IPv6 = System.Net.Sockets.Socket.OSSupportsIPv6;
            if(allows_IPv6)
            {
                log_message("Creating an ipv6 socket");
                //udp_agent = new UdpClient(AddressFamily.InterNetworkV6);
                udp_agent = new UdpClient(0, AddressFamily.InterNetwork);
            }
            else
            {
                log_message("Creating an ipv4 socket");
                udp_agent = new UdpClient(0,AddressFamily.InterNetwork);
            }

            //udp_agent.Client.DualMode = true;
            if (OperatingSystem.IsWindows())
            {
                udp_agent.AllowNatTraversal(true);
            }
            owner = ow;
            stopwatch = new();

            var ips = Dns.GetHostAddresses(Dns.GetHostName())[0];
            target = new IPEndPoint(ips, 1);
            trash = new IPEndPoint(IPAddress.Any, 0);   
        }

        public virtual void begin()
        {

        }

        public virtual void pair_up()
        {

            Debug.WriteLine("Pairing up with server");
            //Debug.WriteLine("Listening at:");
            try
            {
                //Debug.WriteLine(udp_agent.Client.LocalEndPoint.ToString());
            }
            catch
            {

            }
            bool paired = false;
            bool fail=false;
            
            udp_agent.Client.ReceiveTimeout = 10000;
            while (!paired)
            {
                try
                {
                    var resp = udp_agent.Receive(ref trash);
                    Debug.WriteLine("Received server message");
                    if (resp != null && resp.Length > 0)
                    {
                        paired = true;
                        break;
                    }
                }
                catch (Exception e) 
                {
                   Debug.WriteLine("Error while pairing up:\n" + e.Message);
                   log_message(e.Message);
                   fail = true;
                   break;
                    
                }

                Thread.Sleep(0);
            }



            Debug.WriteLine("Out of paired loop");

            if (fail) 
            {
                log_message("Pair up has failed");
                pair_up_failed();
                Dispose();
                return; 
            }
           


            string ack = "ACK";
            var json_dict = JsonConvert.SerializeObject(ack);
            var j_bytes = Encoding.ASCII.GetBytes(json_dict);
            udp_agent.Send(j_bytes, target);
            Debug.WriteLine("Pair up done !");
            Debug.WriteLine("new target");
            Debug.WriteLine(target.ToString());
            begin();
            log_message("Worker task done");


        }


        public void Dispose()
        {
            udp_agent.Dispose();

        }

        public virtual void pair_up_failed()
        {

        }

        public void log_message(string msg)
        {
            Debug.WriteLine(msg);

            if(owner != null)
            {
                owner.add_msg(msg);
            }
            
        }

        public void send_data(byte[] data, IPEndPoint iep)
        {
            //Debug.WriteLine(iep.ToString());
            //IPEndPoint new_adr = new IPEndPoint(iep.Address.MapToIPv4(), iep.Port);
            udp_agent.Send(data, iep);

        }

        public void force_bind()
        {
            IPEndPoint localEP = new IPEndPoint(IPAddress.Any, 0);
            
        }
    }
}
