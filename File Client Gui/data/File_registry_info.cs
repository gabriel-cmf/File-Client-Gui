using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace File_Client_Gui.data
{
    public partial class File_registry_info : ObservableObject
    {
        [ObservableProperty]
        private string file_name;

        [ObservableProperty]
        private string file_size;

        [ObservableProperty]
        private string path;

        [ObservableProperty]
        private string status;


        public File_registry_info()
        {
            file_name = string.Empty;
            file_size = string.Empty;
            path = string.Empty;
            status = string.Empty;
        }
    }
}
