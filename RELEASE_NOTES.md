# Release Notes

This document summarizes changes that affect operators, workstation compatibility, deployment reliability, and diagnostics.

## v1.0.3 — Current

### Field workstation compatibility

- Improved automatic hardware detection across Windows 10/11 workstations with different COM and USB-NCM assignments.
- USB-NCM adapters are tracked by persistent PnP identity rather than mutable display name.
- Newly created adapters, verified PnP identities, and the current owner of the target IP are prioritized.
- Administrator state, selected COM port, USB-NCM IPv4 state, HTTP port, and route evidence are recorded as checkpoints.
- Alternative HTTP-port attempts and firewall diagnostics were strengthened.

### Easy Installer and package discovery

- TEZI feed addresses are generated as session-specific absolute URLs.
- Manifest responses use HTTP headers that prevent stale caching.
- Controlled feed refresh is attempted when Easy Installer discovers the feed but does not request the image manifest.
- Unsupported serial commands that return `ERR FORMAT` are not repeated; the supported Zeroconf workflow is used instead.
- HTTP requests, payload transfer evidence, and target shutdown are evaluated independently.

### OTG and recovery reliability

- Stale UUU processes are terminated after USB-device timeout.
- Operators receive separate guidance for OTG removal and reconnection.
- A new recovery attempt does not begin until normal FCC startup without OTG is confirmed through the serial console.
- Windows USB devices are rescanned and a fresh UUU session is created for each retry.

### Post-install verification

- The Power OFF → Recovery NORMAL → Power ON sequence is verified through readback.
- Power-off discharge timing was extended to improve repeated FCC startup reliability.
- If no serial data appears during normal startup, the COM port is refreshed and the FCC is restarted once in Recovery NORMAL.
- Expected and reported OFP versions are compared.
- Physical `eth0 Link is Up` evidence is reported separately from OFP verification.
- Physical Ethernet failure evidence such as `Unable to connect to phy` is preserved in the detailed log.

### User interface and operability

- Prevented the window from disappearing from the taskbar and the process from remaining active after exit.
- A second application instance brings the existing window to the foreground.
- Buttons are disabled during shutdown instead of fading the entire window.
- High-volume console updates are rendered in batches while detailed file logging remains active.
- Low-level serial diagnostic tables are filtered from the operator console and retained only in the detailed log.

## v1.0.2

- Added Turkish and English user-interface support.
- Localized the main screen, settings, warnings, and result dialogs.
- Added the FCC1–FCC6 target table with single-target selection.
- Added automatic discovery of the desktop version repository and platform folders.
- Added natural version sorting with automatic selection of the newest package.
- Added operator guidance and controlled retries when OTG is not detected.
- Added Recovery NORMAL, Power ON, and OFP verification after successful installation.
- Moved console and log rendering to background batches for improved UI responsiveness.

## v1.0.1

- Improved USB-NCM adapter and HTTP-server selection across field workstations.
- Added alternative HTTP ports.
- Added detailed diagnostics for administrator privileges, Windows Defender Firewall, and network-adapter issues.
- Published application file and product version v1.0.1.

## v1.0.0

- First stable release supporting TEZI package transfer over the network.
- Combined Moxa Power/Recovery control, serial monitoring, USB-NCM, local HTTP, mDNS, and OFP verification in one workflow.
- Added platform profiles, version selection, and FCC target configuration.

## Release policy

- Source code and runnable field packages are maintained separately.
- Runnable packages are published through GitHub Releases.
- Every field package is verified using SHA-256.
- Real OFP/TEZI packages, logs, user-specific settings, passwords, and sensitive field data are excluded from Git history.
- Application version, Git tag, and Release title use the same version number.