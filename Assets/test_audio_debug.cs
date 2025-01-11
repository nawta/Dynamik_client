using System.Collections;
using System.Collections.Generic;
using UnityEngine;


using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;

public class test_audio_debug : MonoBehaviour
{

    
        static string speechKey = "fd826178474f4ce1865dc76e4611319a";
        static string speechRegion = "japaneast";
        // This example requires environment variables named "SPEECH_KEY" and "SPEECH_REGION"

        
        static void OutputSpeechRecognitionResult(TranslationRecognitionResult translationRecognitionResult)
        {
            Debug.Log(translationRecognitionResult.Reason);
            switch (translationRecognitionResult.Reason)
            {
                case ResultReason.TranslatedSpeech:
                    Debug.Log($"RECOGNIZED: Text={translationRecognitionResult.Text}");
                    foreach (var element in translationRecognitionResult.Translations)
                    {
                        Debug.Log($"TRANSLATED into '{element.Key}': {element.Value}");
                    }

                    break;
                case ResultReason.NoMatch:
                    Debug.Log($"NOMATCH: Speech could not be recognized.");
                    break;
                case ResultReason.Canceled:
                    var cancellation = CancellationDetails.FromResult(translationRecognitionResult);
                    Debug.Log($"CANCELED: Reason={cancellation.Reason}");

                    if (cancellation.Reason == CancellationReason.Error)
                    {
                        Debug.Log($"CANCELED: ErrorCode={cancellation.ErrorCode}");
                        Debug.Log($"CANCELED: ErrorDetails={cancellation.ErrorDetails}");
                        Debug.Log($"CANCELED: Did you set the speech resource key and region values?");
                    }

                    break;
            }
        }
        
        public static async Task TranslationContinuousRecognitionAsync()
        {
            // Creates an instance of a speech translation config with specified subscription key and service region.
            // Replace with your own subscription key and service region (e.g., "westus").
            var config = SpeechTranslationConfig.FromSubscription(speechKey, speechRegion);

            // Sets source and target languages.
            string fromLanguage = "en-US";
            config.SpeechRecognitionLanguage = fromLanguage;
            config.AddTargetLanguage("de");

            // Sets voice name of synthesis output.
            const string GermanVoice = "de-DE-AmalaNeural";
            config.VoiceName = GermanVoice;
            // Creates a translation recognizer using microphone as audio input.
            using (var recognizer = new TranslationRecognizer(config))
            {
                // Subscribes to events.
                recognizer.Recognizing += (s, e) =>
                {
                    Console.WriteLine($"RECOGNIZING in '{fromLanguage}': Text={e.Result.Text}");
                    foreach (var element in e.Result.Translations)
                    {
                        Console.WriteLine($"    TRANSLATING into '{element.Key}': {element.Value}");
                    }
                };

                recognizer.Recognized += (s, e) =>
                {
                    if (e.Result.Reason == ResultReason.TranslatedSpeech)
                    {
                        Console.WriteLine($"\nFinal result: Reason: {e.Result.Reason.ToString()}, recognized text in {fromLanguage}: {e.Result.Text}.");
                        foreach (var element in e.Result.Translations)
                        {
                            Console.WriteLine($"    TRANSLATING into '{element.Key}': {element.Value}");
                        }
                    }
                };

                recognizer.Synthesizing += (s, e) =>
                {
                    var audio = e.Result.GetAudio();
                    Console.WriteLine(audio.Length != 0
                        ? $"AudioSize: {audio.Length}"
                        : $"AudioSize: {audio.Length} (end of synthesis data)");
                };

                recognizer.Canceled += (s, e) =>
                {
                    Console.WriteLine($"\nRecognition canceled. Reason: {e.Reason}; ErrorDetails: {e.ErrorDetails}");
                };

                recognizer.SessionStarted += (s, e) =>
                {
                    Console.WriteLine("\nSession started event.");
                };

                recognizer.SessionStopped += (s, e) =>
                {
                    Console.WriteLine("\nSession stopped event.");
                };

                // Starts continuous recognition. Uses StopContinuousRecognitionAsync() to stop recognition.
                Console.WriteLine("Say something...");
                await recognizer.StartContinuousRecognitionAsync();

                do
                {
                    Console.WriteLine("Press Enter to stop");
                } while (Console.ReadKey().Key != ConsoleKey.Enter);

                // Stops continuous recognition.
                await recognizer.StopContinuousRecognitionAsync();
            }
        }
        
        async static Task Main()
        {
            
            
            
            var speechTranslationConfig = SpeechTranslationConfig.FromSubscription(speechKey, speechRegion);
            speechTranslationConfig.SpeechRecognitionLanguage = "Ja-jp";
            speechTranslationConfig.AddTargetLanguage("en");

            using var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
            using var translationRecognizer = new TranslationRecognizer(speechTranslationConfig, audioConfig);


            Debug.Log("Speak into your microphone.");
            var translationRecognitionResult = await translationRecognizer.RecognizeOnceAsync();
            OutputSpeechRecognitionResult(translationRecognitionResult);
        }

        
        static async Task MainContinuous()
        {
            await TranslationContinuousRecognitionAsync();
        }
    
    // Start is called before the first frame update
    void Start()
    {
            
        Debug.Log("aaaaa");
        MainContinuous();
    }

    // Update is called once per frame
    void Update()
    {

    }


}
