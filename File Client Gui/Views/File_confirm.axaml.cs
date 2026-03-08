using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using File_Client_Gui.ViewModels;
using System;

namespace File_Client_Gui;

public partial class File_confirm : Window
{
    public File_confirm()
    {
        InitializeComponent();
    }

    private async void Choose_folder(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
      



        if (topLevel == null) { return; }


        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the download folder",
            AllowMultiple = false
        });

        if(folders == null) { return; }

        var fcm = DataContext as File_confirm_Model;
        if (fcm== null) { return; }

        if (folders.Count >=1)
        {
            string? pth = folders[0].TryGetLocalPath();

            if (pth != null) 
            { 
                fcm.Folder_path = pth;
                fcm.Folder_found = true;
            }
        }



    }

    public void Confirm(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    public void Cancel(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}