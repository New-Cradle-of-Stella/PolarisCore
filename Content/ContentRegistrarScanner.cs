using System;

namespace Polaris.Content
{
    /// <summary>
    /// 通用的"扫描标了 <typeparamref name="TAttr"/> 的类 → 实例化 → 调用"流程，取代各模块各自实现的属性扫描
    /// 注册样板（如 PolarisLang 的 PlangRegistryScanner、PolarisAI 的 PnpcRegistry.ScanModules，两者曾是
    /// 几乎逐行相同的独立实现）。只负责发现与调用，不规定 <typeparamref name="TRegistrar"/> 的注册协议本身
    /// ——各模块已有的注册接口签名（含 PolarisTools 生成代码依赖的签名）不受影响。
    /// </summary>
    public static class ContentRegistrarScanner
    {
        /// <summary>
        /// 扫描 <c>PolarisAPI.Types.InPluginsWith&lt;TAttr&gt;()</c> 找到的类型，逐个实例化（允许非公开构造函数）并转换为
        /// <typeparamref name="TRegistrar"/>，连同触发扫描的类型一起交给 <paramref name="run"/> 处理（例如据此设置冲突判定用的"当前来源"）。
        /// 一个类型实例化或处理失败不影响其它类型；默认通过 <see cref="PolarisAPI.Errors"/> 上报。
        /// </summary>
        /// <returns>成功处理的类型数量。</returns>
        public static int ScanAndRun<TAttr, TRegistrar>(Action<TRegistrar, Type> run, Action<Exception, Type> onError = null)
            where TAttr : Attribute
            where TRegistrar : class
        {
            if (run == null)
            {
                throw new ArgumentNullException(nameof(run));
            }

            int count = 0;
            foreach ((Type type, TAttr _) in PolarisAPI.Types.InPluginsWith<TAttr>())
            {
                if (type.IsAbstract || type.IsInterface || !typeof(TRegistrar).IsAssignableFrom(type))
                {
                    continue;
                }

                try
                {
                    var registrar = (TRegistrar)Activator.CreateInstance(type, true);
                    run(registrar, type);
                    count++;
                }
                catch (Exception e)
                {
                    if (onError != null)
                    {
                        onError(e, type);
                    }
                    else
                    {
                        PolarisAPI.Errors.Report(e, $"Auto-registering {type.FullName}", type.Assembly);
                    }
                }
            }

            return count;
        }
    }
}
