# AJA NTV2 Integration Notes

AJA hardware output is stubbed in `Core/AjaSender.cs`. To implement:

## Why a C Wrapper Is Required

The AJA NTV2 SDK exposes a C++ class library (`CNTV2Card`, etc.) with no flat C API.
C# cannot P/Invoke into C++ class methods directly. A thin native adapter DLL is needed.

## What the Adapter Must Expose (flat C API)

```c
// ShowCastAjaAdapter.dll
bool  aja_initialize();
void  aja_shutdown();
int   aja_device_count();
void  aja_device_name(int index, char* buf, int bufLen);
void* aja_open(int deviceIndex, int width, int height, int fpsN, int fpsD);
void  aja_close(void* handle);
int   aja_submit_frame(void* handle, const uint8_t* bgraPixels, int byteCount);
```

## Build Steps (when SDK is available)

1. Install AJA NTV2 SDK.
2. Create a C++ DLL project (`ShowCastAjaAdapter`) referencing NTV2 headers.
3. Implement the flat API above using `CNTV2Card` and `NTV2FormatDesc`.
4. Replace `AjaApi.TryInitialize()` stub with a P/Invoke loader matching the NDIlib pattern.
5. Implement `AjaSender` mirroring `BlackmagicSender`.
