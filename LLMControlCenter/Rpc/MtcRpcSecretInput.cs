using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MTC;

/// <summary>
/// Process-only configuration is opt-in via MTC_RPC_PROCESS_ONLY=1 and
/// MTC_RPC_PROCESS_TOKEN. Credential input additionally requires
/// MTC_RPC_SECRET_INPUT_ENABLED=1 and MTC_RPC_SECRET_CREDENTIAL_TARGET.
/// The credential blob is a short printable-ASCII password and is never persisted here.
/// </summary>
internal sealed class MtcRpcProcessConfiguration
{
    public bool Requested { get; private init; }
    public bool Valid { get; private init; }
    public string AuthToken { get; private init; } = string.Empty;
    public int Port { get; private init; } = 7623;
    public bool SecretInputEnabled { get; private init; }
    public string CredentialTarget { get; private init; } = string.Empty;

    public static MtcRpcProcessConfiguration ReadFromEnvironment()
    {
        bool requested = string.Equals(
            Environment.GetEnvironmentVariable("MTC_RPC_PROCESS_ONLY"),
            "1",
            StringComparison.Ordinal);
        if (!requested)
            return new MtcRpcProcessConfiguration();

        string token = Environment.GetEnvironmentVariable("MTC_RPC_PROCESS_TOKEN")?.Trim() ?? string.Empty;
        bool validToken = token.Length is >= 32 and <= 256 &&
                          !token.Any(char.IsWhiteSpace);
        int port = int.TryParse(Environment.GetEnvironmentVariable("MTC_RPC_PROCESS_PORT"), out int configuredPort) &&
                   configuredPort is >= 1024 and <= 65535
            ? configuredPort
            : 7623;

        string credentialTarget =
            Environment.GetEnvironmentVariable("MTC_RPC_SECRET_CREDENTIAL_TARGET")?.Trim() ?? string.Empty;
        bool secretInputEnabled =
            validToken &&
            string.Equals(
                Environment.GetEnvironmentVariable("MTC_RPC_SECRET_INPUT_ENABLED"),
                "1",
                StringComparison.Ordinal) &&
            credentialTarget.Length is > 0 and <= 256;

        return new MtcRpcProcessConfiguration
        {
            Requested = true,
            Valid = validToken,
            AuthToken = validToken ? token : string.Empty,
            Port = port,
            SecretInputEnabled = secretInputEnabled,
            CredentialTarget = secretInputEnabled ? credentialTarget : string.Empty,
        };
    }
}

internal sealed record MtcRpcSensitivePrompt(string PromptId, string Stage);

internal sealed class MtcRpcPasswordConfirmationGuard : IDisposable
{
    private byte[]? _key;
    private byte[]? _fingerprint;

    public void Begin(byte[] secret)
    {
        Clear();
        _key = RandomNumberGenerator.GetBytes(32);
        _fingerprint = HMACSHA256.HashData(_key, secret);
    }

    public bool Matches(byte[] secret)
    {
        if (_key == null || _fingerprint == null)
            return false;

        byte[] candidate = HMACSHA256.HashData(_key, secret);
        try
        {
            return CryptographicOperations.FixedTimeEquals(_fingerprint, candidate);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidate);
        }
    }

    public void Clear()
    {
        if (_key != null)
            CryptographicOperations.ZeroMemory(_key);
        if (_fingerprint != null)
            CryptographicOperations.ZeroMemory(_fingerprint);
        _key = null;
        _fingerprint = null;
    }

    public void Dispose() => Clear();
}

internal static class MtcRpcSecretInputPolicy
{
    private static readonly IPAddress ApprovedAddress = IPAddress.Parse("192.168.4.75");
    private static readonly TimeSpan PromptFreshness = TimeSpan.FromMinutes(5);
    internal const int MaximumPasswordBytes = 64;
    private static readonly Regex PasswordWord = new(@"\bpasswords?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ConfirmationWord = new(
        @"\b(confirm|confirmation|again|re[- ]?enter|repeat)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RepeatPasswordInstruction = new(
        @"\b(?:repeat|re[- ]?enter)\b.{0,48}\bpassword\b.{0,64}\b(?:verify|confirm)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PasswordMismatch = new(
        @"\b(?:mismatch|(?:does|do|did) not match|doesn't match|don't match|didn't match|must match)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PasswordRejected = new(
        @"\b(?:invalid|incorrect|rejected|try again)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsRequestAllowed(
        bool loopbackRequest,
        bool processOnly,
        bool secretInputEnabled,
        bool credentialTargetConfigured)
        => loopbackRequest && processOnly && secretInputEnabled && credentialTargetConfigured;

    public static bool IsApprovalModeAllowed(string approvalLevel)
        => string.Equals(
            approvalLevel,
            "approve-actions",
            StringComparison.Ordinal);

