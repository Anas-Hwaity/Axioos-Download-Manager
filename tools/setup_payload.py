import hashlib
import lzma
import os
import struct
import sys
from pathlib import Path

MAGIC = b"AXSETUP1"
HEADER_SIZE = 54
LC = 3
LP = 0
PB = 2
MIN_DICTIONARY = 1 << 20
DEFAULT_MAX_DICTIONARY_MB = 128
MAX_PAYLOAD_BYTES = 0x7FFFFFC7


def dictionary_size(length, limit_mb=DEFAULT_MAX_DICTIONARY_MB):
    limit = max(MIN_DICTIONARY, int(limit_mb) << 20)
    size = MIN_DICTIONARY
    while size < length and size < limit:
        size <<= 1
    return min(size, limit)


def filters_for(x86, dictionary, fast):
    preset = 6 if fast else 9 | lzma.PRESET_EXTREME
    chain = []
    if x86:
        chain.append({"id": lzma.FILTER_X86})
    chain.append({"id": lzma.FILTER_LZMA1, "preset": preset, "dict_size": dictionary, "lc": LC, "lp": LP, "pb": PB})
    return chain


def pack_bytes(data, name, limit_mb=DEFAULT_MAX_DICTIONARY_MB, fast=False, say=None):
    if not data or len(data) > MAX_PAYLOAD_BYTES:
        raise ValueError("The installer is empty or too large for the setup program.")
    encoded_name = Path(name).name.encode("utf-8")
    if not encoded_name or len(encoded_name) > 255:
        raise ValueError("The installer needs a short file name.")
    dictionary = dictionary_size(len(data), limit_mb)
    best = None
    for x86 in ((True,) if fast else (True, False)):
        packed = lzma.compress(data, format=lzma.FORMAT_RAW, filters=filters_for(x86, dictionary, fast))
        if say is not None:
            say(f"SETUP EXE: {'with' if x86 else 'without'} the x86 filter the installer packs to {len(packed) / 1048576:.2f} MB")
        if best is None or len(packed) < len(best[1]):
            best = (x86, packed)
    x86, packed = best
    header = MAGIC + bytes([1 if x86 else 0, LC, LP, PB]) + struct.pack("<q", len(data)) + hashlib.sha256(data).digest() + struct.pack("<H", len(encoded_name))
    if len(header) != HEADER_SIZE:
        raise AssertionError("setup payload header size changed")
    return header + encoded_name + packed


def unpack_bytes(payload):
    if len(payload) < HEADER_SIZE or payload[:8] != MAGIC:
        raise ValueError("not a setup payload")
    x86 = bool(payload[8] & 1)
    lc, lp, pb = payload[9], payload[10], payload[11]
    size = struct.unpack_from("<q", payload, 12)[0]
    digest = payload[20:52]
    name_length = struct.unpack_from("<H", payload, 52)[0]
    name = payload[HEADER_SIZE:HEADER_SIZE + name_length].decode("utf-8")
    chain = []
    if x86:
        chain.append({"id": lzma.FILTER_X86})
    chain.append({"id": lzma.FILTER_LZMA1, "dict_size": dictionary_size(size, 1536), "lc": lc, "lp": lp, "pb": pb})
    data = lzma.LZMADecompressor(format=lzma.FORMAT_RAW, filters=chain).decompress(payload[HEADER_SIZE + name_length:], max_length=size)
    if len(data) != size or hashlib.sha256(data).digest() != digest:
        raise ValueError("setup payload failed its integrity check")
    return name, data


def pack_file(source, target, say=None):
    source = Path(source)
    limit = os.environ.get("AXIOOS_SETUP_DICT_MB", str(DEFAULT_MAX_DICTIONARY_MB))
    fast = os.environ.get("AXIOOS_FAST_SETUP") == "1"
    payload = pack_bytes(source.read_bytes(), source.name, int(limit), fast, say)
    Path(target).write_bytes(payload)
    return len(payload)


def main(arguments):
    if len(arguments) != 3:
        print("usage: setup_payload.py <installer> <payload>")
        return 2
    size = pack_file(arguments[1], arguments[2], print)
    print(f"{arguments[2]}  {size} bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
