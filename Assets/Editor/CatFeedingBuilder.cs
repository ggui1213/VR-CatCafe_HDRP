using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Unity.AI.Assistant.Agent.Dynamic.Extension.Editor;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

internal class CommandScript : IRunCommand
{
    public bool inspectOnly;
    public bool finishOnly;
    public bool previewOnly;
    public bool verifyOnly;
    public bool repairModel;
    public bool questPreview;
    public void Execute(ExecutionResult result)
    {
        if (questPreview) { QuestPreviewDiagnostics.Begin(); return; }
        if (repairModel) { CatFeedingBuilder.RepairModel(result); return; }
        if (verifyOnly) { CatFeedingVerification.Begin(); return; }
        if (previewOnly) { CatFeedingBuilder.Preview(result); return; }
        if (finishOnly) { CatFeedingBuilder.FinishSetup(result); return; }
        if (!inspectOnly) { CatFeedingBuilder.Build(result); return; }
        var cat = UnityEngine.Object.FindFirstObjectByType<CatIndoorRoaming>();
        foreach (var bone in cat.GetComponentsInChildren<Transform>(true).Where(t =>
            t.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0 ||
            t.name.IndexOf("jaw", StringComparison.OrdinalIgnoreCase) >= 0 ||
            t.name.IndexOf("tongue", StringComparison.OrdinalIgnoreCase) >= 0))
            Debug.Log("FEED_BONE " + bone.name + " rootLocal=" + cat.transform.InverseTransformPoint(bone.position) + " forward=" + bone.forward + " up=" + bone.up);
        foreach (string file in new[] { "Cat_Drink1", "Cat_Drink2", "Cat_Eat 1", "Cat_Eat 2", "Cat_Meow_Sit", "Cat_Sit_Lick_Paw_R" })
        {
            string path = "Assets/Malbers Animations/Animals Packs/04 Pets/Cat/Animations/Actions/" + file + ".fbx";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__"));
            if (!clip) continue;
            var bindings = AnimationUtility.GetCurveBindings(clip).Where(b =>
                b.path.IndexOf("jaw", StringComparison.OrdinalIgnoreCase) >= 0 || b.path.IndexOf("tongue", StringComparison.OrdinalIgnoreCase) >= 0);
            Debug.Log("FEED_CLIP " + file + " length=" + clip.length + " mouthCurves=" + bindings.Count() + " paths=" + string.Join(",", bindings.Select(b => b.path).Distinct()));
        }
    }
}

public static class CatFeedingBuilder
{
    static ExecutionResult execution;
    static bool previewLighting, previewGizmos, previewGrid;
    public const string Folder = "Assets/Cat/Treats";
    [MenuItem("Cat Cafe/Inspect Feeding Assets")]
    static void Inspect() { new CommandScript { inspectOnly = true }.Execute(new ExecutionResult("Inspect feeding assets")); }

    [MenuItem("Cat Cafe/Build Cat Feeding System")]
    static void Run() { new CommandScript().Execute(new ExecutionResult("Build cat feeding system")); }

    [MenuItem("Cat Cafe/Finish Feeding Setup")]
    static void Finish() { new CommandScript { finishOnly = true }.Execute(new ExecutionResult("Finish feeding setup")); }

