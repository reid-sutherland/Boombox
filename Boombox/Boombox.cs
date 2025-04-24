using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Attributes;
using Exiled.API.Features.Items;
using Exiled.API.Features.Pickups;
using Exiled.API.Features.Spawn;
using Exiled.CustomItems.API.Features;
using Exiled.Events.EventArgs.Item;
using Exiled.Events.EventArgs.Player;
using MEC;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using UnityEngine;
using YamlDotNet.Serialization;
using Boombox.Utils;
using static RoundSummary;
using Exiled.Events.EventArgs.Server;
using System.Linq;

namespace Boombox;

[CustomItem(ItemType.Radio)]
public class Boombox : CustomItem
{
    protected enum QueueType
    {
        Current = 0,
        Next,
        Last,
    }

    [YamlIgnore]
    public override uint Id { get; set; } = 51;

    [YamlIgnore]
    public override string Name { get; set; } = "Boombox";

    [YamlIgnore]
    public override string Description { get; set; } = "It looks like it plays music!";

    [YamlIgnore]
    public override float Weight { get; set; } = 10.0f;

    [YamlIgnore]
    public override Vector3 Scale { get; set; } = new(3.0f, 3.0f, 3.0f);

    [YamlIgnore]
    public string AudioPath => Path.Combine(Paths.Exiled, "Audio", "Boombox");

    [YamlIgnore]
    public string AudioPlayerName => GetType().Name;

    // TODO: For multiple boomboxes, needs to be a list (or a dict/set for no duplicates)
    private int BoomboxSerial { get; set; } = -1;

    // After this is created initially, it should never be null again, just overwritten :)
    private AudioPlayer AudioPlayer { get; set; } = null;

    private AudioClipPlayback CurrentPlayback { get; set; } = null;

    private string DiedWithPlayerId { get; set; } = "";

    private CoroutineHandle fettyHandle;

    private readonly List<string> songList = new() { "yeah.ogg", "krabby-patty.ogg", "679.ogg", "again.ogg", "south-memphis.ogg", "pirates-on-a-boat.ogg" };

    private int SongIndex { get; set; } = 0;

    //private Dictionary<RadioRange, int> PlaylistIndexes { get; set; } = new()
    //{
    //    { RadioRange.Short, 0 },
    //    { RadioRange.Medium, 0 },
    //    { RadioRange.Long, 0 },
    //    { RadioRange.Ultra, 0 },
    //};

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
    public float SpeakerVolume { get; private set; } = 1.0f;

    [Description("Set this to a higher value to increase the volume, duplicate speakers => louder.")]
    public int SpeakerCount { get; private set; } = 1;

    //[Description("The playlist of songs for each radio range setting.")]
    //public Dictionary<RadioRange, List<string>> Playlists { get; set; } = new()
    //{
    //    // memes
    //    {
    //        RadioRange.Short, new()
    //        {
    //            "yeah.ogg",
    //            "krabby-patty.ogg",

    //            //"pirates-on-a-boat.ogg",
    //        }
    //    },
    //    // rap
    //    {
    //        RadioRange.Medium, new()
    //        {
    //            "again.ogg",

    //            "south-memphis.ogg",
    //        }
    //    },
    //    // edm
    //    {
    //        RadioRange.Long, new()
    //        {
    //        }
    //    },
    //    // ???
    //    {
    //        RadioRange.Ultra, new()
    //        {
    //        }
    //    },
    //};

