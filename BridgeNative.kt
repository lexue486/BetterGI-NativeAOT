// 新建文件：BridgeNative.kt
object BridgeNative {
    init {
        System.loadLibrary("bettergi_bridge")
    }
    
    // 对应 C# 的 Java_com_bettergi_bridge_BridgeNative_execute
    external fun execute(jsonCommand: String): String
}