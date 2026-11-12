using System.Text;

namespace MTC;

/// <summary>
/// Lets the coach type <c>!coach &lt;guidance&gt;</c> in the game terminal without the text reaching the game.
/// TradeWars acts on single keystrokes, so a '!' is held back only while the following keys still spell
/// "coach"; any other key releases the held bytes to the game unchanged. Once matched, the line is captured
/// locally with echo: Enter submits it, Escape cancels, Backspace edits.
/// </summary>
internal sealed class CoachInputInterceptor(Action<string> echo, Action<string> submit)
{
    private const string Trigger = "!coach";
    private const int MaximumLineLength = 1_000;

    private readonly StringBuilder _held = new();
    private readonly StringBuilder _line = new();
    private Mode _mode = Mode.Passthrough;

    private enum Mode
    {
        Passthrough,
        Matching,
        Capturing
    }

    public bool IsCapturing => _mode == Mode.Capturing;

    /// <summary>Processes keyboard bytes and returns the bytes that should still be sent to the game.</summary>
    public byte[] Process(byte[] input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var forward = new List<byte>(input.Length);
        foreach (byte value in input)
        {
            switch (_mode)
            {
                case Mode.Passthrough:
                    if (value == (byte)'!')
                    {
                        _held.Clear().Append('!');
                        _mode = Mode.Matching;
                    }
                    else
                    {
                        forward.Add(value);
                    }
                    break;

                case Mode.Matching:
                    char next = (char)value;
                    string candidate = _held.ToString() + next;
                    if (Trigger.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        _held.Append(next);
                        if (_held.Length == Trigger.Length)
                        {
                            _mode = Mode.Capturing;
                            _line.Clear().Append(Trigger);
                            _held.Clear();
                            echo("\r\n\x1b[1;36m" + Trigger);
                        }
                    }
                    else
                    {
                        // Not a coach command: release everything held, then re-examine this byte normally.
                        forward.AddRange(Encoding.Latin1.GetBytes(_held.ToString()));
                        _held.Clear();
                        _mode = Mode.Passthrough;
                        if (value == (byte)'!')
                        {
                            _held.Append('!');
                            _mode = Mode.Matching;
                        }
                        else
                        {
                            forward.Add(value);
                        }
                    }
                    break;

                case Mode.Capturing:
                    if (value is (byte)'\r' or (byte)'\n')
                    {
                        string line = _line.ToString();
                        _line.Clear();
                        _mode = Mode.Passthrough;
                        echo("\x1b[0m\r\n");
                        submit(line);
                    }
                    else if (value == 0x1b)
                    {
                        _line.Clear();
                        _mode = Mode.Passthrough;
                        echo(" [coach cancelled]\x1b[0m\r\n");
                    }
                    else if (value is 0x08 or 0x7f)
                    {
                        if (_line.Length > Trigger.Length)
                        {
                            _line.Length--;
                            echo("\b \b");
                        }
                    }
                    else if (value >= 0x20 && value < 0x7f && _line.Length < MaximumLineLength)
                    {
                        _line.Append((char)value);
                        echo(((char)value).ToString());
                    }
                    break;
            }
        }
        return forward.ToArray();
    }
}
