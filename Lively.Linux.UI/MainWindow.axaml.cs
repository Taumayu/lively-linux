using Avalonia.Controls;
using Avalonia.Interactivity;
using Grpc.Net.Client;
using System.Net.Sockets;
using Lively.Grpc.Common.Proto.Desktop;
using System;
using System.Net.Http;
using Lively.Gallery.Client;
using Microsoft.Extensions.DependencyInjection;
using Lively.Models.Gallery.API;
using Lively.Models;

namespace Lively.Linux.UI;

public partial class MainWindow : Window
{
    private DesktopService.DesktopServiceClient _client;
    private GalleryClient _galleryClient;

    public MainWindow()
    {
        InitializeComponent();
        
        EnsureDaemonRunning();
        
        var socketPath = "/tmp/lively-grpc.sock";
        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, token) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            }
        });

        _client = new DesktopService.DesktopServiceClient(channel);

        var services = new ServiceCollection();
        services.AddHttpClient();
        var provider = services.BuildServiceProvider();
        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        _galleryClient = new GalleryClient(httpClientFactory, "https://api.livelywallpaper.net/api/", "", "", new JsonTokenStore());
        
        EnsureDaemonRunning();
        LoadDisplays();
        LoadWallpapers();
        UpdateAutostartButtonText();
    }

    private void UpdateAutostartButtonText()
    {
        bool isAutostart = AutostartManager.IsAutostartEnabled();
        AutostartButton.Content = isAutostart ? "Disable Autostart (On Boot)" : "Enable Autostart (On Boot)";
        AutostartButton.Background = isAutostart ? Avalonia.Media.Brushes.Green : Avalonia.Media.Brushes.Gray;
    }

    private void AutostartButton_Click(object sender, RoutedEventArgs e)
    {
        AutostartManager.ToggleAutostart();
        UpdateAutostartButtonText();
        StatusTextBlock.Text = "Status: Autostart setting updated.";
    }

    private void EnsureDaemonRunning()
    {
        var socketPath = "/tmp/lively-grpc.sock";
        if (!System.IO.File.Exists(socketPath))
        {
            try 
            {
                var uiDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                var prodDaemon = System.IO.Path.Combine(uiDir, "Lively.Linux.Daemon");
                var scriptDaemon = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lively-linux", "daemon", "Lively.Linux.Daemon");
                var devDaemonProject = System.IO.Path.GetFullPath(System.IO.Path.Combine(uiDir, "..", "..", "..", "..", "Lively.Linux.Daemon", "Lively.Linux.Daemon.csproj"));
                
                System.Diagnostics.ProcessStartInfo pInfo = null;

                if (System.IO.File.Exists(prodDaemon))
                {
                    pInfo = new System.Diagnostics.ProcessStartInfo { FileName = prodDaemon, UseShellExecute = false, CreateNoWindow = true };
                }
                else if (System.IO.File.Exists(devDaemonProject))
                {
                    pInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"run --project \"{devDaemonProject}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                }
                else if (System.IO.File.Exists(scriptDaemon))
                {
                    pInfo = new System.Diagnostics.ProcessStartInfo { FileName = scriptDaemon, UseShellExecute = false, CreateNoWindow = true };
                }

                if (pInfo != null)
                {
                    var dotnetRoot = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
                    if (System.IO.Directory.Exists(dotnetRoot))
                    {
                        pInfo.Environment["DOTNET_ROOT"] = dotnetRoot;
                        pInfo.Environment["PATH"] = dotnetRoot + ":" + (Environment.GetEnvironmentVariable("PATH") ?? "");
                    }
                    System.Diagnostics.Process.Start(pInfo);
                    System.Threading.Thread.Sleep(2000); // Wait for daemon to boot and bind socket
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to auto-start daemon: " + ex.Message);
            }
        }
    }

    private void LoadDisplays()
    {
        var displays = WaylandDisplayScanner.GetDisplays();
        if (displays.Count == 0)
        {
            displays.Add(new DisplayInfo { Name = "eDP-1", Description = "Fallback Display", IsEnabled = true });
        }
        
        DisplayComboBox.ItemsSource = displays;
        if (displays.Count > 0)
            DisplayComboBox.SelectedIndex = 0;
    }

    private async void LoadWallpapers()
    {
        try 
        {
            var localPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lively-linux", "wallpapers");
            if (!System.IO.Directory.Exists(localPath)) System.IO.Directory.CreateDirectory(localPath);
            
            LocalLibraryScanner.EnsureDefaultWallpapers(localPath);

            var localWallpapers = LocalLibraryScanner.ScanLibrary(localPath);
            LocalWallpapersListBox.ItemsSource = localWallpapers;

            StatusTextBlock.Text = $"Status: Loaded {localWallpapers.Count} local wallpapers.";

            try 
            {
                var query = new SearchQueryBuilder().Build();
                var page = await _galleryClient.SearchWallpapers(query);
                WallpapersListBox.ItemsSource = page.Data;
                StatusTextBlock.Text = $"Status: Loaded {localWallpapers.Count} local, {page.Data.Count} online.";
            }
            catch (Exception ex)
            {
                Console.WriteLine("Online gallery unavailable: " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Error loading wallpapers: " + ex.Message;
        }
    }

    private void LocalWallpapersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LocalWallpapersListBox.SelectedItem is LivelyInfoModel selected)
        {
            WallpaperPathTextBox.Text = selected.Id;
        }
    }

    private void WallpapersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WallpapersListBox.SelectedItem is WallpaperDto selected)
        {
            WallpaperPathTextBox.Text = selected.Id;
        }
    }

    private async void DownloadWallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        if (WallpapersListBox.SelectedItem is WallpaperDto selected)
        {
            StatusTextBlock.Text = $"Downloading {selected.Title}...";
            try
            {
                var localPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lively-linux", "wallpapers");
                if (!System.IO.Directory.Exists(localPath)) System.IO.Directory.CreateDirectory(localPath);
                
                var zipPath = System.IO.Path.Combine(localPath, $"{selected.Id}.zip");
                
                await _galleryClient.DownloadWallpaperAsync(selected.Id, zipPath, System.Threading.CancellationToken.None);
                
                var extractPath = System.IO.Path.Combine(localPath, selected.Id);
                if (System.IO.Directory.Exists(extractPath))
                    System.IO.Directory.Delete(extractPath, true);
                
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);
                System.IO.File.Delete(zipPath);

                StatusTextBlock.Text = $"Downloaded & Extracted!";
                LoadWallpapers(); // Refresh local list
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Download Error (Login required?): " + ex.Message;
            }
        }
        else
        {
            StatusTextBlock.Text = "Please select a gallery wallpaper first.";
        }
    }

    private async void SetWallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        StatusTextBlock.Text = "Status: Sending request...";

        try 
        {
            var displayId = "eDP-1";
            if (DisplayComboBox.SelectedItem is DisplayInfo item)
            {
                displayId = item.Name;
            }

            await _client.SetWallpaperAsync(new SetWallpaperRequest { 
                LivelyInfoPath = WallpaperPathTextBox.Text,
                MonitorId = displayId
            });
            StatusTextBlock.Text = $"Status: Success! Sent to {displayId}";
        }
        catch (Exception ex) 
        {
            StatusTextBlock.Text = "Error: " + ex.Message;
        }
    }

    private async void CloseWallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        StatusTextBlock.Text = "Status: Closing wallpapers...";
        try 
        {
            await _client.CloseAllWallpapersAsync(new CloseAllWallpapersRequest { Terminate = true });
            StatusTextBlock.Text = "Status: All wallpapers closed.";
        }
        catch (Exception ex) 
        {
            StatusTextBlock.Text = "Error: " + ex.Message;
        }
    }
}