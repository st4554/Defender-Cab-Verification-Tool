# Defender Cab Verification Tool

<img width="1024" height="467" alt="Screenshot 2026-09-29 164008" src="https://github.com/user-attachments/assets/139188a5-c962-4db8-83be-dd5cdfa1044a" />

A small Windows desktop utility that verifies the digital signatures of files contained inside Microsoft Defender CAB packages (files named like `defender-dism-*.cab`). The tool expands CAB archives, walks through the extracted files, and uses native WinTrust checks to report whether each file is signed and whether its signature is valid.

## Key Features

- Expand and verify contents of `defender-dism-*.cab` packages.
- Verify CABs found in the application folder or a user-selected folder.
- Open a single `.cab` via file picker and verify its contents.
- Per-file results shown in the UI and exportable to CSV.
- Generates timestamped logs in the `logs` folder.
- Added Website links for Microsoft Defender Anti-Virus Definitions*, Platform Updates and Security Center**.
- Now will only extract defender cabs, non-defender cabs will not be verified anymore.

*Will download the latest available definition exe file.

**x64 and x86 versions are only available, as for ARM64 users please use the x64 version to extract defender cab.

## Supported Architectures

This release includes packages and support for the following architectures:

- `x64`
- `x86`

Please note - ARM64 build has been deprecated!

Use the appropriate exe files for the target architecture.

## Usage (GUI)

1. Click `Verify File` to choose a single `.cab` to expand and verify.
3. Click `Export CSV` after a run to save results to a CSV file.
4. Click `Open Logs` to view verification and cleanup logs in the `logs` folder.

## Building

- Requires Visual Studio 2022 or 2026 and .NET Framework 4.8.1.
- Open the solution and build normally.

## Cleanup on Exit

The application removes extracted files (the `defender-dism` folder) on exit and writes a `cleanup-YYYYMMDD-HHMMSS.log` file into the `logs` folder describing actions taken.

## License

See the `LICENSE` file in the repository for license details.
