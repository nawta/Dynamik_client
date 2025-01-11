using UnityEngine;
using UnityEngine.UI;
using Microsoft.CognitiveServices.Speech;
using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.CognitiveServices.Speech.Audio;
using System.IO;
using System.Linq;
using TMPro;
using NetMQ;
using NetMQ.Sockets;
using System.Text.RegularExpressions;
using Unity.VisualScripting;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif
#if PLATFORM_IOS
using UnityEngine.iOS;
using System.Collections;
#endif

public class TestAudiov4 : MonoBehaviour
{
    private bool micPermissionGranted = false;

    [SerializeField]
    public TextMeshProUGUI _outputText = null;

    SpeechRecognizer recognizer;
    AudioConfig audioConfig;
    PushAudioInputStream pushStream;
    SpeechConfig speechConfig;
    private object threadLocker = new object();
    private bool recognitionStarted = false;
    private string message;
    int lastSample = 0;
    AudioSource audioSource;

    private List<float> audioVolumes = new List<float>(); // 音声の大きさを保存するリスト
    private const float VOLUME_THRESHOLD = 0.25f; // 音声の大きさの閾値
    private List<int> wordEndIndices = new List<int>(); // 各語の終了インデックスを保存するリスト
    private List<string> newWords = new List<string>(); // 認識された新しい語を保存するリスト

    private string lastRecognizedText = ""; // 最後に認識されたテキスト

    private float recognitionInterval = 2.0f; // 4秒ごとに認識を切り替えるための間隔
    private float timer = 0.0f;

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
    
    
    private void RecognizingHandler(object sender, SpeechRecognitionEventArgs e)
    {
        lock (threadLocker)
        {
            if (e.Result.Reason == ResultReason.RecognizingSpeech)
            {
                string currentText = e.Result.Text;
                // message = currentText;

                // 新しい語が認識されたか確認
                if (string.IsNullOrEmpty(lastRecognizedText) || currentText.Length > lastRecognizedText.Length)
                {
                    string newWordSegment; //前のタイムスタンプ時に取得した認識文字列と今のタイムスタンプで取得した文字列の差分（つまり新しい発話した文章分)
                    if (string.IsNullOrEmpty(lastRecognizedText))
                    {   //lastRecognizedTextがない時，つまり話始めの文頭.
                        newWordSegment = currentText.Trim();
                    }
                    else
                    {   //lastRecognizedTextがある時，つまり話の途中．newWordSegmentには'is'とか'gonna'とか'is about'とか'is oppor'(tunityまで認識されていない)が入る．
                        newWordSegment = currentText.Substring(lastRecognizedText.Length).Trim();
                    }

                    //newWordSegmentに複数語が含まれている際，分割する．'is about' -> 'is', 'about'．それぞれのタイムスタンプは線形に分割．
                    if (!string.IsNullOrEmpty(newWordSegment))
                    {
                        string[] newWordsArray = newWordSegment.Split(' ');
                        int previousIndex = wordEndIndices.Count > 0 ? wordEndIndices[wordEndIndices.Count - 1] : 0;
                        int k = newWordsArray.Length;
                        int interval = (audioVolumes.Count - previousIndex) / k;

                        for (int i = 0; i < newWordsArray.Length; i++)
                        {
                            if (!string.IsNullOrEmpty(newWordsArray[i]))
                            {
                                wordEndIndices.Add(previousIndex + (i + 1) * interval);
                                newWords.Add(newWordsArray[i]);
                                Debug.Log($"New Word Recognized: {newWordsArray[i]}, at index: {previousIndex + (i + 1) * interval}");
                            }
                        }
                    }
                }
                //TODO: 同時翻訳中にも字幕の大きさ反映したい場合はこの辺.CorrectNewWordsAndIndicesを同時通訳用にいじるべきかも.
                CorrectNewWordsAndIndices(currentText);
                Debug.Log("ST1. " + audioVolumes.Count);
                Debug.Log("ST1. " + string.Join(",", wordEndIndices.Select(n => n.ToString())));
                Debug.Log("ST1. " + string.Join(",", newWords));
                message = HighlightEmphasizedWords(audioVolumes, newWords);
                //////////////////////////////////
                lastRecognizedText = currentText;
            }
        }
    }

