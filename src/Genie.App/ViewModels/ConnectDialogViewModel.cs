using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Genie.Core.Connection;
using Genie.Core.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>
/// One of the four DragonRealms server instances visible on the SGE site.
/// <see cref="GameCode"/> is the protocol token sent to eaccess.play.net.
/// </summary>
public sealed record GameInstance(string GameCode, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public class ConnectDialogViewModel : ReactiveObject
{
    private readonly ProfileStore? _store;
    private readonly Action?       _onStoreChanged;

    /// <summary>The four DR instances offered by the SGE landing page.</summary>
    public static readonly GameInstance[] Instances =
    [
        new("DR",  "DragonRealms (Prime)"),
        new("DRX", "DragonRealms Platinum"),
        new("DRF", "DragonRealms: The Fallen"),
        new("DRT", "DragonRealms Test"),
    ];

    // ── Profile picker ────────────────────────────────────────────────────────

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    [Reactive] public ConnectionProfile? SelectedProfile { get; set; }

    public ReactiveCommand<Unit, Unit> SaveProfileCommand   { get; }
    public ReactiveCommand<Unit, Unit> DeleteProfileCommand { get; }

    // ── Editable fields ───────────────────────────────────────────────────────

    [Reactive] public string        ProfileName { get; set; } = "";
    [Reactive] public GameInstance  Instance    { get; set; } = Instances[0];
    [Reactive] public string        Account     { get; set; } = "";
    [Reactive] public string        Password    { get; set; } = "";

    /// <summary>The preferred character to log in as. May be edited freely;
    /// also bound as the <see cref="ComboBox.SelectedItem"/> of the dropdown.</summary>
    [Reactive] public string        Character   { get; set; } = "";

    // UseStormFrontEnd was removed May 25, 2026 after A/B testing showed no
    // observable difference between FE:GENIE and FE:STORM for the info-verb
    // surface. The underlying ConnectionConfig.FrontEndId field still exists
    // (defaults to "GENIE") in case future testing of other surfaces reveals
    // FE-gated markup. See backlog.md → "FE:STORM hypothesis — disconfirmed"
    // for the experiment writeup and the FE_DIFF Console mode for re-running
    // the comparison.

    // ── Character fetcher ─────────────────────────────────────────────────────

    public ObservableCollection<string> AvailableCharacters { get; } = [];

    [Reactive] public bool   IsFetching   { get; private set; }
    [Reactive] public string FetchStatus  { get; private set; } = "";

    /// <summary>
    /// Label shown on the fetch/change button. Reads <c>Fetch</c> when no
    /// character is selected, <c>Change…</c> when one is — same command,
    /// different intent.
    /// </summary>
    public extern string FetchButtonLabel { [ObservableAsProperty] get; }

    public ReactiveCommand<Unit, Unit> FetchCharactersCommand { get; }

    // ── Dialog result commands ────────────────────────────────────────────────

    public ReactiveCommand<Unit, ConnectResult?> OkCommand     { get; }
    public ReactiveCommand<Unit, ConnectResult?> CancelCommand { get; }

    /// <summary>Designer-friendly parameterless constructor.</summary>
    public ConnectDialogViewModel() : this(null, null) { }

    public ConnectDialogViewModel(ProfileStore? store, Action? onStoreChanged)
    {
        _store          = store;
        _onStoreChanged = onStoreChanged;

        // ── Load profiles into the dropdown ────────────────────────────────
        if (_store is not null)
            foreach (var p in _store.Profiles)
                Profiles.Add(p);

        // ── Auto-populate fields when a profile is selected ────────────────
        this.WhenAnyValue(x => x.SelectedProfile)
            .Where(p => p is not null)
            .Subscribe(p => PopulateFrom(p!));

        // ── Field validity gate for OK / Save / Fetch ──────────────────────
        var canOk = this.WhenAnyValue(
            x => x.Account, x => x.Password, x => x.Character,
            (a, p, c) => !string.IsNullOrWhiteSpace(a)
                      && !string.IsNullOrWhiteSpace(p)
                      && !string.IsNullOrWhiteSpace(c));

        var canSave = this.WhenAnyValue(
            x => x.ProfileName, x => x.Account, x => x.Character,
            (n, a, c) => _store is not null
                      && !string.IsNullOrWhiteSpace(n)
                      && !string.IsNullOrWhiteSpace(a)
                      && !string.IsNullOrWhiteSpace(c));

        var canDelete = this.WhenAnyValue(x => x.SelectedProfile)
            .Select(p => _store is not null && p is not null);

        // Active whenever the credentials are valid and we're not already mid-fetch.
        // The Character field no longer gates the button — instead, the click
        // action clears Character first (effectively "swap to a different one").
        var canFetch = this.WhenAnyValue(
                x => x.Account, x => x.Password, x => x.IsFetching,
                (a, p, busy) => !busy
                            && !string.IsNullOrWhiteSpace(a)
                            && !string.IsNullOrWhiteSpace(p));

        // Button label tracks Character: "Fetch" when empty, "Change…" when set.
        this.WhenAnyValue(x => x.Character)
            .Select(c => string.IsNullOrWhiteSpace(c) ? "Fetch" : "Change…")
            .ToPropertyEx(this, x => x.FetchButtonLabel);

        // ── Commands ───────────────────────────────────────────────────────
        // OK returns both the assembled ConnectionConfig AND the selected
        // saved profile (if any) so callers can attach per-profile state.
        OkCommand     = ReactiveCommand.Create(
            () => (ConnectResult?)new ConnectResult(BuildConfig(), SelectedProfile), canOk);
        CancelCommand = ReactiveCommand.Create(() => (ConnectResult?)null);
        SaveProfileCommand     = ReactiveCommand.Create(SaveProfile,   canSave);
        DeleteProfileCommand   = ReactiveCommand.Create(DeleteProfile, canDelete);
        FetchCharactersCommand = ReactiveCommand.CreateFromTask(FetchCharactersAsync, canFetch);

        // ── Auto-select the lone profile if there's exactly one ────────────
        if (Profiles.Count == 1)
            SelectedProfile = Profiles[0];
    }

    private void PopulateFrom(ConnectionProfile p)
    {
        ProfileName = p.Name;
        Instance    = Instances.FirstOrDefault(i =>
            string.Equals(i.GameCode, p.GameCode, StringComparison.OrdinalIgnoreCase))
            ?? Instances[0];
        Account     = p.AccountName;
        Password    = _store?.GetPassword(p) ?? "";

        // Switching profiles must drop the previous account's character list — it
        // belonged to a different account and showing it here is both confusing and
        // (mildly) a privacy leak. Rebuild the dropdown to contain only this
        // profile's stored character so the binding can still display it.
        AvailableCharacters.Clear();
        if (!string.IsNullOrEmpty(p.CharacterName))
            AvailableCharacters.Add(p.CharacterName);
        Character        = p.CharacterName;
        FetchStatus      = "";
    }

    private ConnectionConfig BuildConfig() => new()
    {
        SgeHost         = "eaccess.play.net",
        SgePort         = 7900,
        AccountName     = Account,
        AccountPassword = Password,
        CharacterName   = Character,
        GameCode        = Instance.GameCode,
        Mode            = ConnectionMode.DirectSGE,
        // FrontEndId left at default "GENIE" — A/B testing showed no FE
        // difference for our probed surfaces; ConnectionConfig's default
        // is "GENIE" so we don't need to set it explicitly here.
    };

    private async Task FetchCharactersAsync()
    {
        // Always start with a clean slate. If the user already had a character
        // selected (e.g. from a profile), this is the "Change…" case — the
        // user wants to see the full list and pick a different name. Clearing
        // also keeps any stale entry from the previous account out of view.
        Character = "";
        AvailableCharacters.Clear();

        IsFetching  = true;
        FetchStatus = "Connecting to SGE…";
        try
        {
            var sge = new SgeAuthClient(NullLogger<SgeAuthClient>.Instance);
            var cfg = BuildConfig();
            var chars = await sge.ListCharactersAsync(cfg);

            foreach (var c in chars)
                AvailableCharacters.Add(c.Name);

            FetchStatus = $"Found {chars.Count} character{(chars.Count == 1 ? "" : "s")} — pick one.";
        }
        catch (Exception ex)
        {
            FetchStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsFetching = false;
        }
    }

    private void SaveProfile()
    {
        if (_store is null) return;

        var existing = _store.Profiles
            .FirstOrDefault(p => string.Equals(p.Name, ProfileName, StringComparison.OrdinalIgnoreCase));

        ConnectionProfile? target;
        if (existing is not null)
        {
            _store.Update(
                existing.Id, ProfileName,
                isSimutronics: true,
                gameCode: Instance.GameCode,
                characterName: Character,
                host: "eaccess.play.net", port: 7900,
                accountName: Account, plainPassword: Password);
            target = existing;
        }
        else
        {
            target = _store.Add(
                ProfileName, "eaccess.play.net", 7900, Account, Password,
                isSimutronics: true,
                gameCode: Instance.GameCode,
                characterName: Character);
            Profiles.Add(target);
            SelectedProfile = target;
        }

        // FE:STORM checkbox was removed (May 25, 2026); we no longer write
        // the FrontEndId from the dialog. Any pre-existing profile keeps
        // whatever value it had, but new profiles will use the
        // ConnectionProfile.FrontEndId default ("GENIE").

        _onStoreChanged?.Invoke();
    }

    private void DeleteProfile()
    {
        if (_store is null || SelectedProfile is null) return;

        var toRemove = SelectedProfile;
        _store.Remove(toRemove.Id);
        Profiles.Remove(toRemove);
        SelectedProfile = null;

        _onStoreChanged?.Invoke();
    }
}
