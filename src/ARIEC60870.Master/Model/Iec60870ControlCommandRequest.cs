// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Model;

public enum Iec60870ControlCommandKind
{
    GeneralInterrogation,
    ClockSync,
    Read,
    SingleCommand,
    DoubleCommand,
    RegulatingStepCommand,
    SetpointNormalizedCommand
}

public sealed class Iec60870ControlCommandRequest
{
    public Iec60870ControlCommandKind Kind { get; init; }
    public int? CommonAddress { get; init; }
    public int InformationObjectAddress { get; init; }
    public int Value { get; init; }
    public double NumericValue { get; init; }
    public bool SelectBeforeOperate { get; init; }
    public int Qualifier { get; init; }
    public DateTime RequestedUtc { get; init; } = DateTime.UtcNow;
    public string OperatorNote { get; init; } = string.Empty;

    public string AddressPrefix => CommonAddress.HasValue ? $"CA {CommonAddress.Value}, " : string.Empty;

    public string Summary => Kind switch
    {
        Iec60870ControlCommandKind.GeneralInterrogation => "General Interrogation",
        Iec60870ControlCommandKind.ClockSync => "Clock synchronization",
        Iec60870ControlCommandKind.Read => $"Read {AddressPrefix}IOA {InformationObjectAddress}",
        Iec60870ControlCommandKind.SingleCommand => $"Single command {AddressPrefix}IOA {InformationObjectAddress}={(Value != 0 ? "CLOSE/ON" : "OPEN/OFF")}{(SelectBeforeOperate ? " SELECT" : " OPERATE")}",
        Iec60870ControlCommandKind.DoubleCommand => $"Double command {AddressPrefix}IOA {InformationObjectAddress}={(Value == 2 ? "CLOSE" : Value == 1 ? "OPEN" : "DCS" + Value)}{(SelectBeforeOperate ? " SELECT" : " OPERATE")}",
        Iec60870ControlCommandKind.RegulatingStepCommand => $"Regulating step {AddressPrefix}IOA {InformationObjectAddress}={(Value == 2 ? "RAISE" : Value == 1 ? "LOWER" : "STOP")}{(SelectBeforeOperate ? " SELECT" : " OPERATE")}",
        Iec60870ControlCommandKind.SetpointNormalizedCommand => $"Setpoint normalized {AddressPrefix}IOA {InformationObjectAddress}={NumericValue:0.#####}{(SelectBeforeOperate ? " SELECT" : " OPERATE")}",
        _ => Kind.ToString()
    };
}

public interface IProtocolControlCommandSession
{
    bool SupportsRuntimeControlCommands { get; }
    void QueueControlCommand(Iec60870ControlCommandRequest request);
}
