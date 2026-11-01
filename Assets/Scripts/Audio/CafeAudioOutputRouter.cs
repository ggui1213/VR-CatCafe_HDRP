using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif

[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
[AddComponentMenu("Cat Cafe/Cafe Audio Output Router")]
public sealed class CafeAudioOutputRouter : MonoBehaviour
{
    [SerializeField] private string headsetDeviceMatch = "Oculus";
    public string CurrentOutput { get; private set; }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private Guid lastEndpoint;
    private bool hasEndpoint;
    private bool failed;

    [DllImport("CafeAudioRouter", CallingConvention = CallingConvention.StdCall)]
    private static extern int CafeSetAudioOutput(ref Guid endpoint);

    private void OnEnable()
    {
        hasEndpoint = false;
        failed = false;
        StartCoroutine(RouteOutputs());
    }

    private void OnDisable() { StopAllCoroutines(); }

    private IEnumerator RouteOutputs()
    {
        // XR initialization can select the headset before the first game frame.
        yield return new WaitForSecondsRealtime(1f);
        while (true)
        {
            ApplyOutput();
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    private void ApplyOutput()
    {
        if (failed)
            return;
        try
        {
            bool worn = false;
            InputDevice headset = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool presenceKnown = headset.isValid
                && headset.TryGetFeatureValue(CommonUsages.userPresence, out worn);
            // An absent/unknown wear sensor must not capture desktop preview audio.
            bool useHeadset = presenceKnown && worn;
            Endpoint endpoint = GetEndpoint(useHeadset, headsetDeviceMatch);
            if (endpoint == null || (hasEndpoint && endpoint.Id == lastEndpoint))
                return;
            Guid id = endpoint.Id;
            int result = CafeSetAudioOutput(ref id);
            if (result != 1)
                throw new InvalidOperationException("Unity XR audio routing returned " + result);
            lastEndpoint = id;
            hasEndpoint = true;
            CurrentOutput = endpoint.Name;
            Debug.Log("[Cafe Audio] Output: " + CurrentOutput
                + " | headsetWorn=" + useHeadset + " | presenceKnown=" + presenceKnown, this);
        }
        catch (Exception exception)
        {
            failed = true;
            Debug.LogWarning("[Cafe Audio] Automatic output selection unavailable: "
                + exception.Message, this);
        }
    }

    private sealed class Endpoint
    {
        public Guid Id;
        public string Name;
    }

    private static Endpoint GetEndpoint(bool headset, string match)
    {
        var enumerator = (DeviceEnumerator)new DeviceEnumeratorObject();
        Device defaultDevice = null;
        DeviceCollection collection = null;
        try
        {
            Check(enumerator.GetDefaultAudioEndpoint(0, 1, out defaultDevice));
            Endpoint desktop = Describe(defaultDevice);
            if (!headset)
                return desktop;

            Check(enumerator.EnumAudioEndpoints(0, 1, out collection));
            uint count;
            Check(collection.GetCount(out count));
            for (uint i = 0; i < count; i++)
            {
                Device candidate = null;
                try
                {
                    Check(collection.Item(i, out candidate));
                    Endpoint endpoint = Describe(candidate);
                    if (!string.IsNullOrWhiteSpace(match)
                        && endpoint.Name.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0)
                        return endpoint;
                }
                finally { Release(candidate); }
            }
            // A headset without a connected audio endpoint falls back to desktop.
            return desktop;
        }
        finally
        {
            Release(defaultDevice);
            Release(collection);
            Release(enumerator);
        }
    }

    private static Endpoint Describe(Device device)
    {
        string id;
        Check(device.GetId(out id));
        string guid = id.Substring(id.LastIndexOf('{'));
        string name = id;
        PropertyStore store = null;
        try
        {
            Check(device.OpenPropertyStore(0, out store));
            name = ReadProperty(store, "a45c254e-df1c-4efd-8020-67d146a850e0", 14);
            if (string.IsNullOrEmpty(name))
                name = ReadProperty(store, "a45c254e-df1c-4efd-8020-67d146a850e0", 2)
                    + " (" + ReadProperty(store, "b3f8fa53-0004-438e-9003-51a46e139bfc", 6) + ")";
        }
        finally { Release(store); }
        return new Endpoint { Id = new Guid(guid), Name = name };
    }

    private static string ReadProperty(PropertyStore store, string format, uint id)
    {
        PropertyKey key = new PropertyKey { Format = new Guid(format), Id = id };
        PropertyVariant value;
        int result = store.GetValue(ref key, out value);
        if (result < 0)
            return string.Empty;
        try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : string.Empty; }
        finally { PropVariantClear(ref value); }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropertyVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Format; public uint Id; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropertyVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface PropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropertyVariant value);
    }

    private static void Check(int result) { Marshal.ThrowExceptionForHR(result); }
    private static void Release(object value)
    {
        if (value != null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class DeviceEnumeratorObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface DeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out DeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out Device device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface DeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out Device device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Device
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activation,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out PropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }
#endif
}
