# Cafe audio output routing

Windows x64 helper for `Assets/Scripts/Audio/CafeAudioOutputRouter.cs`.
It uses Unity's native `IUnityXRAudio.SetOutput` interface to select a render
endpoint for Unity. It does not modify Windows default devices, application
volume, or persistent application routing preferences.

The scene component polls the XR head device's `CommonUsages.userPresence` every
0.5 seconds. A worn headset uses an active output whose name contains the
configured `Headset Device Match` (default: `Oculus`). When the headset is not
worn, the sensor is unavailable, or no matching output is available, Unity uses
the current Windows default multimedia render endpoint. Android Quest builds
keep their platform's own audio output behavior.

## Rebuilding

Close Unity first so Windows can replace the loaded DLL. From PowerShell run:

```powershell
& .\NativePlugins\CafeAudioRouter\Build.ps1
```

The script accepts `UnityEditorPath` and `VisualStudioPath` parameters. It uses
the Unity installation's `Data/PluginAPI/IUnityInterface.h` and MSVC x64 tools.
The DLL has no C/C++ runtime dependencies. Intermediate output goes under Temp.

Interface reference: Unity XR provider `IUnityXRAudio` (also distributed in
[Valve's Unity XR provider SDK](https://github.com/ValveSoftware/unity-xr-plugin/tree/master/CommonHeaders/ProviderInterface)).

## Verified on this machine

Before the fix, Unity's active Windows audio session sent music to Oculus
Virtual Audio Device, while Windows' default device was Creative Pebble Pro.
After routing, the Unity session on Creative Pebble Pro is active with nonzero
peak audio levels; the Oculus session becomes inactive. This verifies delivery
to the desktop output device. Physical headset wear detection and sound still
need the user to test with the headset connected and worn.
