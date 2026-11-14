using System.Windows;
using PromptNotebook.Services;

namespace PromptNotebook.Views;

public partial class LoginWindow : Window
{
    private readonly PasswordService _passwordService;

    public LoginWindow(PasswordService passwordService)
    {
        InitializeComponent();
        _passwordService = passwordService;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_passwordService.Validate(PasswordInput.Password))
        {
            PasswordInput.Clear();
            DialogResult = true;
            return;
        }

        PasswordInput.Clear();
        ErrorText.Visibility = Visibility.Visible;
        PasswordInput.Focus();
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
