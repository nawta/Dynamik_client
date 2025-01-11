//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//

// https://github.com/Azure-Samples/cognitive-services-speech-sdk/blob/master/samples/csharp/unity/speechrecognizer/Assets/SpeechSDKSample/Scripts/SpeechRecognition.cs

using UnityEngine;
using UnityEngine.UI;
using Microsoft.CognitiveServices.Speech;
using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using System.IO;
using Azure;
using Azure.AI;
using Azure.AI.TextAnalytics;
using TMPro;
using NetMQ;
using NetMQ.Sockets;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif
#if PLATFORM_IOS
using UnityEngine.iOS;
using System.Collections;
#endif

public class TestAudio : MonoBehaviour
{
    private bool micPermissionGranted = false;
    
    private bool isRecognizing = false;
    
    [SerializeField]
    public TextMeshProUGUI[] _outputTexts;
    TranslationRecognizer recognizer;
    AudioConfig audioConfig;
    PushAudioInputStream pushStream;
    SpeechTranslationConfig translationConfig;

    private object threadLocker = new object();
    private bool recognitionStarted = false;
    private string[] messages;
    int lastSample = 0;
    
    [SerializeField]
    private int numberOfMicrophones = 2;
    [SerializeField]
    private AudioSource[] audioSources;
    
    [SerializeField]
    private LineRenderer[] lineRenderers;
    private float[] samples;
    private int samplesPerMicrophone;

#if PLATFORM_ANDROID || PLATFORM_IOS
    // Required to manifest microphone permission, cf.
    // https://docs.unity3d.com/Manual/android-manifest.html
    private Microphone mic;
#endif

    private byte[] ConvertAudioClipDataToInt16ByteArray(float[] data)
    {
        MemoryStream dataStream = new MemoryStream();
        int x = sizeof(Int16);
        Int16 maxValue = Int16.MaxValue;
        int i = 0;
        while (i < data.Length)
        {
            dataStream.Write(BitConverter.GetBytes(Convert.ToInt16(data[i] * maxValue)), 0, x);
            ++i;
        }
        byte[] bytes = dataStream.ToArray();
        dataStream.Dispose();
        return bytes;
    }

    private void RecognizingHandler(object sender, TranslationRecognitionEventArgs e)
    {
        lock (threadLocker)
        {
            for (int i = 0; i < numberOfMicrophones; i++)
            {
                messages[i] = "recognizing";
                foreach (var element in e.Result.Translations)
                {
                    Debug.Log("RecognizingHandler: " + messages[i]);
                }
            }
        }
    }

    private void RecognizedHandler(object sender, TranslationRecognitionEventArgs e)
    {
        lock (threadLocker)
        {
            for (int i = 0; i < numberOfMicrophones; i++)
            {
                if (e.Result.Reason == ResultReason.TranslatedSpeech)
                {
                    Debug.Log($"Recognized: {e.Result.Text}");
                    messages[i] = ""; 
                    foreach (var element in e.Result.Translations)
                    {
                        Debug.Log($"Translated into '{element.Key}': {element.Value} at (Microphone {i})");
                        string recognizedText = element.Value;
                        string resultText = QueryText2API(recognizedText);
                        Debug.Log($"Result Text (Microphone {i}): {resultText}");
                        messages[i] += resultText;
                    }
                }
            }
        }
    }

    private void CanceledHandler(object sender, TranslationRecognitionCanceledEventArgs e)
    {
        lock (threadLocker)
        {
            for (int i = 0; i < numberOfMicrophones; i++)
            {
                messages[i] = e.ErrorDetails.ToString();
                Debug.Log("CanceledHandler: " + messages[i]);
            }
        }
    }

