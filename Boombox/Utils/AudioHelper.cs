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

    public static Speaker SetAudioPlayerParent(AudioPlayer audioPlayer, GameObject parent, float speakerVolume = 1.0f, int speakerCount = 1, float minDistance = 5.0f, float maxDistance = 5.0f)
    {
        try
        {
            // Attach created audio player to the gameObject's transform
            audioPlayer.transform.SetParent(parent.transform);

            Speaker outSpeaker = null;
            for (int i = 0; i < speakerCount; i++)
            {
                string speakerName = audioPlayer.Name + $"-Main-{i}";

                // This created speaker will be in 3D space.
                Speaker speaker = audioPlayer.GetOrAddSpeaker(audioPlayer.Name + $"-Main-{i}", isSpatial: true, minDistance: minDistance, maxDistance: maxDistance);

                // Attach created speaker to gameObject.
                speaker.transform.SetParent(parent.transform);

                // Set local positino to zero to make sure that speaker is in the gameObject.
                speaker.transform.localPosition = Vector3.zero;

                // Set volume on the speaker
                speaker.Volume = speakerVolume;

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
        catch (System.Exception ex)
        {
            Log.Error($"Exception during SetAudioPlayerParent(): {ex.Message}");
            Log.Debug($"-- stacktrace: {ex.StackTrace}");
            return null;
        }
    }
}