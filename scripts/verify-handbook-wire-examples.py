#!/usr/bin/env python3
# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
"""Offline and dependency-free validation of every canonical IEC trace-lab example.

Only the handbook JSON contains example bytes; this checker independently computes
link/APCI integrity and expected decoded fields without a second reference fixture.
It validates deliberate negative cases as well as positive examples.
Not a conformance certification of a real RTU.
"""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CANONICAL = ROOT / "site" / "learning" / "iec60870-handbook.json"
MIRROR = ROOT / "docs" / "learning" / "iec60870-handbook.json"
U_COMMANDS = {
    0x07: "STARTDT act",
    0x0B: "STARTDT con",
    0x13: "STOPDT act",
    0x23: "STOPDT con",
    0x43: "TESTFR act",
    0x83: "TESTFR con",
}


def fail(name: str, detail: str) -> None:
    raise ValueError(f"{name}: {detail}")


def checked(name: str, expected: dict, actual: dict) -> None:
    for field, value in expected.items():
        if field == "failure":
            continue
        if actual.get(field) != value:
            fail(name, f"{field}: expected {value!r}, calculated {actual.get(field)!r}")


def decode_101_fixed(frame: bytes) -> tuple[bool, dict, str | None]:
    if len(frame) != 5 or frame[0] != 0x10 or frame[-1] != 0x16:
        return False, {}, "framing"
    data = {"control": frame[1], "address": frame[2],
            "function": frame[1] & 0x0F, "primary": bool(frame[1] & 0x40)}
    if (frame[1] + frame[2]) & 0xFF != frame[3]:
        return False, data, "checksum"
    return True, data, None


def decode_104(frame: bytes, kind: str) -> tuple[bool, dict, str | None]:
    if len(frame) < 6 or frame[0] != 0x68:
        return False, {}, "framing"
    declared = frame[1]
    if declared != len(frame) - 2 or not (4 <= declared <= 253):
        return False, {}, "length"
    ctrl = frame[2:6]
    if kind == "iec104-u":
        if declared != 4 or ctrl[0] not in U_COMMANDS:
            return False, {}, "u-function"
        if ctrl[1:] != b"\x00\x00\x00":
            return False, {}, "reserved-control"
        return True, {"command": U_COMMANDS[ctrl[0]]}, None
    if kind == "iec104-s":
        if declared != 4 or ctrl[0] != 0x01 or ctrl[1] or ctrl[2] & 1:
            return False, {}, "s-control"
        return True, {"nr": int.from_bytes(ctrl[2:4], "little") >> 1}, None
    if kind == "iec104-i":
        if ctrl[0] & 1 or ctrl[2] & 1 or declared < 4 + 10:
            return False, {}, "i-control"
        if len(frame) < 16:
            return False, {}, "asdu-short"
        values = {
            "ns": int.from_bytes(ctrl[0:2], "little") >> 1,
            "nr": int.from_bytes(ctrl[2:4], "little") >> 1,
            "typeId": frame[6],
            "cot": frame[8] & 0x3F,
            "ca": int.from_bytes(frame[10:12], "little"),
            "ioa": int.from_bytes(frame[12:15], "little"),
        }
        return True, values, None
    return False, {}, "unsupported-kind"


def validate() -> tuple[int, int, int]:
    source = CANONICAL.read_text(encoding="utf-8")
    if source != MIRROR.read_text(encoding="utf-8"):
        fail("single-source", "docs mirror differs from site canonical handbook")
    book = json.loads(source)
    if book["schemaVersion"] != 1:
        fail("schema", "unknown handbook version")
    chapters = book["chapters"]
    if len(chapters) < 16:
        fail("chapters", "R3 book is incomplete")
    keys: set[str] = set()
    sections: set[str] = set()
    lab_count = wire_count = negatives = 0

    for chapter_index, chapter in enumerate(chapters, start=1):
        key = chapter["key"]
        if key in keys or chapter["number"] != f"{chapter_index:02}":
            fail(key, "non-unique chapter key or out-of-order number")
        keys.add(key)
        for section_index, section in enumerate(chapter["sections"], start=1):
            name = f"{key}/{section_index}"
            if section["id"] in sections or section["number"] != f"{chapter_index}.{section_index}":
                fail(name, "non-unique section id or invalid numbering")
            sections.add(section["id"])
            lab = section.get("lab")
            if lab is None:
                continue
            lab_count += 1
            for field in ("scenario", "question", "answer"):
                if not lab[field + "En"].strip() or not lab[field + "Id"].strip():
                    fail(name, f"incomplete bilingual {field}")
            if not lab["stepsEn"] or len(lab["stepsEn"]) != len(lab["stepsId"]):
                fail(name, "trace steps missing or languages out of sync")
            expected = lab["expectedFields"]
            kind = lab["wireKind"]
            hex_text = lab["wireHex"]
            if kind == "conceptual":
                if hex_text is not None or not lab["expectedValid"]:
                    fail(name, "conceptual cases cannot assert exact wire integrity")
                continue
            if not isinstance(hex_text, str):
                fail(name, "wire case missing hexadecimal bytes")
            tokens = hex_text.split()
            if not tokens or any(len(part) != 2 or
                any(ch not in "0123456789ABCDEF" for ch in part) for part in tokens):
                fail(name, "frame must be uppercase two-digit hex bytes with spaces")
            frame = bytes.fromhex(hex_text)
            wire_count += 1
            if kind == "iec101-fixed":
                valid, fields, reason = decode_101_fixed(frame)
            elif kind == "iec101-single":
                valid = frame == b"\xE5"
                fields = {"shortKind": "context-dependent"} if valid else {}
                reason = None if valid else "unsupported-short-code"
            elif kind.startswith("iec104-"):
                valid, fields, reason = decode_104(frame, kind)
            else:
                fail(name, "unknown lab wire kind " + kind)

            if valid != lab["expectedValid"]:
                fail(name, f"expectedValid={lab['expectedValid']} computed={valid} ({reason})")
            if not valid:
                negatives += 1
                if reason != expected.get("failure"):
                    fail(name, f"expected failure {expected.get('failure')}, actual {reason}")
            checked(name, expected, fields)

    if lab_count < 20 or wire_count < 14 or negatives < 2:
        fail("coverage", "requires 20 learning labs, >=14 checked wire frames and >=2 negatives")
    print(f"PASS handbook R3: {len(chapters)} chapters, {len(sections)} sections, "
          f"{lab_count} guided labs, {wire_count} checked frames ({negatives} deliberate invalid)")
    return lab_count, wire_count, negatives


if __name__ == "__main__":
    validate()
