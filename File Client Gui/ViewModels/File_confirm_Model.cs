using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace File_Client_Gui.ViewModels
{
    public partial class File_confirm_Model : ObservableObject
    {

        [ObservableProperty]
        private string _folder_path;

        [ObservableProperty]
        private bool _folder_found;

        [ObservableProperty]
        private string _file_name;

        [ObservableProperty]
        private long _packets;


        public File_confirm_Model() 
        {

            _folder_path = string.Empty;
            _folder_found=false;
            _file_name = string.Empty;
            _packets = 0;
        }

    }
}
