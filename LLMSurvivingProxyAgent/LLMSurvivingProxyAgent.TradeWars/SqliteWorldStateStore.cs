using System.Globalization;
using System.Text.Json;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.TradeWars;

public sealed class SqliteWorldStateStore(string connectionString) : IWorldStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS world_universe (
                singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
                sector_count INTEGER NOT NULL,
                stardock_sector INTEGER NULL,
                last_updated TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS sectors (
                sector_id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                exploration INTEGER NOT NULL,
                warp_data_known INTEGER NOT NULL,
                observed_at TEXT NULL,
                port_class INTEGER NULL,
                details_json TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS warps (
                from_sector INTEGER NOT NULL REFERENCES sectors(sector_id) ON DELETE CASCADE,
                to_sector INTEGER NOT NULL,
                PRIMARY KEY (from_sector, to_sector)
            );
            CREATE INDEX IF NOT EXISTS ix_warps_to_sector ON warps(to_sector);
            CREATE INDEX IF NOT EXISTS ix_sectors_port_class ON sectors(port_class);
            CREATE TABLE IF NOT EXISTS corporations (
                corporation_id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                members_json TEXT NOT NULL,
                observed_at TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveUniverseAsync(
        Universe universe,
        CancellationToken cancellationToken = default) =>
        await LoadUniverseAsync(universe, [], cancellationToken);

    public async Task LoadUniverseAsync(
        Universe universe,
        IEnumerable<Sector> sectorObservations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(sectorObservations);
        if (universe.SectorCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(universe), "Universe sector count must be positive.");
        }
        if (universe.StarDockSector is int dock &&
            (dock < 1 || dock > universe.SectorCount))
        {
            throw new ArgumentOutOfRangeException(nameof(universe), "StarDock must be within the universe.");
        }

        var sectors = sectorObservations.ToArray();
        if (sectors.Any(sector => sector is null))
        {
            throw new ArgumentException("Sector collection cannot contain null entries.", nameof(sectorObservations));
        }
        if (sectors.Select(sector => sector.Id).Distinct().Count() != sectors.Length)
        {
            throw new ArgumentException("A universe load cannot contain duplicate sector IDs.", nameof(sectorObservations));
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var saveUniverse = connection.CreateCommand();
        saveUniverse.Transaction = transaction;
        saveUniverse.CommandText = """
            INSERT INTO world_universe(singleton_id, sector_count, stardock_sector, last_updated)
            VALUES (1, $count, $stardock, $updated)
            ON CONFLICT(singleton_id) DO UPDATE SET
                sector_count = excluded.sector_count,
                stardock_sector = excluded.stardock_sector,
                last_updated = excluded.last_updated;
            """;
        saveUniverse.Parameters.AddWithValue("$count", universe.SectorCount);
        saveUniverse.Parameters.AddWithValue("$stardock", (object?)universe.StarDockSector ?? DBNull.Value);
        saveUniverse.Parameters.AddWithValue("$updated", ToDatabaseValue(universe.LastUpdated));
        await saveUniverse.ExecuteNonQueryAsync(cancellationToken);

        var removeOutOfRangeWarps = connection.CreateCommand();
        removeOutOfRangeWarps.Transaction = transaction;
        removeOutOfRangeWarps.CommandText = """
            DELETE FROM warps WHERE from_sector > $count OR to_sector > $count;
            DELETE FROM sectors WHERE sector_id > $count;
            """;
        removeOutOfRangeWarps.Parameters.AddWithValue("$count", universe.SectorCount);
        await removeOutOfRangeWarps.ExecuteNonQueryAsync(cancellationToken);

        var createSector = connection.CreateCommand();
        createSector.Transaction = transaction;
        createSector.CommandText = """
            INSERT OR IGNORE INTO sectors(
                sector_id, name, exploration, warp_data_known, observed_at, port_class, details_json
            ) VALUES ($id, '', $unknown, 0, NULL, NULL, $details);
            """;
        var sectorIdParameter = createSector.Parameters.Add("$id", SqliteType.Integer);
        createSector.Parameters.AddWithValue("$unknown", (int)ExplorationStatus.Unknown);
        createSector.Parameters.AddWithValue("$details", Serialize(SectorDetails.Empty));
        createSector.Prepare();

        for (var sectorId = 1; sectorId <= universe.SectorCount; sectorId++)
        {
            sectorIdParameter.Value = sectorId;
            await createSector.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var sector in sectors)
        {
            await UpsertSectorAsync(connection, transaction, sector, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Universe?> GetUniverseAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sector_count, stardock_sector, last_updated
            FROM world_universe WHERE singleton_id = 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Universe(
            reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.IsDBNull(2) ? null : ParseDate(reader.GetString(2)));
    }

    public async Task<Sector?> GetSectorAsync(
        int sectorId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await GetSectorAsync(connection, sectorId, cancellationToken);
    }

    public async Task<IReadOnlyList<Sector>> GetSectorsAsync(
        int offset = 0,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        if (limit is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Page size must be between 1 and 10,000.");
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sector_id FROM sectors
            ORDER BY sector_id LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        var sectorIds = new List<int>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                sectorIds.Add(reader.GetInt32(0));
            }
        }

        var sectors = new List<Sector>(sectorIds.Count);
        foreach (var sectorId in sectorIds)
        {
            var sector = await GetSectorAsync(connection, sectorId, cancellationToken);
            if (sector is not null)
            {
                sectors.Add(sector);
            }
        }
        return sectors;
    }

    public Task UpsertSectorAsync(Sector sector, CancellationToken cancellationToken = default) =>
        UpsertSectorsAsync([sector], cancellationToken);

    public async Task UpsertSectorsAsync(
        IEnumerable<Sector> sectors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sectors);
        var batch = sectors.ToArray();
        if (batch.Any(sector => sector is null))
        {
            throw new ArgumentException("Sector collection cannot contain null entries.", nameof(sectors));
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var sector in batch)
        {
            await UpsertSectorAsync(connection, transaction, sector, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> GetWarpGraphAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sectors.sector_id, warps.to_sector
            FROM sectors
            LEFT JOIN warps ON warps.from_sector = sectors.sector_id
            ORDER BY sectors.sector_id, warps.to_sector;
            """;

        var graph = new Dictionary<int, List<int>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var fromSector = reader.GetInt32(0);
            if (!graph.TryGetValue(fromSector, out var destinations))
            {
                destinations = [];
                graph.Add(fromSector, destinations);
            }
            if (!reader.IsDBNull(1))
            {
                destinations.Add(reader.GetInt32(1));
            }
        }

        return graph.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<int>)pair.Value);
    }

    public async Task<IReadOnlyList<int>> GetStarDockSectorIdsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT sector_id FROM sectors WHERE port_class = 9 ORDER BY sector_id;";

        var sectors = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sectors.Add(reader.GetInt32(0));
        }
        return sectors;
    }

    public async Task UpsertCorporationAsync(
        Corporation corporation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(corporation);
        if (corporation.Id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(corporation), "Corporation ID must be positive.");
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO corporations(corporation_id, name, members_json, observed_at)
            VALUES ($id, $name, $members, $observed)
            ON CONFLICT(corporation_id) DO UPDATE SET
                name = excluded.name,
                members_json = excluded.members_json,
                observed_at = excluded.observed_at;
            """;
        command.Parameters.AddWithValue("$id", corporation.Id);
        command.Parameters.AddWithValue("$name", corporation.Name);
        command.Parameters.AddWithValue("$members", Serialize(corporation.Members));
        command.Parameters.AddWithValue("$observed", ToDatabaseValue(corporation.ObservedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Corporation?> GetCorporationAsync(
        int corporationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT corporation_id, name, members_json, observed_at
            FROM corporations WHERE corporation_id = $id;
            """;
        command.Parameters.AddWithValue("$id", corporationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Corporation(
            reader.GetInt32(0),
            reader.GetString(1),
            Deserialize<List<string>>(reader.GetString(2)),
            reader.IsDBNull(3) ? null : ParseDate(reader.GetString(3)));
    }

    private static async Task<Sector?> GetSectorAsync(
        SqliteConnection connection,
        int sectorId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sector_id, name, exploration, warp_data_known, observed_at, details_json
            FROM sectors WHERE sector_id = $id;
            """;
        command.Parameters.AddWithValue("$id", sectorId);

        int id;
        string name;
        ExplorationStatus exploration;
        bool warpDataKnown;
        DateTimeOffset? observedAt;
        SectorDetails details;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            id = reader.GetInt32(0);
            name = reader.GetString(1);
            exploration = (ExplorationStatus)reader.GetInt32(2);
            warpDataKnown = reader.GetInt32(3) != 0;
            observedAt = reader.IsDBNull(4) ? null : ParseDate(reader.GetString(4));
            details = Deserialize<SectorDetails>(reader.GetString(5));
        }

        var warpCommand = connection.CreateCommand();
        warpCommand.CommandText = """
            SELECT to_sector FROM warps WHERE from_sector = $id ORDER BY to_sector;
            """;
        warpCommand.Parameters.AddWithValue("$id", id);

        var warps = new List<int>();
        await using (var reader = await warpCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                warps.Add(reader.GetInt32(0));
            }
        }

        return new Sector(
            id,
            name,
            exploration,
            warpDataKnown,
            warps,
            details.Port,
            details.Planets,
            details.Traders,
            details.Ships,
            observedAt)
        {
            WarpObservedAt = details.WarpObservedAt
        };
    }

    private static async Task UpsertSectorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Sector sector,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sector);
        if (sector.Id < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sector), "Sector ID must be positive.");
        }
        if (!sector.WarpDataKnown && sector.Warps.Count != 0)
        {
            throw new ArgumentException("Warp destinations require WarpDataKnown to be true.", nameof(sector));
        }
        if (sector.Warps.Any(warp => warp < 1))
        {
            throw new ArgumentException("Warp destinations must be positive sector IDs.", nameof(sector));
        }
        if (sector.Port is { } port && port.SectorId != sector.Id)
        {
            throw new ArgumentException("Port sector ID must match its containing sector.", nameof(sector));
        }

        var validate = connection.CreateCommand();
        validate.Transaction = transaction;
        validate.CommandText = """
            SELECT sector_count FROM world_universe WHERE singleton_id = 1;
            """;
        var sectorCount = await validate.ExecuteScalarAsync(cancellationToken);
        if (sectorCount is null)
        {
            throw new InvalidOperationException("Save the universe metadata before storing sectors.");
        }
        if (sector.Id > Convert.ToInt32(sectorCount, CultureInfo.InvariantCulture) ||
            sector.Warps.Any(warp => warp > Convert.ToInt32(sectorCount, CultureInfo.InvariantCulture)))
        {
            throw new ArgumentOutOfRangeException(nameof(sector), "Sector or warp destination exceeds universe bounds.");
        }

        var details = new SectorDetails(
            sector.Port,
            sector.Planets,
            sector.Traders,
            sector.Ships,
            sector.WarpObservedAt);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sectors(
                sector_id, name, exploration, warp_data_known, observed_at, port_class, details_json
            ) VALUES ($id, $name, $exploration, $warpKnown, $observed, $portClass, $details)
            ON CONFLICT(sector_id) DO UPDATE SET
                name = excluded.name,
                exploration = MAX(sectors.exploration, excluded.exploration),
                warp_data_known = MAX(sectors.warp_data_known, excluded.warp_data_known),
                observed_at = COALESCE(excluded.observed_at, sectors.observed_at),
                port_class = excluded.port_class,
                details_json = excluded.details_json;
            """;
        command.Parameters.AddWithValue("$id", sector.Id);
        command.Parameters.AddWithValue("$name", sector.Name);
        command.Parameters.AddWithValue("$exploration", (int)sector.Exploration);
        command.Parameters.AddWithValue("$warpKnown", sector.WarpDataKnown ? 1 : 0);
        command.Parameters.AddWithValue("$observed", ToDatabaseValue(sector.ObservedAt));
        command.Parameters.AddWithValue("$portClass", (object?)sector.Port?.ClassIndex ?? DBNull.Value);
        command.Parameters.AddWithValue("$details", Serialize(details));
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (sector.WarpDataKnown)
        {
            var deleteWarps = connection.CreateCommand();
            deleteWarps.Transaction = transaction;
            deleteWarps.CommandText = "DELETE FROM warps WHERE from_sector = $id;";
            deleteWarps.Parameters.AddWithValue("$id", sector.Id);
            await deleteWarps.ExecuteNonQueryAsync(cancellationToken);

            foreach (var destination in sector.Warps.Distinct().Order())
            {
                var insertWarp = connection.CreateCommand();
                insertWarp.Transaction = transaction;
                insertWarp.CommandText = """
                    INSERT INTO warps(from_sector, to_sector) VALUES ($from, $to);
                    """;
                insertWarp.Parameters.AddWithValue("$from", sector.Id);
                insertWarp.Parameters.AddWithValue("$to", destination);
                await insertWarp.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static string? ToDatabaseValue(DateTimeOffset? dateTime) =>
        dateTime?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, JsonOptions)
        ?? throw new InvalidDataException($"Stored {typeof(T).Name} JSON is empty or invalid.");

    private sealed record SectorDetails(
        Port? Port,
        IReadOnlyList<Planet> Planets,
        IReadOnlyList<Trader> Traders,
        IReadOnlyList<Ship> Ships,
        DateTimeOffset? WarpObservedAt = null)
    {
        public static SectorDetails Empty { get; } = new(null, [], [], [], null);
    }
}
