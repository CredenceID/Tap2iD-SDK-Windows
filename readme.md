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


## Supported Hardware
For proper functionality, the Tap2iD SDK for Windows requires compatible hardware for its different data capture methods.

### Bluetooth (BLE) Dongles
For BLE communication, the SDK relies on the Bumble library. A compatible BLE dongle is required. The following list contains VID (Vendor ID) and PID (Product ID) combinations that have been tested and are known to work.

 - **VID/PID** : 0BDA/A728 - Realtek Ble adapter
 - **VID/PID** : 0BDA/8771 - Startech adapter/USBA-BLUETOOTH-V5-C2 

### NFC Readers

For NFC communication, the SDK supports any external USB NFC reader that is PC/SC (Personal Computer/Smart Card) compatible. This is a common standard, and most modern USB NFC readers should work out of the box with Windows drivers.

### Barcode Readers

For QR code engagement, the SDK can use:

 - Any built-in or USB-connected webcam.
 - Any external USB barcode scanner that functions as a standard Human Interface Device (HID) or keyboard wedge.

## Documentation
- [Release Notes](https://github.com/CredenceID/Tap2iD-SDK-Windows/releases)
- [API Documentation](https://github.com/CredenceID/Tap2iD-SDK-Windows/wiki/Tap2iD-SDK-API-Documentation)
- [Integration Guide](https://github.com/CredenceID/Tap2iD-SDK-Windows/wiki/Guide-to-Integrate-Tap2iD-Windows-SDK)

---
© 2026 Credence ID LLC. All rights reserved.

This Sample App is provided by Credence ID for demonstration purposes only and is intended to showcase the usage of the Tap2iD SDK. This Sample App, including its code, assets, and associated materials, is the exclusive property of Credence ID.



