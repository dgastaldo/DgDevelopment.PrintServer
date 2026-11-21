using PrintServer.Watching;

namespace PrintServer.UnitTests;

public sealed class FileFilterTests
{
    private static FileFilter Create(Action<WatcherOptions>? configure = null)
    {
        var options = new WatcherOptions();
        configure?.Invoke(options);
        return new FileFilter(options);
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("REPORT.PDF")]
    [InlineData("label.ps")]
    public void AcceptsPdfAndPostScriptByDefault(string name)
    {
        Assert.True(Create().Accepts(name));
    }

    [Theory]
    [InlineData("memo.doc")]
    [InlineData("memo.DOCX")]
    [InlineData("memo.odt")]
    [InlineData("memo.rtf")]
    [InlineData("notes.txt")]
    [InlineData("table.xls")]
    [InlineData("table.xlsx")]
    [InlineData("table.ods")]
    [InlineData("slides.ppt")]
    [InlineData("slides.pptx")]
    [InlineData("slides.odp")]
    [InlineData("photo.png")]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPEG")]
    public void AcceptsConvertibleDocumentsAndImagesByDefault(string name)
    {
        Assert.True(Create().Accepts(name));
    }

    [Theory]
    [InlineData("archive.zip")]
    [InlineData("program.exe")]
    [InlineData("noextension")]
    [InlineData("")]
    public void RejectsOtherExtensionsByDefault(string name)
    {
        Assert.False(Create().Accepts(name));
    }

    [Theory]
    [InlineData("~report.pdf")]
    [InlineData(".hidden.pdf")]
    [InlineData("report.pdf.tmp")]
    [InlineData("report.pdf.part")]
    [InlineData("report.pdf.PARTIAL")]
    [InlineData("report.pdf.crdownload")]
    [InlineData("report.pdf.download")]
    public void RejectsTemporaryAndHiddenFiles(string name)
    {
        Assert.False(Create().Accepts(name));
    }

    [Fact]
    public void ConfiguredExtensionsReplaceTheDefaultsAndAcceptABareName()
    {
        var filter = Create(options => options.Extensions = ["png", " .JPG "]);

        Assert.True(filter.Accepts("photo.png"));
        Assert.True(filter.Accepts("photo.jpg"));
        Assert.False(filter.Accepts("report.pdf"));
    }

    [Fact]
    public void NullNameIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Create().Accepts(null!));
    }

    [Fact]
    public void NullOptionsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new FileFilter(null!));
    }
}
