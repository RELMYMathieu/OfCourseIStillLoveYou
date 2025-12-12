using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OfCourseIStillLoveYou.Communication;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace OfCourseIStillLoveYou.DesktopClient;

public partial class MainWindow : Window
{
    private const int Delay = 10;
    private const string SettingPath = "settings.json";
    private const string Endpoint = "localhost";
    private const int Port = 5077;
    int cameraCount = 0;
    int cameraCountOld = 0;
    int sizeW = 1000;
    int sizeH = 1000;
    int camsizeH = 400;
    int camsizeW = 400;
    double imageRatio = 1;

    private Bitmap? _initialImage;

    private SettingsPoco? _settings;

    private byte[]? _previousTexture;

    private WriteableBitmap _wbmp;

    private bool _isFullscreen = false;
    private bool _isResizing = false;
    private Point _lastPointerPos;

    private PixelRect _savedBounds;
    private bool _hasSavedBounds = false;

    private List<string?> camIds = new List<string?>();

    private Channel<(int idx, Bitmap? bmp, string speed, string alt)> _frameChannel;
    private Image[] _camImages;
    private Grid[] _camContainers;
    private StackPanel[] _camSpeed;
    private StackPanel[] _camAlt;
    private const int UI_MIN_FRAME_MS = 16; // ~60 FPS

    private IDisposable? _windowStateSubscription;

    private static readonly Regex speedRegex = new Regex(@"\d+", RegexOptions.Compiled);
    private static readonly Regex altRegex = new Regex(@"\d+[.,]?\d*", RegexOptions.Compiled);

    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    public bool IsClosing { get; set; }


    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        StoreInitialImage();
        ReadSettings();

        if (_settings != null) GrpcClient.ConnectToServer(_settings.EndPoint, _settings.Port);

        _camImages = new Image[6];
        _camContainers = new Grid[6];
        _camSpeed = new StackPanel[6];
        _camAlt = new StackPanel[6];

        for (int i = 0; i < 6; i++)
        {
            int idx = i + 1;
            _camImages[i] = this.FindControl<Image>("imgCameraTexture" + idx);
            _camContainers[i] = this.FindControl<Grid>("c" + idx);
            _camSpeed[i] = this.FindControl<StackPanel>("TextInfoSpeed" + idx);
            _camAlt[i] = this.FindControl<StackPanel>("TextInfoAlt" + idx);
        }

