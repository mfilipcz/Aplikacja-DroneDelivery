using Avalonia;
using Avalonia.Controls;
using DroneDeliveryLinux.Services;
using DroneDeliveryLinux.ViewModels;
using System;

namespace DroneDeliveryLinux.Views;

public partial class MainWindow : Window
{
    private readonly GrpcDataService _grpcService;

    public MainWindow()
    {
        Title = "Drone Delivery System";
        Width = 1100;
        Height = 800;
        Icon = null;

        _grpcService = new GrpcDataService();

        ShowLogin();
    }

    private void ShowLogin()
    {
        var vm = new LoginViewModel(_grpcService, (isAdmin) => 
        {
            if (isAdmin) ShowAdmin();
            else ShowUser();
        });
        Content = new LoginView { DataContext = vm };
    }

    private void ShowUser()
    {
        var vm = new MainViewModel(_grpcService, () => ShowLogin());
        Content = new UserView(vm);
    }

    private void ShowAdmin()
    {
        var vm = new AdminViewModel(_grpcService, () => ShowLogin());
        Content = new AdminView { DataContext = vm };
    }
}