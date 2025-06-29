using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using System.Collections.Concurrent;
using Timer = System.Timers.Timer;

namespace DanKit.TCP.Client
{
    /// <summary>
    /// TCP Socket 客户端抽象基类（单例模式）
    /// 提供核心的连接、发送、接收功能，子类通过重写虚方法来定制配置
    /// 强制单例：每个子类型只能有一个实例，确保资源正确管理和避免端口冲突
    /// 功能特性：
    /// - 子类继承定制配置和行为
    /// - 线程安全的发送队列机制
    /// - 自动重连和心跳检测
    /// - 跨平台TCP Keep-Alive支持
    /// - 完善的错误处理
    /// </summary>
    public abstract class TcpClientBase : IDisposable
    {
        #region 单例模式支持

        /// <summary>
        /// 单例实例缓存
        /// Key: 子类类型, Value: 该类型的唯一实例
        /// </summary>
        private static readonly ConcurrentDictionary<Type, TcpClientBase> _instances = new();

        /// <summary>
        /// 单例创建锁，确保每个类型只创建一次实例（按类型分别锁定）
        /// </summary>
        private static readonly ConcurrentDictionary<Type, object> _createLocks = new();

        /// <summary>
        /// 当前正在创建的类型上下文（线程本地存储）
        /// 用于验证构造函数调用的合法性，防止直接 new 创建实例
        /// </summary>
        private static readonly ThreadLocal<Type?> _currentCreatingType = new ThreadLocal<Type?>();

        /// <summary>
        /// 获取或创建当前类型的单例实例（线程安全）
        /// </summary>
        /// <typeparam name="T">TcpClientBase的子类型</typeparam>
        /// <returns>该类型的唯一实例</returns>
        public static T GetInstance<T>() where T : TcpClientBase, new()
        {
            var type = typeof(T);

            // 快速路径：如果实例已存在且有效，直接返回
            if (_instances.TryGetValue(type, out var existingInstance) && !existingInstance.IsDisposed)
            {
                return (T)existingInstance;
            }

            // 慢速路径：需要创建或重新创建实例
            // 使用按类型分别锁定的策略，避免不同类型之间的锁竞争
            var lockObject = _createLocks.GetOrAdd(type, _ => new object());
            
            lock (lockObject)
            {
                // 双重检查锁定：再次检查是否已有有效实例
                if (_instances.TryGetValue(type, out var instance) && !instance.IsDisposed)
                {
                    return (T)instance;
                }

                // 如果存在已释放的实例，先移除
                if (instance != null && instance.IsDisposed)
                {
                    _instances.TryRemove(type, out _);
                }

                // ✅ 设置创建上下文，授权构造函数执行
                _currentCreatingType.Value = type;
                
                try
                {
                    // 创建新实例
                    var newInstance = new T();
                    
                    // 原子地存储新实例
                    _instances[type] = newInstance;
                    
                    return newInstance;
                }
                finally
                {
                    // ✅ 清除创建上下文
                    _currentCreatingType.Value = null;
                }
            }
        }

        /// <summary>
        /// 检查指定类型是否已有单例实例
        /// </summary>
        /// <typeparam name="T">TcpClientBase的子类型</typeparam>
        /// <returns>true表示已有实例且未释放</returns>
        public static bool HasInstance<T>() where T : TcpClientBase
        {
            var type = typeof(T);
            return _instances.TryGetValue(type, out var instance) && !instance.IsDisposed;
        }

        /// <summary>
        /// 获取所有活跃的单例实例
        /// </summary>
        public static IEnumerable<TcpClientBase> GetAllInstances()
        {
            return _instances.Values.Where(instance => !instance.IsDisposed);
        }

        /// <summary>
        /// 获取指定类型的实例（如果存在）
        /// </summary>
        /// <typeparam name="T">TcpClientBase的子类型</typeparam>
        /// <returns>实例（如果存在且未释放），否则为null</returns>
        public static T? GetInstanceIfExists<T>() where T : TcpClientBase
        {
            var type = typeof(T);
            if (_instances.TryGetValue(type, out var instance) && !instance.IsDisposed)
            {
                return (T)instance;
            }
            return null;
        }

        #endregion

        #region 抽象属性 - 子类必须实现

        /// <summary>
        /// 服务器IP地址 - 子类必须指定连接目标
        /// </summary>
        protected abstract string ServerIP { get; }

        /// <summary>
        /// 服务器端口 - 子类必须指定连接目标
        /// </summary>
        protected abstract int ServerPort { get; }

        #endregion

        #region 虚拟配置属性 - 子类可选择重写

        #region 连接配置

        /// <summary>
        /// 连接失败时的最大重试次数
        /// </summary>
        protected virtual int MaxRetryCount => 3;

        /// <summary>
        /// 重试连接的间隔时间（毫秒）
        /// </summary>
        protected virtual int RetryIntervalMs => 1000;

        /// <summary>
        /// TCP连接超时时间（毫秒）
        /// </summary>
        protected virtual int ConnectTimeoutMs => 5000;

        /// <summary>
        /// 是否启用TCP Keep-Alive机制
        /// </summary>
        protected virtual bool EnableKeepAlive => true;

        #endregion

        #region 发送配置

        /// <summary>
        /// 发送队列最大长度限制
        /// </summary>
        protected virtual int MaxSendQueueSize => 1000;

