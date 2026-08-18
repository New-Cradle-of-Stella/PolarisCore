using System;
using System.Collections.Generic;

namespace Polaris.Drawing.Internal
{
    /// <summary>
    /// 两个后端（Screen/Map）共用的最小契约。<see cref="DrawingSurfaceRuntime"/> 只认这套接口，
    /// 不知道也不关心 ver029 类型；契约里的 <c>nodeId</c> 就是节点在本 Surface 内的序号。
    /// </summary>
    internal interface IDrawingBackend : IDisposable
    {
        void CreateNode(int nodeId);

        /// <summary>
        /// 用新录制的命令重建这个节点的几何/文本缓存。实现必须在结束前用自己已经保存的
        /// Transform/Opacity/Visible/Order 重新应用一遍（<see cref="DrawingSurfaceRuntime"/> 不会在
        /// 重建后再补发一次），这样节点在 Invalidate 前后的可见状态才是连续的。
        /// </summary>
        void RebuildNode(int nodeId, IReadOnlyList<DrawOp> ops);

        void SetTransform(int nodeId, DrawTransform transform);

        void SetOpacity(int nodeId, float opacity);

        void SetVisible(int nodeId, bool visible);

        void SetOrder(int nodeId, int order);

        void DestroyNode(int nodeId);

        /// <summary>节点的 Order/Visible/存在性发生变化后调用一次，重新计算整个 Surface 的提交顺序。</summary>
        void Relayout(IReadOnlyList<int> nodeIdsInOrder);

        /// <summary>由 <see cref="DrawingRuntime"/> 每帧调用；Screen 通常是空实现，Map 用来推进 Follow。</summary>
        void Pump(float deltaSeconds);

        DrawRect ComputeBounds(IReadOnlyList<int> nodeIds);

        DrawingDebugStats.BackendStats GetStats();

        void SetSurfacePosition(DrawPoint position);

        void SetSurfaceVisible(bool visible);
    }

    /// <summary>
    /// 一个 Surface 的完整生命周期管理：节点表、构建回调的重放、Order/Visible 变化触发的重新排布，
    /// 以及（仅 Map）跟随目标的驱动。<see cref="DrawingSurface"/>/<see cref="DrawNode"/> 都只是薄包装，
    /// 真正的状态都在这里。
    /// </summary>
    internal sealed class DrawingSurfaceRuntime : IDisposable
    {
        sealed class NodeRecord
        {
            internal int Id;
            internal Action<DrawContext> Build;
            internal DrawTransform Transform = DrawTransform.Identity;
            internal float Opacity = 1f;
            internal bool Visible = true;
            internal int Order;
        }

        readonly Dictionary<int, NodeRecord> nodes = new();
        readonly List<int> nodeIds = new();
        readonly IDrawingBackend backend;
        int nextNodeId;
        string resolvedDebugName;
        DrawPoint position;
        bool visible = true;
        MapFollowRuntime activeFollow;
        int rebuildCount;
        bool disposed;

        internal DrawingSurfaceRuntime(DrawingSurfaceOptions options)
        {
            Space = options.Space;
            Plane = options.Plane;
            Lifetime = options.Lifetime;
            SurfaceOrder = options.Order;
            PixelSnap = options.PixelSnap;
            resolvedDebugName = options.DebugName;

            DrawSpaceRules.Validate(Space, Plane);

            backend = Space == DrawSpace.Screen
                ? new ScreenDrawingBackend(Plane, SurfaceOrder, resolvedDebugName ?? "surface")
                : new MapDrawingBackend(Plane, resolvedDebugName ?? "surface");
        }

        internal DrawSpace Space { get; }

        internal DrawPlane Plane { get; }

        internal DrawLifetime Lifetime { get; }

        internal int SurfaceOrder { get; }

        internal bool PixelSnap { get; }

        internal bool IsDisposed => disposed;

        internal bool Visible
        {
            get => visible;
            set
            {
                EnsureNotDisposed();
                if (visible == value)
                {
                    return;
                }
                visible = value;
                backend.SetSurfaceVisible(value);
            }
        }

        internal DrawPoint Position
        {
            get => position;
            set
            {
                EnsureNotDisposed();
                position = value;
                backend.SetSurfacePosition(value);
            }
        }

        internal DrawRect Bounds
        {
            get
            {
                EnsureNotDisposed();
                return backend.ComputeBounds(nodeIds);
            }
        }

        internal DrawingDebugStats.BackendStats Stats => backend.GetStats();

        internal int RebuildCount => rebuildCount;

        internal bool HasActiveFollow => activeFollow != null;

