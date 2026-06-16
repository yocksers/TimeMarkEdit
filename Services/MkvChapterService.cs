using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MediaBrowser.Model.Logging;

namespace TimeMarkEdit.Services
{
    public class MkvChapterService
    {
        private readonly ILogger _logger;

        private const uint ID_EBML              = 0x1A45DFA3;
        private const uint ID_SEGMENT           = 0x18538067;
        private const uint ID_SEEKHEAD          = 0x114D9B74;
        private const uint ID_SEEK              = 0x4DBB;
        private const uint ID_SEEKID            = 0x53AB;
        private const uint ID_SEEKPOSITION      = 0x53AC;
        private const uint ID_CHAPTERS          = 0x1043A770;
        private const uint ID_EDITIONENTRY      = 0x45B9;
        private const uint ID_CHAPTERATOM       = 0xB6;
        private const uint ID_CHAPTERTIMESTART  = 0x91;
        private const uint ID_CHAPTERFLAGHIDDEN = 0x98;
        private const uint ID_CHAPTERDISPLAY    = 0x80;
        private const uint ID_CHAPSTRING        = 0x85;

        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".mka", ".mks", ".mk3d", ".webm"
        };

        public MkvChapterService(ILogger logger)
        {
            _logger = logger;
        }

        public List<(string Name, long StartPositionTicks)> ReadChapters(string filePath)
        {
            var result = new List<(string, long)>();

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return result;

            if (!SupportedExtensions.Contains(Path.GetExtension(filePath)))
                return result;

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    if (stream.Length < 12) return result;

                    var headerId = ReadEbmlId(reader);
                    if (headerId != ID_EBML) return result;

                    var headerSize = ReadEbmlSize(reader);
                    if (headerSize != long.MaxValue && headerSize > 0)
                        stream.Seek(headerSize, SeekOrigin.Current);

                    while (stream.Position < stream.Length - 8)
                    {
                        uint elemId;
                        long elemSize;
                        try { elemId = ReadEbmlId(reader); elemSize = ReadEbmlSize(reader); }
                        catch { break; }

                        if (elemId == ID_SEGMENT)
                        {
                            ParseSegment(reader, stream, elemSize, result);
                            break;
                        }

                        if (elemSize == long.MaxValue) break;
                        stream.Seek(elemSize, SeekOrigin.Current);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"TimeMarkEdit: Could not read MKV chapters from '{Path.GetFileName(filePath)}': {ex.Message}");
            }

