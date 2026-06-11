// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;

namespace ARIEC60870.Core.Parsing;

public static class LinkControlDecoder
{
    private const byte PrmMask = 0x40;
    private const byte FcbOrAcdMask = 0x20;
    private const byte FcvOrDfcMask = 0x10;
    private const byte FunctionMask = 0x0F;

    public static LinkControlInfo Decode(byte control)
    {
        var prm = (control & PrmMask) != 0;
        var functionCode = control & FunctionMask;

        if (prm)
        {
            return new LinkControlInfo
            {
                Raw = control,
                Prm = true,
                Fcb = (control & FcbOrAcdMask) != 0,
                Fcv = (control & FcvOrDfcMask) != 0,
                FunctionCode = functionCode,
                FunctionName = PrimaryFunctionName(functionCode)
            };
        }

        return new LinkControlInfo
        {
            Raw = control,
            Prm = false,
            Acd = (control & FcbOrAcdMask) != 0,
            Dfc = (control & FcvOrDfcMask) != 0,
            FunctionCode = functionCode,
            FunctionName = SecondaryFunctionName(functionCode)
        };
    }

    private static string PrimaryFunctionName(int functionCode) => functionCode switch
    {
        0 => "Reset remote link",
        1 => "Reset user process",
        3 => "Send user data, confirmed",
        4 => "Send user data, no reply",
        7 => "Reset FCB",
        9 => "Request link status",
        10 => "Request Class 1 data",
        11 => "Request Class 2 data",
        _ => $"Primary function {functionCode}"
    };

    private static string SecondaryFunctionName(int functionCode) => functionCode switch
    {
        0 => "ACK",
        1 => "NACK",
        8 => "User data",
        9 => "No requested data available",
        11 => "Link status / access demand",
        14 => "Link service not functioning",
        15 => "Link service not implemented",
        _ => $"Secondary function {functionCode}"
    };
}
