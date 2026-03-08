using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using File_Client_Gui.ViewModels;
using File_Client_Gui.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Linq;




namespace File_Client_Gui
{
    public partial class App : Application
    {


        public IServiceProvider? services_prov { get; private set; }

        public override void Initialize()
        {
           
            AvaloniaXamlLoader.Load(this);
            //client_main= new Client_main();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var services = new ServiceCollection();
            services.AddSingleton<Client_main>();
            services.AddTransient<MainWindowViewModel>();
            services.AddTransient<MainWindow>();
            services_prov = services.BuildServiceProvider();

            var vm = services_prov.GetRequiredService<MainWindowViewModel>();



            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = vm,
                };

                var mw = desktop.MainWindow as MainWindow;
                mw.client_main.init_fr_info();
               

                
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void DisableAvaloniaDataAnnotationValidation()
        {
            // Get an array of plugins to remove
            var dataValidationPluginsToRemove =
                BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

            // remove each entry found
            foreach (var plugin in dataValidationPluginsToRemove)
            {
                BindingPlugins.DataValidators.Remove(plugin);
            }
        }
    }
}