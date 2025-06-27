using CommonUtils.Core;
using Exiled.API.Enums;
using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.API.Features.Attributes;
using Exiled.API.Features.Items;
using Exiled.API.Features.Pickups;
using Exiled.API.Features.Spawn;
using Exiled.CustomItems.API.Features;
using Exiled.Events.EventArgs.Item;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using MEC;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using UnityEngine;
using UserSettings.ServerSpecific;
using YamlDotNet.Serialization;

namespace Boombox;

[CustomItem(ItemType.Radio)]
public class Boombox : CustomItem
{
    [YamlIgnore]
    public override uint Id { get; set; } = 51;

    [YamlIgnore]
    public override string Name { get; set; } = "JBL Speaker";

    [YamlIgnore]
    public override string Description { get; set; } = "It looks like it's bass boosted!";

    [YamlIgnore]
    public override float Weight { get; set; } = 10.0f;

    [YamlIgnore]
    public override Vector3 Scale { get; set; } = new(3.0f, 3.0f, 3.0f);

    [YamlIgnore]
    public override ItemType Type { get; set; } = ItemType.Radio;

    // NOTE: For multiple boomboxes, needs to be a list (or a dict/set for no duplicates)
    [YamlIgnore]
    public int BoomboxSerial { get; set; } = -1;

    [YamlIgnore]
    public string AudioPlayerName => GetType().Name;

    // Internal master playlist for playlist-independent shuffling - contains each song's original playlist range and index
    [YamlIgnore]
    public List<Tuple<RadioRange, int, string>> AllSongs { get; set; } = new();

    private Config Config => MainPlugin.Configs;

    private AudioPlayer AudioPlayer { get; set; } = null;

    private AudioClipPlayback CurrentPlayback { get; set; } = null;

    private string DiedWithPlayerId { get; set; } = "";

    private bool EasterEggUsed { get; set; } = false;

    private CoroutineHandle EasterEggHandle { get; set; } = new();

    // This is used to ensure the boombox can never be used like a regular radio
    private readonly Exiled.API.Structs.RadioRangeSettings boomboxSettings = new()
    {
        IdleUsage = 1.0f,
        TalkingUsage = 1,
        MaxRange = 1,
    };

    [Description("Where the boombox can spawn. Currently a limit of 1 is required.")]
    public override SpawnProperties SpawnProperties { get; set; } = new()
    {
        Limit = 1,
        DynamicSpawnPoints = new()
        {
            new()
            {
                Chance = 100,
                Location = SpawnLocationType.Inside914,
            },
        },
    };

    [Description("Set the volume of each created speaker. Valid range is [0, 1]. Defaults to 1.")]
    public float SpeakerVolume { get; set; } = 1.0f;

    [Description("Set this to a higher value to increase the volume, duplicate speakers => louder.")]
    public int SpeakerCount { get; set; } = 1;

    [Description("The audio will be at maximum volume for players within this range of the boombox.")]
    public float MinDistance { get; set; } = 1.0f;

    [Description("The audio will gradually fade in volume past the MinDistance until it completely disappears at this range.")]
    public float MaxDistance { get; set; } = 30.0f;

    [Description("The playlists of songs for each RadioRange setting.")]
    public Playlists Playlists { get; set; } = new();

    protected override void SubscribeEvents()
    {
        // Rounds
        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Server.RoundEnded += OnRoundEnded;
        Exiled.Events.Handlers.Server.RestartingRound += OnRestartingRound;
        // Pickups/drops
        Exiled.Events.Handlers.Player.PickingUpItem += OnPickingUpItem;
        Exiled.Events.Handlers.Player.ChangingItem += OnChangingItem;
        Exiled.Events.Handlers.Player.DroppingItem += OnDroppingItem;
        Exiled.Events.Handlers.Player.DroppedItem += OnDroppedItem;
        Exiled.Events.Handlers.Player.Dying += OnDying;
        Exiled.Events.Handlers.Player.Died += OnDied;
        // Radio
        Exiled.Events.Handlers.Player.ChangingRadioPreset += OnChangingRadioPreset;
        Exiled.Events.Handlers.Player.TogglingRadio += OnTogglingRadio;
        Exiled.Events.Handlers.Player.UsingRadioBattery += OnUsingRadioBattery;
        Exiled.Events.Handlers.Item.UsingRadioPickupBattery += OnUsingRadioPickupBattery;

        base.SubscribeEvents();
    }

