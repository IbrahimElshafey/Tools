using GitPngTool.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GitPngTool.Services;

/// <summary>
/// High-performance Git service delivering parallelized async file exports and commit metadata extraction.
/// </summary>
public static class GitService
{
    /// <summary>
    /// Reads the latest commits of a repository including hash, commit date, and message.
    /// </summary>
    public static async Task<List<CommitItem>> GetLatestCommitsAsync(string repoPath, int count = 100, CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repoPath, cancellationToken).ConfigureAwait(false);

        // Format: Hash, ISO Commit Date, Subject (tab-separated)
        string output = await RunGitForTextAsync(repoPath, $"log -{count} --format=\"%h%x09%cI%x09%s\"", cancellationToken).ConfigureAwait(false);
        var commits = new List<CommitItem>();

        foreach (string rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;

            string[] parts = line.Split('\t', 3);

            commits.Add(new CommitItem
            {
                Hash = parts[0],
                Date = parts.Length > 1 && DateTimeOffset.TryParse(parts[1], out var date) ? date : null,
                Message = parts.Length > 2 ? parts[2] : string.Empty
            });
        }

        return commits;
    }

    /// <summary>
    /// Synchronous wrapper for GetLatestCommitsAsync.
    /// </summary>
    public static List<CommitItem> GetLatestCommits(string repoPath, int count = 100)
    {
        return GetLatestCommitsAsync(repoPath, count).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously exports changed files in (commitId^..toCommit) in parallel.
    /// Returns the number of copied files.
    /// </summary>
    public static async Task<int> ExportChangedFilesAsync(
        string repoPath,
        string commitId,
        string destinationDir,
        string toCommit = "HEAD",
        int maxDegreeOfParallelism = -1,
        CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repoPath, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(destinationDir);

        string range = $"{commitId}^..{toCommit}";
        string diffOutput = await RunGitForTextAsync(repoPath, $"-c core.quotepath=false diff-tree --no-commit-id --name-status -r {range}", cancellationToken).ConfigureAwait(false);

        var deletedFiles = new ConcurrentBag<string>();
        var filesToExtract = new ConcurrentBag<string>();

        foreach (string rawLine in diffOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] parts = line.Split('\t');
            char status = parts[0][0];

            switch (status)
            {
                case 'A':
                case 'M':
                case 'C':
                case 'R':
                    string rawPath = status == 'R' ? parts[2] : parts[1];
                    filesToExtract.Add(rawPath);
                    break;

                case 'D':
                    deletedFiles.Add(parts[1].Replace('/', '\\'));
                    break;
            }
        }

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism <= 0 ? Environment.ProcessorCount : maxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };

        // Extract files concurrently
        await Parallel.ForEachAsync(filesToExtract, parallelOptions, async (rawPath, ct) =>
        {
            string normalizedPath = rawPath.Replace('/', Path.DirectorySeparatorChar);
            string destFilePath = Path.Combine(destinationDir, normalizedPath);

            string? parentDir = Path.GetDirectoryName(destFilePath);
            if (!string.IsNullOrEmpty(parentDir))
                Directory.CreateDirectory(parentDir);

            await WriteGitObjectToFileAsync(repoPath, $"{toCommit}:{rawPath}", destFilePath, ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (!deletedFiles.IsEmpty)
            await WriteDeleteScriptAsync(destinationDir, range, deletedFiles, cancellationToken).ConfigureAwait(false);

        return filesToExtract.Count;
    }

    /// <summary>
    /// Synchronous wrapper for ExportChangedFilesAsync.
    /// </summary>
    public static int ExportChangedFiles(string repoPath, string commitId, string destinationDir, string toCommit = "HEAD")
    {
        return ExportChangedFilesAsync(repoPath, commitId, destinationDir, toCommit).GetAwaiter().GetResult();
    }

    /// <summary>Throws when <paramref name="repoPath"/> is not a valid git working tree.</summary>
    public static async Task EnsureGitRepositoryAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(repoPath))
            throw new DirectoryNotFoundException($"Directory not found: {repoPath}");

        var result = await RunGitAsync(repoPath, "rev-parse --is-inside-work-tree", cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("The specified directory is not a Git repository.");
    }

    public static void EnsureGitRepository(string repoPath)
    {
        EnsureGitRepositoryAsync(repoPath).GetAwaiter().GetResult();
    }

    private static async Task WriteDeleteScriptAsync(string destinationDir, string range, IEnumerable<string> deletedFiles, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Auto-generated script to delete files changed/removed in range {range}");
        foreach (string delFile in deletedFiles)
            sb.AppendLine($"Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath \"{delFile}\") -Force -ErrorAction SilentlyContinue");

        await File.WriteAllTextAsync(Path.Combine(destinationDir, "delete_files.ps1"), sb.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> RunGitForTextAsync(string workingDir, string arguments, CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(workingDir, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git {arguments} failed (exit {result.ExitCode}): {result.StdErr}");
        return result.StdOut;
    }

    private static async Task WriteGitObjectToFileAsync(string workingDir, string gitObject, string destFilePath, CancellationToken cancellationToken = default)
    {
        var psi = CreateGitProcess(workingDir, $"show \"{gitObject}\"");
        using var process = Process.Start(psi)!;

        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await using (var fileStream = new FileStream(destFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
        {
            await process.StandardOutput.BaseStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            string stdErr = await stdErrTask.ConfigureAwait(false);
            throw new InvalidOperationException($"git show failed for '{gitObject}': {stdErr}");
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunGitAsync(string workingDir, string arguments, CancellationToken cancellationToken = default)
    {
        var psi = CreateGitProcess(workingDir, arguments);
        using var process = Process.Start(psi)!;

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return (process.ExitCode, await stdOutTask.ConfigureAwait(false), await stdErrTask.ConfigureAwait(false));
    }

    private static ProcessStartInfo CreateGitProcess(string workingDir, string arguments) => new()
    {
        FileName = "git",
        Arguments = arguments,
        WorkingDirectory = workingDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8
    };
}