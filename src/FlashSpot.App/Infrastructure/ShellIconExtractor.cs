using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlashSpot.App.Infrastructure;

internal static class ShellIconExtractor
{
    private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIconImage(string? path, string extension)
    {
        var ext = string.IsNullOrWhiteSpace(extension) ? string.Empty : extension.Trim().ToLowerInvariant();
        var cacheKey = ShouldCacheByPath(ext) && !string.IsNullOrWhiteSpace(path)
            ? path!
            : $"ext:{ext}";

        if (IconCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        ImageSource? result = null;

        if (!string.IsNullOrWhiteSpace(path))
        {
            result = TryGetShellIcon(path!, useFileAttributes: false);
        }

        if (result is null && !string.IsNullOrWhiteSpace(ext))
        {
            result = TryGetShellIcon(ext, useFileAttributes: true);
        }

        IconCache[cacheKey] = result;
        return result;
    }

    private static bool ShouldCacheByPath(string extension)
    {
        return extension is ".exe" or ".lnk" or ".ico";
    }

    private static ImageSource? TryGetShellIcon(string pathOrExtension, bool useFileAttributes)
    {
        var attributes = useFileAttributes ? FileAttributeNormal : 0u;
        var flags = ShgfiIcon | ShgfiSmallIcon | (useFileAttributes ? ShgfiUseFileAttributes : 0u);

        var info = new SHFILEINFO();
        var result = SHGetFileInfo(
            pathOrExtension,
            attributes,
            out info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            flags);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(24, 24));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            _ = DestroyIcon(info.hIcon);
        }
    }

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x000000080;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}
