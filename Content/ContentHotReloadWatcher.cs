using System;
using System.Collections.Generic;
using System.IO;

namespace Polaris.Content
{
    /// <summary>
    /// 通用的目录轮询热重载工具：按固定间隔比较文件修改时间，变化（含首次出现）时回调一次文件路径。
    /// 取代各模块各自实现的轮询热重载（如 PolarisAI 的 PaiHotReload）。刻意不用
    /// <see cref="FileSystemWatcher"/>——Unity/BepInEx 环境下文件锁与事件丢失更常见，轮询更省心。
    /// 只做"文件变了"这一件事：解析、编译、写回目录都留给调用方的回调。
    /// </summary>
    public sealed class ContentHotReloadWatcher
    {
        readonly string directory;
        readonly string searchPattern;
        readonly Action<string> onChanged;
        readonly double pollIntervalSeconds;
        readonly Dictionary<string, DateTime> stamps = new(StringComparer.OrdinalIgnoreCase);
        double elapsed;

        public ContentHotReloadWatcher(string directory, string searchPattern, Action<string> onChanged, double pollIntervalSeconds = 0.5)
        {
            this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
            this.searchPattern = searchPattern ?? throw new ArgumentNullException(nameof(searchPattern));
            this.onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
            this.pollIntervalSeconds = pollIntervalSeconds;
        }

        /// <summary>创建目录（如果不存在）并做一次初始全量扫描，把所有已存在的文件当作"变化"各回调一次。</summary>
        public void Initialize()
        {
            Directory.CreateDirectory(directory);
            Scan();
        }

        /// <summary>每帧调用；累计到轮询间隔才真正扫描一次。</summary>
        public void Tick(float deltaTime)
        {
            elapsed += Math.Max(0, deltaTime);
            if (elapsed < pollIntervalSeconds)
            {
                return;
            }

            elapsed = 0;
            Scan();
        }

        void Scan()
        {
            foreach (string path in Directory.EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories))
            {
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (stamps.TryGetValue(path, out DateTime old) && old == stamp)
                {
                    continue;
                }

                stamps[path] = stamp;
                onChanged(path);
            }
        }
    }
}
