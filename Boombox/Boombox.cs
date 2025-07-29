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
    public List<Tuple<Playlist, string>> AllSongs { get; set; } = new();

    private Config Config => MainPlugin.Configs;

    private HintManager HintManager => MainPlugin.Configs.HintManager;

    private BoomboxStates BoomboxStates { get; set; } = new();

    private bool EasterEggUsed { get; set; } = false;

    private CoroutineHandle EasterEggHandle { get; set; } = new();

    private CoroutineHandle LoopHandle { get; set; } = new();

    private string Identifier(ushort serial) => BoomboxStates.Identifier(serial);

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

    /// <summary>
    /// Initializes all of the tracked states and properties for an individual Boombox object (Radio or RadioPickup),
    /// then creates an AudioPlayer and attaches it to the GameObject of the identified object.
    /// This should be called any time a new Boombox object appears without an AudioPlayer.
    /// </summary>
    /// <returns>True if the tracked states, specifically the AudioPlayer, were successfully created.</returns>
    protected bool InitializeBoombox(ushort serial)
    {
        // First look for a pickup or an item with matching serial to initialize
        GameObject audioAttacher = null;
        Pickup pickup = Pickup.Get(serial);
        if (pickup is not null && pickup is RadioPickup boomboxPickup)
        {
            boomboxPickup.IsEnabled = false;
            boomboxPickup.BatteryLevel = 1.0f;
            boomboxPickup.Range = RadioRange.Short;
            boomboxPickup.Scale = Scale;

            audioAttacher = pickup.GameObject;
        }
        Item item = Item.Get(serial);
        if (item is not null && item is Radio boomboxItem)
        {
            boomboxItem.IsEnabled = false;
            boomboxItem.BatteryLevel = 100;
            boomboxItem.Range = RadioRange.Short;

            if (audioAttacher is null || item.Owner != Server.Host)
            {
                // only set attacher if a pickup was not found or a real player is holding the item
                audioAttacher = item.Owner.GameObject;
            }
        }

        // Create the state object for the boombox and add it to the tracker
        BoomboxState newState = new(serial, Playlists)
        {
            Range = RadioRange.Short,
            LoopMode = LoopMode.None,
            AudioPlayer = null,
            CurrentPlayback = null,
        };
        BoomboxStates[serial] = newState;

        // Create the audio player and attach speakers
        try
        {
            var audioPlayer = AudioHelper.GetAudioPlayer(
                audioPlayerName: newState.AudioPlayerName,
                parent: null, // attach the speaker separately for logging
                speakerVolume: SpeakerVolume,
                speakerCount: SpeakerCount,
                minDistance: MinDistance,
                maxDistance: MaxDistance,
                log: Config.AudioDebug
            );
            if (audioPlayer is not null)
            {
                Log.Info($"Initialize: Created audio player for spawned {newState.Identifier} with name: {audioPlayer.Name}");
                newState.AudioPlayer = audioPlayer;

                // Now attach speakers to the object
                if (audioAttacher is not null)
                {
                    AudioHelper.AttachAudioPlayer(audioPlayer, audioAttacher, SpeakerVolume, SpeakerCount, MinDistance, MaxDistance, log: Config.AudioDebug);
                }
                else
                {
                    Log.Error($"Initialize: Speaker was not attached: No pickup or item found with matching serial: {serial}");
                }
                return true;
            }
            else
            {
                Log.Error($"Initialize: Failed to create audio player for spawned {newState.Identifier}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Initialize: Tried to create audio player for spawned {newState.Identifier}. Exception: {ex.Message}");
        }
        return false;
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
        foreach (var playlist in Playlists.Values)
        {
            foreach (var song in playlist.Songs)
            {
                AllSongs.Add(new(playlist, song));
            }
        }

        // Check each tracked serial and initialize if needed
        foreach (int ser in TrackedSerials)
        {
            ushort serial = (ushort)ser;
            if (BoomboxStates.GetAudioPlayer(serial) is null)
            {
                Log.Debug($"OnRoundStart: No AudioPlayer for {Identifier(serial)}, initializing");
                if (!InitializeBoombox(serial))
                {
                    Log.Error($"OnRoundStart: Failed to initialize AudioPlayer for {Identifier(serial)}");
                }
            }
        }
        Log.Info($"Round started: tracking {BoomboxStates.Count} spawned Boombox(es)");

        LoopHandle = Timing.RunCoroutine(LoopCoroutine());
        Log.Debug($"Starting LoopCoroutine");

        if (Config.EasterEggEnabled)
        {
            EasterEggUsed = false;
        }
    }

    protected void OnRoundEnded(RoundEndedEventArgs ev)
    {
        BoomboxStates.Clear();

        Timing.KillCoroutines(LoopHandle);
    }

    // Initializes newly spawned pickups, and/or attaches the audio player to the pickup for dropped/died events
    protected void OnPickupSpawned(PickupAddedEventArgs ev)
    {
        if (!Check(ev.Pickup))
        {
            return;
        }
        Log.Debug($"Pickup spawned for {Identifier(ev.Pickup.Serial)}");
        ev.Pickup.Scale = Scale;    // scale likes to reset itself so re-apply

        var audioPlayer = BoomboxStates.GetAudioPlayer(ev.Pickup.Serial);
        if (audioPlayer is not null)
        {
            AudioHelper.AttachAudioPlayer(audioPlayer, ev.Pickup.GameObject, SpeakerVolume, SpeakerCount, MinDistance, MaxDistance, log: Config.AudioDebug);
        }
        else
        {
            Log.Debug($"OnPickupSpawned: No AudioPlayer, initializing");
            if (!InitializeBoombox(ev.Pickup.Serial))
            {
                Log.Error($"OnPickupSpawned: AudioPlayer is still null for {Identifier(ev.Pickup.Serial)}");
            }
        }
    }

    // Moves the audio player to the player
    protected override void OnAcquired(Player player, Item item, bool displayMessage)
    {
        base.OnAcquired(player, item, displayMessage);
        Log.Debug($"{player.Nickname} acquired {Identifier(item.Serial)}");

        var audioPlayer = BoomboxStates.GetAudioPlayer(item.Serial);
        if (audioPlayer is not null)
        {
            AudioHelper.AttachAudioPlayer(audioPlayer, player.GameObject, SpeakerVolume, SpeakerCount, MinDistance, MaxDistance, log: Config.AudioDebug);
        }
        else
        {
            Log.Debug($"OnAcquired: No AudioPlayer, initializing");
            if (!InitializeBoombox(item.Serial))
            {
                Log.Error($"OnAcquired: AudioPlayer is still null for {Identifier(item.Serial)}");
            }
        }
    }

    // Prevents banned users from picking up the boombox :)
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

    // Pauses/unpauses the boombox playback
    protected void OnTogglingRadio(TogglingRadioEventArgs ev)
    {
        if (!Check(ev.Radio))
        {
            return;
        }
        if (!BoomboxStates.TryGetValue(ev.Radio.Serial, out var state) || state is null)
        {
            Log.Error($"OnTogglingRadio: state object not found for {Identifier(ev.Radio.Serial)}");
            return;
        }

        Log.Debug($"{ev.Player.Nickname} switched their {state.Identifier}: {(ev.NewState ? "ON" : "OFF")}");
        state.Range = ev.Radio.Range;   // this shouldn't be necessary but feels safer
        if (state.AudioPlayer is null)
        {
            Log.Error($"OnTogglingRadio: no AudioPlayer for toggled {state.Identifier}");
            return;
        }

        if (state.CurrentPlayback is null)
        {
            ChangeSong(ev.Radio.Serial, QueueType.Current, ev.Player);
        }
        if (ev.NewState)
        {
            state.CurrentPlayback.IsPaused = false;
        }
        else
        {
            state.CurrentPlayback.IsPaused = true;
        }
    }

    // Changes the boombox playlist
    protected void OnChangingRadioPreset(ChangingRadioPresetEventArgs ev)
    {
        if (!Check(ev.Radio))
        {
            return;
        }
        if (!ev.Radio.IsEnabled)
        {
            ev.IsAllowed = false;
            return;
        }
        ChangePlaylist(ev.Radio.Serial, ev.NewValue, ev.Player);
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

    public void ChangePlaylist(ushort itemSerial, RadioRange newRange, Player player)
    {
        if (!BoomboxStates.TryGetValue(itemSerial, out var state) || state is null)
        {
            Log.Error($"ChangePlaylist: state object not found for {Identifier(itemSerial)}");
            return;
        }
        state.Range = newRange;
        Player holder = player ?? Server.Host;
        Log.Debug($"{holder.Nickname} changed the {state.Identifier} playlist to {state.Range}: {state.CurrentPlaylist.Name}");
        ChangeSong(itemSerial, QueueType.Current, player, showHint: false); // disable ChangeSong hint to avoid conflict with ChangePlaylist hint
        HintManager.ShowChangePlaylist(state.CurrentPlaylist, player);
    }

    public void ChangeSong(ushort itemSerial, QueueType queueType, Player player = null, bool showHint = true)
    {
        if (!BoomboxStates.TryGetValue(itemSerial, out var state) || state is null)
        {
            Log.Error($"ChangeSong: state object not found for {Identifier(itemSerial)}");
            return;
        }
        if (state.CurrentPlaylist.Length == 0)
        {
            Log.Debug($"No songs in the playlist for range: {state.Range}");
            return;
        }

        switch (queueType)
        {
            case QueueType.Next:
                state.CurrentPlaylist.NextSong();
                break;
            case QueueType.Last:
                state.CurrentPlaylist.PreviousSong();
                break;
            case QueueType.Current:
            default:
                break;
        }
        PlaySong(itemSerial, state.CurrentPlaylist.CurrentSong, player);
        if (showHint)
        {
            HintManager.ShowChangeSong(state.CurrentPlaylist, player);
        }
    }

    public void ShuffleSong(ushort itemSerial, Player player = null)
    {
        if (!BoomboxStates.TryGetValue(itemSerial, out var state) || state is null)
        {
            Log.Error($"ShuffleSong: state object not found for {Identifier(itemSerial)}");
            return;
        }
        if (AllSongs.Count == 0)
        {
            Log.Debug($"Can't shuffle: No songs in any playlists");
            return;
        }

        Tuple<Playlist, string> randomSongTuple = AllSongs.GetRandomValue();
        Playlist randomPlaylist = randomSongTuple.Item1;
        string randomSong = randomSongTuple.Item2;
        Log.Debug($"Shuffled song to '{randomSong}' from playlist: {randomPlaylist.Name}");

        PlaySong(itemSerial, randomSong, player);
        HintManager.ShowShuffleSong(randomPlaylist, randomSong, player);

        // TODO: This method needs some adjustments or at least refinement:
        //  - currently, Shuffle does not affect the current radio range, playlist, song, etc.
        //  - it literally just sets the active playback to a random song
        //  - not sure how to correctly do this yet but maybe it's better that it doesn't change the non-shuffle position?
    }

    public void SwitchLoopMode(ushort itemSerial, Player player = null)
    {
        if (!BoomboxStates.TryGetValue(itemSerial, out var state) || state is null)
        {
            Log.Error($"SwitchLoopMode: state object not found for {Identifier(itemSerial)}");
            return;
        }

        state.LoopMode = state.LoopMode.Next();
        Log.Debug($"Player '{player.Nickname}' switched {state.Identifier} loop mode to {state.LoopMode}");

        // AudioPlayerApi takes care of repeating current song via Loop flag
        state.CurrentPlayback.Loop = state.LoopMode == LoopMode.RepeatSong;
        HintManager.ShowSwitchLoop(state.LoopMode, player);
    }

    private void PlaySong(ushort itemSerial, string song, Player player = null)
    {
        if (!BoomboxStates.TryGetValue(itemSerial, out var state) || state is null)
        {
            Log.Error($"PlaySong: state object not found for {Identifier(itemSerial)}");
            return;
        }
        if (state.AudioPlayer is null)
        {
            Log.Error($"PlaySong: can't play '{song}': no AudioPlayer for {state.Identifier}");
            return;
        }

        state.StopCurrentPlayback();
        if (!state.StartNewPlayback(song))
        {
            return;
        }
        Log.Debug($"Added clip '{state.CurrentPlayback.Clip}' to AudioPlayer for {state.Identifier}");

        // Easter egg
        if (Config.EasterEggEnabled && player is not null)
        {
            // Always kill coroutine first so the timing isn't messed up
            Timing.KillCoroutines(EasterEggHandle);

            if (song == Config.EasterEggSong && player.UserId == Config.EasterEggPlayerId)
            {
                // If the song is not paused/changed, then easter egg will trigger after the configured delay time
                StartEasterEggTimer(state.CurrentPlayback);
            }
        }
    }

    private void StartEasterEggTimer(AudioClipPlayback playback)
    {
        if (EasterEggUsed)
        {
            Log.Debug($"Easter egg has already been used this round");
            return;
        }
        if (playback is null || playback.ReadPosition > AudioClipPlayback.PacketSize)
        {
            Log.Debug($"Easter egg playback is null or past start of clip");
            return;
        }

        // TODO: It seems that either 14.1 or LabAPI 1.1 broke the global speaker, needs a fix
        Log.Debug($"EasterEgg clip '{Config.EasterEggSong}' played: queuing shake");
        AudioPlayer audioPlayer = AudioPlayer.CreateOrGet($"GLOBAL", onIntialCreation: (p) =>
        {
            Speaker speaker = p.AddSpeaker($"Global", isSpatial: false, maxDistance: 5000f);
        });

        EasterEggHandle = Timing.CallDelayed(Config.EasterEggDelay, () =>
        {
            EasterEggUsed = true;
            Log.Debug($"SHAKE");
            Warhead.Shake();    // shake the facility when the beat drops
        });
    }

    private IEnumerator<float> LoopCoroutine()
    {
        for (; ; )
        {
            foreach (var state in BoomboxStates.Values)
            {
                if (state.LoopMode == LoopMode.None || state.LoopMode == LoopMode.RepeatSong) // RepeatSong is handled by playback.Loop
                {
                    continue;
                }

                // Invoke the proper loop action when an active clip playback ends
                var playback = state.CurrentPlayback;
                if (playback is not null && !playback.IsPaused && playback.ReadPosition >= playback.Samples.Length)
                {
                    Log.Debug($"LoopCoroutine: {state.Identifier} clip ended: {playback.Clip} - loop mode: {state.LoopMode}");

                    // Since I don't have a reliable way of getting item's owner from the state, hints will not be shown here
                    if (state.CurrentPlaylist is not null)
                    {
                        switch (state.LoopMode)
                        {
                            case LoopMode.CyclePlaylist:
                                ChangeSong(state.Serial, QueueType.Next);
                                break;
                            case LoopMode.ShuffleAll:
                                ShuffleSong(state.Serial);
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
            // Don't need to clog up game thread(?) with constant checking so once a second should be fast enough
            yield return Timing.WaitForSeconds(1.0f);
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

        if (BoomboxStates.IsBoombox(radioItem.Serial))
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

        if (BoomboxStates.IsBoombox(radioItem.Serial))
        {
            //Log.Debug($"{ev.Player.Nickname} trying to receive with a boombox: denied");
            ev.IsAllowed = false;
        }
    }
}