        /// <summary>
        /// 发送数据的超时时间（毫秒）
        /// </summary>
        protected virtual int SendTimeoutMs => 5000;

        /// <summary>
        /// 发送队列等待超时时间（毫秒）
        /// </summary>
        protected virtual int SendQueueTimeoutMs => 5000;

        /// <summary>
        /// 发送队列满时的行为策略
        /// </summary>
        protected virtual BoundedChannelFullMode SendQueueFullMode => BoundedChannelFullMode.Wait;

        #endregion

        #region 接收配置

        /// <summary>
        /// 接收缓冲区大小（字节）
        /// </summary>
        protected virtual int BufferSize => 4096;

        /// <summary>
        /// 单个消息的最大字节数限制
        /// </summary>
        protected virtual int MaxMessageSize => 4096;

        /// <summary>
        /// Socket级别的接收超时时间（毫秒）
        /// </summary>
        protected virtual int SocketReceiveTimeoutMs => 0;

        #endregion

        #region 心跳配置

        /// <summary>
        /// 自定义心跳包数据，返回null表示不使用心跳
        /// </summary>
        protected virtual byte[]? HeartbeatData => null;

        /// <summary>
        /// 心跳包是否期望服务端响应
        /// </summary>
        protected virtual bool HeartbeatExpectsResponse => true;

        /// <summary>
        /// 心跳检测间隔时间（毫秒）
        /// </summary>
        protected virtual double HeartbeatIntervalMs => 30000;

        #endregion

        #region Keep-Alive配置

        /// <summary>
        /// TCP Keep-Alive空闲时间（秒）
        /// </summary>
        protected virtual int KeepAliveTimeSeconds => 10;

        /// <summary>
        /// TCP Keep-Alive探测间隔（秒）
        /// </summary>
        protected virtual int KeepAliveIntervalSeconds => 1;

        /// <summary>
        /// TCP Keep-Alive重试次数
        /// </summary>
        protected virtual int KeepAliveRetryCount => 3;

        #endregion

        #region 重连配置

        /// <summary>
        /// 是否启用智能重连策略
        /// </summary>
        protected virtual bool EnableSmartReconnection => true;

        /// <summary>
        /// 智能重连的最大尝试次数
        /// </summary>
        protected virtual int MaxReconnectionAttempts => 10;

        /// <summary>
        /// 闲置连接自动清理超时时间
        /// </summary>
        protected virtual TimeSpan IdleTimeout => TimeSpan.FromMinutes(30);

        #endregion

        #region 高级性能配置

        /// <summary>
        /// Channel是否允许同步延续
        /// false（默认）：提高性能，避免线程池饥饿
        /// true：允许同步回调，但可能造成性能问题
        /// </summary>
        protected virtual bool AllowSynchronousContinuations => false;

        /// <summary>
        /// Channel是否为单读者模式
        /// true（默认）：只有一个消费者（SendLoop），可优化性能
        /// false：多个消费者同时读取
        /// </summary>
        protected virtual bool SingleReader => true;

        /// <summary>
        /// Channel是否为单写者模式
        /// false（默认）：多线程可同时调用SendAsync
        /// true：只有一个生产者，可优化性能但限制并发
        /// </summary>
        protected virtual bool SingleWriter => false;

        #endregion

        #endregion

        #region 字段和属性

        #region 核心连接字段

        /// <summary>
        /// 底层TCP Socket连接对象
        /// 负责实际的网络通信
        /// </summary>
        private Socket? _clientSocket;

        /// <summary>
        /// 连接状态标识
        /// true表示已建立连接，false表示未连接或连接已断开
        /// </summary>
        private bool _connected = false;

        /// <summary>
        /// 对象释放状态标识
        /// true表示对象已被释放，false表示对象仍可使用
        /// </summary>
        private bool _disposed = false;

        /// <summary>
        /// 取消令牌源，用于控制异步操作的取消
        /// 在连接断开或对象释放时触发取消
        /// </summary>
        private CancellationTokenSource _cts;

        #endregion

        #region 数据收发相关字段

        /// <summary>
        /// 接收数据缓冲区
        /// 用于存储从网络接收的原始字节数据
        /// </summary>
        private byte[] _buffer;

        /// <summary>
        /// 发送通道，使用现代化的Channel API提供智能背压处理
        /// 采用有界通道设计，当队列满时自动等待，实现优雅的流量控制
        /// </summary>
        private Channel<byte[]> _sendChannel = null!;

        /// <summary>
        /// 发送通道的写入器
        /// 用于向通道中写入待发送的数据，支持异步操作和取消令牌
        /// </summary>
        private ChannelWriter<byte[]> _channelWriter = null!;

        /// <summary>
        /// 发送通道的读取器
        /// 用于从通道中读取待发送的数据，支持异步枚举和背压处理
        /// </summary>
        private ChannelReader<byte[]> _channelReader = null!;

        /// <summary>
        /// 发送循环运行状态标识
        /// 使用volatile确保多线程可见性，防止启动多个SendLoop实例
        /// </summary>
        private volatile bool _sending = false;

        /// <summary>
        /// 通用锁对象，用于保护需要原子操作的代码块
        /// 主要用于状态变更和SendLoop启动的线程同步
        /// </summary>
        private readonly object _lockObject = new object();

        #endregion

        #region 心跳和连接管理字段