    public static MtcRpcSensitivePrompt? FindCurrentAccountSetupPasswordPrompt(
        IReadOnlyList<GameAgentEvent> events,
        bool connected,
        string gameName,
        string host,
        int port,
        DateTimeOffset now)
    {
        if (!connected ||
            !string.Equals(gameName, "Meridian Game D", StringComparison.OrdinalIgnoreCase) ||
            port != 2002 ||
            !IPAddress.TryParse(host, out IPAddress? address) ||
            !ApprovedAddress.Equals(address) ||
            events.Count == 0)
        {
            return null;
        }

        int newCharacterPromptIndex = -1;
        int commandPromptIndex = -1;
        int disconnectIndex = -1;
        int currentPromptIndex = -1;
        var credentialInputIndexes = new List<int>();
        for (int index = 0; index < events.Count; index++)
        {
            GameAgentEvent evt = events[index];
            if (!string.Equals(evt.GameName, "Meridian Game D", StringComparison.OrdinalIgnoreCase))
                continue;

            if (evt.Kind == GameAgentEventKind.Disconnected)
                disconnectIndex = index;

            if (evt.Kind != GameAgentEventKind.ServerPrompt)
            {
                if (evt.Kind == GameAgentEventKind.ClientInput &&
                    evt.Metadata.TryGetValue("inputType", out string? inputType) &&
                    string.Equals(inputType, "account-setup-credential", StringComparison.Ordinal))
                {
                    credentialInputIndexes.Add(index);
                }

                continue;
            }

            if (IsNewCharacterStartPrompt(evt.PlainText))
                newCharacterPromptIndex = index;
            if (evt.PlainText.TrimStart().StartsWith("Command [TL=", StringComparison.OrdinalIgnoreCase))
                commandPromptIndex = index;
            currentPromptIndex = index;
        }

        if (newCharacterPromptIndex < 0 ||
            newCharacterPromptIndex <= commandPromptIndex ||
            newCharacterPromptIndex <= disconnectIndex ||
            currentPromptIndex <= newCharacterPromptIndex)
        {
            return null;
        }

        GameAgentEvent setupPrompt = events[currentPromptIndex];
        if (now - setupPrompt.Timestamp > PromptFreshness ||
            setupPrompt.Timestamp > now.AddSeconds(5) ||
            now - events[newCharacterPromptIndex].Timestamp > PromptFreshness ||
            events.Skip(currentPromptIndex + 1).Any(evt =>
                evt.Kind is GameAgentEventKind.ClientInput or GameAgentEventKind.Disconnected))
        {
            return null;
        }

        int[] setupCredentialInputIndexes = credentialInputIndexes
            .Where(index => index > newCharacterPromptIndex)
            .ToArray();
        int credentialInputCount = setupCredentialInputIndexes.Length;
        int lastCredentialInputIndex = credentialInputCount == 0
            ? -1
            : setupCredentialInputIndexes[^1];
        int instructionEndIndex = lastCredentialInputIndex >= 0
            ? lastCredentialInputIndex
            : events.Count;
        bool repeatPasswordRequired = events
            .Skip(newCharacterPromptIndex + 1)
            .Take(Math.Max(0, instructionEndIndex - newCharacterPromptIndex - 1))
            .Any(evt => RepeatPasswordInstruction.IsMatch(evt.PlainText));

        string? stage = null;
        if (setupPrompt.Metadata.TryGetValue("protectedSetupPromptStage", out string? protectedStage) &&
            (protectedStage == "password" || protectedStage == "password-confirmation"))
        {
            stage = protectedStage;
        }
        else
        {
            stage = ClassifyPasswordPrompt(setupPrompt.PlainText);
        }

        if (stage == null)
            return null;

        if (credentialInputCount == 0)
        {
            stage = "password";
        }
        else if (credentialInputCount == 1 && repeatPasswordRequired)
        {
            stage = "password-confirmation";
        }
        else
        {
            return null;
        }

        string promptId = $"{setupPrompt.Timestamp.UtcDateTime.Ticks}:{stage}";
        return new MtcRpcSensitivePrompt(promptId, stage);
    }

    public static string? ClassifyPasswordPrompt(string? promptText)
    {
        if (string.IsNullOrWhiteSpace(promptText) || !PasswordWord.IsMatch(promptText))
            return null;

        return ConfirmationWord.IsMatch(promptText)
            ? "password-confirmation"
            : "password";
    }

    public static byte[] CreateCredentialPasswordLine(byte[] secret)
    {
        if (secret.Length is 0 or > MaximumPasswordBytes ||
            secret.Any(value => value < 0x21 || value > 0x7E))
        {
            throw new ArgumentException("The protected credential is not a supported printable password.", nameof(secret));
        }

        byte[] payload = new byte[secret.Length + 1];
        Buffer.BlockCopy(secret, 0, payload, 0, secret.Length);
        payload[^1] = (byte)'\r';
        return payload;
    }

