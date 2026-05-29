#!/bin/bash
# ═══════════════════════════════════════════════════
# BetterGI C# Bridge — NativeAOT 编译脚本
# 目标: 将 C# 引擎编译为 libbettergi_bridge.so
# 运行环境: Windows/Linux x86_64 (需安装 .NET 8 SDK + Android NDK)
# ═══════════════════════════════════════════════════

set -e
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

echo "══════════════════════════════════════════"
echo " BetterGI Bridge NativeAOT 编译开始"
echo "══════════════════════════════════════════"

# ── 1. 检查 .NET SDK ──────────────────────────
echo ""
echo "[1/4] 检查 .NET SDK..."
if ! command -v dotnet &> /dev/null; then
    echo "❌ 未找到 dotnet"
    echo "   请安装 .NET 8 SDK:"
    echo "   https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
    echo "   安装后重启终端，运行: dotnet --version 验证"
    exit 1
fi
DOTNET_VER=$(dotnet --version)
echo "   ✅ dotnet 版本: $DOTNET_VER (需要8.x)"

# ── 2. 检查 Android NDK ──────────────────────────
echo ""
echo "[2/4] 检查 Android NDK..."
if [ -z "${ANDROID_NDK_HOME:-}" ]; then
    echo "   ⚠️  ANDROID_NDK_HOME 未设置"
    echo "   请下载 NDK r27 或更新版本:"
    echo "   https://developer.android.com/ndk/downloads"
    echo ""
    echo "   下载后解压到任意目录，然后:"
    echo "   export ANDROID_NDK_HOME=/path/to/android-ndk-r27"
    echo ""
    echo "   或者在编译时传参:"
    echo "   dotnet publish -p:AndroidNdkDirectory=/path/to/ndk ..."
    echo ""
    read -p "   继续编译？(y/N): " CONTINUE
    if [[ "$CONTINUE" != "y" && "$CONTINUE" != "Y" ]]; then
        echo "   已取消"
        exit 1
    fi
else
    echo "   ✅ NDK 路径: $ANDROID_NDK_HOME"
fi

# ── 3. 清理 ──────────────────────────
echo ""
echo "[3/4] 清理旧构建..."
dotnet clean Bridge.csproj -c Release 2>/dev/null || true
rm -rf bin obj

# ── 4. 编译 NativeAOT .so ──────────────────────────
echo ""
echo "[4/4] 编译 NativeAOT (.so)..."
echo "   目标: android-arm64"
echo "   配置: Release | PublishAot | StripSymbols"
echo ""

dotnet publish Bridge.csproj \
  -c Release \
  -r android-arm64 \
  --self-contained true \
  -p:PublishAot=true \
  -p:IlcOptimizationPreference=Speed \
  -p:StripSymbols=true

echo ""
echo "══════════════════════════════════════════"
echo " ✅ 编译成功！"
echo "══════════════════════════════════════════"
echo ""

# 查找输出
SO_FILE=$(find bin/Release -name "*.so" -type f 2>/dev/null | head -1)
if [ -n "$SO_FILE" ]; then
    echo "   输出: $SO_FILE"
    echo "   大小: $(du -h "$SO_FILE" | cut -f1)"
    echo ""
    echo "   下一步: 复制到 Android 项目 jniLibs:"
    echo "   cp \"$SO_FILE\" app/src/main/jniLibs/arm64-v8a/libbettergi_bridge.so"
else
    echo "   ⚠️  未找到 .so 文件，请检查编译日志"
    echo "   搜索路径: bin/Release/**/*.so"
fi

echo ""
echo "完成。"
