using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Defender_Cab_Verification_Tool
{
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

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                CancellationToken = cancellationToken
            };

            WriteLog($"Files to verify: {total}");

            try
            {
                Parallel.ForEach(filtered, parallelOptions, file =>
                {
                    if (parallelOptions.CancellationToken.IsCancellationRequested) return;

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

                        // notify UI (ReportFileResult will marshal to UI if needed)
                        fileCallback(result);

                        // log
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
                    finally
                    {
                        var done = Interlocked.Increment(ref processed);
                        progress?.Report((int)((done / (double)System.Math.Max(total, 1)) * 100));
                    }
                });
            }
            catch (OperationCanceledException)
            {
                WriteLog("Operation cancelled by user.");
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

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                CancellationToken = cancellationToken
            };

            WriteLog($"Files to verify: {total}");

            try
            {
                Parallel.ForEach(filtered, parallelOptions, file =>
                {
                    if (parallelOptions.CancellationToken.IsCancellationRequested) return;

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
                    finally
                    {
                        var done = Interlocked.Increment(ref processed);
                        progress?.Report((int)((done / (double)System.Math.Max(total, 1)) * 100));
                    }
                });
            }
            catch (OperationCanceledException)
            {
                WriteLog("Operation cancelled by user.");
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

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                CancellationToken = cancellationToken
            };

            WriteLog($"Files to verify: {total}");

            try
            {
                Parallel.ForEach(filtered, parallelOptions, file =>
                {
                    if (parallelOptions.CancellationToken.IsCancellationRequested) return;

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
                    finally
                    {
                        var done = Interlocked.Increment(ref processed);
                        progress?.Report((int)((done / (double)System.Math.Max(total, 1)) * 100));
                    }
                });
            }
            catch (OperationCanceledException)
            {
                WriteLog("Operation cancelled by user.");
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

        /// <summary>
        /// Try to enumerate CAB entries using the managed Microsoft.Deployment.Compression.Cab API via reflection.
        /// Returns true and populates entries if the managed API is available and enumeration succeeded.
        /// </summary>
        private static bool TryManagedList(string cabFilePath, out List<string> entries, Action<string> log = null)
        {
            entries = null;
            try
            {
                var cabType = Type.GetType("Microsoft.Deployment.Compression.Cab.CabInfo, Microsoft.Deployment.Compression.Cab");
                if (cabType == null) return false;

                var ctor = cabType.GetConstructor(new[] { typeof(string) });
                if (ctor == null) return false;

                var cabInstance = ctor.Invoke(new object[] { cabFilePath });
                if (cabInstance == null) return false;

                // Prefer instance methods that take no parameters and return some enumerable
                var methods = cabType.GetMethods(BindingFlags.Instance | BindingFlags.Public);
                foreach (var m in methods)
                {
                    if (!string.Equals(m.Name, "GetFiles", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(m.Name, "GetFileNames", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(m.Name, "GetEntries", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(m.Name, "GetEnumerator", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(m.Name, "Files", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(m.Name, "Entries", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var pars = m.GetParameters();
                    if (pars.Length != 0) continue;

                    var result = m.Invoke(cabInstance, null);
                    if (result == null) continue;

                    entries = new List<string>();

                    if (result is string[] arr)
                    {
                        entries.AddRange(arr);
                        log?.Invoke($"Managed list: method {m.Name} returned {arr.Length} string entries.");
                        return true;
                    }

                    if (result is IEnumerable<string> se)
                    {
                        entries.AddRange(se);
                        log?.Invoke($"Managed list: method {m.Name} returned IEnumerable<string> ({entries.Count} entries).");
                        return true;
                    }

                    if (result is System.Collections.IEnumerable ie)
                    {
                        foreach (var o in ie)
                        {
                            if (o == null) continue;
                            if (o is string s) entries.Add(s);
                            else
                            {
                                // Inspect common name properties on the entry object
                                var props = o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                                string candidate = null;
                                foreach (var p in props)
                                {
                                    if (string.Equals(p.Name, "Name", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(p.Name, "FileName", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(p.Name, "EntryName", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(p.Name, "SourceName", StringComparison.OrdinalIgnoreCase))
                                    {
                                        try
                                        {
                                            var val = p.GetValue(o) as string;
                                            if (!string.IsNullOrEmpty(val)) { candidate = val; break; }
                                        }
                                        catch { }
                                    }
                                }
                                entries.Add(candidate ?? o.ToString());
                            }
                        }
                        log?.Invoke($"Managed list: method {m.Name} returned IEnumerable objects ({entries.Count} entries).");
                        return true;
                    }
                }

                // Try property-based enumeration (some libs expose Files/Entries as properties)
                var propsList = cabType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var p in propsList)
                {
                    if (!string.Equals(p.Name, "Files", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(p.Name, "Entries", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var result = p.GetValue(cabInstance);
                    if (result == null) continue;

                    entries = new List<string>();

                    if (result is string[] arrp)
                    {
                        entries.AddRange(arrp);
                        log?.Invoke($"Managed list: property {p.Name} returned {arrp.Length} string entries.");
                        return true;
                    }

                    if (result is IEnumerable<string> sep)
                    {
                        entries.AddRange(sep);
                        log?.Invoke($"Managed list: property {p.Name} returned IEnumerable<string> ({entries.Count} entries).");
                        return true;
                    }

                    if (result is System.Collections.IEnumerable iep)
                    {
                        foreach (var o in iep)
                        {
                            if (o == null) continue;
                            if (o is string s) entries.Add(s);
                            else
                            {
                                var props = o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                                string candidate = null;
                                foreach (var pp in props)
                                {
                                    if (string.Equals(pp.Name, "Name", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(pp.Name, "FileName", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(pp.Name, "EntryName", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(pp.Name, "SourceName", StringComparison.OrdinalIgnoreCase))
                                    {
                                        try
                                        {
                                            var val = pp.GetValue(o) as string;
                                            if (!string.IsNullOrEmpty(val)) { candidate = val; break; }
                                        }
                                        catch { }
                                    }
                                }
                                entries.Add(candidate ?? o.ToString());
                            }
                        }
                        log?.Invoke($"Managed list: property {p.Name} returned IEnumerable objects ({entries.Count} entries).");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Managed listing failed for {Path.GetFileName(cabFilePath)}: {ex.Message}");
                return false;
            }
        }

        private static bool TryManagedExtractSelective(string cabFilePath, string destinationFolder, Action<string> log)
        {
            try
            {
                if (!TryManagedList(cabFilePath, out var entries, log))
                {
                    // Managed API not available or couldn't enumerate
                    return false;
                }

                if (entries != null)
                {
                    log?.Invoke($"Managed entries count: {entries.Count}. Sample: {string.Join(", ", entries.Take(5))}");
                }

                // Look for package-defender.xml anywhere in the path strings
                bool hasPackageDefender = entries != null && entries.Any(e => e != null && e.IndexOf("package-defender.xml", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!hasPackageDefender)
                {
                    log?.Invoke($"Skipping non-Defender CAB (managed): {Path.GetFileName(cabFilePath)} - 'package-defender.xml' not found in entries.");
                    return true; // managed path handled the decision (skip)
                }

                // Build set of entries to extract (match extensions or exact filename anywhere in path)
                var allowedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "package-defender.xml" };
                var allowedExtensions = new HashSet<string>(ExtensionsFilter, StringComparer.OrdinalIgnoreCase);

                var toExtract = new List<string>();
                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry)) continue;
                    var fileName = Path.GetFileName(entry);
                    var ext = Path.GetExtension(fileName) ?? string.Empty;
                    if (allowedExtensions.Contains(ext) || allowedNames.Contains(fileName) || entry.IndexOf("package-defender.xml", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        toExtract.Add(entry);
                    }
                }

                if (toExtract.Count == 0)
                {
                    log?.Invoke($"No allowed entries found inside Defender CAB (managed) {Path.GetFileName(cabFilePath)}.");
                    return true;
                }

                // Create destination folder
                Directory.CreateDirectory(destinationFolder);

                var cabType = Type.GetType("Microsoft.Deployment.Compression.Cab.CabInfo, Microsoft.Deployment.Compression.Cab");
                if (cabType == null) return false;
                var ctor = cabType.GetConstructor(new[] { typeof(string) });
                if (ctor == null) return false;
                var cabInstance = ctor.Invoke(new object[] { cabFilePath });
                if (cabInstance == null) return false;

                // Find an extract method that accepts (string entryName, string destinationFile)
                MethodInfo extractMethod = null;
                foreach (var mi in cabType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!mi.Name.StartsWith("Extract", StringComparison.OrdinalIgnoreCase)) continue;
                    var pars = mi.GetParameters();
                    if (pars.Length == 2 && pars[0].ParameterType == typeof(string) && pars[1].ParameterType == typeof(string))
                    {
                        extractMethod = mi;
                        break;
                    }
                }

                if (extractMethod == null)
                {
                    log?.Invoke("Managed Cab API present but no per-file extract method found; falling back to expand.exe.");
                    return false;
                }

                // Extract each selected entry
                foreach (var entry in toExtract)
                {
                    var relativePath = entry.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                    var destFilePath = Path.Combine(destinationFolder, relativePath);
                    var destFolder = Path.GetDirectoryName(destFilePath);
                    if (!string.IsNullOrEmpty(destFolder) && !Directory.Exists(destFolder))
                        Directory.CreateDirectory(destFolder);

                    try
                    {
                        extractMethod.Invoke(cabInstance, new object[] { entry, destFilePath });
                        log?.Invoke($"Managed extracted: {entry} -> {destFilePath}");
                    }
                    catch (TargetInvocationException tie)
                    {
                        log?.Invoke($"Managed extraction failed for {entry}: {tie.InnerException?.Message ?? tie.Message}");
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"Managed extraction failed for {entry}: {ex.Message}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Managed extraction attempt failed for {Path.GetFileName(cabFilePath)}: {ex.Message}");
                return false;
            }
        }

        private static void ExpandCab(string cabFilePath, string destinationFolder, Action<string> log)
        {
            // First attempt: managed selective extraction (if available)
            try
            {
                var managedHandled = TryManagedExtractSelective(cabFilePath, destinationFolder, log);
                if (managedHandled)
                {
                    // Managed API either extracted selected files or decided to skip non-defender CAB
                    return;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Managed extraction path failed: {ex.Message}");
                // fall through to expand.exe fallback
            }

            // Confirm it's a defender CAB using IsDefenderCab (which will use managed listing if possible, else expand -D)
            if (!IsDefenderCab(cabFilePath, log))
            {
                log?.Invoke($"Skipping non-Defender CAB: {Path.GetFileName(cabFilePath)} - 'package-defender.xml' not found.");
                return;
            }

            // Create destination folder
            try
            {
                Directory.CreateDirectory(destinationFolder);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Failed to create destination folder {destinationFolder}: {ex.Message}");
                return;
            }

            // First extract the package-defender.xml explicitly (single file call)
            var extractSingle = new Func<string, bool>((entryName) =>
            {
                var args = $"-F:{entryName} \"{cabFilePath}\" \"{destinationFolder}\"";
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "expand",
                        Arguments = args,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var p = new Process { StartInfo = psi })
                    {
                        var stdout = new StringBuilder();
                        var stderr = new StringBuilder();
                        p.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) stdout.AppendLine(e.Data); };
                        p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) stderr.AppendLine(e.Data); };
                        p.Start();
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                        p.WaitForExit();

                        log?.Invoke(stdout.ToString().Trim());
                        if (p.ExitCode == 0)
                        {
                            log?.Invoke($"expand extracted {entryName} from {Path.GetFileName(cabFilePath)}.");
                            return true;
                        }
                        else
                        {
                            log?.Invoke($"expand.exe error extracting {entryName} from {Path.GetFileName(cabFilePath)}: {stderr.ToString().Trim()} (exit {p.ExitCode})");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Exception running expand for {entryName}: {ex.Message}");
                    return false;
                }
            });

            // Try extract package-defender.xml first
            bool gotPackageXml = extractSingle("package-defender.xml");

            // If package-defender.xml wasn't extracted, still attempt to continue — but log and proceed to try other allowed patterns if you want
            if (!gotPackageXml)
            {
                log?.Invoke($"Warning: package-defender.xml extraction returned non-zero for {Path.GetFileName(cabFilePath)}.");
                // continue - maybe other files will extract or expand implementation behaves differently
            }

            // Now extract allowed extensions one-by-one to reduce expand.exe argument complexity and avoid ambiguous failures.
            foreach (var ext in ExtensionsFilter)
            {
                if (string.IsNullOrWhiteSpace(ext)) continue;
                var pattern = ext.StartsWith(".") ? $"*{ext}" : $"*{ext}";
                var args = $"-F:{pattern} \"{cabFilePath}\" \"{destinationFolder}\"";
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "expand",
                        Arguments = args,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var p = new Process { StartInfo = psi })
                    {
                        var stdout = new StringBuilder();
                        var stderr = new StringBuilder();
                        p.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) stdout.AppendLine(e.Data); };
                        p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) stderr.AppendLine(e.Data); };
                        p.Start();
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                        p.WaitForExit();

                        if (stdout.Length > 0) log?.Invoke(stdout.ToString().Trim());
                        if (p.ExitCode == 0)
                        {
                            log?.Invoke($"expand extracted pattern {pattern} from {Path.GetFileName(cabFilePath)}.");
                        }
                        else
                        {
                            log?.Invoke($"expand.exe returned {p.ExitCode} for pattern {pattern} on {Path.GetFileName(cabFilePath)}: {stderr.ToString().Trim()}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Failed expanding {cabFilePath} for pattern {pattern}: {ex.Message}");
                }
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

        private static bool IsDefenderCab(string cabFilePath, Action<string> log)
        {
            // Example implementation: check if the file name contains "defender-dism"
            if (string.IsNullOrEmpty(cabFilePath))
            {
                log?.Invoke("CAB file path is null or empty.");
                return false;
            }

            string fileName = Path.GetFileName(cabFilePath);
            bool isDefender = fileName != null && fileName.StartsWith("defender-dism", StringComparison.OrdinalIgnoreCase);
            if (!isDefender)
            {
                log?.Invoke($"File {fileName} is not recognized as a Defender CAB.");
            }
            return isDefender;
        }
    }
}