    // public async void RecognizeSpeech()
    // {
    //     if (recognitionStarted)
    //     {
    //         await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(true);
    //         Debug.Log("Continuous recognition stopped.");
    //
    //         for (int i = 0; i < numberOfMicrophones; i++)
    //         {
    //             var device = Microphone.devices[i];
    //             if (Microphone.IsRecording(device))
    //             {
    //                 Debug.Log("Microphone.End: " + device);
    //                 Microphone.End(device);
    //             }
    //         }
    //
    //         lock (threadLocker)
    //         {
    //             recognitionStarted = false;
    //             Debug.Log("RecognitionStarted: " + recognitionStarted.ToString());
    //         }
    //     }
    //     else
    //     {
    //         for (int i = 0; i < numberOfMicrophones; i++)
    //         {
    //             var device = Microphone.devices[i];
    //             if (!Microphone.IsRecording(device))
    //             {
    //                 Debug.Log("Microphone.Start: " + device);
    //                 audioSources[i].clip = Microphone.Start(device, true, 200, 16000);
    //                 Debug.Log("audioSource.clip channels: " + audioSources[i].clip.channels);
    //                 Debug.Log("audioSource.clip frequency: " + audioSources[i].clip.frequency);
    //             }
    //         }
    //
    //         await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
    //         Debug.Log("Continuous recognition started.");
    //         lock (threadLocker)
    //         {
    //             recognitionStarted = true;
    //             Debug.Log("RecognitionStarted: " + recognitionStarted.ToString());
    //         }
    //     }
    // }
    
    
    public string QueryText2API(string text)
    {
        string res = "";
        try
        {
            using (var client = new RequestSocket("tcp://localhost:5557"))
            {
                Debug.Log($"Sending to server: {text}");
                client.SendFrame(text);
                Debug.Log($"Sent to server: {text}");
                res = client.ReceiveFrameString();
                Debug.Log($"Received from server: {res}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error in QueryText2API: {ex.Message}");
        }

        return res;
    }

    void Start()
    {
        string speechKey = "<Your Azure SpeechService's Speech Key here>";
        string speechRegion = "<Your Azure SpeechService's Region here>";
        Debug.Log(speechKey);
        Debug.Log(speechRegion);
        
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone devices found.");
            return;
        }


        if (_outputTexts == null || _outputTexts.Length != numberOfMicrophones)
        {
            UnityEngine.Debug.LogError("The number of output text elements does not match the number of microphones.");
            return;
        }
        else
        {
            if (numberOfMicrophones > Microphone.devices.Length)
            {
                Debug.LogError("The number of microphones specified is greater than the number of available microphones.");
                return;
            }

            messages = new string[numberOfMicrophones];
            
            //Audio Sourceの定義
            audioSources = new AudioSource[numberOfMicrophones];
            for (int i = 0; i < numberOfMicrophones; i++)
            {
                audioSources[i] = GameObject.Find("MyAudioSource" + i.ToString()).AddComponent<AudioSource>();
            }
            
            translationConfig = SpeechTranslationConfig.FromSubscription(speechKey, speechRegion);
            var fromLanguage = "en-US"; //"en-US";//"ja-JP";//"ko-KR";//"zh-CN";
            var toLanguages = new List<string> { "ja" }; //"en", 

            translationConfig.SpeechRecognitionLanguage = fromLanguage;
            toLanguages.ForEach(translationConfig.AddTargetLanguage);
            
			pushStream = AudioInputStream.CreatePushStream();
            audioConfig = AudioConfig.FromStreamInput(pushStream);
            recognizer = new TranslationRecognizer(translationConfig, audioConfig);
            
            recognizer.Recognizing += RecognizingHandler;
            recognizer.Recognized += RecognizedHandler;
            recognizer.Canceled += CanceledHandler;
            recognizer.SessionStarted += (s, e) => {
                Debug.Log("\nSession started event.");
            };
            recognizer.SessionStopped += (s, e) => {
                Debug.Log("\nSession stopped event.");
            };
            Debug.Log("Translation recognizer created.");
            
            foreach (var device in Microphone.devices)
            {
                Debug.Log("DeviceName: " + device);                
            }
            
            
            //描画用
            samplesPerMicrophone = 1024; // マイクごとのサンプル数
            samples = new float[numberOfMicrophones * samplesPerMicrophone];

            lineRenderers = new LineRenderer[numberOfMicrophones];
            for (int i = 0; i < numberOfMicrophones; i++)
            {
                GameObject lineRendererObject = new GameObject($"LineRenderer_{i}");
                lineRendererObject.transform.SetParent(transform);
                lineRenderers[i] = lineRendererObject.AddComponent<LineRenderer>();
                lineRenderers[i].positionCount = samplesPerMicrophone;
                lineRenderers[i].widthMultiplier = 0.01f;
            }
            
            if (recognizer == null)
            {
                Debug.LogError("Recognizer is not initialized. in Start");
                return;
            }
            // recognizer の初期化が完了するまで待機するための遅延呼び出し
            Invoke("DelayedRecognizeSpeech", 1.0f);
        }
    }
    
