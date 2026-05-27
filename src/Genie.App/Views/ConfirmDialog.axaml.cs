using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Genie.App.Views;

/// <summary>
/// A tiny modal Yes/No prompt. Caller awaits <see cref="Window.ShowDialog{TResult}"/>
/// to get back <c>true</c> for Yes and <c>false</c> for No (also returned by
/// the close-box and Esc, both of which fire the cancel path).
///
/// Designed to be cheap to instantiate — no view-model, no MVVM ceremony. Use
/// the two-arg constructor to set the title and message at construction time.
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string title, string message) : this()
    {
        Title           = title;
        MessageText.Text = message;
    }

    private void OnYesClick(object? sender, RoutedEventArgs e) => Close(true);
    private void OnNoClick (object? sender, RoutedEventArgs e) => Close(false);
}
