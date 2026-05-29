using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace BettergiBridge;

public static class JniBridge
{
    private static BettergiEngine? _engine;
    private static readonly object _lock = new();
    
    // JNI_OnLoad — 由 System.loadLibrary 自动调用
    [UnmanagedCallersOnly(EntryPoint = "JNI_OnLoad")]
    public static int JniOnLoad(nint vm, nint reserved)
    {
        _engine = new BettergiEngine();
        return 0x00010008; // JNI_VERSION_1_8
    }
    
    // 被 Kotlin 端调用的 JNI 方法
    // 签名：Java_com_bettergi_bridge_BridgeNative_execute
    [UnmanagedCallersOnly(EntryPoint = "Java_com_bettergi_bridge_BridgeNative_execute")]
    public static nint Execute(nint jniEnv, nint thiz, nint jsonStringPtr)
    {
        try
        {
            string jsonString = Marshal.PtrToStringAnsi(jsonStringPtr) ?? "";

            string result;
            try
            {
                var request = JsonSerializer.Deserialize<JsonElement>(jsonString);
                result = _engine?.Execute(request) ?? "{\"ok\":false,\"error\":\"Engine not initialized\"}";
            }
            catch (Exception ex)
            {
                result = JsonSerializer.Serialize(new { ok = false, error = $"Parse: {ex.Message}" });
            }

            byte[] resultBytes = Encoding.UTF8.GetBytes(result);
            nint resultPtr = Marshal.AllocHGlobal(resultBytes.Length + 1);
            Marshal.Copy(resultBytes, 0, resultPtr, resultBytes.Length);
            Marshal.WriteByte(resultPtr + resultBytes.Length, 0);
            
            return resultPtr;
        }
        catch (Exception ex)
        {
            string errorResult = JsonSerializer.Serialize(new { ok = false, error = $"JNI Error: {ex.Message}" });
            byte[] errorBytes = Encoding.UTF8.GetBytes(errorResult);
            nint errorPtr = Marshal.AllocHGlobal(errorBytes.Length + 1);
            Marshal.Copy(errorBytes, 0, errorPtr, errorBytes.Length);
            Marshal.WriteByte(errorPtr + errorBytes.Length, 0);
            return errorPtr;
        }
    }
}

public class BettergiEngine
{
    private readonly Dictionary<string, Func<JsonElement, string>> _handlers;
    private readonly Dictionary<string, OpenCvSharp.Mat> _templateCache = new();
    private readonly string _workspace;
    
    public BettergiEngine()
    {
        _workspace = Environment.GetEnvironmentVariable("BRIDGE_WORKSPACE") 
                     ?? "/data/data/com.bettergi.android/files/bridge/";
        
        _handlers = new()
        {
            ["ping"] = _ => Ok(new { pong = true, version = "0.60.1-arm64-nativeaot" }),
            ["match_template"] = HandleMatchTemplate,
            ["match_all"] = HandleMatchAll,
            ["load_template"] = HandleLoadTemplate,
            ["recognize"] = HandleRecognize,
            ["status"] = HandleStatus,
        };
    }
    
    public string Execute(JsonElement req)
    {
        var cmd = req.GetProperty("cmd").GetString() ?? "";
        if (!_handlers.TryGetValue(cmd, out var h))
            return Err($"Unknown: {cmd}");
        try { return h(req); }
        catch (Exception ex) { return Err(ex.Message); }
    }
    
    private string HandleMatchTemplate(JsonElement req)
    {
        var args = GetArgs(req);
        var img = args("image"); var tpl = args("template");
        if (img == null || tpl == null) return Err("args.image + args.template required");
        if (!System.IO.File.Exists(img)) return Err($"Image not found: {img}");
        
        var template = LoadTemplate(tpl);
        if (template == null) return Err($"Template '{tpl}' not loaded");
        
        using var image = OpenCvSharp.Cv2.ImRead(img, OpenCvSharp.ImreadModes.Color);
        if (image.Empty()) return Err("Failed to load image");
        
        using var result = new OpenCvSharp.Mat();
        OpenCvSharp.Cv2.MatchTemplate(image, template, result, OpenCvSharp.TemplateMatchModes.CCoeffNormed);
        OpenCvSharp.Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);
        
