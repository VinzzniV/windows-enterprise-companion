namespace Wec.Host;

internal sealed class MainWindow : Form
{
    public MainWindow()
    {
        Text = "Windows Enterprise Companion";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 800);
    }
}
