![Template 5 (1)](https://github.com/user-attachments/assets/470b82b9-cc72-4ce9-9343-da1bede1bc58)

**Tap2iD Verifier SDK sample for Windows**

June 2024

##

## Overview

The Tap2iD Verifier SDK for Windows provides a set of APIs for integrating mobile driver license functionalities into your applications. The SDK is based on the ISO 18013-5 standard. This document serves as a comprehensive guide for developers on using the SDK, including details about the available methods, request/response formats, and example usage.

## Features

- Parse and verify mdocs (mDLs), with verification of MSO-validity, doc type, certificate chains, tamper check and issuer signatures.
- Seamless Integration: The SDK can be integrated into existing apps or systems, providing a smooth user experience without requiring additional steps
- ISO Compliance: Adheres to international standards for digital identity verification, ensuring compliance with regulatory requirements
- Adaptable: Suitable for businesses of all sizes, from small enterprises to large corporations, and can handle varying volumes of verification requests

## Prerequisites

### Supported Platforms and OS

- Windows 10 or higher

### Software Requirements / Necessary Libraries

- Visual Studio 2019 or later
- WinUI 3 application

### Hardware Requirements

- Bluetooth Low Energy support for Windows Bluetooth
- Webcam on PC OR an external 2D Barcode scanner to read QR code

### Skills Required

- WinUI 3 knowledge
- C#

## Getting Started with SDK Integration

### Installation

1. Download the mDL SDK from the official website. It is provided as Tap2idVerifySDK Package
2. Add the NuGet Package to your WinUI 3 project:


### Authentication (Work In Progress)

To use the mDL SDK, you need an API key. Contact support to get your API key.

_This feature is under active development and will be a part of future releases.._

## API

### Instantiate SDK
A factory method is available to instantiate the class:
```
private IVerifyMdoc tap2idVerifier;


tap2idVerifier = VerifyMdocFactory.CreateVerifyMdoc();

```

### Initialize SDK

This method is called to initialize the Tap2iD SDK. It's mandatory to call this api before calling any other api and this API should be called before calling any other api.
```

tap2idVerifier.InitTap2iDAsync(new CoreSdkConfig { ApiKey = "MyApiKey" });

```

### Define delegate and Verify Mdoc


```
var delegateVerifyState = new DelegateVerifyState
{
	OnVerifyStateCallback = OnVerifyState,
};

var mDocConfig = new MdocConfig
{
	deviceEngagementString = "mdoc:xxxxxxx";
}

var result = await tap2idVerifier.verifyMdocAsync(mDocConfig,delegateVerifyState);

if (tap2IdResult.ResultError == Tap2iDResultError.OK)
{
	Console.WriteLine($"name : {result.getIdentity().getFamilyName}. IsOver21 : {result.getIdentity().getIsAgeOver21()}");
}
else
{
	Console.WriteLine($"Error description: {result.getResultError().getDescription()}. Error code: {result.getResultError().getErrorCode()}");
}

//Define callback
private void OnVeryfyState(VerifyState verifyState)
{

	Console.Writeline($"Current State : {verifyState.ToString()}");

}
```

## Support

For further assistance, contact our support team at [support@credenceid.com](mailto:support@credenceid.com)

## Licensing and Legal Information

- License: The SDK s license and any legal considerations.
- Terms of Use: Terms and conditions for using the SDK.
