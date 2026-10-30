using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using File_Client_Gui.ViewModels;

namespace File_Client_Gui;

public partial class Transfer_resume : Window
{
    public Transfer_resume()
    {
        InitializeComponent();
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
