# 版本更新说明 (CHANGELOG)

## v1.0.0 (2025-06-29)

### 🚀 首次发布

#### 核心功能
- ✅ **强制单例模式**：确保每个子类型只有一个实例
- ✅ **抽象基类设计**：通过继承定制配置和行为
- ✅ **线程安全**：全方位的并发安全保障
- ✅ **异步优先**：全面采用 async/await 模式

#### 网络功能
- ✅ **自动重连机制**：智能重连策略，支持指数退避算法
- ✅ **心跳检测**：可配置的应用层心跳包，确保连接状态
- ✅ **TCP Keep-Alive**：跨平台 TCP 层面的连接保活
- ✅ **数据队列**：基于 Channel 的现代化发送队列，支持背压处理

#### 性能优化
- ✅ **智能背压**：发送队列满时的优雅处理
- ✅ **资源管理**：完善的资源清理机制
- ✅ **跨平台支持**：Windows、Linux、macOS
- ✅ **高性能配置**：可调整的 Channel 参数

#### 依赖注入
- ✅ **DI 集成**：与 Microsoft.Extensions.DependencyInjection 完美集成
- ✅ **单例注册**：确保 DI 容器和基类单例机制一致

### 📋 技术规格

- **目标框架**：.NET 8.0
- **依赖包**：Microsoft.Extensions.DependencyInjection.Abstractions 9.0.6
- **支持平台**：Windows, Linux, macOS
- **代码行数**：约 1500+ 行（含详细注释）

### 🔧 主要 API

#### 核心类
```csharp
public abstract class TcpClientBase : IDisposable
{
    // 单例模式
    public static T GetInstance<T>() where T : TcpClientBase, new()
    
    // 连接管理
    public async Task<bool> TryConnectAsync()
    public async Task<bool> TryReconnectAsync()
    public void Disconnect()
    
    // 数据发送
    public async Task SendAsync(byte[] data)
    public async Task SendAsync(string message, Encoding? encoding = null)
    
    // 状态属性
    public bool IsConnected { get; }
    public bool IsDisposed { get; }
    
    // 事件
    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<Exception>? ErrorOccurred;
    public event Action<byte[], int>? DataReceived;
}
```

#### 依赖注入扩展
```csharp
public static class TcpClientServiceCollectionExtensions
{
    public static IServiceCollection AddTcpClient<T>(this IServiceCollection services)
        where T : TcpClientBase, new()
}
```

### ⚙️ 配置选项

#### 连接配置
- `MaxRetryCount`：最大重试次数（默认：3）
- `RetryIntervalMs`：重试间隔（默认：1000ms）
- `ConnectTimeoutMs`：连接超时（默认：5000ms）
- `EnableKeepAlive`：启用Keep-Alive（默认：true）

#### 发送配置
- `MaxSendQueueSize`：发送队列大小（默认：1000）
- `SendTimeoutMs`：发送超时（默认：5000ms）
- `SendQueueTimeoutMs`：队列等待超时（默认：5000ms）
- `SendQueueFullMode`：队列满处理策略（默认：Wait）

#### 接收配置
- `BufferSize`：接收缓冲区大小（默认：4096字节）
- `MaxMessageSize`：最大消息大小（默认：4096字节）
- `SocketReceiveTimeoutMs`：Socket接收超时（默认：0ms）

#### 心跳配置
- `HeartbeatData`：心跳包数据（默认：null）
- `HeartbeatExpectsResponse`：期望心跳响应（默认：true）
- `HeartbeatIntervalMs`：心跳间隔（默认：30000ms）

#### 重连配置
- `EnableSmartReconnection`：启用智能重连（默认：true）
- `MaxReconnectionAttempts`：最大重连次数（默认：10）
- `IdleTimeout`：空闲超时（默认：30分钟）

#### Keep-Alive配置
- `KeepAliveTimeSeconds`：Keep-Alive空闲时间（默认：10秒）
- `KeepAliveIntervalSeconds`：Keep-Alive探测间隔（默认：1秒）
- `KeepAliveRetryCount`：Keep-Alive重试次数（默认：3）

#### 高级性能配置
- `AllowSynchronousContinuations`：允许同步延续（默认：false）
- `SingleReader`：单读者模式（默认：true）
- `SingleWriter`：单写者模式（默认：false）

### 🏗️ 架构设计

#### 单例模式设计
- 使用 `ConcurrentDictionary` 缓存实例
- 按类型分别锁定，避免不同类型间锁竞争
- 双重检查锁定模式，确保线程安全
- 构造函数保护机制，防止直接实例化

#### 智能重连策略
- 指数退避算法：1s → 2s → 5s → 10s → 30s → 1m → 5m → 10m
- 随机抖动（±25%）避免雷群效应
- 连接成功后自动重置重连计数器
- 可配置的最大重连次数

#### 现代化数据处理
- 基于 `System.Threading.Channels` 的发送队列
- 智能背压处理，队列满时优雅等待
- 异步枚举器处理发送循环
- 可配置的 Channel 性能参数

#### 跨平台兼容性
- Windows：使用 IOControl 配置 Keep-Alive
- Linux/macOS：使用 SetSocketOption 配置 Keep-Alive
- 自动检测平台并选择最佳配置方式
- 配置失败时的优雅降级

### 🔧 内部实现细节

#### 连接管理
- 使用 `SemaphoreSlim` 确保连接操作的原子性
- `Poll` + `Available` 组合检测真实连接状态
- 统一的连接丢失处理机制
- 完善的资源清理流程

#### 数据收发
- 接收循环：纯异步，无超时限制
- 发送循环：基于 Channel 的高性能队列
- 数据大小检查和限制
- 优雅的错误处理和重试

#### 心跳机制
- 基于 `System.Timers.Timer` 的定时检测
- 支持期望响应和不期望响应的心跳模式
- 自动更新最后活动时间
- 心跳失败时的连接重建

### 📚 使用示例

#### 基本使用
```csharp
public class MyTcpClient : TcpClientBase
{
    protected override string ServerIP => "127.0.0.1";
    protected override int ServerPort => 8080;
}

var client = MyTcpClient.GetInstance<MyTcpClient>();
await client.TryConnectAsync();
await client.SendAsync("Hello Server!");
```

#### 依赖注入
```csharp
// Startup.cs
services.AddTcpClient<MyTcpClient>();

// Controller
public class ApiController : ControllerBase
{
    private readonly MyTcpClient _tcpClient;
    
    public ApiController(MyTcpClient tcpClient)
    {
        _tcpClient = tcpClient;
    }
}
```

### 🐛 已知限制

1. **单例限制**：每个子类型只能有一个实例
2. **平台相关**：Keep-Alive 配置在某些平台可能不完全支持
3. **内存使用**：大队列和大缓冲区会增加内存占用

### 📋 待优化项

1. **压缩支持**：未来版本可能添加数据压缩
2. **加密支持**：考虑添加 TLS/SSL 支持
3. **连接池**：多连接管理功能
4. **负载均衡**：多服务器负载均衡
5. **监控指标**：详细的性能监控指标

### 📖 文档

- `README.md`：完整的使用指南和功能介绍
- `API_REFERENCE.md`：详细的 API 参考文档
- `CHANGELOG.md`：版本更新说明（本文档）

### 🤝 贡献者

感谢所有为此项目做出贡献的开发者！

### 📄 许可证

本项目采用开源许可证，具体条款请参见 LICENSE 文件。

---

**DanKit.TCP.Client v1.0.0** - 专业的 .NET TCP 客户端解决方案
