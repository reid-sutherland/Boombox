using Exiled.API.Enums;
using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.API.Features.Attributes;
using Exiled.API.Features.Items;
using Exiled.API.Features.Pickups;
using Exiled.API.Features.Spawn;
using Exiled.CustomItems.API.EventArgs;
using Exiled.CustomItems.API.Features;
using Exiled.Events.EventArgs.Item;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using MEC;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UserSettings.ServerSpecific;
using YamlDotNet.Serialization;

namespace Boombox;

[CustomItem(ItemType.Radio)]
public class Boombox : CustomItem
{
    [YamlIgnore]
    public override uint Id { get; set; } = 80085;

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

    // Internal master playlist for playlist-independent shuffling - contains each song's original playlist range and index
    [YamlIgnore]
    public List<Tuple<RadioRange, int, string>> AllSongs { get; set; } = new();

    private Config Config => MainPlugin.Configs;

    private HintManager HintManager => MainPlugin.Configs.HintManager;

    private bool EasterEggUsed { get; set; } = false;

    private CoroutineHandle EasterEggHandle { get; set; } = new();

    private HashSet<ushort> Serials { get; set; } = new();

    private Dictionary<ushort, AudioPlayer> AudioPlayers { get; set; } = new();

    private Dictionary<ushort, AudioClipPlayback> Playbacks { get; set; } = new();

    private Dictionary<ushort, string> DiedWithPlayerIds { get; set; } = new();

    // This is used to ensure the boombox can never be used like a regular radio
    private readonly Exiled.API.Structs.RadioRangeSettings boomboxSettings = new()
    {
        IdleUsage = 1.0f,
        TalkingUsage = 1,
        MaxRange = 1,
    };

    private string Identifier(ushort serial) => $"{nameof(Boombox)}-{serial}";

    private string GetAudioPlayerName(ushort serial) => $"AP-{Identifier(serial)}";

    private AudioPlayer GetAudioPlayer(ushort serial) => AudioPlayers.TryGetValue(serial, out var audioPlayer) ? audioPlayer : null;

    private AudioClipPlayback GetPlayback(ushort serial) => Playbacks.TryGetValue(serial, out var playback) ? playback : null;

    private string GetDiedWithId(ushort serial) => DiedWithPlayerIds.TryGetValue(serial, out var id) ? id : "";

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
        Exiled.Events.Handlers.Player.DroppingItem += OnDroppingItem;
        Exiled.Events.Handlers.Player.DroppedItem += OnDroppedItem;
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
        Exiled.Events.Handlers.Player.DroppingItem -= OnDroppingItem;
        Exiled.Events.Handlers.Player.DroppedItem -= OnDroppedItem;
        Exiled.Events.Handlers.Player.Died -= OnDied;
        // Radio
        Exiled.Events.Handlers.Player.ChangingRadioPreset -= OnChangingRadioPreset;
        Exiled.Events.Handlers.Player.TogglingRadio -= OnTogglingRadio;
        Exiled.Events.Handlers.Player.UsingRadioBattery -= OnUsingRadioBattery;
        Exiled.Events.Handlers.Item.UsingRadioPickupBattery -= OnUsingRadioPickupBattery;

