# Defender Cab Verification Tool v3.0.29644.0

A small Windows desktop utility that verifies the digital signatures of files contained inside Microsoft Defender CAB packages (files named like `defender-dism-*.cab`). The tool expands CAB archives, walks through the extracted files, and uses native WinTrust checks to report whether each file is signed and whether its signature is valid.

## Key Features

- Expand and verify contents of `defender-dism-*.cab` packages.
- Verify CABs found in the application folder or a user-selected folder.
- Open a single `.cab` via file picker and verify its contents.
- Per-file results shown in the UI and exportable to CSV.
- Generates timestamped logs in the `logs` folder.

## Supported Architectures

This release includes packages and support for the following architectures:

- `arm64`
- `x64`
- `x86`

Use the appropriate exe files for the target architecture.

## Usage (GUI)

1. Click `Verify` to select a folder that contains `defender-dism-*.cab` files and verify all supported CABs found there.
2. Click `Verify File` to choose a single `.cab` to expand and verify.
3. Click `Export CSV` after a run to save results to a CSV file.
4. Click `Open Logs` to view verification and cleanup logs in the `logs` folder.

## Building

- Requires Visual Studio 2022 or 2026 and .NET Framework 4.8.1.
- Open the solution and build normally.

## Cleanup on Exit

The application removes extracted files (the `defender-dism` folder) on exit and writes a `cleanup-YYYYMMDD-HHMMSS.log` file into the `logs` folder describing actions taken.

Bug Fixes for x86 and x64
1. Fixed an issue with launching as a non-admin, was leading to no defender cabs being extracted and verified.
2. Fixed an issue with both x64 and x86 not launching on Windows 10 v21H2 or above, had the wrong OS detection parameters.

Bug Fixes for arm64
1. Fixed an issue with launching as a non-admin, was leading to no defender cabs being extracted and verified.

See the `LICENSE` file in the repository for license details.