    void DelayedRecognizeSpeech()
    {
        if (recognizer != null)
        {
            RecognizeSpeech();
        }
        else
        {
            Debug.LogError("Recognizer is not initialized. in DelayedRecognitionSpeech");
        }
    }
    

    void Disable()
    {
        recognizer.Recognizing -= RecognizingHandler;
        recognizer.Recognized -= RecognizedHandler;
        recognizer.Canceled -= CanceledHandler;
        pushStream.Close();
        recognizer.Dispose();
    }

//     void FixedUpdate()
//     {
// // #if PLATFORM_ANDROID
// //         if (!micPermissionGranted && Permission.HasUserAuthorizedPermission(Permission.Microphone))
// //         {
// //             micPermissionGranted = true;
// //             message = "Click button to recognize speech";
// //         }
// // #elif PLATFORM_IOS
// //         if (!micPermissionGranted && Application.HasUserAuthorization(UserAuthorization.Microphone))
// //         {
// //             micPermissionGranted = true;
// //             message = "Click button to recognize speech";
// //         }
// // #endif
//         lock (threadLocker)
//         {
//             for (int i = 0; i < numberOfMicrophones; i++)
//             {
//                 if (_outputTexts[i] != null)
//                 {
//                     _outputTexts[i].text = messages[i];
//                 }
//             }
//         }
//         
//         for (int i = 0; i < numberOfMicrophones; i++)
//         {
//             var device = Microphone.devices[i];
//             if (Microphone.IsRecording(device) && recognitionStarted == true)
//             {
//                 int pos = Microphone.GetPosition(device);
//                 int diff = pos - lastSample;
//
//                 if (diff > 0)
//                 {
//                     float[] samples = new float[diff * audioSources[i].clip.channels];
//                     audioSources[i].clip.GetData(samples, lastSample);
//                     byte[] ba = ConvertAudioClipDataToInt16ByteArray(samples);
//                     if (ba.Length != 0)
//                     {
//                         pushStream.Write(ba);
//                     }
//                 }
//                 lastSample = pos;
//             }
//         }
//     }


public async void RecognizeSpeech()
{
    if (recognizer == null)
    {
        Debug.LogError("Recognizer is not initialized. in RecognizeSpeech");
        return;
    }
    
    if (recognitionStarted)
    {
        await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(true);
        Debug.Log("Continuous recognition stopped.");

        for (int i = 0; i < numberOfMicrophones; i++)
        {
            var device = Microphone.devices[i];
            if (MicrophoneUtils.IsRecording(device))
            {
                Debug.Log("MicrophoneUtils.End: " + device);
                MicrophoneUtils.End(device);
            }
        }

        lock (threadLocker)
        {
            recognitionStarted = false;
            Debug.Log("RecognitionStarted: " + recognitionStarted.ToString());
        }
    }
    else
    {
        for (int i = 0; i < numberOfMicrophones; i++)
        {
            var device = Microphone.devices[i];
            if (!MicrophoneUtils.IsRecording(device))
            {
                Debug.Log("MicrophoneUtils.Start: " + device);
                audioSources[i].clip = MicrophoneUtils.Start(device, true, 200, 16000);
                Debug.Log("audioSource.clip channels: " + audioSources[i].clip.channels);
                Debug.Log("audioSource.clip frequency: " + audioSources[i].clip.frequency);
            }
        }

        await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
        Debug.Log("Continuous recognition started.");
        lock (threadLocker)
        {
            recognitionStarted = true;
            Debug.Log("RecognitionStarted: " + recognitionStarted.ToString());
        }
    }
}

void FixedUpdate()
{
    // ...

    for (int i = 0; i < numberOfMicrophones; i++)
    {
        var device = Microphone.devices[i];
        if (MicrophoneUtils.IsRecording(device) && recognitionStarted == true)
        {
            int pos = Microphone.GetPosition(device);
            int diff = pos - lastSample;

            if (diff > 0)
            {
                float[] samples = new float[diff * audioSources[i].clip.channels];
                audioSources[i].clip.GetData(samples, lastSample);
                byte[] ba = ConvertAudioClipDataToInt16ByteArray(samples);
                if (ba.Length != 0)
                {
                    pushStream.Write(ba);
                }
            }
            lastSample = pos;
        }
    }
}
    
