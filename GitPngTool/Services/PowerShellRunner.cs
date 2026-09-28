using System.Diagnostics;
using System.IO;

namespace GitPngTool.Services;

/// <summary>
/// Runs the generated delete_files.ps1 cleanup script using an external PowerShell
/// process. This avoids in-process hosting (System.Management.Automation), which fails
/// to locate its built-in snap-in assemblies in a normal framework-dependent build.
/// </summary>
public static class PowerShellRunner
{
    /// <summary>
    /// Executes delete_files.ps1 inside <paramref name="directory"/> when present.
    /// Returns true when a script was found and executed.
    /// </summary>
    public static bool RunDeleteScriptIfPresent(string directory, Action<string>? log = null)
    {
        string scriptPath = Path.Combine(directory, "delete_files.ps1");
        if (!File.Exists(scriptPath))
            return false;

        log?.Invoke("Found delete_files.ps1 script. Executing...");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var stdOut = process.StandardOutput.ReadToEndAsync();
        var stdErr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        string output = stdOut.GetAwaiter().GetResult().Trim();
        if (output.Length > 0)
            log?.Invoke(output);

        log?.Invoke("Successfully executed delete_files.ps1.");
        return true;
    }
}

