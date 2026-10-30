using Avalonia.Threading;
using File_test_client.workers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using File_Client_Gui.ViewModels;
using File_Client_Gui.Views;
using File_Client_Gui.data;
using System.Threading;

namespace File_Client_Gui.workers
{
    public class Transfer_resume_worker : Worker_base
    {

        public List<string> hashes;
        public List<Partial_file_registry> selected_prfs;

        public Transfer_resume_worker(Client_main cm) : base(cm)
        {
               hashes = new List<string>();
               selected_prfs = new List<Partial_file_registry>();
        }


        public void  select_partials()
        {

            List<Partial_file_registry> prfs = new();

            for(int a=0;a<hashes.Count;a++)
            {
                string hash = hashes[a];
                Partial_file_registry? pfr = owner.part_file_dict.ContainsKey(hash) ? owner.part_file_dict[hash] : null;
                if (pfr != null)
                {
                    pfr.toggle_selected();
                    prfs.Add(pfr);
                }
            }

            Task<bool> confirm =  Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new Transfer_resume();
                var vm = new Transfer_resume_Model(prfs);
                
                
                dialog.DataContext = vm;

                bool res = await dialog.ShowDialog<bool>(owner.main_window);

                Partial_file_registry? pfr = null;
                if (res)
                {
                    for(int a=0;a<vm.prfs.Count;a++)
                    {
                        Partial_file_info pfi = vm.prfs[a];
                       
                        if (pfi.Selected == true)
                        {
                            if (pfi.registry != null && pfi.registry.TryGetTarget(out pfr) && pfr != null)
                            {
                                selected_prfs.Add(pfr);
                            }
                        }
                    }
                }

                return res;
            });


            while (!confirm.IsCompleted)
            {
                Thread.Sleep(0);
            }

            bool confirm_result = confirm.Result;

            if(confirm_result == false)
            {
                log_message("User cancelled selection of partial files to resume.");
            }
             else
            {
                log_message("User selected " + selected_prfs.Count + " partial files to resume.");
                owner.urgent_worker_queque.Enqueue(this);
            }


            





        }


        public override void begin()
        {
            log_message("Waiting for user to select partial files to resume...");
            select_partials();
            Dispose();
        }

    }
}
