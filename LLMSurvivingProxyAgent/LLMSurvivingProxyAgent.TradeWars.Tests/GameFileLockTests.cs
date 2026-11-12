using TWXProxy.Core;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class GameFileLockTests
{
    [Fact]
    public void IsLockedByAnotherProcess_ReflectsExclusiveLockAndDoesNotCreateLockFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mtc-lock-test-{Guid.NewGuid():N}");
        string configPath = Path.Combine(directory, "game.json");
        Directory.CreateDirectory(directory);

        try
        {
            Assert.False(GameFileLock.IsLockedByAnotherProcess(configPath));
            Assert.False(File.Exists(GameFileLock.GetLockFilePath(configPath)));

            using (GameFileLock.Acquire("test", configPath, Path.Combine(directory, "game.xdb")))
            {
                Assert.True(GameFileLock.IsLockedByAnotherProcess(configPath));
            }

            Assert.False(GameFileLock.IsLockedByAnotherProcess(configPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
