# Third-party notices

Lexi Frames is MIT licensed (see `LICENSE`). It uses the following open-source
libraries, unmodified, from NuGet. Their licences allow redistribution; the full
licence texts are available at the linked projects.

| Library | Licence | Project |
|---|---|---|
| LibreHardwareMonitorLib | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| BlackSharp.Core | MPL-2.0 | https://github.com/Blacktempel/BlackSharp |
| DiskInfoToolkit | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |
| HidSharp | Apache-2.0 | https://software.seekye.com/hidsharp |
| Microsoft.Diagnostics.Tracing.TraceEvent | MIT | https://github.com/microsoft/perfview |
| Microsoft.Diagnostics.NETCore.Client | MIT | https://github.com/dotnet/diagnostics |
| Mono.Posix.NETStandard | MIT | https://github.com/mono/mono |
| .NET runtime & System.* / Microsoft.Extensions.* packages | MIT | https://github.com/dotnet/runtime |

**MPL-2.0 note:** the MPL-licensed libraries are used as-is. Their source code is
available from the project links above. Lexi Frames' own code is not affected by
the MPL and stays under the MIT licence.

**PawnIO** (the driver used for CPU temperature) is *not* bundled. Users install it
separately from https://pawnio.eu or with `winget install namazso.PawnIO`.
