using DedicatedServer.Chat;
using DedicatedServer.Config;
using DedicatedServer.Crops;
using DedicatedServer.MessageCommands;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Menus;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace DedicatedServer.HostAutomatorStages
{
    internal class StartFarmStage : HostAutomatorStage
    {
        private const string HostName = "CreeperHost";
        private readonly IMonitor monitor;
        private readonly ModConfig config;
        private readonly CropSaver cropSaver;
        private AutomatedHost automatedHost;
        private BuildCommandListener buildCommandListener;
        private DemolishCommandListener demolishCommandListener;
        private PauseCommandListener pauseCommandListener;
        private ServerCommandListener serverCommandListener;

        public StartFarmStage(IModHelper helper, IMonitor monitor, ModConfig config) : base(helper)
        {
            this.monitor = monitor;
            this.config = config;
            if (config.EnableCropSaver)
            {
                cropSaver = new CropSaver(helper, monitor, config);
                cropSaver.Enable();
            }
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnToTitle;
        }

        public override void Execute(object sender, UpdateTickedEventArgs e)
        {
            if (Game1.activeClickableMenu is not TitleMenu menu)
                return;

            // Attempt once, including on failure; don't flood the log every tick.
            Disable();
            try
            {
                ValidateHostConfig();
                // 1.6.15 added the search filter argument to save discovery.
                MethodInfo findSaves = typeof(LoadGameMenu).GetMethod("FindSaveGames",
                    BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(string) }, null)
                    ?? throw new MissingMethodException(typeof(LoadGameMenu).FullName, "FindSaveGames(string)");
                var farmers = (List<Farmer>)findSaves.Invoke(null, new object[] { null });
                Farmer hostedFarmer = farmers.FirstOrDefault(farmer => farmer != null
                    && farmer.slotCanHost && farmer.farmName.Value == config.FarmName);

                if (hostedFarmer != null)
                {
                    monitor.Log($"Hosting existing farm '{config.FarmName}' ({hostedFarmer.slotName}).", LogLevel.Info);
                    Game1.multiplayerMode = 2;
                    SaveGame.Load(hostedFarmer.slotName);
                    Game1.exitActiveMenu();
                }
                else
                {
                    ConfigureNewFarm();
                    monitor.Log($"Creating and hosting farm '{config.FarmName}'.", LogLevel.Info);
                    menu.createdNewCharacter(skipIntro: true);
                }
            }
            catch (Exception ex)
            {
                monitor.Log($"Automatic hosting stopped: {ex.GetBaseException().Message}\n{ex}", LogLevel.Error);
            }
        }

        private static string Option(string value) => value?.Trim().ToLowerInvariant();

        private void ValidateHostConfig()
        {
            if (string.IsNullOrWhiteSpace(config.FarmName))
                throw new ArgumentException("FarmName must not be empty.");
            if (Option(config.MushroomsOrBats) is not ("mushrooms" or "bats"))
                throw new ArgumentException("MushroomsOrBats must be mushrooms or bats.");
            if (config.AcceptPet && string.IsNullOrWhiteSpace(config.PetName))
                throw new ArgumentException("PetName must not be empty when AcceptPet is enabled.");
            if (Option(config.MoveBuildPermission) is not ("off" or "owned" or "on"))
                throw new ArgumentException("MoveBuildPermission must be off, owned, or on.");
        }

        private void ConfigureNewFarm()
        {
            int maxCabins = Game1.Multiplayer.playerLimit - 1;
            if (config.StartingCabins < 0 || config.StartingCabins > maxCabins)
                throw new ArgumentException($"StartingCabins must be between 0 and {maxCabins}.");
            if (Option(config.CabinLayout) is not ("nearby" or "separate"))
                throw new ArgumentException("CabinLayout must be nearby or separate.");
            if (Option(config.MoneyStyle) is not ("shared" or "separate"))
                throw new ArgumentException("MoneyStyle must be shared or separate.");
            if (Option(config.CommunityCenterBundles) is not ("normal" or "remixed"))
                throw new ArgumentException("CommunityCenterBundles must be normal or remixed.");
            if (Option(config.MineRewards) is not ("normal" or "remixed"))
                throw new ArgumentException("MineRewards must be normal or remixed.");

            float profitMargin = Option(config.ProfitMargin) switch
            {
                "normal" => 1f,
                "75%" => 0.75f,
                "50%" => 0.5f,
                "25%" => 0.25f,
                _ => throw new ArgumentException("ProfitMargin must be normal, 75%, 50%, or 25%.")
            };
            string petType = Option(config.PetSpecies) switch
            {
                "dog" => "Dog",
                "cat" => "Cat",
                null when !config.AcceptPet => "Dog",
                _ => throw new ArgumentException("PetSpecies must be dog or cat.")
            };
            string petBreed = (config.PetBreed ?? 0).ToString(CultureInfo.InvariantCulture);
            if ((config.AcceptPet && !config.PetBreed.HasValue)
                || !Pet.TryGetData(petType, out var petData)
                || !petData.Breeds.Any(breed => breed.Id == petBreed && breed.CanBeChosenAtStart))
                throw new ArgumentException($"PetBreed '{petBreed}' is not a starting breed for {petType}.");

            string[] farmTypes = { "standard", "riverland", "forest", "hilltop", "wilderness", "fourcorners", "beach" };
            int farmType = Array.IndexOf(farmTypes, Option(config.FarmType));
            var additionalFarm = farmType >= 0 ? null : DataLoader.AdditionalFarms(Game1.content)
                .FirstOrDefault(farm => string.Equals(farm.Id, config.FarmType?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (farmType < 0 && additionalFarm == null)
                throw new ArgumentException($"Unknown FarmType '{config.FarmType}'. Use a built-in farm name (including meadowlands) or an AdditionalFarms ID.");

            Game1.resetPlayer();
            Game1.startingCabins = config.StartingCabins;
            Game1.cabinsSeparate = Option(config.CabinLayout) == "separate";
            Game1.player.difficultyModifier = profitMargin;
            Game1.player.team.useSeparateWallets.Value = Option(config.MoneyStyle) == "separate";
            Game1.player.farmName.Value = config.FarmName;
            Game1.player.whichPetType = petType;
            Game1.player.whichPetBreed = petBreed;
            Game1.whichFarm = farmType >= 0 ? farmType : 7;
            Game1.whichModFarm = additionalFarm;
            Game1.bundleType = Option(config.CommunityCenterBundles) == "normal" ? Game1.BundleType.Default : Game1.BundleType.Remixed;
            Game1.game1.SetNewGameOption("YearOneCompletable", config.GuaranteeYear1Completable);
            Game1.game1.SetNewGameOption("MineChests", Option(config.MineRewards) == "normal" ? Game1.MineChestType.Default : Game1.MineChestType.Remixed);
            Game1.spawnMonstersAtNight = config.SpawnMonstersOnFarmAtNight;
            Game1.game1.SetNewGameOption("SpawnMonstersAtNight", config.SpawnMonstersOnFarmAtNight);
            Game1.startingGameSeed = config.RandomSeed;
            Game1.player.Name = HostName;
            Game1.player.displayName = Game1.player.Name;
            Game1.player.favoriteThing.Value = "Farms";
            Game1.player.isCustomized.Value = true;
            Game1.multiplayerMode = 2;
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e) => StartAutomation();

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            // Also covers the first day of a newly created farm.
            StartAutomation();
            ReadyCheckHelper.OnDayStarted(sender, e);
        }

        private void StartAutomation()
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer || automatedHost != null)
                return;

            // Apply the current host name to existing saves as well as new farms.
            Game1.player.Name = HostName;
            Game1.player.displayName = HostName;

            Game1.onScreenMenus.Remove(Game1.chatBox);
            var chatBox = new EventDrivenChatBox();
            Game1.chatBox = chatBox;
            Game1.onScreenMenus.Add(chatBox);
            // Cabins still determine how many farmers can join. Don't increase
            // HighestPlayerLimit, which controls allocation of cellar maps.
            Game1.netWorldState.Value.CurrentPlayerLimit = int.MaxValue;
            chatBox.textBoxEnter("/mbp " + Option(config.MoveBuildPermission));
            Game1.player.miningLevel.Value = 10;

            automatedHost = new AutomatedHost(helper, monitor, config, chatBox);
            automatedHost.Enable();
            buildCommandListener = new BuildCommandListener(chatBox);
            buildCommandListener.Enable();
            demolishCommandListener = new DemolishCommandListener(chatBox);
            demolishCommandListener.Enable();
            pauseCommandListener = new PauseCommandListener(chatBox);
            pauseCommandListener.Enable();
            serverCommandListener = new ServerCommandListener(helper, config, chatBox);
            serverCommandListener.Enable();
            monitor.Log($"Host automation ready for '{Game1.player.farmName.Value}'.", LogLevel.Info);
        }

        private void OnReturnToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            automatedHost?.Disable();
            automatedHost = null;
            buildCommandListener?.Disable();
            buildCommandListener = null;
            demolishCommandListener?.Disable();
            demolishCommandListener = null;
            pauseCommandListener?.Disable();
            pauseCommandListener = null;
            serverCommandListener?.Disable();
            serverCommandListener = null;
        }
    }
}
