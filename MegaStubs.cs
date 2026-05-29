// MegaStubs_Android - Minimal stubs for NativeAOT compatibility
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

// ═══ Vanara PInvoke (only RECT needed) ═══
namespace Vanara.PInvoke {
    public struct RECT{public int Left,Top,Right,Bottom;}
}

// ═══ GI Actions Enum ═══
public enum GIActions{OpenPartySetupScreen,NormalAttack,Drop}

// ═══ GameTask Model.Area (needed for references) ═══
namespace BetterGenshinImpact.GameTask.Model.Area {
    public class ImageRegion : IDisposable {
        public bool IsExist() => false; 
        public ImageRegion Find(object o) => this;
        public OpenCvSharp.Mat SrcMat => null!; 
        public string Text => ""; 
        public int Right => 0; 
        public int Top => 0; 
        public int Height => 0;
        public void Dispose(){}
    }
}

// ═══ Core Config (needed for references) ═══
namespace BetterGenshinImpact.Core.Config {
    public class AllConfig { }
}