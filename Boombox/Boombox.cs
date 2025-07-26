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
using Exiled.Events.EventArgs.Map;
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
    public override string Name { get; set; } = "Boombox";

    [YamlIgnore]
    public override string Description { get; set; } = "A radio that plays music";

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

    private static HashSet<ushort> Serials { get; set; } = new();

    private static Dictionary<ushort, AudioPlayer> AudioPlayers { get; set; } = new();

    private static Dictionary<ushort, AudioClipPlayback> Playbacks { get; set; } = new();

    // This is used to ensure the boombox can never be used like a regular radio
    private readonly Exiled.API.Structs.RadioRangeSettings boomboxSettings = new()
    {
        IdleUsage = 1.0f,
        TalkingUsage = 1,
        MaxRange = 1,
    };

    public static bool IsBoombox(ushort serial) => Serials.Contains(serial);

    public static string Identifier(ushort serial) => $"{nameof(Boombox)}({serial})";

    private string GetAudioPlayerName(ushort serial) => $"{Identifier(serial)}-AP";

    private AudioPlayer GetAudioPlayer(ushort serial) => AudioPlayers.TryGetValue(serial, out var audioPlayer) ? audioPlayer : null;

    private AudioClipPlayback GetPlayback(ushort serial) => Playbacks.TryGetValue(serial, out var playback) ? playback : null;

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
        // Map
        Exiled.Events.Handlers.Map.PickupAdded += OnPickupSpawned;
        // Player
        Exiled.Events.Handlers.Player.PickingUpItem += OnPickingUpItem;
        Exiled.Events.Handlers.Player.TogglingRadio += OnTogglingRadio;
        Exiled.Events.Handlers.Player.ChangingRadioPreset += OnChangingRadioPreset;
        Exiled.Events.Handlers.Player.UsingRadioBattery += OnUsingRadioBattery;
        Exiled.Events.Handlers.Item.UsingRadioPickupBattery += OnUsingRadioPickupBattery;
        LabApi.Events.Handlers.PlayerEvents.SendingVoiceMessage += OnPlayerSendingVoiceMessage;
        LabApi.Events.Handlers.PlayerEvents.ReceivingVoiceMessage += OnPlayerReceivingVoiceMessage;

        base.SubscribeEvents();
    }

    protected override void UnsubscribeEvents()
    {
        // Rounds
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RoundEnded -= OnRoundEnded;
        // Map
        Exiled.Events.Handlers.Map.PickupAdded -= OnPickupSpawned;
        // Player
        Exiled.Events.Handlers.Player.PickingUpItem -= OnPickingUpItem;
        Exiled.Events.Handlers.Player.TogglingRadio -= OnTogglingRadio;
        Exiled.Events.Handlers.Player.ChangingRadioPreset -= OnChangingRadioPreset;
        Exiled.Events.Handlers.Player.UsingRadioBattery -= OnUsingRadioBattery;
        Exiled.Events.Handlers.Item.UsingRadioPickupBattery -= OnUsingRadioPickupBattery;
        LabApi.Events.Handlers.PlayerEvents.SendingVoiceMessage -= OnPlayerSendingVoiceMessage;
        LabApi.Events.Handlers.PlayerEvents.ReceivingVoiceMessage -= OnPlayerReceivingVoiceMessage;

        base.UnsubscribeEvents();
    }

    protected void InitializeBoombox(ushort serial)
    {
        // First look for a pickup or an item with matching serial to initialize
        GameObject audioAttacher = null;
        Pickup pickup = Pickup.Get(serial);
        Item item = Item.Get(serial);
        if (pickup is not null && pickup is RadioPickup boomboxPickup)
        {
            boomboxPickup.IsEnabled = false;
            boomboxPickup.BatteryLevel = 1.0f;
            boomboxPickup.Range = RadioRange.Short;
            boomboxPickup.Scale = Scale;

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
                Log.Warn($"Initialize: No item owner was found for {Identifier(serial)}: got ServerHost player");
            }
        }
        else
        {
            Log.Error($"Initialize: No pickup or item with matching serial was found for {Identifier(serial)}");
            return;
        }

        // Initialize tracker objects
        Serials.Add(serial);
        AudioPlayers[serial] = null;
        Playbacks[serial] = null;

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
                Log.Info($"Created audio player for spawned {Identifier(serial)} with name: {audioPlayer.Name}");
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

    protected void OnRoundStarted()
    {
        // Send a broadcast to any player that doesn't have the SS setting set
        if (HintManager.ShowSSWarningHints)
        {
            int keybindSettingId = Config.ServerSettings.ChangeSongKeybind.Base.SettingId;
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

        // Check each tracked serial, if it wasn't initialized via OnPickupSpawned, initialize it here
        foreach (int ser in TrackedSerials)
        {
            ushort serial = (ushort)ser;
            if (!Serials.Contains(serial) || GetAudioPlayer(serial) == null)
            {
                Log.Debug($"On round start: AP tracker was null for serial {serial}, initializing");
                InitializeBoombox(serial);
            }
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
    }

    // Initializes newly spawned pickups, and/or attaches the audio player to the pickup for dropped/died events
    protected void OnPickupSpawned(PickupAddedEventArgs ev)
    {
        if (!Check(ev.Pickup))
        {
            return;
        }
        SetBoomboxSettings(radioPickup: (RadioPickup)ev.Pickup);
        Log.Debug($"Pickup spawned for {Identifier(ev.Pickup.Serial)}");

        var audioPlayer = GetAudioPlayer(ev.Pickup.Serial);
        if (audioPlayer is not null)
        {
            AudioHelper.AttachAudioPlayer(audioPlayer, ev.Pickup.GameObject, SpeakerVolume, SpeakerCount, MinDistance, MaxDistance, log: Config.AudioDebug);
        }
        else
        {
            Log.Debug($"-- no audio player, initializing");
            InitializeBoombox(ev.Pickup.Serial);
        }
    }

    // Prevents banned users from picking up the boombox
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
    }

    // Moves the audio player to the player
    protected override void OnAcquired(Player player, Item item, bool displayMessage)
    {
        base.OnAcquired(player, item, displayMessage);
        Log.Debug($"{player.Nickname} acquired {Identifier(item.Serial)}");

        var audioPlayer = GetAudioPlayer(item.Serial);
        if (audioPlayer is not null)
        {
            var speaker = AudioHelper.AttachAudioPlayer(audioPlayer, player.GameObject, SpeakerVolume, SpeakerCount, MinDistance, MaxDistance, log: Config.AudioDebug);
            if (speaker is null)
            {
                Log.Error($"OnAcquired: Speaker is null");
            }
        }
        else
        {
            Log.Debug($"-- no audio player, initializing");
            InitializeBoombox(item.Serial);
        }

        if (GetAudioPlayer(item.Serial) is null)
        {
            // TODO: Can probably remove this too - several other similar checks that i'll leave for now in case people have crazy experiences
            Log.Error($"OnAcquired: AudioPlayer is still null for {Identifier(item.Serial)}");
        }
    }

    // Just sets the boombox settings
    protected override void OnChanging(ChangingItemEventArgs ev)
    {
        base.OnChanging(ev);
        SetBoomboxSettings((Radio)ev.Item);
    }

    // Pauses/unpauses the boombox playback
    protected void OnTogglingRadio(TogglingRadioEventArgs ev)
    {
        if (!Check(ev.Radio))
        {
            return;
        }
        Log.Debug($"{ev.Player.Nickname} switched their {Identifier(ev.Radio.Serial)}: {(ev.NewState ? "ON" : "OFF")}");

        var audioPlayer = GetAudioPlayer(ev.Radio.Serial);
        if (audioPlayer is not null)
        {
            var currentPlayback = GetPlayback(ev.Radio.Serial);
            if (currentPlayback is null)
            {
                ChangeSong(ev.Radio.Serial, QueueType.Current, ev.Player);
            }
            else if (ev.NewState)
            {
                currentPlayback.IsPaused = false;
            }
            else
            {
                currentPlayback.IsPaused = true;
            }
        }
        else
        {
            Log.Error($"-- audio player was null for toggled radio");
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
            Log.Debug($"{ev.Player.Nickname} changed the {Identifier(ev.Radio.Serial)} playlist to {ev.NewValue}: {Playlists[ev.Radio.Range]}");

            // disable ChangeSong hint to avoid conflict with ChangePlaylist hint
            ChangeSong(ev.Radio.Serial, QueueType.Current, ev.Player, showHint: false);
            HintManager.ShowChangePlaylist(Playlists[ev.NewValue], ev.Player);
        }
    }

    // Not an EXILED handler, called directly when a player holding the boombox presses the SS key
    public void OnBoomboxKeyPressed(Player player, Item currentItem, int settingId)
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
            if (Config.KeybindDebug)
            {
                Log.Debug($"Player '{player.Nickname}' pressed the {Config.ServerSettings.GetKeyType(settingId)} key (id={settingId}) while holding {Identifier(boombox.Serial)}");
            }

            if (settingId == Config.ServerSettings.ChangeSongKeybindId)
            {
                ChangeSong(boombox.Serial, QueueType.Next, player);
            }
            else if (settingId == Config.ServerSettings.ShuffleSongKeybindId)
            {
                ShuffleSong(boombox.Serial, player);
            }
            else if (settingId == Config.ServerSettings.SwitchLoopKeybindId)
            {
                SwitchLoopMode(boombox.Serial, player);
            }
        }
        else if (Config.KeybindDebug)
        {
            Log.Debug($"Player '{player.Nickname}' can't interact: {Identifier(boombox.Serial)} is off");
        }
    }

    public void ChangeSong(ushort itemSerial, QueueType queueType, Player player = null, bool showHint = true)
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
        PlaySong(itemSerial, Playlists[range].CurrentSong, player);
        if (showHint)
        {
            HintManager.ShowChangeSong(Playlists[range], player);
        }
    }

    public void ShuffleSong(ushort itemSerial, Player player = null)
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
        // TODO: Change radio preset to the new range
        //if (newRange != oldRange)
        //{
        //    Radio radio = (Radio)Item.Get(itemSerial);
        //    if (radio is not null)
        //    {
        //        radio.Range = newRange;
        //        Log.Debug($"-- changing radio range to {newRange}");
        //    }
        //}

        Playlists[newRange].SongIndex = newIndex;
        PlaySong(itemSerial, Playlists[newRange].CurrentSong, player);
        HintManager.ShowShuffleSong(Playlists[newRange], player);
    }

    public void SwitchLoopMode(ushort itemSerial, Player player = null)
    {
        // TODO: Add looping entire playlist
        var currentPlayback = GetPlayback(itemSerial);
        if (currentPlayback is null)
        {
            Log.Debug($"Can't loop: No playback for {Identifier(itemSerial)}");
            return;
        }
        currentPlayback.Loop = !currentPlayback.Loop;
        HintManager.ShowSwitchLoop(newLoopMode, player);
    }

    private void PlaySong(ushort itemSerial, string song, Player player = null)
    {
        var audioPlayer = GetAudioPlayer(itemSerial);
        if (audioPlayer is null)
        {
            Log.Error($"Can't play song '{song}': {Identifier(itemSerial)} has a null audio player");
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
        Log.Debug($"Added clip '{playback.Clip}' to {Identifier(itemSerial)} audio player");

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

    // Override CustomItem hints with hints values from config
    protected override void ShowPickedUpMessage(Player player)
    {
        if (!HintManager.TryShowPickedUpHint(player))
        {
            base.ShowPickedUpMessage(player);
        }
    }

    protected override void ShowSelectedMessage(Player player)
    {
        if (!HintManager.TryShowSelectedHint(player))
        {
            base.ShowSelectedMessage(player);
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

    protected void OnUsingRadioPickupBattery(UsingRadioPickupBatteryEventArgs ev)
    {
        if (!Check(ev.RadioPickup))
        {
            return;
        }
        ev.IsAllowed = false;
    }

    // Blocks players from transmitting or receiving voice with a Boombox
    protected void OnPlayerSendingVoiceMessage(LabApi.Events.Arguments.PlayerEvents.PlayerSendingVoiceMessageEventArgs ev)
    {
        // first check that player is trying to use radio channel and is holding a radio
        if (ev.Message.Channel != VoiceChat.VoiceChatChannel.Radio)
        {
            return;
        }
        if (!ev.Player.TryGetRadio(out LabApi.Features.Wrappers.RadioItem radioItem))
        {
            return;
        }

        if (IsBoombox(radioItem.Serial))
        {
            //Log.Debug($"{ev.Player.Nickname} trying to send with a boombox: denied");
            ev.IsAllowed = false;
        }
    }

    protected void OnPlayerReceivingVoiceMessage(LabApi.Events.Arguments.PlayerEvents.PlayerReceivingVoiceMessageEventArgs ev)
    {
        // first check that player is trying to use radio channel and is holding a radio
        if (ev.Message.Channel != VoiceChat.VoiceChatChannel.Radio)
        {
            return;
        }
        if (!ev.Player.TryGetRadio(out LabApi.Features.Wrappers.RadioItem radioItem))
        {
            return;
        }

        if (IsBoombox(radioItem.Serial))
        {
            //Log.Debug($"{ev.Player.Nickname} trying to receive with a boombox: denied");
            ev.IsAllowed = false;
        }
    }
}