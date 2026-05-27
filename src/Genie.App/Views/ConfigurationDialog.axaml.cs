using Avalonia.ReactiveUI;
using Genie.App.ViewModels;
using ReactiveUI;

namespace Genie.App.Views;

public partial class ConfigurationDialog : ReactiveWindow<ConfigurationViewModel>
{
    public ConfigurationDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ConfigurationViewModel vm) return;

            vm.RequestClose += () => Close();

            // Initial pass.
            InitializePanels(vm);

            // Re-bind every panel whenever the user picks a different profile.
            // The VM swaps live engines for drafts (or vice versa) under the hood;
            // each panel needs its `Initialize(engine, callback)` re-invoked to
            // pick up the new instance, repopulate its DataGrid, and clear its form.
            vm.WhenAnyValue(x => x.SelectedProfile)
              .Subscribe(_ => InitializePanels(vm));
        };
    }

    /// <summary>
    /// Hand every panel its current engine reference. Called once when the VM
    /// is set and again whenever SelectedProfile changes. Null engines mean
    /// "this engine isn't available right now" — panels still exist but stay
    /// empty until a profile is picked.
    /// </summary>
    private void InitializePanels(ConfigurationViewModel vm)
    {
        if (vm.HighlightEngine     is { } highlights
         && vm.NameHighlightEngine is { } names
         && vm.PresetEngine        is { } presets)
        {
            HighlightsPanelCtrl.Initialize(
                highlights, names, presets,
                onHighlightsChanged: vm.OnHighlightsChanged,
                onNamesChanged:      vm.OnHighlightsChanged,
                onPresetsChanged:    vm.OnPresetsChanged);
        }

        if (vm.TriggerEngine    is { } triggers)    TriggersPanelCtrl   .Initialize(triggers,    vm.OnTriggersChanged);
        if (vm.SubstituteEngine is { } substitutes) SubstitutesPanelCtrl.Initialize(substitutes, vm.OnSubstitutesChanged);
        if (vm.GagEngine        is { } gags)        GagsPanelCtrl       .Initialize(gags,        vm.OnGagsChanged);
        if (vm.AliasEngine      is { } aliases)     AliasesPanelCtrl    .Initialize(aliases,     vm.OnAliasesChanged);
        if (vm.MacroEngine      is { } macros)      MacrosPanelCtrl     .Initialize(macros,      vm.OnMacrosChanged);
        if (vm.VariableStore    is { } variables)   VariablesPanelCtrl  .Initialize(variables,   vm.OnVariablesChanged);
        if (vm.ClassEngine      is { } classes)     ClassesPanelCtrl    .Initialize(classes,     vm.OnClassesChanged);

        // WindowSettingsStore is always present — it's app-level state, not
        // per-profile, so no nullability check.
        LayoutPanelCtrl.Initialize(vm.WindowSettings, vm.OnWindowSettingsChanged);
    }
}
