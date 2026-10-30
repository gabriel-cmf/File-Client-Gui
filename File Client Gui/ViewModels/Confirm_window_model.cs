using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace File_Client_Gui.ViewModels
{

   


    public partial class Confirm_window_model : ViewModelBase
    {
        [ObservableProperty]
        private string _button_yes_text;

        [ObservableProperty]
        private string _button_no_text;

        [ObservableProperty]
        private string _body_text;

        public Confirm_window_model()
        {
            _button_yes_text = "Yes";
            _button_no_text = "No";
            _body_text = "Are you sure?";
        }
    }
}
