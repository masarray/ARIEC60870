// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace ARIEC60870.Repository.Tests;

public sealed class SetupHeaderActionStyleTests
{
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ConfigHeaderKeepsNativeHandlersAndUsesSuppliedSvgPath()
    {
        var doc = XDocument.Load(Source("MainWindow.xaml"));
        var buttons = doc.Descendants().Where(x => x.Name.LocalName == "Button").ToArray();
        var connect = buttons.Single(x => (string?)x.Attribute(Xaml + "Name") == "SetupConnectButton");
        var close = buttons.Single(x => (string?)x.Attribute(Xaml + "Name") == "SetupCloseButton");

        Assert.Equal("CloseSetupAndConnect_Click", (string?)connect.Attribute("Click"));
        Assert.Equal("CloseSetup_Click", (string?)close.Attribute("Click"));
        Assert.Equal("{StaticResource SetupConnectButtonStyle}", (string?)connect.Attribute("Style"));
        Assert.Equal("{StaticResource SetupCloseIconButtonStyle}", (string?)close.Attribute("Style"));
        Assert.Equal("Close configuration",
            (string?)close.Attribute("AutomationProperties.Name"));
        // A fixed action width accommodates all labels and prevents visual jumps.
        Assert.Equal("176", (string?)connect.Attribute("Width"));
        Assert.Null(connect.Attribute("MinWidth"));
        Assert.Equal("SetupActionFooter",
            (string?)connect.Parent!.Attribute(Xaml + "Name"));
        Assert.Equal("1", (string?)close.Attribute("Grid.Column"));
        var cancel = buttons.Single(x => (string?)x.Attribute(Xaml + "Name") == "SetupCancelButton");
        Assert.Equal("116", (string?)cancel.Attribute("Width"));
        Assert.Equal("CloseSetup_Click", (string?)cancel.Attribute("Click"));
        Assert.Same(connect.Parent, cancel.Parent);
        Assert.Null(close.Attribute("Content"));

        var glyph = close.Elements().Single(x => x.Name.LocalName == "Path");
        Assert.Equal("SetupCloseIcon", (string?)glyph.Attribute(Xaml + "Name"));
        Assert.Equal(
            "M6.4,19L5,17.6L10.6,12L5,6.4L6.4,5L12,10.6L17.6,5L19,6.4L13.4,12L19,17.6L17.6,19L12,13.4L6.4,19Z",
            (string?)glyph.Attribute("Data"));
        Assert.Equal("14", (string?)glyph.Attribute("Width"));
        Assert.Equal("14", (string?)glyph.Attribute("Height"));
        Assert.Contains("Foreground", (string?)glyph.Attribute("Fill"));
    }

    [Fact]
    public void SetupHasViewportSafeScrollBodyAndFixedBottomActions()
    {
        var doc = XDocument.Load(Source("MainWindow.xaml"));
        var setup = doc.Descendants().Single(x =>
            (string?)x.Attribute(Xaml + "Name") == "SetupOverlay");
        var card = setup.Elements().Single(x => x.Name.LocalName == "Border");
        Assert.Equal("900", (string?)card.Attribute("MaxWidth"));
        Assert.Equal("780", (string?)card.Attribute("MaxHeight"));
        Assert.Equal("Center", (string?)card.Attribute("VerticalAlignment"));
        Assert.Equal("SetupDialogCard", (string?)card.Attribute(Xaml + "Name"));

        var scroller = setup.Descendants().Single(x =>
            (string?)x.Attribute(Xaml + "Name") == "SetupSettingsScroll");
        Assert.Equal("2", (string?)scroller.Attribute("Grid.Row"));
        Assert.Equal("Auto", (string?)scroller.Attribute("VerticalScrollBarVisibility"));
        var footer = setup.Descendants().Single(x =>
            (string?)x.Attribute(Xaml + "Name") == "SetupActionFooter");
        Assert.Equal("4", (string?)footer.Parent?.Attribute("Grid.Row"));
        var mapping = setup.Descendants().Single(x =>
            (string?)x.Attribute(Xaml + "Name") == "MappingProfilePanel");
        Assert.Contains(scroller.Descendants(), x => ReferenceEquals(x, mapping));
        Assert.Contains(setup.Descendants(), x =>
            x.Name.LocalName == "Expander" &&
            (string?)x.Attribute("Header") == "Interoperability · advanced");
        Assert.Contains(setup.Descendants(), x =>
            (string?)x.Attribute(Xaml + "Name") == "AdvancedFieldWidthsExpander");
    }

