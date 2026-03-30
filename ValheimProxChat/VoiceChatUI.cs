using System.Collections.Generic;
using UnityEngine;
using ValheimProxChat.Audio;

namespace ValheimProxChat
{
    /// <summary>
    /// Simple IMGUI-based overlay showing voice chat status:
    /// - Microphone status (transmitting indicator)
    /// - Speaking indicator icons above players
    /// </summary>
    public class VoiceChatUI : MonoBehaviour
    {
        private MicrophoneCapture _micCapture;
        private AudioPlaybackManager _playbackManager;

        private Texture2D _micOnTex;
        private Texture2D _micOffTex;
        private Texture2D _speakingTex;

        private GUIStyle _labelStyle;
        private bool _stylesInitialized;

        private void Start()
        {
            _micCapture = GetComponent<MicrophoneCapture>();
            _playbackManager = GetComponent<AudioPlaybackManager>();

            // Create simple colored textures for indicators
            _micOnTex = MakeColorTexture(4, 4, new Color(0.2f, 0.8f, 0.2f, 0.9f));
            _micOffTex = MakeColorTexture(4, 4, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            _speakingTex = MakeColorTexture(4, 4, new Color(0.3f, 0.9f, 0.3f, 0.8f));
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _labelStyle.normal.textColor = Color.white;
            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (!Plugin.IsVoiceChatActive()) return;

            InitStyles();
            DrawMicrophoneIndicator();

            if (Configuration.ShowSpeakingIndicator.Value)
            {
                DrawSpeakingIndicators();
            }
        }

        private void DrawMicrophoneIndicator()
        {
            if (_micCapture == null) return;

            float x = 10f;
            float y = Screen.height - 50f;
            float iconSize = 20f;

            // Background
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(new Rect(x - 4, y - 4, 160, iconSize + 8), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Mic icon (colored square)
            Texture2D icon = _micCapture.IsSpeaking ? _micOnTex : _micOffTex;
            GUI.DrawTexture(new Rect(x, y, iconSize, iconSize), icon);

            // Status text
            string status;
            if (!_micCapture.IsCapturing)
            {
                status = "No Mic";
                _labelStyle.normal.textColor = Color.red;
            }
            else if (_micCapture.IsSpeaking)
            {
                status = "Transmitting";
                _labelStyle.normal.textColor = Color.green;
            }
            else if (Configuration.PushToTalk.Value)
            {
                status = $"PTT [{Configuration.PushToTalkKey.Value.ToUpper()}]";
                _labelStyle.normal.textColor = Color.gray;
            }
            else
            {
                status = "Listening";
                _labelStyle.normal.textColor = Color.gray;
            }

            GUI.Label(new Rect(x + iconSize + 5, y, 130, iconSize), status, _labelStyle);
            _labelStyle.normal.textColor = Color.white;

            // Volume level bar
            if (_micCapture.IsCapturing)
            {
                float barX = x;
                float barY = y + iconSize + 2;
                float barW = 150f;
                float barH = 4f;

                GUI.color = new Color(0.2f, 0.2f, 0.2f, 0.7f);
                GUI.DrawTexture(new Rect(barX, barY, barW, barH), Texture2D.whiteTexture);

                float level = Mathf.Clamp01(_micCapture.CurrentLevel * 10f); // Scale up for visibility
                GUI.color = Color.Lerp(Color.green, Color.red, level);
                GUI.DrawTexture(new Rect(barX, barY, barW * level, barH), Texture2D.whiteTexture);

                GUI.color = Color.white;
            }
        }

        private void DrawSpeakingIndicators()
        {
            if (_playbackManager == null) return;
            if (Camera.main == null) return;

            HashSet<long> speaking = _playbackManager.GetSpeakingPlayers();
            if (speaking.Count == 0) return;

            Camera cam = Camera.main;

            foreach (Player p in Player.GetAllPlayers())
            {
                if (p == Player.m_localPlayer) continue;

                long pid = p.GetPlayerID();
                if (!speaking.Contains(pid)) continue;

                // Project player position to screen space
                Vector3 worldPos = p.GetHeadPoint() + Vector3.up * 0.5f;
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                // Skip if behind camera
                if (screenPos.z < 0) continue;

                // Unity screen coordinates have Y=0 at bottom, GUI has Y=0 at top
                float guiY = Screen.height - screenPos.y;

                // Draw speaking indicator
                float indicatorSize = 16f;
                Rect iconRect = new Rect(
                    screenPos.x - indicatorSize / 2f,
                    guiY - indicatorSize - 5f,
                    indicatorSize,
                    indicatorSize
                );

                GUI.DrawTexture(iconRect, _speakingTex);

                // Draw player name
                string name = p.GetPlayerName();
                Vector2 nameSize = _labelStyle.CalcSize(new GUIContent(name));
                Rect nameRect = new Rect(
                    screenPos.x - nameSize.x / 2f,
                    guiY - indicatorSize - nameSize.y - 5f,
                    nameSize.x,
                    nameSize.y
                );

                // Background for readability
                GUI.color = new Color(0, 0, 0, 0.6f);
                GUI.DrawTexture(new Rect(nameRect.x - 2, nameRect.y - 1, nameRect.width + 4, nameRect.height + 2), Texture2D.whiteTexture);
                GUI.color = Color.white;

                _labelStyle.normal.textColor = Color.green;
                GUI.Label(nameRect, name, _labelStyle);
                _labelStyle.normal.textColor = Color.white;
            }
        }

        private static Texture2D MakeColorTexture(int width, int height, Color color)
        {
            var tex = new Texture2D(width, height);
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private void OnDestroy()
        {
            if (_micOnTex != null) Destroy(_micOnTex);
            if (_micOffTex != null) Destroy(_micOffTex);
            if (_speakingTex != null) Destroy(_speakingTex);
        }
    }
}
