using System.Threading.Channels;
using SmartOnmyoji.Core.Events;

namespace SmartOnmyoji.Core.Engine;

/// <summary>自动化引擎。事件通过 <see cref="Events"/> 通道对外流出,与 UI 完全解耦。</summary>
public interface IAutomationEngine
{
    ChannelReader<EngineEvent> Events { get; }
    Task RunAsync(EngineOptions options, CancellationToken cancellationToken);
}
