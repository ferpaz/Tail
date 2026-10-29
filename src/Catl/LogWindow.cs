using System.Text;
using System.Threading.Channels;
using Terminal.Gui;

namespace Catl;

internal sealed class LogWindow
{
    private readonly TailReader _reader;
    private readonly StringBuilder _content = new();
    private readonly TextView _text = new()
    {
        X = 0,
        Y = 1,
        Width = Dim.Fill(2),
        Height = Dim.Fill(),
        ReadOnly = true,
        WordWrap = false,
        BottomOffset = 0,
        RightOffset = 0
    };
    private readonly Label _name = new() { X = 0, Y = 0, Width = Dim.Fill(), Height = 1 };
    private readonly View _actions = new() { X = 0, Width = Dim.Fill() };
    private readonly List<int> ActionWidths = [];

    private readonly Button Reopen = new("RELOAD (F5)")
    {
        AutoSize = true, Height = 1
    };
    private readonly Button More = new("INFO (^I)")
    {
        AutoSize = true, Height = 1
    };
    private readonly Button Close = new("QUIT (^Q)")
    {
        AutoSize = true, Height = 1
    };
    private readonly Button Wrap = new("● WRAP (^W)")
    {
        AutoSize = true, Height = 1
    };
    private readonly Button Follow = new("■ PAUSED (^F)")
    {
        AutoSize = true, Height = 1, Width = 13
    };

    private readonly Button EncodingButton = new("ENCODING (^E)")
    {
        AutoSize = true, 
        Height = 1, 
        TextAlignment = TextAlignment.Left, 
        ColorScheme = new()
        {
            Normal = Terminal.Gui.Attribute.Make(Color.White, Color.Blue),
            Focus = Terminal.Gui.Attribute.Make(Color.White, Color.Blue),
            HotNormal = Terminal.Gui.Attribute.Make(Color.White, Color.Blue),
            HotFocus = Terminal.Gui.Attribute.Make(Color.White, Color.Blue)
        }
    };

    private readonly Button[] _actionButtons;

    private bool _following = true;
    private FileUpdate? _appliedUpdate;
    private Label? _information;
    
