using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

internal sealed class SheetWindow : Form
{
    private const string CharactersUrl = "https://longstoryshort.app/characters/list/";
    private CoreWebView2Controller? controller;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (Array.IndexOf(args, "--texture") < 0)
            return;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        int width = args.Length >= 3 && int.TryParse(args[1], out var initialWidth) ? initialWidth : 1200;
        int height = args.Length >= 3 && int.TryParse(args[2], out var initialHeight) ? initialHeight : 750;
        using var window = new SheetWindow(width, height);
        Application.Run(window);
    }

    private SheetWindow(int width, int height)
    {
        Text = "Long Story Short — Dicebound";
        ClientSize = ClampViewport(width, height);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(SystemInformation.VirtualScreen.Right + 100, SystemInformation.VirtualScreen.Bottom + 100);
    }

    protected override bool ShowWithoutActivation => true;

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            string profile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Dicebound", "LssWebView");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            if (IsDisposed || Disposing)
                return;

            var view = await environment.CreateCoreWebView2ControllerAsync(Handle);
            if (IsDisposed || Disposing)
            {
                view.Close();
                return;
            }

            controller = view;
            view.Bounds = ClientRectangle;
            view.IsVisible = true;
            var browser = view.CoreWebView2;
            browser.Settings.AreHostObjectsAllowed = false;
            browser.Settings.IsWebMessageEnabled = false;
            browser.Settings.AreDefaultScriptDialogsEnabled = false;
            browser.Settings.AreDefaultContextMenusEnabled = false;
            RestrictNavigation(browser);
            await ApplyViewport(browser, ClientSize);
            await browser.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled", "{\"enabled\":true}");

            string historyPath = Path.Combine(profile, "last-character.txt");
            string? lastCharacter = ReadLastCharacter(historyPath);
            string? restoreTarget = lastCharacter;
            browser.NavigationCompleted += (_, navigation) =>
            {
                if (restoreTarget != null && browser.Source == restoreTarget
                    && (!navigation.IsSuccess || navigation.HttpStatusCode >= 400))
                {
                    restoreTarget = null;
                    browser.Navigate(CharactersUrl);
                    return;
                }

                restoreTarget = null;
                if (navigation.IsSuccess && navigation.HttpStatusCode < 400)
                    RememberCharacter();
            };
            browser.SourceChanged += (_, change) =>
            {
                if (!change.IsNewDocument)
                    RememberCharacter();
            };
            browser.Navigate(lastCharacter ?? CharactersUrl);
            var finished = await Task.WhenAny(StreamFrames(browser), ReadInput(browser));
            await finished;

            void RememberCharacter()
            {
                string? character = CharacterAddress(browser.Source);
                if (character == null || character == lastCharacter)
                    return;

                try
                {
                    File.WriteAllText(historyPath, character);
                    lastCharacter = character;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception error)
        {
            if (IsDisposed || Disposing)
                return;

            string message = error is WebView2RuntimeNotFoundException
                ? "Установите Microsoft Edge WebView2 Runtime (Evergreen)."
                : "Не удалось открыть Long Story Short. " + error.Message;
            Console.Error.WriteLine(message);
            Close();
        }
    }

    private void RestrictNavigation(CoreWebView2 browser)
    {
        bool returnPending = false;
        browser.NavigationStarting += (_, navigation) =>
        {
            if (AllowedAddress(navigation.Uri) != null)
                return;

            navigation.Cancel = true;
            ReturnToList();
        };
        browser.NewWindowRequested += (_, request) =>
        {
            request.Handled = true;
            if (AllowedAddress(request.Uri) is { } address)
                browser.Navigate(address.AbsoluteUri);
            else
                ReturnToList();
        };
        browser.LaunchingExternalUriScheme += (_, request) =>
        {
            request.Cancel = true;
            ReturnToList();
        };

        void ReturnToList()
        {
            if (returnPending || IsDisposed || Disposing)
                return;

            returnPending = true;
            BeginInvoke(() =>
            {
                returnPending = false;
                if (!IsDisposed && !Disposing && browser.Source != CharactersUrl)
                    browser.Navigate(CharactersUrl);
            });
        }
    }

    private static string? ReadLastCharacter(string path)
    {
        try
        {
            return CharacterAddress(File.ReadAllText(path));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string? CharacterAddress(string address)
    {
        if (AllowedAddress(address) is not { } uri)
            return null;

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[0] != "characters" || parts[1] is not ("digital" or "builder"))
            return null;

        // Keep the sheet address without login or sharing parameters.
        return uri.GetLeftPart(UriPartial.Path);
    }

    private static Uri? AllowedAddress(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host == "longstoryshort.app" &&
        uri.IsDefaultPort && uri.UserInfo.Length == 0 ? uri : null;

    private async Task StreamFrames(CoreWebView2 browser)
    {
        var frames = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        var writer = Task.Run(async () =>
        {
            await foreach (string frame in frames.Reader.ReadAllAsync())
                await Console.Out.WriteLineAsync(frame);
        });
        var capture = CaptureFrames(browser, frames.Writer);
        var finished = await Task.WhenAny(capture, writer);
        frames.Writer.TryComplete();
        await finished;
    }

    private async Task CaptureFrames(CoreWebView2 browser, ChannelWriter<string> frames)
    {
        var firstFrame = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var streamError = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = browser.GetDevToolsProtocolEventReceiver("Page.screencastFrame");
        receiver.DevToolsProtocolEventReceived += OnFrame;
        bool started = false;
        try
        {
            await browser.CallDevToolsProtocolMethodAsync("Page.startScreencast",
                "{\"format\":\"jpeg\",\"quality\":72,\"maxWidth\":1200,\"maxHeight\":900}");
            started = true;
            await Task.WhenAny(firstFrame.Task, Task.Delay(1800));
            if (firstFrame.Task.IsCompleted)
                await streamError.Task;
        }
        catch (Exception) when (!IsDisposed && !Disposing)
        {
            // Some WebView2 versions may not stream frames from an off-screen window.
        }
        finally
        {
            receiver.DevToolsProtocolEventReceived -= OnFrame;
        }

        if (IsDisposed || Disposing)
            return;
        if (started)
            await browser.CallDevToolsProtocolMethodAsync("Page.stopScreencast", "{}");

        while (!IsDisposed)
        {
            string result = await browser.CallDevToolsProtocolMethodAsync("Page.captureScreenshot",
                "{\"format\":\"jpeg\",\"quality\":72,\"fromSurface\":true}");
            using var frame = JsonDocument.Parse(result);
            if (frame.RootElement.GetProperty("data").GetString() is { } data)
                frames.TryWrite(data);
            await Task.Delay(33);
        }

        async void OnFrame(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            try
            {
                using var frame = JsonDocument.Parse(e.ParameterObjectAsJson);
                var payload = frame.RootElement;
                if (payload.GetProperty("data").GetString() is { } data)
                {
                    frames.TryWrite(data);
                    firstFrame.TrySetResult(true);
                }
                int id = payload.GetProperty("sessionId").GetInt32();
                await browser.CallDevToolsProtocolMethodAsync("Page.screencastFrameAck", $"{{\"sessionId\":{id}}}");
            }
            catch (Exception error)
            {
                streamError.TrySetException(error);
            }
        }
    }

    private async Task ReadInput(CoreWebView2 browser)
    {
        try
        {
            while (await Task.Run(Console.ReadLine) is { } line)
            {
                using var command = JsonDocument.Parse(line);
                var root = command.RootElement;
                string? method = root.GetProperty("method").GetString();
                if (method is "Input.dispatchMouseEvent" or "Input.dispatchKeyEvent" or "Input.insertText")
                    await browser.CallDevToolsProtocolMethodAsync(method, root.GetProperty("payload").GetRawText());
                else if (method == "GoBack" && browser.CanGoBack)
                    browser.GoBack();
                else if (method == "GoForward" && browser.CanGoForward)
                    browser.GoForward();
                else if (method == "ResizeViewport")
                {
                    var size = root.GetProperty("payload");
                    ClientSize = ClampViewport(size.GetProperty("width").GetInt32(), size.GetProperty("height").GetInt32());
                    await ApplyViewport(browser, ClientSize);
                }
            }
        }
        finally
        {
            Close();
        }
    }

    private static Size ClampViewport(int width, int height) =>
        new(Math.Clamp(width, 1, 1200), Math.Clamp(height, 1, 900));

    private static Task ApplyViewport(CoreWebView2 browser, Size size) =>
        browser.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
            JsonSerializer.Serialize(new { width = size.Width, height = size.Height, deviceScaleFactor = 1, mobile = false }));

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (controller != null)
            controller.Bounds = ClientRectangle;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        controller?.Close();
        controller = null;
        base.OnFormClosed(e);
    }
}