    [Fact]
    public void ConnectedSetupIsReadOnlyAndDisconnectUsesExistingSessionToggle()
    {
        var code = File.ReadAllText(Source("MainWindow.xaml.cs"));
        var session = File.ReadAllText(Source("Features/MainWindow.Session.cs"));
        var state = File.ReadAllText(Source("Features/MainWindow.MappingValueStatus.cs"));
        Assert.Contains("ConnectToggle_Click(sender, e);", code);
        Assert.Contains("if (_sessionCancellation is null)", code);
        Assert.Contains("LoadSetupPreferences();", code);
        Assert.Contains("SetupConnectButton.Tag = isRunning ? \"Disconnect\" : \"Connect\";", session);
        Assert.Contains("SetupButton.IsEnabled = true;", state);
        Assert.DoesNotContain("SetupButton.IsEnabled = !isRunning;", state);
        foreach (var field in new[] { "InteroperabilityConventionComboBox",
            "PassiveSourceComboBox", "SerialHandshakeComboBox",
            "SerialRtsCheckBox", "SerialDtrCheckBox",
            "PassiveLinkBScadaPortComboBox", "PassiveLinkBRtuPortComboBox",
            "PassiveCaptureFolderPicker", "SetupImportProfileButton",
            "SetupRefreshPortsButton", "BackupAddressReviewButton",
            "BackupAddressContinueButton" })
        {
            Assert.Contains(field + ".IsEnabled = !isRunning;", state);
        }
        var names = new[] { "PassiveCaptureFolderPicker", "SetupImportProfileButton",
            "SetupRefreshPortsButton", "BackupAddressReviewButton",
            "BackupAddressContinueButton" };
        var setup = XDocument.Load(Source("MainWindow.xaml"));
        foreach (var name in names)
            Assert.Single(setup.Descendants(),
                x => (string?)x.Attribute(Xaml + "Name") == name);
        // Retain scrolling/expanders online; only configuration inputs lock.
        Assert.DoesNotContain("SetupSettingsScroll.IsEnabled = !isRunning", state);
        var theme = XDocument.Load(Source("Resources/ModernTheme.xaml"));
        foreach (var key in new[] { "SetupDisconnectIdleBrush",
            "SetupDisconnectHoverBrush", "SetupDisconnectPressedBrush" })
            Assert.NotNull(Resource(theme, "LinearGradientBrush", key));
        var buttonTemplate = Resource(theme, "ControlTemplate", "SetupConnectButtonTemplate");
        var disconnect = Assert.Single(buttonTemplate.Descendants(), x =>
            x.Name.LocalName == "Trigger" &&
            (string?)x.Attribute("Property") == "Tag" &&
            (string?)x.Attribute("Value") == "Disconnect");
        Assert.Equal("{StaticResource SetupDisconnectShadow}",
            TriggerSetterValue(disconnect, "SetupConnectSurface", "Effect"));
        Assert.NotNull(Resource(theme, "DropShadowEffect", "SetupDisconnectShadow"));
    }

    [Fact]
    public void EnterConnectDoesNotTriggerOnlineDisconnectOrDismissFailedSetup()
    {
        var help = File.ReadAllText(Source("Features/MainWindow.Help.cs"));
        var setup = File.ReadAllText(Source("MainWindow.xaml.cs"));
        Assert.Contains("CanSubmitSetupOnEnter(ResolveSetupEnterSource(e.OriginalSource))", help);
        Assert.Contains("_sessionCancellation is not null", help);
        Assert.Contains("IsDropDownOpen: true", help);
        Assert.Contains("AcceptsReturn: true", help);
        // WPF CheckBox/ToggleButton derive ButtonBase but must not be
        // mistaken for Cancel/Close or other explicit action Buttons.
        Assert.Contains("focused is not System.Windows.Controls.Button button", help);
        Assert.DoesNotContain("ButtonBase button", help);
        Assert.Contains("SetupConnectButton.RaiseEvent(new RoutedEventArgs(", help);
        Assert.Contains("ButtonBase.ClickEvent", help);
        Assert.Contains("SetupOverlay.IsVisibleChanged += (_, _) => UpdateSetupDefaultAction();", setup);
        Assert.Contains("SetupConnectButton.IsDefault =", setup);
        Assert.Contains("SetupOverlay.IsVisible && _sessionCancellation is null", setup);
        var acceptance = File.ReadAllText(Source("Features/MainWindow.ObservationAcceptance.cs"));
        Assert.Contains("VerifySetupEnterRoutingWithoutTransport(\"IEC-101 Single\")", acceptance);
        Assert.Contains("VerifySetupEnterRoutingWithoutTransport(\"IEC-101 Passive\")", acceptance);
        Assert.Contains("2 => \"IEC-101 Dual\"", acceptance);
        Assert.Contains("0 => \"IEC-103\"", acceptance);
        Assert.Contains("_ => \"IEC-104\"", acceptance);
        Assert.Contains("target.RaiseEvent(key);", acceptance);
        Assert.Contains("RoutedEvent = Keyboard.PreviewKeyDownEvent", acceptance);
        Assert.Contains("SetupConnectButton.Click -= CloseSetupAndConnect_Click;", acceptance);
        Assert.Contains("SetupConnectButton.Click += CloseSetupAndConnect_Click;", acceptance);

        var handler = setup.Split("private void CloseSetupAndConnect_Click(")[1]
            .Split("private void RefreshPorts()", 2)[0]
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("if (_sessionCancellation is not null)", handler);
        Assert.Contains("ConnectToggle_Click(sender, e);", handler);
        Assert.Contains("if (_sessionCancellation is not null)\n            SetupOverlay.Visibility = Visibility.Collapsed;", handler);
        // No unconditional collapse on the Disconnect branch.
        Assert.DoesNotContain("SetupOverlay.Visibility = Visibility.Collapsed;\n        ConnectToggle_Click", handler);
    }

