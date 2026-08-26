using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PointerTest;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        Point p = e.GetPosition(this);

        Console.WriteLine(
            $"POINTER: {p.X:0}, {p.Y:0}"
        );

        StatusText.Text =
            $"Pointer OK: {p.X:0}, {p.Y:0}";
    }

    private void OnButtonClick(
        object? sender,
        RoutedEventArgs e)
    {
        Console.WriteLine("BUTTON CLICK OK");

        StatusText.Text =
            "BUTTON CLICK OK";
    }
}