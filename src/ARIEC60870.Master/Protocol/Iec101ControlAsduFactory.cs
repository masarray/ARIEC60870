// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Master.Protocol;

/// <summary>One IEC-101 control encoder shared by single and A/B physical channels.</summary>
public static class Iec101ControlAsduFactory
{
    /// <summary>Reject contradictory typed intent, never infer SBO from button text.</summary>
    public static bool IsIntentConsistent(Iec60870ControlCommandRequest request)
        => request.OperationMode switch
        {
            Iec60870ControlOperationMode.Select => request.SelectBeforeOperate,
            Iec60870ControlOperationMode.ExecuteSelected => !request.SelectBeforeOperate,
            _ => true // Preserve compatibility with pre-existing direct/SELECT callers.
        };

    public static byte[] Build(Iec60870ControlCommandRequest request, Iec103MasterSettings settings)
        => request.Kind switch
        {
            Iec60870ControlCommandKind.GeneralInterrogation => Iec10xAsduBuilder.GeneralInterrogation(settings),
            Iec60870ControlCommandKind.ClockSync => Iec10xAsduBuilder.ClockSynchronization(settings, DateTime.Now),
            Iec60870ControlCommandKind.Read => Iec10xAsduBuilder.ReadCommand(settings, request.InformationObjectAddress),
            Iec60870ControlCommandKind.SingleCommand => Iec10xAsduBuilder.SingleCommand(settings, request.InformationObjectAddress, request.Value != 0, request.SelectBeforeOperate, request.Qualifier),
            Iec60870ControlCommandKind.DoubleCommand => Iec10xAsduBuilder.DoubleCommand(settings, request.InformationObjectAddress, request.Value, request.SelectBeforeOperate, request.Qualifier),
            Iec60870ControlCommandKind.RegulatingStepCommand => Iec10xAsduBuilder.RegulatingStepCommand(settings, request.InformationObjectAddress, request.Value, request.SelectBeforeOperate, request.Qualifier),
            Iec60870ControlCommandKind.SetpointNormalizedCommand => Iec10xAsduBuilder.SetpointNormalizedCommand(settings, request.InformationObjectAddress, request.NumericValue, request.SelectBeforeOperate, request.Qualifier),
            _ => throw new InvalidOperationException("Unsupported IEC-101 control command kind: " + request.Kind)
        };
}
