using System.Text;
using System.Threading.Channels;
using Terminal.Gui;

namespace Catl;

internal sealed class LogWindow
{
    private readonly TailReader reader;
    private readonly StringBuilder content = new();
    private readonly TextView text = new()
    {
        X = 0,
        Y = Pos.Function(() => Application.Driver.Cols < 70 ? 8 : 7),
        Width = Dim.Fill(),
        Height = Dim.Fill(1),
        ReadOnly = true,
        WordWrap = false,
        BottomOffset = 1,
        RightOffset = 1
    };
    private readonly Label folder = new() { X = 0, Y = 0, Width = Dim.Fill(), Height = 1 };
    private readonly Label name = new() { X = 0, Y = 1, Width = Dim.Fill(), Height = 1 };
    private readonly Label created = new() { X = 0, Y = 2, Width = Dim.Fill(), Height = 1 };
    private readonly Label size = new() { X = 0, Y = 3, Width = Dim.Fill(), Height = 1 };
    private readonly Label modified = new() { X = 0, Y = 4, Width = Dim.Fill(), Height = 1 };
    private readonly Label encodingLabel = new() { X = 0, Y = 5, Width = Dim.Fill(), Height = 1 };
    private readonly Label status = new() { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };
    private readonly CheckBox wrap = new("Wrap ^W") { X = 0, Y = 6 };
    private readonly CheckBox follow = new("Seguir ^F", true) { X = 15, Y = 6 };
    private readonly Channel<MonitorMessage> updates = Channel.CreateBounded<MonitorMessage>(
        new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = true });
    private int reload;
    private int requestedEncoding;
    private string? error;
    private string encoding = "";

    private LogWindow(TailReader reader) => this.reader = reader;

    public static void Run(TailReader reader, FileUpdate initial)
    {
        Application.Init();
        using var cancellation = new CancellationTokenSource();
        Task? monitor = null;
        object? timeout = null;
        try
        {
            var window = new LogWindow(reader);
            window.Build();
            window.Apply(initial);
            monitor = Task.Run(() => window.MonitorAsync(cancellation.Token));
            timeout = Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(100), _ =>
            {
                if (window.updates.Reader.TryRead(out var message))
                {
                    if (message.Update is not null)
                    {
                        window.error = null;
                        window.Apply(message.Update);
                    }
                    else
                    {
                        window.error = message.Error;
                        window.RefreshStatus();
                    }
                }
                return true;
            });
            Application.Run();
        }
        finally
        {
            cancellation.Cancel();
            if (timeout is not null)
                Application.MainLoop.RemoveTimeout(timeout);
            try
            {
                try
                {
                    monitor?.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
            }
            finally
            {
                Application.Shutdown();
            }
        }
    }

    private void Build()
    {
        var normal = new ColorScheme
        {
            Normal = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black),
            Focus = Terminal.Gui.Attribute.Make(Color.White, Color.DarkGray),
            HotNormal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black),
            HotFocus = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.DarkGray)
        };
        var blue = new ColorScheme
        {
            Normal = Terminal.Gui.Attribute.Make(Color.White, Color.Blue),
            Focus = Terminal.Gui.Attribute.Make(Color.White, Color.Cyan),
            HotNormal = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Blue),
            HotFocus = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Cyan)
        };
        Application.Top.ColorScheme = normal;
        text.ColorScheme = normal;
        foreach (var label in new[] { folder, name, created, size, modified, encodingLabel, status })
            label.ColorScheme = blue;
        wrap.ColorScheme = follow.ColorScheme = blue;
        var reopen = new Button("F5 Reabrir")
        {
            X = Pos.Function(() => Application.Driver.Cols < 70 ? 0 : 30),
            Y = Pos.Function(() => Application.Driver.Cols < 70 ? 7 : 6),
            ColorScheme = blue
        };
        var chooseEncoding = new Button("Encoding")
        {
            X = Pos.Function(() => Application.Driver.Cols < 70 ? 15 : 45),
            Y = Pos.Function(() => Application.Driver.Cols < 70 ? 7 : 6),
            ColorScheme = blue
        };
        var close = new Button("^Q Salir")
        {
            X = Pos.Function(() => Application.Driver.Cols < 70 ? 28 : 58),
            Y = Pos.Function(() => Application.Driver.Cols < 70 ? 7 : 6),
            ColorScheme = blue
        };
        reopen.Clicked += () => Interlocked.Exchange(ref reload, 1);
        close.Clicked += () => Application.RequestStop();
        chooseEncoding.Clicked += ChooseEncoding;
        wrap.Toggled += _ => SetWrap();
        follow.Toggled += _ => SetFollow();
        Application.Top.Add(folder, name, created, size, modified, encodingLabel,
            wrap, follow, reopen, chooseEncoding, close, text, status);

        var scroll = new ScrollBarView(text, true);
        scroll.ChangedPosition += () =>
        {
            text.TopRow = scroll.Position;
            text.SetNeedsDisplay();
        };
        scroll.OtherScrollBarView.ChangedPosition += () =>
        {
            text.LeftColumn = scroll.OtherScrollBarView.Position;
            text.SetNeedsDisplay();
        };
        text.DrawContent += _ =>
        {
            scroll.Size = text.Lines;
            scroll.Position = text.TopRow;
            scroll.OtherScrollBarView.Size = text.WordWrap ? 0 : text.Maxlength;
            scroll.OtherScrollBarView.Position = text.LeftColumn;
            scroll.Refresh();
        };
        Application.Top.KeyPress += key =>
        {
            switch (key.KeyEvent.Key)
            {
                case Key.CtrlMask | Key.Q:
                    Application.RequestStop();
                    break;
                case Key.F5:
                    Interlocked.Exchange(ref reload, 1);
                    break;
                case Key.CtrlMask | Key.W:
                    wrap.Checked = !wrap.Checked;
                    SetWrap();
                    break;
                case Key.CtrlMask | Key.F:
                    follow.Checked = !follow.Checked;
                    SetFollow();
                    break;
                case Key.CtrlMask | Key.E:
                    ChooseEncoding();
                    break;
                default:
                    return;
            }
            key.Handled = true;
        };
        text.SetFocus();
    }

    private void ChooseEncoding()
    {
        var choices = new RadioGroup(new NStack.ustring[]
        {
            "Automatico (BOM o UTF-8)", "UTF-8", "UTF-16 LE", "UTF-16 BE",
            "UTF-32 LE", "UTF-32 BE", "Windows-1252 (ANSI occidental)", "Latin-1 (ISO-8859-1)"
        })
        {
            X = 1,
            Y = 1,
            SelectedItem = Volatile.Read(ref requestedEncoding)
        };
        var apply = new Button("Aplicar", true);
        var cancel = new Button("Cancelar");
        using var dialog = new Dialog("Encoding de lectura", apply, cancel)
        {
            Width = Dim.Function(() => Math.Min(55, Application.Driver.Cols)),
            Height = Dim.Function(() => Math.Min(16, Application.Driver.Rows))
        };
        var quit = false;
        dialog.KeyPress += key =>
        {
            if (key.KeyEvent.Key != (Key.CtrlMask | Key.Q))
                return;
            quit = true;
            key.Handled = true;
            Application.RequestStop();
        };
        dialog.Add(choices);
        apply.Clicked += () =>
        {
            Interlocked.Exchange(ref requestedEncoding, choices.SelectedItem);
            Interlocked.Exchange(ref reload, 1);
            Application.RequestStop();
        };
        cancel.Clicked += () => Application.RequestStop();
        Application.Run(dialog);
        if (quit)
            Application.RequestStop();
        else
            text.SetFocus();
    }

    private void SetWrap()
    {
        text.WordWrap = wrap.Checked;
        if (follow.Checked)
            MoveToEnd();
        RefreshStatus();
    }

    private void SetFollow()
    {
        if (follow.Checked)
            MoveToEnd();
        RefreshStatus();
    }

    private void Apply(FileUpdate update)
    {
        var metadata = update.Metadata;
        folder.Text = $" Carpeta: {metadata.Folder}";
        name.Text = $" Archivo: {metadata.Name}";
        created.Text = $" Creacion: {metadata.CreationTime:yyyy-MM-dd HH:mm:ss}";
        size.Text = $" Tamano: {metadata.Length:N0} bytes";
        modified.Text = $" Modificado: {metadata.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
        encoding = update.EncodingName;
        encodingLabel.Text = $" Encoding: {encoding} ({(update.EncodingChoice == FileTextEncoding.Auto ? "automatico" : "manual")}) | ^E Cambiar";
        if (update.Reset || update.Text.Length != 0)
        {
            var top = text.TopRow;
            var left = text.LeftColumn;
            var cursor = text.CursorPosition;
            if (update.Reset)
                content.Clear();
            content.Append(update.Text);
            text.Text = content.ToString();
            if (follow.Checked)
                MoveToEnd();
            else
            {
                text.CursorPosition = cursor;
                text.TopRow = top;
                text.LeftColumn = left;
            }
        }
        RefreshStatus();
    }

    private void MoveToEnd()
    {
        text.MoveEnd();
        text.TopRow = Math.Max(0, text.Lines - Math.Max(1, text.Bounds.Height - 1));
        text.LeftColumn = 0;
        text.SetNeedsDisplay();
    }

    private void RefreshStatus()
    {
        status.Text = error is not null
            ? $" ERROR: {error} | F5 reintentar | ^Q salir"
            : $" {encoding} | {(follow.Checked ? "EN VIVO" : "LECTURA: seguimiento pausado")} | ^W Wrap | ^E Encoding | ^Q Salir";
    }

    private async Task MonitorAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            var caughtUp = true;
            MonitorMessage message;
            try
            {
                if (Interlocked.Exchange(ref reload, 0) != 0)
                    reader.ChangeEncoding((FileTextEncoding)Volatile.Read(ref requestedEncoding));
                var update = reader.Poll();
                caughtUp = update.CaughtUp;
                message = new(update, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                reader.Reset();
                message = new(null, ex.Message);
            }
            await updates.Writer.WriteAsync(message, cancellation);
            if (caughtUp)
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellation);
        }
    }

    private sealed record MonitorMessage(FileUpdate? Update, string? Error);
}
