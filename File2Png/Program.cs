using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

class Program
{

    static void Main(string[] args)
    {
        int x = 1;
        switch (x)
        {
            case 1: File2Png(args); break;
            case 2: CheckPngType(@"C:\Users\Administrator\Downloads\image.png"); break;
        }

    }
    static void CheckPngType(string path)
    {
        using (Bitmap bmp = new Bitmap(path))
        {
            Console.WriteLine($"Dimensions:   {bmp.Width} x {bmp.Height}");
            Console.WriteLine($"Pixel Format: {bmp.PixelFormat}");

            // Lock bits to inspect raw pixel byte data
            BitmapData data = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly,
                bmp.PixelFormat);

            byte[] sample = new byte[32];
            Marshal.Copy(data.Scan0, sample, 0, sample.Length);
            bmp.UnlockBits(data);

            Console.WriteLine("\nFirst 32 Raw Bytes (Hex):");
            Console.WriteLine(BitConverter.ToString(sample));

            Console.WriteLine("\nFirst 4 Bytes as Int32 (Little-Endian): " + BitConverter.ToInt32(sample, 0));
            Console.WriteLine("First 4 Bytes as Int32 (Big-Endian):    " + System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(BitConverter.ToInt32(sample, 0)));
            Console.ReadKey();
        }
    }
    static void File2Png(string[] args)
    {

        if (args.Length == 0)
            args = ["-d", @"C:\Users\Administrator\Downloads\image.png", @"C:\Users\Administrator\Downloads\file.7z"];
        //args = ["-e", @"C:\Users\Administrator\Downloads\file.pdf", @"C:\Users\Administrator\Downloads\image-11.png"];
        if (args.Length < 3)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  Encode: File2Png.exe -e <input-file> <output-image.png>");
            Console.WriteLine("  Decode: File2Png.exe -d <input-image.png> <output-file>");
            return;
        }

        string mode = args[0].ToLower();
        string input = args[1];
        string output = args[2];

        if (!File.Exists(input))
        {
            Console.WriteLine($"Error: '{input}' not found.");
            return;
        }

        if (mode == "-e")
        {
            EncodeToNoise(input, output);
        }
        else if (mode == "-d")
        {
            DecodeFromNoise(input, output);
        }
    }

    static void EncodeToNoise(string inputPath, string outputPath)
    {
        byte[] fileBytes = File.ReadAllBytes(inputPath);

        // 3 bytes per pixel (Red, Green, Blue)
        int bytesPerPixel = 3;
        int totalPixels = (int)Math.Ceiling((double)fileBytes.Length / bytesPerPixel);

        // Create a square image
        int size = (int)Math.Ceiling(Math.Sqrt(totalPixels));

        using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb))
        {
            BitmapData bmpData = bmp.LockBits(
                new Rectangle(0, 0, size, size),
                ImageLockMode.WriteOnly,
                bmp.PixelFormat);

            int bytesRemaining = fileBytes.Length;
            int offset = 0;

            // Copy bytes row by row to account for GDI+ stride alignment
            for (int y = 0; y < size; y++)
            {
                IntPtr rowPointer = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                int bytesToCopy = Math.Min(size * bytesPerPixel, bytesRemaining);

                if (bytesToCopy > 0)
                {
                    Marshal.Copy(fileBytes, offset, rowPointer, bytesToCopy);
                    offset += bytesToCopy;
                    bytesRemaining -= bytesToCopy;
                }
            }

            bmp.UnlockBits(bmpData);
            bmp.Save(outputPath, ImageFormat.Png); // Save as lossless PNG
        }

        Console.WriteLine($"Success! Encoded {fileBytes.Length} bytes into '{outputPath}'.");
    }

    static void DecodeFromNoise(string inputPath, string outputPath)
    {
        byte[] extractedBytes;

        using (Bitmap bmp = new Bitmap(inputPath))
        {
            int size = bmp.Width; // Assuming square from the encoder
            int bytesPerPixel = 3;

            BitmapData bmpData = bmp.LockBits(
                new Rectangle(0, 0, size, size),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            extractedBytes = new byte[size * size * bytesPerPixel];

            for (int y = 0; y < size; y++)
            {
                IntPtr rowPointer = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                Marshal.Copy(rowPointer, extractedBytes, y * (size * bytesPerPixel), size * bytesPerPixel);
            }

            bmp.UnlockBits(bmpData);
        }

        File.WriteAllBytes(outputPath, extractedBytes);
        Console.WriteLine($"Success! Decoded data to '{outputPath}'.");
    }
}