"""WOFF 1.0 → TTF/OTF: распаковывает таблицы (zlib) и собирает обычный SFNT.
Запуск: python tools/woff2ttf.py file.woff [file2.woff …] — рядом появятся .ttf"""
import struct
import sys
import zlib


def convert(src: str) -> str:
    data = open(src, "rb").read()
    sig, flavor, _length, num_tables = struct.unpack(">4sIIH", data[:14])
    if sig != b"wOFF":
        raise ValueError(f"{src}: не WOFF 1.0")
    tables = []
    for i in range(num_tables):
        tag, off, comp, orig, checksum = struct.unpack(">4sIIII", data[44 + i * 20: 64 + i * 20])
        raw = data[off: off + comp]
        if comp != orig:
            raw = zlib.decompress(raw)
        tables.append((tag, checksum, raw))
    tables.sort(key=lambda t: t[0])

    entry_selector = max(i for i in range(16) if (1 << i) <= num_tables)
    search_range = (1 << entry_selector) * 16
    out = bytearray(struct.pack(">IHHHH", flavor, num_tables, search_range, entry_selector, num_tables * 16 - search_range))
    offset = 12 + num_tables * 16
    body = bytearray()
    for tag, checksum, raw in tables:
        out += struct.pack(">4sIII", tag, checksum, offset + len(body), len(raw))
        body += raw + b"\0" * (-len(raw) % 4)
    dst = src.rsplit(".", 1)[0] + ".ttf"
    open(dst, "wb").write(bytes(out + body))
    return dst


if __name__ == "__main__":
    for f in sys.argv[1:]:
        print(convert(f))
