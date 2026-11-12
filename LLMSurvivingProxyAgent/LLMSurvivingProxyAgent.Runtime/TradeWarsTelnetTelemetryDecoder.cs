using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class TradeWarsTelnetTelemetryDecoder(
    Func<ProxyGameEvent, CancellationToken, Task> emitEvent)
{
    private const byte TelnetIac = 255;
    private const byte TelnetSubnegotiation = 250;
    private const byte TelnetWill = 251;
    private const byte TelnetWont = 252;
    private const byte TelnetDo = 253;
    private const byte TelnetDont = 254;
    private const byte TelnetSubnegotiationEnd = 240;
    private static readonly Regex AnsiEscape = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex CitadelCommandPrompt = new(@"Citadel\s+command\s*\[\s*TL\s*=", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PlanetCommandPrompt = new(@"Planet\s+command\s*(?:\[\s*TL\s*=|\(\s*\?\s*=\s*help\s*\))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CommandPrompt = new(@"Command\s*\[\s*TL\s*=", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StopInSectorPrompt = new(
        @"Stop in this sector\s*\(Y,N,E,I,R,S,D,P,\?\)\s*\(\?=Help\)\s*\[N\]\s*\?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Shown on entering a sector guarded by another player's fighters ("...destroy the fighters or pay their toll").
    private static readonly Regex FighterEncounterPrompt = new(
        @"^\s*Option\?\s*\(A,D,I,R,P?,?S,\?\)\s*:\s*\??\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AutopilotConfirmationPrompt = new(
        @"Engage\s+the\s+Autopilot\s*\?\s*\(Y/N/Single\s+step/Express\)\s*\[Y\]\s*\??",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NavPointSelectionPrompt = new(
        @"Choose\s+NavPoint\s*\(\?=Help\)\s*\[Q\]\s*:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MoveSectorPrompt = new(
        @"Which\s+Sector\s*(?:\[\s*\d+\s*\]\s*)?\?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PortTradeOfferPrompt = new(
        @"^\s*Your offer\s*\[\s*[\d,]+\s*\]\s*\?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PortTradeQuantityPrompt = new(
        @"How many holds of\s+(?:Fuel Ore|Organics|Equipment)\s+do you want to\s+(?:buy|sell)\s+\[[\d,]+\]\s*\?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PasswordConfirmationPrompt = new(
        @"(?:confirm|re-?enter|enter again|repeat).{0,20}password|password.{0,20}(?:confirm|again|repeat)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PasswordPrompt = new(@"(?:Password\s*[:?]|Enter\s+(?:the\s+)?password\b|Password\s+required\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NewCharacterPrompt = new(@"Would you like to start a new character in this game\s*\?\s*\(Type\s+Y\s+or\s+N\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TraderAliasPreviewPrompt = new(@"That alias would look like this in the game", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NameConfirmationPrompt = new(@"\bis what you want\s*\?\s*(?:\(\s*Y\s*/\s*N\s*\))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TraderAliasPrompt = new(@"Do you wish to make up a new Alias for your Trader Name", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TraderAliasChoicePrompt = new(@"Use\s+\(N\)ew Name or\s+\(B\)BS Name", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Anchored: the echo "What Alias do you want to use? Name" is not a new prompt.
    private static readonly Regex TraderAliasNamePrompt = new(@"What Alias do you want to use\s*\?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DailyLogPrompt = new(@"(?:(?:Would you like to|Do you wish to)\s+(?:read|see|view)\s+(?:the\s+)?(?:daily|today'?s?)\s+log|Show\s+(?:the\s+)?(?:daily|today'?s?)\s+log)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ClearAvoidsPrompt = new(@"Do you wish to clear some avoids\s*\?\s*\(Y/N\)\s*\[N\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ShipNamePrompt = new(@"What do you want to name your ship\s*\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HomePlanetNamePrompt = new(@"What do you want to name your (?:home\s+)?planet\s*\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NamePrompt = new(@"Please enter your name\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ServerRegistrationPrompt = new(@"Server registered to\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AccountSetupPrompt = new(@"(?:make up|create)\s+(?:a\s+)?new\s+account|create\s+(?:a\s+)?new\s+character", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ChoicePrompt = new(@"Enter your choice\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PausePrompt = new(@"\[Pause\]|\[Press Space or Enter to continue\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SectorLine = new(@"^\s*(?:Current\s+)?Sector\s*:\s*(?<sector>\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TurnsLine = new(@"^\s*Turns left\s*:\s*(?<turns>\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HoldsLine = new(@"^\s*Total Holds\s*:\s*(?<total>\d+)(?<details>.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HoldDetail = new(@"(?<name>Fuel Ore|Organics|Equipment|Colonists|Empty)\s*=\s*(?<quantity>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FightersLine = new(@"^\s*Fighters\s*:\s*(?<fighters>\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CreditsLine = new(@"^\s*Credits\s*:\s*(?<credits>\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExperienceLine = new(@"^\s*Rank and Exp\s*:\s*(?<experience>-?\d+)\s+points.*?Alignment\s*=\s*(?<alignment>-?\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CorporationLine = new(@"^\s*Corporation(?:\s+ID)?\s*:\s*(?<id>\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] KnownPromptSurfaces =
    [
        "BBSName",
        "ServerRegistration",
        "Pause",
        "Menu",
        "DailyLog",
        "ClearAvoids",
        "NewCharacter",
        "Password",
        "PasswordConfirmation",
        "TraderAlias",
        "TraderAliasChoice",
        "TraderAliasName",
        "TraderAliasPreview",
        "NameConfirmation",
        "ShipName",
        "HomePlanetName",
        "UnsupportedAccountSetup",
        "Citadel",
        "Planet",
        "Command",
        "MoveSector",
        "StopInSector",
        "FighterEncounter",
        "AutopilotConfirmation",
        "NavPointSelection",
        "PortTradeOffer",
        "PortTradeQuantity"
    ];

    private readonly StringBuilder _line = new();
    private readonly List<string> _shipInfoLines = [];
    private TelnetParseState _telnetState;
    private bool _skipLineFeed;
    private bool _partialPromptEmitted;
    private bool _collectingShipInfo;
    private bool _inNavPointPreview;
    private int? _lastSector;
    private string? _lastSynchronizedShipName;
    private string? _lastSynchronizedShipType;
    private string? _pendingScannerType;
    private string? _pendingScannerShipName;
    private string? _pendingScannerShipType;

    public async Task ObserveServerBytesAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        foreach (var value in bytes.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (_telnetState)
            {
                case TelnetParseState.Data:
                    if (value == TelnetIac)
                    {
                        _telnetState = TelnetParseState.Iac;
                    }
                    else
                    {
                        await AppendTextAsync((char)value, cancellationToken).ConfigureAwait(false);
                    }
                    break;
                case TelnetParseState.Iac:
                    if (value == TelnetIac)
                    {
                        _telnetState = TelnetParseState.Data;
                    }
                    else if (value == TelnetSubnegotiation)
                    {
                        _telnetState = TelnetParseState.Subnegotiation;
                    }
                    else if (value is TelnetWill or TelnetWont or TelnetDo or TelnetDont)
                    {
                        _telnetState = TelnetParseState.Option;
                    }
                    else
                    {
                        _telnetState = TelnetParseState.Data;
                    }
                    break;
                case TelnetParseState.Option:
                    _telnetState = TelnetParseState.Data;
                    break;
                case TelnetParseState.Subnegotiation:
                    if (value == TelnetIac)
                    {
                        _telnetState = TelnetParseState.SubnegotiationIac;
                    }
                    break;
                case TelnetParseState.SubnegotiationIac:
                    _telnetState = value == TelnetSubnegotiationEnd
                        ? TelnetParseState.Data
                        : value == TelnetIac
                            ? TelnetParseState.Subnegotiation
                            : TelnetParseState.Subnegotiation;
                    break;
                default:
                    throw new InvalidOperationException("The Telnet observer entered an unknown parser state.");
            }
        }
    }

    private async Task AppendTextAsync(char value, CancellationToken cancellationToken)
    {
        if (value == '\r')
        {
            await ProcessLineAsync(cancellationToken).ConfigureAwait(false);
            _skipLineFeed = true;
            return;
        }
        if (value == '\n')
        {
            if (!_skipLineFeed)
            {
                await ProcessLineAsync(cancellationToken).ConfigureAwait(false);
            }
            _skipLineFeed = false;
            return;
        }

        _skipLineFeed = false;
        if (value == '\0')
        {
            return;
        }

        _line.Append(value);
        await ObservePartialPromptAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessLineAsync(CancellationToken cancellationToken)
    {
        var line = Clean(_line.ToString()).Trim();
        var promptWasEmittedForPartialLine = _partialPromptEmitted;
        _line.Clear();
        _partialPromptEmitted = false;
        if (line.Length == 0)
        {
            return;
        }

        if (line.StartsWith("<Set Course to NavPoint>", StringComparison.OrdinalIgnoreCase))
        {
            _inNavPointPreview = true;
        }

        ObserveQuickStatusScanner(line);

        if (line.Contains("<Info>", StringComparison.OrdinalIgnoreCase))
        {
            _collectingShipInfo = true;
            _shipInfoLines.Clear();
        }
        if (_collectingShipInfo)
        {
            _shipInfoLines.Add(line);
        }

        if (!_inNavPointPreview &&
            TryGetSector(line, out var sector) &&
            _lastSector != sector)
        {
            _lastSector = sector;
            await emitEvent(
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow,
                    "CurrentSectorChanged",
                    CurrentSector: sector),
                cancellationToken).ConfigureAwait(false);
        }

        if (TryGetPromptSurface(line, out var surface))
        {
            if (string.Equals(surface, "NavPointSelection", StringComparison.OrdinalIgnoreCase))
            {
                _inNavPointPreview = false;
            }
            if (surface == "Command")
            {
                await CompleteShipInfoAsync(cancellationToken).ConfigureAwait(false);
            }
            if (!promptWasEmittedForPartialLine)
            {
                await EmitPromptAsync(surface, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ObservePartialPromptAsync(CancellationToken cancellationToken)
    {
        if (_partialPromptEmitted || !TryGetPromptSurface(Clean(_line.ToString()), out var surface))
        {
            return;
        }

        if (surface == "Command")
        {
            await CompleteShipInfoAsync(cancellationToken).ConfigureAwait(false);
        }
        if (string.Equals(surface, "NavPointSelection", StringComparison.OrdinalIgnoreCase))
        {
            _inNavPointPreview = false;
        }
        await EmitPromptAsync(surface, cancellationToken).ConfigureAwait(false);
        _partialPromptEmitted = true;
    }

    private async Task CompleteShipInfoAsync(CancellationToken cancellationToken)
    {
        if (!_collectingShipInfo)
        {
            return;
        }

        _collectingShipInfo = false;
        var fields = ParseShipInfo(_shipInfoLines);
        _shipInfoLines.Clear();
        if (fields is null)
        {
            ClearPendingScannerObservation();
            return;
        }

        var metadata = new Dictionary<string, string>(fields.Metadata, StringComparer.Ordinal);
        if (_pendingScannerType is not null &&
            string.Equals(metadata["shipName"], _pendingScannerShipName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(metadata["shipType"], _pendingScannerShipType, StringComparison.OrdinalIgnoreCase))
        {
            metadata["scannerType"] = _pendingScannerType;
        }

        _lastSynchronizedShipName = metadata["shipName"];
        _lastSynchronizedShipType = metadata["shipType"];
        ClearPendingScannerObservation();

        await emitEvent(
            new ProxyGameEvent(
                DateTimeOffset.UtcNow,
                "SynchronizedShipStatus",
                fields.CurrentSector,
                Metadata: metadata),
            cancellationToken).ConfigureAwait(false);
    }

    private void ObserveQuickStatusScanner(string line)
    {
        if (!line.Contains('\u00B3'))
        {
            return;
        }

        string? scannerType = null;
        foreach (var rawToken in line.Split('\u00B3', StringSplitOptions.RemoveEmptyEntries))
        {
            string token = rawToken.Trim();
            if (token.StartsWith("LRS ", StringComparison.Ordinal))
            {
                scannerType = token["LRS ".Length..].Trim();
            }
        }

        if (scannerType is null)
        {
            return;
        }
        ClearPendingScannerObservation();
        if (string.IsNullOrWhiteSpace(_lastSynchronizedShipName) ||
            string.IsNullOrWhiteSpace(_lastSynchronizedShipType))
        {
            return;
        }

        _pendingScannerType = scannerType;
        _pendingScannerShipName = _lastSynchronizedShipName;
        _pendingScannerShipType = _lastSynchronizedShipType;
    }

    private void ClearPendingScannerObservation()
    {
        _pendingScannerType = null;
        _pendingScannerShipName = null;
        _pendingScannerShipType = null;
    }

    private Task EmitPromptAsync(string surface, CancellationToken cancellationToken) =>
        emitEvent(
            new ProxyGameEvent(
                DateTimeOffset.UtcNow,
                "ServerPrompt",
                PromptSurface: surface),
            cancellationToken);

    public static bool TryGetPromptSurface(string text, out string surface)
    {
        string candidate = text.Trim();
        if (KnownPromptSurfaces.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            surface = KnownPromptSurfaces.First(value =>
                string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        return TryGetPrompt(candidate, out surface);
    }

    private static bool TryGetPrompt(string text, out string surface)
    {
        if (AutopilotConfirmationPrompt.IsMatch(text))
        {
            surface = "AutopilotConfirmation";
            return true;
        }
        if (NavPointSelectionPrompt.IsMatch(text))
        {
            surface = "NavPointSelection";
            return true;
        }
        if (MoveSectorPrompt.IsMatch(text))
        {
            surface = "MoveSector";
            return true;
        }
        if (PortTradeOfferPrompt.IsMatch(text))
        {
            surface = "PortTradeOffer";
            return true;
        }
        if (PortTradeQuantityPrompt.IsMatch(text))
        {
            surface = "PortTradeQuantity";
            return true;
        }
        if (CitadelCommandPrompt.IsMatch(text))
        {
            surface = "Citadel";
            return true;
        }
        if (PlanetCommandPrompt.IsMatch(text))
        {
            surface = "Planet";
            return true;
        }
        if (StopInSectorPrompt.IsMatch(text))
        {
            surface = "StopInSector";
            return true;
        }
        if (FighterEncounterPrompt.IsMatch(text))
        {
            surface = "FighterEncounter";
            return true;
        }
        if (CommandPrompt.IsMatch(text))
        {
            surface = "Command";
            return true;
        }
        if (PasswordConfirmationPrompt.IsMatch(text))
        {
            surface = "PasswordConfirmation";
            return true;
        }
        if (PasswordPrompt.IsMatch(text))
        {
            surface = "Password";
            return true;
        }
        if (NewCharacterPrompt.IsMatch(text))
        {
            surface = "NewCharacter";
            return true;
        }
        if (NameConfirmationPrompt.IsMatch(text))
        {
            surface = "NameConfirmation";
            return true;
        }
        if (TraderAliasPreviewPrompt.IsMatch(text))
        {
            surface = "TraderAliasPreview";
            return true;
        }
        if (TraderAliasChoicePrompt.IsMatch(text))
        {
            surface = "TraderAliasChoice";
            return true;
        }
        if (TraderAliasNamePrompt.IsMatch(text))
        {
            surface = "TraderAliasName";
            return true;
        }
        if (TraderAliasPrompt.IsMatch(text))
        {
            surface = "TraderAlias";
            return true;
        }
        if (ShipNamePrompt.IsMatch(text))
        {
            surface = "ShipName";
            return true;
        }
        if (HomePlanetNamePrompt.IsMatch(text))
        {
            surface = "HomePlanetName";
            return true;
        }
        if (AccountSetupPrompt.IsMatch(text))
        {
            surface = "NewCharacter";
            return true;
        }
        if (NamePrompt.IsMatch(text))
        {
            surface = "BBSName";
            return true;
        }
        if (ServerRegistrationPrompt.IsMatch(text))
        {
            surface = "ServerRegistration";
            return true;
        }
        if (ChoicePrompt.IsMatch(text))
        {
            surface = "Menu";
            return true;
        }
        if (DailyLogPrompt.IsMatch(text))
        {
            surface = "DailyLog";
            return true;
        }
        if (ClearAvoidsPrompt.IsMatch(text))
        {
            surface = "ClearAvoids";
            return true;
        }
        if (PausePrompt.IsMatch(text))
        {
            surface = "Pause";
            return true;
        }

        surface = string.Empty;
        return false;
    }

    private static bool TryGetSector(string line, out int sector)
    {
        var match = SectorLine.Match(line);
        return int.TryParse(
            match.Groups["sector"].Value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out sector);
    }

    private static ShipInfoFields? ParseShipInfo(IReadOnlyList<string> lines)
    {
        int? sector = null;
        long? credits = null;
        int? turns = null;
        int? fighters = null;
        int? holdsEmpty = null;
        int? holdsTotal = null;
        int? fuelOre = null;
        int? organics = null;
        int? equipment = null;
        int? colonists = null;
        long? experience = null;
        long? alignment = null;
        int? corporationId = null;
        string? shipName = null;
        string? shipType = null;

        foreach (var line in lines)
        {
            var separator = line.IndexOf(':');
            var value = separator >= 0 ? line[(separator + 1)..].Trim() : string.Empty;
            if (line.StartsWith("Ship Name", StringComparison.OrdinalIgnoreCase))
            {
                shipName = value;
            }
            else if (line.StartsWith("Ship Info", StringComparison.OrdinalIgnoreCase))
            {
                var portedIndex = value.IndexOf(" Ported=", StringComparison.OrdinalIgnoreCase);
                shipType = (portedIndex >= 0 ? value[..portedIndex] : value).Trim();
            }
            else if (line.StartsWith("Current Sector", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSector))
            {
                sector = parsedSector;
            }
            else if (line.StartsWith("Turns left", StringComparison.OrdinalIgnoreCase) &&
                     TryParseInt64(TurnsLine, line, "turns", out var parsedTurns))
            {
                turns = parsedTurns is <= int.MaxValue ? (int)parsedTurns : null;
            }
            else if (line.StartsWith("Total Holds", StringComparison.OrdinalIgnoreCase))
            {
                var match = HoldsLine.Match(line);
                if (!int.TryParse(
                        match.Groups["total"].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var parsedTotal))
                {
                    continue;
                }

                holdsTotal = parsedTotal;
                int? reportedEmpty = null;
                bool hasCargoBreakdown = false;
                foreach (Match detail in HoldDetail.Matches(match.Groups["details"].Value))
                {
                    if (!int.TryParse(
                            detail.Groups["quantity"].Value,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out int quantity))
                    {
                        continue;
                    }

                    switch (detail.Groups["name"].Value.ToUpperInvariant())
                    {
                        case "EMPTY":
                            reportedEmpty = quantity;
                            break;
                        case "FUEL ORE":
                            fuelOre = quantity;
                            hasCargoBreakdown = true;
                            break;
                        case "ORGANICS":
                            organics = quantity;
                            hasCargoBreakdown = true;
                            break;
                        case "EQUIPMENT":
                            equipment = quantity;
                            hasCargoBreakdown = true;
                            break;
                        case "COLONISTS":
                            colonists = quantity;
                            hasCargoBreakdown = true;
                            break;
                    }
                }

                if (hasCargoBreakdown)
                {
                    fuelOre ??= 0;
                    organics ??= 0;
                    equipment ??= 0;
                    colonists ??= 0;
                    long occupiedHolds =
                        (long)fuelOre.Value + organics.Value + equipment.Value + colonists.Value;
                    long calculatedEmpty = parsedTotal - occupiedHolds;
                    if (calculatedEmpty < 0 ||
                        reportedEmpty is { } explicitEmpty && explicitEmpty != calculatedEmpty)
                    {
                        return null;
                    }

                    holdsEmpty = reportedEmpty ?? (int)calculatedEmpty;
                }
                else
                {
                    holdsEmpty = reportedEmpty;
                    if (holdsEmpty == parsedTotal)
                    {
                        fuelOre = 0;
                        organics = 0;
                        equipment = 0;
                    }
                }
            }
            else if (line.StartsWith("Fighters", StringComparison.OrdinalIgnoreCase) &&
                     TryParseInt64(FightersLine, line, "fighters", out var parsedFighters) &&
                     parsedFighters <= int.MaxValue)
            {
                fighters = (int)parsedFighters;
            }
            else if (line.StartsWith("Credits", StringComparison.OrdinalIgnoreCase) &&
                     TryParseInt64(CreditsLine, line, "credits", out var parsedCredits))
            {
                credits = parsedCredits;
            }
            else if (ExperienceLine.Match(line) is { Success: true } experienceMatch)
            {
                if (long.TryParse(experienceMatch.Groups["experience"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedExperience))
                {
                    experience = parsedExperience;
                }
                if (long.TryParse(experienceMatch.Groups["alignment"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAlignment))
                {
                    alignment = parsedAlignment;
                }
            }
            else if (CorporationLine.Match(line) is { Success: true } corporationMatch &&
                     int.TryParse(corporationMatch.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCorporationId))
            {
                corporationId = parsedCorporationId;
            }
        }

        if (sector is null || credits is null || turns is null || fighters is null ||
            string.IsNullOrWhiteSpace(shipType))
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["currentSector"] = sector.Value.ToString(CultureInfo.InvariantCulture),
            ["credits"] = credits.Value.ToString(CultureInfo.InvariantCulture),
            ["turns"] = turns.Value.ToString(CultureInfo.InvariantCulture),
            ["fighters"] = fighters.Value.ToString(CultureInfo.InvariantCulture),
            ["shipType"] = shipType,
            ["shipName"] = shipName ?? string.Empty
        };
        AddOptional(metadata, "holdsEmpty", holdsEmpty);
        AddOptional(metadata, "holdsTotal", holdsTotal);
        AddOptional(metadata, "fuelOre", fuelOre);
        AddOptional(metadata, "organics", organics);
        AddOptional(metadata, "equipment", equipment);
        AddOptional(metadata, "experience", experience);
        AddOptional(metadata, "alignment", alignment);
        AddOptional(metadata, "corporationId", corporationId);
        return new ShipInfoFields(sector.Value, metadata);
    }

    private static bool TryParseInt64(Regex regex, string line, string group, out long value)
    {
        var match = regex.Match(line);
        return long.TryParse(
            match.Groups[group].Value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static void AddOptional<T>(IDictionary<string, string> metadata, string name, T? value)
        where T : struct, IFormattable
    {
        if (value is { } present)
        {
            metadata[name] = present.ToString(null, CultureInfo.InvariantCulture);
        }
    }

    private static string Clean(string value) =>
        AnsiEscape.Replace(value, string.Empty)
            .Replace("\u001b", string.Empty, StringComparison.Ordinal)
            .Replace('\0', ' ');

    private sealed record ShipInfoFields(int CurrentSector, IReadOnlyDictionary<string, string> Metadata);

    private enum TelnetParseState
    {
        Data,
        Iac,
        Option,
        Subnegotiation,
        SubnegotiationIac
    }
}
