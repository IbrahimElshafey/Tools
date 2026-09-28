using System.IO;
using System.IO.Compression;

namespace GitPngTool.Services;

/// <summary>
/// Dual-stage compression helpers: directory -&gt; ZIP -&gt; Brotli, and the reverse.
/// </summary>
public static class CompressionService
{
    /// <summary>Stage 1+2: ZIP the directory (SmallestSize) then Brotli-compress it to a .br payload.</summary>
    public static void CompressDirectoryToBrotli(string sourceDir, string destinationBrotliFile)
    {
        string tempZip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            ZipFile.CreateFromDirectory(sourceDir, tempZip, CompressionLevel.SmallestSize, includeBaseDirectory: false);

            byte[] zipBytes = File.ReadAllBytes(tempZip);
            using var fileStream = File.Create(destinationBrotliFile);
            using var brotli = new BrotliStream(fileStream, CompressionLevel.SmallestSize);
            brotli.Write(zipBytes, 0, zipBytes.Length);
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }

    /// <summary>Reverse stage 2: Brotli-decompress a .br payload back into a .zip archive.</summary>
    public static void DecompressBrotliToZip(string brotliPath, string outputZipPath)
    {
        using var input = File.OpenRead(brotliPath);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = File.Create(outputZipPath);
        brotli.CopyTo(output);
    }

    /// <summary>Reverse stage 1: extract the .zip archive into the target directory (overwriting).</summary>
    public static void ExtractZip(string zipPath, string destinationDir) =>
        ZipFile.ExtractToDirectory(zipPath, destinationDir, overwriteFiles: true);
}
