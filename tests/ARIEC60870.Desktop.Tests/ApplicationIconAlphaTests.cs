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
            AssertTransparentPerimeter(frame, $"ICO {frame.PixelWidth}x{frame.PixelHeight}");
            Assert.Equal(255, PixelAlpha(frame, frame.PixelWidth / 2, frame.PixelHeight / 2));
        }
    }

    [Theory]
    [InlineData("iec101-icon.png")]
    [InlineData("iec103-icon.png")]
    [InlineData("iec104-icon.png")]
    public void ProtocolBadgesHaveFullyTransparentCanvasPerimeters(string fileName)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(LocateAsset(fileName), UriKind.Absolute);
        bitmap.EndInit();
        AssertTransparentPerimeter(bitmap, fileName);
    }

    private static void AssertTransparentPerimeter(BitmapSource source, string label)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = image.PixelWidth;
        var height = image.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        image.CopyPixels(pixels, stride, 0);
        for (var x = 0; x < width; x++)
        {
            Assert.True(pixels[4 * x + 3] == 0, $"{label}: top border x={x} has alpha.");
            Assert.True(pixels[(height - 1) * stride + 4 * x + 3] == 0,
                $"{label}: bottom border x={x} has alpha.");
        }
        for (var y = 0; y < height; y++)
        {
            Assert.True(pixels[y * stride + 3] == 0, $"{label}: left border y={y} has alpha.");
            Assert.True(pixels[y * stride + (width - 1) * 4 + 3] == 0,
                $"{label}: right border y={y} has alpha.");
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
