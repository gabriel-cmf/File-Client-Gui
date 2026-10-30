using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using File_Client_Gui.data;
using File_Client_Gui.ViewModels;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;

namespace File_Client_Gui.Views
{
    public partial class MainWindow : Window
    {

        public Client_main client_main;
        public FilePickerFileType file_picker_json;
        public bool popup_open;
        public bool force_close;


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

            popup_open = false;
            force_close = false;





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
                //client_main_init();
            }
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            // Your initialization logic here
            client_main_init();
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
            //client_main.multi_file_test();
            //return;
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

        /*private void cm_get_file(object? sender, RoutedEventArgs e)
        {

            if (Filename.Text != null && File_ext.Text != null) 
            {

                throw new InvalidOperationException();
                //client_main.create_file_receive_worker(Filename.Text,File_ext.Text);
            }
            
        }*/

        public async Task<bool> confirm_popup(string body, string yes, string no)
        {
            var conf_window = new Confirm_window();
            var conf_vm = new Confirm_window_model();
            conf_window.DataContext = conf_vm;
            conf_vm.Body_text = body;
            conf_vm.Button_yes_text = yes;
            conf_vm.Button_no_text = no;
            Debug.WriteLine("Showing confirmation popup");
            var res = await conf_window.ShowDialog<bool>(this);

            return res;


        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {

            if (client_main.tasks.Count > 0 && !force_close)
            {
                e.Cancel = true;
                Debug.WriteLine("There are ongoing tasks. Confirming exit.");
                bool q = false;

                if (!popup_open)
                {
                    popup_open = true;
                    q = await confirm_popup("There are ongoing tasks. Are you sure you want to exit?", "Yes", "No");
                    popup_open = false;
                }


                if (q)
                {
                    e.Cancel = false;
                    force_close = true;
                    Close();
                }
                else
                {
                    e.Cancel = true;
                    base.OnClosing(e);
                }

            }
            else 
            {
                client_main.shutdown_notify();
                client_main.save_files();
                client_main.save_servers();
                base.OnClosing(e);
            }
            
        }
    }
}