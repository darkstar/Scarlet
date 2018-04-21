using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Drawing;

using Scarlet.Drawing;
using Scarlet.IO;

namespace Scarlet.IO.ImageFormats
{
    /*
     * .phyre format with multiple variants (images, 3D models, ...)
     */
    [MagicNumber("PHYR", 0)]
    [MagicNumber("RYHP", 0)]
    public class Phyre : ImageFormat
    {
        private readonly UInt32 Platform_GCM = 0x47434d00;
        private readonly UInt32 Platform_GNM = 0x474e4d02;

        private struct RootRecordListEntry
        {
            public Int32 FirstSubrecord;
            public Int32 LastSubRecord;
            public Int32 NumSubRecords;
            public string Name;
        }
        private struct SubRecordListEntry
        {
            public Int32 Offset;
            public Int32 Size;
            public string Name;
        }

        /* .phyre files can apparently come in both endiannesses... */
        public Endian Endianness { get; private set; } = Endian.BigEndian;

        public Int32 MagicOffset { get; private set; }
        public Int32 DescriptorTableSize { get; private set; }
        public UInt32 Platform { get; private set; }
        public UInt32 Magic { get; private set; }
        public Int32 NumRootDescriptors { get; private set; }
        public Int32 NumSubDescriptors { get; private set; }
        public Int32 StringTableSize { get; private set; }
        public bool IsSwizzled { get; private set; } = false;

        private RootRecordListEntry[] rootRecords;
        private SubRecordListEntry[] subRecords;

        public ImageBinary ImgBin { get; private set; }

        public override int GetImageCount()
        {
            return 1;
        }

        public override int GetPaletteCount()
        {
            return 0;
        }

        protected override Bitmap OnGetBitmap(int imageIndex, int paletteIndex)
        {
            Bitmap result = ImgBin.GetBitmap();
            if (!IsSwizzled)
                result.RotateFlip(RotateFlipType.RotateNoneFlipY);

            return result;
        }

