using GitPngTool.Models;
using GitPngTool.Services;
using Microsoft.Win32;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace GitPngTool;

/// <summary>
/// Thin UI layer. All real work lives in the Services namespace so it is easy to
/// test and extend (encode pipeline: Git -> ZIP -> Brotli -> PNG, and the reverse).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    #region Encode (Git -> PNG)

    private async void BtnLoadCommits_Click(object sender, RoutedEventArgs e)
    {
        string repoPath = TxtRepoPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(repoPath))
        {
            ShowError("Please enter a valid repository path.");
            return;
        }

        SetLoading(true, "Fetching last 100 commits...");

        try
        {
            // Call fast async GitService method targeting the last 100 commits
            var commits = await GitService.GetLatestCommitsAsync(repoPath, count: 100);
            DgCommits.ItemsSource = commits;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private async void BtnProcessEncode_Click(object sender, RoutedEventArgs e)
    {
        string repoPath = TxtRepoPath.Text.Trim();
        if (DgCommits.SelectedItem is not CommitItem selectedCommit)
        {
            MessageBox.Show("Please select a commit first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "GitPngExport_" + Guid.NewGuid().ToString("N"));
        string? payload = null;

        SetLoading(true, "Processing encode...");

        try
        {
            // 1. Export changed files in parallel asynchronously + generate delete_files.ps1
            Directory.CreateDirectory(tempDir);
            await GitService.ExportChangedFilesAsync(repoPath, selectedCommit.Hash, tempDir);

            // 2. ZIP + Brotli compress the export off the UI thread
            payload = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".br");
            await Task.Run(() => CompressionService.CompressDirectoryToBrotli(tempDir, payload));

            // 3. Pack the payload into a lossless PNG in the repo root
            string outputPng = Path.Combine(repoPath, $"git_change_{Random.Shared.Next(100000, 1000000)}.png");
            await Task.Run(() => PngNoiseEncoder.EncodeToNoise(payload, outputPng));

            MessageBox.Show($"Success! Created PNG in repo root:\n{outputPng}", "Completed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError($"Error processing encode: {ex.Message}");
        }
        finally
        {
            if (payload != null && File.Exists(payload)) File.Delete(payload);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);

            SetLoading(false);
        }
    }

    #endregion

    #region Decode (PNG -> Repo)

    private void BtnBrowsePng_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "PNG Files (*.png)|*.png" };
        if (dlg.ShowDialog() == true)
            TxtPngPath.Text = dlg.FileName;
    }

    private async void BtnProcessDecode_Click(object sender, RoutedEventArgs e)
    {
        string targetRepo = TxtDecodeRepoPath.Text.Trim();
        string pngPath = TxtPngPath.Text.Trim();

        if (!Directory.Exists(targetRepo) || !File.Exists(pngPath))
        {
            ShowError("Please ensure both target repo path and PNG path are valid.");
            return;
        }

        TxtLog.Clear();
        Log("Starting Decode Operation...");
        SetLoading(true, "Decoding PNG file...");

        string tempBr = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".br");
        string tempZip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            await Task.Run(() =>
            {
                PngNoiseEncoder.DecodeFromNoise(pngPath, tempBr);
                Dispatcher.Invoke(() => Log("Converted PNG back to compressed stream."));

                CompressionService.DecompressBrotliToZip(tempBr, tempZip);
                CompressionService.ExtractZip(tempZip, targetRepo);
                Dispatcher.Invoke(() => Log($"Extracted changed files to '{targetRepo}'."));

                PowerShellRunner.RunDeleteScriptIfPresent(targetRepo, msg => Dispatcher.Invoke(() => Log(msg)));
            });

            Log("Decode and Extract operation finished successfully!");
            MessageBox.Show("Decode process completed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            ShowError($"Error during decode: {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempBr)) File.Delete(tempBr);
            if (File.Exists(tempZip)) File.Delete(tempZip);

            SetLoading(false);
        }
    }

    #endregion

    #region UI helpers

    private void Log(string message)
    {
        TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        TxtLog.ScrollToEnd();
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>
    /// Controls the visibility of a loading panel/popup in your XAML.
    /// Assumes XAML controls named LoadingOverlay (Grid/Canvas/Popup) and TxtLoadingMessage (TextBlock).
    /// </summary>
    private void SetLoading(bool isLoading, string message = "Loading...")
    {
        // If you have a loading grid or popup in MainWindow.xaml, configure it here:
        if (FindName("LoadingOverlay") is FrameworkElement loadingOverlay)
        {
            loadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        if (FindName("TxtLoadingMessage") is System.Windows.Controls.TextBlock txtMessage)
        {
            txtMessage.Text = message;
        }

        // Disable primary interaction buttons during background work
        BtnLoadCommits.IsEnabled = !isLoading;
        BtnProcessEncode.IsEnabled = !isLoading;
        BtnProcessDecode.IsEnabled = !isLoading;
    }

    #endregion
}