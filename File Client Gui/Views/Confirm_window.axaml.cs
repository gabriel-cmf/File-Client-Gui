using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using File_Client_Gui.data;
using File_Client_Gui.ViewModels;

namespace File_Client_Gui.Views;

public partial class Confirm_window : Window
{
    public Confirm_window()
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