    [Fact]
    public void BrandPngAlignsToHeaderTopWithoutShiftingRailButtons()
    {
        var doc = XDocument.Load(Source("MainWindow.xaml"));
        var image = doc.Descendants().Single(x =>
            (string?)x.Attribute(Xaml + "Name") == "ProtocolLogoImage");
        var logoBox = image.Parent!;
        var railScroll = doc.Descendants().Single(x =>
            (string?)x.Attribute(Xaml + "Name") == "ActionRailScroll");
        var railShell = railScroll.Parent!;
        var header = doc.Descendants().Single(x =>
            (string?)x.Attribute(Xaml + "Name") == "MainWorkspaceHeader");

        Assert.Equal("0", (string?)railShell.Attribute("Grid.Row"));
        Assert.Equal("0", (string?)header.Attribute("Grid.Row"));
        Assert.Equal("8,0,8,8", (string?)railShell.Attribute("Padding"));
        Assert.Equal("0,-4,0,25", (string?)logoBox.Attribute("Margin"));
        Assert.Equal("68", (string?)logoBox.Attribute("Width"));
        Assert.Equal("70", (string?)logoBox.Attribute("Height"));
        Assert.Equal("Assets/Icons/ariec60870-icon.png",
            (string?)image.Attribute("Source"));

        // Inset(0) + logo Y(-4) + transparent PNG edge(4) = header Y(0).
        // The 8 DIP removed from the rail's top padding is compensated in
        // the logo's bottom margin, so Setup/Connect keep their original Y.
        Assert.Equal(0, 0 - 4 + 4);
        Assert.Equal(8 + 70 + 13, 0 - 4 + 70 + 25);
        var buttons = logoBox.Parent!.Elements()
            .Where(x => x.Name.LocalName == "Button").ToArray();
        Assert.Equal("SetupButton", (string?)buttons[0].Attribute(Xaml + "Name"));
        Assert.Equal("StartButton", (string?)buttons[1].Attribute(Xaml + "Name"));
    }

