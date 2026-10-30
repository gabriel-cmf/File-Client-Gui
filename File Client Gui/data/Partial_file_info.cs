using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace File_Client_Gui.data
{
    public partial class Partial_file_info : ObservableObject
    {
        [ObservableProperty]
        private string file_name;
        [ObservableProperty]
        private string file_size;
        [ObservableProperty]
        private string path;

        [ObservableProperty]
        private bool selected;

        [ObservableProperty]
        private string complete_name;


        public WeakReference<Partial_file_registry>? registry;

        public Partial_file_info()
        {
            file_name = string.Empty;
            file_size = string.Empty;
            path = string.Empty;
            selected=false;
            registry = null;
            complete_name = string.Empty;
        }


    }
}
