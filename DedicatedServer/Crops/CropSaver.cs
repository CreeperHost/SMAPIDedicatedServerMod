using DedicatedServer.Config;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace DedicatedServer.Crops
{
    /// <summary>Preserve already harvestable, player-planted crops for one harvest after their season ends.</summary>
    public class CropSaver
    {
        private const string HarmonyId = "objectmanagermanager.DedicatedServer.CropSaver";
        private static CropSaver instance;
        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private readonly Harmony harmony = new Harmony(HarmonyId);
        private readonly XmlSerializer serializer = new XmlSerializer(typeof(CropSaveData));
        private Utils.SerializableDictionary<CropLocation, CropData> cropDictionary = new();
        private Utils.SerializableDictionary<CropLocation, CropComparisonData> beginningOfDayCrops = new();
        private readonly Dictionary<Crop, HoeDirt> finalHarvestCrops = new();
        private bool enabled;
        private bool canWriteSaveData = true;

        // Keep the original XML names so farms from version 1.0.1 can retain their crop protection.
        public struct CropSaveData
        {
            public Utils.SerializableDictionary<CropLocation, CropData> cropDictionary { get; set; }
            public Utils.SerializableDictionary<CropLocation, CropComparisonData> beginningOfDayCrops { get; set; }
        }

        public struct CropLocation
        {
            public string LocationName { get; set; }
            public int TileX { get; set; }
            public int TileY { get; set; }
        }

        public struct CropGrowthStage
        {
            public int CurrentPhase { get; set; }
            public int DayOfCurrentPhase { get; set; }
            public bool FullyGrown { get; set; }
            public List<int> PhaseDays { get; set; }
            public int OriginalRegrowAfterHarvest { get; set; }
        }

        public struct CropComparisonData
        {
            public CropGrowthStage CropGrowthStage { get; set; }
            public int RowInSpriteSheet { get; set; }
            public bool Dead { get; set; }
            public bool ForageCrop { get; set; }
            // 1.6 uses string item IDs. XML written with the old integer property still deserializes.
            public string WhichForageCrop { get; set; }
            public string SeedId { get; set; }
        }

        public struct CropData
        {
            public bool MarkedForDeath { get; set; }
            public List<string> OriginalSeasonsToGrowIn { get; set; }
            public bool HasExistedInIncompatibleSeason { get; set; }
            public int OriginalRegrowAfterHarvest { get; set; }
            public bool HarvestableLastNight { get; set; }
            public string SeedId { get; set; }
        }

        public CropSaver(IModHelper helper, IMonitor monitor, ModConfig config)
        {
            this.helper = helper;
            this.monitor = monitor;
        }

        public void Enable()
        {
            if (enabled)
                return;

            instance = this;
            // Crop seasons and regrowth are shared asset data in 1.6. Never modify that data globally.
            harmony.Patch(AccessTools.Method(typeof(Crop), nameof(Crop.IsInSeason), new[] { typeof(GameLocation) }),
                postfix: new HarmonyMethod(typeof(CropSaver), nameof(IsInSeasonPostfix)));
            harmony.Patch(AccessTools.Method(typeof(Crop), nameof(Crop.harvest)),
                prefix: new HarmonyMethod(typeof(CropSaver), nameof(HarvestPrefix)),
                postfix: new HarmonyMethod(typeof(CropSaver), nameof(HarvestPostfix)));
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.DayEnding += OnDayEnding;
            helper.Events.GameLoop.Saved += OnSaved;
            helper.Events.GameLoop.SaveLoaded += OnLoaded;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            enabled = true;
        }

        public void Disable()
        {
            if (!enabled)
                return;

            helper.Events.GameLoop.DayStarted -= OnDayStarted;
            helper.Events.GameLoop.DayEnding -= OnDayEnding;
            helper.Events.GameLoop.Saved -= OnSaved;
            helper.Events.GameLoop.SaveLoaded -= OnLoaded;
            helper.Events.GameLoop.ReturnedToTitle -= OnReturnedToTitle;
            helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;
            harmony.UnpatchAll(HarmonyId);
            enabled = false;
            instance = null;
            Reset();
        }

        private void Reset()
        {
            cropDictionary.Clear();
            beginningOfDayCrops.Clear();
            finalHarvestCrops.Clear();
            canWriteSaveData = true;
        }

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e) => Reset();

        private void OnLoaded(object sender, SaveLoadedEventArgs e)
        {
            Reset();
            if (!Context.IsMainPlayer || Constants.CurrentSavePath == null)
                return;

            string path = Path.Combine(Constants.CurrentSavePath, "AdditionalCropData");
            if (!File.Exists(path))
                return;

            try
            {
                using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var data = (CropSaveData)serializer.Deserialize(reader);
                cropDictionary = data.cropDictionary ?? new();
                beginningOfDayCrops = data.beginningOfDayCrops ?? new();
                foreach (var key in cropDictionary.Keys.ToArray())
                {
                    if (cropDictionary[key].OriginalSeasonsToGrowIn == null)
                        cropDictionary.Remove(key);
                }
                // Also restore the watch list for mods which reload a save in the middle of a day.
                ForEachCrop((location, dirt, crop) =>
                {
                    if (TryGetProtectedCrop(crop, location, out var saved)
                        && saved.HasExistedInIncompatibleSeason && !crop.dead.Value)
                        finalHarvestCrops[crop] = dirt;
                });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is XmlException)
            {
                // Preserve the damaged file for recovery instead of silently replacing it with empty metadata.
                canWriteSaveData = false;
                monitor.Log($"Could not load crop protection data from {path}. The existing file will be preserved. {ex.Message}", LogLevel.Error);
            }
        }

        private void OnSaved(object sender, SavedEventArgs e)
        {
            if (!Context.IsMainPlayer || !canWriteSaveData || Constants.CurrentSavePath == null)
                return;

            string path = Path.Combine(Constants.CurrentSavePath, "AdditionalCropData");
            string temporaryPath = path + ".tmp";
            try
            {
                // Write only after the farm save succeeds. A failed serialization never removes the previous metadata.
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    serializer.Serialize(stream, new CropSaveData { cropDictionary = cropDictionary, beginningOfDayCrops = beginningOfDayCrops });
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, path + "_old");
                else
                    File.Move(temporaryPath, path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                monitor.Log($"Could not save crop protection data. The previous file has been preserved. {ex.Message}", LogLevel.Error);
            }
        }

        private static CropLocation GetLocation(GameLocation location, Crop crop) => new()
        {
            LocationName = location.NameOrUniqueName,
            TileX = (int)crop.tilePosition.X,
            TileY = (int)crop.tilePosition.Y
        };

        private static bool IsEligibleLocation(GameLocation location) => location != null
            && location.IsOutdoors && !location.SeedsIgnoreSeasonsHere() && location is not IslandLocation;

        private static string GetSeedId(Crop crop) => crop.isWildSeedCrop() ? crop.whichForageCrop.Value : crop.netSeedIndex.Value;

        private static bool IsHarvestable(Crop crop) => !crop.dead.Value && crop.phaseDays.Count > 0
            && crop.currentPhase.Value >= crop.phaseDays.Count - 1
            && (!crop.fullyGrown.Value || crop.dayOfCurrentPhase.Value <= 0);

        private static CropComparisonData Compare(Crop crop) => new()
        {
            SeedId = GetSeedId(crop),
            RowInSpriteSheet = crop.rowInSpriteSheet.Value,
            Dead = crop.dead.Value,
            ForageCrop = crop.forageCrop.Value,
            WhichForageCrop = crop.whichForageCrop.Value,
            CropGrowthStage = new CropGrowthStage
            {
                CurrentPhase = crop.currentPhase.Value,
                DayOfCurrentPhase = crop.dayOfCurrentPhase.Value,
                FullyGrown = crop.fullyGrown.Value,
                PhaseDays = crop.phaseDays.ToList(),
                OriginalRegrowAfterHarvest = crop.GetData()?.RegrowDays ?? -1
            }
        };

        private static bool SameCrop(CropComparisonData first, CropComparisonData second)
        {
            var a = first.CropGrowthStage;
            var b = second.CropGrowthStage;
            if (a.PhaseDays == null || b.PhaseDays == null)
                return false;
            bool bothMature = a.CurrentPhase >= a.PhaseDays.Count - 1 && b.CurrentPhase >= b.PhaseDays.Count - 1;
            bool aReady = !a.FullyGrown || a.DayOfCurrentPhase <= 0;
            bool bReady = !b.FullyGrown || b.DayOfCurrentPhase <= 0;
            bool harvestedToday = bothMature && aReady && !bReady;
            return (first.SeedId == null || first.SeedId == second.SeedId)
                && first.RowInSpriteSheet == second.RowInSpriteSheet
                && first.Dead == second.Dead && first.ForageCrop == second.ForageCrop
                && first.WhichForageCrop == second.WhichForageCrop
                && a.CurrentPhase == b.CurrentPhase
                && (a.DayOfCurrentPhase == b.DayOfCurrentPhase || harvestedToday);
        }

        private static void ForEachCrop(Action<GameLocation, HoeDirt, Crop> action)
        {
            Utility.ForEachLocation(location =>
            {
                if (IsEligibleLocation(location))
                {
                    foreach (var feature in location.terrainFeatures.Values)
                    {
                        if (feature is HoeDirt { crop: not null } dirt)
                            action(location, dirt, dirt.crop);
                    }
                }
                return true;
            });
        }

        private void OnDayEnding(object sender, DayEndingEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            RemoveHarvestedCrops();
            var found = new HashSet<CropLocation>();
            ForEachCrop((location, dirt, crop) =>
            {
                var key = GetLocation(location, crop);
                found.Add(key);
                var comparison = Compare(crop);
                if (!crop.dead.Value && (!beginningOfDayCrops.TryGetValue(key, out var original) || !SameCrop(original, comparison)))
                {
                    var data = crop.GetData();
                    if (data?.Seasons != null)
                    {
                        cropDictionary[key] = new CropData
                        {
                            SeedId = GetSeedId(crop),
                            OriginalSeasonsToGrowIn = data.Seasons.Select(season => season.ToString().ToLowerInvariant()).ToList(),
                            OriginalRegrowAfterHarvest = data.RegrowDays
                        };
                    }
                }
                if (cropDictionary.TryGetValue(key, out var saved))
                {
                    saved.HarvestableLastNight = IsHarvestable(crop);
                    cropDictionary[key] = saved;
                }
            });
            foreach (var key in cropDictionary.Keys.Where(key => !found.Contains(key)).ToArray())
                cropDictionary.Remove(key);
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            beginningOfDayCrops.Clear();
            finalHarvestCrops.Clear();
            ForEachCrop((location, dirt, crop) =>
            {
                var key = GetLocation(location, crop);
                if (cropDictionary.TryGetValue(key, out var saved))
                {
                    if (saved.SeedId != null && saved.SeedId != GetSeedId(crop))
                        cropDictionary.Remove(key);
                    else
                    {
                        saved.SeedId = GetSeedId(crop);
                        if (!saved.OriginalSeasonsToGrowIn.Contains(location.GetSeason().ToString().ToLowerInvariant()))
                            saved.HasExistedInIncompatibleSeason = true;
                        if (saved.HasExistedInIncompatibleSeason && !saved.HarvestableLastNight)
                            saved.MarkedForDeath = true;
                        if (saved.MarkedForDeath)
                            crop.Kill();
                        else if (saved.HasExistedInIncompatibleSeason && !crop.dead.Value)
                            finalHarvestCrops[crop] = dirt;
                        cropDictionary[key] = saved;
                    }
                }
                beginningOfDayCrops[key] = Compare(crop);
            });
        }

        private bool TryGetProtectedCrop(Crop crop, GameLocation location, out CropData data)
        {
            data = default;
            return enabled && Context.IsMainPlayer && IsEligibleLocation(location)
                && cropDictionary.TryGetValue(GetLocation(location, crop), out data)
                && (data.SeedId == null || data.SeedId == GetSeedId(crop)) && !data.MarkedForDeath;
        }

        private static void IsInSeasonPostfix(Crop __instance, GameLocation location, ref bool __result)
        {
            if (!__result && instance != null && instance.TryGetProtectedCrop(__instance, location, out var data)
                && data.HarvestableLastNight)
                __result = true;
        }

        private static void HarvestPrefix(Crop __instance, out bool __state)
        {
            __state = instance != null && instance.TryGetProtectedCrop(__instance, __instance.currentLocation, out var data)
                && data.HasExistedInIncompatibleSeason && IsHarvestable(__instance);
        }

        private static void HarvestPostfix(Crop __instance, bool __state, ref bool __result)
        {
            // A successful harvest of a regrowing crop changes this counter. A failed harvest (e.g. full
            // inventory) leaves it unchanged, so the crop must remain available in that case.
            if (__state && __instance.fullyGrown.Value && __instance.dayOfCurrentPhase.Value > 0)
                __result = true;
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (e.IsMultipleOf(15) && Context.IsWorldReady && Context.IsMainPlayer)
                RemoveHarvestedCrops();
        }

        private void RemoveHarvestedCrops()
        {
            // Farmhands can harvest on clients without this mod. Observe their networked regrowth state
            // on the host and remove the crop before it can provide another harvest.
            foreach (var pair in finalHarvestCrops.ToArray())
            {
                Crop crop = pair.Key;
                HoeDirt dirt = pair.Value;
                if (dirt.crop != crop || crop.dead.Value)
                    finalHarvestCrops.Remove(crop);
                else if (crop.fullyGrown.Value && crop.dayOfCurrentPhase.Value > 0)
                {
                    dirt.crop = null;
                    finalHarvestCrops.Remove(crop);
                }
            }
        }
    }
}

