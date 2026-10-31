namespace LLMSurvivingProxyAgent.Runtime;

internal sealed class TradeWarsTelnetClientNegotiator
{
    private const byte TelnetIac = 255;
    private const byte TelnetSubnegotiation = 250;
    private const byte TelnetWill = 251;
    private const byte TelnetWont = 252;
    private const byte TelnetDo = 253;
    private const byte TelnetDont = 254;
    private const byte TelnetSubnegotiationEnd = 240;

    private TelnetParseState _state;

    public byte[] CreateResponses(ReadOnlySpan<byte> bytes)
    {
        List<byte>? responses = null;
        foreach (var value in bytes)
        {
            switch (_state)
            {
                case TelnetParseState.Data when value == TelnetIac:
                    _state = TelnetParseState.Iac;
                    break;
                case TelnetParseState.Data:
                    break;
                case TelnetParseState.Iac:
                    _state = value switch
                    {
                        TelnetDo => TelnetParseState.DoOption,
                        TelnetWill => TelnetParseState.WillOption,
                        TelnetWont or TelnetDont => TelnetParseState.IgnoreOption,
                        TelnetSubnegotiation => TelnetParseState.Subnegotiation,
                        _ => TelnetParseState.Data
                    };
                    break;
                case TelnetParseState.DoOption:
                    responses ??= [];
                    responses.Add(TelnetIac);
                    responses.Add(TelnetWont);
                    responses.Add(value);
                    _state = TelnetParseState.Data;
                    break;
                case TelnetParseState.WillOption:
                    responses ??= [];
                    responses.Add(TelnetIac);
                    responses.Add(TelnetDont);
                    responses.Add(value);
                    _state = TelnetParseState.Data;
                    break;
                case TelnetParseState.IgnoreOption:
                    _state = TelnetParseState.Data;
                    break;
                case TelnetParseState.Subnegotiation when value == TelnetIac:
                    _state = TelnetParseState.SubnegotiationIac;
                    break;
                case TelnetParseState.Subnegotiation:
                    break;
                case TelnetParseState.SubnegotiationIac:
                    _state = value == TelnetSubnegotiationEnd
                        ? TelnetParseState.Data
                        : TelnetParseState.Subnegotiation;
                    break;
                default:
                    throw new InvalidOperationException("The Telnet negotiator entered an unknown parser state.");
            }
        }

        return responses?.ToArray() ?? [];
    }

    private enum TelnetParseState
    {
        Data,
        Iac,
        DoOption,
        WillOption,
        IgnoreOption,
        Subnegotiation,
        SubnegotiationIac
    }
}