        /// <summary>
        /// 心跳检测定时器
        /// 定期执行心跳检查，验证连接状态和发送心跳包
        /// </summary>
        private Timer? _heartbeatTimer;

        /// <summary>
        /// 心跳定时器操作锁
        /// 保护心跳定时器的启动、停止、释放操作的线程安全
        /// </summary>
        private readonly object _heartbeatLock = new();

        /// <summary>
        /// 最后一次心跳活动时间
        /// 用于判断连接是否超时，包括数据接收和心跳响应
        /// </summary>
        private DateTime _lastHeartbeatTime = DateTime.MinValue;

        /// <summary>
        /// 连接操作同步锁
        /// 确保同时只有一个连接操作在进行，避免并发连接冲突
        /// </summary>
        private readonly SemaphoreSlim _connectLock = new(1, 1);

        /// <summary>
        /// 连接丢失处理锁
        /// 确保 HandleConnectionLoss 只能被一个线程执行，避免重复清理和事件触发
        /// </summary>
        private readonly object _connectionLossLock = new object();

        #endregion

        #region 调试和监控字段

        /// <summary>
        /// 当前运行的SendLoop实例数量（调试用）
        /// 正常情况下应该始终为0或1，大于1表示存在并发问题
        /// </summary>
        private int _sendLoopCount = 0;

        #endregion

        #region 高级配置属性

        /// <summary>
        /// 智能重连策略管理器实例
        /// 负责实现指数退避算法和重连逻辑
        /// </summary>
        private ReconnectionStrategy? _reconnectionStrategy;

        /// <summary>
        /// 累计重连次数
        /// 记录智能重连策略执行的总重连次数
        /// </summary>
        private int _reconnectionCount = 0;

        #endregion

        #region 事件定义

        /// <summary>
        /// 连接建立成功事件
        /// 在TCP连接成功建立后触发
        /// </summary>
        public event Action? Connected;

        /// <summary>
        /// 连接断开事件
        /// 在连接断开或连接失败时触发
        /// </summary>
        public event Action? Disconnected;

        /// <summary>
        /// 错误发生事件
        /// 在发生各种错误（网络错误、超时等）时触发
        /// </summary>
        public event Action<Exception>? ErrorOccurred;

        /// <summary>
        /// 数据接收事件
        /// 当从服务器接收到数据时触发
        /// 参数1：接收到的字节数组缓冲区
        /// 参数2：有效数据的长度
        /// </summary>
        public event Action<byte[], int>? DataReceived;

        #endregion

        #region 虚拟事件处理方法 - 子类可选择重写

        /// <summary>
        /// 连接建立成功时调用（虚拟方法）
        /// 子类可重写此方法来实现自定义的连接成功处理逻辑
        /// 默认实现：触发Connected事件
        /// </summary>
        protected virtual void OnConnected()
        {
            Connected?.Invoke();
        }

        /// <summary>
        /// 连接断开时调用（虚拟方法）
        /// 子类可重写此方法来实现自定义的断开连接处理逻辑
        /// 默认实现：触发Disconnected事件
        /// </summary>
        protected virtual void OnDisconnected()
        {
            Disconnected?.Invoke();
        }

        /// <summary>
        /// 错误发生时调用（虚拟方法）
        /// 子类可重写此方法来实现自定义的错误处理逻辑
        /// 默认实现：触发ErrorOccurred事件
        /// </summary>
        /// <param name="exception">发生的异常</param>
        protected virtual void OnErrorOccurred(Exception exception)
        {
            ErrorOccurred?.Invoke(exception);
        }

        /// <summary>
        /// 数据接收时调用（虚拟方法）
        /// 子类可重写此方法来实现自定义的数据处理逻辑
        /// 默认实现：触发DataReceived事件
        /// </summary>
        /// <param name="buffer">接收到的数据缓冲区</param>
        /// <param name="length">有效数据长度</param>
        protected virtual void OnDataReceived(byte[] buffer, int length)
        {
            DataReceived?.Invoke(buffer, length);
        }

        #endregion

        #region 状态属性

        /// <summary>
        /// 获取当前连接状态
        /// true表示已连接，false表示未连接或连接已断开
        /// </summary>
        public bool IsConnected => _connected;

        /// <summary>
        /// 获取对象是否已被释放
        /// true表示已调用Dispose方法，对象不可再使用
        /// </summary>
        public bool IsDisposed => _disposed;

        #endregion

        #region 智能重连策略

        /// <summary>
        /// 智能重连策略类
        /// 实现指数退避算法和网络质量感知
        /// </summary>
        private class ReconnectionStrategy
        {
            private int _attemptCount = 0;
            private DateTime _lastAttempt = DateTime.MinValue;
            private readonly TimeSpan[] _backoffIntervals =
            {
                TimeSpan.FromSeconds(1),     // 立即重连
                TimeSpan.FromSeconds(2),     // 2秒后
                TimeSpan.FromSeconds(5),     // 5秒后
                TimeSpan.FromSeconds(10),    // 10秒后
                TimeSpan.FromSeconds(30),    // 30秒后
                TimeSpan.FromMinutes(1),     // 1分钟后
                TimeSpan.FromMinutes(5),     // 5分钟后
                TimeSpan.FromMinutes(10)     // 10分钟后（最大间隔）
            };

            private readonly TcpClientBase _client;

            public ReconnectionStrategy(TcpClientBase client)
            {
                _client = client;
            }