    protected override void SubscribeEvents()
    {
        // Rounds
        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Server.RoundEnded += OnRoundEnded;
        // Pickups/drops
        Exiled.Events.Handlers.Player.SearchingPickup += OnSearchingPickup;
        Exiled.Events.Handlers.Player.PickingUpItem += OnPickingUpItem;
        Exiled.Events.Handlers.Player.DroppedItem += OnDroppedItem;
        Exiled.Events.Handlers.Player.UsedItem += OnItemUsed;
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
        // Pickups/drops
        Exiled.Events.Handlers.Player.SearchingPickup -= OnSearchingPickup;
        Exiled.Events.Handlers.Player.PickingUpItem -= OnPickingUpItem;
        Exiled.Events.Handlers.Player.DroppedItem -= OnDroppedItem;
        Exiled.Events.Handlers.Player.UsedItem -= OnItemUsed;
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
        Log.Info($"Loading audio clips from directory: {AudioPath}");
        //AudioHelper.LoadAudioClips(AudioPath, Playlists[RadioRange.Short]);
        //AudioHelper.LoadAudioClips(AudioPath, Playlists[RadioRange.Medium]);
        //AudioHelper.LoadAudioClips(AudioPath, Playlists[RadioRange.Long]);
        //AudioHelper.LoadAudioClips(AudioPath, Playlists[RadioRange.Ultra]);
        AudioHelper.LoadAudioClips(AudioPath, songList);

        BoomboxSerial = -1;
        if (SpawnProperties.Count() > 0)
        {
            Log.Info($"Round started: spawning Boombox");
            SpawnAll();
            if (TrackedSerials.Count <= 0)
            {
                throw new System.Exception($"Boombox did not spawn");
            }
            else if (TrackedSerials.Count > 1)
            {
                string serials = string.Join(", ", TrackedSerials.Select(ser => ser.ToString()));
                throw new System.Exception($"Found multiple spawned boomboxes: {serials}");
            }
            BoomboxSerial = TrackedSerials.First();
            Log.Debug($"Found spawned boombox with serial: {BoomboxSerial}");

            Item item = Item.Get((ushort)BoomboxSerial);
            if (item is not null)
            {
                // "initialize" the boombox
                Radio boombox = (Radio)item;
                boombox.IsEnabled = false;
                boombox.BatteryLevel = 100;
                boombox.Range = RadioRange.Short;
                var rangeSettings = new Exiled.API.Structs.RadioRangeSettings()
                {
                    IdleUsage = 1.0f,
                    TalkingUsage = 1,
                    MaxRange = 1,
                };
                boombox.SetRangeSettings(RadioRange.Short, rangeSettings);
                boombox.SetRangeSettings(RadioRange.Medium, rangeSettings);
                boombox.SetRangeSettings(RadioRange.Long, rangeSettings);
                boombox.SetRangeSettings(RadioRange.Ultra, rangeSettings);

                // TODO: Figure out how to disable all of the base-game radio behavior when the item is spawned

                AudioPlayer = AudioHelper.GetAudioPlayer(AudioPlayerName);
                if (AudioPlayer is null)
                {
                    Log.Error($"Tried to create audio player for new boombox spawn, but it failed");
                }
            }
            else
            {
                Log.Error($"Tried to retrieve Item with serial={BoomboxSerial}, but it could not cast to an Item");
            }
        }
    }

    protected void OnRoundEnded(RoundEndedEventArgs ev)
    {
        DiedWithPlayerId = "";
    }

    protected void OnSearchingPickup(SearchingPickupEventArgs ev)
    {
        if (!Check(ev.Pickup))
        {
            return;
        }
        Log.Debug($"{ev.Player.Nickname} is searching the boombox: serial={ev.Pickup.Serial}");

        if (AudioPlayer is not null)
        {
            Log.Debug($"-- positions..." +
                      $"\n-- audio:        {AudioPlayer.transform.position}" +
                      $"\n-- audio parent: {(AudioPlayer.transform.parent is null ? "" : AudioPlayer.transform.parent.position)}" +
                      $"\n-- pickup:       {ev.Pickup.Transform.position}" +
                      $"\n-- player:       {ev.Player.Transform.position}");
            foreach (var kvp in AudioPlayer.SpeakersByName)
            {
                Log.Debug($"--- {kvp.Key}: {kvp.Value.transform.position}");
            }
        }
    }

