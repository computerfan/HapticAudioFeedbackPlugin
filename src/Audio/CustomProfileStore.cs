#nullable disable
namespace Loupedeck.HapticAudioFeedback;

using System.Text.Json;

internal sealed record ProfileInfo(string Id, string Label, string Description, bool IsCustom);
internal sealed record ProfileCatalog(Dictionary<string, AudioSettings> Profiles, ProfileInfo[] ProfileInfo, int ProfilesRevision, string ProfilesError);
internal sealed class ProfileRequest
{
    public string Operation { get; set; }
    public string Id { get; set; }
    public string Name { get; set; }
    public AudioSettings Settings { get; set; }
    public int? ExpectedRevision { get; set; }
}
internal sealed record ProfileResult(ProfileCatalog Catalog, string SelectedId);

internal sealed class CustomProfileStore
{
    public const string SettingName = "CustomAudioProfilesV1";
    public const int MaximumProfiles = 32;
    private sealed class Entry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public AudioSettings Settings { get; set; }
    }
    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public int Revision { get; set; }
        public List<Entry> Profiles { get; set; } = new();
    }
    private readonly object _gate = new();
    private readonly Action<string> _write;
    private Document _document = new();
    private string _loadError;

    public CustomProfileStore(Func<string> read, Action<string> write, Action<Exception> onError)
    {
        _write = write;
        try
        {
            var json = read();
            if (json == null) return;
            var document = JsonSerializer.Deserialize<Document>(json) ?? throw new InvalidDataException("Missing profile document.");
            if (document.Version != 1 || document.Revision < 0 || document.Revision == int.MaxValue || document.Profiles == null || document.Profiles.Count > MaximumProfiles)
                throw new InvalidDataException("Unsupported profile document.");
            var ids = new HashSet<string>();
            var names = new HashSet<string>(AudioProfiles.All.Select(p => p.Label), StringComparer.OrdinalIgnoreCase);
            foreach (var profile in document.Profiles)
            {
                if (profile?.Id == null || !profile.Id.StartsWith("custom-", StringComparison.Ordinal) ||
                    !Guid.TryParseExact(profile.Id[7..], "N", out _) || !ids.Add(profile.Id))
                    throw new InvalidDataException("Invalid custom profile ID.");
                profile.Name = ValidateName(profile.Name);
                if (!names.Add(profile.Name)) throw new InvalidDataException("Duplicate profile name.");
                profile.Settings = Normalize(profile.Settings);
            }
            _document = document;
        }
        catch (Exception ex)
        {
            _loadError = "Custom profiles could not be loaded. Saved data was preserved; restart the plugin after resolving the storage error.";
            onError(ex);
        }
    }

    public ProfileCatalog Snapshot()
    {
        lock (_gate)
        {
            var profiles = AudioProfiles.All.ToDictionary(p => p.Id, p => p.Create());
            var info = AudioProfiles.All.Select(p => new ProfileInfo(p.Id, p.Label, p.Description, false)).ToList();
            foreach (var profile in _document.Profiles)
            {
                profiles.Add(profile.Id, profile.Settings.Copy());
                info.Add(new(profile.Id, profile.Name, "Custom profile. Apply to restore its saved tuning; paused state is preserved.", true));
            }
            return new(profiles, info.ToArray(), _document.Revision, _loadError);
        }
    }
    public AudioSettings Resolve(string id)
    {
        lock (_gate)
        {
            var custom = _document.Profiles.FirstOrDefault(p => p.Id == id);
            if (custom != null) return custom.Settings.Copy();
            return AudioProfiles.Create(id);
        }
    }
    public ProfileResult Save(ProfileRequest request)
    {
        lock (_gate)
        {
            if (_loadError != null) throw new InvalidOperationException(_loadError);
            if (request.ExpectedRevision != _document.Revision)
                throw new InvalidOperationException("Profiles changed elsewhere. Reload saved settings before saving a profile.");
            var name = ValidateName(request.Name);
            Entry previous = null;
            AudioSettings settings;
            if (request.Operation == "duplicate") settings = Resolve(request.Id);
            else if (request.Operation == "save")
            {
                settings = Normalize(request.Settings);
                if (!string.IsNullOrEmpty(request.Id))
                    previous = _document.Profiles.FirstOrDefault(p => p.Id == request.Id)
                        ?? throw new ArgumentException("Only an existing custom profile can be updated. Save a new copy instead.");
            }
            else throw new ArgumentException("Unknown profile operation.");
            if (AudioProfiles.All.Any(p => string.Equals(p.Label, name, StringComparison.OrdinalIgnoreCase)) ||
                _document.Profiles.Any(p => p.Id != previous?.Id && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A profile with that name already exists. Choose another name.");
            if (previous == null && _document.Profiles.Count >= MaximumProfiles)
                throw new ArgumentException($"You can save up to {MaximumProfiles} custom profiles.");
            var entry = new Entry { Id = previous?.Id ?? "custom-" + Guid.NewGuid().ToString("N"), Name = name, Settings = Normalize(settings) };
            var next = new Document { Revision = checked(_document.Revision + 1), Profiles = _document.Profiles.ToList() };
            if (previous == null) next.Profiles.Add(entry);
            else next.Profiles[next.Profiles.IndexOf(previous)] = entry;
            // Do not publish changes in memory unless SDK persistence succeeds.
            _write(JsonSerializer.Serialize(next));
            _document = next;
            return new(Snapshot(), entry.Id);
        }
    }
    private static string ValidateName(string name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 64 || name.Any(char.IsControl))
            throw new ArgumentException("Profile name must contain 1–64 characters without control characters.");
        return name;
    }
    private static AudioSettings Normalize(AudioSettings settings)
    {
        if (settings == null) throw new ArgumentException("Profile settings are required.");
        settings.Validate();
        var copy = settings.Copy(); copy.Enabled = true; copy.EnableDebugServer = false;
        return copy;
    }
}
