using System;
using System.Collections.Generic;
using OpenCoffee;

namespace OpenCoffee;

/// <summary>
/// A single point-in-time snapshot of all machine data.
/// Combines maintenance status, maintenance counters, and product counters.
/// </summary>
public class MachineSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool CoffeeReady { get; set; }
    public bool HasAlerts { get; set; }
    public List<int>? ActiveAlerts { get; set; }
    public MaintenanceStatusDto? Maintenance { get; set; }
    public MaintenanceCountersDto? MaintenanceCounts { get; set; }
    public ProductsDto? Products { get; set; }

    public class MaintenanceStatusDto
    {
        public int? CleaningPercent { get; set; }
        public int? FilterPercent { get; set; }
        public int? DecalcPercent { get; set; }
        public int? CappuRinsePercent { get; set; }
        public int? CoffeeRinsePercent { get; set; }
        public int? CappuCleanPercent { get; set; }

        public static MaintenanceStatusDto From(MaintenanceStatus s) => new()
        {
            CleaningPercent = s.CleaningPercent,
            FilterPercent = s.FilterPercent,
            DecalcPercent = s.DecalcPercent,
            CappuRinsePercent = s.CappuRinsePercent,
            CoffeeRinsePercent = s.CoffeeRinsePercent,
            CappuCleanPercent = s.CappuCleanPercent,
        };
    }

    public class MaintenanceCountersDto
    {
        public int? CleaningCount { get; set; }
        public int? FilterChangeCount { get; set; }
        public int? DecalcCount { get; set; }
        public int? CappuRinseCount { get; set; }
        public int? CoffeeRinseCount { get; set; }
        public int? CappuCleanCount { get; set; }

        public static MaintenanceCountersDto From(MaintenanceCounters c) => new()
        {
            CleaningCount = c.CleaningCount,
            FilterChangeCount = c.FilterChangeCount,
            DecalcCount = c.DecalcCount,
            CappuRinseCount = c.CappuRinseCount,
            CoffeeRinseCount = c.CoffeeRinseCount,
            CappuCleanCount = c.CappuCleanCount,
        };
    }

    public class ProductsDto
    {
        public int TotalProducts { get; set; }
        public Dictionary<string, int> Items { get; set; } = new();

        public static ProductsDto From(ProductCounters p) => new()
        {
            TotalProducts = p.TotalProducts,
            Items = p.NamedProducts,
        };
    }
}
