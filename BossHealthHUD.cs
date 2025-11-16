using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ItemStatsSystem;
using UnityEngine;

namespace bosshealthhud
{
    // Duckov 모드 로더가 찾는 엔트리: bosshealthhud.ModBehaviour
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        protected override void OnAfterSetup()
        {
            try
            {
                GameObject root = new GameObject("BossHealthHUDRoot");
                UnityEngine.Object.DontDestroyOnLoad(root);

                root.AddComponent<BossHealthHUDManager>();

                Debug.Log("[BossHealthHUD] OnAfterSetup - HUD 초기화 완료");
            }
            catch (Exception ex)
            {
                Debug.Log("[BossHealthHUD] OnAfterSetup 예외: " + ex);
            }
        }
    }

    public class BossHealthHUDManager : MonoBehaviour
    {
        private Camera _mainCamera;

        private CharacterMainControl _player;
        private CharacterMainControl _boss;

        private float _currentHpValue;
        private float _maxHpValue;
        private float _smoothDisplayRatio = 1f;

        private float _nextScanTime;
        private float _scanInterval = 0.5f;   // 0.5초마다 보스 후보 재탐색

        private GUIStyle _nameStyle;
        private GUIStyle _hpTextStyle;
        private bool _uiEnabled = true;       // F8 토글

        // 꼬마덕 HP가 128이라, 그보다 살짝 여유 있게 120 이상을 보스로 취급
        private float _bossMinMaxHp = 120f;

        // 플레이어와 너무 멀면 보스라도 표시 안 하도록 거리 제한
        private float _maxBossDisplayDistance = 200f;

        // HP 바용 흰 텍스처
        private Texture2D _hpTex;

        // 보스 HUD를 띄울 이름들 (화이트리스트)
        private static readonly string[] _bossNameExact =
        {
            "로든",
            "광산장",
            "BA 대장",
            "파리 대장",
            "폭주 아케이드",
            "폭주 기계 거미",
            "???",
            "꼬마덕",
			"비다",
			"쓰리샷 형님",
			"폭탄광",
			"바리케이드",
			"미셀",
			"고급 엔지니어",
			"샷건",
			"푸룽푸룽",
			"구루구루",
			"팔라팔라",
			"빌리빌리",
			"코코코코",
			"흥이",
			"교도관",
			"폭풍?",
			"일진",
			"급속 단장",
			"방랑자",
			"라이트맨"
			
        };

        // 이름 안에 이런 키워드가 들어가면 보스로 취급 (대장급 등)
        private static readonly string[] _bossNameKeywords =
        {
            "대장",
            "장",
            "보스"
        };

        // 보스바에서 무조건 제외할 이름들 (잡몹/자주 나오는 애들)
        private static readonly string[] _excludeBossNames =
        {
            "넝마꾼",
            "용병",
            "일반 BA",
            "파리 대원",
            "늑대",
            "일진",
            "부처 형"
        };

        private void Awake()
        {
            Debug.Log("[BossHealthHUD] Manager Awake");

            // 1x1 흰 텍스처 생성 (HP바 그릴 때 색 입혀서 사용)
            _hpTex = new Texture2D(1, 1);
            _hpTex.SetPixel(0, 0, Color.white);
            _hpTex.Apply();
        }

        private void Update()
        {
            // HUD 전체 ON/OFF
            if (Input.GetKeyDown(KeyCode.F8))
            {
                _uiEnabled = !_uiEnabled;
                Debug.Log("[BossHealthHUD] HUD " + (_uiEnabled ? "ON" : "OFF"));
            }

            if (!_uiEnabled)
            {
                return;
            }

            if (_mainCamera == null)
            {
                TryFindMainCamera();
            }

            if (_player == null || !_player)
            {
                TryFindPlayer();
            }

            // 주기적으로 보스 대상 찾기 (또는 보스가 죽었을 때 재탐색)
            if (Time.time >= _nextScanTime)
            {
                _nextScanTime = Time.time + _scanInterval;
                UpdateBossTarget();
            }

            UpdateHpValues();
        }

        private void TryFindMainCamera()
        {
            try
            {
                _mainCamera = Camera.main;
            }
            catch (Exception ex)
            {
                Debug.Log("[BossHealthHUD] Camera.main 실패: " + ex);
            }
        }

        private void TryFindPlayer()
        {
            try
            {
                _player = CharacterMainControl.Main;
                if (_player != null)
                {
                    Debug.Log("[BossHealthHUD] Player(Main) 찾음");
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[BossHealthHUD] Player 찾기 예외: " + ex);
            }
        }

        private void UpdateBossTarget()
        {
            try
            {
                // 지금 보스가 살아있고 너무 멀지 않으면 그대로 유지
                if (_boss != null && _boss && _boss.Health != null)
                {
                    Health bh = _boss.Health;
                    if (bh.CurrentHealth > 0f && bh.MaxHealth > 0f)
                    {
                        if (_player != null && _player)
                        {
                            float dist = Vector3.Distance(_player.transform.position, _boss.transform.position);
                            if (dist <= _maxBossDisplayDistance)
                            {
                                return; // 기존 보스 계속 유지
                            }
                        }
                    }
                }

                _boss = null;
                _currentHpValue = 0f;
                _maxHpValue = 0f;

                CharacterMainControl[] allChars = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                if (allChars == null || allChars.Length == 0)
                {
                    return;
                }

                CharacterMainControl best = null;
                float bestMaxHp = 0f;

                for (int i = 0; i < allChars.Length; i++)
                {
                    CharacterMainControl ch = allChars[i];
                    if (ch == null || !ch)
                    {
                        continue;
                    }

                    // 플레이어 자신 제외
                    if (_player != null && ch == _player)
                    {
                        continue;
                    }

                    string displayName = SafeGetName(ch);
                    if (!IsBossName(displayName))
                    {
                        continue;
                    }

                    Health h = ch.Health;
                    if (h == null)
                    {
                        continue;
                    }

                    float maxHp = h.MaxHealth;
                    float curHp = h.CurrentHealth;

                    if (curHp <= 0f)
                    {
                        continue;
                    }

                    // 꼬마덕(128) 이상만 보스로 취급 (보정값 120)
                    if (maxHp < _bossMinMaxHp)
                    {
                        continue;
                    }

                    if (_player != null && _player)
                    {
                        float dist = Vector3.Distance(_player.transform.position, ch.transform.position);
                        if (dist > _maxBossDisplayDistance)
                        {
                            continue;
                        }
                    }

                    if (maxHp > bestMaxHp)
                    {
                        bestMaxHp = maxHp;
                        best = ch;
                    }
                }

                if (best != null)
                {
                    _boss = best;
                    Debug.Log("[BossHealthHUD] 보스 후보 선택: " + SafeGetName(_boss) +
                              " Cur=" + _boss.Health.CurrentHealth +
                              " / Max=" + _boss.Health.MaxHealth);
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[BossHealthHUD] UpdateBossTarget 예외: " + ex);
            }
        }

        private void UpdateHpValues()
        {
            if (_boss == null || !_boss)
            {
                _currentHpValue = 0f;
                _maxHpValue = 0f;
                return;
            }

            Health h = _boss.Health;
            if (h == null)
            {
                _currentHpValue = 0f;
                _maxHpValue = 0f;
                return;
            }

            try
            {
                float hpCur = h.CurrentHealth;
                float hpMax = h.MaxHealth;

                if (hpMax <= 0f || hpCur <= 0f)
                {
                    _currentHpValue = 0f;
                    _maxHpValue = 0f;
                    return;
                }

                _currentHpValue = hpCur;
                _maxHpValue = hpMax;

                float targetRatio = Mathf.Clamp01(_currentHpValue / _maxHpValue);
                _smoothDisplayRatio = Mathf.Lerp(_smoothDisplayRatio, targetRatio, Time.deltaTime * 8f);
            }
            catch (Exception ex)
            {
                Debug.Log("[BossHealthHUD] HP 값 읽기 예외: " + ex);
            }
        }

        private void OnGUI()
        {
            if (!_uiEnabled)
            {
                return;
            }

            if (_boss == null || !_boss)
            {
                return;
            }

            if (_currentHpValue <= 0f || _maxHpValue <= 0f)
            {
                return;
            }

            if (_nameStyle == null)
            {
                _nameStyle = new GUIStyle(GUI.skin.label);
                _nameStyle.alignment = TextAnchor.MiddleCenter;
                _nameStyle.fontSize = 22;
                _nameStyle.normal.textColor = Color.white;
            }

            if (_hpTextStyle == null)
            {
                _hpTextStyle = new GUIStyle(GUI.skin.label);
                _hpTextStyle.alignment = TextAnchor.MiddleCenter;
                _hpTextStyle.fontSize = 18;
                _hpTextStyle.normal.textColor = Color.white;
            }

            // ───── 무기 슬롯 위에 위치시키기 ─────
            float barWidth = Screen.width * 0.75f;
            float barHeight = 32f;   // 조금 키워서 글씨 안 잘리게

            float bottomMargin = 230f;

            float x = (Screen.width - barWidth) * 0.5f;
            float y = Screen.height - bottomMargin - barHeight;

            float ratio = Mathf.Clamp01(_smoothDisplayRatio);
            Color oldColor = GUI.color;

            if (_hpTex == null)
            {
                _hpTex = new Texture2D(1, 1);
                _hpTex.SetPixel(0, 0, Color.white);
                _hpTex.Apply();
            }

            // ░ 테두리 (거의 검정에 가까운 어두운 빨강)
            GUI.color = new Color(0.15f, 0f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(x, y, barWidth, barHeight), _hpTex);

            // █ 실제 HP (밝은 빨강)
            GUI.color = new Color(0.9f, 0.1f, 0.1f, 0.95f);
            GUI.DrawTexture(new Rect(x + 2f, y + 2f, (barWidth - 4f) * ratio, barHeight - 4f), _hpTex);

            // 이름 + HP 숫자
            GUI.color = Color.white;

            string bossName = SafeGetName(_boss);
            GUI.Label(new Rect(x, y - 30f, barWidth, 26f), bossName, _nameStyle);

            string hpText = string.Format("{0} / {1}",
                Mathf.CeilToInt(_currentHpValue),
                Mathf.CeilToInt(_maxHpValue));
            GUI.Label(new Rect(x, y + 2f, barWidth, barHeight - 4f), hpText, _hpTextStyle);

            GUI.color = oldColor;
        }

        private static bool IsBossName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            // 제외 리스트에 있으면 무조건 false
            for (int i = 0; i < _excludeBossNames.Length; i++)
            {
                if (name.Contains(_excludeBossNames[i]))
                {
                    return false;
                }
            }

            // 정확히 일치하는 이름
            for (int i = 0; i < _bossNameExact.Length; i++)
            {
                if (name == _bossNameExact[i])
                {
                    return true;
                }
            }

            // 키워드가 들어간 이름 (대장, 장, 보스 등)
            for (int i = 0; i < _bossNameKeywords.Length; i++)
            {
                if (name.Contains(_bossNameKeywords[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string SafeGetName(CharacterMainControl ch)
        {
            if (ch == null)
            {
                return "Boss";
            }

            try
            {
                if (ch.characterPreset != null)
                {
                    return ch.characterPreset.DisplayName;
                }
            }
            catch
            {
            }

            return ch.name;
        }
    }
}
