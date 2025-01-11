using UnityEngine;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Translation;
using System.Threading.Tasks;

public class SpeechTranslator : MonoBehaviour
{
    private string subscriptionKey = "<Your Azure SpeechService's Speech Key here>";
    private string region = "<Your Azure SpeechService's Region here>";
    private TranslationRecognizer recognizer;

    private async void Start()
    {
        var config = SpeechTranslationConfig.FromSubscription(subscriptionKey, region);
        string fromLanguage = "ja-JP";
        config.SpeechRecognitionLanguage = fromLanguage;
        config.AddTargetLanguage("en");
        // const string GermanVoice = "de-DE-AmalaNeural";
        // config.VoiceName = GermanVoice;

        recognizer = new TranslationRecognizer(config);
        recognizer.Recognizing += OnRecognizing;
        recognizer.Recognized += OnRecognized;
        recognizer.Synthesizing += OnSynthesizing;
        recognizer.Canceled += OnCanceled;
        recognizer.SessionStarted += OnSessionStarted;
        recognizer.SessionStopped += OnSessionStopped;

        await recognizer.StartContinuousRecognitionAsync();
    }

    private void OnRecognizing(object sender, TranslationRecognitionEventArgs e)
    {
        Debug.Log($"RECOGNIZING in '{e.Result.Text}': Text={e.Result.Text}");
        foreach (var element in e.Result.Translations)
        {
            Debug.Log($" TRANSLATING into '{element.Key}': {element.Value}");
        }
    }

    private void OnRecognized(object sender, TranslationRecognitionEventArgs e)
    {
        if (e.Result.Reason == ResultReason.TranslatedSpeech)
        {
            Debug.Log($"Final result: Reason: {e.Result.Reason}, recognized text: {e.Result.Text}.");
            foreach (var element in e.Result.Translations)
            {
                Debug.Log($" TRANSLATING into '{element.Key}': {element.Value}");
            }
        }
    }

    private void OnSynthesizing(object sender, TranslationSynthesisEventArgs e)
    {
        var audio = e.Result.GetAudio();
        Debug.Log(audio.Length != 0 ? $"AudioSize: {audio.Length}" : $"AudioSize: {audio.Length} (end of synthesis data)");
    }

    private void OnCanceled(object sender, TranslationRecognitionCanceledEventArgs e)
    {
        Debug.LogError($"Recognition canceled. Reason: {e.Reason}; ErrorDetails: {e.ErrorDetails}");
    }

    private void OnSessionStarted(object sender, SessionEventArgs e)
    {
        Debug.Log("Session started event.");
    }

    private void OnSessionStopped(object sender, SessionEventArgs e)
    {
        Debug.Log("Session stopped event.");
    }

    private async void OnDestroy()
    {
        await recognizer.StopContinuousRecognitionAsync();
        recognizer.Dispose();
    }
}