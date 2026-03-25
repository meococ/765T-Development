using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core;

internal sealed class RevitSourceMetadata
{
    public static RevitSourceMetadata Empty { get; } = new RevitSourceMetadata();

    public string RevitVersion { get; set; } = string.Empty;

    public bool? IsWorkshared { get; set; }

    public string WorksharingSummary { get; set; } = string.Empty;
}

internal static class RevitSourceMetadataDetector
{
    private const int TextProbeBytes = 2 * 1024 * 1024;
    private const int StatFlagNoName = 1;
    private const int StorageRead = 0x00000000;
    private const int StorageShareDenyWrite = 0x00000020;
    private static readonly Regex ProductVersionRegex = new Regex(@"product-version>\s*(20\d{2})\s*<", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex FormatVersionRegex = new Regex(@"Format:\s*(20\d{2})", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex LegacyVersionRegex = new Regex(@"Revit\s*(20\d{2})", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex WorksharingRegex = new Regex(@"Worksharing:\s*([^\r\n]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static RevitSourceMetadata Detect(string path, string sourceKind)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return RevitSourceMetadata.Empty;
        }

        try
        {
            if (string.Equals(sourceKind, ProjectSourceKinds.RevitFamily, StringComparison.OrdinalIgnoreCase))
            {
                return DetectFamily(path);
            }

            if (string.Equals(sourceKind, ProjectSourceKinds.RevitProject, StringComparison.OrdinalIgnoreCase))
            {
                return DetectProject(path);
            }
        }
        catch
        {
            return RevitSourceMetadata.Empty;
        }

        return RevitSourceMetadata.Empty;
    }

    private static RevitSourceMetadata DetectFamily(string path)
    {
        var probe = ReadTextProbe(path);
        var version = TryMatchValue(ProductVersionRegex, probe);
        if (string.IsNullOrWhiteSpace(version))
        {
            return RevitSourceMetadata.Empty;
        }

        return new RevitSourceMetadata
        {
            RevitVersion = version
        };
    }

    private static RevitSourceMetadata DetectProject(string path)
    {
        var basicFileInfoText = ReadBasicFileInfoText(path);
        var probe = string.IsNullOrWhiteSpace(basicFileInfoText) ? ReadTextProbe(path) : basicFileInfoText;
        var version = TryMatchValue(FormatVersionRegex, probe);
        if (string.IsNullOrWhiteSpace(version))
        {
            version = TryMatchValue(LegacyVersionRegex, probe);
        }

        var rawWorksharing = TryMatchValue(WorksharingRegex, probe);
        var isWorkshared = string.IsNullOrWhiteSpace(rawWorksharing)
            ? (bool?)null
            : rawWorksharing.IndexOf("not", StringComparison.OrdinalIgnoreCase) < 0;

        return new RevitSourceMetadata
        {
            RevitVersion = version,
            IsWorkshared = isWorkshared,
            WorksharingSummary = FormatWorksharingSummary(rawWorksharing, isWorkshared)
        };
    }

    private static string ReadTextProbe(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytesToRead = (int)Math.Min(TextProbeBytes, stream.Length);
        if (bytesToRead <= 0)
        {
            return string.Empty;
        }

        var buffer = new byte[bytesToRead];
        var bytesRead = stream.Read(buffer, 0, bytesToRead);
        return DecodeBuffer(buffer, bytesRead);
    }

    private static string ReadBasicFileInfoText(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return string.Empty;
        }

        IStorage? storage = null;
        IStream? stream = null;
        try
        {
            var hr = StgOpenStorage(path, null, StorageRead | StorageShareDenyWrite, IntPtr.Zero, 0, out storage);
            if (hr != 0 || storage == null)
            {
                return string.Empty;
            }

            storage.OpenStream("BasicFileInfo", IntPtr.Zero, StorageRead | StorageShareDenyWrite, 0, out stream);
            if (stream == null)
            {
                return string.Empty;
            }

            stream.Stat(out var stat, StatFlagNoName);
            var bufferLength = checked((int)Math.Min((long)TextProbeBytes, stat.cbSize));
            if (bufferLength <= 0)
            {
                return string.Empty;
            }

            var buffer = new byte[bufferLength];
            stream.Read(buffer, bufferLength, IntPtr.Zero);
            return DecodeBuffer(buffer, bufferLength);
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            ReleaseComObject(stream);
            ReleaseComObject(storage);
        }
    }

    private static string DecodeBuffer(byte[] buffer, int bytesRead)
    {
        if (buffer.Length == 0 || bytesRead <= 0)
        {
            return string.Empty;
        }

        var sanitized = buffer
            .Take(bytesRead)
            .Where(value => value != 0)
            .ToArray();
        return sanitized.Length == 0 ? string.Empty : Encoding.UTF8.GetString(sanitized);
    }

    private static string TryMatchValue(Regex regex, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var match = regex.Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static string FormatWorksharingSummary(string rawValue, bool? isWorkshared)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var normalized = NormalizeWhitespace(rawValue.Trim().TrimEnd('.', ';', ','));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return isWorkshared == false
            ? "Not workshared"
            : isWorkshared == true
                ? $"Workshared ({normalized})"
                : normalized;
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static void ReleaseComObject(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int StgOpenStorage(
        string pwcsName,
        IStorage? pstgPriority,
        int grfMode,
        IntPtr snbExclude,
        int reserved,
        out IStorage ppstgOpen);

    [ComImport]
    [Guid("0000000B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IStorage
    {
        void CreateStream([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, int grfMode, int reserved1, int reserved2, out IStream ppstm);
        void OpenStream([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, IntPtr reserved1, int grfMode, int reserved2, out IStream ppstm);
        void CreateStorage([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, int grfMode, int reserved1, int reserved2, out IStorage ppstg);
        void OpenStorage([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, IStorage? pstgPriority, int grfMode, IntPtr snbExclude, int reserved, out IStorage ppstg);
        void CopyTo(int ciidExclude, Guid rgiidExclude, IntPtr snbExclude, IStorage pstgDest);
        void MoveElementTo([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, IStorage pstgDest, [MarshalAs(UnmanagedType.LPWStr)] string pwcsNewName, int grfFlags);
        void Commit(int grfCommitFlags);
        void Revert();
        void EnumElements(int reserved1, IntPtr reserved2, int reserved3, out object ppenum);
        void DestroyElement([MarshalAs(UnmanagedType.LPWStr)] string pwcsName);
        void RenameElement([MarshalAs(UnmanagedType.LPWStr)] string pwcsOldName, [MarshalAs(UnmanagedType.LPWStr)] string pwcsNewName);
        void SetElementTimes([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, FILETIME[] pctime, FILETIME[] patime, FILETIME[] pmtime);
        void SetClass(ref Guid clsid);
        void SetStateBits(int grfStateBits, int grfMask);
        void Stat(out STATSTG pstatstg, int grfStatFlag);
    }
}