            return result;
        }

        private void ParseSegment(BinaryReader reader, Stream stream, long segmentSize, List<(string, long)> result)
        {
            var segmentDataStart = stream.Position;
            var segmentEnd = segmentSize == long.MaxValue ? stream.Length : segmentDataStart + segmentSize;
            const long maxHeaderScan = 16L * 1024 * 1024;
            var scanEnd = Math.Min(segmentEnd, segmentDataStart + maxHeaderScan);

            while (stream.Position < scanEnd - 8)
            {
                uint elemId;
                long elemSize;
                try { elemId = ReadEbmlId(reader); elemSize = ReadEbmlSize(reader); }
                catch { break; }

                var contentPos = stream.Position;

                if (elemId == ID_CHAPTERS)
                {
                    ParseChapters(reader, stream, elemSize, result);
                    return;
                }

                if (elemId == ID_SEEKHEAD)
                {
                    var chapAbsPos = FindChaptersInSeekHead(reader, stream, elemSize, segmentDataStart);
                    if (elemSize != long.MaxValue)
                        stream.Seek(contentPos + elemSize, SeekOrigin.Begin);

                    if (chapAbsPos > 0 && chapAbsPos < segmentEnd)
                    {
                        stream.Seek(chapAbsPos, SeekOrigin.Begin);
                        try
                        {
                            var chapId = ReadEbmlId(reader);
                            var chapSize = ReadEbmlSize(reader);
                            if (chapId == ID_CHAPTERS)
                                ParseChapters(reader, stream, chapSize, result);
                        }
                        catch { }
                        return;
                    }

                    continue;
                }

                if (elemSize == long.MaxValue) break;
                stream.Seek(contentPos + elemSize, SeekOrigin.Begin);
            }
        }

        private long FindChaptersInSeekHead(BinaryReader reader, Stream stream, long seekHeadSize, long segmentDataStart)
        {
            if (seekHeadSize == long.MaxValue || seekHeadSize <= 0) return -1;
            var end = stream.Position + seekHeadSize;

            while (stream.Position < end - 4)
            {
                uint elemId;
                long elemSize;
                try { elemId = ReadEbmlId(reader); elemSize = ReadEbmlSize(reader); }
                catch { break; }

                var contentPos = stream.Position;

                if (elemId == ID_SEEK && elemSize != long.MaxValue && elemSize > 0)
                {
                    var seekEnd = contentPos + elemSize;
                    long seekId = -1;
                    long seekPos = -1;

                    while (stream.Position < seekEnd - 2)
                    {
                        uint sId;
                        long sSize;
                        try { sId = ReadEbmlId(reader); sSize = ReadEbmlSize(reader); }
                        catch { break; }

                        var sContentPos = stream.Position;

                        if (sId == ID_SEEKID && sSize >= 1 && sSize <= 8)
                            seekId = ReadVarUInt(reader, (int)sSize);
                        else if (sId == ID_SEEKPOSITION && sSize >= 1 && sSize <= 8)
                            seekPos = ReadVarUInt(reader, (int)sSize);
                        else
                        {
                            if (sSize == long.MaxValue) break;
                            stream.Seek(sContentPos + sSize, SeekOrigin.Begin);
                        }
                    }

                    if (seekId == (long)ID_CHAPTERS && seekPos >= 0)
                        return segmentDataStart + seekPos;

                    stream.Seek(contentPos + elemSize, SeekOrigin.Begin);
                    continue;
                }

                if (elemSize == long.MaxValue) break;
                stream.Seek(contentPos + elemSize, SeekOrigin.Begin);
            }

            return -1;
        }

        private void ParseChapters(BinaryReader reader, Stream stream, long size, List<(string, long)> result)
        {
            if (size == long.MaxValue || size <= 0) return;
            var end = stream.Position + size;

            while (stream.Position < end - 2)
            {
                uint elemId;
                long elemSize;
                try { elemId = ReadEbmlId(reader); elemSize = ReadEbmlSize(reader); }
                catch { break; }

                var contentPos = stream.Position;

                if (elemId == ID_EDITIONENTRY)
                {
                    if (elemSize != long.MaxValue)
                        ParseEdition(reader, stream, elemSize, result);
                }
                else
                {
                    if (elemSize == long.MaxValue) break;
                    stream.Seek(contentPos + elemSize, SeekOrigin.Begin);
                    continue;
                }

                if (elemSize == long.MaxValue) break;
                stream.Seek(contentPos + elemSize, SeekOrigin.Begin);
            }
        }

        private void ParseEdition(BinaryReader reader, Stream stream, long size, List<(string, long)> result)
        {
            if (size <= 0) return;
            var end = stream.Position + size;

            while (stream.Position < end - 2)
            {
                uint elemId;
                long elemSize;
                try { elemId = ReadEbmlId(reader); elemSize = ReadEbmlSize(reader); }
                catch { break; }

                var contentPos = stream.Position;

                if (elemId == ID_CHAPTERATOM && elemSize != long.MaxValue && elemSize > 0)
                    ParseChapterAtom(reader, stream, elemSize, result);
                else
                {
                    if (elemSize == long.MaxValue) break;
                }

                if (elemSize == long.MaxValue) break;
                stream.Seek(contentPos + elemSize, SeekOrigin.Begin);
            }
        }

        private void ParseChapterAtom(BinaryReader reader, Stream stream, long size, List<(string, long)> result)
        {
            var end = stream.Position + size;
            long timeStartNs = -1;
            string name = string.Empty;
            bool hidden = false;

            while (stream.Position < end - 1)
            {
                uint elemId;
                long elemSize;
                try { elemId = ReadEbmlId(reader); elemSize = ReadEbmlSize(reader); }
                catch { break; }

                if (elemSize == long.MaxValue) break;
                var nextPos = stream.Position + elemSize;
                if (nextPos > end) break;

                if (elemId == ID_CHAPTERTIMESTART && elemSize >= 1 && elemSize <= 8)
                {
                    timeStartNs = ReadVarUInt(reader, (int)elemSize);
                }
                else if (elemId == ID_CHAPTERFLAGHIDDEN && elemSize == 1)
                {
                    hidden = reader.ReadByte() != 0;
                }
                else if (elemId == ID_CHAPTERDISPLAY)
                {
                    var displayEnd = nextPos;
                    while (stream.Position < displayEnd - 1)
                    {
                        uint dId;
                        long dSize;
                        try { dId = ReadEbmlId(reader); dSize = ReadEbmlSize(reader); }
                        catch { break; }

                        if (dSize == long.MaxValue) break;
                        var dNextPos = stream.Position + dSize;
                        if (dNextPos > displayEnd) break;

                        if (dId == ID_CHAPSTRING && dSize > 0 && string.IsNullOrEmpty(name))
                        {
                            var bytes = reader.ReadBytes((int)dSize);
                            name = Encoding.UTF8.GetString(bytes);
                        }

                        stream.Seek(dNextPos, SeekOrigin.Begin);
                    }
                }

                stream.Seek(nextPos, SeekOrigin.Begin);
            }

            if (!hidden && timeStartNs >= 0)
            {
                var ticks = timeStartNs / 100L;
                result.Add((name, ticks));
            }
        }

        private uint ReadEbmlId(BinaryReader reader)
        {
            byte b0 = reader.ReadByte();
            if ((b0 & 0x80) != 0) return b0;
            byte b1 = reader.ReadByte();
            if ((b0 & 0x40) != 0) return (uint)((b0 << 8) | b1);
            byte b2 = reader.ReadByte();
            if ((b0 & 0x20) != 0) return (uint)((b0 << 16) | (b1 << 8) | b2);
            byte b3 = reader.ReadByte();
            if ((b0 & 0x10) != 0) return ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
            throw new InvalidDataException($"Invalid EBML ID byte 0x{b0:X2}");
        }

        private long ReadEbmlSize(BinaryReader reader)
        {
            byte b0 = reader.ReadByte();
            int width;
            long firstMasked;

            if      ((b0 & 0x80) != 0) { width = 1; firstMasked = b0 & 0x7F; }
            else if ((b0 & 0x40) != 0) { width = 2; firstMasked = b0 & 0x3F; }
            else if ((b0 & 0x20) != 0) { width = 3; firstMasked = b0 & 0x1F; }
            else if ((b0 & 0x10) != 0) { width = 4; firstMasked = b0 & 0x0F; }
            else if ((b0 & 0x08) != 0) { width = 5; firstMasked = b0 & 0x07; }
            else if ((b0 & 0x04) != 0) { width = 6; firstMasked = b0 & 0x03; }
            else if ((b0 & 0x02) != 0) { width = 7; firstMasked = b0 & 0x01; }
            else if (b0 == 0x01)       { width = 8; firstMasked = 0; }
            else throw new InvalidDataException($"Invalid EBML size byte 0x{b0:X2}");

            long val = firstMasked;
            for (int i = 1; i < width; i++)
                val = (val << 8) | reader.ReadByte();

            long unknownVal = (1L << (7 * width)) - 1;
            if (val == unknownVal) return long.MaxValue;

            return val;
        }

        private long ReadVarUInt(BinaryReader reader, int byteCount)
        {
            long val = 0;
            for (int i = 0; i < byteCount; i++)
                val = (val << 8) | reader.ReadByte();
            return val;
        }
    }
}
