using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Scarlet.IO.ContainerFormats
{
    public class MPKFile : ContainerElement
    {
        public uint CompressionFlag { get; private set; }
        public uint ID { get; private set; }
        public ulong Offset { get; private set; }
        public ulong CompressedFileSize { get; private set; }
        public ulong UncompressedFileSize { get; private set; }
        public string FilePath { get; private set; }

        public MPKFile(EndianBinaryReader reader, Int16 version)
        {
            if (version <= 1)
            {
                CompressionFlag = reader.ReadUInt32();
                Offset = reader.ReadUInt32();
                CompressedFileSize = reader.ReadUInt32();
                UncompressedFileSize = reader.ReadUInt32();
                reader.ReadBytes(0x10); /* padding, should be 0x00 */
            }
            else
            {
                CompressionFlag = reader.ReadUInt32();
                ID = reader.ReadUInt32();
                Offset = reader.ReadUInt64();
                CompressedFileSize = reader.ReadUInt64();
                UncompressedFileSize = reader.ReadUInt64();
            }
            FilePath = Encoding.ASCII.GetString(reader.ReadBytes(0xE0)).TrimEnd('\0');
        }

        public override string GetName()
        {
            return FilePath;
        }

        public override Stream GetStream(Stream containerStream)
        {
            containerStream.Seek((long)Offset, SeekOrigin.Begin);
            MemoryStream stream = new MemoryStream();
            FileFormat.CopyStream(containerStream, stream, (int)CompressedFileSize);
            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }
    }

    [MagicNumber("MPK\0", 0x00)]
    public class MPK : ContainerFormat
    {
        public string MagicNumber { get; private set; }
        public ushort Unknown0x04 { get; private set; }
        public short Version { get; private set; }
        public int NumFiles { get; private set; }
        public byte[] Unknown0x0C { get; private set; } /* 34 bytes */
        public MPKFile[] Files { get; private set; }

        protected override void OnOpen(EndianBinaryReader reader)
        {
            MagicNumber = Encoding.ASCII.GetString(reader.ReadBytes(4));
            Unknown0x04 = reader.ReadUInt16();
            Version = reader.ReadInt16();
            NumFiles = reader.ReadInt32();
            Unknown0x0C = reader.ReadBytes(0x34);

            Files = new MPKFile[NumFiles];
            for (int i = 0; i < Files.Length; i++) Files[i] = new MPKFile(reader, Version);
        }

        public override int GetElementCount()
        {
            return NumFiles;
        }

        protected override ContainerElement GetElement(Stream containerStream, int elementIndex)
        {
            if (elementIndex < 0 || elementIndex >= Files.Length) throw new IndexOutOfRangeException("Invalid file index specified");
            return Files[elementIndex];
        }
    }
}
