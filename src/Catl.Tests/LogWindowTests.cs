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
            .Select(i => $"{i}: {new string('x', 120)}")) + "\n");
        var reader = new TailReader(path);
        var initial = reader.Poll();
        var driver = new FakeDriver();
        Application.Init(driver);
        var stage = 0;
        var watch = Stopwatch.StartNew();
        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), _ =>
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "La interfaz no completo el seguimiento.");
            var text = Application.Top.Subviews.OfType<TextView>().Single();
            if (stage == 0)
            {
                Assert.True(text.TopRow > 0);
                driver.SendKeys('w', ConsoleKey.W, false, false, true);
                Assert.True(text.WordWrap);
                var wrap = Application.Top.Subviews.OfType<CheckBox>()
                    .Single(box => box.Text.ToString() == "Wrap ^W");
                wrap.MouseEvent(new MouseEvent { X = 1, Y = 0, Flags = MouseFlags.Button1Clicked });
                Assert.False(text.WordWrap);
                driver.SetBufferSize(100, 30);
                Application.Top.LayoutSubviews();
                Assert.Equal(100, text.Bounds.Width);
                Assert.Equal(22, text.Bounds.Height);
                driver.SetBufferSize(60, 18);
                Application.Top.LayoutSubviews();
                Assert.Equal(60, text.Bounds.Width);
                Assert.Equal(9, text.Bounds.Height);
                Assert.All(Application.Top.Subviews.OfType<Button>(),
                    button => Assert.True(button.Frame.Right <= 60));
                driver.SetBufferSize(100, 30);
                driver.SendKeys('f', ConsoleKey.F, false, false, true);
                text.TopRow = 0;
                File.AppendAllText(path, "mensaje agregado en vivo\n");
                stage = 1;
            }
            else if (stage == 1 && text.Text.ToString()?.Contains("mensaje agregado en vivo", StringComparison.Ordinal) == true)
            {
                Assert.Equal(0, text.TopRow);
                driver.SendKeys('f', ConsoleKey.F, false, false, true);
                Assert.True(text.TopRow > 0);
                var lastWrite = File.GetLastWriteTimeUtc(path);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                    stream.Write(Encoding.UTF8.GetBytes("F5_OK"));
                File.SetLastWriteTimeUtc(path, lastWrite);
                driver.SendKeys('\0', ConsoleKey.F5, false, false, false);
                stage = 2;
            }
            else if (stage == 2 && text.Text.ToString()?.StartsWith("F5_OK", StringComparison.Ordinal) == true)
            {
                driver.SendKeys('q', ConsoleKey.Q, false, false, true);
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
    public void EncodingDialogRecoversLegacyFileAndUpdatesHeader()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, [0x45, 0x73, 0x70, 0x61, 0xF1, 0x6F, 0x6C, 0x0A]);
        var info = new FileInfo(path);
        var initial = new FileUpdate("", true,
            new(info.DirectoryName!, info.Name, info.CreationTime, info.Length, info.LastWriteTime),
            "pendiente", true);
        var reader = new TailReader(path);
        var driver = new FakeDriver();
        Application.Init(driver);
        var stage = 0;
        var watch = Stopwatch.StartNew();
        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), _ =>
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "No se aplico el encoding seleccionado.");
            var labels = Application.Top.Subviews.OfType<Label>().ToArray();
            if (stage == 0 && labels.Any(label => label.Text.ToString()?.Contains("ERROR:", StringComparison.Ordinal) == true))
            {
                stage = 1;
                Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), _ =>
                {
                    var dialog = Assert.IsType<Dialog>(Application.Current);
                    Descendants(dialog).OfType<RadioGroup>().Single().SelectedItem = 6;
                    driver.SendKeys('\r', ConsoleKey.Enter, false, false, false);
                    return false;
                });
                driver.SendKeys('e', ConsoleKey.E, false, false, true);
            }
            else if (stage == 1 && labels.Any(label => label.Text.ToString()
                         ?.Contains("windows-1252 (manual)", StringComparison.Ordinal) == true))
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

    [Fact]
    public void CtrlQAlsoQuitsFromEncodingDialog()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "hola\n");
        var reader = new TailReader(path);
        var initial = reader.Poll();
        var driver = new FakeDriver();
        Application.Init(driver);
        var opened = false;
        var quit = false;
        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), _ =>
        {
            Assert.False(opened, "Ctrl-Q debe cerrar toda la aplicacion, no solamente el selector.");
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
