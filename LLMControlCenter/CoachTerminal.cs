using Avalonia.Threading;

namespace MTC;

public partial class MainWindow
{
    private CoachInputInterceptor? _coachInterceptor;
    private Action<byte[]>? _keyboardInputHandler;

    /// <summary>Routes typed <c>!coach ...</c> lines to the agent's playbook instead of the game.</summary>
    private Action<byte[]> WithCoachCommands(Action<byte[]> sendToGame)
    {
        _coachInterceptor ??= new CoachInputInterceptor(WriteCoachEcho, SubmitCoachCommand);
        CoachInputInterceptor interceptor = _coachInterceptor;
        return bytes =>
        {
            byte[] forward = interceptor.Process(bytes);
            if (forward.Length > 0)
                sendToGame(forward);
        };
    }

    private void WriteCoachEcho(string text)
    {
        void Write()
        {
            _parser.Feed(text);
            _buffer.Dirty = true;
        }

        if (Dispatcher.UIThread.CheckAccess())
            Write();
        else
            Dispatcher.UIThread.Post(Write);
    }

    private void SubmitCoachCommand(string line)
    {
        _ = Task.Run(async () =>
        {
            string message = await _dashboardHost
                .SubmitCoachCommandAsync(DefaultAgentId, line)
                .ConfigureAwait(false);
            WriteCoachEcho($"\x1b[1;36m[coach] {message.Replace("\r", " ").Replace("\n", " ")}\x1b[0m\r\n");
        });
    }
}
