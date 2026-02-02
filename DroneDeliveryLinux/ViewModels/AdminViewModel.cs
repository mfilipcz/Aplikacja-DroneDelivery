using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DroneDeliveryLinux.Models;
using DroneDeliveryLinux.Services;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace DroneDeliveryLinux.ViewModels;

public partial class AdminViewModel : ObservableObject
{
    private readonly GrpcDataService _grpcService;
    private readonly Action _onLogout;
    private DispatcherTimer _timer;

    [ObservableProperty]
    private ObservableCollection<UserMsg> _users = new();

    [ObservableProperty]
    private ObservableCollection<DroneOrder> _allOrders = new();

    [ObservableProperty]
    private UserMsg? _selectedUser;

    [ObservableProperty]
    private string _newUsername = "";

    [ObservableProperty]
    private string _newPassword = "";

    public AdminViewModel(GrpcDataService grpcService, Action onLogout)
    {
        _grpcService = grpcService;
        _onLogout = onLogout;

        // Order refresh timer
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (s, e) => await RefreshOrders();
        _timer.Start();

        // Load initial data
        _ = RefreshUsers();
        _ = RefreshOrders();
    }

    [RelayCommand]
    private async Task RefreshUsers()
    {
        var users = await _grpcService.GetAllUsersAsync();
        Users.Clear();
        foreach (var u in users) Users.Add(u);
    }

    [RelayCommand]
    private async Task RefreshOrders()
    {
        // Admin sees all orders
        var orders = await _grpcService.GetAllOrdersAsync();
        
        // Sync list
        AllOrders.Clear();
        foreach (var o in orders) AllOrders.Add(o);
    }

    [RelayCommand]
    private async Task ApproveOrder(DroneOrder order)
    {
        if (order.Status == "Oczekuje na zatwierdzenie")
        {
            // Update status via Admin method
            var success = await _grpcService.UpdateOrderStatusAsync(order.Id, "W drodze");
            if (success)
            {
                order.Status = "W drodze";
                await RefreshOrders();
            }
        }
    }

    [RelayCommand]
    private async Task RejectOrder(DroneOrder order)
    {
        await _grpcService.DeleteOrderAsync(order.Id);
        await RefreshOrders();
    }

    [RelayCommand]
    private async Task DeleteUser()
    {
        if (SelectedUser != null)
        {
            await _grpcService.DeleteUserAsync(SelectedUser.Username);
            await RefreshUsers();
        }
    }

    [RelayCommand]
    private async Task AddUser()
    {
        if (!string.IsNullOrWhiteSpace(NewUsername) && !string.IsNullOrWhiteSpace(NewPassword))
        {
            var (success, msg) = await _grpcService.RegisterAsync(NewUsername, NewPassword);
            if (success)
            {
                NewUsername = "";
                NewPassword = "";
                await RefreshUsers();
            }
        }
    }

    [RelayCommand]
    private void Logout()
    {
        _timer.Stop();
        _grpcService.Logout();
        _onLogout();
    }
}