using System.IO;

namespace LocalMusicPlayer.Services;

public static class PlaylistLibrary
{
    public static string RootPath =>
        Path.Combine(@"F:\Documents\MyWorks\MusicPlayer", "Playlist");

    public static void EnsureRootExists() => Directory.CreateDirectory(RootPath);

    public static string CreatePlaylist(string? requestedName)
    {
        EnsureRootExists();
        var baseName = SanitizePlaylistName(requestedName);
        var name = baseName;
        var suffix = 1;

        while (Directory.Exists(Path.Combine(RootPath, name)))
            name = $"{baseName} ({suffix++})";

        Directory.CreateDirectory(Path.Combine(RootPath, name));
        return name;
    }

    public static string? AddTrackToPlaylist(string sourcePath, string destinationDirectory)
    {
        if (!File.Exists(sourcePath))
            return null;

        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
        if (!File.Exists(destinationPath))
            File.Copy(sourcePath, destinationPath);

        var sourceLyrics = Path.ChangeExtension(sourcePath, ".lrc");
        var destinationLyrics = Path.ChangeExtension(destinationPath, ".lrc");
        if (File.Exists(sourceLyrics) && !File.Exists(destinationLyrics))
            File.Copy(sourceLyrics, destinationLyrics);

        return destinationPath;
    }

    public static void DeleteTrack(string trackPath)
    {
        if (File.Exists(trackPath))
            File.Delete(trackPath);

        var lyricsPath = Path.ChangeExtension(trackPath, ".lrc");
        if (File.Exists(lyricsPath))
            File.Delete(lyricsPath);
    }

    public static void DeletePlaylist(string playlistDirectory)
    {
        var rootPath = Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar);
        var playlistPath = Path.GetFullPath(playlistDirectory).TrimEnd(Path.DirectorySeparatorChar);

        if (string.Equals(rootPath, playlistPath, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pattern in new[] { "*.mp3", "*.lrc" })
            foreach (var path in Directory.EnumerateFiles(rootPath, pattern, SearchOption.TopDirectoryOnly))
                File.Delete(path);
            return;
        }

        var expectedPrefix = rootPath + Path.DirectorySeparatorChar;
        if (!playlistPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The playlist is outside the playlist library.");

        if (Directory.Exists(playlistPath))
            Directory.Delete(playlistPath, recursive: true);
    }

    private static string SanitizePlaylistName(string? requestedName)
    {
        var name = string.IsNullOrWhiteSpace(requestedName) ? "Playlist" : requestedName.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            name = name.Replace(invalidCharacter, '_');

        name = name.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(name) ? "Playlist" : name;
    }

    public static void CopyFromSourceFolder(string sourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        if (!Directory.Exists(sourceRoot))
            return;

        EnsureRootExists();
        var rootDir = new DirectoryInfo(sourceRoot);
        var subDirs = rootDir
            .EnumerateDirectories()
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (subDirs.Count > 0)
        {
            foreach (var sub in subDirs)
            {
                var destDir = Path.Combine(RootPath, sub.Name);
                CopyMp3AndLrcFromDirectory(sub.FullName, destDir);
            }
        }
        else
        {
            var destDir = Path.Combine(RootPath, rootDir.Name);
            CopyMp3AndLrcFromDirectory(sourceRoot, destDir);
        }
    }

    private static void CopyMp3AndLrcFromDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var pattern in new[] { "*.mp3", "*.lrc" })
        {
            foreach (var path in Directory
                         .EnumerateFiles(sourceDir, pattern, SearchOption.TopDirectoryOnly)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var dest = Path.Combine(destDir, Path.GetFileName(path));
                try
                {
                    File.Copy(path, dest, overwrite: true);
                }
                catch (IOException)
                {
                    // Locked or transient IO — skip this file
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