    protected void OnPickingUpItem(PickingUpItemEventArgs ev)
    {
        if (!Check(ev.Pickup))
        {
            return;
        }
        Log.Debug($"{ev.Player.Nickname} is picking up the boombox: serial={ev.Pickup.Serial}");

        if (AudioPlayer is not null)
        {
            Speaker speaker = AudioHelper.SetAudioPlayerParent(AudioPlayer, ev.Player.GameObject);
        }
    }

    protected void OnDroppedItem(DroppedItemEventArgs ev)
    {
        if (!Check(ev.Pickup))
        {
            return;
        }
        Log.Debug($"{ev.Player.Nickname} dropped the boombox: serial={ev.Pickup.Serial}");

        if (AudioPlayer is not null)
        {
            Speaker speaker = AudioHelper.SetAudioPlayerParent(AudioPlayer, ev.Pickup.GameObject);
        }

        //bool wasPaused = false;
        //int wasReadPosition = -1;
        //if (AudioPlayer is not null)
        //{
        //    if (CurrentPlayback is not null)
        //    {
        //        wasPaused = CurrentPlayback.IsPaused;
        //        wasReadPosition = CurrentPlayback.ReadPosition;
        //        CurrentPlayback.IsPaused = true;
        //    }
        //    Log.Debug($"-- previous audio position: {AudioPlayer.transform.position}");
        //}

        //if (AudioPlayer is not null)
        //{
        //    Speaker speaker = AudioHelper.SetAudioPlayerParent(AudioPlayer, ev.Pickup.GameObject);

        //    // call this to re-create the current playback
        //    PlaySong(QueueType.Current);
        //    if (CurrentPlayback is not null)
        //    {
        //        if (wasReadPosition >= -1)
        //        {
        //            CurrentPlayback.ReadPosition = wasReadPosition;
        //        }
        //        CurrentPlayback.IsPaused = wasPaused;
        //    }
        //}
    }

    protected void OnItemUsed(UsedItemEventArgs ev)
    {
        // I don't think this is possible. Delete if we never see these error logs.
        if (!Check(ev.Item))
        {
            return;
        }
        Log.Error($"{ev.Player.Nickname} used the boombox: {Name}");
        Log.Error($"{ev.Player.Nickname} used the boombox: {Name}");
        Log.Error($"{ev.Player.Nickname} used the boombox: {Name}");
        Log.Error($"{ev.Player.Nickname} used the boombox: {Name}");
    }

