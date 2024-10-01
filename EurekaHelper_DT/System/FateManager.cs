using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using EurekaHelper_DT.Windows;
using EurekaHelper_DT.XIV;

namespace EurekaHelper_DT.System
{
    public class FateManager : IDisposable
    {
        private readonly EurekaHelper_DT _plugin = null!;
        private List<IFate> lastFates = new();
        private IEurekaTracker EurekaTracker;

        public FateManager(EurekaHelper_DT plugin)
        {
            _plugin = plugin;
            DalamudApi.ClientState.TerritoryChanged += OnTerritoryChanged;

            if (Utils.IsPlayerInEurekaZone(DalamudApi.ClientState.TerritoryType))
            {
                EurekaTracker = Utils.GetEurekaTracker(DalamudApi.ClientState.TerritoryType);
                DalamudApi.Framework.Update += OnUpdate;
            }
        }

        private void OnTerritoryChanged(ushort territoryId)
        {
            if (Utils.IsPlayerInEurekaZone(territoryId))
            {
                if (EurekaHelper_DT.Config.AutoCreateTracker)
                    if (!PluginWindow.GetConnection().IsConnected())
                        _ = Task.Run(async() => await _plugin.PluginWindow.CreateTracker(Utils.GetIndexOfZone(territoryId), true));

                EurekaTracker = Utils.GetEurekaTracker(territoryId);
                DalamudApi.Framework.Update += OnUpdate;
            }
            else
            {
                DalamudApi.Framework.Update -= OnUpdate;
            }
        }

        private void OnUpdate(IFramework framework)
        {
            if (EurekaHelper_DT.Config.DisplayFateProgress)
            {
                var instanceFates = DalamudApi.FateTable.Where(x => !Utils.IsBunnyFate(x.FateId)).ToList();
                foreach (var fate in instanceFates)
                {
                    EurekaFate eurekaFate = EurekaTracker.GetFates().SingleOrDefault(i => fate.FateId == i.FateId);
                    if (eurekaFate is null || eurekaFate.FateProgress == fate.Progress)
                        continue;

                    if (fate.Progress % 25 == 0)
                    {
                        eurekaFate.FateProgress = fate.Progress;
                        var sb = new SeStringBuilder()
                            .AddText($"{eurekaFate.BossName}: ")
                            .Append(Utils.MapLink(eurekaFate.TerritoryId, eurekaFate.MapId, eurekaFate.FatePosition))
                            .AddText(" is at ")
                            .AddUiForeground(58)
                            .AddText($"{eurekaFate.FateProgress}%")
                            .AddUiForegroundOff();

                        EurekaHelper_DT.PrintMessage(sb.BuiltString);
                    }
                }
            }

            if (DalamudApi.FateTable.SequenceEqual(lastFates))
                return;

            var currFates = DalamudApi.FateTable.Except(lastFates).ToList();
            var newFates = EurekaTracker.GetFates().Where(i => currFates.Select(i => i.FateId).Contains(i.FateId)).ToList();

            foreach (var fate in newFates)
                DisplayFatePop(fate);

            lastFates = DalamudApi.FateTable.ToList();
        }

        private static void DisplayFatePop(EurekaFate fate)
        {
            var sb = new SeStringBuilder()
                .AddText($"{fate.BossName}: ")
                .Append(Utils.MapLink(fate.TerritoryId, fate.MapId, fate.FatePosition));

            if (!fate.IsBunnyFate)
            {
                if (EurekaHelper_DT.Config.DisplayToastPop)
                    DalamudApi.ToastGui.ShowQuest(sb.BuiltString);

                if (EurekaHelper_DT.Config.PlayPopSound)
                    SoundManager.PlaySoundEffect(EurekaHelper_DT.Config.NMSoundEffect);

                if (EurekaHelper_DT.Config.DisplayFatePop)
                {
                    DalamudApi.PluginInterface.RemoveChatLinkHandler(fate.FateId);
                    if (EurekaHelper_DT.Config.PayloadOptions != PayloadOptions.Nothing)
                    {
                        DalamudLinkPayload payload = DalamudApi.PluginInterface.AddChatLinkHandler(fate.FateId, (i, m) =>
                        {
                            Utils.SetFlagMarker(fate, randomizeCoords: EurekaHelper_DT.Config.RandomizeMapCoords);

                            switch (EurekaHelper_DT.Config.PayloadOptions)
                            {
                                case PayloadOptions.CopyToClipboard:
                                    Utils.CopyToClipboard(Utils.RandomFormattedText(fate));
                                    break;

                                default:
                                case PayloadOptions.ShoutToChat:
                                    Utils.SendMessage(Utils.RandomFormattedText(fate));
                                    break;
                            }
                        });

                        var text = EurekaHelper_DT.Config.PayloadOptions switch
                        {
                            PayloadOptions.ShoutToChat => "shout",
                            PayloadOptions.CopyToClipboard => "copy",
                            _ => "shout"
                        };

                        sb.AddText(" ");
                        sb.AddUiForeground(32);
                        sb.Add(payload);
                        sb.AddText($"[Click to {text}]");
                        sb.Add(RawPayload.LinkTerminator);
                        sb.AddUiForegroundOff();
                    }

                    EurekaHelper_DT.PrintMessage(sb.BuiltString);
                }

                if (EurekaHelper_DT.Config.AutoPopFate)
                {
                    if (PluginWindow.GetConnection().IsConnected() && PluginWindow.GetConnection().CanModify())
                    {
                        var trackerFate = PluginWindow.GetConnection().GetTracker().GetFates().Find(x => x.IncludeInTracker && x.FateId == fate.FateId);

                        if (trackerFate is null)
                            return;

                        if (!trackerFate.IsPopped() || (EurekaHelper_DT.Config.AutoPopFateWithinRange && trackerFate.IsRespawnTimeWithinRange(TimeSpan.FromMinutes(5))))
                        {
                            _ = Task.Run(async () =>
                            {
                                await PluginWindow.GetConnection().SetPopTime((ushort)fate.TrackerId, DateTimeOffset.Now.ToUnixTimeMilliseconds());
                            });
                        }
                    }
                }
            }
            else
            {
                if (EurekaHelper_DT.Config.DisplayBunnyFates)
                {
                    EurekaHelper_DT.PrintMessage(sb.BuiltString);
                    SoundManager.PlaySoundEffect(EurekaHelper_DT.Config.BunnySoundEffect);
                }
            }
        }

        public void Dispose()
        {
            DalamudApi.ClientState.TerritoryChanged -= OnTerritoryChanged;
            DalamudApi.Framework.Update -= OnUpdate;
        }
    }
}
