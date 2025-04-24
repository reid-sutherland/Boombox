using System.IO;
using System.Collections.Generic;
using Exiled.API.Features;
using UnityEngine;

namespace Boombox.Utils;

public static class AudioHelper
{
    public static void LoadAudioClip(string audioDir, string audioFile)
    {
        string filepath = Path.Combine(audioDir, audioFile);
        string name = audioFile.Replace(".ogg", "");
        if (MainPlugin.Configs.AudioDebug)
        {
            Log.Debug($"-- loading audio clip: {name}");
        }
        if (!AudioClipStorage.LoadClip(filepath, name))
        {
            Log.Error($"Failed to load clip: {filepath}");
        }
    }

    public static void LoadAudioClips(string audioDir, List<string> audioFiles)
    {
        foreach (string file in audioFiles)
        {
            LoadAudioClip(audioDir, file);
        }
    }

    public static AudioPlayer GetAudioPlayer(string audioPlayerName, GameObject parent = null)
    {
        return AudioPlayer.CreateOrGet(audioPlayerName, onIntialCreation: (player) =>
        {
            if (parent is not null)
            {
                SetAudioPlayerParent(player, parent);
            }
        });
    }

    public static Speaker SetAudioPlayerParent(AudioPlayer audioPlayer, GameObject parent, float volume = 1.0f)
    {
        // Attach created audio player to the gameObject's transform
        audioPlayer.transform.SetParent(parent.transform);

        // TODO: CONFIGURE ALL THIS SHIT
        //  - seems adding more duplicate speakers works to make it louder lmao
        //  - volume
        //  - min/maxDisatance

        Log.Debug($"Setting new positions for SPEAKERS");
        Speaker outSpeaker = null;
        for (int i = 0; i < 1; i++)
        {
            string speakerName = audioPlayer.Name + $"-Main-{i}";

            // This created speaker will be in 3D space.
            Speaker speaker = audioPlayer.GetOrAddSpeaker(audioPlayer.Name + $"-Main-{i}", isSpatial: true, minDistance: 4.0f, maxDistance: 60.0f);

            // Attach created speaker to gameObject.
            speaker.transform.SetParent(parent.transform);

            // Set local positino to zero to make sure that speaker is in the gameObject.
            speaker.transform.localPosition = Vector3.zero;

            // Set volume on the speaker
            speaker.Volume = 1.0f;

            if (i == 0)
            {
                outSpeaker = speaker;
            }
        }

        if (MainPlugin.Configs.AudioDebug)
        {
            Log.Debug($"Setting audio player speaker to position: {outSpeaker.transform.position}");
        }
        return outSpeaker;
    }
}