        return maxVal > 0.7 
            ? Ok(new { found = true, x = maxLoc.X + template.Width/2, y = maxLoc.Y + template.Height/2, confidence = Math.Round(maxVal,4) })
            : Ok(new { found = false, confidence = Math.Round(maxVal,4) });
    }
    
    private string HandleMatchAll(JsonElement req)
    {
        var args = GetArgs(req);
        var img = args("image");
        if (img == null || !System.IO.File.Exists(img)) return Err("args.image required + must exist");
        
        using var image = OpenCvSharp.Cv2.ImRead(img, OpenCvSharp.ImreadModes.Color);
        if (image.Empty()) return Err("Failed to load image");
        
        var tNames = new List<string>();
        if (req.GetProperty("args").TryGetProperty("templates", out var tj))
            foreach (var t in tj.EnumerateArray()) tNames.Add(t.GetString()!);
        if (tNames.Count == 0) return Err("args.templates array required");
        
        var results = new List<object>();
        foreach (var tName in tNames)
        {
            var tpl = LoadTemplate(tName);
            if (tpl == null) { results.Add(new { template=tName,found=false,error="not loaded"}); continue; }
            
            using var r = new OpenCvSharp.Mat();
            OpenCvSharp.Cv2.MatchTemplate(image, tpl, r, OpenCvSharp.TemplateMatchModes.CCoeffNormed);
            OpenCvSharp.Cv2.MinMaxLoc(r, out _, out double mv, out _, out OpenCvSharp.Point ml);
            
            results.Add(mv > 0.7 
                ? new { template=tName, found=true, x=ml.X+tpl.Width/2, y=ml.Y+tpl.Height/2, confidence=Math.Round(mv,4) }
                : new { template=tName, found=false, confidence=Math.Round(mv,4) });
        }
        return Ok(new { matches = results });
    }
    
    private string HandleLoadTemplate(JsonElement req)
    {
        var args = GetArgs(req);
        var tName = args("template");
        if (tName == null) return Err("args.template required");
        
        var tPath = args("path") ?? System.IO.Path.Combine(_workspace, "templates", tName + ".png");
        if (!System.IO.File.Exists(tPath)) return Err($"Template not found: {tPath}");
        
        var mat = OpenCvSharp.Cv2.ImRead(tPath, OpenCvSharp.ImreadModes.Color);
        if (mat.Empty()) return Err("Failed to load template");
        
        lock (_templateCache) _templateCache[tName] = mat;
        return Ok(new { template=tName, loaded=true, w=mat.Width, h=mat.Height });
    }
    
    private string HandleRecognize(JsonElement req)
    {
        var img = GetArgs(req)("image");
        if (img == null || !System.IO.File.Exists(img)) 
            return Ok(new { message="No image. Send args.image path.", readyForMatch=_templateCache.Count });
        
        using var m = OpenCvSharp.Cv2.ImRead(img, OpenCvSharp.ImreadModes.Color);
        return m.Empty() 
            ? Err("Failed to load image") 
            : Ok(new { message="loaded", w=m.Width, h=m.Height, channels=m.Channels(), templatesReady=_templateCache.Count });
    }
    
    private string HandleStatus(JsonElement _)
        => Ok(new { engine="BetterGI v0.60.1", platform="android-arm64-nativeaot", bridge="v2", cachedTemplates=_templateCache.Count });
    
    private OpenCvSharp.Mat? LoadTemplate(string name)
    {
        lock (_templateCache)
        {
            if (_templateCache.TryGetValue(name, out var t)) return t;
        }
        var tp = System.IO.Path.Combine(_workspace, "templates", name + ".png");
        if (!System.IO.File.Exists(tp)) return null;
        var mat = OpenCvSharp.Cv2.ImRead(tp, OpenCvSharp.ImreadModes.Color);
        lock (_templateCache) _templateCache[name] = mat;
        return mat;
    }
    
    private static Func<string, string?> GetArgs(JsonElement req)
    {
        var has = req.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Object;
        return key => has && a.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
    
    private static string Ok(object d) => JsonSerializer.Serialize(new { ok = true, data = d });
    private static string Err(string m) => JsonSerializer.Serialize(new { ok = false, error = m });
}
