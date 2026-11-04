using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Univer.Desktop.Core
{
    // Read-only reader for OLE compound files (the Word 97-2003 .doc container).
    // Every sector chain is bounded by the file itself, so damaged or looping tables stop with InvalidDataException.
    internal sealed class CompoundFile
    {
        private const uint EndOfChain = 0xFFFFFFFE, FreeSector = 0xFFFFFFFF;
        private const int MiniSectorSize = 64;
        private readonly byte[] data;
        private readonly int sectorSize;
        private readonly long miniCutoff;
        private readonly uint[] fat, miniFat;
        private readonly byte[] miniStream;
        private readonly List<Entry> entries = new List<Entry>();
        private struct Entry { public string Name; public int Type; public uint Start; public long Size; }
        public CompoundFile(byte[] data)
        {
            this.data = data;
            if (data.Length < 512 || U32(data, 0) != 0xE011CFD0 || U32(data, 4) != 0xE11AB1A1) throw new InvalidDataException("Not a compound file");
            var shift = U16(data, 0x1E);
            if (shift != 9 && shift != 12 || U16(data, 0x20) != 6) throw new InvalidDataException("Unsupported compound file");
            sectorSize = 1 << shift;
            var sectors = (data.Length + sectorSize - 1) / sectorSize;
            var fatCount = U32(data, 0x2C); miniCutoff = U32(data, 0x38);
            if (fatCount > sectors) throw new InvalidDataException("Damaged compound file");
            var fatSectors = new List<uint>();
            for (var i = 0; i < 109 && fatSectors.Count < fatCount; i++) { var sector = U32(data, 0x4C + i * 4); if (sector != FreeSector) fatSectors.Add(sector); }
            var next = U32(data, 0x44); var per = sectorSize / 4 - 1;
            for (var guard = 0; fatSectors.Count < fatCount && next != EndOfChain && next != FreeSector; guard++)
            {
                if (guard > sectors) throw new InvalidDataException("Damaged compound file");
                var offset = Offset(next);
                for (var j = 0; j < per && fatSectors.Count < fatCount; j++) fatSectors.Add(U32(data, offset + j * 4));
                next = U32(data, offset + per * 4);
            }
            fat = new uint[fatSectors.Count * (sectorSize / 4)];
            for (var i = 0; i < fatSectors.Count; i++)
            {
                var offset = Offset(fatSectors[i]);
                for (var j = 0; j < sectorSize / 4; j++) fat[i * (sectorSize / 4) + j] = U32(data, offset + j * 4);
            }
            var directory = Chain(U32(data, 0x30), long.MaxValue, fat, sectorSize, data, Offset);
            for (var offset = 0; offset + 128 <= directory.Length; offset += 128)
            {
                var length = Math.Min(64, (int)U16(directory, offset + 64));
                var name = length >= 2 ? Encoding.Unicode.GetString(directory, offset, length - 2) : "";
                var size = sectorSize == 512 ? U32(directory, offset + 120) : (long)BitConverter.ToUInt64(directory, offset + 120);
                entries.Add(new Entry { Name = name, Type = directory[offset + 66], Start = U32(directory, offset + 116), Size = size });
            }
            if (entries.Count == 0 || entries[0].Type != 5) throw new InvalidDataException("Damaged compound file");
            var miniFatStart = U32(data, 0x3C);
            var miniFatBytes = miniFatStart == EndOfChain || miniFatStart == FreeSector ? new byte[0] : Chain(miniFatStart, long.MaxValue, fat, sectorSize, data, Offset);
            miniFat = new uint[miniFatBytes.Length / 4];
            for (var i = 0; i < miniFat.Length; i++) miniFat[i] = U32(miniFatBytes, i * 4);
            var root = entries[0];
            miniStream = root.Size == 0 || root.Start == EndOfChain ? new byte[0] : Chain(root.Start, root.Size, fat, sectorSize, data, Offset);
        }
        // Returns the named stream, or null when the file has none.
        public byte[] Stream(string name)
        {
            foreach (var entry in entries)
            {
                if (entry.Type != 2 || !string.Equals(entry.Name, name, StringComparison.Ordinal)) continue;
                if (entry.Size == 0) return new byte[0];
                if (entry.Size < miniCutoff) return Chain(entry.Start, entry.Size, miniFat, MiniSectorSize, miniStream, s => checked((int)s * MiniSectorSize));
                return Chain(entry.Start, entry.Size, fat, sectorSize, data, Offset);
            }
            return null;
        }
        private int Offset(uint sector)
        {
            var offset = ((long)sector + 1) * sectorSize;
            if (offset >= data.Length) throw new InvalidDataException("Damaged compound file");
            return (int)offset;
        }
        private static byte[] Chain(uint start, long size, uint[] table, int unit, byte[] source, Func<uint, int> offsetOf)
        {
            if (size != long.MaxValue && (size < 0 || size > source.Length)) throw new InvalidDataException("Damaged compound file");
            var result = new MemoryStream();
            var sector = start;
            for (var guard = 0; sector != EndOfChain && result.Length < size; guard++)
            {
                if (guard > table.Length || sector >= table.Length) throw new InvalidDataException("Damaged compound file");
                var offset = offsetOf(sector);
                if (offset < 0 || offset >= source.Length) throw new InvalidDataException("Damaged compound file");
                var count = (int)Math.Min(Math.Min(unit, source.Length - offset), size - result.Length);
                // A valid chain visits each sector once, so it can never be longer than its source. A looping table
                // in a damaged file would otherwise grow up to the guard (many times the file) before stopping.
                if (result.Length + count > source.Length) throw new InvalidDataException("Damaged compound file");
                result.Write(source, offset, count);
                sector = table[sector];
            }
            if (size != long.MaxValue && result.Length < size) throw new InvalidDataException("Truncated compound file");
            return result.ToArray();
        }
        internal static ushort U16(byte[] buffer, int offset)
        {
            if (offset < 0 || offset + 2 > buffer.Length) throw new InvalidDataException("Damaged document");
            return BitConverter.ToUInt16(buffer, offset);
        }
        internal static uint U32(byte[] buffer, int offset)
        {
            if (offset < 0 || offset + 4 > buffer.Length) throw new InvalidDataException("Damaged document");
            return BitConverter.ToUInt32(buffer, offset);
        }
    }
}
