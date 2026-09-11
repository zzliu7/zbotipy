using DiscordRPC;

namespace LocalMusicPlayer.Services;

public sealed class DiscordService : IDisposable
{
    public const string ApplicationId = "1491513834228154409";
    public const string DefaultLargeImageKey = "logo";

    public const int MaxDiscordHhImageIndex = 22;
    public const int MaxDiscordSjImageIndex = 3;
    public const int MaxDiscordEhImageIndex = 4;

    private const int MaxDetailsLength = 128;
    private const int MaxStateLength = 128;
    private const int MaxLargeImageTextLength = 128;

    private DiscordRpcClient? _client;
    private bool _disposed;
    private bool _enabled = true;

    public bool DiscordEnabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
                Clear();
        }
    }

    public void Initialize()
    {
        if (_disposed || !_enabled)
            return;

        try
        {
            EnsureClient();
        }
        catch
        {
            // Discord missing or IPC error — stay non-fatal
        }
    }

    public void UpdatePlaying(string title, string artist, TimeSpan position, string? comment = null)
    {
        if (_disposed || !_enabled)
            return;

        try
        {
            if (!EnsureClient())
                return;

            var details = Clamp(OrUnknownTitle(title), MaxDetailsLength);
            var state = Clamp(OrUnknownArtist(artist), MaxStateLength);
            var startUtc = DateTime.UtcNow - (position < TimeSpan.Zero ? TimeSpan.Zero : position);

            var imageKey = ResolveLargeImageKey(artist);

            var presence = new RichPresence
            {
                Type = ActivityType.Listening,
                Details = details,
                State = state,
                Timestamps = new Timestamps(startUtc),
                Assets = new Assets
                {
                    LargeImageKey = imageKey,
                    LargeImageText = LargeImageTextFromComment(comment)
                }
            };

            _client!.SetPresence(presence);
        }
        catch
        {
            // Ignore RPC failures
        }
    }

    public void UpdatePaused(string title, string artist, string? comment = null)
    {
        if (_disposed || !_enabled)
            return;

        try
        {
            if (!EnsureClient())
                return;

            var details = Clamp($"Paused: {OrUnknownTitle(title)}", MaxDetailsLength);
            var state = Clamp(OrUnknownArtist(artist), MaxStateLength);
            var imageKey = ResolveLargeImageKey(artist);

            var presence = new RichPresence
            {
                Type = ActivityType.Listening,
                Details = details,
                State = state,
                Timestamps = null,
                Assets = new Assets
                {
                    LargeImageKey = imageKey,
                    LargeImageText = LargeImageTextFromComment(comment)
                }
            };

            _client!.SetPresence(presence);
        }
        catch
        {
            // Ignore RPC failures
        }
    }

    public void Clear()
    {
        if (_client is null || _client.IsDisposed)
            return;

        try
        {
            _client.ClearPresence();
        }
        catch
        {
            // Ignore
        }
    }

    public void ApplyTestPresence()
    {
        Initialize();
        UpdatePlaying("Test Song", "Test Artist", TimeSpan.FromSeconds(42));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (_client is { IsDisposed: false })
            {
                try
                {
                    _client.ClearPresence();
                }
                catch
                {
                    // Ignore
                }

                _client.Dispose();
            }
        }
        catch
        {
            // Ignore disposal issues
        }

        _client = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private bool EnsureClient()
    {
        if (_client is { IsDisposed: false })
            return true;

        try
        {
            _client?.Dispose();
            var client = new DiscordRpcClient(ApplicationId);
            client.Initialize();
            _client = client;
            return true;
        }
        catch
        {
            _client = null;
            return false;
        }
    }

    public static string ResolveLargeImageKey(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
            return DefaultLargeImageKey;

        var a = artist.Trim().ToLowerInvariant();

        var isSuperJunior = a.Contains("super junior", StringComparison.Ordinal);
        var isDe = a.Contains("d&e", StringComparison.Ordinal)
                   || a.Contains("d & e", StringComparison.Ordinal);

        if (isSuperJunior && isDe)
            return RandomImageKey("hh", MaxDiscordHhImageIndex);

        if (isSuperJunior)
            return RandomImageKey("sj", MaxDiscordSjImageIndex);

        if (a.Contains("eunhyuk", StringComparison.Ordinal))
            return RandomImageKey("eh", MaxDiscordEhImageIndex);

        if (a.Contains("donghae", StringComparison.Ordinal))
            return RandomImageKey("dh", MaxDiscordEhImageIndex);

        return DefaultLargeImageKey;
    }

    public static string RandomImageKey(string prefix, int maxIndex)
    {
        if (maxIndex < 1 || string.IsNullOrEmpty(prefix))
            return DefaultLargeImageKey;

        var n = Random.Shared.Next(1, maxIndex + 1);
        return $"{prefix}{n}";
    }

    private static string OrUnknownTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "Unknown title" : title.Trim();

    private static string OrUnknownArtist(string? artist) =>
        string.IsNullOrWhiteSpace(artist) ? "Unknown artist" : artist.Trim();

    private static string Clamp(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;
        return value[..maxLength];
    }

    private static string LargeImageTextFromComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return string.Empty;

        var singleLine = comment.Trim().ReplaceLineEndings(" ");
        return Clamp(singleLine, MaxLargeImageTextLength);
    }
}
