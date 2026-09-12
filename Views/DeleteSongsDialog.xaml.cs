using System.Windows;

namespace LocalMusicPlayer.Views;

public partial class DeleteSongsDialog : Window
{
    public DeleteSongsDialog(int songCount)
    {
        InitializeComponent();
        QuestionText.Text = songCount == 1
            ? "Do you want to delete the song?"
            : $"Do you want to delete the {songCount} selected songs?";
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
