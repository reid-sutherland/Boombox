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

    /// Notes about Radio items:
    /// - If the item was initially spawned as a pickup, then Item.Get(serial) seems to not work.
    /// - Getters always return the same data from initialized, and Setters do not seem to stick.
    /// - However if the item was spawned as an item (e.g. customitems.give) then the serial approach works for pickups/items.
    /// Notes about additional tracker dictionaries:
    /// - The Radio item issues above make it difficult to get an equipped Radio's range correctly.
    /// - Adding a Range tracker was the best way that I could find to make the Loop coroutine work effectively.
    /// - However, this could cause bugs in the future so need to find a better system or revert.
    /// TODO: Now I need a better way to maintain per-Boombox state...

    private static HashSet<ushort> Serials { get; set; } = new();

    private static Dictionary<ushort, RadioRange> RadioRanges { get; set; } = new();

    private static Dictionary<ushort, LoopMode> LoopModes { get; set; } = new();

    private static Dictionary<ushort, AudioPlayer> AudioPlayers { get; set; } = new();

    private static Dictionary<ushort, AudioClipPlayback> Playbacks { get; set; } = new();

    private bool EasterEggUsed { get; set; } = false;

    private CoroutineHandle EasterEggHandle { get; set; } = new();

    private CoroutineHandle LoopHandle { get; set; } = new();

    public static bool IsBoombox(ushort serial) => Serials.Contains(serial);

    public static string Identifier(ushort serial) => $"{nameof(Boombox)}({serial})";

    private RadioRange GetRange(ushort serial) => RadioRanges.TryGetValue(serial, out var range) ? range : RadioRange.Short;

    private LoopMode GetLoopMode(ushort serial) => LoopModes.TryGetValue(serial, out var loopMode) ? loopMode : LoopMode.None;

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
        if (audioAttacher is null)
        {
            Log.Error($"Initialize: No pickup or item with matching serial was found for {Identifier(serial)}");
            return;
        }

        // Initialize tracker objects
        Serials.Add(serial);
        RadioRanges[serial] = RadioRange.Short;
        LoopModes[serial] = LoopMode.None;
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
                Log.Info($"Initialize: Created audio player for spawned {Identifier(serial)} with name: {audioPlayer.Name}");
            }
            else
            {
                Log.Error($"Initialize: Failed to create audio player for spawned {Identifier(serial)}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Initialize: Tried to create audio player for spawned {Identifier(serial)}. Exception: {ex.Message}");
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

        // Check each tracked serial and initialize if needed
        foreach (int ser in TrackedSerials)
        {
            ushort serial = (ushort)ser;
            if (!Serials.Contains(serial) || GetAudioPlayer(serial) == null)
            {
                Log.Debug($"OnRoundStart: No AudioPlayer for {Identifier(serial)}, initializing");
                InitializeBoombox(serial);
            }
        }
        Log.Info($"Round started: spawned {Serials.Count} Boombox(es)");

        LoopHandle = Timing.RunCoroutine(LoopCoroutine());
        Log.Debug($"Starting LoopCoroutine");

        if (Config.EasterEggEnabled)
        {
            EasterEggUsed = false;
        }
    }

    protected void OnRoundEnded(RoundEndedEventArgs ev)
    {
        Serials.Clear();
        RadioRanges.Clear();
        LoopModes.Clear();
        AudioPlayers.Clear();
        Playbacks.Clear();

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

        var audioPlayer = GetAudioPlayer(ev.Pickup.Serial);
        if (audioPlayer is not null)
        {
            AudioHelper.AttachAudioPlayer(audioPlayer, ev.Pickup.GameObject, SpeakerVolume, SpeakerCount, MinDistance, MaxDistance, log: Config.AudioDebug);
        }
        else
        {
            Log.Debug($"OnPickupSpawned: No AudioPlayer, initializing");
            InitializeBoombox(ev.Pickup.Serial);
        }

        if (GetAudioPlayer(ev.Pickup.Serial) is null)
        {
            Log.Error($"OnPickupSpawned: AudioPlayer is still null for {Identifier(ev.Pickup.Serial)}");
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
            Log.Debug($"OnAcquired: No AudioPlayer, initializing");
            InitializeBoombox(item.Serial);
        }

        if (GetAudioPlayer(item.Serial) is null)
        {
            Log.Error($"OnAcquired: AudioPlayer is still null for {Identifier(item.Serial)}");
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
        Log.Debug($"{ev.Player.Nickname} switched their {Identifier(ev.Radio.Serial)}: {(ev.NewState ? "ON" : "OFF")}");
        RadioRanges[ev.Radio.Serial] = ev.Radio.Range;

        var audioPlayer = GetAudioPlayer(ev.Radio.Serial);
        if (audioPlayer is not null)
        {
            var currentPlayback = GetPlayback(ev.Radio.Serial);
            if (currentPlayback is null)
            {
                ChangeSong(ev.Radio.Serial, QueueType.Current, ev.Player);
                currentPlayback = GetPlayback(ev.Radio.Serial);
            }

            if (ev.NewState)
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
        if (!ev.Radio.IsEnabled)
        {
            ev.IsAllowed = false;
            return;
        }

        // TODO: Replace this logic with a ChangePlaylist method so ChangeSong is separate
        RadioRanges[ev.Radio.Serial] = ev.NewValue;
        Log.Debug($"{ev.Player.Nickname} changed the {Identifier(ev.Radio.Serial)} playlist to {ev.NewValue}: {Playlists[ev.Radio.Range].Name}");

        // disable ChangeSong hint to avoid conflict with ChangePlaylist hint
        ChangeSong(ev.Radio.Serial, QueueType.Current, ev.Player, showHint: false);
        HintManager.ShowChangePlaylist(Playlists[ev.NewValue], ev.Player);
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
        RadioRange range = GetRange(itemSerial);
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

        // TODO: This method needs some adjustments or at least refinement:
        //  - currently, Shuffle does not affect the current radio range, playlist, song, etc.
        //  - it literally just sets the active playback to a random song

        // Set the radio and song index to the new range and playlist position
        //RadioRange oldRange = GetRange(itemSerial);
        //if (newRange != oldRange)
        //{
        //    Radio radio = (Radio)Item.Get(itemSerial);
        //    if (radio is not null)
        //    {
        //        // TODO: setting the radio to the new range does not work
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
        LoopMode oldLoopMode = GetLoopMode(itemSerial);
        LoopMode newLoopMode = NextLoopMode(oldLoopMode);
        Log.Debug($"Player '{player.Nickname}' switched {Identifier(itemSerial)} loop mode from {oldLoopMode} to {newLoopMode}");

        var playback = GetPlayback(itemSerial);
        if (playback is not null)
        {
            playback.Loop = newLoopMode == LoopMode.RepeatSong;
        }
        LoopModes[itemSerial] = newLoopMode;
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

        playback = audioPlayer.AddClip(song, loop: GetLoopMode(itemSerial) == LoopMode.RepeatSong);
        Playbacks[itemSerial] = playback;
        Log.Debug($"Added clip '{playback.Clip}' to {Identifier(itemSerial)} audio player");

        if (player is not null)
        {
            // Easter egg
            if (Config.EasterEggEnabled)
            {
                if (song == Config.EasterEggSong && player.UserId == Config.EasterEggPlayerId)
                {
                    // TODO: PlaySong cancels on song change but need to cancel (or delay) coroutine on playback paused
                    ActivateEasterEgg(playback);
                }
                else
                {
                    Timing.KillCoroutines(EasterEggHandle);
                }
            }
        }
    }

    private void ActivateEasterEgg(AudioClipPlayback playback)
    {
        if (EasterEggUsed)
        {
            Log.Debug($"Easter egg has already been used this round");
            return;
        }
        if (playback is null || playback.ReadPosition > AudioClipPlayback.PacketSize)
        {
            Log.Debug($"Easter egg playback is missing or past start of clip");
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

    private IEnumerator<float> LoopCoroutine()
    {
        for (; ; )
        {
            foreach (var serial in Serials)
            {
                var loopMode = GetLoopMode(serial);
                if (loopMode == LoopMode.None || loopMode == LoopMode.RepeatSong) // RepeatSong is handled by playback.Loop
                {
                    continue;
                }

                // Invoke the proper loop action when an active clip playback ends
                var playback = GetPlayback(serial);
                if (playback is not null && !playback.IsPaused && playback.ReadPosition >= playback.Samples.Length)
                {
                    Log.Debug($"LoopCoroutine: {Identifier(serial)} clip ended: {playback.Clip} - loop mode: {loopMode}");

                    // TODO: With the tracked Ranges approach, the only thing we're missing here is the item owner...
                    //       but that's only needed for the hint so maybe it's better that way

                    RadioRange range = GetRange(serial);
                    Playlist playlist = Playlists[range];
                    Player player = null;
                    if (playlist is not null)
                    {
                        switch (loopMode)
                        {
                            case LoopMode.CyclePlaylist:
                                playlist.NextSong();
                                PlaySong(serial, playlist.CurrentSong, player);
                                break;
                            case LoopMode.ShuffleAll:
                                ShuffleSong(serial, player);
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
            // Don't need to clog up CPU with constant checking so once a second should be fast enough
            yield return Timing.WaitForSeconds(1.0f);
        }
    }

    private LoopMode NextLoopMode(LoopMode loopMode)
    {
        return loopMode switch
        {
            LoopMode.None => LoopMode.RepeatSong,
            LoopMode.RepeatSong => LoopMode.CyclePlaylist,
            LoopMode.CyclePlaylist => LoopMode.ShuffleAll,
            LoopMode.ShuffleAll => LoopMode.None,
            _ => LoopMode.None,
        };
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