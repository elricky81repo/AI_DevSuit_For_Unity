using System;
using System.Threading.Tasks;
using UnityEngine;
using WavSave;

/// <summary>
/// Handles microphone recording, WAV saving, and STT transcription with robust error logging.
/// </summary>
public class SoundRecordSys : MonoBehaviour
{
    [Tooltip("Assign a GameObject that has an AIManager component.")]
    public GameObject AIManagerObject;

    [Tooltip("Maximum recording time in seconds.")]
    public int maxRecordedTime = 25;

    [Tooltip("Language code for transcription: es, en, pt, etc.")]
    public string transcriptionLanguage = "es";

    private AudioClip _clip;
    private float _recordingTime;
    private bool _isRecording;
    private double _startTime;
    private const string _fileName = "output.wav";

    /// <summary>
    /// Begins recording from the default microphone.
    /// Logs available devices and any exceptions.
    /// </summary>
    public void StartRecording()
    {
        try
        {
            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                Debug.LogError("SoundRecordSys: No microphone devices found.");
                return;
            }

            //Debug.Log($"SoundRecordSys: Available devices: {string.Join(", ", devices)}");

            string deviceName = devices[0];
            //Debug.Log($"SoundRecordSys: Using device '{deviceName}'");

            _startTime = Time.realtimeSinceStartup;
            _isRecording = true;
            _recordingTime = 0f;
            _clip = Microphone.Start(deviceName, false, maxRecordedTime, 16000);

            if (_clip == null)
                Debug.LogError("SoundRecordSys: Microphone.Start returned null.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"SoundRecordSys: Exception starting recording: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Stops recording, saves to WAV, sends to STT, and returns the transcribed text.
    /// Logs errors and returns null on failure or short recordings.
    /// </summary>
    public async Task<string> EndRecordingAsync()
    {
        if (!_isRecording)
        {
            Debug.LogWarning("SoundRecordSys: Attempt to end recording when none is active.");
            return null;
        }

        string result = null;

        try
        {
            Microphone.End(null);
            _isRecording = false;

            _recordingTime = (float)(Time.realtimeSinceStartup - _startTime);
            //Debug.Log($"SoundRecordSys: Recording ended at {_recordingTime:F2} seconds.");

            if (_recordingTime < 0.5f)
            {
                //Debug.LogWarning("SoundRecordSys: Recording too short (<0.5s); ignoring.");
                return null;
            }

            AudioClip monoClip = DownmixToMono(_clip);
            //Debug.Log("SoundRecordSys: Audio downmixed to mono.");

            byte[] data = WavSaveLib.Save(_fileName, monoClip);
            //Debug.Log($"SoundRecordSys: WAV saved ({data.Length} bytes).");

            if (AIManagerObject == null)
            {
                Debug.LogError("SoundRecordSys: AIManagerObject GameObject reference is null.");
                return null;
            }

            var aiManager = AIManagerObject.GetComponent<AIManager>();
            if (aiManager == null)
            {
                Debug.LogError("SoundRecordSys: AIManager component not found on GameObject.");
                return null;
            }

            //Debug.Log("SoundRecordSys: Sending audio data to STT...");
            result = await aiManager.CreateAudioTranscriptionAsync(data);
            //Debug.Log($"SoundRecordSys: STT response received: '{result}'");
        }
        catch (Exception ex)
        {
            Debug.LogError($"SoundRecordSys: Exception during EndRecordingAsync: {ex.Message}\n{ex.StackTrace}");
        }

        return result;
    }

    private void Update()
    {
        if (_isRecording)
        {
            _recordingTime = (float)(Time.realtimeSinceStartup - _startTime);

            if (_recordingTime >= maxRecordedTime)
            {
                Debug.LogWarning("SoundRecordSys: Max recording time reached; auto-stopping.");
                Microphone.End(null);
                _isRecording = false;
            }
        }
    }

    /// <summary>
    /// Converts a multichannel AudioClip to mono; logs any errors.
    /// </summary>
    private AudioClip DownmixToMono(AudioClip stereoClip)
    {
        try
        {
            int samplesCount = stereoClip.samples;
            int channels = stereoClip.channels;
            int freq = stereoClip.frequency;

            float[] stereoData = new float[samplesCount * channels];
            stereoClip.GetData(stereoData, 0);

            float[] monoData = new float[samplesCount];
            for (int i = 0; i < samplesCount; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                    sum += stereoData[i * channels + c];

                monoData[i] = sum / channels;
            }

            var monoClip = AudioClip.Create(
                stereoClip.name + "_mono",
                samplesCount,
                1,
                freq,
                false
            );

            monoClip.SetData(monoData, 0);
            return monoClip;
        }
        catch (Exception ex)
        {
            Debug.LogError($"SoundRecordSys: Error downmixing audio: {ex.Message}");
            return stereoClip;
        }
    }
}