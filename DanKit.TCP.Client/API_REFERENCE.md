# DanKit.TCP.Client API 参考

## 核心类

### TcpClientBase (抽象基类)

TCP 客户端的抽象基类，提供完整的 TCP 通信功能。

#### 必须实现的抽象属性

```csharp
protected abstract string ServerIP { get; }    // 服务器IP地址
protected abstract int ServerPort { get; }     // 服务器端口
```

#### 单例模式方法

```csharp
// 获取或创建单例实例
public static T GetInstance<T>() where T : TcpClientBase, new()

// 检查是否已有实例
public static bool HasInstance<T>() where T : TcpClientBase

// 获取实例（如果存在）
public static T? GetInstanceIfExists<T>() where T : TcpClientBase

// 获取所有活跃实例
public static IEnumerable<TcpClientBase> GetAllInstances()
```

#### 连接管理方法

```csharp
// 尝试连接（不抛异常）
public async Task<bool> TryConnectAsync()

// 尝试重连
public async Task<bool> TryReconnectAsync()

// 手动断开连接
public void Disconnect()
```

#### 数据发送方法

```csharp
// 发送字节数组
public async Task SendAsync(byte[] data)

// 发送字符串（默认UTF-8编码）
public async Task SendAsync(string message, Encoding? encoding = null)
```

#### 状态属性

```csharp
public bool IsConnected { get; }    // 连接状态
public bool IsDisposed { get; }     // 释放状态
```

#### 事件

```csharp
public event Action? Connected;                    // 连接建立
public event Action? Disconnected;                 // 连接断开
public event Action<Exception>? ErrorOccurred;     // 错误发生
public event Action<byte[], int>? DataReceived;    // 数据接收
```

#### 可重写的虚拟方法

```csharp
// 事件处理方法
protected virtual void OnConnected()
protected virtual void OnDisconnected()
protected virtual void OnErrorOccurred(Exception exception)
protected virtual void OnDataReceived(byte[] buffer, int length)
```

## 配置属性

### 连接配置

```csharp
protected virtual int MaxRetryCount => 3;           // 最大重试次数
protected virtual int RetryIntervalMs => 1000;     // 重试间隔(ms)
protected virtual int ConnectTimeoutMs => 5000;    // 连接超时(ms)
protected virtual bool EnableKeepAlive => true;    // 启用Keep-Alive
```

### 发送配置

```csharp
protected virtual int MaxSendQueueSize => 1000;                               // 发送队列大小
protected virtual int SendTimeoutMs => 5000;                                  // 发送超时(ms)
protected virtual int SendQueueTimeoutMs => 5000;                             // 队列等待超时(ms)
protected virtual BoundedChannelFullMode SendQueueFullMode => Wait;           // 队列满处理策略
```

### 接收配置

```csharp
protected virtual int BufferSize => 4096;                    // 接收缓冲区大小
protected virtual int MaxMessageSize => 4096;               // 最大消息大小
protected virtual int SocketReceiveTimeoutMs => 0;          // Socket接收超时(ms)
```

### 心跳配置

```csharp
protected virtual byte[]? HeartbeatData => null;             // 心跳包数据
protected virtual bool HeartbeatExpectsResponse => true;     // 期望心跳响应
protected virtual double HeartbeatIntervalMs => 30000;      // 心跳间隔(ms)
```

### 重连配置

```csharp
protected virtual bool EnableSmartReconnection => true;          // 启用智能重连
protected virtual int MaxReconnectionAttempts => 10;            // 最大重连次数
protected virtual TimeSpan IdleTimeout => TimeSpan.FromMinutes(30);  // 空闲超时
```

### Keep-Alive配置

```csharp
protected virtual int KeepAliveTimeSeconds => 10;        // Keep-Alive空闲时间(秒)
protected virtual int KeepAliveIntervalSeconds => 1;     // Keep-Alive探测间隔(秒)
protected virtual int KeepAliveRetryCount => 3;          // Keep-Alive重试次数
```

### 高级性能配置

```csharp
protected virtual bool AllowSynchronousContinuations => false;  // 允许同步延续
protected virtual bool SingleReader => true;                    // 单读者模式
protected virtual bool SingleWriter => false;                   // 单写者模式
```

## 依赖注入扩展

### TcpClientServiceCollectionExtensions

```csharp
// 注册TCP客户端为单例服务
public static IServiceCollection AddTcpClient<T>(this IServiceCollection services)
    where T : TcpClientBase, new()
```

## 使用示例

### 基本实现

```csharp
public class MyTcpClient : TcpClientBase
{
    protected override string ServerIP => "127.0.0.1";
    protected override int ServerPort => 8080;
}

// 使用
var client = MyTcpClient.GetInstance<MyTcpClient>();
await client.TryConnectAsync();
await client.SendAsync("Hello");
client.Dispose();
```

### 高级配置

```csharp
public class AdvancedTcpClient : TcpClientBase
{
    protected override string ServerIP => "192.168.1.100";
    protected override int ServerPort => 9999;
    
    // 自定义心跳
    protected override byte[]? HeartbeatData => 
        Encoding.UTF8.GetBytes("PING");
    
    // 快速重连
    protected override int MaxRetryCount => 5;
    protected override int RetryIntervalMs => 500;
    
    // 大队列
    protected override int MaxSendQueueSize => 5000;
    
    // 自定义事件处理
    protected override void OnConnected()
    {
        base.OnConnected();
        Console.WriteLine("Connected to advanced server");
    }
    
    protected override void OnDataReceived(byte[] buffer, int length)
    {
        base.OnDataReceived(buffer, length);
        var message = Encoding.UTF8.GetString(buffer, 0, length);
        ProcessMessage(message);
    }
    
    private void ProcessMessage(string message)
    {
        // 自定义消息处理逻辑
    }
}
```

### 依赖注入使用

```csharp
// 启动配置
services.AddTcpClient<MyTcpClient>();

// 控制器或服务中使用
public class ApiController : ControllerBase
{
    private readonly MyTcpClient _tcpClient;
    
    public ApiController(MyTcpClient tcpClient)
    {
        _tcpClient = tcpClient;
    }
    
    [HttpPost("send")]
    public async Task<IActionResult> SendMessage([FromBody] string message)
    {
        try
        {
            if (!_tcpClient.IsConnected)
                await _tcpClient.TryConnectAsync();
                
            await _tcpClient.SendAsync(message);
            return Ok("消息发送成功");
        }
        catch (Exception ex)
        {
            return BadRequest($"发送失败: {ex.Message}");
        }
    }
}
```

## 错误处理

### 常见异常类型

- `ObjectDisposedException` - 对象已释放
- `ArgumentNullException` - 参数为空
- `OperationCanceledException` - 操作被取消
- `TimeoutException` - 操作超时
- `SocketException` - 网络错误
- `InvalidOperationException` - 无效操作（如直接new实例）

### 错误处理模式

```csharp
client.ErrorOccurred += (exception) =>
{
    switch (exception)
    {
        case SocketException sockEx:
            Log.Error($"网络错误: {sockEx.ErrorCode}");
            break;
        case TimeoutException:
            Log.Warning("操作超时");
            break;
        default:
            Log.Error($"未知错误: {exception.Message}");
            break;
    }
};
```

## 最佳实践

1. **资源管理**：始终调用 `Dispose()` 释放资源
2. **单例使用**：通过 `GetInstance<T>()` 获取实例
3. **异常处理**：订阅 `ErrorOccurred` 事件处理错误
4. **配置优化**：根据网络环境调整超时和重连参数
5. **性能优化**：合理设置队列大小和缓冲区大小
