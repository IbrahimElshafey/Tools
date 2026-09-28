using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace GitPngTool.Services;

/// <summary>
/// Encodes arbitrary bytes into a lossless PNG "noise" image and back.
/// This is the exact same algorithm as the original File2Png tool (Program.cs).
/// </summary>
public static class PngNoiseEncoder
{
    // 3 bytes per pixel (Red, Green, Blue) with PixelFormat.Format24bppRgb.
    private const int BytesPerPixel = 3;

    /// <summary>Packs a file's bytes into a square lossless PNG image.</summary>
    public static void EncodeToNoise(string inputPath, string outputPath)
    {
        byte[] fileBytes = File.ReadAllBytes(inputPath);

        int totalPixels = (int)Math.Ceiling((double)fileBytes.Length / BytesPerPixel);
        int size = (int)Math.Ceiling(Math.Sqrt(totalPixels));

        using var bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb);
        BitmapData bmpData = bmp.LockBits(
            new Rectangle(0, 0, size, size),
            ImageLockMode.WriteOnly,
            bmp.PixelFormat);

        int bytesRemaining = fileBytes.Length;
        int offset = 0;

        // Copy bytes row by row to account for GDI+ stride alignment.
        for (int y = 0; y < size; y++)
        {
            IntPtr rowPointer = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
            int bytesToCopy = Math.Min(size * BytesPerPixel, bytesRemaining);

            if (bytesToCopy > 0)
            {
                Marshal.Copy(fileBytes, offset, rowPointer, bytesToCopy);
                offset += bytesToCopy;
                bytesRemaining -= bytesToCopy;
            }
        }

        bmp.UnlockBits(bmpData);
        bmp.Save(outputPath, ImageFormat.Png); // lossless
    }

    /// <summary>Extracts the raw byte data back out of a PNG noise image.</summary>
    public static void DecodeFromNoise(string inputPath, string outputPath)
    {
        byte[] extractedBytes;

        using (var bmp = new Bitmap(inputPath))
        {
            int size = bmp.Width; // square image produced by the encoder

            BitmapData bmpData = bmp.LockBits(
                new Rectangle(0, 0, size, size),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            extractedBytes = new byte[size * size * BytesPerPixel];

            for (int y = 0; y < size; y++)
            {
                IntPtr rowPointer = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                Marshal.Copy(rowPointer, extractedBytes, y * (size * BytesPerPixel), size * BytesPerPixel);
            }

            bmp.UnlockBits(bmpData);
        }

        File.WriteAllBytes(outputPath, extractedBytes);
    }
}