    [MenuItem("Cat Cafe/Preview Treat Model")]
    static void PreviewMenu() { new CommandScript { previewOnly = true }.Execute(new ExecutionResult("Preview treat model")); }
    [MenuItem("Cat Cafe/Verify Cat Feeding")]
    static void VerifyMenu() { new CommandScript { verifyOnly = true }.Execute(new ExecutionResult("Verify cat feeding")); }
    [MenuItem("Cat Cafe/Diagnose Quest Preview")]
    static void DiagnoseQuestMenu() { new CommandScript { questPreview = true }.Execute(new ExecutionResult("Diagnose Quest preview")); }
    [MenuItem("Cat Cafe/Repair Treat Model Mesh")]
    static void RepairMenu() { new CommandScript { repairModel = true }.Execute(new ExecutionResult("Repair treat model mesh")); }
    public static void RepairModel(ExecutionResult result)
    {
        var generated = PacketMesh();
        var reloaded = SaveObj(generated);
        result.DestroyObject(generated);
        var tube = UnityEngine.Object.FindFirstObjectByType<CatTreatTube>();
        var filter = tube.wrapper.GetComponent<MeshFilter>();
        result.RegisterObjectModification(filter);
        filter.sharedMesh = reloaded;
        EditorUtility.SetDirty(filter);
        PrefabUtility.SaveAsPrefabAsset(tube.gameObject,Folder+"/Creamy Cat Treat.prefab");
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tube.gameObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(tube.gameObject.scene);
        Debug.Log("TREAT_MESH_RELOADED: vertices="+reloaded.vertexCount+" scene="+filter.sharedMesh.vertexCount);
    }
    public static void Preview(ExecutionResult result)
    {
        var tube = UnityEngine.Object.FindFirstObjectByType<CatTreatTube>();
        var old = GameObject.Find("Treat model studio preview");
        if (old) result.DestroyObject(old);
        var copy = UnityEngine.Object.Instantiate(tube.visuals.gameObject);
        result.RegisterObjectCreation(copy);
        copy.name = "Treat model studio preview";
        copy.hideFlags = HideFlags.DontSave;
        copy.transform.SetPositionAndRotation(new Vector3(0.2f,1.2f,0.1f),Quaternion.identity);
        copy.SetActive(true);
        Selection.activeGameObject = null;
        var view = SceneView.lastActiveSceneView;
        previewLighting = view.sceneLighting; previewGizmos = view.drawGizmos; previewGrid = view.showGrid;
        view.sceneLighting = true; view.drawGizmos = false; view.showGrid = false;
        view.in2DMode = false;
        view.LookAtDirect(copy.transform.position, Quaternion.Euler(0f,-12f,0f),0.12f);
        view.Focus();
        view.Repaint();
        var wrapper = copy.transform.Find("Soft foil wrapper");
        Debug.Log("TREAT_MODEL_PREVIEW: mesh=" + wrapper.GetComponent<MeshFilter>().sharedMesh.vertexCount +
            " material=" + wrapper.GetComponent<Renderer>().sharedMaterial.name + " bounds=" + wrapper.GetComponent<Renderer>().bounds);
    }
    [MenuItem("Cat Cafe/Close Treat Model Preview")]
    static void ClosePreview()
    {
        var preview = GameObject.Find("Treat model studio preview");
        if (preview) new ExecutionResult("Close treat preview").DestroyObject(preview);
        var view = SceneView.lastActiveSceneView;
        view.sceneLighting = previewLighting; view.drawGizmos = previewGizmos; view.showGrid = previewGrid;
        view.Repaint();
    }

    public static void FinishSetup(ExecutionResult result)
    {
        execution = result;
        var events = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
        if (!events)
        {
            var go = new GameObject("XR UI EventSystem",typeof(EventSystem),typeof(XRUIInputModule));
            result.RegisterObjectCreation(go);
        }
        else if (!events.GetComponent<XRUIInputModule>())
        {
            foreach (var module in events.GetComponents<BaseInputModule>()) { result.RegisterObjectModification(module); module.enabled = false; }
            Undo.AddComponent<XRUIInputModule>(events.gameObject);
        }
        var tube = UnityEngine.Object.FindFirstObjectByType<CatTreatTube>();
        var menu = UnityEngine.Object.FindFirstObjectByType<WristTreatMenu>();
        PrefabUtility.SaveAsPrefabAsset(tube.gameObject,Folder+"/Creamy Cat Treat.prefab");
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tube.gameObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(tube.gameObject.scene);
        SceneView.lastActiveSceneView.Frame(new Bounds(tube.transform.position,Vector3.one*0.22f),true);
        Debug.Log("CAT_FEEDING_UI_READY: XR EventSystem and tracked-device graphic raycaster connected.");
    }

