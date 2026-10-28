using Catl;

namespace Catl.Tests;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("--help", CommandAction.Help)]
    [InlineData("-h", CommandAction.Help)]
    [InlineData("--version", CommandAction.Version)]
    [InlineData("-v", CommandAction.Version)]
    public void RecognizesInformationalArguments(string argument, CommandAction action)
        => Assert.Equal(action, CommandLine.Parse([argument]).Action);

    [Fact]
    public void RejectsInvalidArguments()
    {
        string[][] cases = [[], ["--unknown"], ["one.log", "two.log"], ["--help", "file.log"], [""]];
        foreach (var arguments in cases)
            Assert.Throws<ArgumentException>(() => CommandLine.Parse(arguments));
    }

    [Fact]
    public void RejectsMissingFile()
        => Assert.Throws<FileNotFoundException>(() =>
            CommandLine.Parse([Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.log")]));

    [Fact]
    public void AcceptsTextFilesRegardlessOfExtension()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.Equal(Path.GetFullPath(path), CommandLine.Parse([path]).FilePath);
            Assert.Equal(path, CommandLine.Parse(["--", path]).FilePath);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
