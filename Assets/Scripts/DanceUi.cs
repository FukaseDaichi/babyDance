using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BabyDance
{
    /// <summary>実行時に uGUI を組み立てる。開く / 再生・停止 / BPM / ダンス切替 / メッセージ。</summary>
    public sealed class DanceUi : MonoBehaviour
    {
        private const float MinBpm = 60f;
        private const float MaxBpm = 200f;

        public DancePlayer player;

        private Font _font;
        private Button _playButton;
        private Text _playLabel;
        private Text _bpmLabel;
        private Text _message;
        private Slider _bpmSlider;
        private Dropdown _danceDropdown;

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildEventSystem();
            var panel = BuildCanvasAndPanel();

            AddButton(panel, "Open music", player.Open);
            _playButton = AddButton(panel, "Play", player.TogglePlay);
            _playLabel = _playButton.GetComponentInChildren<Text>();

            _bpmLabel = AddText(panel, "");
            _bpmSlider = AddSlider(panel, MinBpm, MaxBpm, (float)player.Bpm, v => player.SetBpm(v));

            var names = new List<string>();
            for (var i = 0; i < player.DanceCount; i++) names.Add(player.DanceName(i));
            _danceDropdown = AddDropdown(panel, names, player.SelectDance);

            _message = AddText(panel, "Open a music file to start");

            player.Changed += Refresh;
            player.Message += msg => _message.text = msg;
            Refresh();
        }

        private void Refresh()
        {
            _playButton.interactable = player.HasClip;
            _playLabel.text = player.IsPlaying ? "Stop" : "Play";
            _bpmLabel.text = $"BPM {player.Bpm:0}";
            _bpmSlider.SetValueWithoutNotify((float)player.Bpm);
            _danceDropdown.SetValueWithoutNotify(player.driver.CurrentIndex);
        }

        private static void BuildEventSystem()
        {
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private static RectTransform BuildCanvasAndPanel()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panelGo.transform.SetParent(canvasGo.transform, false);
            panelGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            var rect = panelGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(16f, 16f);
            rect.sizeDelta = new Vector2(320f, 0f);
            var layout = panelGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            panelGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        private static void Attach(GameObject go, Transform parent, float height)
        {
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = height;
        }

        private void ApplyFont(GameObject root)
        {
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                text.font = _font;
                text.color = Color.white;
                text.fontSize = 18;
            }
        }

        private Button AddButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = DefaultControls.CreateButton(new DefaultControls.Resources());
            Attach(go, parent, 40f);
            go.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.9f);
            go.GetComponentInChildren<Text>().text = label;
            ApplyFont(go);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }

        private Text AddText(Transform parent, string content)
        {
            var go = DefaultControls.CreateText(new DefaultControls.Resources());
            Attach(go, parent, 24f);
            var text = go.GetComponent<Text>();
            text.text = content;
            ApplyFont(go);
            return text;
        }

        private Slider AddSlider(Transform parent, float min, float max, float value, UnityEngine.Events.UnityAction<float> onChanged)
        {
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            Attach(go, parent, 24f);
            var slider = go.GetComponent<Slider>();
            slider.fillRect.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.9f);
            slider.handleRect.GetComponent<Image>().color = Color.white;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        private Dropdown AddDropdown(Transform parent, List<string> options, UnityEngine.Events.UnityAction<int> onChanged)
        {
            var go = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            Attach(go, parent, 40f);
            go.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f);
            var dropdown = go.GetComponent<Dropdown>();
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            ApplyFont(go);
            dropdown.onValueChanged.AddListener(onChanged);
            return dropdown;
        }
    }
}