    protected void OnDying(DyingEventArgs ev)
    {
        Item boombox = ev.ItemsToDrop.Where(item => item.Serial == BoomboxSerial).FirstOrDefault();
        if (boombox is not null)
        {
            Log.Debug($"{ev.Player.Nickname} is dying with the boombox - serial={boombox.Serial}");
            Log.Debug($"-- player pos: {ev.Player.Transform.position}");
            DiedWithPlayerId = ev.Player.UserId;
        }
        else
        {
            Log.Debug($"Player {ev.Player.Nickname} is dying without the boombox: {BoomboxSerial}");
        }
    }

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
                    Speaker speaker = AudioHelper.SetAudioPlayerParent(AudioPlayer, boomboxPickup.GameObject);
                }
                else
                {
                    Log.Error($"!!!!!!!!!!!!!!!! but a Pickup was not found with serial: {BoomboxSerial}");
                }
            }
            else
            {
                Log.Error($"Player ${ev.Player.Nickname} has died but it does not match the player from OnDying");
            }

            Log.Debug($"Clearing DiedWithPlayerId: {DiedWithPlayerId}");
            DiedWithPlayerId = "";
        }
        else
        {
            Log.Debug($"Player {ev.Player.Nickname} has died but nobody died with the boombox");
        }
    }

    protected void OnChangingRadioPreset(ChangingRadioPresetEventArgs ev)
    {
        if (!Check(ev.Radio))
        {
            return;
        }
        Log.Debug($"{ev.Player.Nickname} changed the radio preset from {ev.OldValue} to {ev.NewValue}");

        //Log.Debug($"-- max range ({ev.Radio.Range}): {ev.Radio.RangeSettings.MaxRange}");
        // TODO: Figure out how to disable the built-in radio functions
        SetRadioRange(ev.Radio);
        //Log.Debug($"-- max range of current setting ({ev.Radio.Range}): {ev.Radio.RangeSettings.MaxRange}");

        if (AudioPlayer is not null)
        {
            if (CurrentPlayback is not null)
            {
                AudioPlayer.RemoveClipByName(CurrentPlayback.Clip);
                CurrentPlayback = null;
            }

            PlaySong(QueueType.Next);
        }
    }

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
            else
            {
                if (ev.NewState)
                {
                    PlaySong(QueueType.Current);
                }
            }
        }
    }

    protected void OnUsingRadioBattery(UsingRadioBatteryEventArgs ev)
    {
        // Make the battery drain as slow as possible
        // Note: Setting it to <1 just makes the battery always dead for some reason
        ev.Drain = 1.0f;
    }

    protected void OnUsingRadioPickupBattery(UsingRadioPickupBatteryEventArgs ev)
    {
        // I think this applies when an un-held radio on the ground is using battery
        ev.Drain = 1.0f;
    }

    private void PlaySong(QueueType queueType, bool addAllSongs = true, bool shuffle = false)
    {
        if (shuffle)
        {
            throw new System.Exception("Shuffle is not yet supported");
        }

        switch (queueType)
        {
            case QueueType.Next:
                //PlaylistIndexes[range]++;
                SongIndex++;
                if (SongIndex >= songList.Count)
                {
                    SongIndex = 0;
                }
                break;
            case QueueType.Last:
                SongIndex--;
                if (SongIndex < 0)
                {
                    SongIndex = songList.Count - 1;
                }
                break;
            case QueueType.Current:
                break;
            default:
                break;
        }

        string song = songList[SongIndex].Replace(".ogg", "");
        CurrentPlayback = AudioPlayer.AddClip(song);
        Log.Debug($"Added clip '{CurrentPlayback.Clip}' to boombox audio player");

        //if (song == "again")    // and player == jared
        //{
        //    Log.Debug($"playing fetty wap. shaking in 10");
        //    AudioPlayer audioPlayer = AudioPlayer.CreateOrGet($"GLOBAL FETTY", onIntialCreation: (p) =>
        //    {
        //        // sad volume :( multi-speaker hack seems to bug out in global
        //        Speaker speaker = p.AddSpeaker($"Global", isSpatial: false, maxDistance: 5000f);
        //    });
        //    var playback = audioPlayer.AddClip(song);

        //    // shake the world for fetty
        //    fettyHandle = Timing.CallDelayed(10.5f, () =>
        //    {
        //        Log.Debug($"SHAKE");
        //        Warhead.Shake();
        //    });
        //}
        //else
        //{
        //    CurrentPlayback = AudioPlayer.AddClip(song);
        //    Log.Debug($"Added clip '{CurrentPlayback.Clip}' to boombox audio player");
        //    Timing.KillCoroutines(fettyHandle);
        //}

        if (addAllSongs)
        {
            AddAllSongs(SongIndex);
        }
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

    private void SetRadioRange(Radio radio)
    {
        // It's unclear why, but for some reason setting radio ranges on spawn does not appear to save.
        // But if the ranges are set while a player is interacting, then they seem to save correctly.

        var rangeSettings = new Exiled.API.Structs.RadioRangeSettings()
        {
            IdleUsage = 1.0f,
            TalkingUsage = 1,
            MaxRange = 1,
        };
        radio.SetRangeSettings(RadioRange.Short, rangeSettings);
        radio.SetRangeSettings(RadioRange.Medium, rangeSettings);
        radio.SetRangeSettings(RadioRange.Long, rangeSettings);
        radio.SetRangeSettings(RadioRange.Ultra, rangeSettings);
    }
}