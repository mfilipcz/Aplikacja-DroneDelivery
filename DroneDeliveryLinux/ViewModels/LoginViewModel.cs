using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DroneDeliveryLinux.Services;
using System;
using System.Threading.Tasks;

namespace DroneDeliveryLinux.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly GrpcDataService _grpcService;
    private readonly Action<bool> _onLoginSuccess; // bool isAdmin

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _messageColor = "Red";

    [ObservableProperty]
    private bool _isBusy = false;

    public LoginViewModel(GrpcDataService grpcService, Action<bool> onLoginSuccess)
    {
        _grpcService = grpcService;
        _onLoginSuccess = onLoginSuccess;
    }

    [RelayCommand]
    private async Task Login()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Podaj login i hasło";
            MessageColor = "Red";
            return;
        }

        IsBusy = true;
        ErrorMessage = "";

        var (success, message) = await _grpcService.LoginAsync(Username, Password);
        
        IsBusy = false;

        if (success)
        {
            _onLoginSuccess(_grpcService.IsAdmin);
        }
        else
        {
            ErrorMessage = message;
            MessageColor = "Red";
        }
    }

    [RelayCommand]
    private async Task Register()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Podaj login i hasło do rejestracji";
            MessageColor = "Red";
            return;
        }

        IsBusy = true;
        ErrorMessage = "";

        var (success, message) = await _grpcService.RegisterAsync(Username, Password);
        
        IsBusy = false;
        ErrorMessage = message;
        MessageColor = success ? "Green" : "Red";
    }
}