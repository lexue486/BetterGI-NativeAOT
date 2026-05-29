using System.Text.Json;
using OpenCvSharp;

var engine = new BettergiEngine();
Console.Error.WriteLine("[Bridge] BettergiEngine v2 initialized. Ready.");

while (true)
{
    var line = Console.ReadLine();
    if (line == null) break;
    if (string.IsNullOrWhiteSpace(line)) continue;
    
    string? response;
    try
    {
        var request = JsonSerializer.Deserialize<JsonElement>(line);
        response = engine.Execute(request);
    }
    catch (Exception ex)
    {
        response = JsonSerializer.Serialize(new { ok = false, error = $"Parse: {ex.Message}" });
    }
    Console.WriteLine(response);
    Console.Out.Flush();
}

public class BettergiEngine
{
    private readonly Dictionary<string, Func<JsonElement, string>> _handlers;
    private readonly Dictionary<string, Mat> _templateCache = new();
    private readonly string _workspace;
    
    public BettergiEngine()
    {
        _workspace = Environment.GetEnvironmentVariable("BRIDGE_WORKSPACE") 
                     ?? "/data/data/com.bettergi.android/files/bridge/";
        
        _handlers = new()
        {
            ["ping"] = _ => Ok(new { pong = true, version = "0.60.1-arm64" }),
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
    
    // ─── Template Matching ───
    private string HandleMatchTemplate(JsonElement req)
    {
        var args = GetArgs(req);
        var img = args("image"); var tpl = args("template");
        if (img == null || tpl == null) return Err("args.image + args.template required");
        if (!File.Exists(img)) return Err($"Image not found: {img}");
        
        var template = LoadTemplate(tpl);
        if (template == null) return Err($"Template '{tpl}' not loaded");
        
        using var image = Cv2.ImRead(img, ImreadModes.Color);
        if (image.Empty()) return Err("Failed to load image");
        
        using var result = new Mat();
        Cv2.MatchTemplate(image, template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);
        
        return maxVal > 0.7 
            ? Ok(new { found = true, x = maxLoc.X + template.Width/2, y = maxLoc.Y + template.Height/2, confidence = Math.Round(maxVal,4) })
            : Ok(new { found = false, confidence = Math.Round(maxVal,4) });
    }
    
    // ─── Batch Match ───
    private string HandleMatchAll(JsonElement req)
    {
        var args = GetArgs(req);
        var img = args("image");
        if (img == null || !File.Exists(img)) return Err("args.image required + must exist");
        
        using var image = Cv2.ImRead(img, ImreadModes.Color);
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
            
            using var r = new Mat();
            Cv2.MatchTemplate(image, tpl, r, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(r, out _, out double mv, out _, out OpenCvSharp.Point ml);
            
            results.Add(mv > 0.7 ? new { template=tName, found=true, x=ml.X+tpl.Width/2, y=ml.Y+tpl.Height/2, confidence=Math.Round(mv,4) }
                                  : new { template=tName, found=false, confidence=Math.Round(mv,4) });
        }
        return Ok(new { matches = results });
    }
    
    // ─── Load Template ───
    private string HandleLoadTemplate(JsonElement req)
    {
        var args = GetArgs(req);
        var tName = args("template");
        if (tName == null) return Err("args.template required");
        
        var tPath = args("path") ?? Path.Combine(_workspace, "templates", tName + ".png");
        if (!File.Exists(tPath)) return Err($"Template not found: {tPath}");
        
        var mat = Cv2.ImRead(tPath, ImreadModes.Color);
        if (mat.Empty()) return Err("Failed to load template");
        
        lock (_templateCache) _templateCache[tName] = mat;
        return Ok(new { template=tName, loaded=true, w=mat.Width, h=mat.Height });
    }
    
    // ─── Recognize (image info) ───
    private string HandleRecognize(JsonElement req)
    {
        var img = GetArgs(req)("image");
        if (img == null || !File.Exists(img)) return Ok(new { message="No image. Send args.image path.", readyForMatch=_templateCache.Count });
        
        using var m = Cv2.ImRead(img, ImreadModes.Color);
        return m.Empty() ? Err("Failed to load image") 
            : Ok(new { message="loaded", w=m.Width, h=m.Height, channels=m.Channels(), templatesReady=_templateCache.Count });
    }
    
    // ─── Status ───
    private string HandleStatus(JsonElement _)
        => Ok(new { engine="BetterGI v0.60.1", platform="linux-arm64", bridge="v2", cachedTemplates=_templateCache.Count });
    
    // ─── Helpers ───
    private Mat? LoadTemplate(string name)
    {
        lock (_templateCache)
        {
            if (_templateCache.TryGetValue(name, out var t)) return t;
        }
        var tp = Path.Combine(_workspace, "templates", name + ".png");
        if (!File.Exists(tp)) return null;
        var mat = Cv2.ImRead(tp, ImreadModes.Color);
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