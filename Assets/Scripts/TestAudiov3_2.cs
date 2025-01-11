//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//
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
using System.Text.RegularExpressions;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif
#if PLATFORM_IOS
using UnityEngine.iOS;
using System.Collections;
#endif
public class TestAudiov3_2 : MonoBehaviour
{
    private bool micPermissionGranted = false;
    
    [SerializeField]
    public TextMeshProUGUI _outputText = null;

    TranslationRecognizer recognizer;
    AudioConfig audioConfig;
    PushAudioInputStream pushStream;
    SpeechTranslationConfig translationConfig;
    private object threadLocker = new object();
    private bool recognitionStarted = false;
    private string message;
    int lastSample = 0;
    AudioSource audioSource;
    
    private float recognitionInterval = 2.0f; // 4秒ごとに認識を切り替えるための間隔
    private float timer = 0.0f;
    private float interval = 0.5f; // 0.5秒のインターバル
    private bool hasIntervalPassed = false; // インターバルが経過したかどうかのフラグ

    
    // コンパイル済み正規表現をクラスレベルで定義
    private static readonly Regex regex_eos = new Regex("([。．！？])", RegexOptions.Compiled);
    private static readonly Regex regex_punctuation = new Regex(@"([。．、，！？])[^。．、，！？]*$", RegexOptions.Compiled);

    

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
            // message = "recognizing";
            if(hasIntervalPassed){
                foreach (var element in e.Result.Translations)
                {
                    message = InsertNewlines(element.Value); //ここは話している最中の未完成の文章に対応する
                    Debug.Log("RecognizingHandler: " + message);
                }
            }            
        }
    }
    private void RecognizedHandler(object sender, TranslationRecognitionEventArgs e)
    {
        lock (threadLocker)
        {
            message = "";
            foreach (var element in e.Result.Translations)
            {
                message = InsertNewlines(element.Value); //ここは話し終わったあとの完成形の文章に対応する. ここにQueryText2APIを挟むと要約タスクが挟めたりする
                Debug.Log("RecognizedHandler: " + message);
            }
        }
    }
    private void CanceledHandler(object sender, TranslationRecognitionCanceledEventArgs e)
    {
        lock (threadLocker)
        {
            message = e.ErrorDetails.ToString();
            Debug.Log("CanceledHandler: " + message);
        }
    }
    
    private void ExecutePeriodicTask()
    {
        // Interval秒ごとに行いたい処理をここに記述
        // フラグを反転させる
        hasIntervalPassed = !hasIntervalPassed;

        // 反転後のフラグの状態をログに出力
        Debug.Log("hasIntervalPassed is now: " + hasIntervalPassed);
        
    }


    public async void RecognizeSpeech()
    {
        if (recognitionStarted)
        {
            await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(true);
            if (Microphone.IsRecording(Microphone.devices[0]))
            {
                Debug.Log("Microphone.End: " + Microphone.devices[0]);
                Microphone.End(null);
                lastSample = 0;
            }
            lock (threadLocker)
            {
                recognitionStarted = false;
                Debug.Log("RecognitionStarted: " + recognitionStarted.ToString());
            }
        }
        else
        {
            if (!Microphone.IsRecording(Microphone.devices[0]))
            {
                Debug.Log("Microphone.Start: " + Microphone.devices[0]);
                audioSource.clip = Microphone.Start(Microphone.devices[0], true, 200, 16000);
                Debug.Log("audioSource.clip channels: " + audioSource.clip.channels);
                Debug.Log("audioSource.clip frequency: " + audioSource.clip.frequency);
            }
            await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
            lock (threadLocker)
            {
                recognitionStarted = true;
                Debug.Log("RecognitionStarted: " + recognitionStarted.ToString());
            }
        }
        // 問答無用でマイクの音声を拾う
        // if (!Microphone.IsRecording(Microphone.devices[0]))
        //     {
        //         Debug.Log(audioSource);
        //         Debug.Log("Microphone.Start: " + Microphone.devices[0]);
        //         audioSource.clip = Microphone.Start(Microphone.devices[0], true, 200, 16000);
        //         Debug.Log("audioSource.clip channels: " + audioSource.clip.channels);
        //         Debug.Log("audioSource.clip frequency: " + audioSource.clip.frequency);
        //     }
        //
        // await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
        // lock (threadLocker)
        //     {
        //         recognitionStarted = true;
        //         Debug.Log("RecognitionStarted: " + recognitionStarted.ToString());
        //     }
    }
    
    public string QueryText2API(string text)
    {
        string res = "";
        using (var client = new RequestSocket("tcp://localhost:5557"))
        {
            client.SendFrame(text);
 
            res = client.ReceiveFrameString();
            Debug.Log("From Server: " + res);
        
            Debug.Log(" ");
            Debug.Log("Press any key to exit...");
            Console.ReadKey();
        }
        return res;
    }
    
    static string InsertNewlines(string text)
    {
        // コンパイル済み正規表現を使って置換
        // 句読点の時点で改行を入れる
        return regex_eos.Replace(text, "$1\n");
    }
    
    static string RemoveAfterLastPunctuation(string text)
    {
        // 正規表現で最後の句読点以降を削除
        return regex_punctuation.Replace(text, "$1");
    }
    
    void Start()
    {
        string speechKey = "<Your Azure SpeechService's Speech Key here>";
        string speechRegion = "<Your Azure SpeechService's Region here>";
        Debug.Log(speechKey);
        Debug.Log(speechRegion);
        if (_outputText == null)
        {
            UnityEngine.Debug.LogError("outputText property is null! Assign a UI Text element to it.");
        }
        else
        {
            // Continue with normal initialization, Text and Button objects are present.
#if PLATFORM_ANDROID
            // Request to use the microphone, cf.
            // https://docs.unity3d.com/Manual/android-RequestingPermissions.html
            message = "Waiting for mic permission";
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
            }
#elif PLATFORM_IOS
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                Application.RequestUserAuthorization(UserAuthorization.Microphone);
            }
