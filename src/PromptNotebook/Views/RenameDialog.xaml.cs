using System.Windows;
using System.Windows.Input;

namespace PromptNotebook.Views;

public partial class RenameDialog : Window
{
    public string PromptTitle => TitleTextBox.Text.Trim();
    public string PromptCategory => CategoryTextBox.Text.Trim();

    public RenameDialog(string currentTitle, string currentCategory)
    {
        InitializeComponent();
        TitleTextBox.Text = currentTitle;
        CategoryTextBox.Text = currentCategory;
        Loaded += (_, _) =>
        {
            TitleTextBox.Focus();
            TitleTextBox.SelectAll();
        };
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            MessageBox.Show("Tên prompt không được để trống.", "Prompt Notebook", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }
}
