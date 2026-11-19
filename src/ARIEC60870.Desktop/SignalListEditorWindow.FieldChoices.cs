// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ARIEC60870.Desktop;

public sealed partial class SignalListEditorWindow
{
    // The selection text is UI guidance only. The persisted mapping value is
    // always the exact Value token (e.g. "9", "M_ME_NA_1", or "MonitorOnly").
    // These are common IEC-101/104 choices, NOT an engine capability claim,
    // RTU autodetection, or permission to transmit commands.
    private sealed record SignalFieldChoice(string Value, string Label, string Detail);

    private static readonly IReadOnlyDictionary<string, SignalFieldChoice[]> FieldChoices =
        new Dictionary<string, SignalFieldChoice[]>(StringComparer.Ordinal)
        {
            [nameof(SignalListEditorRow.TypeId)] =
            [
                new("1", "TI 1 · Single-point", "M_SP_NA_1 · status"),
                new("3", "TI 3 · Double-point", "M_DP_NA_1 · breaker status"),
                new("5", "TI 5 · Step position", "M_ST_NA_1 · tap position"),
                new("9", "TI 9 · Normalized value", "M_ME_NA_1 · measured"),
                new("11", "TI 11 · Scaled value", "M_ME_NB_1 · signed integer"),
                new("13", "TI 13 · Short floating-point", "M_ME_NC_1 · IEEE 754"),
                new("15", "TI 15 · Integrated totals", "M_IT_NA_1 · counter"),
                new("21", "TI 21 · Normalized, no quality", "M_ME_ND_1 · measured"),
                new("30", "TI 30 · Timed single-point", "M_SP_TB_1 · CP56Time2a"),
                new("31", "TI 31 · Timed double-point", "M_DP_TB_1 · CP56Time2a"),
                new("32", "TI 32 · Timed step position", "M_ST_TB_1 · CP56Time2a"),
                new("33", "TI 33 · Timed bitstring", "M_BO_TB_1 · CP56Time2a"),
                new("34", "TI 34 · Timed normalized", "M_ME_TD_1 · CP56Time2a"),
                new("35", "TI 35 · Timed scaled", "M_ME_TE_1 · CP56Time2a"),
                new("36", "TI 36 · Timed floating-point", "M_ME_TF_1 · CP56Time2a"),
                new("37", "TI 37 · Timed counter", "M_IT_TB_1 · CP56Time2a"),
                new("45", "TI 45 · Single command", "C_SC_NA_1 · control"),
                new("46", "TI 46 · Double command", "C_DC_NA_1 · control"),
                new("47", "TI 47 · Regulating step", "C_RC_NA_1 · raise/lower"),
                new("48", "TI 48 · Normalized setpoint", "C_SE_NA_1 · control"),
                new("49", "TI 49 · Scaled setpoint", "C_SE_NB_1 · control"),
                new("50", "TI 50 · Floating setpoint", "C_SE_NC_1 · control"),
                new("51", "TI 51 · Bitstring command", "C_BO_NA_1 · control"),
                new("58", "TI 58 · Timed single command", "C_SC_TA_1 · CP56Time2a"),
                new("59", "TI 59 · Timed double command", "C_DC_TA_1 · CP56Time2a"),
                new("60", "TI 60 · Timed regulating step", "C_RC_TA_1 · CP56Time2a"),
                new("61", "TI 61 · Timed normalized setpoint", "C_SE_TA_1 · CP56Time2a"),
                new("62", "TI 62 · Timed scaled setpoint", "C_SE_TB_1 · CP56Time2a"),
                new("63", "TI 63 · Timed floating setpoint", "C_SE_TC_1 · CP56Time2a"),
                new("64", "TI 64 · Timed bitstring command", "C_BO_TA_1 · CP56Time2a"),
                new("70", "TI 70 · End of initialization", "M_EI_NA_1 · system"),
                new("100", "TI 100 · Interrogation", "C_IC_NA_1 · system"),
                new("101", "TI 101 · Counter interrogation", "C_CI_NA_1 · system"),
                new("102", "TI 102 · Read command", "C_RD_NA_1 · system"),
                new("103", "TI 103 · Clock synchronization", "C_CS_NA_1 · system"),
                new("104", "TI 104 · Test command", "C_TS_NA_1 · system"),
                new("105", "TI 105 · Reset process", "C_RP_NA_1 · system"),
                new("106", "TI 106 · Delay acquisition", "C_CD_NA_1 · system")
            ],
            [nameof(SignalListEditorRow.SignalType)] =
            [
                new("M_SP_NA_1", "Single-point status", "TI 1"),
                new("M_DP_NA_1", "Double-point status", "TI 3"),
                new("M_ST_NA_1", "Step position", "TI 5"),
                new("M_ME_NA_1", "Normalized measurement", "TI 9"),
                new("M_ME_NB_1", "Scaled measurement", "TI 11"),
                new("M_ME_NC_1", "Floating measurement", "TI 13"),
                new("M_IT_NA_1", "Integrated totals", "TI 15"),
                new("M_ME_ND_1", "Normalized, no quality", "TI 21"),
                new("M_SP_TB_1", "Timed single-point", "TI 30"),
                new("M_DP_TB_1", "Timed double-point", "TI 31"),
                new("M_ST_TB_1", "Timed step position", "TI 32"),
                new("M_BO_TB_1", "Timed bitstring", "TI 33"),
                new("M_ME_TD_1", "Timed normalized", "TI 34"),
                new("M_ME_TE_1", "Timed scaled", "TI 35"),
                new("M_ME_TF_1", "Timed floating", "TI 36"),
                new("M_IT_TB_1", "Timed counter", "TI 37"),
                new("C_SC_NA_1", "Single command", "TI 45"),
                new("C_DC_NA_1", "Double command", "TI 46"),
                new("C_RC_NA_1", "Regulating step", "TI 47"),
                new("C_SE_NA_1", "Normalized setpoint", "TI 48"),
                new("C_SE_NB_1", "Scaled setpoint", "TI 49"),
                new("C_SE_NC_1", "Floating setpoint", "TI 50"),
                new("C_BO_NA_1", "Bitstring command", "TI 51"),
                new("C_SC_TA_1", "Timed single command", "TI 58"),
                new("C_DC_TA_1", "Timed double command", "TI 59"),
                new("C_RC_TA_1", "Timed regulating step", "TI 60"),
                new("C_SE_TA_1", "Timed normalized setpoint", "TI 61"),
                new("C_SE_TB_1", "Timed scaled setpoint", "TI 62"),
                new("C_SE_TC_1", "Timed floating setpoint", "TI 63"),
                new("C_BO_TA_1", "Timed bitstring command", "TI 64"),
                new("M_EI_NA_1", "Initialization", "TI 70"),
                new("C_IC_NA_1", "Interrogation", "TI 100"),
                new("C_CI_NA_1", "Counter interrogation", "TI 101"),
                new("C_RD_NA_1", "Read", "TI 102"),
                new("C_CS_NA_1", "Clock sync", "TI 103")
            ],
            [nameof(SignalListEditorRow.CommandPolicy)] =
            [
                new("MonitorOnly", "Monitor only", "Never used as a control"),
                new("SingleCommandRemoteOnly", "Single command · remote only", "Project command policy"),
                new("DoubleCommandRemoteOnly", "Double command · remote only", "Project command policy"),
                new("RegulatingStepRemoteOnly", "Regulating step · remote only", "Project command policy"),
                new("SetpointNormalizedRemoteOnly", "Normalized setpoint · remote only", "Project command policy"),
                new("DoubleCommand", "Double command · legacy policy", "Review before live control"),
                new("SingleCommand", "Single command · legacy policy", "Review before live control"),
                new("RegulatingStep", "Regulating step · legacy policy", "Review before live control"),
                new("SetpointNormalized", "Normalized setpoint · legacy policy", "Review before live control")
            ],
            [nameof(SignalListEditorRow.ExpectedClass)] =
            [
                new("", "Not specified", "Leave class unassigned"),
                new("1", "Class 1", "Event / high-priority polling"),
                new("2", "Class 2", "Background polling")
            ],
            [nameof(SignalListEditorRow.ExpectedCot)] =
            [
                new("", "Not specified", "No expected COT restriction"),
                new("1", "Cyclic / periodic", "COT 1"),
                new("2", "Background scan", "COT 2"),
                new("3", "Spontaneous", "COT 3"),
                new("4", "Initialized", "COT 4"),
                new("5", "Request", "COT 5"),
                new("6", "Activation", "COT 6"),
                new("7", "Activation confirmation", "COT 7"),
                new("8", "Deactivation", "COT 8"),
                new("9", "Deactivation confirmation", "COT 9"),
                new("10", "Activation termination", "COT 10"),
                new("11", "Return information: remote", "COT 11"),
                new("12", "Return information: local", "COT 12"),
                new("13", "File transfer", "COT 13"),
                new("20", "General interrogation response", "COT 20"),
                new("21", "Group 1 interrogation", "COT 21"),
                new("22", "Group 2 interrogation", "COT 22"),
                new("23", "Group 3 interrogation", "COT 23"),
                new("24", "Group 4 interrogation", "COT 24"),
                new("25", "Group 5 interrogation", "COT 25"),
                new("26", "Group 6 interrogation", "COT 26"),
                new("27", "Group 7 interrogation", "COT 27"),
                new("28", "Group 8 interrogation", "COT 28"),
                new("29", "Group 9 interrogation", "COT 29"),
                new("30", "Group 10 interrogation", "COT 30"),
                new("31", "Group 11 interrogation", "COT 31"),
                new("32", "Group 12 interrogation", "COT 32"),
                new("33", "Group 13 interrogation", "COT 33"),
                new("34", "Group 14 interrogation", "COT 34"),
                new("35", "Group 15 interrogation", "COT 35"),
                new("36", "Group 16 interrogation", "COT 36"),
                new("44", "Unknown Type ID", "COT 44"),
                new("45", "Unknown COT", "COT 45"),
                new("46", "Unknown common address", "COT 46"),
                new("47", "Unknown IOA", "COT 47")
            ],
            [nameof(SignalListEditorRow.TestCategory)] =
            [
                new("TM", "Telemetering", "Measured analog values"),
                new("TSS", "Telesignal single", "Single-point indication"),
                new("TSD", "Telesignal double", "Double-point indication"),
                new("TC", "Tap changer", "Step position feedback"),
                new("RCD", "Remote digital control", "Control command point"),
                new("RCA", "Remote analog setpoint", "Setpoint command point"),
                new("MLK", "Link monitoring", "Communication health"),
                new("SOE", "Sequence of events", "Chronological events")
            ],
            [nameof(SignalListEditorRow.Unit)] =
            [
                new("", "No unit", "Digital or uncalibrated point"),
                new("counts", "Raw counts", "Do not assume engineering scaling"),
                new("MW", "Megawatts", "Active power"),
                new("MVar", "Megavars", "Reactive power"),
                new("kW", "Kilowatts", "Active power"),
                new("kVar", "Kilovars", "Reactive power"),
                new("kV", "Kilovolts", "Voltage"),
                new("V", "Volts", "Voltage"),
                new("kA", "Kiloamperes", "Current"),
                new("A", "Amperes", "Current"),
                new("Hz", "Hertz", "Frequency"),
                new("%", "Percent", "Percentage"),
                new("°C", "Celsius", "Temperature"),
                new("tap", "Tap position", "Step number"),
                new("MWh", "Megawatt-hours", "Energy"),
                new("kWh", "Kilowatt-hours", "Energy")
            ],
            [nameof(SignalListEditorRow.ScalingMode)] =
            [
                new("Factor", "Factor", "Scale, offset and CT/VT ratios"),
                new("TwoPoint", "Two-point", "X0/X100 mapped to Y0/Y100")
            ]
        };

