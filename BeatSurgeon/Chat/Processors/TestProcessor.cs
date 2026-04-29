using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    internal sealed class TestProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("TestProcessor");

        public string[] HandledCommands => new[] { "!test" };

        public bool CanHandle(ChatContext ctx) => true;

        public Task ExecuteAsync(ChatContext ctx, CancellationToken ct)
        {
            _log.Command(ctx?.Username, ctx?.Command, true, "noop");
            return Task.CompletedTask;
        }
    }
}