    private void RecognizedHandler(object sender, SpeechRecognitionEventArgs e)
    {
        lock (threadLocker)
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech)
            {
                // message = e.Result.Text;
                Debug.Log("Recognized: " + message);
                Debug.Log("1. " + audioVolumes.Count);
                Debug.Log("1. " + string.Join(",", wordEndIndices.Select(n => n.ToString())));
                Debug.Log("1. " + string.Join(",", newWords));
                CorrectNewWordsAndIndices(e.Result.Text); // newWordsには単語も途中分割されまくった状態で入っているので，統合（ex. This, is, my, import, ant, budget)
                message = HighlightEmphasizedWords(audioVolumes, newWords); // 強調された単語をハイライトする
                lastRecognizedText = ""; // 完全な認識が終わったのでリセット
                audioVolumes.Clear(); // 認識された後にaudioVolumesをクリア
                wordEndIndices.Clear(); // 認識された後にwordEndIndicesをクリア
                newWords.Clear(); // 認識された単語をクリア
            }
            else if (e.Result.Reason == ResultReason.NoMatch)
            {
                Debug.Log("No speech could be recognized.");
            }
        }
    }
    
    //RecognizedHandler内から呼び出される．内部でnewWordsとwordEndIndicesを変更してる（破壊的）. newWordsには単語も途中分割されまくった状態で入っているので，統合（ex. This, is, my, import, ant, budget)する関数.
    private void CorrectNewWordsAndIndices(string recognizedText)
    {
        string[] recognizedWords = recognizedText.Split(' ');
        List<string> correctedNewWords = new List<string>();
        List<int> correctedWordEndIndices = new List<int>();

        Debug.Log("2. " + string.Join(",", wordEndIndices.Select(n => n.ToString())));
        Debug.Log("2. " + string.Join(",", newWords));
        Debug.Log("2. " + string.Join(",", recognizedWords));
  
        int wordIndex = 0;
        for (int i = 0; i < newWords.Count; i++)
        {
            string newWord = newWords[i];
            // Debug.Log("out: " + newWord);

            // 複数に分割された単語を統合
            while (wordIndex < recognizedWords.Length && !newWord.Equals(recognizedWords[wordIndex].Trim(',', '，', '.' ,'．',':', ';'), StringComparison.OrdinalIgnoreCase))
            {
                // Debug.Log("inside while: " + newWord + ". ........." + recognizedWords[wordIndex] + ".........." + wordIndex.ToString());
                
                i++;
                if (i < newWords.Count)
                {
                    newWord += newWords[i];
                }
                else
                {
                    break;
                }
            }

            correctedNewWords.Add(recognizedWords[wordIndex]); //普通にコンマとかついてるしキャピたらイズされてるこっちの方がいい．
            if (i < wordEndIndices.Count)
            {
                correctedWordEndIndices.Add(wordEndIndices[i]);
            }
            wordIndex++;
        }

        newWords = correctedNewWords;
        wordEndIndices = correctedWordEndIndices;
        
        Debug.Log("3. " + string.Join(",", wordEndIndices.Select(n => n.ToString())));
        Debug.Log("3. " + string.Join(",", newWords));
    }

    private void CanceledHandler(object sender, SpeechRecognitionCanceledEventArgs e)
    {
        lock (threadLocker)
        {
            message = e.ErrorDetails.ToString();
            Debug.Log("Canceled: " + message);
        }
    }

    public async void ToggleRecognition()
    {
        if (recognitionStarted)
        {
            await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(true);

            if (Microphone.IsRecording(Microphone.devices[0]))
            {
                Debug.Log("Microphone.End: " + Microphone.devices[0]);
                Microphone.End(null);
                lastSample = 0;
                audioVolumes.Clear(); // マイクの認識を停止したらaudioVolumesをクリア
                wordEndIndices.Clear(); // マイクの認識を停止したらwordEndIndicesもクリア
                newWords.Clear();
                // Debug.Log(audioVolumes);
                // Debug.Log(wordEndIndices);
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

    private float GetAverageVolume(float[] data)
    {
        float sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            sum += Mathf.Abs(data[i]);
        }
        return sum / data.Length;
    }

    private string HighlightEmphasizedWords(List<float> volumes, List<string> newWords)
    {
        List<float> avgVolumes = new List<float>();
        string resultText = "";

        for (int i = 0; i < newWords.Count; i++)
        {
            if (i >= wordEndIndices.Count)
                break;

            int startIndex = (i == 0) ? 0 : wordEndIndices[i - 1];
            int endIndex = wordEndIndices[i];

            float avgVolume = 0;
            for (int j = startIndex; j < endIndex; j++)
            {
                avgVolume += volumes[j];
            }
            // avgVolume /= (endIndex - startIndex);
            Debug.Log(avgVolume);

            // 語ごとの平均音量を記録
            avgVolumes.Add(avgVolume);
        }

        // 平均音量からsizeを決める
        for (int i = 0; i < newWords.Count; i++)
        {
            float size;
            if (i == 0)
            {
                size = 90; // 文頭の語は特別にサイズを90に固定
            }
            else
            {
                size = Mathf.Clamp(avgVolumes[i], 90, 200); // 音量を元にサイズを決定
            }
            resultText += $"<size={size}>{newWords[i]}</size> ";
        }

        return resultText;
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
            message = "Press Space to recognize speech";
#endif
            speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
            speechConfig.SpeechRecognitionLanguage = "en-US";

            pushStream = AudioInputStream.CreatePushStream();
            audioConfig = AudioConfig.FromStreamInput(pushStream);
            recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            recognizer.Recognizing += RecognizingHandler;
            recognizer.Recognized += RecognizedHandler;
            recognizer.Canceled += CanceledHandler;

            foreach (var device in Microphone.devices)
            {
                Debug.Log("DeviceName: " + device);
            }
            audioSource = GameObject.Find("MyAudioSource0").GetComponent<AudioSource>();
        }
    }

    void Disable()
    {
        recognizer.Recognizing -= RecognizingHandler;
        recognizer.Recognized -= RecognizedHandler;
        recognizer.Canceled -= RecognizedHandler;
        pushStream.Close();
        recognizer.Dispose();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ToggleRecognition();
        }

#if PLATFORM_ANDROID
        if (!micPermissionGranted && Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            micPermissionGranted = true;
            message = "Press Space to recognize speech";
        }
#elif PLATFORM_IOS
        if (!micPermissionGranted && Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            micPermissionGranted = true;
            message = "Press Space to recognize speech";
        }
#endif

        lock (threadLocker)
        {
            if (_outputText != null)
            {
                _outputText.text = message;
            }
        }

        if (Microphone.IsRecording(Microphone.devices[0]) && recognitionStarted)
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
                    audioVolumes.AddRange(GetAverageVolumes(samples)); // 音声の大きさを保存
                }
            }
            lastSample = pos;
        }
    }

    private IEnumerable<float> GetAverageVolumes(float[] samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            yield return Mathf.Abs(samples[i]);
        }
    }
}