    public static void Build(ExecutionResult result)
    {
        execution = result;
        if (GameObject.Find("Cat Feeding System")) { result.LogWarning("Cat Feeding System already exists."); return; }
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Cat", "Treats");
        var head = Camera.main.transform;
        var left = GameObject.Find("Left Hand Model").transform;
        var cats = UnityEngine.Object.FindObjectsByType<CatIndoorRoaming>(FindObjectsSortMode.None);
        AddAnimations(cats[0].GetComponent<Animator>());
        foreach (var cat in cats)
        {
            var feed = cat.GetComponent<CatTreatFeeding>();
            if (!feed) feed = Undo.AddComponent<CatTreatFeeding>(cat.gameObject);
            result.RegisterObjectModification(feed);
            var bones = cat.GetComponentsInChildren<Transform>(true);
            var headBone = bones.First(t => t.name == "Head");
            var tongue = bones.First(t => t.name == "Tongue03");
            var mouth = new GameObject("Treat mouth target").transform;
            mouth.SetParent(headBone, false);
            mouth.position = tongue.position + cat.transform.forward * 0.012f + cat.transform.up * 0.012f;
            result.RegisterObjectCreation(mouth.gameObject);
            feed.mouth = mouth;
            feed.hungryMeows = Enumerable.Range(1, 4).Select(i => AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/Malbers Animations/Animals Packs/04 Pets/Cat/Audio/Meow Hungry " + i + ".wav")).Where(c => c).ToArray();
            EditorUtility.SetDirty(feed);
        }
        var system = new GameObject("Cat Feeding System");
        result.RegisterObjectCreation(system);
        var tube = BuildTube(system.transform);
        PrefabUtility.SaveAsPrefabAsset(tube.gameObject, Folder + "/Creamy Cat Treat.prefab");
        tube.player = head;
        tube.transform.position = new Vector3(-2.2f, 1.12f, 2.65f);
        tube.transform.rotation = Quaternion.Euler(0f, 180f, -12f);
        var pickup = new GameObject("Wrist treat pickup point").transform;
        pickup.SetParent(system.transform, false);
        pickup.SetPositionAndRotation(tube.transform.position, tube.transform.rotation);
        tube.home = pickup;
        var menu = system.AddComponent<WristTreatMenu>();
        menu.leftHand = left;
        menu.head = head;
        menu.treat = tube;
        menu.pickupPoint = pickup;
        BuildMenu(menu, system.transform);
        var references = AssetDatabase.LoadAllAssetsAtPath("Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/XRI Default Input Actions.inputactions")
            .OfType<UnityEngine.InputSystem.InputActionReference>().ToArray();
        tube.leftTrigger = references.FirstOrDefault(r => r.action != null && r.action.name == "Activate Value" && r.action.actionMap.name.Contains("Left"));
        tube.rightTrigger = references.FirstOrDefault(r => r.action != null && r.action.name == "Activate Value" && r.action.actionMap.name.Contains("Right"));
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(system.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(system.scene);
        Selection.activeGameObject = tube.gameObject;
        SceneView.lastActiveSceneView.Frame(new Bounds(tube.transform.position, Vector3.one * 0.22f), false);
        Debug.Log("CAT_FEEDING_BUILT: editable sachet prefab, left wrist XR menu, four feeding cats, jaw/tongue lick layer and hungry meows.");
    }

    static void AddAnimations(Animator animator)
    {
        var controller = animator.runtimeAnimatorController as AnimatorController;
        Undo.RegisterCompleteObjectUndo(controller, "Add feeding animations");
        var baseMachine = controller.layers[0].stateMachine;
        var satisfied = baseMachine.states.FirstOrDefault(s => s.state.name == "TreatSatisfied").state;
        if (!satisfied) satisfied = baseMachine.AddState("TreatSatisfied");
        satisfied.motion = CopyClip("Cat_Sit_Lick_Paw_R", "Treat satisfied grooming", false, false);
        var lick = CopyClip("Cat_Drink2", "Treat mouth licking", true, true);
        var mask = new AvatarMask { name = "Treat jaw and tongue only" };
        var transforms = animator.GetComponentsInChildren<Transform>(true);
        mask.transformCount = transforms.Length;
        for (int i = 0; i < transforms.Length; i++)
        {
            string path = AnimationUtility.CalculateTransformPath(transforms[i], animator.transform);
            mask.SetTransformPath(i, path);
            mask.SetTransformActive(i, path.Contains("/Head/Jaw"));
        }
        AssetDatabase.CreateAsset(mask, Folder + "/Treat mouth.mask");
        if (!controller.layers.Any(l => l.name == "Treat Mouth")) controller.AddLayer("Treat Mouth");
        var layers = controller.layers;
        var layer = layers.First(l => l.name == "Treat Mouth");
        layer.avatarMask = mask;
        layer.defaultWeight = 0f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        var state = layer.stateMachine.AddState("Lick");
        state.motion = lick;
        state.speed = 1.15f;
        layer.stateMachine.defaultState = state;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
    }
    static AnimationClip CopyClip(string source, string name, bool loop, bool mouthOnly)
    {
        string path = "Assets/Malbers Animations/Animals Packs/04 Pets/Cat/Animations/Actions/" + source + ".fbx";
        var imported = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
        var clip = UnityEngine.Object.Instantiate(imported);
        clip.name = name;
        AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
        if (mouthOnly)
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (!binding.path.Contains("/Head/Jaw")) AnimationUtility.SetEditorCurve(clip, binding, null);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, Folder + "/" + name + ".anim");
        return clip;
    }
    static CatTreatTube BuildTube(Transform parent)
    {
        var coral = Material("Coral foil", new Color(0.87f, 0.24f, 0.18f), 0.22f, 0.43f);
        var cream = Material("Warm ivory", new Color(1f, 0.92f, 0.74f), 0f, 0.28f);
        var navy = Material("Deep teal ink", new Color(0.025f, 0.17f, 0.19f), 0f, 0.2f);
        var silver = Material("Crimped foil", new Color(0.72f, 0.75f, 0.73f), 0.8f, 0.55f);
        var food = Material("Salmon cream", new Color(0.94f, 0.65f, 0.48f), 0f, 0.58f);
        var go = new GameObject("Creamy Cat Treat"); go.transform.SetParent(parent, false);
        var tube = go.AddComponent<CatTreatTube>();
        tube.body = go.AddComponent<Rigidbody>(); tube.body.isKinematic = true; tube.body.useGravity = false; tube.body.mass = 0.025f;
        tube.body.interpolation = RigidbodyInterpolation.Interpolate;
        var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(0.04f, 0.165f, 0.018f);
        var grab = go.AddComponent<XRGrabInteractable>(); tube.grab = grab;
        grab.colliders.Clear(); grab.colliders.Add(col);
        grab.movementType = XRBaseInteractable.MovementType.Kinematic;
        grab.addDefaultGrabTransformers = false; grab.throwOnDetach = false;
        var transformer = go.AddComponent<TreatPalmGrabTransformer>(); transformer.treat = tube;
        grab.startingSingleGrabTransformers.Clear(); grab.startingSingleGrabTransformers.Add(transformer);
        var attach = new GameObject("Palm grip pose").transform; attach.SetParent(go.transform, false);
        attach.localRotation = Quaternion.Inverse(tube.GripRotation); grab.attachTransform = attach;
        var visuals = new GameObject("Treat visuals").transform; visuals.SetParent(go.transform, false); tube.visuals = visuals;
        var wrapper = new GameObject("Soft foil wrapper").transform; wrapper.SetParent(visuals, false); tube.wrapper = wrapper;
        var generated = PacketMesh(); var mesh = SaveObj(generated); execution.DestroyObject(generated);
        wrapper.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        wrapper.gameObject.AddComponent<MeshRenderer>().sharedMaterial = coral;
        Part("Bottom heat seal", PrimitiveType.Cube, wrapper, new Vector3(0f,-0.077f,0f), new Vector3(0.039f,0.009f,0.0015f), silver);
        for (int i = 0; i < 13; i++)
            Part("Seal crimp", PrimitiveType.Cube, wrapper, new Vector3(-0.018f + i * 0.003f,-0.077f,-0.0011f), new Vector3(0.0007f,0.008f,0.0005f), cream);
        Part("Torn top foil", PrimitiveType.Cube, wrapper, new Vector3(0f,0.075f,0f), new Vector3(0.022f,0.005f,0.002f), silver);
        Part("Ivory label", PrimitiveType.Cube, wrapper, new Vector3(0f,0.005f,-0.0064f), new Vector3(0.032f,0.093f,0.0005f), cream);
        // A simple embossed cat mark stays readable at actual hand-held size.
        Part("Cat face", PrimitiveType.Sphere, wrapper, new Vector3(0f,0.027f,-0.007f), new Vector3(0.014f,0.012f,0.001f), navy);
        var earL = Part("Cat left ear", PrimitiveType.Cube, wrapper, new Vector3(-0.005f,0.033f,-0.007f), new Vector3(0.006f,0.006f,0.0007f), navy);
        earL.transform.localRotation = Quaternion.Euler(0f,0f,25f);
        var earR = Part("Cat right ear", PrimitiveType.Cube, wrapper, new Vector3(0.005f,0.033f,-0.007f), new Vector3(0.006f,0.006f,0.0007f), navy);
        earR.transform.localRotation = Quaternion.Euler(0f,0f,-25f);
        Part("Left cat eye", PrimitiveType.Sphere, wrapper, new Vector3(-0.003f,0.0275f,-0.0077f), new Vector3(0.0016f,0.001f,0.0003f), cream);
        Part("Right cat eye", PrimitiveType.Sphere, wrapper, new Vector3(0.003f,0.0275f,-0.0077f), new Vector3(0.0016f,0.001f,0.0003f), cream);
        WorldText("PURR\n<size=45%>CREAMY TREAT</size>", wrapper, new Vector3(0f,0.008f,-0.0071f), new Vector2(0.029f,0.024f), navy.color);
        WorldText("SALMON\n<size=70%>12 g  /  CAT CAFE</size>", wrapper, new Vector3(0f,-0.021f,-0.0071f), new Vector2(0.029f,0.018f), coral.color);
        var tip = new GameObject("Open tip").transform; tip.SetParent(go.transform, false); tip.localPosition = new Vector3(0f,0.081f,0f); tube.tip = tip;
        var paste = Part("Fresh salmon cream", PrimitiveType.Capsule, visuals, new Vector3(0f,0.084f,0f), new Vector3(0.008f,0.006f,0.007f), food);
        tube.paste = paste.transform;
        return tube;
    }
    static Mesh PacketMesh()
    {
        const int rows = 24, sides = 24;
        var vertices = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
        for (int y = 0; y <= rows; y++)
        {
            float t = y / (float)rows;
            float neck = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.78f,1f,t));
            float width = Mathf.Lerp(0.019f,0.010f,neck);
            float depth = 0.0058f * Mathf.Pow(Mathf.Sin(Mathf.PI * t),0.45f) + 0.0007f;
            for (int side = 0; side <= sides; side++)
            {
                float u = side/(float)sides, angle = u * Mathf.PI * 2f;
                float wrinkle = 1f + 0.025f * Mathf.Sin(t * 35f + angle * 3f);
                vertices.Add(new Vector3(Mathf.Cos(angle)*width*wrinkle, Mathf.Lerp(-0.074f,0.074f,t), Mathf.Sin(angle)*depth));
                uvs.Add(new Vector2(u,t));
                if (y == rows || side == sides) continue;
                int a=y*(sides+1)+side,b=a+sides+1;
                triangles.AddRange(new[] { a,b,a+1,a+1,b,b+1 });
            }
        }
        var mesh = new Mesh { name = "Wrinkled tapered squeeze-treat sachet" };
        mesh.SetVertices(vertices); mesh.SetUVs(0,uvs); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
    static Mesh SaveObj(Mesh mesh)
    {
        var text = new System.Text.StringBuilder("# Cat Cafe editable squeeze-treat packet\no SqueezeSachet\n");
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var v in mesh.vertices) text.AppendFormat(culture,"v {0:R} {1:R} {2:R}\n",v.x,v.y,v.z);
        foreach (var uv in mesh.uv) text.AppendFormat(culture,"vt {0:R} {1:R}\n",uv.x,uv.y);
        var triangles=mesh.triangles;
        for (int i=0;i<triangles.Length;i+=3)
        {
            int a=triangles[i]+1,b=triangles[i+1]+1,c=triangles[i+2]+1;
            text.AppendFormat(culture,"f {0}/{0} {1}/{1} {2}/{2}\n",a,b,c);
        }
        string path=Folder+"/SqueezeSachet.obj";
        System.IO.File.WriteAllText(path,text.ToString());
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        var imported=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().First();
        if (imported.vertexCount<100) throw new InvalidOperationException("Treat packet OBJ import lost geometry");
        return imported;
    }
    static void BuildMenu(WristTreatMenu menu, Transform root)
    {
        var panel = new GameObject("Left wrist treat menu", typeof(RectTransform), typeof(Canvas), typeof(TrackedDeviceGraphicRaycaster));
        panel.transform.SetParent(root,false);
        panel.transform.localScale = Vector3.one * 0.0006f;
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(440f,310f);
        var canvas = panel.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = Camera.main;
        menu.panel = panel.transform;
        UiBox("Panel background", panel.transform, Vector2.zero, new Vector2(440f,310f), new Color(0.025f,0.11f,0.13f,0.97f));
        UiText("CAT CAFE  /  TREATS", panel.transform, new Vector2(0f,116f), new Vector2(395f,35f), 22f, new Color(1f,0.85f,0.64f));
        UiText("A little treat. A happy cat.", panel.transform, new Vector2(0f,78f), new Vector2(395f,30f), 18f, Color.white);
        menu.offerButton = UiButton("Offer creamy treat", panel.transform, new Vector2(0f,23f), new Vector2(390f,64f), new Color(0.91f,0.33f,0.24f));
        menu.putAwayButton = UiButton("Put away", panel.transform, new Vector2(0f,-36f), new Vector2(390f,38f), new Color(0.13f,0.27f,0.29f));
        menu.status = UiText("Raise your left palm to choose", panel.transform, new Vector2(0f,-103f), new Vector2(400f,70f), 17f, new Color(0.85f,0.91f,0.86f));
        panel.transform.position = new Vector3(-2.05f,1.30f,2.65f);
        panel.transform.rotation = Quaternion.Euler(0f,180f,0f);
    }
    static Material Material(string name, Color color, float metallic, float smoothness)
    {
        var material = new Material(Shader.Find("HDRP/Lit")) { name = name, color = color };
        material.SetColor("_BaseColor",color); material.SetFloat("_Metallic",metallic); material.SetFloat("_Smoothness",smoothness);
        AssetDatabase.CreateAsset(material,Folder+"/"+name+".mat"); return material;
    }
    static GameObject Part(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var part=GameObject.CreatePrimitive(type); part.name=name; part.transform.SetParent(parent,false);
        part.transform.localPosition=position; part.transform.localScale=scale;
        execution.DestroyObject(part.GetComponent<Collider>());
        part.GetComponent<Renderer>().sharedMaterial=material; return part;
    }
    static TextMeshPro WorldText(string text, Transform parent, Vector3 position, Vector2 size, Color color)
    {
        var label=new GameObject("Packet label").AddComponent<TextMeshPro>(); label.transform.SetParent(parent,false);
        label.transform.localPosition=position; label.rectTransform.sizeDelta=size; label.text=text; label.color=color;
        label.font=Font(); label.alignment=TextAlignmentOptions.Center; label.enableAutoSizing=true; label.fontSizeMin=0.01f; label.fontSizeMax=1f;
        return label;
    }
    static TMP_FontAsset Font() => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
    static Image UiBox(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image)); go.transform.SetParent(parent,false);
        var rect=go.GetComponent<RectTransform>(); rect.anchoredPosition=position; rect.sizeDelta=size;
        var image=go.GetComponent<Image>(); image.color=color; image.raycastTarget=false; return image;
    }
    static TextMeshProUGUI UiText(string text, Transform parent, Vector2 position, Vector2 size, float fontSize, Color color)
    {
        var label=new GameObject(text,typeof(RectTransform)).AddComponent<TextMeshProUGUI>(); label.transform.SetParent(parent,false);
        label.rectTransform.anchoredPosition=position; label.rectTransform.sizeDelta=size; label.text=text;
        label.font=Font(); label.fontSize=fontSize; label.alignment=TextAlignmentOptions.Center; label.color=color; label.raycastTarget=false; return label;
    }
    static Button UiButton(string text, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var background=UiBox(text,parent,position,size,color); background.raycastTarget=true;
        var button=background.gameObject.AddComponent<Button>(); button.targetGraphic=background;
        UiText(text,background.transform,Vector2.zero,size,22f,Color.white); return button;
    }
}
