using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Defender_Cab_Verification_Tool
{
    public partial class MainForm : Form
    {
        private CancellationTokenSource _cts;
        private volatile bool _isCleaning;
        private volatile bool _allowClose;

        public MainForm()
        {
            InitializeComponent();
        }

        private void BtnVerify_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select folder containing Defender CAB files (defender-dism-*.cab)";
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                StartVerification(fbd.SelectedPath);
            }
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
        }

        private void BtnOpenLogs_Click(object sender, EventArgs e)
        {
            try
            {
                var logsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logsFolder))
                {
                    MessageBox.Show(this, "Logs folder does not exist yet.", "Logs", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                ProcessStartHelper.OpenFolder(logsFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Unable to open logs folder: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnExportCsv_Click(object sender, EventArgs e)
        {
            try
            {
                if (listViewResults.Items.Count == 0)
                {
                    MessageBox.Show(this, "No results to export.", "Export CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (var sfd = new SaveFileDialog())
                {
                    sfd.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
                    sfd.FileName = $"defender-verify-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
                    if (sfd.ShowDialog(this) != DialogResult.OK) return;

                    using (var sw = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8))
                    {
                        sw.WriteLine("Status,File,Thumbprint,Detail");
                        foreach (ListViewItem item in listViewResults.Items)
                        {
                            var status = item.SubItems[0].Text.Replace("\"", "\"\"");
                            var file = item.SubItems[1].Text.Replace("\"", "\"\"");
                            var thumb = item.SubItems[2].Text.Replace("\"", "\"\"");
                            var detail = item.SubItems.Count > 3 ? item.SubItems[3].Text.Replace("\"", "\"\"") : string.Empty;
                            sw.WriteLine($"\"{status}\",\"{file}\",\"{thumb}\",\"{detail}\"");
                        }
                    }
                }

                MessageBox.Show(this, "Export complete.", "Export CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Export failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExitMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void AboutMenuItem_Click(object sender, EventArgs e)
        {
            using (var about = new AboutForm())
            {
                about.ShowDialog(this);
            }
        }

        private void StartVerification(string sourceFolder = null)
        {
            btnVerify.Enabled = false;
            btnCancel.Enabled = true;
            btnExportCsv.Enabled = false;
            listViewResults.Items.Clear();
            progressBar.Value = 0;
            lblProgressPercent.Text = "0%";
            statusLabel.Text = "Verifying...";
            _cts = new CancellationTokenSource();

            var progress = new Progress<int>(percent =>
            {
                try
                {
                    progressBar.Value = Math.Min(Math.Max(percent, 0), 100);
                    lblProgressPercent.Text = $"{progressBar.Value}%";
                }
                catch { }
            });

            Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(sourceFolder))
                    {
                        DefenderCabVerifier.VerifyAll(
                            fileResult => ReportFileResult(fileResult),
                            progress,
                            text => ReportStatus(text),
                            _cts.Token);
                    }
                    else
                    {
                        DefenderCabVerifier.VerifyAll(
                            sourceFolder,
                            fileResult => ReportFileResult(fileResult),
                            progress,
                            text => ReportStatus(text),
                            _cts.Token);
                    }
                }
                finally
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        btnVerify.Enabled = true;
                        btnCancel.Enabled = false;
                        btnExportCsv.Enabled = listViewResults.Items.Count > 0;
                        statusLabel.Text = "Finished";
                    }));
                }
            });
        }

        private void ReportFileResult(FileVerificationResult result)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)(() => ReportFileResult(result)));
                return;
            }

            var item = new ListViewItem(result.IsValid ? "Valid" : "Invalid");
            item.SubItems.Add(result.FilePath);
            item.SubItems.Add(result.Thumbprint ?? string.Empty);
            item.SubItems.Add(result.ErrorDetail ?? string.Empty);
            item.ForeColor = result.IsValid ? Color.DarkGreen : Color.DarkRed;
            listViewResults.Items.Add(item);
            listViewResults.EnsureVisible(listViewResults.Items.Count - 1);
        }

        private void ReportStatus(string text)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)(() => ReportStatus(text)));
                return;
            }

            statusLabel.Text = text.Length > 60 ? text.Substring(0, 57) + "..." : text;
        }

        private void AboutMenuItem_Click_1(object sender, EventArgs e)
        {
            using (var about = new AboutForm())
            {
                about.ShowDialog(this);
            }
        }

        private void btnVerifyFile_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Defender CAB file (defender-dism-*.cab)";
                ofd.Filter = "CAB files (*.cab)|*.cab";
                ofd.CheckFileExists = true;
                ofd.Multiselect = false;
                ofd.DefaultExt = "cab";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;

                StartVerificationForCab(ofd.FileName);
            }
        }

        private void StartVerificationForCab(string cabFile)
        {
            btnVerify.Enabled = false;
            btnCancel.Enabled = true;
            btnExportCsv.Enabled = false;
            listViewResults.Items.Clear();
            progressBar.Value = 0;
            lblProgressPercent.Text = "0%";
            statusLabel.Text = "Verifying...";
            _cts = new CancellationTokenSource();

            var progress = new Progress<int>(percent =>
            {
                try
                {
                    progressBar.Value = Math.Min(Math.Max(percent, 0), 100);
                    lblProgressPercent.Text = $"{progressBar.Value}%";
                }
                catch { }
            });

            Task.Run(() =>
            {
                try
                {
                    DefenderCabVerifier.VerifyCab(
                        cabFile,
                        fileResult => ReportFileResult(fileResult),
                        progress,
                        text => ReportStatus(text),
                        _cts.Token);
                }
                finally
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        btnVerify.Enabled = true;
                        btnCancel.Enabled = false;
                        btnExportCsv.Enabled = listViewResults.Items.Count > 0;
                        statusLabel.Text = "Finished";
                    }));
                }
            });
        }

        // Subscribe to cleanup on close by overriding OnFormClosing
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Allow normal close if cleanup already permitted
            if (_allowClose)
            {
                base.OnFormClosing(e);
                return;
            }

            // If cleanup already running, prevent re-entrancy
            if (_isCleaning)
            {
                e.Cancel = true;
                return;
            }

            // Cancel initial close, start cleanup asynchronously, show notice
            e.Cancel = true;
            _isCleaning = true;
            var cleanupForm = new CleanupForm();
            cleanupForm.Show(this);

            Task.Run(() =>
            {
                // Prepare log file in logs folder
                var logsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                try { Directory.CreateDirectory(logsFolder); } catch { }
                var logfile = Path.Combine(logsFolder, $"cleanup-{System.DateTime.Now:yyyyMMdd-HHmmss}.log");

                void WriteLog(string s)
                {
                    try { File.AppendAllText(logfile, s + System.Environment.NewLine, System.Text.Encoding.UTF8); } catch { }
                }

                WriteLog("Cleanup started: " + System.DateTime.Now.ToString("u"));
                try
                {
                    CleanExtractedFolders(WriteLog);
                    WriteLog("Cleanup finished: " + System.DateTime.Now.ToString("u"));
                }
                catch (System.Exception ex)
                {
                    WriteLog("Cleanup error: " + ex.Message);
                }
                finally
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        try { cleanupForm.Close(); } catch { }
                        _isCleaning = false;
                        _allowClose = true;
                        // re-trigger close; will pass through now
                        this.Close();
                    }));
                }
            });
        }

        // Best-effort delete of extracted folders; logs each step via provided logger
        private void CleanExtractedFolders(System.Action<string> log)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var root = Path.Combine(baseDir, "defender-dism");

                if (!Directory.Exists(root))
                {
                    log($"No extracted folder found at {root}");
                    return;
                }

                log($"Removing extracted folder: {root}");

                // Try to clear read-only attributes on files first
                try
                {
                    foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                        }
                        catch (System.Exception exFileAttr)
                        {
                            log($"Warning: could not reset attributes for {file}: {exFileAttr.Message}");
                        }
                    }
                }
                catch (System.Exception exEnumerate)
                {
                    log($"Warning: error enumerating files: {exEnumerate.Message}");
                }

                try
                {
                    Directory.Delete(root, true);
                    log($"Deleted folder: {root}");
                }
                catch (System.Exception exDel)
                {
                    log($"Error deleting folder {root}: {exDel.Message}");
                }
            }
            catch (System.Exception ex)
            {
                log("Unexpected cleanup error: " + ex.Message);
            }
        }
    }

    internal static class ProcessStartHelper
    {
        internal static void OpenFolder(string path)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            };
            System.Diagnostics.Process.Start(psi);
        }
    }
}
