using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace Defender_Cab_Verification_Tool
{
    public class FileVerificationResult
    {
        public string FilePath { get; set; }
        public bool IsValid { get; set; }
        public string Thumbprint { get; set; }
        public string ErrorDetail { get; set; }
    }

    public static class DefenderCabVerifier
    {
        private static readonly string[] ExtensionsFilter = new[]
        {
            ".exe", ".dll", ".mui", ".sys", ".ax", ".ocx", ".cpl", ".scr",
            ".msu", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle",
            ".cab", ".cat", ".cdxml", ".ps1xml", ".psd1", ".psm1"
        };

        private static readonly string LogsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        // WinTrust constants/types for native signature verification
        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        private const uint ERROR_SUCCESS = 0x00000000;
        private const uint WTD_UI_NONE = 2;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_REVOCATION_CHECK_NONE = 0x00000010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile; // pointer to WINTRUST_FILE_INFO
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);

        /// <summary>
        /// Verifies CABs in application folder. Reports per-file results via fileCallback, progress via progress,
        /// and writes a timestamped log file to the `logs` folder. Can be cancelled with CancellationToken.
        /// </summary>
        /// <param name="fileCallback">Called for every file processed.</param>
        /// <param name="progress">Reports 0..100 progress percent.</param>
        /// <param name="logLine">General log lines (status messages) will be sent here and also written to disk.</param>
        /// <param name="cancellationToken">Cancel the run.</param>
        public static void VerifyAll(Action<FileVerificationResult> fileCallback, IProgress<int> progress, Action<string> logLine, CancellationToken cancellationToken)
        {
            if (fileCallback == null) throw new ArgumentNullException(nameof(fileCallback));
            if (logLine == null) throw new ArgumentNullException(nameof(logLine));

            Directory.CreateDirectory(LogsFolder);
            string logfile = Path.Combine(LogsFolder, $"verify-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            void WriteLog(string s)
            {
                try
                {
                    File.AppendAllText(logfile, s + System.Environment.NewLine, Encoding.UTF8);
                }
                catch { /* best effort */ }
                logLine(s);
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            try { Directory.SetCurrentDirectory(baseDir); } catch { /* ignore */ }

            WriteLog("Starting Defender CAB verification: " + System.DateTime.Now.ToString("u"));

            var x86 = GetLatestCab("defender-dism-x86*.cab");
            var x64 = GetLatestCab("defender-dism-x64*.cab");
            var arm64 = GetLatestCab("defender-dism-arm64*.cab");

            string root = Path.Combine(baseDir, "defender-dism");
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (System.Exception ex) { WriteLog("Warning: could not remove existing root folder: " + ex.Message); }
            Directory.CreateDirectory(root);

            if (x86 != null) { Directory.CreateDirectory(Path.Combine(root, "x86")); ExpandCab(x86, Path.Combine(root, "x86"), WriteLog); }
            if (x64 != null) { Directory.CreateDirectory(Path.Combine(root, "x64")); ExpandCab(x64, Path.Combine(root, "x64"), WriteLog); }
            if (arm64 != null) { Directory.CreateDirectory(Path.Combine(root, "arm64")); ExpandCab(arm64, Path.Combine(root, "arm64"), WriteLog); }

            var filesList = new List<string>();
            if (Directory.Exists(root))
            {
                filesList.AddRange(Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories));
            }

            var filtered = filesList.Where(f => ExtensionsFilter.Contains(Path.GetExtension(f) ?? string.Empty, System.StringComparer.OrdinalIgnoreCase)).ToList();
            int total = filtered.Count;
            int processed = 0;
            progress?.Report(0);

            WriteLog($"Files to verify: {total}");

            foreach (var file in filtered)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    WriteLog("Operation cancelled by user.");
                    break;
                }

                try
                {
                    var ok = VerifySignatureNative(file, out string thumbprint, out string errorDetail);
                    var result = new FileVerificationResult
                    {
                        FilePath = file,
                        IsValid = ok,
                        Thumbprint = thumbprint,
                        ErrorDetail = errorDetail
                    };

                    // notify UI
                    fileCallback(result);

                    // log line
                    if (ok)
                    {
                        WriteLog($"Valid   {file}");
                        if (!string.IsNullOrEmpty(thumbprint)) WriteLog($"Signer Thumbprint  {thumbprint}");
                    }
                    else
                    {
                        var fi = new FileInfo(file);
                        WriteLog($"Invalid   {file}");
                        WriteLog($"Modified  {fi.LastWriteTime}  Size  {fi.Length}");
                        if (!string.IsNullOrEmpty(errorDetail)) WriteLog($"Detail: {errorDetail}");
                    }
                }
                catch (System.Exception ex)
                {
                    var res = new FileVerificationResult
                    {
                        FilePath = file,
                        IsValid = false,
                        Thumbprint = null,
                        ErrorDetail = "Exception: " + ex.Message
                    };
                    fileCallback(res);
                    WriteLog($"Error processing {file}: {ex.Message}");
                }

                processed++;
                progress?.Report((int)((processed / (double)System.Math.Max(total, 1)) * 100));
            }

            WriteLog("Finished verification: " + System.DateTime.Now.ToString("u"));
            WriteLog("Log saved to: " + logfile);
        }

        /// <summary>
        /// Verifies CABs found in the provided sourceFolder. Behavior mirrors VerifyAll but searches inside sourceFolder
        /// for defender-dism-*.cab files to expand and process.
        /// </summary>
        public static void VerifyAll(string sourceFolder, Action<FileVerificationResult> fileCallback, IProgress<int> progress, Action<string> logLine, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(sourceFolder)) throw new System.ArgumentNullException(nameof(sourceFolder));
            if (fileCallback == null) throw new System.ArgumentNullException(nameof(fileCallback));
            if (logLine == null) throw new System.ArgumentNullException(nameof(logLine));

            Directory.CreateDirectory(LogsFolder);
            string logfile = Path.Combine(LogsFolder, $"verify-{System.DateTime.Now:yyyyMMdd-HHmmss}.log");

            void WriteLog(string s)
            {
                try
                {
                    File.AppendAllText(logfile, s + System.Environment.NewLine, Encoding.UTF8);
                }
                catch { /* best effort */ }
                logLine(s);
            }

            WriteLog("Starting Defender CAB verification (user folder): " + System.DateTime.Now.ToString("u"));

            var x86 = GetLatestCabInFolder(sourceFolder, "defender-dism-x86*.cab");
            var x64 = GetLatestCabInFolder(sourceFolder, "defender-dism-x64*.cab");
            var arm64 = GetLatestCabInFolder(sourceFolder, "defender-dism-arm64*.cab");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string root = Path.Combine(baseDir, "defender-dism");
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (System.Exception ex) { WriteLog("Warning: could not remove existing root folder: " + ex.Message); }
            Directory.CreateDirectory(root);

            if (x86 != null) { Directory.CreateDirectory(Path.Combine(root, "x86")); ExpandCab(x86, Path.Combine(root, "x86"), WriteLog); }
            if (x64 != null) { Directory.CreateDirectory(Path.Combine(root, "x64")); ExpandCab(x64, Path.Combine(root, "x64"), WriteLog); }
            if (arm64 != null) { Directory.CreateDirectory(Path.Combine(root, "arm64")); ExpandCab(arm64, Path.Combine(root, "arm64"), WriteLog); }

            var filesList = new List<string>();
            if (Directory.Exists(root))
            {
                filesList.AddRange(Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories));
            }

            var filtered = filesList.Where(f => ExtensionsFilter.Contains(Path.GetExtension(f) ?? string.Empty, System.StringComparer.OrdinalIgnoreCase)).ToList();
            int total = filtered.Count;
            int processed = 0;
            progress?.Report(0);

            WriteLog($"Files to verify: {total}");

            foreach (var file in filtered)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    WriteLog("Operation cancelled by user.");
                    break;
                }

                try
                {
                    var ok = VerifySignatureNative(file, out string thumbprint, out string errorDetail);
                    var result = new FileVerificationResult
                    {
                        FilePath = file,
                        IsValid = ok,
                        Thumbprint = thumbprint,
                        ErrorDetail = errorDetail
                    };

                    fileCallback(result);

                    if (ok)
                    {
                        WriteLog($"Valid   {file}");
                        if (!string.IsNullOrEmpty(thumbprint)) WriteLog($"Signer Thumbprint  {thumbprint}");
                    }
                    else
                    {
                        var fi = new FileInfo(file);
                        WriteLog($"Invalid   {file}");
                        WriteLog($"Modified  {fi.LastWriteTime}  Size  {fi.Length}");
                        if (!string.IsNullOrEmpty(errorDetail)) WriteLog($"Detail: {errorDetail}");
                    }
                }
                catch (System.Exception ex)
                {
                    var res = new FileVerificationResult
                    {
                        FilePath = file,
                        IsValid = false,
                        Thumbprint = null,
                        ErrorDetail = "Exception: " + ex.Message
                    };
                    fileCallback(res);
                    WriteLog($"Error processing {file}: {ex.Message}");
                }

                processed++;
                progress?.Report((int)((processed / (double)System.Math.Max(total, 1)) * 100));
            }

            WriteLog("Finished verification: " + System.DateTime.Now.ToString("u"));
            WriteLog("Log saved to: " + logfile);
        }

        public static void VerifyCab(string cabFile, Action<FileVerificationResult> fileCallback, IProgress<int> progress, Action<string> logLine, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(cabFile)) throw new System.ArgumentNullException(nameof(cabFile));
            if (!File.Exists(cabFile)) throw new FileNotFoundException("CAB file not found", cabFile);
            if (fileCallback == null) throw new System.ArgumentNullException(nameof(fileCallback));
            if (logLine == null) throw new System.ArgumentNullException(nameof(logLine));

            Directory.CreateDirectory(LogsFolder);
            string logfile = Path.Combine(LogsFolder, $"verify-{System.DateTime.Now:yyyyMMdd-HHmmss}.log");

            void WriteLog(string s)
            {
                try { File.AppendAllText(logfile, s + System.Environment.NewLine, Encoding.UTF8); } catch { }
                logLine(s);
            }

            WriteLog("Starting Defender CAB verification (single CAB): " + System.DateTime.Now.ToString("u"));

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string root = Path.Combine(baseDir, "defender-dism");
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (System.Exception ex) { WriteLog("Warning: could not remove existing root folder: " + ex.Message); }
            Directory.CreateDirectory(root);

            // extract selected CAB into a dedicated folder
            var targetFolder = Path.Combine(root, "selected");
            try { if (Directory.Exists(targetFolder)) Directory.Delete(targetFolder, true); } catch { }
            Directory.CreateDirectory(targetFolder);
            ExpandCab(cabFile, targetFolder, WriteLog);

            var filesList = new List<string>();
            filesList.AddRange(Directory.EnumerateFiles(targetFolder, "*.*", SearchOption.AllDirectories));

            var filtered = filesList.Where(f => ExtensionsFilter.Contains(Path.GetExtension(f) ?? string.Empty, System.StringComparer.OrdinalIgnoreCase)).ToList();
            int total = filtered.Count;
            int processed = 0;
            progress?.Report(0);

            WriteLog($"Files to verify: {total}");

            foreach (var file in filtered)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    WriteLog("Operation cancelled by user.");
                    break;
                }

                try
                {
                    var ok = VerifySignatureNative(file, out string thumbprint, out string errorDetail);
                    var result = new FileVerificationResult
                    {
                        FilePath = file,
                        IsValid = ok,
                        Thumbprint = thumbprint,
                        ErrorDetail = errorDetail
                    };

                    fileCallback(result);

                    if (ok)
                    {
                        WriteLog($"Valid   {file}");
                        if (!string.IsNullOrEmpty(thumbprint)) WriteLog($"Signer Thumbprint  {thumbprint}");
                    }
                    else
                    {
                        var fi = new FileInfo(file);
                        WriteLog($"Invalid   {file}");
                        WriteLog($"Modified  {fi.LastWriteTime}  Size  {fi.Length}");
                        if (!string.IsNullOrEmpty(errorDetail)) WriteLog($"Detail: {errorDetail}");
                    }
                }
                catch (System.Exception ex)
                {
                    var res = new FileVerificationResult
                    {
                        FilePath = file,
                        IsValid = false,
                        Thumbprint = null,
                        ErrorDetail = "Exception: " + ex.Message
                    };
                    fileCallback(res);
                    WriteLog($"Error processing {file}: {ex.Message}");
                }

                processed++;
                progress?.Report((int)((processed / (double)System.Math.Max(total, 1)) * 100));
            }

            WriteLog("Finished verification: " + System.DateTime.Now.ToString("u"));
            WriteLog("Log saved to: " + logfile);
        }

        private static string GetLatestCab(string pattern)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var files = Directory.GetFiles(baseDir, pattern, SearchOption.TopDirectoryOnly);
                if (files == null || files.Length == 0) return null;
                return files.OrderBy(f => File.GetCreationTimeUtc(f)).LastOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static string GetLatestCabInFolder(string folder, string pattern)
        {
            try
            {
                var files = Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly);
                if (files == null || files.Length == 0) return null;
                return files.OrderBy(f => File.GetCreationTimeUtc(f)).LastOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static void ExpandCab(string cabFilePath, string destinationFolder, Action<string> log)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "expand",
                    Arguments = $"-R \"{cabFilePath}\" -F:* \"{destinationFolder}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        var stdout = p.StandardOutput.ReadToEnd();
                        var stderr = p.StandardError.ReadToEnd();
                        p.WaitForExit();
                        if (!string.IsNullOrEmpty(stdout)) log(stdout.Trim());
                        if (!string.IsNullOrEmpty(stderr)) log("expand.exe error: " + stderr.Trim());
                    }
                }
            }
            catch (System.Exception ex)
            {
                log("Failed expanding " + cabFilePath + ": " + ex.Message);
            }
        }

        private static bool VerifySignatureNative(string filePath, out string thumbprint, out string errorDetail)
        {
            thumbprint = null;
            errorDetail = null;

            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                pcwszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            IntPtr pFileInfo = IntPtr.Zero;
            var wtData = new WINTRUST_DATA();
            try
            {
                pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                Marshal.StructureToPtr(fileInfo, pFileInfo, false);

                wtData.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
                wtData.pPolicyCallbackData = IntPtr.Zero;
                wtData.pSIPClientData = IntPtr.Zero;
                wtData.dwUIChoice = WTD_UI_NONE;
                wtData.fdwRevocationChecks = 0;
                wtData.dwUnionChoice = WTD_CHOICE_FILE;
                wtData.pFile = pFileInfo;
                wtData.dwStateAction = 0;
                wtData.hWVTStateData = IntPtr.Zero;
                wtData.pwszURLReference = null;
                wtData.dwProvFlags = WTD_REVOCATION_CHECK_NONE;
                wtData.dwUIContext = 0;

                uint result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref wtData);
                if (result == ERROR_SUCCESS)
                {
                    try
                    {
                        var cert = X509Certificate.CreateFromSignedFile(filePath);
                        thumbprint = new X509Certificate2(cert).Thumbprint;
                    }
                    catch { thumbprint = null; }
                    return true;
                }
                else
                {
                    errorDetail = $"WinVerifyTrust returned 0x{result:X8}";
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                errorDetail = ex.Message;
                return false;
            }
            finally
            {
                if (pFileInfo != IntPtr.Zero) Marshal.FreeHGlobal(pFileInfo);
            }
        }
    }
}