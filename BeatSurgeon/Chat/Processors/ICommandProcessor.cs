using System.Threading;
using System.Threading.Tasks;

namespace BeatSurgeon.Chat.Processors
{
    internal interface ICommandProcessor
    {
        string[] HandledCommands { get; }
        bool CanHandle(ChatContext ctx);
        Task ExecuteAsync(ChatContext ctx, CancellationToken ct);
    }
}
