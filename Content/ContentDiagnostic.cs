namespace Polaris.Content
{
    /// <summary>诊断严重程度：从提示到错误，供扩展内容的校验/冲突检查统一表达。</summary>
    public enum ContentDiagnosticSeverity
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// 一条内容诊断：某个扩展文件/定义在解析、校验或注册时发现的问题。取代各模块各自定义的诊断类型
    /// （如 PolarisLang 的 PlangConflict、PolarisAI.Authoring 的 PaiDiagnostic/PnpcDiagnostic）。
    /// </summary>
    public readonly struct ContentDiagnostic
    {
        public ContentDiagnostic(string code, ContentDiagnosticSeverity severity, string message, string source = null)
        {
            Code = code ?? "";
            Severity = severity;
            Message = message ?? "";
            Source = source;
        }

        /// <summary>机器可读的问题分类，例如 "duplicate-key"。</summary>
        public string Code { get; }

        public ContentDiagnosticSeverity Severity { get; }

        /// <summary>给人看的一句话说明。</summary>
        public string Message { get; }

        /// <summary>问题来源（模组程序集名、文件路径等），可为空。</summary>
        public string Source { get; }

        public override string ToString() => Source == null ? Message : $"{Message} ({Source})";
    }
}
