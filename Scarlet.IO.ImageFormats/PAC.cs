using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

using Scarlet.Drawing;
using Scarlet.IO;

namespace Scarlet.IO.ImageFormats
{
    /*
     * *.PAC files as found (at least) in the two "Love Hina" games for the Dreamcast:
     *  - Love Hina: Smile Again
     *  - Love Hina: Totsuzen Engeji Happening
     *  
     *  Each file conforms to the following format:
     *  
     *  Int32    numFiles
     *  -----
     *  Int32    File 1 Offset
     *  Int32    File 1 Length
     *  ...
     *  Int32    File n Offset
     *  Int32    File n Length
     *  -----
     *  Int32    File 1 Image Width
     *  Int32    File 1 Image Height
     *  Int32    File 1 Image Format
     *  byte[]   File 1 Pixel Data, (width * height * 2) bytes, or: (File 1 Length - 12) bytes
     *  ...
     *  
     *  Some files are not images, for those the Width, Height and/or Format values 
     *  are incorrect which is easily detected. Those files are skipped.
     */
    [FormatDetection(typeof(PAC), nameof(DetectPAC))]
    public class PAC : ImageFormat
    {
        public class PACImageInfo
        {
            public int Width;
            public int Height;
            public int Offset;
            public int Length;
            public int Format; /* 2 means x1r5g5b5, everything else(?) means a4r4g4b4? */
            public byte[] PixelData;
        }

        public class PACHeader
        {
            public Int32 NumImages;
            public List<PACImageInfo> ImageInfo;
        }

        public PACHeader Header { get; private set; }
        
        private static PACHeader ReadHeader(EndianBinaryReader reader)
        {
            int totalSize = 4; /* start with the header size */
            PACHeader result = new PACHeader();
            PACImageInfo[] rawImageInfo; /* contains all sections, even those that are not images */

            reader.Endianness = Endian.LittleEndian;
            result.NumImages = reader.ReadInt32();

            /* some arbitrary upper limit for the number of files */
            if (result.NumImages > 0x10000)
                return null;

            /* each image has two ints, offset and size, in the header */
            if (reader.BaseStream.Length < 4 + result.NumImages * 8)
                return null;

            //result.ImageInfo = new PACImageInfo[result.NumImages];
            rawImageInfo = new PACImageInfo[result.NumImages];
            totalSize += result.NumImages * 8;

            for (int i = 0; i < result.NumImages; i++)
            {
                rawImageInfo[i] = new PACImageInfo();
                rawImageInfo[i].Offset = reader.ReadInt32();
                rawImageInfo[i].Length = reader.ReadInt32();
                totalSize += rawImageInfo[i].Length;
            }

            if (reader.BaseStream.Length != totalSize)
                return null;

            /* create the final list of "real" images */
            result.ImageInfo = new List<PACImageInfo>();

            for (int i = 0; i < result.NumImages; i++)
            {
                reader.BaseStream.Seek(rawImageInfo[i].Offset, System.IO.SeekOrigin.Begin);
                rawImageInfo[i].Width = reader.ReadInt32();
                rawImageInfo[i].Height = reader.ReadInt32();
                rawImageInfo[i].Format = reader.ReadInt32();

                /* skip all "images" with one dimension of zero ... those are something else, not images */
                /* TODO: the entry at index 0 is always like this. Apparently it has to do with the 
                 * positions of where the "sub-images" are placed onto the main image for animation.
                 * Need to figure out how this works... */
                if ((rawImageInfo[i].Width <= 8) || (rawImageInfo[i].Width > 4096) ||
                    (rawImageInfo[i].Height <= 8) || (rawImageInfo[i].Height > 4096))
                    continue;

                /* check to see if size makes sense... length of the image data should not be less than 12+width*height*2, but it can be more */
                if (12 + rawImageInfo[i].Width * rawImageInfo[i].Height * 2 > rawImageInfo[i].Length)
                    return null;

                /* check the format */
                if ((rawImageInfo[i].Format < 1) || (rawImageInfo[i].Format > 3))
                    return null;

                /* at this point we are pretty confident that what we're seeing here is an image... store it */
                result.ImageInfo.Add(rawImageInfo[i]);
            }

            /* fix the number of images */
            result.NumImages = result.ImageInfo.Count;
            return result;
        }

        public static bool DetectPAC(EndianBinaryReader reader)
        {
            if (ReadHeader(reader) == null)
                return false;

            return true;
        }

        public override int GetImageCount()
        {
            if (Header == null)
                throw new InvalidOperationException("No valid PAC file detected");

            return Header.NumImages;
        }

        public override int GetPaletteCount()
        {
            return 0;
        }

        protected override Bitmap OnGetBitmap(int imageIndex, int paletteIndex)
        {
            PixelDataFormat pixelFormat = PixelDataFormat.Undefined;

            if (Header == null)
                throw new InvalidOperationException("No valid PAC file detected");

            if ((imageIndex < 0) || (imageIndex >= Header.NumImages))
                throw new ArgumentException("Invalid imageIndex specified.");

            switch(Header.ImageInfo[imageIndex].Format)
            {
                case 1:
                    pixelFormat = PixelDataFormat.FormatRgb565;
                    break;
                case 2:
                    pixelFormat = PixelDataFormat.FormatArgb1555;
                    break;
                case 3:
                    pixelFormat = PixelDataFormat.FormatArgb4444;
                    break;
                default:
                    throw new ApplicationException("Invalid image format detected. Please update PAC.cs.");
            }

            ImageBinary imgbin = new ImageBinary();
            imgbin.Width = Header.ImageInfo[imageIndex].Width;
            imgbin.Height = Header.ImageInfo[imageIndex].Height;
            imgbin.InputPixelFormat = pixelFormat;
            imgbin.InputEndianness = Endian.LittleEndian;
            imgbin.AddInputPixels(Header.ImageInfo[imageIndex].PixelData);

            return imgbin.GetBitmap();
        }

        protected override void OnOpen(EndianBinaryReader reader)
        {
            Header = ReadHeader(reader);

            if (Header == null)
                throw new InvalidOperationException("Invalid PAC header.");

            /* read the image pixel data */
            for (int i = 0; i < Header.NumImages; i++)
            {
                reader.BaseStream.Seek(Header.ImageInfo[i].Offset + 12, System.IO.SeekOrigin.Begin);
                Header.ImageInfo[i].PixelData = reader.ReadBytes(Header.ImageInfo[i].Length - 12);
            }
        }
    }
}
