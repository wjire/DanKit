using DanKit.TCP.Client;
using System.Text;

namespace DanKit.TCP.Client.Examples
{
    /// <summary>
    /// 简单的TCP客户端示例
    /// 连接到本地服务器并发送消息
    /// </summary>
    public class SimpleTcpClient : TcpClientBase
    {
        protected override string ServerIP => "127.0.0.1";
        protected override int ServerPort => 8080;

        // 可选：自定义配置
        protected override int MaxRetryCount => 5;
        protected override double HeartbeatIntervalMs => 15000; // 15秒心跳
    }

    /// <summary>
    /// 带心跳的TCP客户端示例
    /// 发送自定义心跳包保持连接
    /// </summary>
    public class HeartbeatTcpClient : TcpClientBase
    {
        protected override string ServerIP => "192.168.1.100";
        protected override int ServerPort => 9999;

        // 自定义心跳包
        protected override byte[]? HeartbeatData => 
            Encoding.UTF8.GetBytes("PING");

        protected override bool HeartbeatExpectsResponse => true;
        protected override double HeartbeatIntervalMs => 10000; // 10秒心跳

        // 重写事件处理
        protected override void OnConnected()
        {
            base.OnConnected();
            Console.WriteLine("Heart连接已建立，开始心跳检测...");
        }

        protected override void OnDataReceived(byte[] buffer, int length)
        {
            base.OnDataReceived(buffer, length);
            
            var message = Encoding.UTF8.GetString(buffer, 0, length);
            Console.WriteLine($"收到响应: {message}");
            
            // 如果收到PONG，说明心跳正常
            if (message.Trim() == "PONG")
            {
                Console.WriteLine("心跳响应正常");
            }
        }
    }

    /// <summary>
    /// 高性能TCP客户端示例
    /// 大缓冲区和大队列配置
    /// </summary>
    public class HighPerformanceTcpClient : TcpClientBase
    {
        protected override string ServerIP => "high-perf.server.com";
        protected override int ServerPort => 8080;

        // 性能优化配置
        protected override int MaxSendQueueSize => 10000;       // 大队列
        protected override int BufferSize => 65536;             // 64KB缓冲区
        protected override int MaxMessageSize => 1048576;       // 1MB最大消息

        // Channel性能优化
        protected override bool SingleReader => true;
        protected override bool SingleWriter => false;
        protected override bool AllowSynchronousContinuations => false;

        // 快速超时
        protected override int ConnectTimeoutMs => 3000;
        protected override int SendTimeoutMs => 3000;
    }

    /// <summary>
    /// 使用示例程序
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // 示例1：简单使用
            await SimpleClientExample();

            // 示例2：带心跳的客户端
            await HeartbeatClientExample();

            // 示例3：高性能客户端
            await HighPerformanceClientExample();

            // 示例4：事件处理
            await EventHandlingExample();
        }

        /// <summary>
        /// 简单客户端使用示例
        /// </summary>
        public static async Task SimpleClientExample()
        {
            Console.WriteLine("=== 简单客户端示例 ===");

            var client = SimpleTcpClient.GetInstance<SimpleTcpClient>();

            try
            {
                // 连接到服务器
                var connected = await client.TryConnectAsync();
                if (connected)
                {
                    Console.WriteLine("连接成功！");

                    // 发送文本消息
                    await client.SendAsync("Hello, Server!");
                    await client.SendAsync("How are you?");

                    // 发送二进制数据
                    var binaryData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
                    await client.SendAsync(binaryData);

                    // 等待一段时间接收响应
                    await Task.Delay(2000);
                }
                else
                {
                    Console.WriteLine("连接失败！");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
            }
            finally
            {
                client.Dispose();
            }
        }

        /// <summary>
        /// 心跳客户端使用示例
        /// </summary>
        public static async Task HeartbeatClientExample()
        {
            Console.WriteLine("=== 心跳客户端示例 ===");

            var client = HeartbeatTcpClient.GetInstance<HeartbeatTcpClient>();

            try
            {
                var connected = await client.TryConnectAsync();
                if (connected)
                {
                    Console.WriteLine("心跳客户端连接成功！");

                    // 发送业务数据
                    await client.SendAsync("START_BUSINESS_DATA");

                    // 等待心跳和业务数据交互
                    await Task.Delay(30000); // 等待30秒观察心跳
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"心跳客户端错误: {ex.Message}");
            }
            finally
            {
                client.Dispose();
            }
        }

        /// <summary>
        /// 高性能客户端使用示例
        /// </summary>
        public static async Task HighPerformanceClientExample()
        {
            Console.WriteLine("=== 高性能客户端示例 ===");

            var client = HighPerformanceTcpClient.GetInstance<HighPerformanceTcpClient>();

            try
            {
                var connected = await client.TryConnectAsync();
                if (connected)
                {
                    Console.WriteLine("高性能客户端连接成功！");

                    // 快速发送大量数据
                    var tasks = new List<Task>();
                    for (int i = 0; i < 1000; i++)
                    {
                        var message = $"Message {i:D4}: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}";
                        tasks.Add(client.SendAsync(message));
                    }

                    // 等待所有发送完成
                    await Task.WhenAll(tasks);
                    Console.WriteLine("1000条消息发送完成！");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"高性能客户端错误: {ex.Message}");
            }
            finally
            {
                client.Dispose();
            }
        }

        /// <summary>
        /// 事件处理示例
        /// </summary>
        public static async Task EventHandlingExample()
        {
            Console.WriteLine("=== 事件处理示例 ===");

            var client = SimpleTcpClient.GetInstance<SimpleTcpClient>();

            // 订阅所有事件
            client.Connected += () =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 连接建立事件触发");
            };

            client.Disconnected += () =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 连接断开事件触发");
            };

            client.DataReceived += (buffer, length) =>
            {
                var message = Encoding.UTF8.GetString(buffer, 0, length);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 接收到数据: {message}");
            };

            client.ErrorOccurred += (exception) =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 错误发生: {exception.Message}");
            };

            try
            {
                // 尝试连接并观察事件
                await client.TryConnectAsync();
                await Task.Delay(1000);

                // 发送数据并观察事件
                await client.SendAsync("Test Message");
                await Task.Delay(1000);

                // 手动断开并观察事件
                client.Disconnect();
                await Task.Delay(1000);
            }
            finally
            {
                client.Dispose();
            }
        }
    }
}
