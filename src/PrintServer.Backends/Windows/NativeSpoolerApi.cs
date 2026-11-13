using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PrintServer.Backends.Windows;

[SupportedOSPlatform("windows")]
[ExcludeFromCodeCoverage(Justification = "Thin wrapper over winspool.drv; needs a real Windows spooler.")]
public sealed partial class NativeSpoolerApi : ISpoolerApi
{
    private const int PrinterEnumLocal = 0x2;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorInsufficientBuffer = 122;
    private const int BufferSize = 64 * 1024;

    public IReadOnlyList<SpoolerPrinter> EnumeratePrinters()
    {
        _ = EnumPrinters(PrinterEnumLocal, null, 2, nint.Zero, 0, out var needed, out _);
        if (needed == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!EnumPrinters(PrinterEnumLocal, null, 2, buffer, needed, out _, out var returned))
            {
                throw new InvalidOperationException($"EnumPrinters failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }

            var printers = new List<SpoolerPrinter>(returned);
            var size = Marshal.SizeOf<PrinterInfo2>();
            for (var i = 0; i < returned; i++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo2>(buffer + (i * size));
                printers.Add(new SpoolerPrinter(Marshal.PtrToStringUni(info.PrinterName) ?? string.Empty, info.Status));
            }

            return printers;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public string? GetDefaultPrinterName()
    {
        const int capacity = 1024;
        var buffer = Marshal.AllocHGlobal(capacity * sizeof(char));
        try
        {
            var length = capacity;
            return GetDefaultPrinter(buffer, ref length) ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public int SubmitRawDocument(string printerName, string documentName, Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!OpenPrinter(printerName, out var printer, nint.Zero))
        {
            throw new InvalidOperationException($"Cannot open printer '{printerName}': {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        var docName = Marshal.StringToHGlobalUni(documentName);
        var dataType = Marshal.StringToHGlobalUni("RAW");
        var documentStarted = false;
        try
        {
            var info = new DocInfo1 { DocName = docName, OutputFile = nint.Zero, DataType = dataType };
            var jobId = StartDocPrinter(printer, 1, ref info);
            if (jobId <= 0)
            {
                throw new InvalidOperationException($"StartDocPrinter failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }

            documentStarted = true;
            if (!StartPagePrinter(printer))
            {
                throw new InvalidOperationException($"StartPagePrinter failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }

            var buffer = new byte[BufferSize];
            int read;
            while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                if (!WritePrinter(printer, ref buffer[0], read, out var written) || written != read)
                {
                    throw new InvalidOperationException($"WritePrinter failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                }
            }

            _ = EndPagePrinter(printer);
            return jobId;
        }
        finally
        {
            if (documentStarted)
            {
                _ = EndDocPrinter(printer);
            }

            _ = ClosePrinter(printer);
            Marshal.FreeHGlobal(docName);
            Marshal.FreeHGlobal(dataType);
        }
    }

    public uint? GetJobStatus(string printerName, int jobId)
    {
        if (!OpenPrinter(printerName, out var printer, nint.Zero))
        {
            throw new InvalidOperationException($"Cannot open printer '{printerName}': {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        try
        {
            _ = GetJob(printer, jobId, 1, nint.Zero, 0, out var needed);
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorInvalidParameter)
            {
                return null;
            }

            if (error != ErrorInsufficientBuffer || needed == 0)
            {
                throw new InvalidOperationException($"GetJob failed: {new Win32Exception(error).Message}");
            }

            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetJob(printer, jobId, 1, buffer, needed, out _))
                {
                    error = Marshal.GetLastWin32Error();
                    return error == ErrorInvalidParameter
                        ? null
                        : throw new InvalidOperationException($"GetJob failed: {new Win32Exception(error).Message}");
                }

                return Marshal.PtrToStructure<JobInfo1>(buffer).Status;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = ClosePrinter(printer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DocInfo1
    {
        public nint DocName;
        public nint OutputFile;
        public nint DataType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo2
    {
        public nint ServerName;
        public nint PrinterName;
        public nint ShareName;
        public nint PortName;
        public nint DriverName;
        public nint Comment;
        public nint Location;
        public nint DevMode;
        public nint SepFile;
        public nint PrintProcessor;
        public nint DataType;
        public nint Parameters;
        public nint SecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint Jobs;
        public uint AveragePpm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobInfo1
    {
        public uint JobId;
        public nint PrinterName;
        public nint MachineName;
        public nint UserName;
        public nint Document;
        public nint DataType;
        public nint StatusText;
        public uint Status;
        public uint Priority;
        public uint Position;
        public uint TotalPages;
        public uint PagesPrinted;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("winspool.drv", EntryPoint = "OpenPrinterW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenPrinter(string printerName, out nint printer, nint defaults);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClosePrinter(nint printer);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true)]
    private static partial int StartDocPrinter(nint printer, int level, ref DocInfo1 docInfo);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndDocPrinter(nint printer);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StartPagePrinter(nint printer);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndPagePrinter(nint printer);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WritePrinter(nint printer, ref byte buffer, int count, out int written);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("winspool.drv", EntryPoint = "EnumPrintersW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumPrinters(int flags, string? name, int level, nint printerEnum, int bufferSize, out int needed, out int returned);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDefaultPrinter(nint buffer, ref int size);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("winspool.drv", EntryPoint = "GetJobW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetJob(nint printer, int jobId, int level, nint job, int bufferSize, out int needed);
}
