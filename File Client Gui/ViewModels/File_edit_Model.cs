using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace File_Client_Gui.ViewModels
{
    public partial class File_edit_Model : ObservableObject
    {
        [ObservableProperty]
        private string _folder_path;

        [ObservableProperty]
        private string folder_status;


        public File_edit_Model()
        {
            _folder_path = string.Empty;

            folder_status = string.Empty;
        }

        
    }
}
