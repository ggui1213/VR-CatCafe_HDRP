using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class CatTeaserWandBuilder
{
    const string Folder = "Assets/Cat/TeaserWand";
    [MenuItem("Cat Cafe/Build Cat Teaser Wand")]
    public static void Build()
    {
        if (GameObject.Find("Cat Teaser Wand")) { Debug.Log("Cat Teaser Wand already exists."); return; }
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Cat", "TeaserWand");
        var teal = Material("Ocean grip", new Color(0.025f, 0.4f, 0.5f), 0.1f);
        var dark = Material("Graphite flexible rod", new Color(0.045f, 0.065f, 0.075f), 0.25f);
        var brass = Material("Warm brass", new Color(0.85f, 0.58f, 0.21f), 0.7f);
        var coral = Material("Coral feather", new Color(0.95f, 0.24f, 0.16f), 0f);
        var gold = Material("Honey feather", new Color(1f, 0.65f, 0.18f), 0f);
        var cream = Material("Ivory feather", new Color(0.95f, 0.89f, 0.73f), 0f);
        var root = new GameObject("Cat Teaser Wand");
        Undo.RegisterCreatedObjectUndo(root, "Create interactive cat teaser");
        var wand = root.AddComponent<CatTeaserWand>();
        var grip = new GameObject("Grab Handle"); grip.transform.SetParent(root.transform);
        grip.transform.position = new Vector3(-2.4f, 0.58f, 2.8f);
        var body = grip.AddComponent<Rigidbody>(); body.mass = 0.16f; body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var gripCollider = grip.AddComponent<CapsuleCollider>(); gripCollider.radius = 0.022f; gripCollider.height = 0.22f;
        Part("Soft grip", PrimitiveType.Capsule, grip.transform, Vector3.zero, new Vector3(0.038f, 0.105f, 0.038f), teal);
        for (int i = 0; i < 7; i++)
            Part("Grip rib", PrimitiveType.Cylinder, grip.transform, new Vector3(0, -0.075f + i * 0.024f, 0), new Vector3(0.041f, 0.002f, 0.041f), dark);
        Part("End cap", PrimitiveType.Sphere, grip.transform, new Vector3(0,-0.095f,0), new Vector3(0.04f,0.018f,0.04f), brass);
        Part("Ferrule", PrimitiveType.Cylinder, grip.transform, new Vector3(0,0.11f,0), new Vector3(0.026f,0.016f,0.026f), brass);
        var shaft = new GameObject("Curved flexible shaft"); shaft.transform.SetParent(grip.transform, false);
        var shaftMesh = Tube(); AssetDatabase.CreateAsset(shaftMesh, Folder + "/CurvedRod.asset");
        shaft.AddComponent<MeshFilter>().sharedMesh = shaftMesh;
        shaft.AddComponent<MeshRenderer>().sharedMaterial = dark;
        var anchor = new GameObject("Tether eye").transform; anchor.SetParent(grip.transform, false); anchor.localPosition = RodPoint(1);
        Part("Tip ferrule", PrimitiveType.Sphere, anchor, Vector3.zero, Vector3.one * 0.018f, brass);
        var grab = grip.AddComponent<XRGrabInteractable>(); grab.colliders.Clear(); grab.colliders.Add(gripCollider);
        grab.addDefaultGrabTransformers = false;
        var palmTransformer = grip.AddComponent<WandPalmGrabTransformer>(); palmTransformer.wand = wand;
        grab.startingSingleGrabTransformers.Clear(); grab.startingSingleGrabTransformers.Add(palmTransformer);
        grab.interactionLayers = (InteractionLayerMask)1;
        grab.movementType = XRBaseInteractable.MovementType.Kinematic;
        grab.throwOnDetach = false;
        var attach = new GameObject("Hand grip pose").transform; attach.SetParent(grip.transform, false);
        attach.localRotation = Quaternion.Inverse(wand.HeldGripRotation);
        grab.attachTransform = attach;
        var rope = new List<Rigidbody>(); Rigidbody previous = body;
        const int links = 6; const float length = 0.07f;
        for (int i = 0; i < links; i++)
        {
            var link = new GameObject(i == links - 1 ? "Feather lure" : "Tether link " + i);
            link.transform.SetParent(root.transform);
            link.transform.position = anchor.position - Vector3.up * (length * (i + 1));
            var rb = link.AddComponent<Rigidbody>(); rb.mass = i == links - 1 ? 0.025f : 0.012f;
            rb.linearDamping = i == links - 1 ? 2.5f : 3.5f; rb.angularDamping = 6f;
            rb.maxAngularVelocity = 5f; rb.sleepThreshold = 0.015f;
            rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.solverIterations = 24; rb.solverVelocityIterations = 12;
            var col = link.AddComponent<SphereCollider>(); col.radius = i == links - 1 ? 0.035f : 0.007f;
            var joint = link.AddComponent<ConfigurableJoint>(); joint.connectedBody = previous;
            joint.autoConfigureConnectedAnchor = false; joint.anchor = Vector3.zero;
            joint.connectedAnchor = i == 0 ? body.transform.InverseTransformPoint(anchor.position) : Vector3.zero;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Limited;
            joint.linearLimit = new SoftJointLimit { limit = length, contactDistance = 0.003f, bounciness = 0f };
            // A rope limits distance; a very stiff spring on tiny masses injects solver jitter.
            joint.linearLimitSpring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.projectionMode = JointProjectionMode.None;
            joint.enableCollision = false; rope.Add(rb); previous = rb;
        }
        var lure = rope[rope.Count - 1];
        Part("Bell", PrimitiveType.Sphere, lure.transform, Vector3.zero, new Vector3(0.05f,0.06f,0.05f), brass);
        for (int i = 0; i < 5; i++)
        {
            var feather = new GameObject("Feather " + i); feather.transform.SetParent(lure.transform, false);
            feather.transform.localRotation = Quaternion.Euler(10 + i * 5, i * 72, -22 + i * 10);
            var mesh = Feather(0.16f + i * 0.012f); AssetDatabase.CreateAsset(mesh, Folder + "/Feather" + i + ".asset");
            feather.AddComponent<MeshFilter>().sharedMesh = mesh;
            feather.AddComponent<MeshRenderer>().sharedMaterial = i % 3 == 0 ? cream : i % 2 == 0 ? gold : coral;
            for (int j = 1; j < 9; j++)
            {
                float t = j / 10f;
                var barb = Part("Feather barb", PrimitiveType.Cube, feather.transform,
                    new Vector3(0, -t * 0.16f, 0.002f), new Vector3(Mathf.Sin(t * Mathf.PI) * 0.045f,0.0011f,0.0009f), gold);
                barb.transform.localRotation = Quaternion.Euler(0,0,18);
            }
        }
        var line = root.AddComponent<LineRenderer>(); line.sharedMaterial = cream;
        line.startWidth = 0.003f; line.endWidth = 0.002f; line.numCornerVertices = 3; line.numCapVertices = 3;
        line.useWorldSpace = true; line.positionCount = rope.Count + 1;
        line.SetPosition(0, anchor.position); for(int i=0;i<rope.Count;i++)line.SetPosition(i+1,rope[i].position);
        var labelRoot = new GameObject("Pickup hint").transform; labelRoot.SetParent(root.transform);
        var label = new GameObject("Pick up to play").AddComponent<TextMeshPro>(); label.transform.SetParent(labelRoot,false);
        label.text = "<b>PICK UP</b>\n<size=65%>Play with a cat</size>"; label.fontSize=1f; label.alignment=TextAlignmentOptions.Center;
        label.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        label.color=new Color(1,0.93f,0.75f); label.rectTransform.sizeDelta=new Vector2(0.65f,0.3f);
        labelRoot.gameObject.SetActive(false);
        var origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath("c348712bda248c246b8c49b3db54643f"));
        var actionRefs = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(actions)).OfType<InputActionReference>()
            .Where(r => r.action != null && (r.action.name == "Move" || r.action.name == "Teleport Mode")).ToArray();
        wand.grab=grab; wand.handle=body; wand.lure=lure; wand.tetherAnchor=anchor;
        wand.tetherBodies=rope.ToArray(); wand.tetherLine=line; wand.player=origin.transform;
        wand.hintRoot=labelRoot; wand.locomotionActions=actionRefs;
        // The display base stays behind when the player grabs the handle.
        var stand = new GameObject("Wand display stand"); stand.transform.SetParent(root.transform);
        stand.transform.position = new Vector3(-2.4f,0,2.8f);
        Part("Weighted base",PrimitiveType.Cylinder,stand.transform,new Vector3(0,0.022f,0),new Vector3(0.26f,0.022f,0.26f),teal);
        Part("Stand stem",PrimitiveType.Cylinder,stand.transform,new Vector3(0,0.25f,0),new Vector3(0.018f,0.23f,0.018f),brass);
        Part("Handle cradle",PrimitiveType.Sphere,stand.transform,new Vector3(0,0.47f,0),new Vector3(0.07f,0.025f,0.07f),dark);
        var standCol=stand.AddComponent<CapsuleCollider>();standCol.center=new Vector3(0,0.24f,0);standCol.height=0.48f;standCol.radius=0.055f;
        var obstacle=stand.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Capsule;obstacle.center=standCol.center;
        obstacle.radius=0.1f;obstacle.height=0.48f;obstacle.carving=true;
        var cols=root.GetComponentsInChildren<Collider>();
        foreach(var a in cols)foreach(var b in cols)if(a!=b && a.gameObject!=stand && b.gameObject!=stand)Physics.IgnoreCollision(a,b);
        AddCatAnimations();
        foreach(var cat in Object.FindObjectsByType<CatIndoorRoaming>(FindObjectsSortMode.None))
        {
            var chaser=cat.GetComponent<CatToyChaser>();if(!chaser)chaser=Undo.AddComponent<CatToyChaser>(cat.gameObject);
            chaser.leftPaw=cat.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="L Hand");
            chaser.rightPaw=cat.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="R Hand");EditorUtility.SetDirty(chaser);
        }
        PrefabUtility.SaveAsPrefabAsset(root,Folder+"/Cat Teaser Wand.prefab");
        EditorSceneManager.MarkSceneDirty(root.scene);AssetDatabase.SaveAssets();
        Debug.Log("Created physical grab wand and enabled toy play for every cat.");
    }

    static void AddCatAnimations()
    {
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Cat/CatRoaming.controller");
        var map=new Dictionary<string,string>{{"ToyPawLeft","Attacks/Cat_Attack_Paw_Left.fbx"},{"ToyPawRight","Attacks/Cat_Attack_Paw_Right.fbx"},
            {"ToyScratch","Attacks/Cat_Attack_Scratch.fbx"},{"ToyJump","Jump/Cat_Jump_InPlace.fbx"}};
        foreach(var pair in map)
        {
            if(controller.layers[0].stateMachine.states.Any(s=>s.state.name==pair.Key))continue;
            var clip=AssetDatabase.LoadAllAssetsAtPath("Assets/Malbers Animations/Animals Packs/04 Pets/Cat/Animations/"+pair.Value)
                .OfType<AnimationClip>().First(c=>!c.name.StartsWith("__") && (pair.Key!="ToyJump" || c.name=="Cat_Jump_InPlace"));
            var copy=Object.Instantiate(clip);copy.name=pair.Key;AnimationUtility.SetAnimationEvents(copy,new AnimationEvent[0]);
            AssetDatabase.CreateAsset(copy,Folder+"/"+pair.Key+".anim");
            var state=controller.layers[0].stateMachine.AddState(pair.Key);state.motion=copy;
        }
        if (!controller.layers[0].stateMachine.states.Any(s => s.state.name == "ToyWatch"))
        {
            var turn = controller.layers[0].stateMachine.states.First(s => s.state.name == "Turn").state.motion as BlendTree;
            var idle = turn.children.First(c => Mathf.Abs(c.threshold) < 0.01f).motion;
            controller.layers[0].stateMachine.AddState("ToyWatch").motion = idle;
        }
        EditorUtility.SetDirty(controller);
    }

    static Material Material(string name,Color color,float metallic)
    {
        var path=Folder+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find("HDRP/Lit"));m.name=name;AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",0.4f);
        m.SetFloat("_DoubleSidedEnable",1);m.SetFloat("_CullMode",0);EditorUtility.SetDirty(m);return m;
    }
    static GameObject Part(string name,PrimitiveType type,Transform parent,Vector3 pos,Vector3 scale,Material mat)
    {
        var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=mat;return go;
    }
    static Vector3 RodPoint(float t) {return new Vector3(0.34f*t*t,0.12f+0.75f*t-0.19f*t*t*t,0);}
    static Mesh Tube()
    {
        const int steps=30,sides=8;var v=new List<Vector3>();var tris=new List<int>();
        for(int i=0;i<=steps;i++){float t=i/(float)steps;var tangent=(RodPoint(Mathf.Min(1,t+0.01f))-RodPoint(Mathf.Max(0,t-0.01f))).normalized;
            var normal=Vector3.Cross(tangent,Vector3.forward).normalized;float radius=Mathf.Lerp(0.007f,0.003f,t);
            for(int j=0;j<sides;j++){float a=j*2*Mathf.PI/sides;v.Add(RodPoint(t)+radius*(normal*Mathf.Cos(a)+Vector3.forward*Mathf.Sin(a)));
                if(i<steps){int k=i*sides+j,n=i*sides+(j+1)%sides;tris.AddRange(new[]{k,n,k+sides,n,n+sides,k+sides});}}}
        var mesh=new Mesh{name="Tapered curved wand shaft"};mesh.SetVertices(v);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();return mesh;
    }
    static Mesh Feather(float length)
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
        for(int i=0;i<=16;i++){float t=i/16f;float width=Mathf.Sin(t*Mathf.PI)*0.024f;
            v.Add(new Vector3(-width,-t*length,0.012f*Mathf.Sin(t*Mathf.PI)));v.Add(new Vector3(width,-t*length,0.012f*Mathf.Sin(t*Mathf.PI)));
            uv.Add(new Vector2(0,t));uv.Add(new Vector2(1,t));if(i<16){int k=i*2;tris.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3,k+1,k+2,k,k+3,k+2,k+1});}}
        var mesh=new Mesh{name="Curved feather vane"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();return mesh;
    }
}