    public static bool ShouldAllowServerLineToRecorders(bool secretOutputRedactionRequired, string? plainText)
    {
        if (!secretOutputRedactionRequired)
            return true;

        return !string.IsNullOrWhiteSpace(plainText) &&
               plainText.TrimStart().StartsWith("Command [TL=", StringComparison.OrdinalIgnoreCase);
    }

    public static string ClassifyProtectedSetupPrompt(string? promptText)
    {
        if (string.IsNullOrWhiteSpace(promptText))
            return "unknown";

        string normalized = promptText.Trim();
        if (normalized.Contains("planet", StringComparison.OrdinalIgnoreCase))
            return "planet";

        bool mentionsShip = normalized.Contains("ship", StringComparison.OrdinalIgnoreCase);
        bool mentionsName = normalized.Contains("name", StringComparison.OrdinalIgnoreCase);
        if (mentionsShip && mentionsName)
            return "ship-name";

        if (normalized.Contains("trader", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("merchant", StringComparison.OrdinalIgnoreCase) ||
            (mentionsName &&
             (normalized.Contains("enter", StringComparison.OrdinalIgnoreCase) ||
              normalized.Contains("what", StringComparison.OrdinalIgnoreCase) ||
              normalized.Contains("known as", StringComparison.OrdinalIgnoreCase))))
        {
            return "trader-alias";
        }

        return "unknown";
    }

    public static string? ClassifyProtectedSetupNotice(string? serverLine)
    {
        if (string.IsNullOrWhiteSpace(serverLine) || !PasswordWord.IsMatch(serverLine))
            return null;

        if (PasswordMismatch.IsMatch(serverLine))
            return "password-mismatch";
        if (PasswordRejected.IsMatch(serverLine))
            return "password-rejected";

        return null;
    }

    private static bool IsNewCharacterStartPrompt(string? text)
        => !string.IsNullOrWhiteSpace(text) &&
           text.Contains("start a new character", StringComparison.OrdinalIgnoreCase);
}

internal static class MtcRpcApprovedPauseKeyPolicy
{
    private static readonly IPAddress ApprovedAddress = IPAddress.Parse("192.168.4.75");
    private const string ApprovedGameName = "Meridian Game D";
    private const string ApprovedPauseText = "Press Space or Enter to continue";

    public static bool IsAllowed(
        bool loopbackRequest,
        bool processOnly,
        bool connected,
        string gameName,
        string host,
        int port,
        string currentPrompt,
        string key)
    {
        if (!loopbackRequest ||
            !processOnly ||
            !connected ||
            !string.Equals(gameName, ApprovedGameName, StringComparison.OrdinalIgnoreCase) ||
            port != 2002 ||
            !IPAddress.TryParse(host, out IPAddress? address) ||
            !ApprovedAddress.Equals(address) ||
            string.IsNullOrWhiteSpace(currentPrompt) ||
            !currentPrompt.Contains("[Pause]", StringComparison.OrdinalIgnoreCase) ||
            !currentPrompt.Contains(ApprovedPauseText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(key, "Enter", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, "Space", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetInputByte(string key)
        => string.Equals(key, "Enter", StringComparison.OrdinalIgnoreCase)
            ? "\r"
            : string.Equals(key, "Space", StringComparison.OrdinalIgnoreCase)
                ? " "
                : throw new ArgumentOutOfRangeException(nameof(key), "Only Enter and Space are supported.");
}

internal static class WindowsCredentialSecretStore
{
    private const uint GenericCredentialType = 1;
    private const uint MaximumCredentialBlobBytes = 5120;

    public static byte[] ReadPrintableAsciiSecret(string target)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Credential Manager secret input is available only on Windows.");
        if (string.IsNullOrWhiteSpace(target) || target.Length > 256)
            throw new InvalidOperationException("The process-only credential target is not configured.");

        if (!CredReadW(target, GenericCredentialType, 0, out IntPtr credentialPointer))
            throw new InvalidOperationException("The configured Credential Manager entry is unavailable.");

        IntPtr blobPointer = IntPtr.Zero;
        uint blobSize = 0;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            blobPointer = credential.CredentialBlob;
            blobSize = credential.CredentialBlobSize;
            if (credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize is 0 or > MtcRpcSecretInputPolicy.MaximumPasswordBytes)
            {
                throw new InvalidOperationException("The configured Credential Manager entry has an invalid secret.");
            }

            byte[] secret = new byte[(int)credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, secret, 0, secret.Length);
            if (secret.Any(value => value < 0x21 || value > 0x7E))
            {
                CryptographicOperations.ZeroMemory(secret);
                throw new InvalidOperationException("The configured Credential Manager secret is not printable ASCII.");
            }

            return secret;
        }
        finally
        {
            try
            {
                if (blobPointer != IntPtr.Zero && blobSize <= MaximumCredentialBlobBytes)
                {
                    for (int index = 0; index < blobSize; index++)
                        Marshal.WriteByte(blobPointer, index, 0);
                }
            }
            finally
            {
                CredFree(credentialPointer);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);
}
