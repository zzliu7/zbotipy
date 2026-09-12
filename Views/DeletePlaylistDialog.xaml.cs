using System.Windows;

namespace LocalMusicPlayer.Views;

public partial class DeletePlaylistDialog : Window
{
    public DeletePlaylistDialog()
    {
        InitializeComponent();
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
