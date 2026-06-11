using System.Windows.Input;
using Genie.Core.Capture;

namespace Genie.App.ViewModels;

/// <summary>
/// Per-entry view-model for the <b>Analyst ▸ Run Capture Recipe</b> submenu.
/// Carries the loaded <see cref="CaptureRecipe"/> plus a <see cref="Display"/>
/// label and a pre-bound <see cref="ICommand"/> so the menu's container style
/// can bind <c>{Binding Command}</c> / <c>{Binding}</c> directly — mirroring the
/// <see cref="LayoutMenuItem"/> pattern that avoids the ancestor-cast crash
/// (see MainWindow.axaml). The shared run command reads <see cref="Recipe"/>
/// from the CommandParameter.
/// </summary>
public sealed record RecipeMenuItem(string Display, CaptureRecipe Recipe, ICommand Command);
