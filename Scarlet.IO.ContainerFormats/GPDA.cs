using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Scarlet.IO.ContainerFormats
{
    /* .dat files from the PS3 game "Oreimo" ("Ore no imouto ga konna ni kawaii wake ga nai Happy Ending") */

    public class GPDAFileInfo : ContainerElement
    {
        public Int64 Offset { get; private set; }
        public Int64 Size { get; private set; }
        public Int64 NameOffset { get; private set; }
        public string Name { get; private set; }

        public GPDAFileInfo(EndianBinaryReader reader)
        {
            long pos;
            Int32 nameLength;
            byte[] gzHeader;

            Offset = reader.ReadInt64();
            reader.ReadInt64(); /* unknown, always zero? */
            Size = reader.ReadInt64();
            NameOffset = reader.ReadInt64();

            pos = reader.BaseStream.Position;
            reader.BaseStream.Seek(NameOffset, SeekOrigin.Begin);
            nameLength = reader.ReadInt32();
            Name = Encoding.ASCII.GetString(reader.ReadBytes(nameLength));

            /* fixup filename if the file is .gz compressed... */
            /* TODO: when the gz plugin can detect gzip files even though they 
             * do not end in ".gz", this check can be removed */
            reader.BaseStream.Seek(Offset, SeekOrigin.Begin);
            gzHeader = reader.ReadBytes(2);
            if (!Name.EndsWith(".gz") && gzHeader[0] == 0x1f && gzHeader[1] == 0x8b)
                Name += ".gz";

            reader.BaseStream.Seek(pos, SeekOrigin.Begin);
        }

        public override string GetName()
        {
            return Name;
        }

        public override Stream GetStream(Stream containerStream)
        {
            MemoryStream strm = new MemoryStream();

            containerStream.Seek(Offset, SeekOrigin.Begin);
            FileFormat.CopyStream(containerStream, strm, (int)Size);
            strm.Seek(0, SeekOrigin.Begin);

            return strm;
        }
    }

    [MagicNumber("GPDA64BY", 0)]
    public class GPDA : ContainerFormat
    {
        public string MagicNumber { get; private set; }
        public Int64 FileSize { get; private set; }
        public Int32 Unknown { get; private set; } /* should be == 0 everywhere? */
        public Int32 NumFiles { get; private set; }
        public GPDAFileInfo[] Files { get; private set; }

        public override int GetElementCount()
        {
            return NumFiles;
        }

        protected override ContainerElement GetElement(Stream containerStream, int elementIndex)
        {
            if ((elementIndex < 0) || (elementIndex >= NumFiles))
                throw new ArgumentException("Invalid element index.");

            return Files[elementIndex];
        }

        protected override void OnOpen(EndianBinaryReader reader)
        {
            MagicNumber = Encoding.ASCII.GetString(reader.ReadBytes(8)); /* "GPDA64BY" */
            FileSize = reader.ReadInt64();
            Unknown = reader.ReadInt32();
            NumFiles = reader.ReadInt32();

            Files = new GPDAFileInfo[NumFiles];
            for (int i = 0; i < NumFiles; i++)
            {
                Files[i] = new GPDAFileInfo(reader);
            }
        }
    }
}