#else
            micPermissionGranted = true;
            message = "Click button to recognize speech";
#endif
            translationConfig = SpeechTranslationConfig.FromSubscription(speechKey, speechRegion);
            var fromLanguage = "en-US";//"en-US";//"ja-JP";//"ko-KR";//"zh-CN";
            var toLanguages = new List<string> { "ja" };
            translationConfig.SpeechRecognitionLanguage = fromLanguage;
            toLanguages.ForEach(translationConfig.AddTargetLanguage);
            
			pushStream = AudioInputStream.CreatePushStream();
            audioConfig = AudioConfig.FromStreamInput(pushStream);
            recognizer = new TranslationRecognizer(translationConfig, audioConfig);
            
            recognizer.Recognizing += RecognizingHandler;
            recognizer.Recognized += RecognizedHandler;
            recognizer.Canceled += CanceledHandler;
            
            foreach (var device in Microphone.devices)
            {
                Debug.Log("DeviceName: " + device);                
            }
            audioSource = GameObject.Find("MyAudioSource0").GetComponent<AudioSource>();
            RecognizeSpeech();
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
    void FixedUpdate()
    {
#if PLATFORM_ANDROID
        if (!micPermissionGranted && Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            micPermissionGranted = true;
            message = "Click button to recognize speech";
        }
#elif PLATFORM_IOS
        if (!micPermissionGranted && Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            micPermissionGranted = true;
            message = "Click button to recognize speech";
        }
#endif
        
        //もし何秒かずつに区切りたい場合(Re-translationではなくStreaming Translationにしたい場合)はここで調節．Re-translationであればここはコメントアウト．
        /////////////
        // timer += Time.fixedDeltaTime; // FixedUpdate の時間間隔に基づいてタイマーを更新します
        // if (timer >= recognitionInterval)
        // {
        //     timer = 0.0f;
        //     RecognizeSpeech(); // 2秒ごとに認識を切り替える
        // }
        /////////////
        
        
        
        lock (threadLocker)
        {
            if (_outputText != null)
            {
                _outputText.text = message;
                // Debug.Log("DeviceMessage: " + message); 
            }
        }
        
        
        if (Microphone.IsRecording(Microphone.devices[0]) && recognitionStarted == true)
        {
            int pos = Microphone.GetPosition(Microphone.devices[0]);
            int diff = pos - lastSample;
            if (diff > 0)
            {
                float[] samples = new float[diff * audioSource.clip.channels];
                audioSource.clip.GetData(samples, lastSample);
                byte[] ba = ConvertAudioClipDataToInt16ByteArray(samples);
                if (ba.Length != 0)
                {
                    pushStream.Write(ba);
                }
            }
            lastSample = pos;
        }
        
        // インターバルタイマーを更新
        timer += Time.fixedDeltaTime;
        if (timer >= interval)
        {
            timer = 0.0f;
            ExecutePeriodicTask();
        }
    }
}