        var opts = new BoundedChannelOptions(10)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        };
        _frameChannel = Channel.CreateBounded<(int, Bitmap?, string, string)>(opts);

        Task.Run(async () => await FrameConsumerAsync());
        Task.Run(async () => await CameraTextureWorkerAsync());
        Task.Run(CameraFetchWorker);

        this.SizeChanged += (_, __) => Resize();
    }

    private void ReadSettings()
    {
        try
        {
            var settingsText = File.ReadAllText(SettingPath);
            _settings = JsonSerializer.Deserialize<SettingsPoco>(settingsText);
        }
        catch (Exception)
        {
            _settings = new SettingsPoco { EndPoint = Endpoint, Port = Port };
        }
    }

    private void StoreInitialImage()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
            _initialImage = (Bitmap)this.FindControl<Image>("imgCameraTexture1").Source);
    }

    private async Task CameraTextureWorkerAsync()
    {
        try
        {
            while (!IsClosing)
            {
                await Task.Delay(Delay).ConfigureAwait(false);

                for (int i = 0; i < cameraCount; i++)
                {
                    if (IsClosing) break;
                    if (i >= 6) continue;
                    if (camIds == null || string.IsNullOrEmpty(camIds[i])) continue;

                    CameraData cameraData = null;
                    try
                    {
                        cameraData = await GrpcClient.GetCameraDataAsync(camIds[i]).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"GetCameraDataAsync failed for cam {camIds[i]}: {ex}");
                        continue;
                    }

                    if (cameraData == null || cameraData.Texture == null)
                    {
                        try { await _frameChannel.Writer.WriteAsync((i, (Bitmap?)null, "", "")).ConfigureAwait(false); }
                        catch (ChannelClosedException) { break; }
                        continue;
                    }

                    if (ByteArrayCompare(cameraData.Texture, _previousTexture)) continue;
                    _previousTexture = cameraData.Texture;

                    var bytes = new byte[cameraData.Texture.Length];
                    Buffer.BlockCopy(cameraData.Texture, 0, bytes, 0, cameraData.Texture.Length);

                    Bitmap? decodedBmp = null;
                    try
                    {
                        decodedBmp = await Task.Run(() =>
                        {
                            using var msLocal = new MemoryStream(bytes, writable: false);
                            return new Bitmap(msLocal);
                        }).ConfigureAwait(false);
                    }
                    catch (Exception decodeEx)
                    {
                        Console.WriteLine($"Decode failed for cam {i}: {decodeEx}");
                        decodedBmp = null;
                    }

                    var speed = speedRegex.Match(cameraData.Speed ?? "").Value;
                    var altitude = altRegex.Match(cameraData.Altitude ?? "").Value;

                    try
                    {
                        await _frameChannel.Writer.WriteAsync((i, decodedBmp, speed, altitude)).ConfigureAwait(false);
                    }
                    catch (ChannelClosedException) { break; }
                }
            }
        }
        finally
        {
            _frameChannel.Writer.TryComplete();
        }
    }

    private async Task FrameConsumerAsync()
    {
        await foreach (var item in _frameChannel.Reader.ReadAllAsync())
        {
            if (IsClosing) break;

            int idx = item.idx;
            Bitmap? bmp = item.bmp;
            string speed = item.speed;
            string alt = item.alt;

            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var img = _camImages[idx];
                    var cc = _camContainers[idx];
                    if (img != null)
                    {
                        if (bmp == null)
                        {
                            img.Source = _initialImage;
                            cc.ClipToBounds = false;
                            img.Stretch = Avalonia.Media.Stretch.Uniform;
                        }
                        else
                        {
                            img.Source = bmp;
                            cc.ClipToBounds = true;
                            img.Stretch = Avalonia.Media.Stretch.Fill;

                            double ratio = (double)bmp.PixelSize.Width / (double)bmp.PixelSize.Height;
                            if (imageRatio != ratio)
                            {
                                imageRatio = ratio;
                                Resize();
                            }
                        }
                    }

                    var tSpeed = _camSpeed[idx];
                    var tAlt = _camAlt[idx];
                    if (tSpeed != null && tAlt != null)
                    {
                        tSpeed.Children.Clear();
                        tAlt.Children.Clear();
                        if (!string.IsNullOrEmpty(speed) && !string.IsNullOrEmpty(alt))
                        {
                            var bigFontSize = Math.Max(camsizeH / 25, 10);
                            var smallFontSize = Math.Max(camsizeH / 50, 5);

                            tSpeed.Children.Add(new TextBlock { Text = "SPEED", FontSize = smallFontSize });
                            tSpeed.Children.Add(new TextBlock { Text = speed, FontSize = bigFontSize });
                            tSpeed.Children.Add(new TextBlock { Text = "KM/H", FontSize = smallFontSize });

                            tAlt.Children.Add(new TextBlock { Text = "ALTITUDE", FontSize = smallFontSize });
                            tAlt.Children.Add(new TextBlock { Text = alt, FontSize = bigFontSize });
                            tAlt.Children.Add(new TextBlock { Text = "KM", FontSize = smallFontSize });
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UI update exception for cam {idx}: {ex}");
            }
        }
    }

    static bool ByteArrayCompare(ReadOnlySpan<byte> a1, ReadOnlySpan<byte> a2)
    {
        return a1.SequenceEqual(a2);
    }

    private void CameraFetchWorker()
    {
        while (!IsClosing)
        {
            Task.Delay(1000).Wait();
            try
            {
                List<string>? cameraIds = GrpcClient.GetCameraIds();
                if (cameraIds == null || cameraIds.Count == 0)
                    Dispatcher.UIThread.InvokeAsync(NotifyWaitingForCameraFeed);
                else
                    Dispatcher.UIThread.InvokeAsync(ClearInfoText);

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cameraIds != null && cameraIds.Count != cameraCountOld)
                    {
                        UpdateCameraList(cameraIds);
                        cameraCountOld = cameraIds.Count;
                    }
                });
            }
            catch (Exception)
            {
                Dispatcher.UIThread.InvokeAsync(NotifyConnectingToServer);
            }
        }
    }

    private void SetImageSize(Image im, int maxsizeH, int camsizeH, bool scale)
    {
        if (scale)
        {
            im.Height = camsizeH;
            im.Width = (int)(camsizeH * imageRatio);
        }
        else
        {
            im.Height = maxsizeH;
            im.Width = (int)(maxsizeH * imageRatio);
        }
    }

    private void Resize()
    {
        var wd = this.FindControl<Window>("RootWindow");
        sizeW = (int)wd.Width;
        sizeH = (int)wd.Height;
        // if ((sizeWold != sizeW) || (sizeHold != sizeH) || (cameraNmbOld != cameraNmb))
        // {
            var MainCanvas = this.FindControl<Canvas>("mc");
            var InfoCanvas = this.FindControl<Canvas>("ic");
            var txtCanvas = this.FindControl<Canvas>("i1");
            var txtInfo = this.FindControl<TextBlock>("TextInfo");
            var cc7 = this.FindControl<Canvas>("im7c");
            var cc8 = this.FindControl<Canvas>("im8c");
            var cc9 = this.FindControl<Canvas>("im9c");

            var im8 = this.FindControl<Image>("ImgClose");
            var im9 = this.FindControl<Image>("ImgResize");
            var imbck = this.FindControl<Image>("background");

            var cc1 = this.FindControl<Grid>("c1");
            var cc2 = this.FindControl<Grid>("c2");
            var cc3 = this.FindControl<Grid>("c3");
            var cc4 = this.FindControl<Grid>("c4");
            var cc5 = this.FindControl<Grid>("c5");
            var cc6 = this.FindControl<Grid>("c6");
            var im1 = this.FindControl<Image>("imgCameraTexture1");
            var im2 = this.FindControl<Image>("imgCameraTexture2");
            var im3 = this.FindControl<Image>("imgCameraTexture3");
            var im4 = this.FindControl<Image>("imgCameraTexture4");
            var im5 = this.FindControl<Image>("imgCameraTexture5");
            var im6 = this.FindControl<Image>("imgCameraTexture6");

            MainCanvas.Width = sizeW;
            MainCanvas.Height = sizeH;
            Canvas.SetTop(MainCanvas, 0);
            Canvas.SetLeft(MainCanvas, 0);

            InfoCanvas.Width = sizeW;
            InfoCanvas.Height = 32;
            Canvas.SetTop(InfoCanvas, sizeH - 32);
            Canvas.SetLeft(InfoCanvas, 0);

            txtCanvas.Width = sizeW - 64;
            txtCanvas.Height = 32;
            Canvas.SetTop(txtCanvas, 0);
            Canvas.SetLeft(txtCanvas, 0);

            txtInfo.Width = sizeW - 64 * 2;
            txtInfo.Height = 32;
            Canvas.SetTop(txtInfo, 0);
            Canvas.SetLeft(txtInfo, 64);
            txtInfo.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            txtInfo.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;

            cc8.Width = 32;
            cc8.Height = 32;
            cc9.Width = 32;
            cc9.Height = 32;
            Canvas.SetTop(cc8, 0);
            Canvas.SetLeft(cc8, sizeW - 64);
            Canvas.SetTop(cc9, 0);
            Canvas.SetLeft(cc9, sizeW - 32);
            im8.Width = 32;
            im8.Height = 32;
            im9.Width = 32;
            im9.Height = 32;

            imbck.Width = sizeW;
            imbck.Height = sizeH;
            imbck.Stretch = Avalonia.Media.Stretch.Fill;

            if (cameraCount < 2)
            {
                cc1.IsVisible = true;
                cc2.IsVisible = false;
                cc3.IsVisible = false;
                cc4.IsVisible = false;
                cc5.IsVisible = false;
                cc6.IsVisible = false;

                var maxsizeH = sizeH;

                camsizeH = maxsizeH;
                camsizeW = Math.Min(sizeW, (int)(camsizeH * imageRatio));
                if (camsizeW < camsizeH)
                {
                    camsizeH = camsizeW;
                }

                cc1.Height = camsizeH;
                cc1.Width = camsizeW;
                Canvas.SetTop(cc1, (sizeH - camsizeH) / 2);
                Canvas.SetLeft(cc1, (sizeW - camsizeW) / 2);

                SetImageSize(im1, maxsizeH, camsizeH, camsizeW == camsizeH);
            }
            else if (cameraCount == 2)
            {
                cc1.IsVisible = true;
                cc2.IsVisible = true;
                cc3.IsVisible = false;
                cc4.IsVisible = false;
                cc5.IsVisible = false;
                cc6.IsVisible = false;

                var maxsizeH = sizeH;

                camsizeH = sizeH;
                camsizeW = Math.Min(sizeW / 2, (int)(camsizeH * imageRatio));
                if (camsizeW < camsizeH)
                {
                    camsizeH = camsizeW;
                }

                cc1.Height = camsizeH;
                cc1.Width = camsizeW;
                Canvas.SetTop(cc1, (sizeH - camsizeH) / 2);
                Canvas.SetLeft(cc1, (sizeW - 2 * camsizeW) / 2);

                cc2.Height = camsizeH;
                cc2.Width = camsizeW;
                Canvas.SetTop(cc2, (sizeH - camsizeH) / 2);
                Canvas.SetLeft(cc2, camsizeW + (sizeW - 2 * camsizeW) / 2);

                SetImageSize(im1, maxsizeH, camsizeH, camsizeW == camsizeH);
                SetImageSize(im2, maxsizeH, camsizeH, camsizeW == camsizeH);
            }
            else if (cameraCount == 3)
            {
                cc1.IsVisible = true;
                cc2.IsVisible = true;
                cc3.IsVisible = true;
                cc4.IsVisible = false;
                cc5.IsVisible = false;
                cc6.IsVisible = false;

                var maxsizeH = sizeH / 2;
                var maxsizeTopW = sizeW / 2;
                var maxsizeBotW = sizeW;

                camsizeH = maxsizeH;
                var camsizeTopW = Math.Min(maxsizeTopW, (int)(camsizeH * imageRatio));
                if (camsizeTopW < camsizeH)
                {
                    camsizeH = camsizeTopW;
                }

                var camsizeBotW = Math.Min(maxsizeBotW, (int)(camsizeH * imageRatio));

                cc1.Height = camsizeH;
                cc1.Width = camsizeTopW;
                Canvas.SetTop(cc1, (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc1, (sizeW - 2 * camsizeTopW) / 2);

                cc2.Height = camsizeH;
                cc2.Width = camsizeTopW;
                Canvas.SetTop(cc2, (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc2, camsizeTopW + (sizeW - 2 * camsizeTopW) / 2);

                cc3.Height = camsizeH;
                cc3.Width = camsizeBotW;
                Canvas.SetTop(cc3, camsizeH + (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc3, (sizeW - camsizeBotW) / 2);

                SetImageSize(im1, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im2, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im3, maxsizeH, camsizeH, camsizeTopW == camsizeH);
            }
            else if (cameraCount == 4)
            {
                cc1.IsVisible = true;
                cc2.IsVisible = true;
                cc3.IsVisible = true;
                cc4.IsVisible = true;
                cc5.IsVisible = false;
                cc6.IsVisible = false;

                var maxsizeH = sizeH / 2;
                var maxsizeTopW = sizeW / 2;
                var maxsizeBotW = sizeW / 2;

                camsizeH = maxsizeH;
                var camsizeTopW = Math.Min(maxsizeTopW, (int)(camsizeH * imageRatio));
                if (camsizeTopW < camsizeH)
                {
                    camsizeH = camsizeTopW;
                }

                var camsizeBotW = Math.Min(maxsizeBotW, (int)(camsizeH * imageRatio));

                cc1.Height = camsizeH;
                cc1.Width = camsizeTopW;
                Canvas.SetTop(cc1, (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc1, (sizeW - 2 * camsizeTopW) / 2);

                cc2.Height = camsizeH;
                cc2.Width = camsizeTopW;
                Canvas.SetTop(cc2, (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc2, camsizeTopW + (sizeW - 2 * camsizeTopW) / 2);

                cc3.Height = camsizeH;
                cc3.Width = camsizeBotW;
                Canvas.SetTop(cc3, camsizeH + (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc3, (sizeW - 2 * camsizeBotW) / 2);

                cc4.Height = camsizeH;
                cc4.Width = camsizeBotW;
                Canvas.SetTop(cc4, camsizeH + (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc4, camsizeBotW + (sizeW - 2 * camsizeBotW) / 2);

                SetImageSize(im1, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im2, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im3, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im4, maxsizeH, camsizeH, camsizeTopW == camsizeH);
            }
            else if (cameraCount == 5)
            {
                cc1.IsVisible = true;
                cc2.IsVisible = true;
                cc3.IsVisible = true;
                cc4.IsVisible = true;
                cc5.IsVisible = true;
                cc6.IsVisible = false;

                var maxsizeH = sizeH / 2;
                var maxsizeTopW = sizeW / 3;
                var maxsizeBotW = sizeW / 2;

                camsizeH = maxsizeH;
                var camsizeTopW = Math.Min(maxsizeTopW, (int)(camsizeH * imageRatio));
                if (camsizeTopW < camsizeH)
                {
                    camsizeH = camsizeTopW;
                }

                var camsizeBotW = Math.Min(maxsizeBotW, (int)(camsizeH * imageRatio));

                cc1.Height = camsizeH;
                cc1.Width = camsizeTopW;
                Canvas.SetTop(cc1, (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc1, (sizeW - 3 * camsizeTopW) / 2);

                cc2.Height = camsizeH;
                cc2.Width = camsizeTopW;
                Canvas.SetTop(cc2, (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc2, camsizeTopW + (sizeW - 3 * camsizeTopW) / 2);

                cc5.Height = camsizeH;
                cc5.Width = camsizeTopW;
                Canvas.SetTop(cc5, (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc5, 2 * camsizeTopW + (sizeW - 3 * camsizeTopW) / 2);

                cc3.Height = camsizeH;
                cc3.Width = camsizeBotW;
                Canvas.SetTop(cc3, camsizeH + (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc3, (sizeW - 2 * camsizeBotW) / 2);

                cc4.Height = camsizeH;
                cc4.Width = camsizeBotW;
                Canvas.SetTop(cc4, camsizeH + (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc4, camsizeBotW + (sizeW - 2 * camsizeBotW) / 2);

                SetImageSize(im1, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im2, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im3, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im4, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im5, maxsizeH, camsizeH, camsizeTopW == camsizeH);
            }
            else if (cameraCount >= 6)
            {
                cc1.IsVisible = true;
                cc2.IsVisible = true;
                cc3.IsVisible = true;
                cc4.IsVisible = true;
                cc5.IsVisible = true;
                cc6.IsVisible = true;

                var maxsizeH = sizeH / 2;
                var maxsizeTopW = sizeW / 3;
                var maxsizeBotW = sizeW / 3;

                camsizeH = maxsizeH;
                var camsizeTopW = Math.Min(maxsizeTopW, (int)(camsizeH * imageRatio));
                if (camsizeTopW < camsizeH)
                {
                    camsizeH = camsizeTopW;
                }

                var camsizeBotW = Math.Min(maxsizeBotW, (int)(camsizeH * imageRatio));

                cc1.Height = camsizeH;
                cc1.Width = camsizeTopW;
                Canvas.SetTop(cc1, (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc1, (sizeW - 3 * camsizeTopW) / 2);

                cc2.Height = camsizeH;
                cc2.Width = camsizeTopW;
                Canvas.SetTop(cc2, (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc2, camsizeTopW + (sizeW - 3 * camsizeTopW) / 2);

                cc5.Height = camsizeH;
                cc5.Width = camsizeTopW;
                Canvas.SetTop(cc5, (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc5, 2 * camsizeTopW + (sizeW - 3 * camsizeTopW) / 2);

                cc3.Height = camsizeH;
                cc3.Width = camsizeBotW;
                Canvas.SetTop(cc3, camsizeH + (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc3, (sizeW - 3 * camsizeBotW) / 2);

                cc4.Height = camsizeH;
                cc4.Width = camsizeBotW;
                Canvas.SetTop(cc4, camsizeH + (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc4, camsizeBotW + (sizeW - 3 * camsizeBotW) / 2);

                cc6.Height = camsizeH;
                cc6.Width = camsizeBotW;
                Canvas.SetTop(cc6, camsizeH + (sizeH - 2 * camsizeH) / 2);
                Canvas.SetLeft(cc6, 2 * camsizeBotW + (sizeW - 3 * camsizeBotW) / 2);

                SetImageSize(im1, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im2, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im3, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im4, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im5, maxsizeH, camsizeH, camsizeTopW == camsizeH);
                SetImageSize(im6, maxsizeH, camsizeH, camsizeTopW == camsizeH);
            }
        // }
    }

    private void NotifyWaitingForCameraFeed()
    {
        var textInfo = this.FindControl<TextBlock>("TextInfo");
        textInfo.IsVisible = true;
        textInfo.Text = "Waiting for camera feed...";
        this.FindControl<Image>("imgCameraTexture1").Source = _initialImage;
        this.FindControl<Image>("imgCameraTexture2").Source = _initialImage;
        this.FindControl<Image>("imgCameraTexture3").Source = _initialImage;
        this.FindControl<Image>("imgCameraTexture4").Source = _initialImage;
        this.FindControl<Image>("imgCameraTexture5").Source = _initialImage;
        this.FindControl<Image>("imgCameraTexture6").Source = _initialImage;
    }

    private void ClearInfoText()
    {
        var textInfo = this.FindControl<TextBlock>("TextInfo");
        textInfo.IsVisible = false;
        textInfo.Text = "";
    }

    private void NotifyConnectingToServer()
    {
        var textInfo = this.FindControl<TextBlock>("TextInfo");
        textInfo.IsVisible = true;
        textInfo.Text = "Connecting to server...";
    }

    private void UpdateCameraList(List<string?> cameraIds)
    {
        cameraCount = cameraIds.Count;

        camIds = camIds
            .Where(id => id != null && cameraIds.Contains(id))
            .ToList();

        var additions = cameraIds
            .Where(id => id != null && !camIds.Contains(id))
            .ToList();

        camIds.AddRange(additions);

        Resize();
    }

    private void ImgCameraTexture_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var window = this;
        var imgResize = this.FindControl<Image>("ImgResize");
        var imgClose = this.FindControl<Image>("ImgClose");
        var screen = Screens.Primary;

        if (!_isFullscreen)
        {
            _savedBounds = new PixelRect(
                this.Position,
                new PixelSize((int)this.Width, (int)this.Height)
            );
            _hasSavedBounds = true;

            imgResize.IsVisible = false;
            imgClose.IsVisible = false;

            var a = screen.WorkingArea;

            this.Position = a.Position;
            this.Width = a.Width;
            this.Height = a.Height;

            window.WindowState = WindowState.FullScreen;
            _isFullscreen = true;
        }
        else
        {
            window.WindowState = WindowState.Normal;

            imgResize.IsVisible = true;
            imgClose.IsVisible = true;

            this.Position = _savedBounds.Position;
            this.Width = _savedBounds.Size.Width;
            this.Height = _savedBounds.Size.Height;

            _isFullscreen = false;
        }
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void ImgResize_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isResizing = true;
            _lastPointerPos = e.GetPosition(this);
        }
    }

    private void ImgResize_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isResizing = false;
    }

    private void ImgResize_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isResizing && this is Window window)
        {
            var pos = e.GetPosition(window);
            var dx = pos.X - _lastPointerPos.X;
            var dy = pos.Y - _lastPointerPos.Y;

            window.Width = Math.Max(window.MinWidth, window.Width + dx);
            window.Height = Math.Max(window.MinHeight, window.Height + dy);

            _lastPointerPos = pos;
        }
    }

    private void ImgClose_OnTapped(object? sender, TappedEventArgs e)
    {
        Close();
    }

    private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        IsClosing = true;
    }
}