            /// <summary>
            /// 获取下次重连的延迟时间
            /// </summary>
            public TimeSpan GetNextRetryDelay()
            {
                if (_attemptCount == 0)
                    return TimeSpan.Zero; // 首次重连立即执行

                var backoffIndex = Math.Min(_attemptCount - 1, _backoffIntervals.Length - 1);
                var baseDelay = _backoffIntervals[backoffIndex];

                // 添加随机抖动（±25%）避免雷群效应
                var jitter = Random.Shared.NextDouble() * 0.5 - 0.25; // -25% 到 +25%
                var jitteredDelay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * (1 + jitter));

                return jitteredDelay;
            }

            /// <summary>
            /// 尝试智能重连
            /// </summary>
            public async Task<bool> TryReconnectAsync()
            {
                if (!_client.EnableSmartReconnection || _client._disposed)
                    return false;

                // 检查是否达到最大重连次数
                if (_client.MaxReconnectionAttempts > 0 && _attemptCount >= _client.MaxReconnectionAttempts)
                {
                    return false;
                }

                var delay = GetNextRetryDelay();
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _client._cts.Token);
                }

                _attemptCount++;
                _lastAttempt = DateTime.Now;

                try
                {
                    // 尝试重连
                    var success = await _client.TryConnectAsync();
                    if (success)
                    {
                        Reset(); // 重连成功，重置计数器
                        Interlocked.Increment(ref _client._reconnectionCount);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _client.ErrorOccurred?.Invoke(ex);
                }

                return false;
            }

            /// <summary>
            /// 重置重连策略（连接成功后调用）
            /// </summary>
            public void Reset()
            {
                _attemptCount = 0;
                _lastAttempt = DateTime.MinValue;
            }