    void LateUpdate()
    {
        // 音声波形の描画
        for (int i = 0; i < numberOfMicrophones; i++)
        {
            DrawWaveform(i);
        }
    }
    
    void DrawWaveform(int microphoneIndex)
    {
        int offset = microphoneIndex * samplesPerMicrophone;
        for (int i = 0; i < samplesPerMicrophone; i++)
        {
            Vector3 position = new Vector3((float)i / (samplesPerMicrophone - 1), samples[offset + i], 0);
            lineRenderers[microphoneIndex].SetPosition(i, position);
        }
    }
}


public class MicrophoneUtils
{
    private static Dictionary<string, RecordingParameters> ongoingRecordings =
        new Dictionary<string, RecordingParameters>();

    public static AudioClip Start(
        string deviceName,
        bool loop,
        int lengthSec,
        int frequency)
    {

        if (IsRecording(deviceName))
        {
            if (!ongoingRecordings.ContainsKey(deviceName))
            {
                Debug.LogError(
                    "Something is using Microphone class directly to record, please change it to use MicrophoneUtils instead!");
                End(deviceName);
                return StartNewRecording();
            }

            var ongoingRecording = ongoingRecordings[deviceName];
            if (loop != ongoingRecording.loop
                || lengthSec != ongoingRecording.lengthSec
                || frequency != ongoingRecording.frequency)
            {
                Debug.LogWarningFormat("MicUtils: attempting to record same device from another place " +
                                       "but recording params don't quite match:\ncurrent: {0}\nrequested: {1}",
                    ongoingRecording,
                    new RecordingParameters { loop = loop, lengthSec = lengthSec, frequency = frequency });
                //todo: possibly somehow convert audioclip data to suit desired parameters if that becomes a problem,
                //      for now just return the clip with first specified parameters
            }

            ongoingRecording.users++;
            Debug.LogFormat("Microphone {0} now has {1} users", deviceName, ongoingRecording.users);

            return ongoingRecording.audioClip;
        }

        return StartNewRecording();


        AudioClip StartNewRecording()
        {
            var audioClip = Microphone.Start(deviceName, loop, lengthSec, frequency);
            var parameters = new RecordingParameters
            {
                loop = loop,
                lengthSec = lengthSec,
                frequency = frequency,
                audioClip = audioClip,
                users = 1
            };
            ongoingRecordings.Add(deviceName, parameters);
            Debug.LogFormat("Started the '{0}' input device: {1}", deviceName, parameters);
            return audioClip;
        }
    }

    public static bool IsRecording(string deviceName)
    {
        return Microphone.IsRecording(deviceName);
    }

    /// How many places are using this input source right now
    public static int GetUsersRecording(string deviceName)
    {
        if (!IsRecording(deviceName))
        {
            return 0;
        }

        if (!ongoingRecordings.ContainsKey(deviceName))
        {
            Debug.LogErrorFormat("Someone's recording '{0}' without MicUtils knowing!", deviceName);
            return -1;
        }

        return ongoingRecordings[deviceName].users;
    }

    public static void End(string deviceName)
    {
        if (!IsRecording(deviceName))
        {
            return;
        }

        if (!ongoingRecordings.ContainsKey(deviceName))
        {
            JustStop();
            return;
        }

        var ongoingRecording = ongoingRecordings[deviceName];
        if (ongoingRecording.users <= 1)
        {
            ongoingRecordings.Remove(deviceName);
            JustStop();
        }
        else
        {
            ongoingRecording.users--;
            Debug.LogFormat("Microphone {0} now has {1} users", deviceName, ongoingRecording.users);
        }


        void JustStop()
        {
            Debug.LogFormat("Stopping the '{0}' input device", deviceName);
            Microphone.End(deviceName);
        }
    }
}




public class RecordingParameters
{
    public bool loop;
    public int lengthSec;
    public int frequency;
    public int users;
    public AudioClip audioClip;
 
    public override string ToString()
    {
        return $"{nameof(loop)}: {loop}, {nameof(lengthSec)}: {lengthSec}, {nameof(frequency)}: {frequency}, {nameof(users)}: {users}";
    }
}