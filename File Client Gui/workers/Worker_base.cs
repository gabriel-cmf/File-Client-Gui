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
                udp_agent = new UdpClient(AddressFamily.InterNetwork);
            }
            else
            {
                log_message("Creating an ipv4 socket");
                udp_agent = new UdpClient(AddressFamily.InterNetwork);
            }

            //udp_agent.Client.DualMode = true;
            udp_agent.AllowNatTraversal(true);
            owner = ow;
            stopwatch = new();

            var ips = Dns.GetHostAddresses(Dns.GetHostName())[0];
            target = new IPEndPoint(ips, 1);

        }

        public virtual void begin()
        {

        }

        public virtual void pair_up()
        {

            Console.WriteLine("Pairing up with server");
            bool paired = false;
            bool fail=false;

            while (!paired)
            {
                try
                {
                    var resp = udp_agent.Receive(ref target);
                    if (resp != null && resp.Length > 0)
                    {
                        paired = true;
                        break;
                    }
                }
                catch (Exception e) 
                {
                   fail = true;
                   break;
                    
                }

                Thread.Sleep(0);
            }


            if (fail) { return; }
            Console.WriteLine("Out of paired loop");


            string ack = "ACK";
            var json_dict = JsonConvert.SerializeObject(ack);
            var j_bytes = Encoding.ASCII.GetBytes(json_dict);
            udp_agent.Send(j_bytes, target);
            Console.WriteLine("Pair up done !");
            begin();


        }


        public void Dispose()
        {
            udp_agent.Dispose();

        }

        public void log_message(string msg)
        {
            Debug.WriteLine(msg);

            if(owner != null)
            {
                owner.add_msg(msg);
            }
            
        }
    }
}
