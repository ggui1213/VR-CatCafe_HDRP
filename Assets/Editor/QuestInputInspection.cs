using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.XR.OpenXR.Input;
using System.Reflection;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.Management;

[InitializeOnLoad]
internal static class QuestInputInspection
{
    static double next;
    static QuestInputInspection()
    {
        // Full-scene/action scans are an explicit diagnostic, not background work
        // during every normal Editor or Quest Link preview session.
        if (!SessionState.GetBool("QuestProbe", false) && !SessionState.GetBool("QuestLiveInspection", false)) return;
        next = EditorApplication.timeSinceStartup + 3;
        EditorApplication.update += Update;
    }

    public static void PlayProbe()
    {
        EditorSceneManager.OpenScene("Assets/_Recovery/QuestRepairInspection.unity", OpenSceneMode.Single);
        SessionState.SetBool("QuestProbe", true);
        SessionState.SetString("QuestProbeDeadline", DateTime.UtcNow.AddSeconds(75).ToString("O"));
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        EditorApplication.EnterPlaymode();
    }

    public static void Inspect()
    {
        EditorSceneManager.OpenScene("Assets/_Recovery/QuestRepairInspection.unity", OpenSceneMode.Single);
        next = 0;
        Update();
        Debug.Log("Quest input inspection saved to Logs/QuestInputInspection.txt");
    }