    protected override void UnsubscribeEvents()
    {
        // Rounds
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RoundEnded -= OnRoundEnded;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;
        // Pickups/drops
        Exiled.Events.Handlers.Player.PickingUpItem -= OnPickingUpItem;
        Exiled.Events.Handlers.Player.ChangingItem -= OnChangingItem;
        Exiled.Events.Handlers.Player.DroppingItem -= OnDroppingItem;
        Exiled.Events.Handlers.Player.DroppedItem -= OnDroppedItem;
        Exiled.Events.Handlers.Player.Dying -= OnDying;
        Exiled.Events.Handlers.Player.Died -= OnDied;
        // Radio
        Exiled.Events.Handlers.Player.ChangingRadioPreset -= OnChangingRadioPreset;
        Exiled.Events.Handlers.Player.TogglingRadio -= OnTogglingRadio;
        Exiled.Events.Handlers.Player.UsingRadioBattery -= OnUsingRadioBattery;
        Exiled.Events.Handlers.Item.UsingRadioPickupBattery -= OnUsingRadioPickupBattery;

        base.UnsubscribeEvents();
    }

    protected void OnRoundStarted()
    {
        // TODO: the checking is not working yet, but for now just give everybody the reminder
        // Send a broadcast to any player that doesn't have the SS setting set
        if (Config.ShowHints)
        {
            int keybindSettingId = MainPlugin.Singleton.ChangeSongKeybind.SettingId;
            foreach (Player player in Player.List)
            {
                bool hasKeybindSetting = ServerSpecificSettingsSync.TryGetSettingOfUser(player.ReferenceHub, keybindSettingId, out SSKeybindSetting result);
                if (!hasKeybindSetting)
                {
                    Log.Warn($"Player {player.Nickname} does not have the server-specific key bound for boombox!");
                    player.ShowHint("Make sure Boombox Key is bound to F in server-specific settings!!!", 10.0f);
                }
            }
        }

        // Populate a list of all songs for shuffling
        AllSongs = new();
        foreach (var item in Playlists)
        {
            Playlist playlist = item.Value;
            for (int index = 0; index < playlist.Length; index++)
            {
                AllSongs.Add(new(item.Key, index, playlist.Songs[index]));
            }
        }

        BoomboxSerial = -1;
        if (TrackedSerials.Count <= 0)
        {
            throw new Exception($"Boombox did not spawn");
        }
        else if (TrackedSerials.Count > 1)
        {
            // This shouldn't happen but if it does, just destroy all but one
            string serials = string.Join(", ", TrackedSerials.Select(ser => ser.ToString()));
            Log.Error($"Found multiple spawned boomboxes: {serials}");
            try
            {
                while (TrackedSerials.Count > 1)
                {
                    int deleteser = TrackedSerials.Last();
                    Log.Warn($"Destroying bb: {deleteser}");
                    RadioPickup deletebb = (RadioPickup)Pickup.Get((ushort)deleteser);
                    if (deletebb is not null)
                    {
                        deletebb.Destroy();
                        TrackedSerials.Remove(TrackedSerials.Last());
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Destory bb ex: {ex.Message}");
            }
        }
        BoomboxSerial = TrackedSerials.First();
        Log.Info($"Round started: spawned Boombox with serial: {BoomboxSerial}");

        // "Initialize" the boombox pickup
        Pickup pickup = Pickup.Get((ushort)BoomboxSerial);
        if (pickup is not null)
        {
            RadioPickup boombox = (RadioPickup)pickup;
            if (boombox is not null)
            {
                boombox.IsEnabled = false;
                boombox.BatteryLevel = 1.0f;
                boombox.Range = RadioRange.Short;
            }

            // Create the audio player
            try
            {
                AudioPlayer = AudioHelper.GetAudioPlayer(AudioPlayerName);
                if (AudioPlayer is null)
                {
                    Log.Error($"Tried to create audio player for new boombox spawn, but it failed");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Tried to get audio player. Exception: {ex.Message}");
            }
        }
        else
        {
            Log.Error($"Tried to retrieve Pickup with serial={BoomboxSerial}, but it could not cast to an Pickup");
        }
        if (Config.EasterEggEnabled)
        {
            EasterEggUsed = false;
        }
    }

    protected void OnRoundEnded(RoundEndedEventArgs ev)
    {
        DiedWithPlayerId = "";
    }

    protected void OnRestartingRound()
    {
        // TODO: Test this? this should only be necessary if the "Restart" button is clicked, normally it does not seem to be necessary
        Log.Debug($"OnRestartingRound...");
        Pickup boomboxPickup = Pickup.Get((ushort)BoomboxSerial);
        if (boomboxPickup is not null)
        {
            Log.Debug($"-- found pickup: serial={BoomboxSerial}");
            boomboxPickup.Destroy();
            Log.Debug($"-- destroyed Boombox pickup");
        }
        Item boomboxItem = Item.Get((ushort)BoomboxSerial);
        if (boomboxItem is not null)
        {
            Log.Debug($"-- found item: serial={BoomboxSerial}");
            boomboxItem.Destroy();
            Log.Debug($"-- destroyed Boombox item");
        }

        DiedWithPlayerId = "";
    }

    // Moves the audio player from the pickup to the player
    protected void OnPickingUpItem(PickingUpItemEventArgs ev)
    {
        if (!Check(ev.Pickup))
        {
            return;
        }
        if (Config.BannedPlayerIds.Contains(ev.Player.UserId))
        {
            ev.Player.ShowHint(Config.BannedMessage, 5.0f);
            ev.IsAllowed = false;
            return;
        }

        // TODO: Consider replacing this with overriding CustomItem.OnAcquired - be sure to call base

        Log.Debug($"{ev.Player.Nickname} is picking up the boombox: serial={ev.Pickup.Serial}");
        if (AudioPlayer is not null)
        {
            AudioHelper.AttachAudioPlayer(AudioPlayer, ev.Player.GameObject, SpeakerVolume, SpeakerCount, MinDistance, MaxDistance);
        }
    }

    // Just sets the boombox settings
    protected void OnChangingItem(ChangingItemEventArgs ev)
    {
        if (!Check(ev.Item))
        {
            return;
        }
        SetBoomboxSettings((Radio)ev.Item);
    }

    // Just sets the boombox settings
    protected override void OnDroppingItem(DroppingItemEventArgs ev)
    {
        if (!Check(ev.Item))
        {
            return;
        }
    }

    // Moves the audio player from the player to the dropped pickup
    protected void OnDroppedItem(DroppedItemEventArgs ev)
    {
        if (!Check(ev.Pickup))
        {
            return;
        }

        // TODO: Consider using PickupSpawned instead here

        SetBoomboxSettings(radioPickup: (RadioPickup)ev.Pickup);
        if (AudioPlayer is not null)
        {
            AudioHelper.AttachAudioPlayer(AudioPlayer, ev.Pickup.GameObject, SpeakerVolume, SpeakerCount, MinDistance, MaxDistance);
        }
    }

    // Checks if the player is dying with the boombox so it can be handled in OnDied
    protected void OnDying(DyingEventArgs ev)
    {
        Item boombox = ev.ItemsToDrop.Where(item => item.Serial == BoomboxSerial).FirstOrDefault();
        if (boombox is not null)
        {
            Log.Debug($"{ev.Player.Nickname} is dying with the boombox - serial={boombox.Serial}");
            DiedWithPlayerId = ev.Player.UserId;
        }
    }

    // Moves the audio player from the player to the dropped pickup if the dead player was dying with the boombox
    protected void OnDied(DiedEventArgs ev)
    {
        if (!string.IsNullOrEmpty(DiedWithPlayerId))
        {
            if (ev.Player.UserId == DiedWithPlayerId)
            {
                Log.Debug($"Player {ev.Player.Nickname} died with the boombox");

                Pickup boomboxPickup = Pickup.Get((ushort)BoomboxSerial);
                if (boomboxPickup is not null)
                {
                    Log.Debug($"-- pickup pos: {boomboxPickup.Position}");
                    AudioHelper.AttachAudioPlayer(AudioPlayer, boomboxPickup.GameObject, SpeakerVolume, SpeakerCount, MinDistance, MaxDistance);
                }
                else
                {
                    Log.Error($"!!! ... but a Pickup was not found with serial: {BoomboxSerial}");
                }

                Log.Debug($"Clearing DiedWithPlayerId: {DiedWithPlayerId}");
                DiedWithPlayerId = "";
            }
            else
            {
                Log.Warn($"Player ${ev.Player.Nickname} has died but it does not match the player from OnDying");
            }
        }
    }

    // Changes the boombox playlist
    protected void OnChangingRadioPreset(ChangingRadioPresetEventArgs ev)
    {
        if (!Check(ev.Radio))
        {
            return;
        }
        if (ev.Radio.IsEnabled)
        {
            Log.Debug($"{ev.Player.Nickname} changed the radio preset from {ev.OldValue} to {ev.NewValue}");

            ChangeSong(ev.Player, ev.NewValue, QueueType.Current, showHint: false);                 // disable change-song hint so it doesn't conflict with the change-playlist hint above
            if (Config.ShowHints && Playlists[ev.NewValue].Length > 0)
            {
                ev.Player.ShowHint($"Changed playlist to '{Playlists[ev.NewValue].Name}'", 1.0f);     // don't show a conflicting hint if the playlist is empty
            }
        }
    }

    // Pauses/unpauses the boombox playback
    protected void OnTogglingRadio(TogglingRadioEventArgs ev)
    {
        if (!Check(ev.Radio))
        {
            return;
        }
        Log.Debug($"{ev.Player.Nickname} turned the boombox {(ev.NewState ? "ON" : "off")}");

        if (AudioPlayer is not null)
        {
            if (CurrentPlayback is not null)
            {
                if (ev.NewState)
                {
                    CurrentPlayback.IsPaused = false;
                }
                else
                {
                    CurrentPlayback.IsPaused = true;
                }
            }
            else if (ev.NewState)
            {
                ChangeSong(ev.Player, ev.Radio.Range, QueueType.Current);
            }
        }
    }

    // Disable battery drain on Boombox
    protected void OnUsingRadioBattery(UsingRadioBatteryEventArgs ev)
    {
        if (!Check(ev.Radio))
        {
            return;
        }
        ev.IsAllowed = false;
    }

    // Same as above
    protected void OnUsingRadioPickupBattery(UsingRadioPickupBatteryEventArgs ev)
    {
        if (!Check(ev.RadioPickup))
        {
            return;
        }
        ev.IsAllowed = false;
    }

    // Not an EXILED handler, called directly when a player holding the boombox presses the SS key
    public void OnBoomboxKeyPressed(Player player, Item currentItem, bool shuffle = false)
    {
        if (!Check(currentItem))
        {
            return;
        }
        Radio boombox = (Radio)currentItem;
        if (boombox is not null)
        {
            if (boombox.IsEnabled)
            {
                if (shuffle)
                {
                    ShuffleSong(player, boombox.Range);
                }
                else
                {
                    ChangeSong(player, boombox.Range, QueueType.Next);
                }
            }
            else
            {
                Log.Debug($"Can't interact: Boombox is off");
            }
        }
    }

    public void ChangeSong(Player player, RadioRange range, QueueType queueType, bool showHint = true, bool addAllSongs = true)
    {
        Playlist playlist = Playlists[range];
        if (playlist.Length == 0)
        {
            Log.Debug($"No songs in the playlist for range: {range}");
            if (Config.ShowHints)
            {
                player.ShowHint($"Playlist '{playlist.Name}' has no songs :(", 2.0f);
            }
            return;
        }

        // TODO: Try replacing these with circular buffers
        switch (queueType)
        {
            case QueueType.Next:
                Playlists[range].NextSong();
                break;
            case QueueType.Last:
                Playlists[range].PreviousSong();
                break;
            case QueueType.Current:
            default:
                break;
        }
        PlaySong(Playlists[range].CurrentSong, player, showHint);

        //if (addAllSongs)
        //{
        //    AddAllSongs(SongIndex);
        //}
    }

    public void ShuffleSong(Player player, RadioRange oldRange, bool addAllSongs = false)
    {
        Tuple<RadioRange, int, string> randomSong = AllSongs.GetRandomValue();
        RadioRange newRange = randomSong.Item1;
        int newIndex = randomSong.Item2;
        string newSong = randomSong.Item3;
        Log.Debug($"Shuffled song to '{newSong}' (range={newRange} index={newIndex})");

        // Set the radio and song index to the new range and playlist position
        Playlists[newRange].SongIndex = newIndex;
        if (newRange != oldRange)
        {
            Radio radio = (Radio)Item.Get((ushort)BoomboxSerial);
            if (radio is not null)
            {
                Log.Debug($"-- changing radio range to {newRange}");
                // TODO: Change radio preset to the new range
                radio.Range = newRange;
            }
            else
            {
                Log.Warn($"-- could not change radio range to {newRange}: boombox Radio is null");
            }
        }

        PlaySong(newSong, player);
    }

    private void PlaySong(string song, Player player = null, bool shuffle = false, bool showHint = true)
    {
        song = song.Replace(".ogg", "");
        if (AudioPlayer is null)
        {
            Log.Error($"Can't play song '{song}': AudioPlayer is null");
            return;
        }

        // Stop current song
        if (CurrentPlayback is not null)
        {
            AudioPlayer.RemoveClipByName(CurrentPlayback.Clip);
            CurrentPlayback = null;
        }

        CurrentPlayback = AudioPlayer.AddClip(song);
        Log.Debug($"Added clip to boombox audio player: {CurrentPlayback.Clip}");
        if (player is not null)
        {
            if (Config.ShowHints && showHint)
            {
                string action = shuffle ? "Shuffled" : "Changed";
                player.ShowHint($"{action} song to '{song}'", 0.5f);
            }

            // Easter egg
            if (Config.EasterEggEnabled)
            {
                if (song == Config.EasterEggSong && player.UserId == Config.EasterEggPlayerId)
                {
                    PlayWarhead();
                }
                else
                {
                    Timing.KillCoroutines(EasterEggHandle);
                }
            }
        }
    }

    private void PlayWarhead()
    {
        if (EasterEggUsed)
        {
            Log.Debug($"Easter egg has already been used this round");
            return;
        }

        Log.Debug($"EasterEggSong '{Config.EasterEggSong}' played - queuing shake");
        AudioPlayer audioPlayer = AudioPlayer.CreateOrGet($"GLOBAL", onIntialCreation: (p) =>
        {
            // sad volume :( multi-speaker hack seems to bug out in global
            Speaker speaker = p.AddSpeaker($"Global", isSpatial: false, maxDistance: 5000f);
        });

        // shake the world
        EasterEggHandle = Timing.CallDelayed(Config.EasterEggDelay, () =>
        {
            EasterEggUsed = true;
            Log.Debug($"SHAKE");
            Warhead.Shake();
        });
    }

    private void AddAllSongs(int startIndex)
    {
        // TODO: This should be a coroutine that plays the next song after the playback time has elapsed
        // But it also needs to check for pause / song change events

        //Log.Debug($"Adding all songs from start index: {startIndex}");
        //for (int i = 0; i < songList.Count; i++)
        //{
        //    string clip = songList[(i + startIndex) % songList.Count].Replace(".ogg", "");
        //    AudioPlayer.AddClip(clip);
        //    AudioPlayer.
        //    Log.Debug($"-- {i}: {clip}");
        //}
    }

    // The boombox's radio settings need to be set to ensure it can't be used like a regular radio.
    // But, when it transitions between item (in hand) and pickup (on the ground), the settings reset.
    // So, this method should be called when the item transitions or when the radio is equipped.
    private void SetBoomboxSettings(Radio radio = null, RadioPickup radioPickup = null)
    {
        if (radio is not null)
        {
            Log.Debug($"** setting boombox settings on Radio: {radio.Serial}");
            radio.SetRangeSettings(RadioRange.Short, boomboxSettings);
            radio.SetRangeSettings(RadioRange.Medium, boomboxSettings);
            radio.SetRangeSettings(RadioRange.Long, boomboxSettings);
            radio.SetRangeSettings(RadioRange.Ultra, boomboxSettings);
            radio.BatteryLevel = 100;
        }
        else if (radioPickup is not null)
        {
            Log.Debug($"** setting boombox settings on RadioPickup: {radioPickup.Serial}");
            radioPickup.BatteryLevel = 100;
        }
    }
}