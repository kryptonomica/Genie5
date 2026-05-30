---
name: feedback-avalonia-dock
description: Hard-won lessons about Dock.Avalonia 11.3.6.4 setup and Avalonia ListBox virtualization inside Dock content areas
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 0aba752d-dd44-4f08-98fa-704f54c7ec21
---

## Dock.Avalonia 11.3.6.4 setup rules

- Package `Dock.Avalonia.Themes.Fluent` is REQUIRED — DockControl is completely invisible without `<DockFluentTheme/>` in Application.Styles. Do not use `StyleInclude` or `ResourceInclude` for it in this version.
- Namespace split: `IRootDock` is in `Dock.Model.Controls`; `IDockable`, `IFactory`, `Orientation`, `Alignment` are in `Dock.Model.Core`; concrete classes (`Document`, `Tool`, `DocumentDock`, `ToolDock`, etc.) are in `Dock.Model.ReactiveUI.Controls`.
- `SplitterDockable` was renamed to `ProportionalDockSplitter` in this version.
- Do NOT double-initialize: either call `factory.InitLayout(layout)` manually OR set `InitializeLayout="True"` on DockControl — not both. Currently using `InitializeLayout="True"` on DockControl only.
- Do NOT use `ControlRecycling` — causes content to appear in wrong panes (reuses control instances across dockable types).

**Why:** These were discovered by examining NuGet errors, XML documentation inspection, and GitHub API fetching the DockReactiveUISample.

## Avalonia VirtualizingStackPanel inside Dock

When a `ListBox` (or subclass) is placed directly inside a Dock content area, the Dock's `ContentPresenter` passes infinite available height during the measure pass. The default `VirtualizingStackPanel` ItemsPanel collapses to 0 visible height when available height is infinite — resulting in 0 rendered items even with a populated `ItemsSource`.

**Fix:** Always set `ItemsPanel` to a non-virtualizing `StackPanel` when using ListBox inside Dock content:
```xml
<ListBox.ItemsPanel>
  <ItemsPanelTemplate><StackPanel/></ItemsPanelTemplate>
</ListBox.ItemsPanel>
```

With a 2000-item cap this is acceptable performance. Vitals/Room panels work because they use `ScrollViewer > StackPanel` (not a ListBox), which doesn't suffer from this issue.

**Why:** Diagnosed by adding a `{Binding Lines.Count}` diagnostic label which confirmed 152 items existed while the ListBox rendered nothing.

## Application.DataTemplates vs Window.DataTemplates

DataTemplates in `Application.DataTemplates` work correctly for Dock content rendering. No need to move them to `Window.DataTemplates`.
