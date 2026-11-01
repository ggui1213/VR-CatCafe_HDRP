using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using System.Text;

public class XRControllerDiagnostic : MonoBehaviour
{
    TrackedPoseDriver m_Driver;
    float m_NextLogTime;
    int m_LogCount;

    void Awake()
    {
        m_Driver = GetComponent<TrackedPoseDriver>();
    }

    void OnEnable()
    {
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }

    void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        Debug.Log($"[XRDiag] Device changed: {device.name} ({device.layout}) -> {change}");
    }

    void Update()
    {
        if (Time.time < m_NextLogTime || m_LogCount > 60)
            return;

        m_NextLogTime = Time.time + 1.0f;
        m_LogCount++;

        var sb = new StringBuilder();
        sb.AppendLine($"[XRDiag] {gameObject.name} (t={Time.time:F1}s):");
        sb.AppendLine($"  localPos={transform.localPosition}, localRot={transform.localEulerAngles}");

        if (m_Driver != null)
        {
            var posAction = m_Driver.positionInput.action;
            if (posAction != null)
            {
                sb.AppendLine($"  PosAction: {posAction.name}, enabled={posAction.enabled}, phase={posAction.phase}");
                sb.AppendLine($"  PosValue={posAction.ReadValue<Vector3>()}");
                sb.AppendLine($"  ActiveControl: {(posAction.activeControl != null ? posAction.activeControl.path : "null")}");
                sb.AppendLine($"  Controls count: {posAction.controls.Count}");
                foreach (var c in posAction.controls)
                {
                    sb.AppendLine($"    bound control: {c.path} on {c.device.name} ({c.device.layout})");
                }
            }
            else
            {
                sb.AppendLine("  PosAction: NULL");
            }

            var rotAction = m_Driver.rotationInput.action;
            if (rotAction != null)
            {
                sb.AppendLine($"  RotValue={rotAction.ReadValue<Quaternion>().eulerAngles}, activeControl: {(rotAction.activeControl != null ? rotAction.activeControl.path : "null")}");
            }
        }

        // List all XR devices
        sb.Append("  XR Devices in InputSystem: ");
        int devCount = 0;
        foreach (var dev in InputSystem.devices)
        {
            if (dev is UnityEngine.InputSystem.XR.XRController || dev.name.Contains("XR") || dev.name.Contains("Quest") || dev.name.Contains("Touch") || dev.name.Contains("Oculus"))
            {
                sb.Append($"{dev.name}({dev.layout}, added={dev.added}, enabled={dev.enabled}) ");
                devCount++;
            }
        }
        if (devCount == 0) sb.Append("NONE");

        Debug.Log(sb.ToString());
    }
}