    [Fact]
    public void ConfigButtonsHaveIndependentHoverAndPressedVisualStates()
    {
        var doc = XDocument.Load(Source("Resources/ModernTheme.xaml"));
        var connect = Resource(doc, "Style", "SetupConnectButtonStyle");
        var close = Resource(doc, "Style", "SetupCloseIconButtonStyle");
        Assert.Equal("{StaticResource ArSeeWorkspaceActionButtonStyle}",
            (string?)connect.Attribute("BasedOn"));
        Assert.Equal("30", SetterValue(close, "Height"));
        Assert.Equal("30", SetterValue(close, "Width"));

        var connectTemplate = Resource(doc, "ControlTemplate", "SetupConnectButtonTemplate");
        var closeTemplate = Resource(doc, "ControlTemplate", "SetupCloseIconButtonTemplate");
        Assert.Equal("{StaticResource SetupConnectButtonTemplate}", SetterValue(connect, "Template"));
        Assert.Equal("{StaticResource SetupCloseIconButtonTemplate}", SetterValue(close, "Template"));

        foreach (var key in new[] {
            "SetupConnectIdleBrush", "SetupConnectHoverBrush", "SetupConnectPressedBrush" })
        {
            var gradient = Resource(doc, "LinearGradientBrush", key);
            Assert.Equal(3, gradient.Elements().Count(x => x.Name.LocalName == "GradientStop"));
        }

        var connectHover = Trigger(connectTemplate, "IsMouseOver");
        var connectPressed = Trigger(connectTemplate, "IsPressed");
        Assert.Equal("{StaticResource SetupConnectHoverBrush}",
            TriggerSetterValue(connectHover, "SetupConnectSurface", "Background"));
        Assert.Equal("{StaticResource SetupConnectPressedBrush}",
            TriggerSetterValue(connectPressed, "SetupConnectSurface", "Background"));
        Assert.Contains(connectPressed.Descendants(),
            x => x.Name.LocalName == "TranslateTransform" &&
                 (string?)x.Attribute("Y") == "1");

        var closeHover = Trigger(closeTemplate, "IsMouseOver");
        var closePressed = Trigger(closeTemplate, "IsPressed");
        Assert.Equal("#FFF0F1", TriggerSetterValue(closeHover, "SetupCloseSurface", "Background"));
        Assert.Equal("#B42335", TriggerSetterValue(closeHover, null, "Foreground"));
        Assert.Equal("#EF3346", TriggerSetterValue(closePressed, "SetupCloseSurface", "Background"));
        Assert.Equal("#FFFFFF", TriggerSetterValue(closePressed, null, "Foreground"));
    }

    [Fact]
    public void ConfigCloseUsesCompactCaptionButtonWithQuietIdleAndRedFeedback()
    {
        var theme = XDocument.Load(Source("Resources/ModernTheme.xaml"));
        var close = Resource(theme, "Style", "SetupCloseIconButtonStyle");
        var template = Resource(theme, "ControlTemplate", "SetupCloseIconButtonTemplate");
        Assert.Equal("Transparent", SetterValue(close, "Background"));
        Assert.Equal("Transparent", SetterValue(close, "BorderBrush"));
        var surface = template.Descendants().Single(x => x.Name.LocalName == "Border" &&
            (string?)x.Attribute(Xaml + "Name") == "SetupCloseSurface");
        Assert.Equal("6", (string?)surface.Attribute("CornerRadius"));
        Assert.Equal("0", (string?)surface.Attribute("BorderThickness"));
        Assert.Equal("#FFF0F1", TriggerSetterValue(Trigger(template, "IsMouseOver"),
            "SetupCloseSurface", "Background"));
        Assert.Equal("#EF3346", TriggerSetterValue(Trigger(template, "IsPressed"),
            "SetupCloseSurface", "Background"));
    }

    [Fact]
    public void FrameDetailsIsPopupFreeButRetainsDecodedValuesAndAccessibleActions()
    {
        var window = XDocument.Load(Source("MainWindow.xaml"));
        var panel = Assert.Single(window.Descendants(), x =>
            (string?)x.Attribute(Xaml + "Name") == "FrameInterpreterPanel");
        Assert.Equal("False", (string?)panel.Attribute("ToolTipService.IsEnabled"));
        var grid = Assert.Single(panel.Descendants(), x =>
            (string?)x.Attribute(Xaml + "Name") == "FramePropertiesGrid");
        Assert.Equal("False", (string?)grid.Attribute("ToolTipService.IsEnabled"));
        Assert.Equal("ExcludeHeader", (string?)grid.Attribute("ClipboardCopyMode"));
        var templateColumns = grid.Descendants()
            .Where(x => x.Name.LocalName == "DataGridTemplateColumn").ToArray();
        Assert.Equal("{Binding Field}", (string?)templateColumns[0].Attribute("ClipboardContentBinding"));
        Assert.Equal("{Binding Value}", (string?)templateColumns[1].Attribute("ClipboardContentBinding"));

        Assert.DoesNotContain(panel.DescendantsAndSelf().SelectMany(x => x.Attributes()),
            a => a.Name.LocalName == "ToolTip" ||
                 (a.Name.LocalName == "ToolTipService.IsEnabled" && a.Value == "True"));
        Assert.Contains(panel.Descendants(), x =>
            x.Name.LocalName == "TextBlock" && (string?)x.Attribute("Text") == "{Binding Value}");

        var toggle = Assert.Single(window.Descendants(), x =>
            (string?)x.Attribute(Xaml + "Name") == "TraceInspectorToggle");
        Assert.Null(toggle.Attribute("ToolTip"));
        Assert.Equal("False", (string?)toggle.Attribute("ToolTipService.IsEnabled"));
        Assert.Equal("Expand Frame Details", (string?)toggle.Attribute("AutomationProperties.Name"));
        Assert.DoesNotContain("TraceInspectorToggle.ToolTip =",
            File.ReadAllText(Source("Features/MainWindow.ObservationLayout.cs")));
    }

