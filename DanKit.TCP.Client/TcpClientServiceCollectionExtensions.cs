using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DanKit.TCP.Client
{
    /// <summary>
    /// TcpClientBase 的依赖注入扩展方法
    /// 确保子类只能以单例模式注册，与基类的单例机制完美集成
    /// </summary>
    public static class TcpClientServiceCollectionExtensions
    {
        /// <summary>
        /// 注册 TcpClientBase 子类为单例服务
        /// 直接使用基类的单例机制，确保与 GetInstance<T>() 方法获取的是同一个实例
        /// </summary>
        /// <typeparam name="T">TcpClientBase的子类型</typeparam>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddTcpClient<T>(this IServiceCollection services)
            where T : TcpClientBase, new()
        {
            // 简洁注册：直接使用基类单例机制，构造函数会自动保护
            services.TryAddSingleton<T>(provider => TcpClientBase.GetInstance<T>());

            return services;
        }
    }
}
