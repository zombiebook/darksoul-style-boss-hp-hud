using System;
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

        // 여러 보스를 동시에 표시하기 위한 리스트
        private readonly List<CharacterMainControl> _bossList = new List<CharacterMainControl>();
        private const int MaxBossBars = 3; // 동시에 최대 몇 줄까지 표시할지

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

        // 보스 HUD를 띄울 이름들 (화이트리스트: 한·영·일)
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
            "라이트맨",
            "Pato Chapo",
            "Man of Light",
            "Speedy Group Commander",
            "Lordon",
            "Vida",
            "Big Xing",
            "Rampaging Arcade",
            "Senior Engineer",
            "Triple-Shot Man",
            "Misel",
            "Mine Manager",
            "Shotgunner",
            "Mad Bomber",
            "Security Captain",
            "Fly Captain",
            "School Bully",
            "Billy Billy",
            "Gulu Gulu",
            "Pala Pala",
            "Pulu Pulu",
            "Koko Koko",
            "Roadblock",
            "チビガモ",
            "光の男",
            "ロードン",
            "スピード団団長",
            "ハエ隊長",
            "暴走アーケード",
            "ヴィーダ",
            "いじめっ子",
            "施設長",
            "マルセル",
            "上級エンジニア",
            "トリプルS親分",
            "ショットガンナー",
            "BA隊長",
            "ロードブロック",
            "グルグル",
            "パラパラ",
            "ビッグシン",
            "ビリビリ",
            "プロプロ",
            "ロロロロ",
            "爆弾マニア",
            "看守長",
            "レイダー"
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

            // 주기적으로 보스 대상 다시 스캔 (죽었거나 멀어졌거나, 새 보스 등장 등)
            if (Time.time >= _nextScanTime)
            {
                _nextScanTime = Time.time + _scanInterval;
                ScanBosses();
            }
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

        private void ScanBosses()
        {
            try
            {
                _bossList.Clear();

                CharacterMainControl[] allChars = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                if (allChars == null || allChars.Length == 0)
                {
                    return;
                }

                List<CharacterMainControl> candidates = new List<CharacterMainControl>();

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

                    // 죽은 보스는 제외
                    if (curHp <= 0f)
                    {
                        continue;
                    }

                    // 꼬마덕(128) 이상만 보스로 취급 (보정값 120)
                    if (maxHp < _bossMinMaxHp)
                    {
                        continue;
                    }

                    // 플레이어와 거리 제한
                    if (_player != null && _player)
                    {
                        float dist = Vector3.Distance(_player.transform.position, ch.transform.position);
                        if (dist > _maxBossDisplayDistance)
                        {
                            continue;
                        }
                    }

                    candidates.Add(ch);
                }

                if (candidates.Count == 0)
                {
                    return;
                }

                // MaxHP 기준으로 내림차순 정렬 후, 상위 N개만 선택
                candidates.Sort((a, b) =>
                {
                    Health ha = a != null ? a.Health : null;
                    Health hb = b != null ? b.Health : null;
                    float ma = (ha != null) ? ha.MaxHealth : 0f;
                    float mb = (hb != null) ? hb.MaxHealth : 0f;
                    return mb.CompareTo(ma);
                });

                for (int i = 0; i < candidates.Count && i < MaxBossBars; i++)
                {
                    _bossList.Add(candidates[i]);
                }

                if (_bossList.Count > 0)
                {
                    Debug.Log("[BossHealthHUD] 보스 수: " + _bossList.Count);
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[BossHealthHUD] ScanBosses 예외: " + ex);
            }
        }

        private void OnGUI()
        {
            if (!_uiEnabled)
            {
                return;
            }

            if (_bossList == null || _bossList.Count == 0)
            {
                return;
            }

            if (_player == null || !_player)
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

            float baseX = (Screen.width - barWidth) * 0.5f;
            float baseY = Screen.height - bottomMargin - barHeight;

            float verticalSpacing = barHeight + 40f; // 줄 간 간격

            Color oldColor = GUI.color;

            int drawnCount = 0;

            for (int i = 0; i < _bossList.Count && drawnCount < MaxBossBars; i++)
            {
                CharacterMainControl boss = _bossList[i];
                if (boss == null || !boss)
                {
                    continue;
                }

                Health h = boss.Health;
                if (h == null)
                {
                    continue;
                }

                float curHp = h.CurrentHealth;
                float maxHp = h.MaxHealth;

                if (maxHp <= 0f || curHp <= 0f)
                {
                    continue;
                }

                // 거리 체크: 멀어지면 해당 보스만 스킵 (다른 보스는 그릴 수 있음)
                float dist = Vector3.Distance(_player.transform.position, boss.transform.position);
                if (dist > _maxBossDisplayDistance)
                {
                    continue;
                }

                float ratio = Mathf.Clamp01(curHp / maxHp);

                float x = baseX;
                float y = baseY - drawnCount * verticalSpacing;

                // ░ 테두리 (거의 검정에 가까운 어두운 빨강)
                GUI.color = new Color(0.15f, 0f, 0f, 0.8f);
                GUI.DrawTexture(new Rect(x, y, barWidth, barHeight), _hpTex);

                // █ 실제 HP (밝은 빨강)
                GUI.color = new Color(0.9f, 0.1f, 0.1f, 0.95f);
                GUI.DrawTexture(
                    new Rect(x + 2f, y + 2f, (barWidth - 4f) * ratio, barHeight - 4f),
                    _hpTex
                );

                // 이름 + HP 숫자
                GUI.color = Color.white;

                string bossName = SafeGetName(boss);
                GUI.Label(new Rect(x, y - 34f, barWidth, 32f), bossName, _nameStyle);

                string hpText = string.Format("{0} / {1}",
                    Mathf.CeilToInt(curHp),
                    Mathf.CeilToInt(maxHp));
                GUI.Label(new Rect(x, y + 1f, barWidth, barHeight - 2f), hpText, _hpTextStyle);

                drawnCount++;
            }

            GUI.color = oldColor;
        }

        private static bool IsBossName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            // 전부 소문자로 통일해서 비교
            string lower = name.ToLowerInvariant();

            // 1) 제외 리스트 먼저 (잡몹/부처 형 등)
            for (int i = 0; i < _excludeBossNames.Length; i++)
            {
                string ex = _excludeBossNames[i];
                if (!string.IsNullOrEmpty(ex) && lower.Contains(ex.ToLowerInvariant()))
                {
                    return false;
                }
            }

            // 2) 정확히 일치하는 이름 (한·영·일 보스들)
            for (int i = 0; i < _bossNameExact.Length; i++)
            {
                string exact = _bossNameExact[i];
                if (!string.IsNullOrEmpty(exact) && lower == exact.ToLowerInvariant())
                {
                    return true;
                }
            }

            // 3) 키워드 포함 (대장, 장, 보스 등)
            for (int i = 0; i < _bossNameKeywords.Length; i++)
            {
                string kw = _bossNameKeywords[i];
                if (!string.IsNullOrEmpty(kw) && lower.Contains(kw.ToLowerInvariant()))
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
