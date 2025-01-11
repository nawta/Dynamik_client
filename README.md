# HA_2022_teamC

### 注意事項
以下のディレクトリは100MBを超えたので[ここ](https://drive.google.com/drive/folders/1JITQ1qo_dNusZ1bEZWdRxj7GDUrXKtGK?usp=sharing)に別途格納している  

- Assets/Fonts
- Assets/SpeechSDK




## Assets/Scripts/の内容
- Fullscreen.cs
ゲームビューを勝手にフルスクリーンにするやつ．実験の時に使おう

- testAudio.cs
なんかマルチパーソンに対応しようとマイク足したりしまくったらバグった．
- testAudiov2.cs
デバッグコンソールにASR認識結果とその翻訳結果が表示される．デバッグ用．
- testAudiov3.cs
outputTextに翻訳結果が表示される． 一回サーバに翻訳結果を飛ばしてサーバの方で品詞分解したりしてる
- testAudiov3_2.cs
これも同じくoutputTextに翻訳結果が表示される． APIには飛ばしていない．デモ用．
