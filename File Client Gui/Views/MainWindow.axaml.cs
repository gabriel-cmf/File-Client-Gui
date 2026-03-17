using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using File_Client_Gui.data;
using File_Client_Gui.ViewModels;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace File_Client_Gui.Views
{
    public partial class MainWindow : Window
    {

        public Client_main client_main;
        public FilePickerFileType file_picker_json;


        public MainWindow()
        {
            InitializeComponent();
            client_main = null;
            //client_main.wakeup_notify();
            //Task worker_task = Task.Factory.StartNew(() => { client_main.listen_for_server(); });
            file_picker_json = new FilePickerFileType(".json")
            {
                Patterns = new[] { "*.json" },
                AppleUniformTypeIdentifiers = new[] { "public.json" },
            };





        }


        public void client_main_init()
        {
            Debug.WriteLine("Client main init");
            if(client_main == null) { return; }
            client_main.wakeup_notify();
            Task worker_task = Task.Factory.StartNew(() => { client_main.listen_for_server(); });
        }

        /*public void pass()
        {
            var mwvm = DataContext as MainWindowViewModel;

            if (mwvm != null)
            {
                mwvm.cm = client_main;
            }
        }*/

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (this.DataContext is MainWindowViewModel vm)
            {

                
                client_main = vm.cm;
                client_main.main_window = this;
                client_main_init();
            }
        }

        public void set_file_status(string st)
        {
            file_status.Text = st;
        }

        public void add_file_registry(File_registry_info fri)
        {
            var mwvm = DataContext as MainWindowViewModel;
            if (mwvm == null) 
            {
                Debug.WriteLine("Error: DataContext was null in add_file_registry");
                return; 
            }

            mwvm.Fr_info.Add(fri);
        }

        private void Button_OnClick(object? sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Click!");
        }

        public async void load_file_archive(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) { return; }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Archive File",
                FileTypeFilter= new[] { file_picker_json },
                AllowMultiple = false
            });

            if (files.Count >= 1)
            {
                var file = files[0];
                string? pth = file.TryGetLocalPath();
                if (pth == null) { return; }
                string jsonData = File.ReadAllText(pth);
                Dictionary<string, object>? dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonData);
                if(dict == null) { return; }

                //client_main.add_server((string)dict["url"], (string)dict["port"]);
                //client_main.create_file_receive_worker((string)dict["hash"], (string)dict["url"], (string)dict["port"]);

                client_main.add_server((string)dict["url"]);
                client_main.create_file_receive_worker((string)dict["hash"], (string)dict["url"]);



            }
        }

        private void cm_get_file(object? sender, RoutedEventArgs e)
        {

            if (Filename.Text != null && File_ext.Text != null) 
            {

                throw new InvalidOperationException();
                //client_main.create_file_receive_worker(Filename.Text,File_ext.Text);
            }
            
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            client_main.shutdown_notify();
            client_main.save_files();
            client_main.save_servers();
            base.OnClosing(e);
        }
    }
}