using Exiled.API.Features;
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace Boombox;

public static class AudioHelper
{
    public static bool LoadAudioClip(string audioDir, string audioFile, bool log = false)
    {
        if (log)
        {
            Log.Debug($"-- loading audio clip: {audioFile}");
        }

        string filepath = Path.Combine(audioDir, audioFile);
        string name = audioFile.Replace(".ogg", "");
        return AudioClipStorage.LoadClip(filepath, name);
    }

    public static List<string> LoadAudioClips(string audioDir, List<string> audioFiles, bool log = false)
    {
        List<string> failedClips = new();
        foreach (string file in audioFiles)
        {
            if (!LoadAudioClip(audioDir, file, log))
            {
                failedClips.Add(file);
            }
        }
        return failedClips;
    }

    public static AudioPlayer GetAudioPlayer(string audioPlayerName, GameObject parent = null, float speakerVolume = 1.0f, int speakerCount = 1, float minDistance = 5.0f, float maxDistance = 5.0f, bool log = false)
    {
        return AudioPlayer.CreateOrGet(audioPlayerName, onIntialCreation: (audioPlayer) =>
        {
            if (parent is not null)
            {
                AttachAudioPlayer(audioPlayer, parent, speakerVolume, speakerCount, minDistance, maxDistance, log);
            }
        });
    }

    public static Speaker AttachAudioPlayer(AudioPlayer audioPlayer, GameObject parent, float speakerVolume = 1.0f, int speakerCount = 1, float minDistance = 5.0f, float maxDistance = 5.0f, bool log = false)
    {
        try
        {
            // Attach created audio player to the gameObject's transform
            audioPlayer.transform.SetParent(parent.transform);

            Speaker outSpeaker = null;
            for (int i = 0; i < speakerCount; i++)
            {
                string speakerName = $"{audioPlayer.Name}-S{i}";

                // This created speaker will be in 3D space.
                Speaker speaker = audioPlayer.GetOrAddSpeaker(speakerName, isSpatial: true, minDistance: minDistance, maxDistance: maxDistance);

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

            if (log)
            {
                Log.Debug($"-- setting {audioPlayer.Name} speaker to parent at position: {audioPlayer.transform.parent.position}");
            }
            return outSpeaker;
        }
        catch (Exception ex)
        {
            Log.Error($"Exception during {nameof(AttachAudioPlayer)}: {ex.Message}");
            if (log)
            {
                Log.Debug($"-- stacktrace: {ex.StackTrace}");
            }
            return null;
        }
    }
}