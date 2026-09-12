using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using LocalMusicPlayer.Models;
using LocalMusicPlayer.Services;

namespace LocalMusicPlayer.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    public const string NoLyricsPlaceholder = "No Lyrics... sigh";

    private readonly AudioPlayerService _audio = new();
    private readonly DiscordService _discord = new();
    private readonly UserPreferences _preferences = UserPreferences.Load();
    private readonly DispatcherTimer _playbackTimer;
    private DateTime _nextDiscordPositionSyncUtc = DateTime.MinValue;
    private bool _audioLoaded;
    private bool _disposed;
    private bool _sliderSeekInProgress;
    private int _currentIndex = -1;

    private readonly ObservableCollection<Track> _emptyTracks = new();

    public ObservableCollection<Playlist> Playlists { get; } = new();

    private Playlist? _selectedPlaylist;
    public Playlist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            if (!SetProperty(ref _selectedPlaylist, value))
                return;

            ResetPlaybackState();
            SelectedTrack = null;
            OnPropertyChanged(nameof(Tracks));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Tracks in the selected playlist (main list and playback).</summary>
    public ObservableCollection<Track> Tracks => SelectedPlaylist?.Tracks ?? _emptyTracks;

    public LyricsService Lyrics { get; } = new();

    public bool DiscordRichPresenceEnabled
    {
        get => _discord.DiscordEnabled;
        set
        {
            if (_discord.DiscordEnabled == value)
                return;

            _discord.DiscordEnabled = value;
            OnPropertyChanged(nameof(DiscordRichPresenceEnabled));
        }
    }

    public bool MinimizeToTray
    {
        get => _preferences.MinimizeToTray;
        set
        {
            if (_preferences.MinimizeToTray == value)
                return;

            _preferences.MinimizeToTray = value;
            _preferences.Save();
            OnPropertyChanged(nameof(MinimizeToTray));
        }
    }

    private PlaybackMode _currentPlaybackMode = PlaybackMode.Normal;
    public PlaybackMode CurrentPlaybackMode
    {
        get => _currentPlaybackMode;
        set
        {
            if (!SetProperty(ref _currentPlaybackMode, value))
                return;

            OnPropertyChanged(nameof(PlaybackModeNormalOpacity));
            OnPropertyChanged(nameof(PlaybackModeRepeatAllOpacity));
            OnPropertyChanged(nameof(PlaybackModeRepeatOneOpacity));
            OnPropertyChanged(nameof(PlaybackModeShuffleOpacity));
        }
    }

    public double PlaybackModeNormalOpacity => CurrentPlaybackMode == PlaybackMode.Normal ? 1 : 0.35;
    public double PlaybackModeRepeatAllOpacity => CurrentPlaybackMode == PlaybackMode.RepeatAll ? 1 : 0.35;
    public double PlaybackModeRepeatOneOpacity => CurrentPlaybackMode == PlaybackMode.RepeatOne ? 1 : 0.35;
    public double PlaybackModeShuffleOpacity => CurrentPlaybackMode == PlaybackMode.Shuffle ? 1 : 0.35;

    private Track? _selectedTrack;
    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (!SetProperty(ref _selectedTrack, value))
                return;

            CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _timeDisplay = "0:00 / 0:00";
    public string TimeDisplay
    {
        get => _timeDisplay;
        set => SetProperty(ref _timeDisplay, value);
    }

    private string _currentLyric = string.Empty;
    public string CurrentLyric
    {
        get => _currentLyric;
        set => SetProperty(ref _currentLyric, value);
    }

    private string _nextLyric = string.Empty;
    public string NextLyric
    {
        get => _nextLyric;
        set => SetProperty(ref _nextLyric, value);
    }

    public ObservableCollection<MainLyricLineItem> MainLyricLines { get; } = new();

    private int _currentLyricLineIndex = -1;
    public int CurrentLyricLineIndex
    {
        get => _currentLyricLineIndex;
        private set => SetProperty(ref _currentLyricLineIndex, value);
    }

    private bool _mainLyricsVisible;
    public bool MainLyricsVisible
    {
        get => _mainLyricsVisible;
        set => SetProperty(ref _mainLyricsVisible, value);
    }

    public bool IsAudioLoaded => _audioLoaded;

    private bool _hasLyricsForCurrentTrack;
    public bool HasLyricsForCurrentTrack
    {
        get => _hasLyricsForCurrentTrack;
        private set => SetProperty(ref _hasLyricsForCurrentTrack, value);
    }

    private bool _floatingLyricsVisible;
    public bool FloatingLyricsVisible
    {
        get => _floatingLyricsVisible;
        set => SetProperty(ref _floatingLyricsVisible, value);
    }

    private double _progressValue;
    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    private double _progressMaximum = 1;
    public double ProgressMaximum
    {
        get => _progressMaximum;
        set => SetProperty(ref _progressMaximum, value);
    }

    private string _playPauseIconGlyph = "\uE102"; // Play (Segoe MDL2 Assets)
    public string PlayPauseIconGlyph
    {
        get => _playPauseIconGlyph;
        private set => SetProperty(ref _playPauseIconGlyph, value);
    }

    public ICommand LoadFolderCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand ToggleMainLyricsCommand { get; }
    public ICommand ToggleDiscordPresenceCommand { get; }
    public ICommand ToggleMinimizeToTrayCommand { get; }
    public ICommand SetPlaybackModeNormalCommand { get; }
    public ICommand SetPlaybackModeRepeatAllCommand { get; }
    public ICommand SetPlaybackModeRepeatOneCommand { get; }
    public ICommand SetPlaybackModeShuffleCommand { get; }

    public MainViewModel()
    {
        _playbackTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _playbackTimer.Tick += OnPlaybackTick;
        _audio.PlaybackEnded += OnAudioPlaybackEnded;

        LoadFolderCommand = new RelayCommand(LoadFolder);
        PlayPauseCommand = new RelayCommand(
            TogglePlayPause,
            () => !_disposed && (_audioLoaded || SelectedTrack is not null));
        NextCommand = new RelayCommand(NextTrack, () => Tracks.Count > 0);
        PreviousCommand = new RelayCommand(PreviousTrack, () => Tracks.Count > 0);
        ToggleMainLyricsCommand = new RelayCommand(ToggleMainLyrics);
        ToggleDiscordPresenceCommand = new RelayCommand(() => DiscordRichPresenceEnabled = !DiscordRichPresenceEnabled);
        ToggleMinimizeToTrayCommand = new RelayCommand(() => MinimizeToTray = !MinimizeToTray);
        SetPlaybackModeNormalCommand = new RelayCommand(() => CurrentPlaybackMode = PlaybackMode.Normal);
        SetPlaybackModeRepeatAllCommand = new RelayCommand(() => CurrentPlaybackMode = PlaybackMode.RepeatAll);
        SetPlaybackModeRepeatOneCommand = new RelayCommand(() => CurrentPlaybackMode = PlaybackMode.RepeatOne);
        SetPlaybackModeShuffleCommand = new RelayCommand(() => CurrentPlaybackMode = PlaybackMode.Shuffle);

        try
        {
            _discord.Initialize();
        }
        catch
        {
            // Non-fatal if Discord is unavailable
        }

        LoadPlaylistsFromLibraryDisk();
    }

    public void TestDiscordRichPresence() => _discord.ApplyTestPresence();

    private void LoadFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description =
                "Select a folder: MP3 and LRC files are copied to your Playlist library. Subfolders become playlists; MP3s are ordered by file name in each.",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        ResetPlaybackState();
        try
        {
            PlaylistLibrary.CopyFromSourceFolder(dialog.SelectedPath);
        }
        catch (Exception)
        {
            // Copy failures should not crash the app; still try to refresh from disk.
        }

        LoadPlaylistsFromLibraryDisk();

        CommandManager.InvalidateRequerySuggested();
    }

    private void LoadPlaylistsFromLibraryDisk()
    {
        Playlists.Clear();
        _selectedPlaylist = null;
        OnPropertyChanged(nameof(SelectedPlaylist));
        OnPropertyChanged(nameof(Tracks));
        SelectedTrack = null;

        PlaylistLibrary.EnsureRootExists();
        var rootPath = PlaylistLibrary.RootPath;
        var rootDir = new DirectoryInfo(rootPath);
        var subDirs = rootDir
            .EnumerateDirectories()
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (subDirs.Count > 0)
        {
            foreach (var sub in subDirs)
            {
                var playlist = new Playlist { Name = sub.Name, DirectoryPath = sub.FullName };
                foreach (var path in Directory
                             .EnumerateFiles(sub.FullName, "*.mp3", SearchOption.TopDirectoryOnly)
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    playlist.Tracks.Add(TrackMetadataReader.Read(path));

                Playlists.Add(playlist);
            }
        }
        else
        {
            var playlist = new Playlist { Name = rootDir.Name, DirectoryPath = rootDir.FullName };
            foreach (var path in Directory
                         .EnumerateFiles(rootPath, "*.mp3", SearchOption.TopDirectoryOnly)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                playlist.Tracks.Add(TrackMetadataReader.Read(path));

            Playlists.Add(playlist);
        }

        SelectedPlaylist = Playlists.Count > 0 ? Playlists[0] : null;
        CommandManager.InvalidateRequerySuggested();
    }

    public Playlist CreatePlaylist(string? requestedName)
    {
        var name = PlaylistLibrary.CreatePlaylist(requestedName);
        var playlist = new Playlist
        {
            Name = name,
            DirectoryPath = Path.Combine(PlaylistLibrary.RootPath, name)
        };

        var insertAt = 0;
        while (insertAt < Playlists.Count &&
               StringComparer.OrdinalIgnoreCase.Compare(Playlists[insertAt].Name, name) < 0)
            insertAt++;

        Playlists.Insert(insertAt, playlist);
        SelectedPlaylist = playlist;
        return playlist;
    }

    public void AddTracksToPlaylist(IEnumerable<Track> tracks, Playlist destination)
    {
        foreach (var track in tracks.DistinctBy(t => t.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            var destinationPath = PlaylistLibrary.AddTrackToPlaylist(track.FilePath, destination.DirectoryPath);
            if (destinationPath is null || destination.Tracks.Any(t =>
                    string.Equals(t.FilePath, destinationPath, StringComparison.OrdinalIgnoreCase)))
                continue;

            destination.Tracks.Add(TrackMetadataReader.Read(destinationPath));
        }
    }

    public void DeleteTracks(IEnumerable<Track> tracks)
    {
        if (SelectedPlaylist is null)
            return;

        var tracksToDelete = tracks
            .Where(SelectedPlaylist.Tracks.Contains)
            .Distinct()
            .ToList();
        if (tracksToDelete.Count == 0)
            return;

        if (SelectedTrack is not null && tracksToDelete.Contains(SelectedTrack))
            ResetPlaybackState();

        foreach (var track in tracksToDelete)
        {
            PlaylistLibrary.DeleteTrack(track.FilePath);
            SelectedPlaylist.Tracks.Remove(track);
        }

        SelectedTrack = null;
        CommandManager.InvalidateRequerySuggested();
    }

    public void DeletePlaylist(Playlist playlist)
    {
        var index = Playlists.IndexOf(playlist);
        if (index < 0)
            return;

        if (ReferenceEquals(SelectedPlaylist, playlist))
            ResetPlaybackState();

        PlaylistLibrary.DeletePlaylist(playlist.DirectoryPath);
        Playlists.RemoveAt(index);

        if (Playlists.Count == 0)
            SelectedPlaylist = null;
        else
            SelectedPlaylist = Playlists[Math.Min(index, Playlists.Count - 1)];

        CommandManager.InvalidateRequerySuggested();
    }

    private void ResetPlaybackState()
    {
        if (_disposed)
            return;

        if (_audioLoaded)
            _audio.Stop();

        _audioLoaded = false;
        OnPropertyChanged(nameof(IsAudioLoaded));
        _currentIndex = -1;
        TimeDisplay = "0:00 / 0:00";
        ProgressValue = 0;
        ProgressMaximum = 1;
        _playbackTimer.Stop();
        Lyrics.Clear();
        HasLyricsForCurrentTrack = false;
        CurrentLyric = string.Empty;
        NextLyric = string.Empty;
        MainLyricLines.Clear();
        CurrentLyricLineIndex = -1;
        _discord.Clear();
        _nextDiscordPositionSyncUtc = DateTime.MinValue;
        RefreshPlayPauseLabel();
        CommandManager.InvalidateRequerySuggested();
    }

    public void LoadAndPlayTrack(Track track)
    {
        if (_disposed)
            return;

        var idx = Tracks.IndexOf(track);
        if (idx >= 0)
            _currentIndex = idx;

        SelectedTrack = track;

        Lyrics.LoadForAudioFile(track.FilePath);
        RebuildLyricLineTexts();
        _audio.Load(track.FilePath);
        _audioLoaded = true;
        OnPropertyChanged(nameof(IsAudioLoaded));

        ProgressMaximum = Math.Max(_audio.TotalTime.TotalSeconds, 0.01);
        ProgressValue = 0;
        RefreshPlaybackUi();

        if (!_playbackTimer.IsEnabled)
            _playbackTimer.Start();

        _audio.Play();
        PushDiscordPlaying(TimeSpan.Zero);
        RefreshPlayPauseLabel();
        CommandManager.InvalidateRequerySuggested();
    }

    private void NextTrack()
    {
        if (_disposed || Tracks.Count == 0)
            return;

        switch (CurrentPlaybackMode)
        {
            case PlaybackMode.RepeatOne:
                ReplayCurrent();
                break;
            case PlaybackMode.Shuffle:
                PlayAtIndex(PickRandomIndex());
                break;
            case PlaybackMode.RepeatAll:
                {
                    var next = _currentIndex < 0 ? 0 : (_currentIndex + 1) % Tracks.Count;
                    PlayAtIndex(next);
                    break;
                }
            case PlaybackMode.Normal:
            default:
                if (_currentIndex < 0)
                    PlayAtIndex(0);
                else if (_currentIndex >= Tracks.Count - 1)
                    StopPlayback();
                else
                    PlayAtIndex(_currentIndex + 1);
                break;
        }
    }

    private void PreviousTrack()
    {
        if (_disposed || Tracks.Count == 0)
            return;

        switch (CurrentPlaybackMode)
        {
            case PlaybackMode.RepeatOne:
                ReplayCurrent();
                break;
            case PlaybackMode.Shuffle:
                PlayAtIndex(PickRandomIndex());
                break;
            case PlaybackMode.RepeatAll:
                {
                    var prev = _currentIndex < 0 ? Tracks.Count - 1 : (_currentIndex - 1 + Tracks.Count) % Tracks.Count;
                    PlayAtIndex(prev);
                    break;
                }
            case PlaybackMode.Normal:
            default:
                if (_currentIndex <= 0)
                    return;

                PlayAtIndex(_currentIndex - 1);
                break;
        }
    }

    private void PlayAtIndex(int index)
    {
        if (index < 0 || index >= Tracks.Count)
            return;

        LoadAndPlayTrack(Tracks[index]);
    }

    private void ReplayCurrent()
    {
        if (_currentIndex >= 0 && _currentIndex < Tracks.Count)
            LoadAndPlayTrack(Tracks[_currentIndex]);
        else if (SelectedTrack is not null)
            LoadAndPlayTrack(SelectedTrack);
        else if (Tracks.Count > 0)
            LoadAndPlayTrack(Tracks[0]);
    }

    private int PickRandomIndex()
    {
        if (Tracks.Count <= 1)
            return 0;

        int n;
        do
        {
            n = Random.Shared.Next(Tracks.Count);
        } while (n == _currentIndex);

        return n;
    }

    public void BeginSliderSeek()
    {
        if (!_audioLoaded || _disposed)
            return;

        _sliderSeekInProgress = true;
    }

    public void FinishSliderSeek()
    {
        if (!_sliderSeekInProgress || !_audioLoaded || _disposed)
            return;

        _sliderSeekInProgress = false;

        var seconds = Math.Clamp(ProgressValue, 0, ProgressMaximum);
        _audio.Seek(TimeSpan.FromSeconds(seconds));
        RefreshPlaybackUi();
        if (_audio.IsPlaying)
            PushDiscordPlaying(_audio.CurrentTime);
    }

    private void OnPlaybackTick(object? sender, EventArgs e) => RefreshPlaybackUi();

    private void RefreshPlaybackUi()
    {
        if (_disposed || !_audioLoaded)
            return;

        var current = _audio.CurrentTime;
        var total = _audio.TotalTime;
        TimeDisplay = $"{FormatMmSs(current)} / {FormatMmSs(total)}";

        if (Lyrics.TimedLines.Count == 0)
        {
            CurrentLyric = NoLyricsPlaceholder;
            NextLyric = string.Empty;
            CurrentLyricLineIndex = -1;
        }
        else
        {
            var (lyric, nextLyric) = Lyrics.GetCurrentAndNextLyrics(current);
            CurrentLyric = lyric;
            NextLyric = nextLyric;
            CurrentLyricLineIndex = Lyrics.GetCurrentLineIndex(current);
        }

        if (!_sliderSeekInProgress)
        {
            var max = ProgressMaximum;
            var pos = Math.Clamp(current.TotalSeconds, 0, max);
            ProgressValue = pos;
        }

        RefreshPlayPauseLabel();
        SyncDiscordPlaybackWhilePlaying();
    }

    private static string FormatMmSs(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
            t = TimeSpan.Zero;

        var totalSeconds = (long)Math.Floor(t.TotalSeconds);
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return $"{minutes}:{seconds:D2}";
    }

    private void Play()
    {
        if (_disposed)
            return;

        if (!_audioLoaded)
        {
            if (SelectedTrack is null)
                return;

            LoadAndPlayTrack(SelectedTrack);
            return;
        }

        _audio.Play();
        if (SelectedTrack is not null)
            PushDiscordPlaying(_audio.CurrentTime);

        RefreshPlayPauseLabel();
        CommandManager.InvalidateRequerySuggested();
    }

    private void Pause()
    {
        if (!_audioLoaded)
            return;

        _audio.Pause();
        if (SelectedTrack is not null)
            _discord.UpdatePaused(SelectedTrack.Title, SelectedTrack.Artist ?? string.Empty, SelectedTrack.Comment);

        RefreshPlayPauseLabel();
        CommandManager.InvalidateRequerySuggested();
    }

    private void TogglePlayPause()
    {
        if (_disposed)
            return;

        if (_audioLoaded && _audio.IsPlaying)
            Pause();
        else
            Play();
    }

    private void RefreshPlayPauseLabel()
    {
        var glyph = _audioLoaded && _audio.IsPlaying ? "\uE103" : "\uE102"; // Pause : Play
        PlayPauseIconGlyph = glyph;
    }

    private void StopPlayback()
    {
        if (!_audioLoaded)
            return;

        _audio.Stop();
        _discord.Clear();
        _nextDiscordPositionSyncUtc = DateTime.MinValue;
        RefreshPlaybackUi();
        RefreshPlayPauseLabel();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RebuildLyricLineTexts()
    {
        MainLyricLines.Clear();
        for (var i = 0; i < Lyrics.TimedLines.Count; i++)
            MainLyricLines.Add(new MainLyricLineItem(i, Lyrics.TimedLines[i].Text));

        HasLyricsForCurrentTrack = Lyrics.TimedLines.Count > 0;
        CurrentLyricLineIndex = -1;
    }

    private void ToggleMainLyrics() => MainLyricsVisible = !MainLyricsVisible;

    private void PushDiscordPlaying(TimeSpan position)
    {
        if (SelectedTrack is null)
            return;

        _nextDiscordPositionSyncUtc = DateTime.UtcNow.AddSeconds(8);
        _discord.UpdatePlaying(SelectedTrack.Title, SelectedTrack.Artist ?? string.Empty, position, SelectedTrack.Comment);
    }

    private void SyncDiscordPlaybackWhilePlaying()
    {
        if (!_audioLoaded || !_audio.IsPlaying || SelectedTrack is null)
            return;

        var now = DateTime.UtcNow;
        if (now < _nextDiscordPositionSyncUtc)
            return;

        _nextDiscordPositionSyncUtc = now.AddSeconds(8);
        _discord.UpdatePlaying(SelectedTrack.Title, SelectedTrack.Artist ?? string.Empty, _audio.CurrentTime, SelectedTrack.Comment);
    }

    private void OnAudioPlaybackEnded(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        if (Dispatcher.CurrentDispatcher.CheckAccess())
            HandlePlaybackNaturalEnd();
        else
            Dispatcher.CurrentDispatcher.BeginInvoke(HandlePlaybackNaturalEnd, DispatcherPriority.Normal);
    }

    private void HandlePlaybackNaturalEnd()
    {
        if (_disposed || !_audioLoaded || Tracks.Count == 0)
            return;

        if (CurrentPlaybackMode == PlaybackMode.RepeatOne)
        {
            ReplayCurrent();
            return;
        }

        NextTrack();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _playbackTimer.Stop();
        _playbackTimer.Tick -= OnPlaybackTick;
        _audio.PlaybackEnded -= OnAudioPlaybackEnded;
        _discord.Dispose();
        _audio.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
