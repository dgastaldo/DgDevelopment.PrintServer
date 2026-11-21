using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf.IO;
using PrintServer.Backends.Common;
using PrintServer.Backends.Selection;
using PrintServer.Conversion;
using PrintServer.Core.Printing;

namespace PrintServer.UnitTests;

internal sealed class OutputWritingProcessRunner : IProcessRunner
{
    public int ExitCode { get; set; }

    public string StandardError { get; set; } = string.Empty;

    public bool ProduceOutput { get; set; } = true;

    public Exception? Throw { get; set; }

    public TimeSpan? Hang { get; set; }

    public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        Calls.Add((fileName, arguments));
        if (Throw is not null)
        {
            throw Throw;
        }

        if (Hang is not null)
        {
            await Task.Delay(Hang.Value, ct).ConfigureAwait(false);
        }

        if (ProduceOutput)
        {
            var outputDirectory = arguments[arguments.ToList().IndexOf("--outdir") + 1];
            var source = arguments[^1];
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(source) + ".pdf"), "%PDF-1.4 fake", ct).ConfigureAwait(false);
        }

        return new ProcessResult(ExitCode, string.Empty, StandardError);
    }
}

public sealed class ConversionTests : IDisposable
{
    private const string TinyJpeg = "/9j/4AAQSkZJRgABAQAAAAAAAAD/2wBDABALDA4MChAODQ4SERATGCgaGBYWGDEjJR0oOjM9PDkzODdASFxOQERXRTc4UG1RV19iZ2hnPk1xeXBkeFxlZ2P/2wBDARESEhgVGC8aGi9jQjhCY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2NjY2P/wAARCAAGAAgDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAABQb/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCKALXj/9k=";

