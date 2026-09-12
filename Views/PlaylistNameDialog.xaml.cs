using System.Windows;

namespace LocalMusicPlayer.Views;

public partial class PlaylistNameDialog : Window
{
    public string PlaylistName => PlaylistNameBox.Text;

    public PlaylistNameDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PlaylistNameBox.Focus();
    }

    private void Create_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
