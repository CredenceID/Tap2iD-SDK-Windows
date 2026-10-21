# Tap2iD-SDK-Windows

<img width="1187" height="297" alt="sdk windows" src="https://github.com/user-attachments/assets/61ab5654-5475-4bc9-b1f0-d39da59b4dce" />

**Credence ID’s Tap2iD SDK** is a standards-based digital identity verification SDK within the Verify with Credence (VwC) platform. It enables secure verification of multiple ISO-compliant mobile credentials, including:

Mobile Driving Licenses (mDL) – ISO/IEC 18013.1

Photo ID mDocs – ISO/IEC 23220.1

EU Digital Identity Personal Identification (EUDI PID) - eu.europa.ec.eudi.pid.1

Available for Android, iOS, and Windows desktop, the SDK allows seamless integration into existing applications, providing interoperable, multi-document verification capabilities. Tap2iD SDK abstracts the complexity of digital identity protocols and cryptographic validation, delivering a unified and scalable verification solution for modern digital credential ecosystems.

---

## Pre-requisites

### Complete your Verify with Credence portal registration and generate the SDK license key required to begin integration
- Follow the [Prerequisite: Verify with Credence Portal Registration & Key Generation](https://github.com/CredenceID/Tap2iD-SDK-Windows/wiki/Prerequisite:-Verify-with-Credence-Portal-Registration-&-Key-Generation) to get started.


## Requirements
- Windows 11 (build 22000) or later. Windows 10 is not supported from SDK 2.3.0.
- Visual Studio 2022 with the .NET 8 SDK and the Windows App SDK (WinUI 3) workload.
- The Tap2iD NuGet feed added as a package source: see the [Integration Guide](https://github.com/CredenceID/Tap2iD-SDK-Windows/wiki/Guide-to-Integrate-Tap2iD-Windows-SDK). Apps target `net8.0-windows10.0.22000.0` with `<WindowsSdkPackageVersion>10.0.22000.57</WindowsSdkPackageVersion>`.

## Running the sample
1. Open `Tap2iDSampleWinUI.sln` and select the x64 platform.
2. Enable Developer Mode in Windows Settings, then run with F5. The local build is not signed, which Visual Studio accepts for debugging.
3. Paste your SDK license key in the app, or save it in `Documents\Tap2iD\license.txt`, then initialize the SDK.

To install the package on another PC, sign the `.msix` with your own certificate and set the `Publisher` in `Package.appxmanifest` to that certificate's subject.

## Supported Hardware
For proper functionality, the Tap2iD SDK for Windows requires compatible hardware for its different data capture methods.

### Bluetooth (BLE)
The SDK can use either the PC's built-in Bluetooth adapter (Microsoft stack; the adapter must support Bluetooth Low Energy and the peripheral role) or a supported BLE dongle through the Bumble library. Some built-in adapters cannot advertise while Bluetooth headphones are connected: disconnect them, or use a dongle. The following dongles (VID/PID) have been tested and are known to work:

 - **VID/PID** : 0BDA/A728 - Realtek Ble adapter
 - **VID/PID** : 0BDA/8771 - Startech adapter/USBA-BLUETOOTH-V5-C2 

### NFC Readers

For NFC communication, the SDK supports any external USB NFC reader that is PC/SC (Personal Computer/Smart Card) compatible. This is a common standard, and most modern USB NFC readers should work out of the box with Windows drivers. The Credence ID P35 and the Access-IS ATOM readers are also supported.

### Barcode Readers

For QR code engagement, the SDK can use:

 - Any built-in or USB-connected webcam.
 - Any external USB barcode scanner that functions as a standard Human Interface Device (HID) or keyboard wedge.
 - Access-IS ATOM and ATOM3K document readers (QR and PDF417). The ATOM3K requires the Access-IS Document Reader software (ADRS) 6.0.0.4 or later.
 - HID TripTick Halo 210 and the Credence ID P35's built-in scanner.

## Documentation
- [Release Notes](https://github.com/CredenceID/Tap2iD-SDK-Windows/releases)
- [API Documentation](https://github.com/CredenceID/Tap2iD-SDK-Windows/wiki/Tap2iD-SDK-API-Documentation)
- [Integration Guide](https://github.com/CredenceID/Tap2iD-SDK-Windows/wiki/Guide-to-Integrate-Tap2iD-Windows-SDK)

---
© 2026 Credence ID LLC. All rights reserved.

This Sample App is provided by Credence ID for demonstration purposes only and is intended to showcase the usage of the Tap2iD SDK. This Sample App, including its code, assets, and associated materials, is the exclusive property of Credence ID.