    public static void VerifyBindings()
    {
        EditorSceneManager.OpenScene("Assets/_Recovery/QuestRepairInspection.unity", OpenSceneMode.Single);
        var report = new StringBuilder("OFFLINE TEST: synthetic Quest Touch Plus devices; NOT a hardware tracking test.\n");
        // Run the same registration routine called by OpenXRLoader.InitializeInternal.
        typeof(OpenXRInput).GetMethod("RegisterLayouts", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        report.AppendLine("RegisterLayouts IL bytes=" + typeof(OpenXRInput).GetMethod("RegisterLayouts", BindingFlags.NonPublic | BindingFlags.Static).GetMethodBody().GetILAsByteArray().Length);
        foreach (var l in InputSystem.ListLayouts()) if (l.Contains("Quest") || l.Contains("OpenXR")) report.AppendLine("REGISTERED " + l);
        var featureSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
        var selectedSettings = UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(EditorUserBuildSettings.selectedBuildTargetGroup);
        report.AppendLine("selectedGroup=" + EditorUserBuildSettings.selectedBuildTargetGroup + " settings=" + selectedSettings + " OpenXR features=" + featureSettings.GetFeatures<OpenXRFeature>().Length);
        var devices = new System.Collections.Generic.List<InputDevice>();
        try
        {
            foreach (string side in new[] { "Left", "Right" })
            {
                var device = InputSystem.AddDevice(new InputDeviceDescription
                {
                    interfaceName = "XRInputV1", manufacturer = "Oculus",
                    product = "Meta Quest Touch Plus Controller OpenXR"
                });
                devices.Add(device);
                InputSystem.SetDeviceUsage(device, side + "Hand");
                report.AppendLine(side + " device layout=" + device.layout + " type=" + device.GetType().FullName);
                if (!(device is XRController)) throw new Exception("Quest device did not resolve to an XRController layout");
                TrackedPoseDriver driver = null;
                foreach (var candidate in Resources.FindObjectsOfTypeAll<TrackedPoseDriver>())
                    if (candidate.gameObject.scene.IsValid() && candidate.gameObject.activeInHierarchy && candidate.name == side + " Controller") driver = candidate;
                if (driver == null) throw new Exception("Missing active " + side + " Controller");
                var pos = driver.positionInput.action;
                var rot = driver.rotationInput.action;
                var tracking = driver.trackingStateInput.action;
                pos.Enable(); rot.Enable(); tracking.Enable();
                for (int step = 1; step <= 2; step++)
                {
                    var expected = new Vector3(side == "Left" ? -0.25f : 0.25f, 1.0f + step * 0.1f, 0.4f + step * 0.1f);
                    var expectedRot = Quaternion.Euler(step * 10, step * 15, 0);
                    InputSystem.QueueDeltaStateEvent((UnityEngine.InputSystem.Controls.Vector3Control)device["devicePosition"], expected);
                    InputSystem.QueueDeltaStateEvent((UnityEngine.InputSystem.Controls.QuaternionControl)device["deviceRotation"], expectedRot);
                    InputSystem.QueueDeltaStateEvent((UnityEngine.InputSystem.Controls.IntegerControl)device["trackingState"], 3);
                    InputSystem.QueueDeltaStateEvent((UnityEngine.InputSystem.Controls.ButtonControl)device["isTracked"], 1f);
                    InputSystem.Update();
                    var actual = pos.ReadValue<Vector3>();
                    var actualRot = rot.ReadValue<Quaternion>();
                    report.AppendLine(side + " step=" + step + " positionControls=" + pos.controls.Count + " rotationControls=" + rot.controls.Count + " trackingControls=" + tracking.controls.Count + " position=" + actual + " tracking=" + tracking.ReadValue<int>());
                    if (Vector3.Distance(actual, expected) > 0.001f || Quaternion.Angle(actualRot, expectedRot) > 0.1f || tracking.ReadValue<int>() != 3)
                        throw new Exception("Pose readback mismatch for " + side);
                }
                foreach (var renderer in driver.GetComponentsInChildren<Renderer>(true))
                    report.AppendLine("  mesh " + PathOf(renderer.transform) + " active=" + renderer.gameObject.activeInHierarchy + " enabled=" + renderer.enabled);
                foreach (var poser in driver.GetComponentsInChildren<ControllerHandPoser>(true))
                {
                    poser.triggerAction.action.Enable(); poser.gripAction.action.Enable();
                    InputSystem.QueueDeltaStateEvent((UnityEngine.InputSystem.Controls.AxisControl)device["trigger"], 0.7f);
                    InputSystem.QueueDeltaStateEvent((UnityEngine.InputSystem.Controls.AxisControl)device["grip"], 0.8f);
                    InputSystem.Update();
                    var trigger = poser.triggerAction.action.ReadValue<float>();
                    var grip = poser.gripAction.action.ReadValue<float>();
                    report.AppendLine("  poser trigger=" + trigger + " grip=" + grip);
                    if (Mathf.Abs(trigger - 0.7f) > 0.001f || Mathf.Abs(grip - 0.8f) > 0.001f) throw new Exception("Finger input mismatch");
                }
            }
            // Preserve the latest pre-Play scene separately; do not replace the saved CoffeeShop scene.
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/_Recovery/CoffeeShop_QuestRecovered.unity", true);
            report.AppendLine("PASS: both hands resolve and read changing position, rotation, tracking state and finger input.");
        }
        catch (Exception e)
        {
            report.AppendLine("FAIL: " + e);
            throw;
        }
        finally
        {
            foreach (var d in devices) InputSystem.RemoveDevice(d);
            File.WriteAllText("Logs/QuestOfflineBindingTest.txt", report.ToString());
        }
    }

    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

    static void Action(StringBuilder s, string label, InputAction a)
    {
        s.AppendLine(label + ": " + (a == null ? "NULL" : a.name + " enabled=" + a.enabled + " controls=" + a.controls.Count));
        if (a == null) return;
        foreach (var b in a.bindings) s.AppendLine("  binding=" + b.effectivePath);
        foreach (var c in a.controls) s.AppendLine("  control=" + c.path);
    }

    static void Update()
    {
        if (SessionState.GetBool("QuestProbe", false) && DateTime.UtcNow > DateTime.Parse(SessionState.GetString("QuestProbeDeadline", "")))
        {
            SessionState.SetBool("QuestProbe", false);
            EditorApplication.Exit(0);
            return;
        }
        if (EditorApplication.timeSinceStartup < next || EditorApplication.isCompiling) return;
        next = EditorApplication.timeSinceStartup + 5;
        try
        {
            var s = new StringBuilder();
            s.AppendLine(DateTime.Now.ToString("O") + " playing=" + EditorApplication.isPlaying + " target=" + EditorUserBuildSettings.activeBuildTarget + " selectedGroup=" + EditorUserBuildSettings.selectedBuildTargetGroup);
            var general = UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            s.AppendLine("GENERAL " + (general == null ? "NULL" : general.name + " assigned=" + general.AssignedSettings));
            if (general != null && general.AssignedSettings != null)
                foreach (var loader in general.AssignedSettings.activeLoaders) s.AppendLine("LOADER " + loader.GetType().FullName);
            var xrSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            s.AppendLine("OPENXR SETTINGS " + (xrSettings == null ? "NULL" : xrSettings.name));
            if (xrSettings != null)
                foreach (var feature in xrSettings.GetFeatures<OpenXRFeature>()) s.AppendLine("FEATURE " + feature.GetType().Name + " enabled=" + feature.enabled);
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                var scene = SceneManager.GetSceneAt(i);
                s.AppendLine("SCENE " + scene.path + " dirty=" + scene.isDirty);
            }
            foreach (var layout in InputSystem.ListLayouts())
                if (layout.Contains("Quest") || layout.Contains("Touch") || layout.Contains("OpenXR") || layout == "XRController") s.AppendLine("LAYOUT " + layout);
            foreach (var d in InputSystem.devices)
            {
                s.AppendLine("DEVICE " + d.name + " layout=" + d.layout + " type=" + d.GetType().FullName + " usages=" + string.Join(",", d.usages) + " enabled=" + d.enabled);
                if (d is TrackedDevice || d.name.Contains("OpenXR"))
                    foreach (var c in d.allControls)
                        if (c.name == "devicePosition" || c.name == "trackingState" || c.name == "isTracked" || c.name == "trigger" || c.name == "grip") s.AppendLine("  " + c.path + "=" + c.ReadValueAsObject());
            }
            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (mb == null || !mb.gameObject.scene.IsValid()) continue;
                string type = mb.GetType().Name;
                if (mb is TrackedPoseDriver driver)
                {
                    s.AppendLine("DRIVER " + PathOf(mb.transform) + " active=" + mb.gameObject.activeInHierarchy + " enabled=" + mb.enabled + " localPos=" + mb.transform.localPosition);
                    Action(s, "position", driver.positionInput.action);
                    Action(s, "rotation", driver.rotationInput.action);
                    Action(s, "tracking", driver.trackingStateInput.action);
                }
                else if (type == "InputActionManager" || type.Contains("Simulator") || type == "XRInputModalityManager")
                    s.AppendLine("COMPONENT " + PathOf(mb.transform) + " " + type + " active=" + mb.gameObject.activeInHierarchy + " enabled=" + mb.enabled + " " + EditorJsonUtility.ToJson(mb));
            }
            File.WriteAllText("Logs/QuestInputInspection.txt", s.ToString());
        }
        catch (Exception e) { File.WriteAllText("Logs/QuestInputInspection.txt", e.ToString()); }
    }
}