    private const string TinyPortraitPng = "iVBORw0KGgoAAAANSUhEUgAAAAYAAAAIAQMAAADgCBx7AAAAIGNIUk0AAHomAACAhAAA+gAAAIDoAAB1MAAA6mAAADqYAAAXcJy6UTwAAAAGUExURQAA/////3vcmSwAAAABYktHRAH/Ai3eAAAAB3RJTUUH6gsVFCYbOYhFnAAAAAtJREFUCNdjYEAFAAAQAAGhxSHBAAAAAElFTkSuQmCC";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"printserver-convert-{Guid.NewGuid():N}");
    private readonly OutputWritingProcessRunner _runner = new();

    public ConversionTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string File_(string name, byte[]? content = null)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, content ?? [1, 2, 3]);
        return path;
    }

    private LibreOfficeConverter Libre(Action<ConversionOptions>? configure = null, Func<string, bool>? exists = null, HostPlatform platform = HostPlatform.Linux)
    {
        var options = new ConversionOptions();
        configure?.Invoke(options);
        return new LibreOfficeConverter(_runner, Options.Create(options), NullLogger<LibreOfficeConverter>.Instance, exists ?? (_ => true), platform);
    }

    [Fact]
    public void ALinuxInstallIsFoundInTheUsualPlaces()
    {
        Assert.Equal("/usr/bin/libreoffice", LibreOfficeLocator.Find(null, HostPlatform.Linux, path => path == "/usr/bin/libreoffice"));
        Assert.Equal("/usr/bin/soffice", LibreOfficeLocator.Find(null, HostPlatform.Other, _ => true));
    }

    [Fact]
    public void AMacInstallIsFoundInTheApplicationsFolder()
    {
        Assert.Equal(
            "/Applications/LibreOffice.app/Contents/MacOS/soffice",
            LibreOfficeLocator.Find(null, HostPlatform.MacOS, _ => true));
    }

    [Fact]
    public void AWindowsInstallIsSearchedInProgramFiles()
    {
        var candidates = LibreOfficeLocator.Candidates(HostPlatform.Windows).ToList();

        Assert.All(candidates, path => Assert.EndsWith(Path.Combine("LibreOffice", "program", "soffice.exe"), path, StringComparison.Ordinal));
    }

    [Fact]
    public void AConfiguredPathIsUsedOnlyWhenItExists()
    {
        Assert.Equal("/custom/soffice", LibreOfficeLocator.Find("/custom/soffice", HostPlatform.Linux, path => path == "/custom/soffice"));
        Assert.Null(LibreOfficeLocator.Find("/custom/soffice", HostPlatform.Linux, path => path == "/usr/bin/soffice"));
        Assert.Null(LibreOfficeLocator.Find(null, HostPlatform.Linux, _ => false));
        Assert.Throws<ArgumentNullException>(() => LibreOfficeLocator.Find(null, HostPlatform.Linux, null!));
    }

    [Theory]
    [InlineData(DocumentFormats.Doc)]
    [InlineData(DocumentFormats.Docx)]
    [InlineData(DocumentFormats.Odt)]
    [InlineData(DocumentFormats.Rtf)]
    [InlineData(DocumentFormats.PlainText)]
    [InlineData(DocumentFormats.Xls)]
    [InlineData(DocumentFormats.Xlsx)]
    [InlineData(DocumentFormats.Ods)]
    [InlineData(DocumentFormats.Ppt)]
    [InlineData(DocumentFormats.Pptx)]
    [InlineData(DocumentFormats.Odp)]
    public void LibreOfficeConvertsOfficeAndOpenDocumentFormats(string format)
    {
        Assert.True(Libre().CanConvert(format));
    }

    [Theory]
    [InlineData(DocumentFormats.Pdf)]
    [InlineData(DocumentFormats.Png)]
    [InlineData(DocumentFormats.Unknown)]
    public void LibreOfficeLeavesOtherFormatsAlone(string format)
    {
        Assert.False(Libre().CanConvert(format));
    }

    [Fact]
    public async Task LibreOfficeProducesThePdfWhereRequested()
    {
        var source = File_("report.docx");
        var output = Path.Combine(_directory, "report.docx.pdf");

        var result = await Libre(exists: path => path == "/usr/bin/soffice").ConvertToPdfAsync(source, DocumentFormats.Docx, output, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("%PDF-1.4 fake", await File.ReadAllTextAsync(output));
        var call = Assert.Single(_runner.Calls);
        Assert.Equal("/usr/bin/soffice", call.FileName);
        Assert.Contains("--headless", call.Arguments);
        Assert.Contains("--convert-to", call.Arguments);
        Assert.Equal(source, call.Arguments[^1]);
        Assert.Contains(call.Arguments, argument => argument.StartsWith("-env:UserInstallation=file:///", StringComparison.Ordinal));
        Assert.Empty(Directory.GetDirectories(_directory));
    }

    [Fact]
    public async Task LibreOfficeReceivesTheFileAsASingleArgument()
    {
        var source = File_("--convert-to; rm -rf x.docx");

        await Libre().ConvertToPdfAsync(source, DocumentFormats.Docx, Path.Combine(_directory, "out.pdf"), CancellationToken.None);

        Assert.Equal(source, Assert.Single(_runner.Calls).Arguments[^1]);
    }

    [Fact]
    public async Task LibreOfficeReportsAMissingInstallation()
    {
        var result = await Libre(exists: _ => false).ConvertToPdfAsync(File_("a.docx"), DocumentFormats.Docx, Path.Combine(_directory, "a.pdf"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Conversion:LibreOfficePath", result.Error, StringComparison.Ordinal);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task LibreOfficeReportsAFailedRun()
    {
        _runner.ExitCode = 81;
        _runner.StandardError = "Error: source file could not be loaded";
        _runner.ProduceOutput = false;

        var result = await Libre().ConvertToPdfAsync(File_("a.docx"), DocumentFormats.Docx, Path.Combine(_directory, "a.pdf"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("exit code 81", result.Error, StringComparison.Ordinal);
        Assert.Contains("could not be loaded", result.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(_directory));
    }

    [Fact]
    public async Task LibreOfficeReportsAMissingOutput()
    {
        _runner.ProduceOutput = false;

        var result = await Libre().ConvertToPdfAsync(File_("a.docx"), DocumentFormats.Docx, Path.Combine(_directory, "a.pdf"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("produced no PDF", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibreOfficeTruncatesLongErrors()
    {
        _runner.ExitCode = 1;
        _runner.StandardError = new string('x', 5000);
        _runner.ProduceOutput = false;

        var result = await Libre().ConvertToPdfAsync(File_("a.docx"), DocumentFormats.Docx, Path.Combine(_directory, "a.pdf"), CancellationToken.None);

        Assert.True(result.Error!.Length < 400);
    }

    [Fact]
    public async Task LibreOfficeGivesUpAfterTheTimeout()
    {
        _runner.Hang = TimeSpan.FromSeconds(30);

        var result = await Libre(options => options.Timeout = TimeSpan.FromMilliseconds(50))
            .ConvertToPdfAsync(File_("a.docx"), DocumentFormats.Docx, Path.Combine(_directory, "a.pdf"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("did not finish", result.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(_directory));
    }

    [Fact]
    public async Task ACancellationFromTheCallerIsNotAConversionFailure()
    {
        _runner.Hang = TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Libre().ConvertToPdfAsync(File_("a.docx"), DocumentFormats.Docx, Path.Combine(_directory, "a.pdf"), cts.Token));
    }

    [Fact]
    public async Task LibreOfficeReportsAProcessThatCannotStart()
    {
        _runner.Throw = new InvalidOperationException("Cannot start 'soffice'");

        var result = await Libre().ConvertToPdfAsync(File_("a.docx"), DocumentFormats.Docx, Path.Combine(_directory, "a.pdf"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Cannot start", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibreOfficeRefusesFormatsItDoesNotHandle()
    {
        var result = await Libre().ConvertToPdfAsync(File_("a.png"), DocumentFormats.Png, Path.Combine(_directory, "a.pdf"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task LibreOfficeChecksItsArguments()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Libre().ConvertToPdfAsync(" ", DocumentFormats.Docx, "out.pdf", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => Libre().ConvertToPdfAsync("a.docx", DocumentFormats.Docx, " ", CancellationToken.None));
    }

    [Theory]
    [InlineData(TinyJpeg, DocumentFormats.Jpeg, "a.jpg", false)]
    [InlineData(TinyPortraitPng, DocumentFormats.Png, "a.png", true)]
    public async Task ImagesBecomeAnA4PageFittedToTheirShape(string base64, string format, string name, bool portrait)
    {
        var source = File_(name, Convert.FromBase64String(base64));
        var output = Path.Combine(_directory, name + ".pdf");

        var result = await new ImageToPdfConverter().ConvertToPdfAsync(source, format, output, CancellationToken.None);

        Assert.True(result.Succeeded);
        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);
        var page = document.Pages[0];
        Assert.Equal(portrait, page.Height.Point > page.Width.Point);
        Assert.Equal(595, Math.Min(page.Width.Point, page.Height.Point), 1);
        Assert.Equal(842, Math.Max(page.Width.Point, page.Height.Point), 1);
    }

    [Fact]
    public async Task ACorruptImageFailsWithAMessage()
    {
        var result = await new ImageToPdfConverter().ConvertToPdfAsync(File_("bad.png", [1, 2, 3, 4]), DocumentFormats.Png, Path.Combine(_directory, "bad.pdf"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("could not be converted", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingImageFailsWithAMessage()
    {
        var result = await new ImageToPdfConverter().ConvertToPdfAsync(Path.Combine(_directory, "missing.png"), DocumentFormats.Png, Path.Combine(_directory, "m.pdf"), CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task TheImageConverterRefusesOtherFormatsAndHonoursCancellation()
    {
        var converter = new ImageToPdfConverter();

        Assert.True(converter.CanConvert(DocumentFormats.Jpeg));
        Assert.True(converter.CanConvert(DocumentFormats.Png));
        Assert.False(converter.CanConvert(DocumentFormats.Docx));
        Assert.False((await converter.ConvertToPdfAsync("a.docx", DocumentFormats.Docx, "a.pdf", CancellationToken.None)).Succeeded);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ConvertToPdfAsync("a.png", DocumentFormats.Png, "a.pdf", cts.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => converter.ConvertToPdfAsync(" ", DocumentFormats.Png, "a.pdf", CancellationToken.None));
    }

    [Fact]
    public async Task TheRouterSendsEachFormatToItsConverter()
    {
        var router = new DocumentConverter([new ImageToPdfConverter(), Libre()]);
        var png = File_("a.png", Convert.FromBase64String(TinyPortraitPng));

        Assert.True(router.CanConvert(DocumentFormats.Png));
        Assert.True(router.CanConvert(DocumentFormats.Docx));
        Assert.False(router.CanConvert(DocumentFormats.Pdf));
        Assert.True((await router.ConvertToPdfAsync(png, DocumentFormats.Png, Path.Combine(_directory, "p.pdf"), CancellationToken.None)).Succeeded);
        Assert.Empty(_runner.Calls);
        Assert.True((await router.ConvertToPdfAsync(File_("a.docx"), DocumentFormats.Docx, Path.Combine(_directory, "d.pdf"), CancellationToken.None)).Succeeded);
        Assert.Single(_runner.Calls);
        Assert.False((await router.ConvertToPdfAsync("a.pdf", DocumentFormats.Pdf, "a.out", CancellationToken.None)).Succeeded);
    }

    private static ServiceProvider Build(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDocumentConversion(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void TheConversionIsRegisteredWithItsDefaults()
    {
        using var provider = Build();

        var converter = provider.GetRequiredService<IDocumentConverter>();
        var options = provider.GetRequiredService<IOptions<ConversionOptions>>().Value;

        Assert.True(converter.CanConvert(DocumentFormats.Docx));
        Assert.True(converter.CanConvert(DocumentFormats.Jpeg));
        Assert.Equal(TimeSpan.FromMinutes(2), options.Timeout);
        Assert.Null(options.LibreOfficePath);
    }

    [Fact]
    public void TheOptionsAreBoundAndValidated()
    {
        using var custom = Build(("Conversion:LibreOfficePath", "/opt/lo/soffice"), ("Conversion:Timeout", "00:05:00"));
        using var invalid = Build(("Conversion:Timeout", "00:00:00"));

        var options = custom.GetRequiredService<IOptions<ConversionOptions>>().Value;

        Assert.Equal("/opt/lo/soffice", options.LibreOfficePath);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Timeout);
        Assert.Throws<OptionsValidationException>(() => invalid.GetRequiredService<IOptions<ConversionOptions>>().Value);
    }

    [Fact]
    public void RegistrationChecksItsArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ConversionServiceCollectionExtensions.AddDocumentConversion(null!, new ConfigurationBuilder().Build()));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddDocumentConversion(null!));
    }

    [Fact]
    public void TheResultsKnowTheirOutcome()
    {
        Assert.True(ConversionResult.Success().Succeeded);
        Assert.Null(ConversionResult.Success().Error);
        Assert.False(ConversionResult.Failure("no").Succeeded);
        Assert.Equal("no", ConversionResult.Failure("no").Error);
        Assert.Throws<ArgumentException>(() => ConversionResult.Failure(" "));
    }
}
