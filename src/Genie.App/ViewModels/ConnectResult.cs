using Genie.Core.Connection;
using Genie.Core.Profiles;

namespace Genie.App.ViewModels;

/// <summary>
/// Output of the Connect dialog. Carries both the assembled <see cref="ConnectionConfig"/>
/// (what the network layer needs) and the saved <see cref="ConnectionProfile"/> the user
/// picked from the dropdown (so the app can scope per-profile configuration files,
/// title-bar labels, etc.). The profile may be null if the user typed credentials
/// directly without selecting a saved profile.
/// </summary>
public sealed record ConnectResult(ConnectionConfig Config, ConnectionProfile? Profile);
