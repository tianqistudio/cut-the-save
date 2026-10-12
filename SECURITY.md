# Security policy

## Supported scope

Security reports are welcome for the current Windows `win-x64` build and the source code in this repository. Do not include private save files, Steam credentials, full usernames, or private paths in a report.

## High-risk areas

- Save parsing and atomic replacement.
- Backup and restore path handling.
- RTSS DLL loading and Profile writes.
- The RTSS elevation boundary: helper mode accepts only `apply` or `undo`, redetects the same installed RTSS path after elevation, never targets Global Profile, and records rollback state before reporting success.
- Any future update manifest, signature, or downloader.

## Reporting

Until a public security contact is configured, report issues privately to the project maintainer rather than publishing an exploit or attaching a private `ctr_save.bin`. Include the application version, Windows version, reproduction steps, and sanitized logs. The application should remain usable in offline mode when update or guide sources are unavailable.