        /* note: this is ugly code converted from python code found here:
         * https://zenhax.com/viewtopic.php?t=7573
         * TODO: it needs some serious cleanup work */
        protected override void OnOpen(EndianBinaryReader reader)
        {
            string magic;
            Int32 tmp;
            int maxRecord = 0;
            Int32 descStringOffset, numSubrecords, firstSubRecord, lastSubRecord;
            Int32 pInstanceListHeaderSize = 0x24;
            Int32 manualOffset = 0, temp = 0, datasize = 0, m_instanceListCount = 0, importNameOffset = 0;
            Int32 baseOffset;
            long pos;
            string name;
            Int32[] m_size = new Int32[0];
            Int32[] m_objectsSize = new Int32[0];
            Int32[] m_arraysSize = new Int32[0];
            Int32 m_totalSize = 0;
            string m_formatString;
            string m_memoryType = "";
            string m_texStateCorrected;
            string importName;
            Int32 width = 0, height = 0, dataOffset = 0;
            byte[] data;
            PixelDataFormat pixelFormat = PixelDataFormat.Undefined;

            /* TODO: extract the first few checks (up to the magic value) into a separate method,
             * and use that to detect a valid file
             */
        magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (magic == "RYHP")
                Endianness = Endian.LittleEndian;

            reader.Endianness = Endianness;

            /* check the filename, if possible */
            if (reader.BaseStream is FileStream)
            {
                string fname = (reader.BaseStream as FileStream).Name.ToLower();
                if (fname.Contains(".dae.") || fname.Contains(".fx#") || fname.Contains(".fx."))
                    throw new NotSupportedException("3D models and shaders are not supported.");
            }

            MagicOffset = reader.ReadInt32();
            DescriptorTableSize = reader.ReadInt32();
            Platform = reader.ReadUInt32();

            if ((Platform != Platform_GCM) && (Platform != Platform_GNM))
                throw new NotSupportedException("Invalid platform.");

            /* check the extra magic value */
            reader.BaseStream.Seek(MagicOffset, SeekOrigin.Begin);
            Magic = reader.ReadUInt32();
            if (Magic != 0x01020304)
                throw new NotSupportedException("Invalid secondary magic value.");

            /* this must be the same number that we already read */
            tmp = reader.ReadInt32();
            if (tmp != DescriptorTableSize)
                throw new NotSupportedException("Invalid descriptor table size.");

            /* this number of int32's will be skipped later */
            tmp = reader.ReadInt32() + 2;
            NumRootDescriptors = reader.ReadInt32();
            NumSubDescriptors = reader.ReadInt32();
            StringTableSize = reader.ReadInt32();
            reader.ReadBytes(tmp  * 4);

            rootRecords = new RootRecordListEntry[NumRootDescriptors];

            for (int i = 0; i < NumRootDescriptors; i++)
            {
                reader.ReadUInt32(); /* unknown */
                reader.ReadUInt16(); /* unknown */
                reader.ReadUInt16(); /* unknown */
                descStringOffset = reader.ReadInt32();
                numSubrecords = reader.ReadInt32();
                reader.ReadUInt32(); /* unknown */
                reader.ReadUInt32(); /* unknown */
                reader.ReadUInt32(); /* unknown */
                reader.ReadUInt32(); /* unknown */
                reader.ReadUInt32(); /* unknown */
                if (numSubrecords > 0)
                {
                    firstSubRecord = maxRecord;
                    maxRecord += numSubrecords;
                    lastSubRecord = maxRecord - 1;
                }
                else
                {
                    firstSubRecord = 0;
                    lastSubRecord = 0;
                }
                /* read the record name */
                pos = reader.BaseStream.Position;
                reader.BaseStream.Seek(MagicOffset + DescriptorTableSize - StringTableSize + descStringOffset, SeekOrigin.Begin);
                name = reader.ReadNullTerminatedString();
                if (name == "PEffect")
                {
                    throw new NotSupportedException("Not a texture file.");
                }
                reader.BaseStream.Seek(pos, SeekOrigin.Begin);

                rootRecords[i].FirstSubrecord = firstSubRecord;
                rootRecords[i].LastSubRecord = lastSubRecord;
                rootRecords[i].NumSubRecords = numSubrecords;
                rootRecords[i].Name = name;
            }

            subRecords = new SubRecordListEntry[NumSubDescriptors];
            for (int i = 0; i < NumSubDescriptors; i++)
            {
                descStringOffset = reader.ReadInt32();
                reader.ReadInt32(); /* unknown */
                subRecords[i].Offset = reader.ReadInt32();
                subRecords[i].Size = reader.ReadInt32();
                reader.ReadInt32(); /* unknown */
                reader.ReadInt32(); /* unknown */
                pos = reader.BaseStream.Position;
                reader.BaseStream.Seek(MagicOffset + DescriptorTableSize - StringTableSize + descStringOffset, SeekOrigin.Begin);
                subRecords[i].Name = reader.ReadNullTerminatedString();
                reader.BaseStream.Seek(pos, SeekOrigin.Begin);
            }

            for (int i = 0; i < NumRootDescriptors; i++)
            {
                if (rootRecords[i].NumSubRecords > 0)
                {
                    for (int j = rootRecords[i].FirstSubrecord; j < rootRecords[i].LastSubRecord + 1; j++)
                    {
                        if (i < 6)
                        {
                            reader.BaseStream.Seek(subRecords[j].Offset, SeekOrigin.Begin);
                            temp = reader.ReadInt32();
                            if ((subRecords[j].Name == "m_vramBufferSize") ||
                                (subRecords[j].Name == "m_maxTextureBufferSize") ||
                                (subRecords[j].Name == "m_sharedVideoMemoryBufferSize"))
                            {
                                datasize = temp;
                            }
                            else if (subRecords[j].Name == "m_instanceListCount") /* number of PInstanceListHeader structures */
                            {
                                m_instanceListCount = temp;
                                importNameOffset = pInstanceListHeaderSize * m_instanceListCount;
                                m_size = new Int32[m_instanceListCount];
                                m_objectsSize = new Int32[m_instanceListCount];
                                m_arraysSize = new Int32[m_instanceListCount];
                            }
                        }
                        else if (rootRecords[i].Name == "PInstanceListHeader")
                        {
                            for (int k = 0; k < m_instanceListCount; k++)
                            {
                                baseOffset = MagicOffset + DescriptorTableSize + k * pInstanceListHeaderSize;
                                reader.BaseStream.Seek(baseOffset + subRecords[j].Offset, SeekOrigin.Begin);
                                temp = reader.ReadInt32();
                                if (subRecords[j].Name == "m_size")
                                {
                                    m_size[k] = temp;
                                    m_totalSize += temp;
                                }
                                else if (subRecords[j].Name == "m_objectsSize")
                                {
                                    m_objectsSize[k] = temp;
                                }
                                else if (subRecords[j].Name == "m_arraysSize")
                                {
                                    m_arraysSize[k] = temp;
                                }
                            }
                        }
                        else if (rootRecords[i].Name == "PString")
                        {
                            baseOffset = MagicOffset + DescriptorTableSize + pInstanceListHeaderSize * m_instanceListCount;
                            reader.BaseStream.Seek(baseOffset + m_objectsSize[0], SeekOrigin.Begin);
                            importName = Encoding.ASCII.GetString(reader.ReadBytes(m_arraysSize[0])).TrimEnd('\0');
                            /* Console.WriteLine("[i] Phyre import name: {0}", importName); */
                        }
                        else if (rootRecords[i].Name == "PTexture2DGNM")
                        {
                            /* GNM exclusive and quite hacky */
                            baseOffset = MagicOffset + DescriptorTableSize + pInstanceListHeaderSize * m_instanceListCount;
                            reader.BaseStream.Seek(baseOffset + m_size[0] + subRecords[j].Offset, SeekOrigin.Begin);
                            reader.ReadBytes(subRecords[j].Size);
                            if (subRecords[j].Name == "m_texState")
                            {
                                reader.BaseStream.Seek(baseOffset + m_size[0] + subRecords[j].Offset + 14, SeekOrigin.Begin);
                                m_texStateCorrected = Encoding.ASCII.GetString(reader.ReadBytes(13));
                            }
                        }
                        else if (rootRecords[i].Name == "PTexture2DBase")
                        {
                            baseOffset = MagicOffset + DescriptorTableSize + pInstanceListHeaderSize * m_instanceListCount;
                            reader.BaseStream.Seek(baseOffset + m_size[0] + subRecords[j].Offset, SeekOrigin.Begin);
                            temp = reader.ReadInt32();
                            if (subRecords[j].Name == "m_width")
                                width = temp;
                            else if (subRecords[j].Name == "m_height")
                                height = temp;
                        }
                        else if (rootRecords[i].Name == "PTextureCommonBase")
                        {
                            baseOffset = MagicOffset + DescriptorTableSize + pInstanceListHeaderSize * m_instanceListCount + m_totalSize + manualOffset;
                            if (subRecords[j].Name == "m_format")
                            {
                                reader.BaseStream.Seek(baseOffset, SeekOrigin.Begin);
                                m_formatString = reader.ReadNullTerminatedString();
                                manualOffset += m_formatString.Length + 1;
                                /* safeguard against cubemaps and PTexture3D */
                                if (m_formatString != "PTexture2D")
                                {
                                    throw new NotSupportedException("Only 2D textures supported.");
                                }
                            }
                            else if (subRecords[j].Name == "m_memoryType")
                            {
                                reader.BaseStream.Seek(baseOffset, SeekOrigin.Begin);
                                m_memoryType = reader.ReadNullTerminatedString();
                                manualOffset += m_memoryType.Length + 1;
                                dataOffset = MagicOffset + DescriptorTableSize + pInstanceListHeaderSize * m_instanceListCount + m_totalSize + manualOffset;
                                if (Platform != Platform_GNM)
                                    dataOffset += 0x25;
                                else
                                    dataOffset += 0x2b;
                            }
                            else
                            {
                                reader.BaseStream.Seek(baseOffset, SeekOrigin.Begin);
                                manualOffset += subRecords[j].Size;
                            }
                        }
                    }
                }
            }

            /* check for swizzled textures */
            if (Platform == Platform_GCM)
            {
                reader.BaseStream.Seek(MagicOffset + DescriptorTableSize + pInstanceListHeaderSize * m_instanceListCount + m_size[0] + 0x10, SeekOrigin.Begin);
                Int32 flag = reader.ReadInt32();
                /* Todo: maybe also 0x09 == swizzled? check this... */
                if (flag == 0x09)
                    IsSwizzled = true;
            }

            reader.BaseStream.Seek(dataOffset, SeekOrigin.Begin);
            data = reader.ReadBytes(datasize);
            if (Platform == Platform_GNM)
            {
                /* the image data is a GNF image, without header. Push it through the GNF decoder? */
                throw new NotSupportedException("GNF images not supported yet.");
            }

            switch (m_memoryType)
            {
                case "DXT1":
                    pixelFormat = PixelDataFormat.FormatDXT1Rgba;
                    break;
                case "DXT3":
                    pixelFormat = PixelDataFormat.FormatDXT3;
                    break;
                case "DXT5":
                    pixelFormat = PixelDataFormat.FormatDXT5;
                    break;
                case "BC5":
                    pixelFormat = PixelDataFormat.FormatRGTC2; /* or _signed? */
                    break;
                case "BC7":
                    pixelFormat = PixelDataFormat.FormatBPTC;
                    break;
                case "RGBA8":
                    pixelFormat = PixelDataFormat.FormatRgba8888;
                    break;
                case "ARGB8":
                    pixelFormat = PixelDataFormat.FormatBgra8888;
                    if (Platform == Platform_GCM && IsSwizzled)
                    {
                        /* TODO: fix this */
                        pixelFormat |= PixelDataFormat.PixelOrderingSwizzledVita;
                    }
                    break;
                case "L8":
                    pixelFormat = PixelDataFormat.FormatLuminance8;
                    break;
                case "A8":
                    pixelFormat = PixelDataFormat.FormatAlpha8;
                    break;
                default:
                    throw new NotSupportedException($"Unknown pixel format {m_memoryType}.");
            }

            ImgBin = new ImageBinary();
            ImgBin.Width = width;
            ImgBin.Height = height;
            ImgBin.InputPixelFormat = pixelFormat;
            ImgBin.InputEndianness = Endianness;
            ImgBin.AddInputPixels(data);
        }
    }
}
