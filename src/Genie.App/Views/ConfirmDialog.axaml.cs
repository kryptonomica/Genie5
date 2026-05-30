using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Genie.App.Views;

/// <summary>
/// A tiny modal prompt supporting two button shapes:
///
/// <para><b>2-button (Yes/No)</b> — use the 2-arg constructor; await
/// <c>ShowDialog&lt;bool&gt;(owner)</c>. Returns <c>true</c> for Yes and
/// <c>false</c> for No / Esc / close-box.</para>
///
/// <para><b>3-button (Yes/No/Cancel)</b> — use the 5-arg constructor with
/// custom button labels; await <c>ShowDialog&lt;bool?&gt;(owner)</c>.
/// Returns <c>true</c> for Yes, <c>false</c> for No, <c>null</c> for
/// Cancel / Esc / close-box. Use the 3-button shape when the caller needs
/// to distinguish "did the negative thing" from "decided not to act at
/// all" — e.g. Save-changes prompts where No means "skip the save and
/// proceed" but Cancel means "go back, I'll decide later".</para>
///
/// Designed to be cheap to instantiate — no view-model, no MVVM ceremony.
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
        // 2-button default: No carries the Esc / close-box role.
        NoButton.IsCancel = true;
    }

    public ConfirmDialog(string title, string message) : this()
    {
        Title           = title;
        MessageText.Text = message;
    }

    public ConfirmDialog(string title, string message,
                         string yesText, string noText, string cancelText) : this()
    {
        Title             = title;
        MessageText.Text  = message;
        YesButton.Content = yesText;
        NoButton.Content  = noText;
        // 3-button mode: hand the Esc / close-box role to Cancel; No
        // becomes a deliberate "proceed without doing the thing".
        NoButton.IsCancel       = false;
        CancelButton.Content    = cancelText;
        CancelButton.IsCancel   = true;
        CancelButton.IsVisible  = true;
    }

    private void OnYesClick   (object? sender, RoutedEventArgs e) => Close(true);
    private void OnNoClick    (object? sender, RoutedEventArgs e) => Close(false);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
