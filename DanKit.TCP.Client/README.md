# DanKit.TCP.Client

一个功能强大的 .NET TCP 客户端库，基于抽象基类设计，提供单例模式、自动重连、心跳检测等企业级功能。

> 🇨🇳 国内镜像访问：[Gitee 镜像仓库](https://gitee.com/wjire/dan-kit/tree/master/DanKit.TCP.Client)


## 🚀 核心特性

### 🔧 设计模式

- **强制单例模式**：每个子类型只能有一个实例，确保资源正确管理
- **抽象基类设计**：通过继承定制配置和行为
- **线程安全**：全方位的并发安全保障

### 📡 网络功能

- **自动重连机制**：智能重连策略，支持指数退避算法
- **心跳检测**：可配置的应用层心跳包，确保连接状态
- **TCP Keep-Alive**：跨平台 TCP 层面的连接保活
- **数据队列**：基于 Channel 的现代化发送队列，支持背压处理

### ⚡ 性能优化

- **异步优先**：全面采用 async/await 模式
- **智能背压**：发送队列满时的优雅处理
- **资源管理**：完善的资源清理机制
- **跨平台**：支持 Windows、Linux、macOS

## 📦 安装要求

- **.NET 8.0** 或更高版本
- **Microsoft.Extensions.DependencyInjection.Abstractions 9.0.6**

## 🛠️ 快速开始

### 1. 创建自定义 TCP 客户端

```csharp
using DanKit.TCP.Client;

public class MyTcpClient : TcpClientBase
{
    // 必须实现：服务器连接信息
    protected override string ServerIP => "127.0.0.1";
    protected override int ServerPort => 8080;
    
    // 可选：自定义心跳包
    protected override byte[]? HeartbeatData => 
        Encoding.UTF8.GetBytes("PING");
    
    // 可选：重写配置
    protected override int MaxRetryCount => 5;
    protected override int HeartbeatIntervalMs => 30000;
}
```

### 2. 使用客户端

```csharp
// 获取单例实例
var client = MyTcpClient.GetInstance<MyTcpClient>();

// 订阅事件
client.Connected += () => Console.WriteLine("连接成功！");
client.Disconnected += () => Console.WriteLine("连接断开！");
client.DataReceived += (buffer, length) => 
{
    var message = Encoding.UTF8.GetString(buffer, 0, length);
    Console.WriteLine($"收到数据: {message}");
};
client.ErrorOccurred += (ex) => Console.WriteLine($"错误: {ex.Message}");

// 连接到服务器
await client.TryConnectAsync();

// 发送数据
await client.SendAsync("Hello Server!");
await client.SendAsync(Encoding.UTF8.GetBytes("Binary Data"));

// 关闭连接
client.Dispose();
```

### 3. 依赖注入集成

```csharp
// 注册服务
services.AddTcpClient<MyTcpClient>();

// 使用服务
public class MyService
{
    private readonly MyTcpClient _tcpClient;
    
    public MyService(MyTcpClient tcpClient)
    {
        _tcpClient = tcpClient;
    }
    
    public async Task SendMessageAsync(string message)
    {
        await _tcpClient.SendAsync(message);
    }
}
```

## ⚙️ 详细配置

### 连接配置

| 属性 | 默认值 | 说明 |
|------|--------|------|
| `MaxRetryCount` | 3 | 连接失败时的最大重试次数 |
| `RetryIntervalMs` | 1000 | 重试连接的间隔时间（毫秒） |
| `ConnectTimeoutMs` | 5000 | TCP连接超时时间（毫秒） |
| `EnableKeepAlive` | true | 是否启用TCP Keep-Alive机制 |

### 发送配置

| 属性 | 默认值 | 说明 |
|------|--------|------|
| `MaxSendQueueSize` | 1000 | 发送队列最大长度限制 |
| `SendTimeoutMs` | 5000 | 发送数据的超时时间（毫秒） |
| `SendQueueTimeoutMs` | 5000 | 发送队列等待超时时间（毫秒） |
| `SendQueueFullMode` | Wait | 发送队列满时的行为策略 |

### 接收配置

| 属性 | 默认值 | 说明 |
|------|--------|------|
| `BufferSize` | 4096 | 接收缓冲区大小（字节） |
| `MaxMessageSize` | 4096 | 单个消息的最大字节数限制 |
| `SocketReceiveTimeoutMs` | 0 | Socket级别的接收超时时间（毫秒） |

### 心跳配置

| 属性 | 默认值 | 说明 |
|------|--------|------|
| `HeartbeatData` | null | 自定义心跳包数据，null表示不使用心跳 |
| `HeartbeatExpectsResponse` | true | 心跳包是否期望服务端响应 |
| `HeartbeatIntervalMs` | 30000 | 心跳检测间隔时间（毫秒） |

### 重连配置

| 属性 | 默认值 | 说明 |
|------|--------|------|
| `EnableSmartReconnection` | true | 是否启用智能重连策略 |
| `MaxReconnectionAttempts` | 10 | 智能重连的最大尝试次数 |
| `IdleTimeout` | 30分钟 | 闲置连接自动清理超时时间 |

## 📚 高级用法

### 自定义重连策略

```csharp
public class CustomTcpClient : TcpClientBase
{
    protected override string ServerIP => "192.168.1.100";
    protected override int ServerPort => 9999;
    
    // 更激进的重连策略
    protected override bool EnableSmartReconnection => true;
    protected override int MaxReconnectionAttempts => 20;
    protected override int RetryIntervalMs => 500;
    
    // 自定义心跳检测
    protected override byte[]? HeartbeatData => 
        new byte[] { 0x01, 0x02, 0x03, 0x04 }; // 二进制心跳包
    protected override bool HeartbeatExpectsResponse => true;
    protected override double HeartbeatIntervalMs => 15000; // 15秒心跳
}
```

### 事件处理最佳实践

```csharp
public class BusinessTcpClient : TcpClientBase
{
    protected override string ServerIP => "business.server.com";
    protected override int ServerPort => 8080;
    
    protected override void OnConnected()
    {
        base.OnConnected(); // 调用基类触发事件
        
        // 自定义连接成功逻辑
        Console.WriteLine("业务连接已建立，开始同步数据...");
        _ = Task.Run(SyncDataAsync);
    }
    
    protected override void OnDataReceived(byte[] buffer, int length)
    {
        base.OnDataReceived(buffer, length); // 调用基类触发事件
        
        // 自定义数据处理逻辑
        ProcessBusinessData(buffer, length);
    }
    
    protected override void OnErrorOccurred(Exception exception)
    {
        base.OnErrorOccurred(exception); // 调用基类触发事件
        
        // 自定义错误处理
        LogError(exception);
        NotifyAdministrator(exception);
    }
    
    private async Task SyncDataAsync()
    {
        // 连接后的业务逻辑
        await SendAsync("SYNC_REQUEST");
    }
    
    private void ProcessBusinessData(byte[] buffer, int length)
    {
        // 业务数据处理逻辑
        var message = Encoding.UTF8.GetString(buffer, 0, length);
        // 处理消息...
    }
    
    private void LogError(Exception ex)
    {
        // 错误日志记录
    }
    
    private void NotifyAdministrator(Exception ex)
    {
        // 错误通知机制
    }
}
```

### 高性能配置

```csharp
public class HighPerformanceTcpClient : TcpClientBase
{
    protected override string ServerIP => "high-perf.server.com";
    protected override int ServerPort => 8080;
    
    // 大容量发送队列
    protected override int MaxSendQueueSize => 10000;
    
    // 大缓冲区
    protected override int BufferSize => 65536; // 64KB
    protected override int MaxMessageSize => 1048576; // 1MB
    
    // 优化的 Channel 配置
    protected override bool SingleReader => true;   // 单读者优化
    protected override bool SingleWriter => false;  // 支持多写者
    protected override bool AllowSynchronousContinuations => false; // 异步优化
    
    // 快速超时设置
    protected override int ConnectTimeoutMs => 3000;
    protected override int SendTimeoutMs => 3000;
    
    // 频繁心跳
    protected override double HeartbeatIntervalMs => 10000; // 10秒
}
```

## 🔍 监控和调试

### 连接状态监控

```csharp
var client = MyTcpClient.GetInstance<MyTcpClient>();

// 检查连接状态
if (client.IsConnected)
{
    Console.WriteLine("客户端已连接");
}

// 检查对象状态
if (client.IsDisposed)
{
    Console.WriteLine("客户端已释放");
}

// 获取所有活跃实例
var allClients = TcpClientBase.GetAllInstances();
Console.WriteLine($"当前活跃客户端数量: {allClients.Count()}");
```

### 错误处理策略

```csharp
client.ErrorOccurred += (exception) =>
{
    switch (exception)
    {
        case SocketException sockEx:
            Console.WriteLine($"网络错误: {sockEx.ErrorCode} - {sockEx.Message}");
            break;
            
        case TimeoutException timeoutEx:
            Console.WriteLine($"超时错误: {timeoutEx.Message}");
            break;
            
        case ObjectDisposedException:
            Console.WriteLine("客户端已被释放");
            break;
            
        default:
            Console.WriteLine($"未知错误: {exception.Message}");
            break;
    }
};
```

## ⚠️ 注意事项

### 单例模式约束

- **禁止直接 new**：必须使用 `GetInstance<T>()` 方法获取实例
- **类型唯一性**：每个子类型只能有一个实例
- **线程安全**：多线程环境下安全使用

```csharp
// ✅ 正确用法
var client = MyTcpClient.GetInstance<MyTcpClient>();

// ❌ 错误用法 - 会抛出异常
var client = new MyTcpClient(); // InvalidOperationException
```

### 资源管理

- **及时释放**：使用完毕后调用 `Dispose()` 方法
- **事件订阅**：避免事件处理器中的内存泄漏
- **异常处理**：正确处理网络异常和超时

```csharp
// 使用 using 语句确保资源释放
using var client = MyTcpClient.GetInstance<MyTcpClient>();
await client.TryConnectAsync();
// 自动调用 Dispose()
```

### 网络环境考虑

- **防火墙**：确保目标端口可访问
- **NAT 穿透**：注意 NAT 环境下的连接保持
- **网络质量**：根据网络环境调整超时和重连参数

## 🔧 故障排除

### 常见问题

1. **连接失败**
   - 检查服务器 IP 和端口
   - 验证网络连通性
   - 确认防火墙设置

2. **连接断开频繁**
   - 调整心跳间隔
   - 检查网络稳定性
   - 优化 Keep-Alive 参数

3. **发送队列满**
   - 增大 `MaxSendQueueSize`
   - 降低发送频率
   - 检查网络带宽

4. **内存泄漏**
   - 及时调用 `Dispose()`
   - 正确取消订阅事件
   - 检查异步操作的取消

### 调试技巧

```csharp
// 启用详细日志
client.ErrorOccurred += (ex) => 
{
    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {ex}");
};

client.Connected += () => 
{
    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CONNECTED");
};

client.Disconnected += () => 
{
    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DISCONNECTED");
};
```

## 📄 许可证

本项目基于开源许可证发布，具体许可条款请查看项目根目录的 LICENSE 文件。

## 🤝 贡献

欢迎提交 Issue 和 Pull Request 来改进这个项目！

---

**DanKit.TCP.Client** - 企业级 TCP 客户端解决方案
