# Dynamik client

### Notes
The following directories exceed 100MB and are stored separately [here](https://drive.google.com/drive/folders/1JITQ1qo_dNusZ1bEZWdRxj7GDUrXKtGK?usp=sharing):

- Assets/Fonts  
- Assets/SpeechSDK  

## Contents of Assets/Scripts/
- **Fullscreen.cs**  
  Automatically sets the game view to fullscreen. Intended for use during experiments.  

- **testAudio.cs**  
  It bugged out after attempting to support multi-person input by adding multiple microphones.  

- **testAudiov2.cs**  
  Displays ASR recognition results and their translations in the debug console. For debugging purposes.  

- **testAudiov3.cs**  
  Shows translation results in `outputText`. The results are sent to the server for additional processing, such as part-of-speech tagging.  

- **testAudiov3_2.cs**  
  Also displays translation results in `outputText`. However, it does not send data to the API. Designed for demo use.  