    private readonly Channel<MonitorMessage> _updates = Channel.CreateBounded<MonitorMessage>(
        new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = true });
    private int _reload;
    private int _requestedEncoding;
    private string? _error;
    private string _encoding = "";

    private LogWindow(TailReader reader)
    {
        _reader = reader;
        _actionButtons = [Close, More, Reopen, Wrap, Follow, EncodingButton];
    }

    public static void Run(TailReader reader, FileUpdate initial)
    {
        Application.Init();

        var cancellation = new CancellationTokenSource();

        Task? monitor = null;
        object? timeout = null;

        try
        {
            var window = new LogWindow(reader);
            window.Build();
            window.Apply(initial);

            monitor = Task.Run(() => window.MonitorAsync(cancellation.Token), cancellation.Token);

            timeout = Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(100), _ =>
            {
                if (!window._updates.Reader.TryRead(out var message)) return true;
                
                if (message.Update is not null)
                {
                    window._error = null;
                    window.Apply(message.Update);
                }
                else
                {
                    window._error = message.Error;
                    window.RefreshStatus();
                }

                return true;
            });

            Application.Run();
        }
        finally
        {
            cancellation.Cancel();
            if (timeout is not null) Application.MainLoop.RemoveTimeout(timeout);

            try
            {
                monitor?.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Si hay errores los ignora, para actualizar la próxima vez que se ejecuta
            }
            finally
            {
                cancellation?.Dispose();
                Application.Shutdown();
            }
        }
    }

    private void Build()
    {
        var normal = new ColorScheme
        {
            Normal = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black),
            Focus = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black),
            Disabled = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black),
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
        _text.ColorScheme = normal;
        _name.ColorScheme = blue;
        _actions.ColorScheme = blue;

        ActionWidths.AddRange(_actionButtons.Select(ab => ab.Bounds.Width + 1));
        
        for (var index = 0; index < _actionButtons.Length; index++)
        {
            var itemIndex = index;
            var button = _actionButtons[index];
            button.ColorScheme = blue;
            button.X = Pos.Function(() => ActionPosition(itemIndex).X);
            button.Y = Pos.Function(() => ActionPosition(itemIndex).Y);
            _actions.Add(button);
        }
        _actions.Height = Dim.Function(() => ActionRows);
        _actions.Y = Pos.AnchorEnd() - Pos.Function(() => ActionRows);
        
        _text.Height = Dim.Fill(3);

        Reopen.Clicked += () => Interlocked.Exchange(ref _reload, 1);
        Close.Clicked += () => Application.RequestStop();
        More.Clicked += ShowInformation;
        EncodingButton.Clicked += ChooseEncoding;
        Wrap.Clicked += SetWrap;
        Follow.Clicked += SetFollow;

        Application.Top.Add(_name, _text, _actions);

        var scrollColors = new ColorScheme
        {
            Normal = Terminal.Gui.Attribute.Make(Color.White, Color.DarkGray),
            Focus = Terminal.Gui.Attribute.Make(Color.White, Color.DarkGray)
        };

        var scroll = new ScrollBarView(_text, true, false)
        {
            X = Pos.AnchorEnd(1),
            Y = 1,
            Width = 1,
            Height = Dim.Fill() - Dim.Function(() => ActionRows + 2),
            AutoHideScrollBars = false,
            ShowScrollIndicator = true,
            Size = 1,
            ColorScheme = scrollColors,
            IsVertical = true
        };

        var horizontal = new ScrollBarView(_text, false, false)
        {
            X = Pos.Left(_text),
            Y = Pos.Bottom(_text) + 1,
            Width = Dim.Width(_text),
            Height = 1,
            AutoHideScrollBars = false,
            ShowScrollIndicator = true,
            Size = 1,
            ColorScheme = scrollColors
        };

        scroll.ChangedPosition += () =>
        {
            _text.TopRow = Math.Min(scroll.Position, Math.Max(0, _text.Lines - _text.Bounds.Height));
            _text.SetNeedsDisplay();
        };
        
        horizontal.ChangedPosition += () =>
        {
            _text.LeftColumn = Math.Min(horizontal.Position, Math.Max(0, _text.Maxlength - _text.Bounds.Width));
            _text.SetNeedsDisplay();
        };

        _text.DrawContent += _ =>
        {
            scroll.Size = Math.Max(1, _text.Lines);
            scroll.Position = _text.TopRow;
            horizontal.Visible = !_text.WordWrap;
            horizontal.Size = Math.Max(1, _text.Maxlength);
            horizontal.Position = _text.LeftColumn;
            // Terminal.Gui 1.x recalculates a scrollbar against its own Bounds when Size changes.
            scroll.Frame = new Rect(Application.Top.Bounds.Width - 1, 1, 1,
                Math.Max(0, Application.Top.Bounds.Height - ActionRows - 3));
            horizontal.Frame = new Rect(_text.Frame.X, _text.Frame.Bottom + 1, _text.Bounds.Width, 1);

            scroll.Refresh();
            horizontal.Refresh();
        };

        Application.RootKeyEvent += ProcessKeyEvent;
        _text.SetFocus();
    }

    private bool ProcessKeyEvent(KeyEvent key)
    {
        if (Application.Current != Application.Top) return false;

        switch (key.Key)
        {
            case Key.CtrlMask | Key.Q:
                Application.RequestStop();
                break;
            case Key.F5:
                Interlocked.Exchange(ref _reload, 1);
                break;
            case Key.CtrlMask | Key.W:
                SetWrap();
                break;
            case Key.CtrlMask | Key.F:
                SetFollow();
                break;
            case Key.CtrlMask | Key.E:
                ChooseEncoding();
                break;
            case Key.CtrlMask | Key.I:
                ShowInformation();
                break;
            default:
                return false;
        }

        return true;
    }

    private Point ActionPosition(int index)
    {
        var x = 0;
        var y = 0;
        for (var i = 0; i <= index; i++)
        {
            var width = ActionWidths[i];
            if (x != 0 && x + width > Application.Driver.Cols)
            {
                x = 0;
                y++;
            }
            
            if (i == index) return new(x, y);
            x += width;
        }
        return Point.Empty;
    }

    private int ActionRows => ActionPosition(_actionButtons.Length - 1).Y + 1;

    private void ShowInformation()
    {
        var close = new Button("Close", true);
        close.Clicked += () => Application.RequestStop();

        _information = new Label { X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(2) };

        using var dialog = new Dialog("File information", close);
        dialog.Width = Dim.Function(() => Math.Min(90, Application.Driver.Cols));
        dialog.Height = Dim.Function(() => Math.Min(11, Application.Driver.Rows));
        dialog.Add(_information);

        RefreshInformation();

        try
        {
            RunDialog(dialog);
        }
        finally
        {
            _information = null;
        }
    }

    private void ChooseEncoding()
    {
        var choices = new RadioGroup([
            "Automatic (BOM or UTF-8)", "UTF-8", "UTF-16 LE", "UTF-16 BE",
            "UTF-32 LE", "UTF-32 BE", "Windows-1252 (Western ANSI)", "Latin-1 (ISO-8859-1)"
        ])
        {
            X = 1,
            Y = 1,
            SelectedItem = Volatile.Read(ref _requestedEncoding)
        };

        var cancel = new Button("Cancel");
        cancel.Clicked += () => Application.RequestStop();

        var apply = new Button("Apply", true);
        apply.Clicked += () =>
        {
            Interlocked.Exchange(ref _requestedEncoding, choices.SelectedItem);
            Interlocked.Exchange(ref _reload, 1);
            Application.RequestStop();
        };

        using var dialog = new Dialog("Reading encoding", apply, cancel);
        dialog.Width = Dim.Function(() => Math.Min(36, Application.Driver.Cols));
        dialog.Height = Dim.Function(() => Math.Min(13, Application.Driver.Rows));
        dialog.Add(choices);

        RunDialog(dialog);
    }

    private void RunDialog(Dialog dialog)
    {
        var quit = false;
        dialog.KeyPress += key =>
        {
            if (key.KeyEvent.Key != (Key.CtrlMask | Key.Q)) return;
            quit = true;
            key.Handled = true;
            Application.RequestStop();
        };
        Application.Run(dialog);
        if (quit)
            Application.RequestStop();
        else
            _text.SetFocus();
    }

    private void SetWrap()
    {
        _text.WordWrap = !_text.WordWrap;
        if (_following) MoveToEnd();
        RefreshStatus();
    }

    private void SetFollow()
    {
        _following = !_following;
        if (_following) MoveToEnd();
        RefreshStatus();
    }

    private void Apply(FileUpdate update)
    {
        _appliedUpdate = update;
        _name.Text = $" {update.Metadata.Name}";
        _encoding = update.EncodingName;
        RefreshInformation();
        if (update.Reset || update.Text.Length != 0)
        {
            var top = _text.TopRow;
            var left = _text.LeftColumn;
            var cursor = _text.CursorPosition;
            if (update.Reset)
                _content.Clear();
            _content.Append(update.Text);
            _text.Text = _content.ToString();
            if (_following)
                MoveToEnd();
            else
            {
                _text.CursorPosition = cursor;
                _text.TopRow = top;
                _text.LeftColumn = left;
            }
        }
        RefreshStatus();
    }

    private void MoveToEnd()
    {
        _text.MoveEnd();
        _text.TopRow = Math.Max(0, _text.Lines - Math.Max(1, _text.Bounds.Height));
        _text.LeftColumn = 0;
        _text.SetNeedsDisplay();
    }

    private void RefreshStatus()
    {
        Wrap.Text = _text.WordWrap 
            ? Wrap.Text.Replace("○", "●") 
            : Wrap.Text.Replace("●", "○");

        Follow.Text = _following 
            ? Follow.Text.Replace("PAUSED", "LIVE") 
            : Follow.Text.Replace("LIVE", "PAUSED");
        
        EncodingButton.Text = $"{_encoding}";
    }

    private void RefreshInformation()
    {
        if (_information is null || _appliedUpdate is null)
            return;
        var metadata = _appliedUpdate.Metadata;
        _information.Text = $"""
            Path    : {metadata.Folder}
            File    : {metadata.Name}
            Created : {metadata.CreationTime:yyyy-MM-dd HH:mm:ss}
            Size    : {metadata.Length:N0} bytes
            Modified: {metadata.LastWriteTime:yyyy-MM-dd HH:mm:ss}
            Encoding: {_appliedUpdate.EncodingName} ({(_appliedUpdate.EncodingChoice == FileTextEncoding.Auto ? "automatic" : "manual")})
            """;
    }

    private async Task MonitorAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            var caughtUp = true;
            MonitorMessage message;
            try
            {
                if (Interlocked.Exchange(ref _reload, 0) != 0)
                    _reader.ChangeEncoding((FileTextEncoding)Volatile.Read(ref _requestedEncoding));
                var update = _reader.Poll();
                caughtUp = update.CaughtUp;
                message = new(update, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                _reader.Reset();
                message = new(null, ex.Message);
            }
            await _updates.Writer.WriteAsync(message, cancellation);
            if (caughtUp)
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellation);
        }
    }

    private sealed record MonitorMessage(FileUpdate? Update, string? Error);
}
