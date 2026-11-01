#include "IUnityInterface.h"

// ABI from Unity's XR provider audio interface (IUnityXRAudio).
// Only Unity's own output is redirected; Windows defaults stay unchanged.
UNITY_DECLARE_INTERFACE(CafeXRAudio)
{
    bool (UNITY_INTERFACE_API* SetOutput)(const void* guid);
    bool (UNITY_INTERFACE_API* SetInput)(const void* guid);
};
UNITY_REGISTER_INTERFACE_GUID(0x62a4ec89e8d54f6bULL, 0x92ae9e5c46bc6e55ULL, CafeXRAudio);

static IUnityInterfaces* interfaces = nullptr;

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* value)
{
    interfaces = value;
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
{
    interfaces = nullptr;
}

extern "C" int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API CafeSetAudioOutput(const void* endpointGuid)
{
    if (!interfaces || !endpointGuid)
        return -1;
    CafeXRAudio* audio = interfaces->Get<CafeXRAudio>();
    if (!audio || !audio->SetOutput)
        return -2;
    return audio->SetOutput(endpointGuid) ? 1 : 0;
}
