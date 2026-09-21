using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Defender_Cab_Verification_Tool
{
    public partial class MainForm : Form
    {
        private const int MinSupportedBuild = 19044; // Windows 10 v21H2 build
        private CancellationTokenSource _cts;
        private volatile bool _isCleaning;
        private volatile bool _allowClose;

        public MainForm()
        {
            InitializeComponent();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            try
            {
                var build = GetWindowsBuildNumber();
                if (build < MinSupportedBuild)
                {
                    var msg =
                        $"Defender Cab Verification Tool requires x64 version of Windows 10 version 21H2 (build {MinSupportedBuild}) or later.\r\n" +
                        $"Detected OS build: {build}. The application will now exit.";
                    MessageBox.Show(this, msg, "Unsupported OS", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close();
                    return;
                }
            }
            catch
            {
                // If detection fails, be conservative and allow startup,
                // or optionally show a warning. Here we allow startup.
            }
        }

        // P/Invoke to get accurate Windows version/build (avoids manifest-dependent Environment.OSVersion issues)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OSVERSIONINFOEX
        {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int RtlGetVersion(ref OSVERSIONINFOEX versionInfo);

        private static int GetWindowsBuildNumber()
        {
            try
            {
                var os = new OSVERSIONINFOEX();
                os.dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFOEX));
                if (RtlGetVersion(ref os) == 0)
                {
                    return os.dwBuildNumber;
                }
            }
            catch
            {
                // fall through to fallback
            }

            // Fallback (may be unreliable without proper app manifest)
            try
            {
                return Environment.OSVersion.Version.Build;
            }
            catch
            {
                return 0;
            }
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

        private void antiVirusDefinitionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Open the Microsoft Defender Antivirus definitions download page in the default browser
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://go.microsoft.com/fwlink/?LinkID=121721&arch=x64",
                UseShellExecute = true
            });
        }

        private void platformUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Open the Microsoft Defender platform updates download page in the default browser
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://www.catalog.update.microsoft.com/Search.aspx?q=KB4052623",
                UseShellExecute = true
            });
        }

        private void securityCenterUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Open the Microsoft Defender Security Center updates download page in the default browser
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://www.catalog.update.microsoft.com/Search.aspx?q=KB5007651",
                UseShellExecute = true
            });
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
