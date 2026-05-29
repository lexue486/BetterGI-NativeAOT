# BetterGI C# Bridge - NativeAOT 版本

此项目是将原始的C# Bridge从独立可执行程序（ET_EXEC）改造为Android原生共享库（ET_DYN .so），通过JNI在应用进程内运行，绕过Android SELinux/seccomp-bpf的exec限制。

## 改造说明

### 主要变化

1. **项目类型变更**：
   - 从 `OutputType=Exe` 改为 `OutputType=Library`
   - 启用 `PublishAot=true` 以支持NativeAOT编译
   - 目标平台从 `linux-arm64` 改为 `android-arm64`

2. **入口点变更**：
   - 移除了基于stdin/stdout的IPC主循环
   - 实现了JNI接口函数：`JNI_OnLoad` 和 `Java_com_bettergi_bridge_BridgeNative_execute`
   - 所有通信改为方法调用而非标准输入输出

3. **依赖优化**：
   - 移除了Windows专用依赖（Vanara、WPF等）
   - 精简了MegaStubs.cs，只保留必要的存根
   - 保留了核心功能：OpenCvSharp4（模板匹配）

### 核心功能

- `ping`: 测试连接
- `match_template`: 模板匹配
- `match_all`: 批量匹配
- `load_template`: 加载模板
- `recognize`: 图像信息
- `status`: 状态查询

### 编译命令

```bash
dotnet publish Bridge.csproj \
  -c Release \
  -r android-arm64 \
  --self-contained true \
  -p:PublishAot=true \
  -p:IlcOptimizationPreference=Speed
```

输出：`bin/Release/net8.0/android-arm64/publish/libbettergi_bridge.so`

### Android端使用

将生成的`.so`文件放入`app/src/main/jniLibs/arm64-v8a/libbettergi_bridge.so`

Kotlin端调用方式：
```kotlin
val response = BridgeNative.execute("""{"cmd":"ping"}""")
```

### 注意事项

- 为保证NativeAOT兼容性，移除了部分高级功能（如V8脚本引擎、Git操作等）
- 模板缓存现在在进程内维护，需要注意线程安全
- 与原接口保持兼容，Android端业务代码无需修改