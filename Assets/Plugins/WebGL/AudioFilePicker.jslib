// ブラウザの <input type="file"> を開き、選択されたファイルの名前と blob URL を
// "ファイル名\nblob URL" の 1 文字列で AudioLoader.OnFileChosen へ SendMessage する。
// キャンセル時は change が発火しないため何も返さない。
var BabyDanceAudioFilePicker = {
    BabyDance_OpenAudioFile: function (gameObjectNamePtr, methodNamePtr) {
        var gameObjectName = UTF8ToString(gameObjectNamePtr);
        var methodName = UTF8ToString(methodNamePtr);
        var id = 'babydance-audio-file-picker';

        var stale = document.getElementById(id);
        if (stale) document.body.removeChild(stale);

        var input = document.createElement('input');
        input.id = id;
        input.type = 'file';
        input.accept = 'audio/*,.mp3,.wav,.ogg';
        input.style.display = 'none';
        input.onchange = function () {
            var file = input.files[0];
            if (file) SendMessage(gameObjectName, methodName, file.name + '\n' + URL.createObjectURL(file));
            document.body.removeChild(input);
        };
        document.body.appendChild(input);
        input.click();
    }
};
mergeInto(LibraryManager.library, BabyDanceAudioFilePicker);