        base.UnsubscribeEvents();
    }

    protected void Initialize(ushort serial)
    {
        // First look for a pickup, then an item with matching serial to initialize
        GameObject audioAttacher = null;
        Pickup pickup = Pickup.Get(serial);
        Item item = Item.Get(serial);
        if (pickup is not null && pickup is RadioPickup boomboxPickup)
        {
            boomboxPickup.IsEnabled = false;
            boomboxPickup.BatteryLevel = 1.0f;
            boomboxPickup.Range = RadioRange.Short;

            audioAttacher = pickup.GameObject;
        }
        else if (item is not null && item is Radio boomboxItem)
        {
            boomboxItem.IsEnabled = false;
            boomboxItem.BatteryLevel = 100;
            boomboxItem.Range = RadioRange.Short;

            audioAttacher = item.Owner.GameObject;
            if (item.Owner == Server.Host)
            {
                Log.Warn($"Initialize: no item owner was found for {Identifier(serial)}: got ServerHost player");
            }
        }
        else
        {
            Log.Error($"Initialize: no pickup or item with matching serial was found for {Identifier(serial)}");
            return;
        }

        if (audioAttacher is not null) // NOTE: Only GetAudioPlayer() really cares if a gameobject was found, but still need to check 'if found'
        {
            // Initialize tracker objects
            //Serials.Add(serial);
            //AudioPlayers.Add(serial, null);
            //Playbacks.Add(serial, null);
            //DiedWithPlayerIds.Add(serial, "");
            Serials.Add(serial);
            AudioPlayers[serial] = null;
            Playbacks[serial] = null;
            DiedWithPlayerIds[serial] = "";

            // Create the audio player
            try
            {
                var audioPlayer = AudioHelper.GetAudioPlayer(
                    GetAudioPlayerName(serial),
                    parent: audioAttacher,
                    speakerVolume: SpeakerVolume,
                    speakerCount: SpeakerCount,
                    minDistance: MinDistance,
                    maxDistance: MaxDistance,
                    log: Config.AudioDebug
                );
                if (audioPlayer is not null)
                {
                    AudioPlayers[serial] = audioPlayer;
                    Log.Debug($"Created audio player for spawned {Identifier(serial)}");
                }
                else
                {
                    Log.Error($"Failed to create audio player for spawned {Identifier(serial)}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Tried to create audio player for spawned {Identifier(serial)}. Exception: {ex.Message}");
            }
        }
    }

    protected void OnRoundStarted()
    {
        // Send a broadcast to any player that doesn't have the SS setting set
        if (HintManager.ShowSSWarningHints)
        {
            int keybindSettingId = ServerSettings.ChangeSongKeybind.Base.SettingId;
            foreach (Player player in Player.List)
            {
                bool hasKeybindSetting = ServerSpecificSettingsSync.TryGetSettingOfUser(player.ReferenceHub, keybindSettingId, out SSKeybindSetting result);
                if (!hasKeybindSetting)
                {
                    // TODO: the checking is still inaccurate so don't spam logs, but for now just give everybody the reminder
                    //Log.Warn($"Player {player.Nickname} does not have the server-specific key bound for boombox!");
                    player.ShowHint("Make sure Boombox Key is bound to F in server-specific settings!!!", 5.0f);
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

        // "Initialize" the spawned pickups
        Serials.Clear();
        AudioPlayers.Clear();
        Playbacks.Clear();
        DiedWithPlayerIds.Clear();
        foreach (int ser in TrackedSerials)
        {
            ushort serial = (ushort)ser;
            Initialize(serial);
        }

        Log.Info($"Round started: spawned {Serials.Count} Boombox(es)");

        if (Config.EasterEggEnabled)
        {
            EasterEggUsed = false;
        }
    }

    protected void OnRoundEnded(RoundEndedEventArgs ev)
    {
        Serials.Clear();
        AudioPlayers.Clear();
        Playbacks.Clear();
        DiedWithPlayerIds.Clear();
    }

    protected void OnRestartingRound()
    {
        // TODO: Need to clear trackers if the pickup/item with tracker serial is ever removed?
        // TODO: If spawned on the ground after round start, need to initialize them
    }

    // Moves the audio player to the player
    protected override void OnAcquired(Player player, Item item, bool displayMessage)
    {
        base.OnAcquired(player, item, displayMessage);
        Log.Debug($"{player.Nickname} acquired a {Identifier(item.Serial)}");

        var audioPlayer = GetAudioPlayer(item.Serial);
        if (audioPlayer is null)
        {
            // TODO: If it's null, we should probably create an audio player right? repeat elsewhere
            Initialize(item.Serial);
            audioPlayer = GetAudioPlayer(item.Serial);
        }
        if (audioPlayer is not null)
        {
            var speaker = AudioHelper.AttachAudioPlayer(audioPlayer, player.GameObject, SpeakerVolume, SpeakerCount, MinDistance, MaxDistance, log: Config.AudioDebug);
            if (speaker is null)
            {
                Log.Error($"OnAcquired: Speaker is null");
            }
        }
    }

    // Prevents banned users from picking up the boombox
    protected void OnPickingUpItem(PickingUpItemEventArgs ev)
    {
        if (Check(ev.Pickup))
        {
            return;
        }
        if (Config.BannedPlayerIds.Contains(ev.Player.UserId))
        {
            ev.Player.ShowHint(Config.BannedMessage, 5.0f);
            ev.IsAllowed = false;
            return;
        }
    }

    // Just sets the boombox settings
    protected override void OnChanging(ChangingItemEventArgs ev)
    {
        base.OnChanging(ev);
        SetBoomboxSettings((Radio)ev.Item);
    }

    // Moves the audio player from the player to the dropped pickup
    protected void OnDroppedItem(DroppedItemEventArgs ev)
    {
        if (!Check(ev.Pickup))
        {
            return;
        }
        SetBoomboxSettings(radioPickup: (RadioPickup)ev.Pickup);

        // TODO: Consider using PickupSpawned instead here -- ????

        var audioPlayer = GetAudioPlayer(ev.Pickup.Serial);
        if (audioPlayer is not null)
        {
            Log.Debug($"{ev.Player.Nickname} dropped the {Identifier(ev.Pickup.Serial)}");
            AudioHelper.AttachAudioPlayer(audioPlayer, ev.Pickup.GameObject, SpeakerVolume, SpeakerCount, MinDistance, MaxDistance, log: Config.AudioDebug);
        }
    }

    protected override void OnOwnerDying(OwnerDyingEventArgs ev)
    {
        base.OnOwnerDying(ev);
        if (ev.Item is not null)
        {
            Log.Debug($"{ev.Player.Nickname} is dying with the {Identifier(ev.Item.Serial)}");
            DiedWithPlayerIds[ev.Item.Serial] = ev.Player.UserId;
        }
    }

    // Moves the audio player from the player to the dropped pickup if the dead player was dying with the boombox
    protected void OnDied(DiedEventArgs ev)
    {
        int toRemove = -1;
        foreach (var it in DiedWithPlayerIds)
        {
            if (it.Value == ev.Player.UserId)
            {
                ushort serial = it.Key;
                toRemove = serial;

                Log.Debug($"{ev.Player.Nickname} died with the {Identifier(serial)}");
                Pickup boomboxPickup = Pickup.Get(serial);
                if (boomboxPickup is not null)
                {
                    Log.Debug($"-- pickup pos: {boomboxPickup.Position}");
                    var audioPlayer = GetAudioPlayer(serial);
                    if (audioPlayer is not null)
                    {
                        AudioHelper.AttachAudioPlayer(audioPlayer, boomboxPickup.GameObject, SpeakerVolume, SpeakerCount, MinDistance, MaxDistance, log: Config.AudioDebug);
                    }
                    else
                    {
                        Log.Error($"-- dropped-pickup {Identifier(serial)} did not have an audio player");
                    }
                }
                else
                {
                    Log.Error($"-- a Pickup was not found with serial: {serial}");
                }
                break;
            }
        }
        if (toRemove >= 0)
        {
            DiedWithPlayerIds.Remove((ushort)toRemove);
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

            // don't show conflicting hints if the playlist is empty or when changing song here
            ChangeSong(ev.Player, ev.Radio.Serial, ev.NewValue, QueueType.Current, showHint: false);     // disable change-song hint so it doesn't conflict with the change-playlist hint above
            HintManager.ShowChangePlaylist(ev.Player, Playlists[ev.NewValue]);
        }
    }

    // Pauses/unpauses the boombox playback
    protected void OnTogglingRadio(TogglingRadioEventArgs ev)
    {
        if (!Check(ev.Radio))
        {
            return;
        }
        Log.Debug($"{ev.Player.Nickname} switched the {Identifier(ev.Radio.Serial)}: {(ev.NewState ? "ON" : "off")}");

        var audioPlayer = GetAudioPlayer(ev.Radio.Serial);
        if (audioPlayer is not null)
        {
            var currentPlayback = GetPlayback(ev.Radio.Serial);
            if (currentPlayback is null)
            {
                ChangeSong(ev.Player, ev.Radio.Serial, ev.Radio.Range, QueueType.Current);
            }
            else if (ev.NewState)
            {
                // TODO: not a big deal but un-pausing doesn't show anything - calling ChangeSongHint here may show the wrong info if current playback is from shuffle
                currentPlayback.IsPaused = false;
            }
            else
            {
                currentPlayback.IsPaused = true;
            }
        }
    }

    // Not an EXILED handler, called directly when a player holding the boombox presses the SS key
    public void OnBoomboxKeyPressed(Player player, Item currentItem, bool shuffle = false)
    {
        if (!Check(currentItem))
        {
            return;
        }
        Radio boombox = (Radio)currentItem;
        if (boombox is null)
        {
            return;
        }

        if (boombox.IsEnabled)
        {
            if (shuffle)
            {
                ShuffleSong(player, currentItem.Serial, boombox.Range);
            }
            else
            {
                ChangeSong(player, currentItem.Serial, boombox.Range, QueueType.Next);
            }
        }
        else
        {
            Log.Debug($"Can't interact: Boombox is off");
        }
    }

    public void ChangeSong(Player player, ushort itemSerial, RadioRange range, QueueType queueType, bool showHint = true)
    {
        Playlist playlist = Playlists[range];
        if (playlist.Length == 0)
        {
            Log.Debug($"No songs in the playlist for range: {range}");
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
        PlaySong(Playlists[range].CurrentSong, itemSerial, player, showHint);
        HintManager.ShowChangeSong(player, Playlists[range]);
    }

    public void ShuffleSong(Player player, ushort itemSerial, RadioRange oldRange)
    {
        if (AllSongs.Count == 0)
        {
            Log.Debug($"Can't shuffle: No songs in any playlists");
            return;
        }

        Tuple<RadioRange, int, string> randomSong = AllSongs.GetRandomValue();
        RadioRange newRange = randomSong.Item1;
        int newIndex = randomSong.Item2;
        string newSong = randomSong.Item3;
        Log.Debug($"Shuffled song to '{newSong}' (range={newRange} index={newIndex})");

        // Set the radio and song index to the new range and playlist position
        Playlists[newRange].SongIndex = newIndex;
        if (newRange != oldRange)
        {
            Radio radio = (Radio)Item.Get(itemSerial);
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

        PlaySong(Playlists[newRange].CurrentSong, itemSerial, player);
        HintManager.ShowShuffleSong(player, Playlists[newRange]);
    }

    private void PlaySong(string song, ushort itemSerial, Player player = null, bool shuffle = false, bool showHint = true, bool addAll = false)
    {
        var audioPlayer = GetAudioPlayer(itemSerial);
        if (audioPlayer is null)
        {
            Log.Error($"Can't play song '{song}': {Identifier(itemSerial)} audio player is null");
            return;
        }

        // Stop current song
        var playback = GetPlayback(itemSerial);
        if (playback is not null)
        {
            audioPlayer.RemoveClipByName(playback.Clip);
        }

        playback = audioPlayer.AddClip(song);
        Playbacks[itemSerial] = playback;
        Log.Debug($"Added clip to {Identifier(itemSerial)} audio player: {playback.Clip}");
        if (player is not null)
        {
            // TODO: think this might need a check for position 0 in the playback
            // Easter egg
            if (Config.EasterEggEnabled)
            {
                if (song == Config.EasterEggSong && player.UserId == Config.EasterEggPlayerId)
                {
                    ActivateEasterEgg();
                }
                else
                {
                    Timing.KillCoroutines(EasterEggHandle);
                }
            }
        }
    }

    private void ActivateEasterEgg()
    {
        if (EasterEggUsed)
        {
            Log.Debug($"Easter egg has already been used this round");
            return;
        }

        // TODO: It seems that either 14.1 or LabAPI 1.1 broke the global speaker, needs a fix
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
        //    string clip = songList[(i + startIndex) % songList.Count];
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
            //Log.Debug($"** setting boombox settings on Radio: {radio.Serial}");
            radio.SetRangeSettings(RadioRange.Short, boomboxSettings);
            radio.SetRangeSettings(RadioRange.Medium, boomboxSettings);
            radio.SetRangeSettings(RadioRange.Long, boomboxSettings);
            radio.SetRangeSettings(RadioRange.Ultra, boomboxSettings);
            radio.BatteryLevel = 100;
        }
        else if (radioPickup is not null)
        {
            //Log.Debug($"** setting boombox settings on RadioPickup: {radioPickup.Serial}");
            radioPickup.BatteryLevel = 100;
        }
    }
}