            /// <summary>
            /// 获取当前重连尝试次数
            /// </summary>
            public int AttemptCount => _attemptCount;
        }

        #endregion

        #endregion

        #region 构造函数和实例创建

        /// <summary>
        /// 受保护的构造函数，防止外部直接实例化
        /// 只能通过 GetInstance<T>() 方法获取单例实例
        /// </summary>
        protected TcpClientBase()
        {
            var type = GetType();

            // ✅ 严格检查：只有在正确的创建上下文中才能执行
            if (_currentCreatingType.Value != type)
            {
                throw new InvalidOperationException(
                    $"不能直接使用 new {type.Name}() 创建实例！" +
                    $"TcpClient 使用单例模式，请使用 {type.Name}.GetInstance<{type.Name}>() 方法获取实例。");
            }

            _reconnectionStrategy = new ReconnectionStrategy(this);
            _buffer = new byte[BufferSize];
            _cts = new CancellationTokenSource();

            // 初始化发送通道
            InitializeSendChannel();
        }

        /// <summary>
        /// 初始化发送通道
        /// 在实例创建后调用，使用当前的配置属性
        /// </summary>
        private void InitializeSendChannel()
        {
            // 创建有界通道，提供智能背压处理
            var channelOptions = new BoundedChannelOptions(MaxSendQueueSize)
            {
                FullMode = SendQueueFullMode,
                SingleReader = SingleReader,                          // 可配置的单读者模式
                SingleWriter = SingleWriter,                          // 可配置的单写者模式
                AllowSynchronousContinuations = AllowSynchronousContinuations  // 可配置的同步延续
            };

            _sendChannel = Channel.CreateBounded<byte[]>(channelOptions);
            _channelWriter = _sendChannel.Writer;
            _channelReader = _sendChannel.Reader;
        }

        #endregion

        #region 连接管理方法

        /// <summary>
        /// 手动连接到服务器（不发送数据）
        /// </summary>
        /// <returns>连接是否成功</returns>
        public async Task<bool> TryConnectAsync()
        {
            try
            {
                await ConnectAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 手动触发重连（用于测试或手动恢复）
        /// 会先断开现有连接，然后尝试重新连接
        /// </summary>
        /// <returns>重连是否成功</returns>
        public async Task<bool> TryReconnectAsync()
        {
            if (_disposed) return false;

            try
            {
                // 先断开现有连接
                if (_connected)
                {
                    Disconnect();
                    await Task.Delay(100); // 短暂等待确保清理完成
                }

                // 尝试重新连接
                await ConnectAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 内部连接方法，支持自动重试
        /// </summary>
        private async Task ConnectAsync()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().Name);
            await _connectLock.WaitAsync();
            try
            {
                if (_connected) return;
                int retry = 0;
                Exception? lastEx = null;
                while (retry < MaxRetryCount)
                {
                    try
                    {
                        CleanupSocket(); // 先清理旧Socket
                        _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                        // 配置Socket选项
                        ConfigureSocket(_clientSocket);

                        var connectTask = _clientSocket.ConnectAsync(IPAddress.Parse(ServerIP), ServerPort);
                        var timeoutTask = Task.Delay(ConnectTimeoutMs);
                        var completed = await Task.WhenAny(connectTask, timeoutTask);
                        if (completed == connectTask)
                        {
                            await connectTask; // 捕获异常

                            // ✅ 连接成功后的状态设置也要同步
                            lock (_connectionLossLock)
                            {
                                if (_disposed) return; // 连接过程中可能被释放
                                _connected = true;
                            }

                            _lastHeartbeatTime = DateTime.Now;

                            // 重置智能重连策略
                            _reconnectionStrategy?.Reset();

                            // 重新创建 CancellationTokenSource
                            _cts?.Dispose();
                            _cts = new CancellationTokenSource();

                            //连接成功后立即启动所有后台服务
                            OnConnected();
                            StartHeartbeat();
                            StartSendLoop();
                            _ = Task.Run(() => ReceiveLoop(_cts.Token));
                            return;
                        }
                        else
                        {
                            _clientSocket.Close();
                            throw new TimeoutException($"连接超时({ConnectTimeoutMs}ms)");
                        }
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        retry++;
                        if (retry < MaxRetryCount)
                        {
                            await Task.Delay(RetryIntervalMs);
                        }
                    }
                }
                throw new Exception($"连接失败，重试{MaxRetryCount}次后仍未成功", lastEx);
            }
            finally
            {
                _connectLock.Release();
            }
        }

        /// <summary>
        /// 手动断开连接（线程安全）
        /// </summary>
        public void Disconnect()
        {
            // ✅ 使用相同的锁保护，避免与 HandleConnectionLoss 冲突
            lock (_connectionLossLock)
            {
                if (!_connected) return;
                _connected = false;
            }

            // 执行清理（复用清理逻辑，但不触发重连）
            try
            {
                StopHeartbeat();
                _cts?.Cancel();
                CleanupSocket();
                OnDisconnected();
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new Exception($"手动断开连接时发生异常: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// 检测连接是否真正可用
        /// 使用Poll和Available组合检测，提高准确性
        /// 核心原理：当Poll返回true（有读取事件）但Available=0（无数据）时，
        /// 通常表示对方关闭了连接或网络发生了错误，这是最可靠的断线检测方法。
        /// </summary>
        private bool IsSocketConnected()
        {
            try
            {
                if (_clientSocket == null) return false;

                // 步骤1：检测是否有读取事件（1毫秒超时）
                // - 有数据到达 → true
                // - 连接关闭/错误 → true  
                // - 连接空闲 → false
                bool part1 = _clientSocket.Poll(1000, SelectMode.SelectRead);

                // 步骤2：检测接收缓冲区是否有数据
                // - 有待读数据 → Available > 0
                // - 无待读数据 → Available = 0
                bool part2 = (_clientSocket.Available == 0);

                // 步骤3：组合判断
                // part1=true && part2=true 意味着：
                // "有读取事件但无数据" = 连接断开的典型特征
                if (part1 && part2)
                    return false; // 连接已断开
                else
                    return true;  // 连接正常
            }
            catch
            {
                // 任何异常都视为连接问题
                return false;
            }
        }

        /// <summary>
        /// 清理Socket资源
        /// </summary>
        private void CleanupSocket()
        {
            try { _clientSocket?.Shutdown(SocketShutdown.Both); } catch { }
            try { _clientSocket?.Close(); } catch { }
            try { _clientSocket?.Dispose(); } catch { }
            _clientSocket = null;
        }

        #endregion

        #region 数据发送方法

        /// <summary>
        /// 异步发送数据到服务器（Channel + Wait模式 + 预启动SendLoop）
        /// 采用现代化的Channel机制，提供智能背压处理和优雅的流量控制
        /// SendLoop在连接建立时预启动，确保最佳性能和响应速度
        /// </summary>
        /// <param name="data">要发送的字节数组</param>
        /// <returns>任务完成表示数据已成功写入发送通道</returns>
        /// <exception cref="ObjectDisposedException">客户端已被释放</exception>
        /// <exception cref="ArgumentNullException">数据为null</exception>
        /// <exception cref="OperationCanceledException">操作被取消</exception>
        public async Task SendAsync(byte[] data)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().Name);
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length == 0) return;

            // ✅ 改进：自动重连逻辑
            if (!_connected)
            {
                await ConnectAsync();
            }

            // ✅ 改进：确保SendLoop正在运行
            if (!_sending)
            {
                StartSendLoop();
            }

            using var timeoutCts = new CancellationTokenSource(SendQueueTimeoutMs);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, _cts.Token);

            try
            {
                await _channelWriter.WriteAsync(data, combinedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"发送队列已满，等待{SendQueueTimeoutMs}ms后仍无法写入。" +
                    $"当前队列大小限制：{MaxSendQueueSize}，建议增大队列大小或降低发送频率。");
            }
            catch (OperationCanceledException)
            {
                throw new ObjectDisposedException(this.GetType().Name, "发送操作被取消");
            }
            catch (InvalidOperationException)
            {
                // ✅ 新增：Channel关闭时尝试重新初始化
                if (!_disposed && _connected)
                {
                    try
                    {
                        InitializeSendChannel();
                        StartSendLoop();
                        // 重试一次
                        await _channelWriter.WriteAsync(data, combinedCts.Token);
                        return;
                    }
                    catch
                    {
                        // 重试失败，抛出原始异常
                    }
                }
                throw new ObjectDisposedException(this.GetType().Name, "发送通道已关闭");
            }
        }

        /// <summary>
        /// 发送字符串数据（UTF-8编码）
        /// </summary>
        /// <param name="message">要发送的字符串</param>
        /// <param name="encoding">字符编码，默认UTF-8</param>
        public async Task SendAsync(string message, Encoding? encoding = null)
        {
            encoding ??= Encoding.UTF8;
            var data = encoding.GetBytes(message);
            await SendAsync(data);
        }

        /// <summary>
        /// 启动发送循环线程（线程安全，避免重复启动）
        /// 使用原子操作和双重检查锁定模式，确保只有一个SendLoop实例运行
        /// 通常在连接建立时调用，也可用于防御性编程确保SendLoop正在运行
        /// </summary>
        private void StartSendLoop()
        {
            // 第一次检查：避免不必要的锁竞争
            if (_sending) return;

            lock (_lockObject)
            {
                // 第二次检查：确保线程安全
                if (_sending) return;

                // 原子地设置发送状态
                _sending = true;
                Interlocked.Increment(ref _sendLoopCount);

                // 启动后台发送任务
                _ = Task.Run(async () => await SendLoop());
            }
        }

        /// <summary>
        /// 发送循环：使用Channel的异步枚举器处理发送队列
        /// 该方法在后台线程中运行，提供现代化的异步数据处理机制
        /// </summary>
        private async Task SendLoop()
        {
            var currentLoopId = Thread.CurrentThread.ManagedThreadId;

            try
            {
                // 验证只有一个SendLoop在运行（调试用）
                var currentCount = _sendLoopCount;
                if (currentCount > 1)
                {
                    ErrorOccurred?.Invoke(new InvalidOperationException(
                        $"检测到多个SendLoop同时运行: {currentCount}, 当前线程ID: {currentLoopId}"));
                }

                // 🆕 使用Channel的现代化异步枚举器
                // ReadAllAsync 会自动处理等待和背压，无需手动信号管理
                await foreach (var data in _channelReader.ReadAllAsync(_cts.Token))
                {
                    if (!_connected || _disposed) break;

                    // 发送数据，带超时控制
                    using var timeoutCts = new CancellationTokenSource(SendTimeoutMs);
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _cts.Token);
                    try
                    {
                        if (_clientSocket == null)
                        {
                            throw new InvalidOperationException("Socket连接已断开");
                        }

                        await _clientSocket.SendAsync(
                            new ArraySegment<byte>(data),
                            SocketFlags.None,
                            combinedCts.Token);
                    }
                    catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                    {
                        // 发送超时
                        ErrorOccurred?.Invoke(new TimeoutException($"发送数据超时({SendTimeoutMs}ms)"));
                        break;
                    }
                    catch (SocketException ex)
                    {
                        // ✅ 修复：使用统一的断开处理
                        HandleConnectionLoss($"Socket发送异常: {ex.Message} (ErrorCode: {ex.ErrorCode})");
                        break;
                    }
                    catch (Exception ex)
                    {
                        // ✅ 修复：使用统一的断开处理
                        HandleConnectionLoss($"发送数据时发生未知异常: {ex.Message}");
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常的取消操作（如Dispose时触发）
            }
            catch (Exception ex)
            {
                // ✅ 修复：SendLoop本身的异常也使用统一处理
                HandleConnectionLoss($"SendLoop异常 (线程{currentLoopId}): {ex.Message}");
            }
            finally
            {
                // 确保发送状态被正确重置
                lock (_lockObject)
                {
                    _sending = false;
                    Interlocked.Decrement(ref _sendLoopCount);
                }
            }
        }

        #endregion

        #region 数据接收方法

        /// <summary>
        /// 数据接收循环
        /// 在后台线程中持续接收服务器发送的数据
        /// 
        /// 设计说明：
        /// 1. 不设置接收超时，因为服务端不发送数据是正常的
        /// 2. 连接状态检测由心跳机制负责
        /// 3. TCP层面的连接断开会自然触发ReceiveAsync返回0或抛异常
        /// 4. 这种设计符合TCP协议的特性，避免了误判和性能问题
        /// </summary>
        private async Task ReceiveLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _connected)
                {
                    try
                    {
                        if (_clientSocket == null)
                        {
                            break; // Socket已断开，退出接收循环
                        }

                        // 纯接收操作，无超时限制
                        // 服务端什么时候发送数据是不可预测的，这是正常的
                        int len = await _clientSocket.ReceiveAsync(
                            new ArraySegment<byte>(_buffer),
                            SocketFlags.None,
                            token);

                        if (len > 0)
                        {
                            // 收到数据，更新最后活动时间
                            _lastHeartbeatTime = DateTime.Now;

                            // 数据大小检查
                            if (len > MaxMessageSize)
                            {
                                ErrorOccurred?.Invoke(new Exception(
                                    $"收到超大数据包({len}字节)，超过限制({MaxMessageSize}字节)，已丢弃"));
                                continue;
                            }

                            // 通知业务层处理数据
                            OnDataReceived(_buffer, len);
                        }
                        else if (len == 0)
                        {
                            // ✅ 修复：使用统一的断开处理
                            HandleConnectionLoss("服务端优雅关闭连接");
                            break;
                        }
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        // ✅ 修复：使用统一的断开处理
                        HandleConnectionLoss($"连接被对方重置: {ex.Message}");
                        break;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionAborted)
                    {
                        // ✅ 修复：使用统一的断开处理
                        HandleConnectionLoss($"连接被中止: {ex.Message}");
                        break;
                    }
                    catch (SocketException ex)
                    {
                        // ✅ 修复：使用统一的断开处理
                        HandleConnectionLoss($"Socket接收异常: {ex.Message} (ErrorCode: {ex.ErrorCode})");
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常的取消操作（如Dispose时触发）
            }
            catch (ObjectDisposedException)
            {
                // Socket已被释放，正常情况
            }
            catch (Exception ex)
            {
                // ✅ 修复：使用统一的断开处理
                HandleConnectionLoss($"接收循环发生未知异常: {ex.Message}");
            }
        }

        #endregion

        #region 心跳检测方法

        /// <summary>
        /// 启动心跳检测定时器（线程安全）
        /// </summary>
        private void StartHeartbeat()
        {
            lock (_heartbeatLock)
            {
                // ✅ 先停止现有定时器
                if (_heartbeatTimer != null)
                {
                    _heartbeatTimer.Stop();
                    _heartbeatTimer.Dispose();
                }

                // ✅ 创建新的定时器
                _heartbeatTimer = new Timer(HeartbeatIntervalMs);
                _heartbeatTimer.Elapsed += (s, e) => HeartbeatCheck();
                _heartbeatTimer.AutoReset = true;
                _heartbeatTimer.Start();

                // ✅ 重置心跳时间
                _lastHeartbeatTime = DateTime.Now;
            }
        }

        /// <summary>
        /// 停止心跳检测（线程安全）
        /// </summary>
        private void StopHeartbeat()
        {
            lock (_heartbeatLock)
            {
                _heartbeatTimer?.Stop();
            }
        }

        /// <summary>
        /// 心跳检测逻辑
        /// </summary>
        private void HeartbeatCheck()
        {
            if (_disposed) return;
            if (_connected)
            {
                try
                {
                    // 第一层：Socket底层连接状态检查（始终执行）
                    if (!IsSocketConnected())
                    {
                        HandleConnectionLoss("Socket连接断开");
                        return;
                    }

                    // 第二层：心跳超时检查（仅在有心跳数据且期望响应时执行）
                    if (HeartbeatData != null && HeartbeatData.Length > 0 && HeartbeatExpectsResponse)
                    {
                        if (_lastHeartbeatTime != DateTime.MinValue &&
                            DateTime.Now.Subtract(_lastHeartbeatTime).TotalMilliseconds > HeartbeatIntervalMs * 2)
                        {
                            HandleConnectionLoss("心跳响应超时");
                            return;
                        }
                    }

                    // 第三层：发送心跳包
                    if (HeartbeatData != null && HeartbeatData.Length > 0)
                    {
                        // 直接调用异步方法，让.NET运行时优化线程使用
                        _ = SendHeartbeatAsync();
                    }
                }
                catch (Exception ex)
                {
                    HandleConnectionLoss($"心跳检测异常: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 异步发送心跳包
        /// </summary>
        private async Task SendHeartbeatAsync()
        {
            try
            {
                if (HeartbeatData != null)
                {
                    await SendAsync(HeartbeatData);

                    // 如果心跳包不期望响应，发送成功后立即更新活动时间
                    // 这样可以防止因为没有业务数据而误判连接超时
                    if (!HeartbeatExpectsResponse)
                    {
                        _lastHeartbeatTime = DateTime.Now;
                    }
                }
            }
            catch (Exception ex)
            {
                HandleConnectionLoss($"心跳发送失败: {ex.Message}");
            }
        }

        #endregion

        #region Socket配置方法

        /// <summary>
        /// 配置Socket选项
        /// 包括超时设置、Keep-Alive、Nagle算法等
        /// </summary>
        private void ConfigureSocket(Socket socket)
        {
            // 设置发送和接收超时
            socket.SendTimeout = SendTimeoutMs;
            socket.ReceiveTimeout = SocketReceiveTimeoutMs;

            // 启用地址重用
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            // 禁用Nagle算法，提高实时性
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

            if (EnableKeepAlive)
            {
                // 启用TCP Keep-Alive
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                // 设置Keep-Alive参数（跨平台兼容）
                ConfigureKeepAliveParameters(socket);
            }
        }

        /// <summary>
        /// 配置Keep-Alive参数（跨平台兼容）
        /// </summary>
        private void ConfigureKeepAliveParameters(Socket socket)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Windows: 使用IOControl方式
                    var keepAliveValues = new byte[12];
                    BitConverter.GetBytes(1).CopyTo(keepAliveValues, 0);      // 启用
                    BitConverter.GetBytes(KeepAliveTimeSeconds * 1000).CopyTo(keepAliveValues, 4);  // 转换为毫秒
                    BitConverter.GetBytes(KeepAliveIntervalSeconds * 1000).CopyTo(keepAliveValues, 8);   // 转换为毫秒

                    // 使用unchecked转换来避免编译器警告
                    const int SIO_KEEPALIVE_VALS = unchecked((int)0x98000004);
                    socket.IOControl(SIO_KEEPALIVE_VALS, keepAliveValues, null);
                }
                else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    // Linux/macOS: 使用SetSocketOption方式
                    socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, KeepAliveTimeSeconds);        // 空闲时间（秒）
                    socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, KeepAliveIntervalSeconds); // 探测间隔（秒）
                    socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, KeepAliveRetryCount);    // 重试次数
                }
                else
                {
                    // 其他系统：尝试通用方法
                    TryGenericKeepAliveConfig(socket);
                }
            }
            catch (Exception ex)
            {
                // Keep-Alive配置失败不影响连接，记录错误即可
                ErrorOccurred?.Invoke(new Exception($"Keep-Alive配置失败: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// 尝试通用的Keep-Alive配置方法
        /// </summary>
        private void TryGenericKeepAliveConfig(Socket socket)
        {
            bool configSuccess = false;

            // 尝试Linux方式
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, KeepAliveTimeSeconds);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, KeepAliveIntervalSeconds);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, KeepAliveRetryCount);
                configSuccess = true;
            }
            catch
            {
                // 尝试Windows方式
                try
                {
                    var keepAliveValues = new byte[12];
                    BitConverter.GetBytes(1).CopyTo(keepAliveValues, 0);
                    BitConverter.GetBytes(KeepAliveTimeSeconds * 1000).CopyTo(keepAliveValues, 4);
                    BitConverter.GetBytes(KeepAliveIntervalSeconds * 1000).CopyTo(keepAliveValues, 8);

                    // 使用常量而不是IOControlCode枚举
                    const int SIO_KEEPALIVE_VALS = unchecked((int)0x98000004);
                    socket.IOControl(SIO_KEEPALIVE_VALS, keepAliveValues, null);
                    configSuccess = true;
                }
                catch
                {
                    // 所有自定义配置都失败，Keep-Alive仍然启用但使用系统默认参数
                    // 注意：系统默认参数通常很保守（如2小时空闲时间），可能不适合实时通信

                    // 由于无法设置快速检测参数，我们需要依赖应用层心跳来补偿
                    if (HeartbeatData == null || HeartbeatData.Length == 0)
                    {
                        // 如果没有设置应用层心跳，给出建议
                        ErrorOccurred?.Invoke(new Exception(
                            "Keep-Alive自定义参数设置失败，建议设置HeartbeatData来实现应用层心跳检测"));
                    }
                }
            }

            // 记录配置结果
            if (!configSuccess)
            {
                ErrorOccurred?.Invoke(new Exception(
                    "Keep-Alive参数配置失败，使用系统默认值（可能2小时检测间隔）。" +
                    "建议检查运行环境或使用应用层心跳补偿"));
            }
        }

        #endregion

        #region 连接丢失处理方法

        /// <summary>
        /// 处理连接丢失的逻辑
        /// 封装连接丢失后的完整清理流程，确保所有后台服务都被正确停止
        /// 线程安全：多个线程同时检测到断开时，只有第一个线程执行清理逻辑
        /// </summary>
        /// <param name="reason">丢失原因描述</param>
        private void HandleConnectionLoss(string reason)
        {
            // ✅ 线程安全的状态检查和设置
            lock (_connectionLossLock)
            {
                // 确保只有第一个检测到断开的线程执行清理
                if (!_connected) return;

                // 原子地设置连接状态
                _connected = false;
            }

            // 在锁外执行清理操作，避免长时间持有锁
            try
            {
                // 🔄 Step 1: 立即停止所有后台服务
                StopHeartbeat();           // 停止心跳定时器

                // 取消所有异步操作，这会停止 ReceiveLoop 和 SendLoop
                _cts?.Cancel();

                // 清理 Socket 资源
                CleanupSocket();

                // 🔄 Step 2: 通知业务层（只触发一次）
                OnDisconnected();
                OnErrorOccurred(new Exception(reason));

                // 🔄 Step 3: 启动智能重连（如果启用，只启动一次）
                if (EnableSmartReconnection && !_disposed)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var success = await _reconnectionStrategy!.TryReconnectAsync();
                            if (!success)
                            {
                                // 重连完全失败的处理
                                OnErrorOccurred(new Exception($"智能重连失败，已达到最大重试次数({MaxReconnectionAttempts})"));
                            }
                        }
                        catch (Exception ex)
                        {
                            OnErrorOccurred(new Exception($"智能重连过程中发生异常: {ex.Message}", ex));
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // 清理过程中的异常也要报告，但不影响状态
                OnErrorOccurred(new Exception($"连接清理过程中发生异常: {ex.Message}", ex));
            }
        }

        #endregion

        #region 资源释放方法

        /// <summary>
        /// 释放所有资源，并从单例缓存中移除
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            lock (_lockObject)
            {
                if (_disposed) return;
                _disposed = true;
            }

            // 先断开连接
            Disconnect();

            // 清理资源
            try
            {
                // 关闭发送通道，通知SendLoop停止
                _channelWriter?.Complete();
            }
            catch { }

            try { _connectLock?.Dispose(); } catch { }

            DisposeHeartbeat();  // 安全地清理心跳定时器

            try { _cts?.Dispose(); } catch { }
            try { _clientSocket?.Dispose(); } catch { }

            // ✅ 新增：从单例缓存中移除
            var type = GetType();
            _instances.TryRemove(type, out _);
        }

        /// <summary>
        /// 完全清理心跳定时器资源
        /// </summary>
        private void DisposeHeartbeat()
        {
            try
            {
                _heartbeatTimer?.Stop();     // 先停止定时器
                _heartbeatTimer?.Dispose();  // 再释放资源
                _heartbeatTimer = null;      // 清空引用
            }
            catch (Exception ex)
            {
                // 只有在未完全销毁时才报告错误
                if (!_disposed)
                {
                    ErrorOccurred?.Invoke(new Exception($"清理心跳定时器时发生异常: {ex.Message}", ex));
                }
            }
        }

        #endregion
    }
}