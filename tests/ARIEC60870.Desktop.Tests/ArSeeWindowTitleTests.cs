// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.Services;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class ArSeeWindowTitleTests
{
    private const string ExpectedCaption = "ArSee • Open Source IEC 60870-5-101/103/104 Protocol Tester & Analyzer";

    [Theory]
    [InlineData("3.6.8-rc.4+release", "3.6.8-rc.4")]
    [InlineData("3.6.8+p0-safe-installer-upgrade", "3.6.8")]
    [InlineData("3.6.8", "3.6.8")]
    public void BuildMetadataIsNotShownButPrereleaseIdentifierIs(string informationalVersion, string expectedVersion)
    {
        Assert.Equal($"{ExpectedCaption} • version {expectedVersion}",
            ArSeeWindowTitle.Format(informationalVersion, new Version(3, 6, 8, 0)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingInformationalVersionUsesAssemblyVersion(string? informationalVersion)
    {
        Assert.Equal($"{ExpectedCaption} • version 3.6.8",
            ArSeeWindowTitle.Format(informationalVersion, new Version(3, 6, 8, 0)));
    }

    [Fact]
    public void RunningDesktopAssemblyHasAUsableVersionCaption()
    {
        var title = ArSeeWindowTitle.ForAssembly(typeof(ArSeeWindowTitle).Assembly);
        Assert.StartsWith($"{ExpectedCaption} • version ", title);
        Assert.DoesNotContain("+", title);
    }
}