    private static bool TryGetFieldChoices(string path, out SignalFieldChoice[] choices) =>
        FieldChoices.TryGetValue(path, out choices!);

    // Keep the actual WPF ComboBox editable for project-specific Type IDs,
    // policies, units or legacy tokens not represented in this short catalog.
    // TextSearch.TextPath ensures selection writes ONLY the original code to
    // SignalListEditorRow; friendly names never enter the JSON mapping.
    private ComboBox CreateSignalChoiceCombo(string path, SignalFieldChoice[] choices)
    {
        var combo = new ComboBox
        {
            Name = "SignalMapChoice_" + path,
            IsEditable = true,
            IsTextSearchEnabled = true,
            StaysOpenOnEdit = true,
            MaxDropDownHeight = 294,
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 12.5,
            Background = Brushes.White,
            BorderBrush = BorderBrushSoft,
            Foreground = TextMainBrush,
            ToolTip = "Choose a value or type a custom code"
        };
        TextSearch.SetTextPath(combo, nameof(SignalFieldChoice.Value));
        foreach (var choice in choices)
        {
            var caption = new TextBlock
            {
                Text = choice.Label,
                FontSize = 12,
                Foreground = TextMainBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var sub = new TextBlock
            {
                Text = choice.Detail,
                FontSize = 10.5,
                Foreground = TextMutedBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0)
            };
            var stack = new StackPanel { Margin = new Thickness(2, 3, 4, 3) };
            stack.Children.Add(caption);
            stack.Children.Add(sub);
            var item = new ComboBoxItem
            {
                Content = stack,
                ToolTip = choice.Value + " · " + choice.Label + " — " + choice.Detail
            };
            TextSearch.SetText(item, choice.Value);
            combo.Items.Add(item);
        }
        combo.SetBinding(ComboBox.TextProperty, new Binding(path)
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        return combo;
    }
}