        internal DrawNode Add(Action<DrawContext> build)
        {
            EnsureNotDisposed();
            if (build == null)
            {
                throw new ArgumentNullException(nameof(build));
            }

            // 调用方没给 DebugName 时，从构建回调的声明程序集反查插件 GUID 当名字，不做栈回溯。
            resolvedDebugName ??= Polaris.Infra.CallbackOwnerResolver.ResolveGuid(build.Method);

            int id = nextNodeId++;
            var record = new NodeRecord { Id = id, Build = build };
            nodes[id] = record;
            nodeIds.Add(id);

            backend.CreateNode(id);
            RunBuild(record);
            Relayout();

            return new DrawNode(this, id);
        }

        internal MapFollowHandle Follow(IMapDrawTarget target, MapFollowOptions options)
        {
            EnsureNotDisposed();
            if (Space != DrawSpace.Map)
            {
                throw new NotSupportedException("DrawingSurface.Follow is only meaningful for DrawSpace.Map surfaces.");
            }
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            activeFollow?.Dispose();
            var runtime = new MapFollowRuntime(target, options ?? new MapFollowOptions());
            activeFollow = runtime;
            return new MapFollowHandle(this, runtime);
        }

        /// <summary>由 <see cref="MapFollowHandle.Dispose"/> 调用；只在它仍是当前生效的 Follow 时才清空。</summary>
        internal void OnFollowDisposed(MapFollowRuntime runtime)
        {
            if (ReferenceEquals(activeFollow, runtime))
            {
                activeFollow = null;
            }
        }

        internal void Pump(float deltaSeconds)
        {
            if (disposed)
            {
                return;
            }

            if (activeFollow != null)
            {
                activeFollow.Update(deltaSeconds);
                if (activeFollow.IsDisposed)
                {
                    activeFollow = null;
                }
                else if (activeFollow.Visible)
                {
                    Position = activeFollow.Position;
                    Visible = true;
                }
                else
                {
                    Visible = false;
                }
            }

            backend.Pump(deltaSeconds);
        }

        internal void InvalidateAll()
        {
            EnsureNotDisposed();
            foreach (int id in nodeIds)
            {
                if (nodes.TryGetValue(id, out NodeRecord record))
                {
                    RunBuild(record);
                }
            }
        }

        internal void InvalidateNode(int id)
        {
            EnsureNotDisposed();
            RunBuild(nodes[id]);
        }

        internal DrawTransform GetNodeTransform(int id) => nodes[id].Transform;

        internal void SetNodeTransform(int id, DrawTransform value)
        {
            EnsureNotDisposed();
            nodes[id].Transform = value;
            backend.SetTransform(id, value);
        }

        internal float GetNodeOpacity(int id) => nodes[id].Opacity;

        internal void SetNodeOpacity(int id, float value)
        {
            EnsureNotDisposed();
            nodes[id].Opacity = value;
            backend.SetOpacity(id, value);
        }

        internal bool GetNodeVisible(int id) => nodes[id].Visible;

        internal void SetNodeVisible(int id, bool value)
        {
            EnsureNotDisposed();
            nodes[id].Visible = value;
            backend.SetVisible(id, value);
            Relayout();
        }

        internal int GetNodeOrder(int id) => nodes[id].Order;

        internal void SetNodeOrder(int id, int value)
        {
            EnsureNotDisposed();
            nodes[id].Order = value;
            backend.SetOrder(id, value);
            Relayout();
        }

        internal void RemoveNode(int id)
        {
            if (disposed || !nodes.Remove(id))
            {
                return;
            }

            nodeIds.Remove(id);
            backend.DestroyNode(id);
            Relayout();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            activeFollow?.Dispose();
            activeFollow = null;
            backend.Dispose();
            nodes.Clear();
            nodeIds.Clear();
        }

        void RunBuild(NodeRecord record)
        {
            var buffer = new DrawCommandBuffer();
            record.Build(buffer);
            buffer.Seal();
            IReadOnlyList<DrawOp> ops = buffer.Ops;

            // MTRX/字体只有 LoadStage == 7 才能碰；还没就绪时把这次重建的落地推迟到就绪那一帧，
            // 已经就绪时 WhenReady 会同步立即执行，行为和过去直接调用一致。
            Polaris.API.GameSessionRuntime.WhenReady(() => backend.RebuildNode(record.Id, ops));
            rebuildCount++;
        }

        void Relayout() => backend.Relayout(nodeIds);

        void EnsureNotDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(DrawingSurface));
            }
        }
    }

    /// <summary>Screen/Map 合法 Space+Plane 组合校验；非法组合立即抛异常，不静默降级。</summary>
    internal static class DrawSpaceRules
    {
        internal static void Validate(DrawSpace space, DrawPlane plane)
        {
            bool valid = space switch
            {
                DrawSpace.Screen => plane is DrawPlane.Background or DrawPlane.Hud or DrawPlane.Overlay,
                DrawSpace.Map => plane is DrawPlane.WorldBehindActors or DrawPlane.WorldActors or DrawPlane.WorldForeground,
                _ => false,
            };

            if (!valid)
            {
                throw new NotSupportedException($"{plane} is not a valid plane for {space}.");
            }
        }
    }
}
