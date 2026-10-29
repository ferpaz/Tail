using System.Diagnostics;
using System.Text;
using Catl;
using Terminal.Gui;

namespace Catl.Tests;

public sealed class LogWindowTests
{
    [Fact]
    public void LiveViewSupportsWrapMouseResizePauseResumeAndQuit()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, string.Join('\n', Enumerable.Range(1, 100)
            .Select(i => $"{i}: {new string('x', 200)}")) + "\n");
        var reader = new TailReader(path);
        var initial = reader.Poll();
        var driver = new FakeDriver();
        Application.Init(driver);
        var stage = 0;
        var watch = Stopwatch.StartNew();
        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), _ =>
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "The interface did not complete live follow.");
            var text = Application.Top.Subviews.OfType<TextView>().Single();
            if (stage == 0)
            {
                Assert.True(text.TopRow > 0);
                driver.SendKeys('w', ConsoleKey.W, false, false, true);
                Assert.True(text.WordWrap);
                var wrap = ActionButton("^W");
                wrap.MouseEvent(new MouseEvent { X = 1, Y = 0, Flags = MouseFlags.Button1Clicked });
                Assert.False(text.WordWrap);
                Assert.Equal(Color.Gray, text.ColorScheme.Focus.Foreground);
                Assert.Equal(Color.Black, text.ColorScheme.Focus.Background);
                Assert.Equal(0, text.Frame.X);
                Assert.Equal(1, text.Frame.Y);
                Assert.Equal(" " + Path.GetFileName(path),
                    Application.Top.Subviews.OfType<Label>().Single(label => label.Frame.Y == 0).Text.ToString());
                Assert.Empty(Application.Top.Subviews.OfType<CheckBox>());
                var vertical = Application.Top.Subviews.OfType<ScrollBarView>().Single(bar => bar.IsVertical);
                Assert.True(vertical.Visible);
                Assert.True(vertical.ShowScrollIndicator);
                Assert.Equal(driver.Cols - 1, vertical.Frame.X);
                var horizontal = Application.Top.Subviews.OfType<ScrollBarView>().Single(bar => !bar.IsVertical);
                var actions = ActionButton("^Q").SuperView;
                Assert.NotNull(actions);
                foreach (var (columns, rows) in new[] { (140, 30), (100, 30), (60, 18) })
                {
                    driver.SetBufferSize(columns, rows);
                    Application.Refresh();
                    Assert.Equal(columns - 2, text.Bounds.Width);
                    Assert.Equal(actions.Frame.Y - 2, text.Frame.Bottom);
                    Assert.Equal(text.Bounds.Height, vertical.Bounds.Height);
                    Assert.Equal(actions.Frame.Y - 1, horizontal.Frame.Y);
                    Assert.True(horizontal.Visible);
                    Assert.True(horizontal.Frame.Bottom <= actions.Frame.Y);
                    Assert.NotEqual(' ', driver.Contents[horizontal.Frame.Y, horizontal.Frame.X, 0]);
                    horizontal.MouseEvent(new MouseEvent
                    {
                        X = horizontal.Bounds.Width - 1,
                        Y = 0,
                        Flags = MouseFlags.Button1Pressed
                    });
                    Assert.True(text.LeftColumn > 0);
                    driver.SendKeys('w', ConsoleKey.W, false, false, true);
                    Application.Refresh();
                    Assert.False(horizontal.Visible);
                    driver.SendKeys('w', ConsoleKey.W, false, false, true);
                    Application.Refresh();
                    Assert.True(horizontal.Visible);
                    Assert.Equal(actions.Frame.Y - 1, horizontal.Frame.Y);
                }
                Assert.Equal(
                    [ActionButton("^Q"), ActionButton("^I"), ActionButton("F5"),
                        ActionButton("^W"), ActionButton("^F"), EncodingButton()],
                    ActionButtons());
                Assert.All(Descendants(Application.Top).OfType<Button>(),
                    button => Assert.True(button.Frame.Right <= 60));
                driver.SetBufferSize(100, 30);
                text.SetFocus();
                Application.Refresh();
                Assert.Equal((int)Terminal.Gui.Attribute.Make(Color.Gray, Color.Black),
                    driver.Contents[text.Frame.Y, text.Frame.X, 1]);
                Assert.Equal(' ', driver.Contents[text.Frame.Bottom, 2, 0]);
                Assert.Equal(' ', driver.Contents[text.Frame.Y, text.Frame.Right, 0]);
                Assert.NotEqual(' ', driver.Contents[vertical.Frame.Y, vertical.Frame.X, 0]);
                ActionButton("^F").MouseEvent(new MouseEvent { Flags = MouseFlags.Button1Clicked });
                vertical.Position = 5;
                Assert.Equal(5, text.TopRow);
                vertical.MouseEvent(new MouseEvent
                {
                    X = 0,
                    Y = vertical.Bounds.Height - 1,
                    Flags = MouseFlags.Button1Pressed
                });
                Assert.True(text.TopRow > 5);
                horizontal.MouseEvent(new MouseEvent
                {
                    X = horizontal.Bounds.Width - 1,
                    Y = 0,
                    Flags = MouseFlags.Button1Pressed
                });
                Assert.True(text.LeftColumn > 0);
                text.TopRow = 0;
                File.AppendAllText(path, "message appended live\n");
                stage = 1;
            }
            else if (stage == 1 && text.Text.ToString()?.Contains("message appended live", StringComparison.Ordinal) == true)
            {
                Assert.Equal(0, text.TopRow);
                driver.SendKeys('f', ConsoleKey.F, false, false, true);
                Assert.True(text.TopRow > 0);
                var lastWrite = File.GetLastWriteTimeUtc(path);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                    stream.Write(Encoding.UTF8.GetBytes("F5_OK"));
                File.SetLastWriteTimeUtc(path, lastWrite);
                ActionButton("F5").MouseEvent(new MouseEvent { Flags = MouseFlags.Button1Clicked });
                stage = 2;
            }
            else if (stage == 2 && text.Text.ToString()?.StartsWith("F5_OK", StringComparison.Ordinal) == true)
            {
                ActionButton("^Q").MouseEvent(new MouseEvent { Flags = MouseFlags.Button1Clicked });
                stage = 3;
                return false;
            }
            return true;
        });
        try
        {
            LogWindow.Run(reader, initial);
            Assert.Equal(3, stage);
        }
        finally
        {
            Application.Shutdown();
            File.Delete(path);
        }
    }

    [Fact]
    public void EncodingDialogRecoversLegacyFileAndUpdatesStatusBar()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, [0x45, 0x73, 0x70, 0x61, 0xF1, 0x6F, 0x6C, 0x0A]);
        var info = new FileInfo(path);
        var initial = new FileUpdate("", true,
            new(info.DirectoryName!, info.Name, info.CreationTime, info.Length, info.LastWriteTime),
            "pending", true);
        var reader = new TailReader(path);
        var driver = new FakeDriver();
        Application.Init(driver);
        var stage = 0;
        var watch = Stopwatch.StartNew();
        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), _ =>
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "The selected encoding was not applied.");
            if (stage == 0)
            {
                stage = 1;
                Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), _ =>
                {
                    var dialog = Assert.IsType<Dialog>(Application.Current);
                    Descendants(dialog).OfType<RadioGroup>().Single().SelectedItem = 6;
                    driver.SendKeys('\r', ConsoleKey.Enter, false, false, false);
                    return false;
                });
                EncodingButton().MouseEvent(new MouseEvent { Flags = MouseFlags.Button1Clicked });
            }
            else if (stage == 1 && EncodingButton().Text.ToString()
                         ?.Contains("windows-1252", StringComparison.Ordinal) == true)
            {
                var text = Application.Top.Subviews.OfType<TextView>().Single();
                Assert.Equal("Espa\u00f1ol\n", text.Text.ToString()?.Replace("\r\n", "\n"));
                stage = 2;
                driver.SendKeys('q', ConsoleKey.Q, false, false, true);
                return false;
            }
            return true;
        });
        try
        {
            LogWindow.Run(reader, initial);
            Assert.Equal(2, stage);
        }
        finally
        {
            Application.Shutdown();
            File.Delete(path);
        }

    }

    private static IEnumerable<View> Descendants(View view)
        => view.Subviews.SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static Button[] ActionButtons()
        => Application.Top.Subviews.Single(view => view.Subviews.OfType<Button>().Any())
            .Subviews.OfType<Button>().ToArray();

    private static Button ActionButton(string shortcut)
        => ActionButtons()
            .Single(button => button.Text.ToString()?.Contains(shortcut, StringComparison.Ordinal) == true);

    private static Button EncodingButton() => ActionButtons()[^1];

    [Fact]
    public void InformationButtonOpensLiveMetadataDialog()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "hello\n");
        var reader = new TailReader(path);
        var initial = reader.Poll();
        var driver = new FakeDriver();
        Application.Init(driver);
        var opened = false;
        var updated = false;
        var watch = Stopwatch.StartNew();
        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), _ =>
        {
            if (opened)
                return true;
            opened = true;
            Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), _ =>
            {
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "The metadata did not update.");
                var dialog = Assert.IsType<Dialog>(Application.Current);
                var information = Descendants(dialog).OfType<Label>().Single().Text.ToString();
                Assert.Contains(Path.GetDirectoryName(path)!, information);
                Assert.Contains(Path.GetFileName(path), information);
                Assert.Contains("Created :", information);
                Assert.Contains("Modified:", information);
                Assert.Contains("Encoding: utf-8 (automatic)", information);
                if (!updated)
                {
                    File.AppendAllText(path, "another line\n");
                    updated = true;
                    return true;
                }
                if (information?.Contains($"Size    : {new FileInfo(path).Length:N0} bytes", StringComparison.Ordinal) != true)
                    return true;
                driver.SendKeys('q', ConsoleKey.Q, false, false, true);
                return false;
            });
            Application.MainLoop.AddIdle(() =>
            {
                ActionButton("^I").MouseEvent(new MouseEvent { Flags = MouseFlags.Button1Clicked });
                return false;
            });
            return false;
        });
        try
        {
            LogWindow.Run(reader, initial);
            Assert.True(updated);
        }
        finally
        {
            Application.Shutdown();
            File.Delete(path);
        }
    }

    [Fact]
    public void CtrlQAlsoQuitsFromEncodingDialog()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "hello\n");
        var reader = new TailReader(path);
        var initial = reader.Poll();
        var driver = new FakeDriver();
        Application.Init(driver);
        var opened = false;
        var quit = false;
        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), _ =>
        {
            Assert.False(opened, "Ctrl-Q must close the entire application, not just the selector.");
            opened = true;
            Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), _ =>
            {
                Assert.IsType<Dialog>(Application.Current);
                driver.SendKeys('q', ConsoleKey.Q, false, false, true);
                quit = true;
                return false;
            });
            driver.SendKeys('e', ConsoleKey.E, false, false, true);
            return true;
        });
        try
        {
            LogWindow.Run(reader, initial);
            Assert.True(quit);
        }
        finally
        {
            Application.Shutdown();
            File.Delete(path);
        }
    }
}