    [Fact]
    public void CommandPanelLabelsUseRealIECTypeIdsAndNeverExposeGlobalClear()
    {
        var window = XDocument.Load(Source("MainWindow.xaml"));
        var panel = window.Descendants().Single(x => (string?)x.Attribute(Xaml + "Name") == "CommandDockPanel");
        var buttons = panel.Descendants().Where(x => x.Name.LocalName == "Button").ToArray();
        Assert.Equal("GI • TI-100", Button("CommandDockGiButton").Attribute("Content")?.Value);
        Assert.Equal("Timesync • TI-103", Button("CommandDockClockSyncButton").Attribute("Content")?.Value);
        Assert.Equal("Read IOA • TI-102", Button("CommandDockReadButton").Attribute("Content")?.Value);
        Assert.Equal("CommandDock_Gi_Click", Button("CommandDockGiButton").Attribute("Click")?.Value);
        Assert.Equal("CommandDock_ClockSync_Click", Button("CommandDockClockSyncButton").Attribute("Click")?.Value);
        Assert.Equal("CommandDock_Read_Click", Button("CommandDockReadButton").Attribute("Click")?.Value);
        Assert.DoesNotContain(buttons, b => (string?)b.Attribute("Click") == "Clear_Click");

        XElement Button(string name) => buttons.Single(b => (string?)b.Attribute(Xaml + "Name") == name);
    }

    [Fact]
    public void QocAndQosHaveSeparateDropDownsWithExplicitVendorExtension()
    {
        var window = XDocument.Load(Source("MainWindow.xaml"));
        var qoc = window.Descendants().Single(x =>
            (string?)x.Attribute(Xaml + "Name") == "CommandQualifierComboBox");
        var qos = window.Descendants().Single(x =>
            (string?)x.Attribute(Xaml + "Name") == "CommandSetpointQualifierComboBox");
        var custom = window.Descendants().Single(x =>
            (string?)x.Attribute(Xaml + "Name") == "CommandQualifierCustomBox");
        Assert.Equal("ComboBox", qoc.Name.LocalName);
        Assert.Equal("ComboBox", qos.Name.LocalName);
        Assert.Equal("0", (string?)qoc.Attribute("SelectedIndex"));
        Assert.Equal("0", (string?)qos.Attribute("SelectedIndex"));
        Assert.Equal("Collapsed", (string?)qos.Attribute("Visibility"));
        Assert.Equal("Collapsed", (string?)custom.Attribute("Visibility"));
        Assert.Equal(new[] { "0", "1", "2", "3", "custom" },
            qoc.Elements().Select(x => (string?)x.Attribute("Tag")).ToArray());
        Assert.Equal(new[] { "0", "custom" },
            qos.Elements().Select(x => (string?)x.Attribute("Tag")).ToArray());
        Assert.All(new[] { qoc, qos }, c =>
            Assert.Equal("CommandQualifierComboBox_SelectionChanged",
                (string?)c.Attribute("SelectionChanged")));
    }

    private static XElement Resource(XDocument doc, string element, string key)
        => doc.Descendants().Single(x => x.Name.LocalName == element &&
                                          (string?)x.Attribute(Xaml + "Key") == key);

    private static XElement Trigger(XElement template, string property)
        => template.Descendants().Single(x => x.Name.LocalName == "Trigger" &&
            (string?)x.Attribute("Property") == property &&
            (string?)x.Attribute("Value") == "True");

    private static string? TriggerSetterValue(XElement trigger, string? target, string property)
        => trigger.Elements().Single(x => x.Name.LocalName == "Setter" &&
            (string?)x.Attribute("Property") == property &&
            (string?)x.Attribute("TargetName") == target).Attribute("Value")?.Value;

    private static string? SetterValue(XElement style, string property)
        => style.Elements().Single(x => x.Name.LocalName == "Setter" &&
                                      (string?)x.Attribute("Property") == property)
            .Attribute("Value")?.Value;

    private static string Source(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null;
             dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, "src", "ARIEC60870.Desktop",
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) return full;
        }
        throw new FileNotFoundException("Cannot locate desktop XAML source for Config style acceptance.");
    }
}
