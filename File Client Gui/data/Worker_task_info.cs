using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace File_Client_Gui.data
{
    public partial class Worker_task_info : ObservableObject
    {

        [ObservableProperty]
        private string name;

        [ObservableProperty]
        private string filesize;

        [ObservableProperty]
        private float progress;

        [ObservableProperty]
        private bool hash_match;

        public Worker_task_info() 
        {
            name="Task";
            filesize = "0 B";
            progress = 0;
            hash_match = false;
        }


        public void set_name(string st)
        {
            Name = st;
        }
    }
}
