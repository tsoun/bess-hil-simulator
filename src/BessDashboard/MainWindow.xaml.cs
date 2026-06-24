using System.Net;
using System.Net.Sockets;
using System.Windows;
using Microsoft.AspNetCore.Builder;

namespace BessDashboard;

public partial class MainWindow : Window
{
    private WebApplication? _server;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        int port = GetFreePort();
        string url = $"http://localhost:{port}";

        _server = BessWebServer.Build(url);
        await _server.StartAsync();

        await WebView.EnsureCoreWebView2Async();
        WebView.CoreWebView2.NavigationCompleted += (_, _) =>
            Dispatcher.Invoke(() => Splash.Visibility = Visibility.Hidden);
        WebView.CoreWebView2.Navigate(url);
    }

    protected override void OnClosed(EventArgs e)
    {
        _server?.StopAsync().GetAwaiter().GetResult();
        base.OnClosed(e);
    }

    static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
