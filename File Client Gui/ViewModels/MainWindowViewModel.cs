
using CommunityToolkit.Mvvm.Input;
using File_Client_Gui.data;
using File_test_client.workers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace File_Client_Gui.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {

        public Client_main cm;
        public string Greeting { get; } = "Welcome to Avalonia!";
        public List<Worker_task_info> tasks { get; }
        public ObservableCollection<Worker_task_info> Ob_tasks { get; }
        public ObservableCollection<File_registry_info> Fr_info { get; }


        public MainWindowViewModel(Client_main c)
        {
            Debug.WriteLine("Creating MainWindowViewModel");
            tasks = new List<Worker_task_info>();
            cm = c ?? throw new ArgumentNullException(nameof(cm));
            Debug.Assert(cm != null);
            Worker_task_info wti = new();
            Fr_info = new();
            
            
            tasks.Add(wti);
            Ob_tasks = new ObservableCollection<Worker_task_info>(tasks);
        }

        [RelayCommand]
        public void Exclude_entry(Worker_task_info wti)
        {
            Ob_tasks.Remove(wti);
        }

        [RelayCommand]
        public void pass()
        {
            
        }

        [RelayCommand]
        public void Open_download_file()
        {
            object o = new();
            cm.main_window.load_file_archive(o,new Avalonia.Interactivity.RoutedEventArgs());
        }
    }
}
