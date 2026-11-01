using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;
using Unity.Profiling;

/// <summary>Opt-in, bounded capture. Never scans scenes or writes files every frame.</summary>
[InitializeOnLoad]
public static class QuestPreviewDiagnostics
{
    const string Key = "CatCafe.QuestPreviewCapture";
    static readonly string[] MarkerNames = { "CatCafe.Treat.Recruit", "CatCafe.Treat.Feed", "CatCafe.Treat.CatAI", "CatCafe.Treat.Navigation" };
    static readonly List<Row> rows = new List<Row>(6000);
    static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
    static readonly FrameTiming[] timings = new FrameTiming[1];
    static ProfilerRecorder[] recorders;
    static XRDisplaySubsystem display;
    static CatTreatTube tube;
    static double started, nextDisplayLookup, readySince = -1d;
    static int lastFrame = -1;
    static bool initialized, reduced;
    struct Row
    {
        public double seconds, frame, gpu, cpuMain, cpuRender, appGpu, compositorGpu, recruit, contact, ai, navigation;
        public int drops, presents;
        public float scale;
        public bool held, eating;
    }
    static QuestPreviewDiagnostics()
    {
        if (SessionState.GetBool(Key,false)) EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key,false)) Finish("Play mode stopped");
        };
    }
    public static void Begin()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Start Quest capture from Edit mode.");
        int balanced = Array.IndexOf(QualitySettings.names,"Balanced");
        if (balanced >= 0) QualitySettings.SetQualityLevel(balanced,true);
        SessionState.SetBool(Key,true);
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isPaused) return;
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (!initialized)
            {
                initialized = true; started = now;
                tube = UnityEngine.Object.FindFirstObjectByType<CatTreatTube>();
                recorders = MarkerNames.Select(n => ProfilerRecorder.StartNew(ProfilerCategory.Scripts,n,1,
                    ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame)).ToArray();
                Debug.Log("QUEST_CAPTURE_START: Balanced, 100% eye buffer for 18s, then 80% for 22s. Use treats normally.");
            }
            if (now >= nextDisplayLookup)
            {
                nextDisplayLookup = now + 1d;
                displays.Clear(); SubsystemManager.GetSubsystems(displays);
                display = displays.FirstOrDefault(d => d.running);
            }
            if (display == null)
            {
                if (now-started > 25d) Finish("No running XR display; headset test incomplete");
                return;
            }
            bool gpuAvailable = display.TryGetAppGPUTimeLastFrame(out float appGpu);
            display.TryGetFramePresentCount(out int presents);
            if (readySince < 0d)
            {
                if (!gpuAvailable || appGpu <= 0f || presents <= 0)
                {
                    if (now-started > 25d) Finish("No valid headset presentation; test incomplete");
                    return;
                }
                readySince = now;
                display.scaleOfAllRenderTargets = 1f;
            }
            double elapsed = now-readySince;
            if (!reduced && elapsed >= 18d)
            {
                reduced = true;
                display.scaleOfAllRenderTargets = 0.8f;
                Debug.Log("QUEST_CAPTURE_SCALE: eye buffer 80% per axis; pixel count approximately 64% of baseline.");
            }
            if (elapsed >= 40d) { Finish("40-second headset capture completed"); return; }
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            FrameTimingManager.CaptureFrameTimings();
            bool frameTiming = FrameTimingManager.GetLatestTimings(1,timings)>0;
            int drops = -1;
            if (display.TryGetDroppedFrameCount(out int dropped)) drops = dropped;
            double compositor = display.TryGetCompositorGPUTimeLastFrame(out float comp) ? comp*1000d : -1d;
            rows.Add(new Row {
                seconds=elapsed, frame=Time.unscaledDeltaTime*1000d,
                gpu=frameTiming ? timings[0].gpuFrameTime : -1d,
                cpuMain=frameTiming ? timings[0].cpuMainThreadFrameTime : -1d,
                cpuRender=frameTiming ? timings[0].cpuRenderThreadFrameTime : -1d,
                appGpu=gpuAvailable ? appGpu*1000d : -1d, compositorGpu=compositor,
                drops=drops,presents=presents,scale=display.scaleOfAllRenderTargets,
                held=tube && tube.IsHeld,eating=tube && tube.FeedingCat,
                recruit=Read(0),contact=Read(1),ai=Read(2),navigation=Read(3)
            });
        }
        catch (Exception ex) { Debug.LogError("QUEST_CAPTURE_ERROR: "+ex); Finish("Capture error"); }
    }
    static double Read(int index) => recorders[index].Valid ? recorders[index].LastValue / 1000000d : -1d;
    static void Finish(string reason)
    {
        SessionState.SetBool(Key,false);
        EditorApplication.update -= Tick;
        if (recorders != null) foreach (var recorder in recorders) recorder.Dispose();
        Directory.CreateDirectory("Logs/QuestDiagnostics");
        var csv = new StringBuilder("seconds,scale,frame_ms,gpu_ms,cpu_main_ms,cpu_render_ms,xr_app_gpu_ms,xr_compositor_gpu_ms,dropped,presented,held,eating,recruit_ms,food_contact_ms,cat_ai_ms,navigation_ms\n");
        foreach (var r in rows) csv.AppendLine(string.Join(",", new object[] { r.seconds,r.scale,r.frame,r.gpu,r.cpuMain,r.cpuRender,r.appGpu,r.compositorGpu,r.drops,r.presents,r.held?1:0,r.eating?1:0,r.recruit,r.contact,r.ai,r.navigation }.Select(v => Convert.ToString(v,CultureInfo.InvariantCulture))));
        File.WriteAllText("Logs/QuestDiagnostics/QuestPreviewCapture.csv",csv.ToString());
        var summary = new StringBuilder(reason+"\n");
        summary.AppendLine("Warmup (first 3s) and eye-buffer transition (18–21s) excluded. User motion is not controlled.");
        Summarize(summary,"100%",rows.Where(r=>r.seconds>3d && r.seconds<18d).ToArray());
        Summarize(summary,"80%",rows.Where(r=>r.seconds>21d).ToArray());
        File.WriteAllText("Logs/QuestDiagnostics/QuestPreviewCapture.txt",summary.ToString());
        Debug.Log("QUEST_CAPTURE_DONE: "+summary);
    }
    static void Summarize(StringBuilder text,string name,Row[] values)
    {
        if (values.Length==0) { text.AppendLine(name+": no valid samples"); return; }
        text.AppendLine(name+" samples="+values.Length+" held="+values.Count(r=>r.held)+" eating="+values.Count(r=>r.eating));
        foreach (var item in new[] {
            ("frame",values.Select(r=>r.frame)), ("XR app GPU",values.Select(r=>r.appGpu)),
            ("CPU main",values.Select(r=>r.cpuMain)), ("recruit",values.Select(r=>r.recruit)),
            ("food contact",values.Select(r=>r.contact)), ("cat AI",values.Select(r=>r.ai)), ("navigation",values.Select(r=>r.navigation)) })
        {
            var sorted=item.Item2.Where(v=>v>=0d).OrderBy(v=>v).ToArray();
            if (sorted.Length>0) text.AppendLine(item.Item1+" ms median="+sorted[sorted.Length/2].ToString("F3")+
                " p95="+sorted[(int)((sorted.Length-1)*0.95d)].ToString("F3")+" max="+sorted[sorted.Length-1].ToString("F3"));
        }
        var drops=values.Where(r=>r.drops>=0).ToArray();
        if (drops.Length>0) text.AppendLine("XR dropped-frame counter delta="+(drops.Last().drops-drops.First().drops));
    }
}
