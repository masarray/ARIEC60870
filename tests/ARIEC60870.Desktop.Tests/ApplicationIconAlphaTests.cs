// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

/// <summary>
/// Regresses the white matte seen around IEC protocol logos in the Windows
/// taskbar and title bar. The displayed Window.Icon uses these exact PNGs,
/// not the generic executable's ICO file.
/// </summary>
public sealed class ApplicationIconAlphaTests
{
    [Theory]
    [InlineData("iec101-icon.png")]
    [InlineData("iec103-icon.png")]
    [InlineData("iec104-icon.png")]
    public void ProtocolWindowIconHasTransparentCornersAndOpaqueBadge(string fileName)
    {
        var icon = new BitmapImage();
        icon.BeginInit();
        icon.CacheOption = BitmapCacheOption.OnLoad;
        icon.UriSource = new Uri(LocateAsset(fileName), UriKind.Absolute);
        icon.EndInit();

        Assert.True(icon.PixelWidth >= 128 && icon.PixelHeight >= 128);
        Assert.Equal(0, PixelAlpha(icon, 0, 0));
        Assert.Equal(0, PixelAlpha(icon, icon.PixelWidth - 1, 0));
        Assert.Equal(0, PixelAlpha(icon, 0, icon.PixelHeight - 1));
        Assert.Equal(0, PixelAlpha(icon, icon.PixelWidth - 1, icon.PixelHeight - 1));
        Assert.Equal(255, PixelAlpha(icon, icon.PixelWidth / 2, icon.PixelHeight / 2));
    }

    [Fact]
    public void GenericExecutableIconContainsTransparentCorners()
    {
        using var stream = File.OpenRead(LocateAsset("ariec60870-app.ico"));
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        Assert.True(decoder.Frames.Count >= 3, "ICO should include taskbar-friendly 16/32/48 px frames.");
        foreach (var frame in decoder.Frames)
        {
            Assert.Equal(0, PixelAlpha(frame, 0, 0));
            Assert.Equal(0, PixelAlpha(frame, frame.PixelWidth - 1, frame.PixelHeight - 1));
        }
    }

    private static byte PixelAlpha(BitmapSource image, int x, int y)
    {
        var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        pixels.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[3];
    }

    private static string LocateAsset(string fileName)
    {
        for (DirectoryInfo? root = new(AppContext.BaseDirectory); root is not null;
             root = root.Parent)
        {
            var candidate = Path.Combine(root.FullName, "src", "ARIEC60870.Desktop",
                "Assets", "Icons", fileName);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Repository icon asset not found", fileName);
    }
}
