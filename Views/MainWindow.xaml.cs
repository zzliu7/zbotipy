using System.ComponentModel;
using System.Drawing;
using DrawingIcon = System.Drawing.Icon;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LocalMusicPlayer.Models;
using LocalMusicPlayer.ViewModels;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using WinFormsMenu = System.Windows.Forms.ContextMenuStrip;
using WinFormsMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace LocalMusicPlayer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private LyricsWindow? _lyricsWindow;
    private FormsNotifyIcon? _trayIcon;
    private bool _forceClose;
    private System.Windows.Point _trackDragStart;
    private ListBoxItem? _selectionToCollapseOnMouseUp;
    private bool _trackDragStarted;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.MainLyricsVisible) && _viewModel.MainLyricsVisible)
        {
            Dispatcher.BeginInvoke(ScrollCurrentLyricIntoView, DispatcherPriority.Background);
            return;
        }

        if (e.PropertyName != nameof(MainViewModel.CurrentLyricLineIndex))
            return;

        if (!_viewModel.MainLyricsVisible)
            return;

        Dispatcher.BeginInvoke(ScrollCurrentLyricIntoView, DispatcherPriority.Background);
    }

    private void ScrollCurrentLyricIntoView()
    {
        var idx = _viewModel.CurrentLyricLineIndex;
        if (idx < 0 || idx >= LyricsLinesList.Items.Count)
            return;

        LyricsLinesList.ScrollIntoView(LyricsLinesList.Items[idx]);

        void CenterCurrentLineInViewport()
        {
            try
            {
                var i = _viewModel.CurrentLyricLineIndex;
                if (i < 0 || i >= LyricsLinesList.Items.Count)
                    return;

                if (LyricsLinesList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                    return;

                if (FindScrollViewer(LyricsLinesList) is not ScrollViewer scroll)
                    return;

                LyricsLinesList.UpdateLayout();
                scroll.UpdateLayout();

                var p = container.TransformToAncestor(scroll).Transform(new System.Windows.Point(0, 0));
                var offset = scroll.VerticalOffset + p.Y + container.ActualHeight * 0.5 - scroll.ViewportHeight * 0.5;
                var max = Math.Max(0, scroll.ExtentHeight - scroll.ViewportHeight);
                scroll.ScrollToVerticalOffset(Math.Clamp(offset, 0, max));
            }
            catch
            {
                // Virtualization/layout not ready yet
            }
        }

        Dispatcher.BeginInvoke(CenterCurrentLineInViewport, DispatcherPriority.Loaded);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? root)
    {
        if (root is null)
            return null;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv)
                return sv;

            if (FindScrollViewer(child) is { } found)
                return found;
        }

        return null;
    }

    private void TrackList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox && listBox.SelectedItem is Track track)
            _viewModel.LoadAndPlayTrack(track);
    }

    private void CreatePlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new PlaylistNameDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            PlaylistList.ScrollIntoView(_viewModel.CreatePlaylist(dialog.PlaylistName));
    }

    private void TrackList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _trackDragStart = e.GetPosition(TrackList);
        _trackDragStarted = false;
        _selectionToCollapseOnMouseUp = null;

        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);

        if (Keyboard.IsKeyDown(Key.Enter))
        {
            if (item is null)
                return;

            item.IsSelected = !item.IsSelected;
            item.Focus();
            e.Handled = true;
            return;
        }

        // Like File Explorer, starting a drag on one of several selected items keeps the group selected.
        if (item is { IsSelected: true } && TrackList.SelectedItems.Count > 1 &&
            Keyboard.Modifiers == ModifierKeys.None)
        {
            _selectionToCollapseOnMouseUp = item;
            item.Focus();
            e.Handled = true;
        }
    }

    private void TrackList_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectionToCollapseOnMouseUp is { } item && !_trackDragStarted)
        {
            TrackList.SelectedItems.Clear();
            item.IsSelected = true;
        }

        _selectionToCollapseOnMouseUp = null;
    }

    private void TrackList_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || TrackList.SelectedItems.Count == 0)
            return;

        var current = e.GetPosition(TrackList);
        if (Math.Abs(current.X - _trackDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _trackDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var tracks = TrackList.SelectedItems.Cast<Track>().ToList();
        _trackDragStarted = true;
        System.Windows.DragDrop.DoDragDrop(
            TrackList,
            new System.Windows.DataObject(typeof(List<Track>), tracks),
            System.Windows.DragDropEffects.Copy);
        _selectionToCollapseOnMouseUp = null;
    }

    private void PlaylistList_OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(List<Track>)) &&
                    FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void PlaylistList_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(typeof(List<Track>)) is not List<Track> tracks ||
            FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is not Playlist playlist)
            return;

        _viewModel.AddTracksToPlaylist(tracks, playlist);
        e.Handled = true;
    }

    private void MainWindow_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
            return;

        if (PlaylistList.IsKeyboardFocusWithin && PlaylistList.SelectedItem is Playlist playlist)
        {
            var playlistDialog = new DeletePlaylistDialog { Owner = this };
            if (playlistDialog.ShowDialog() == true)
                _viewModel.DeletePlaylist(playlist);

            e.Handled = true;
            return;
        }

        if (TrackList.SelectedItems.Count == 0 || !TrackList.IsKeyboardFocusWithin)
            return;

        var tracks = TrackList.SelectedItems.Cast<Track>().ToList();
        var dialog = new DeleteSongsDialog(tracks.Count) { Owner = this };
        if (dialog.ShowDialog() == true)
            _viewModel.DeleteTracks(tracks);

        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void PlaybackSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _viewModel.BeginSliderSeek();
    }

    private void MainWindow_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Defer so Slider.Value and bindings update before we read ProgressValue (tunneling hits the window first).
        Dispatcher.BeginInvoke(() => _viewModel.FinishSliderSeek(), DispatcherPriority.Input);
    }

    internal void RefreshFloatingLyricsButtonPresentation()
    {
        var visible = _lyricsWindow is { IsVisible: true };
        FloatingLyricsButton.ToolTip = visible
            ? "Hide floating lyrics"
            : (_lyricsWindow is null ? "Floating lyrics window" : "Show floating lyrics");
        _viewModel.FloatingLyricsVisible = visible;
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose || !_viewModel.MinimizeToTray)
            return;

        e.Cancel = true;
        Hide();
        EnsureTrayIcon().Visible = true;
    }

    private FormsNotifyIcon EnsureTrayIcon()
    {
        if (_trayIcon is not null)
            return _trayIcon;

        _trayIcon = new FormsNotifyIcon
        {
            Text = "Zbotipy",
            Visible = false,
            Icon = LoadTrayIcon()
        };

        var menu = new WinFormsMenu();
        menu.Items.Add(new WinFormsMenuItem("Open", null, (_, _) => Dispatcher.BeginInvoke(RestoreFromTray)));
        menu.Items.Add(new WinFormsMenuItem("Exit", null, (_, _) => Dispatcher.BeginInvoke(QuitFromTray)));
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(RestoreFromTray);
        return _trayIcon;
    }

    private static DrawingIcon LoadTrayIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                using var extracted = DrawingIcon.ExtractAssociatedIcon(path);
                if (extracted is not null)
                    return new DrawingIcon(extracted, extracted.Size);
            }
        }
        catch
        {
            // fall through
        }

        return (DrawingIcon)SystemIcons.Application.Clone();
    }

    private void RestoreFromTray()
    {
        if (_trayIcon is not null)
            _trayIcon.Visible = false;

        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void QuitFromTray()
    {
        _forceClose = true;
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        Close();
    }

    private void FloatingLyrics_OnClick(object sender, RoutedEventArgs e)
    {
        if (_lyricsWindow is null)
        {
            // No Owner: owned windows are minimized with the main window; keep lyrics independent so minimize leaves it visible.
            _lyricsWindow = new LyricsWindow
            {
                DataContext = _viewModel
            };
            _lyricsWindow.Closed += (_, _) =>
            {
                _lyricsWindow = null;
                RefreshFloatingLyricsButtonPresentation();
            };
            _lyricsWindow.Left = Left + (Width - _lyricsWindow.Width) / 2;
            _lyricsWindow.Top = Top + 48;
            _lyricsWindow.Show();
        }
        else if (_lyricsWindow.IsVisible)
            _lyricsWindow.Hide();
        else
            _lyricsWindow.Show();

        RefreshFloatingLyricsButtonPresentation();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _lyricsWindow?.Close();
        _lyricsWindow = null;
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
