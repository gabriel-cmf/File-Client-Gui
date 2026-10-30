using CommunityToolkit.Mvvm.ComponentModel;
using File_Client_Gui.data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace File_Client_Gui.ViewModels
{
    public partial  class Transfer_resume_Model : ViewModelBase
    {
        
        public ObservableCollection<Partial_file_info> prfs { get; }
        

        public Transfer_resume_Model(List<Partial_file_registry> partial_files)
        {
            prfs = new();
            foreach (var file in partial_files)
            {
                file.update_pfi();
                if(file.pfi != null)
                {
                    prfs.Add(file.pfi);
                }
                
            }
        }
    }
}
