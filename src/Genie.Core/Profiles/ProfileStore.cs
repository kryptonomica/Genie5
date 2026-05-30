using System.Text.Json;

namespace Genie.Core.Profiles;

public sealed class ProfileStore
{
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly List<ConnectionProfile> _profiles = new();

    public IReadOnlyList<ConnectionProfile> Profiles => _profiles;

    public void Load(string path)
    {
        _profiles.Clear();
        if (!File.Exists(path)) return;
        var loaded = JsonSerializer.Deserialize<List<ConnectionProfile>>(
            File.ReadAllText(path), _json) ?? new();
        _profiles.AddRange(loaded);
    }

    public void Save(string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(_profiles, _json));

    public ConnectionProfile Add(string name, string host, int port,
                                  string accountName, string plainPassword,
                                  bool isSimutronics = false, string gameCode = "",
                                  string characterName = "", bool autoConnect = false)
    {
        if (autoConnect)
            foreach (var other in _profiles) other.AutoConnect = false;

        var profile = new ConnectionProfile
        {
            Name              = name,
            IsSimutronics     = isSimutronics,
            GameCode          = gameCode,
            CharacterName     = characterName,
            Host              = host,
            Port              = port,
            AccountName       = accountName,
            AutoConnect       = autoConnect,
            EncryptedPassword = ProfileCrypto.Encrypt(plainPassword)
        };
        _profiles.Add(profile);
        return profile;
    }

    public void Update(Guid id, string name, bool isSimutronics,
                       string gameCode, string characterName,
                       string host, int port,
                       string accountName, string plainPassword,
                       bool autoConnect = false)
    {
        var p = _profiles.FirstOrDefault(x => x.Id == id);
        if (p is null) return;
        p.Name          = name;
        p.IsSimutronics = isSimutronics;
        p.GameCode      = gameCode;
        p.CharacterName = characterName;
        p.Host          = host;
        p.Port          = port;
        p.AccountName   = accountName;
        if (!string.IsNullOrEmpty(plainPassword))
            p.EncryptedPassword = ProfileCrypto.Encrypt(plainPassword);
        if (autoConnect)
            foreach (var other in _profiles)
                if (other.Id != id) other.AutoConnect = false;
        p.AutoConnect = autoConnect;
    }

    public ConnectionProfile? GetAutoConnectProfile()
        => _profiles.FirstOrDefault(p => p.AutoConnect);

    public void Remove(Guid id) => _profiles.RemoveAll(p => p.Id == id);

    public string GetPassword(ConnectionProfile profile)
        => ProfileCrypto.Decrypt(profile.EncryptedPassword);
}
