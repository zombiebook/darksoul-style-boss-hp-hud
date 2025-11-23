using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using ItemStatsSystem;
using UnityEngine;
using Duckov;    // AudioManager, CharacterMainControl, Health

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
        // ───── 기본 ─────
        private Camera _mainCamera;
        private CharacterMainControl _player;

        // 여러 보스를 동시에 표시하기 위한 리스트
        private readonly List<CharacterMainControl> _bossList =
            new List<CharacterMainControl>();

        // 🔁 FindObjectsOfType 부하 줄이기용
        private float _scanInterval = 0.8f;   // 몇 초마다 보스 스캔할지
        private float _scanTimer = 0.2f;

        // 🔧 HP 바 위치 조절 (Y 오프셋, 픽셀 단위) – 양수: 위로, 음수: 아래로
        private float _barOffsetY = 0f;

        // HUD On/Off
        private bool _uiEnabled = true;       // F8 토글

        // 꼬마덕 HP가 128이라, 그보다 살짝 여유 있게 120 이상을 보스로 취급
        private float _bossMinMaxHp = 120f;

        // 플레이어와 너무 멀면 보스라도 표시 안 하도록 거리 제한
        private float _maxBossDisplayDistance = 20f;

        // HP 바용 흰 텍스처
        private Texture2D _hpTex;

        // HP/이름 텍스트 스타일
        private GUIStyle _nameStyle;
        private GUIStyle _hpTextStyle;

        // ───── DUCK HUNTED 오버레이 관련 ─────
        private bool _showDuckHunted;
        private float _duckHuntedTimer;
        private const float DuckHuntedDuration = 3.5f;
        private string _lastKilledBossName;
        private GUIStyle _duckHuntedStyle;
        private GUIStyle _duckHuntedSubStyle;

        // ───── 맵 진입 배너(위에 맵 이름 + "지금 진입 중") ─────
        private string _currentAreaSceneName;
        private string _currentAreaDisplayName;
        private float _enterAreaBannerTimer;
        private const float EnterAreaBannerDuration = 3.0f;
        private GUIStyle _enterAreaBannerMainStyle;
        private GUIStyle _enterAreaBannerSubStyle;

        // 보스 HP 변화 추적(죽었는지 체크)
        private readonly Dictionary<CharacterMainControl, float> _lastHpMap =
            new Dictionary<CharacterMainControl, float>();
        private readonly List<CharacterMainControl> _cleanupList =
            new List<CharacterMainControl>();

        // 보스 HUD를 띄울 이름들 (화이트리스트: 한·영·일)
        private static readonly string[] _bossNameExact =
        {
            "로든",
            "광산장",
            "BA 대장",
            "파리 대장",
            "축구 주장",
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

        // 이름에 포함되면 보스로 판단할 키워드들 (지금은 화이트리스트만 사용)
        private static readonly string[] _bossNameKeywords =
        {

        };

        private void Awake()
        {
            Debug.Log("[BossHealthHUD] Manager Awake");
            TryFindMainCamera();
            TryFindPlayer();
            LoadConfig();
            _scanTimer = 0.2f;   // 시작 직후 한 번 빨리 스캔
        }

        private void LoadConfig()
        {
            try
            {
                string dllPath = Assembly.GetExecutingAssembly().Location;
                string folder = Path.GetDirectoryName(dllPath);
                if (string.IsNullOrEmpty(folder))
                {
                    return;
                }

                string cfgPath = Path.Combine(folder, "BossHealthHUD.cfg");

                if (!File.Exists(cfgPath))
                {
                    string[] lines =
                    {
                        "# BossHealthHUD configuration",
                        "# bar_offset_y = HP 바 세로 위치 조절 (픽셀)",
                        "#   양수: 화면 위쪽으로 이동,  음수: 화면 아래쪽으로 이동",
                        "bar_offset_y=0"
                    };
                    File.WriteAllLines(cfgPath, lines);
                    _barOffsetY = 0f;
                    Debug.Log("[BossHealthHUD] 기본 BossHealthHUD.cfg 생성");
                    return;
                }

                string[] cfgLines = File.ReadAllLines(cfgPath);
                foreach (string raw in cfgLines)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;

                    string line = raw.Trim();
                    if (line.StartsWith("#"))
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();

                    float f;
                    if (key == "bar_offset_y" && float.TryParse(value, out f))
                    {
                        _barOffsetY = f;
                    }
                }

                Debug.Log("[BossHealthHUD] CFG 로드 완료 - bar_offset_y=" + _barOffsetY);
            }
            catch (Exception ex)
            {
                Debug.Log("[BossHealthHUD] LoadConfig 예외: " + ex);
            }
        }

        private void Update()
        {
            // F8로 HUD ON/OFF 토글
            if (UnityEngine.Input.GetKeyDown(KeyCode.F8))
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

            if (_player == null)
            {
                TryFindPlayer();
            }

            // 1) 보스 사망 체크 (매 프레임)
            UpdateBossDeathState();

            // 2) 맵 진입 배너 갱신 (씬 이름 변경 감지)
            UpdateAreaBanner();

            // 3) 일정 시간마다만 보스 스캔 (해상도 상관없이 부하 줄이기)
            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = _scanInterval;
                ScanBosses();
            }

            // 4) DUCK HUNTED 타이머
            if (_showDuckHunted)
            {
                _duckHuntedTimer -= Time.deltaTime;
                if (_duckHuntedTimer <= 0f)
                {
                    _duckHuntedTimer = 0f;
                    _showDuckHunted = false;
                    _lastKilledBossName = null;
                }
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
                    CharacterMainControl boss = candidates[i];
                    if (boss != null && !_bossList.Contains(boss))
                    {
                        _bossList.Add(boss);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[BossHealthHUD] ScanBosses 예외: " + ex);
            }
        }

        // 동시에 표시할 수 있는 보스 바 최대 개수
        private const int MaxBossBars = 3;

        // 보스 HP 변화 감지해서 죽었을 때 DUCK HUNTED + 사운드 트리거
        private void UpdateBossDeathState()
        {
            if (_bossList == null || _bossList.Count == 0)
                return;

            try
            {
                _cleanupList.Clear();

                foreach (CharacterMainControl boss in _bossList)
                {
                    if (boss == null || !boss)
                    {
                        _cleanupList.Add(boss);
                        continue;
                    }

                    Health h = boss.Health;
                    if (h == null)
                    {
                        _cleanupList.Add(boss);
                        continue;
                    }

                    float curHp = h.CurrentHealth;

                    float prevHp;
                    // 처음 보는 보스면 현재 HP를 저장만 해두고 넘어감
                    if (!_lastHpMap.TryGetValue(boss, out prevHp))
                    {
                        _lastHpMap[boss] = curHp;
                        continue;
                    }

                    // 이전에는 살아 있었는데(>0), 지금 0 이하 → 방금 죽은 것
                    if (prevHp > 0f && curHp <= 0f)
                    {
                        string bossName = SafeGetName(boss);
                        TriggerDuckHunted(bossName);   // 여기서 문구 + 소리 둘 다 실행
                        _cleanupList.Add(boss);
                    }

                    // HP 갱신
                    _lastHpMap[boss] = curHp;
                }

                // 죽었거나 null 된 보스 정리
                for (int i = 0; i < _cleanupList.Count; i++)
                {
                    CharacterMainControl dead = _cleanupList[i];
                    _lastHpMap.Remove(dead);
                    _bossList.Remove(dead);
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[BossHealthHUD] UpdateBossDeathState 예외: " + ex);
            }
        }

        private void TriggerDuckHunted(string bossName)
        {
            _showDuckHunted = true;
            _duckHuntedTimer = DuckHuntedDuration;
            _lastKilledBossName = bossName;

            Debug.Log("[BossHealthHUD] DUCK HUNTED -> " + bossName);

            TryPlayBossDefeatedSound();
        }

        // 🔊 보스 처치 사운드: 코루틴으로 2개 순차 재생
        // 🔊 보스 처치 사운드: 코루틴으로 2개 순차 재생
        private void TryPlayBossDefeatedSound()
        {
            try
            {
                StartCoroutine(PlayBossDefeatedSequence());
            }
            catch (Exception ex)
            {
                Debug.LogError("[BossHealthHUD] TryPlayBossDefeatedSound ERROR: " + ex);
            }
        }


        private IEnumerator PlayBossDefeatedSequence()
        {
            string dllPath = Assembly.GetExecutingAssembly().Location;
            string folder = Path.GetDirectoryName(dllPath);
            if (string.IsNullOrEmpty(folder))
            {
                Debug.Log("[BossHealthHUD] DLL 폴더 경로를 찾지 못했습니다.");
                yield break;
            }

            string audioDir = Path.Combine(folder, "Audio");

            // ✅ 1번 소리: 예전에 잘 되던 기본 파일
            string firstPath = Path.Combine(audioDir, "BossDefeated.wav");
            // ✅ 2번 소리: 추가 재생용
            string secondPath = Path.Combine(audioDir, "BossDefeated_2.mp3");

            bool hasFirst = File.Exists(firstPath);
            bool hasSecond = File.Exists(secondPath);

            // 둘 다 없으면 아무것도 못 함
            if (!hasFirst && !hasSecond)
            {
                Debug.Log("[BossHealthHUD] BossDefeated sound files not found");
                yield break;
            }

            // 🔸 죽는 이펙트 먼저 들리게 약간 기다렸다가 1번 소리 재생
            const float firstDelay = 0.35f;   // 너무 겹치면 0.5f 정도까지 올려도 됨

            if (hasFirst)
            {
                // 죽는 이펙트가 먼저 나가도록 살짝 딜레이
                yield return new WaitForSeconds(firstDelay);

                AudioManager.PostCustomSFX(firstPath, null, false);
                Debug.Log("[BossHealthHUD] BossDefeated (first) sound played: " + firstPath);

                // 1번 끝나고 2번까지 대기 (원래 2.5f 쓰던 자리)
                yield return new WaitForSeconds(1.0f);
            }

            // 2번 소리 (있으면 이어서)
            if (hasSecond)
            {
                AudioManager.PostCustomSFX(secondPath, null, false);
                Debug.Log("[BossHealthHUD] BossDefeated_2 sound played: " + secondPath);
            }
        }



        // ───── 맵 진입 배너(씬 이름 변경 감지 + 로컬라이즈) ─────
        private void UpdateAreaBanner()
        {
            try
            {
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(sceneName))
                {
                    if (_currentAreaSceneName != sceneName)
                    {
                        _currentAreaSceneName = sceneName;
                        _currentAreaDisplayName = GetLocalizedAreaName(sceneName);

                        if (!string.IsNullOrEmpty(_currentAreaDisplayName))
                        {
                            _enterAreaBannerTimer = EnterAreaBannerDuration;
                            Debug.Log("[BossHealthHUD] Area entered: " + sceneName + " -> " + _currentAreaDisplayName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[BossHealthHUD] UpdateAreaBanner 예외: " + ex);
            }

            if (_enterAreaBannerTimer > 0f)
            {
                _enterAreaBannerTimer -= Time.deltaTime;
                if (_enterAreaBannerTimer < 0f)
                    _enterAreaBannerTimer = 0f;
            }
        }

        private string GetLocalizedAreaName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
                return null;

            string lower = sceneName.ToLowerInvariant();
            bool isJap = Application.systemLanguage == SystemLanguage.Japanese;
            bool isEng = Application.systemLanguage == SystemLanguage.English;

            // 기지(Base)
            if (lower == "base" || lower.Contains("base"))
            {
                if (isJap) return "バンカー";
                if (isEng) return "Bunker";
                return "벙커";
            }

            // 제로존 : Level_GroundZero_
            if (lower.Contains("groundzero"))
            {
                if (isJap) return "エリアゼロ";
                if (isEng) return "Ground Zero";
                return "제로존";
            }

            // 창고 구역 : HiddenWarehouse
            if (lower.Contains("hiddenwarehouse"))
            {
                if (isJap) return "倉庫エリア";
                if (isEng) return "Warehouse Area";
                return "창고 구역";
            }

            // 농장마을 어딘가 : Farm_01
            if (lower == "farm_01" || lower.Contains("farm_01"))
            {
                if (isJap) return "農場町・どこか";
                if (isEng) return "Farm Town - somewhere";
                return "농장마을 어딘가";
            }

            // 농장마을 : Farm_Main
            if (lower == "farm_main" || lower.Contains("farm_main"))
            {
                if (isJap) return "農場町";
                if (isEng) return "Farm Town";
                return "농장마을";
            }

            // J-Lab 연구소 입구 : Farm_JLab_Facility
            if (lower.Contains("farm_jlab_facility"))
            {
                if (isJap) return "J-Lab研究所・入口";
                if (isEng) return "J-Lab Entrance";
                return "J-Lab 연구소 입구";
            }

            // J-Lab 연구소 : JLab_1, level_jlab*
            if (lower.Contains("jlab"))
            {
                if (isJap) return "J-Lab研究所";
                if (isEng) return "J-Lab";
                return "J-Lab 연구소";
            }

            // 폭풍 구역 : StormZone
            if (lower.Contains("stormzone"))
            {
                if (isJap) return "嵐エリア";
                if (isEng) return "Storm Zone";
                return "폭풍 구역";
            }

            return null;
        }

        private void OnGUI()
        {
            if (!_uiEnabled)
            {
                return;
            }

            Color originalColor = GUI.color;

            // ====== 보스 HP 바들 그리기 ======
            if (_player != null && _player && _bossList != null && _bossList.Count > 0)
            {
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

                float barWidth = Screen.width * 0.75f;
                float barHeight = 32f;   // 바 두께

                // 기본 230f 에서 CFG 값으로 위/아래 이동
                float bottomMargin = 230f + _barOffsetY;

                float baseX = (Screen.width - barWidth) * 0.5f;
                float baseY = Screen.height - bottomMargin - barHeight;

                // 바들 간 적당한 간격
                float verticalSpacing = barHeight + 30f;

                if (_hpTex == null)
                {
                    _hpTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    _hpTex.SetPixel(0, 0, Color.white);
                    _hpTex.Apply();
                }

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

                    float maxHp = h.MaxHealth;
                    float curHp = h.CurrentHealth;

                    if (maxHp <= 0f || curHp <= 0f)
                    {
                        continue;
                    }

                    // 꼬마덕(128) 이상만 보스표시 (보정값 120)
                    if (maxHp < _bossMinMaxHp)
                    {
                        continue;
                    }

                    // 거리 체크
                    float dist = Vector3.Distance(_player.transform.position, boss.transform.position);
                    if (dist > _maxBossDisplayDistance)
                    {
                        continue;
                    }

                    // 화면 안에 있는 보스만 HP바 표시
                    if (_mainCamera != null)
                    {
                        try
                        {
                            Vector3 vp = _mainCamera.WorldToViewportPoint(boss.transform.position);
                            bool onScreen =
                                vp.z > 0f &&
                                vp.x >= 0f && vp.x <= 1f &&
                                vp.y >= 0f && vp.y <= 1f;

                            if (!onScreen)
                            {
                                continue;
                            }
                        }
                        catch
                        {
                        }
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

                    // 이름은 바 바로 위 (위아래 여유 넉넉)
                    Rect nameRect = new Rect(
                        x,
                        y - 29f,
                        barWidth,
                        30f
                    );

                    // HP 텍스트는 막대 안 중앙 (위쪽 안 잘리게 여유)
                    Rect hpRect = new Rect(
                        x + 2f,
                        y + 1f,
                        barWidth - 4f,
                        barHeight - 2f
                    );

                    GUI.Label(nameRect, bossName, _nameStyle);
                    GUI.Label(
                        hpRect,
                        string.Format("{0:0}/{1:0}  ({2:P0})", curHp, maxHp, ratio),
                        _hpTextStyle
                    );

                    drawnCount++;
                }
            }

            // ====== 맵 진입 배너 (위에 맵 이름 + "지금 진입 중") ======
            if (_enterAreaBannerTimer > 0f && !string.IsNullOrEmpty(_currentAreaDisplayName))
            {
                if (_enterAreaBannerMainStyle == null)
                {
                    _enterAreaBannerMainStyle = new GUIStyle(GUI.skin.label);
                    _enterAreaBannerMainStyle.alignment = TextAnchor.MiddleCenter;
                    _enterAreaBannerMainStyle.fontSize = 30;
                    _enterAreaBannerMainStyle.fontStyle = FontStyle.Bold;
                    _enterAreaBannerMainStyle.normal.textColor = Color.white;
                }

                if (_enterAreaBannerSubStyle == null)
                {
                    _enterAreaBannerSubStyle = new GUIStyle(GUI.skin.label);
                    _enterAreaBannerSubStyle.alignment = TextAnchor.MiddleCenter;
                    _enterAreaBannerSubStyle.fontSize = 20;
                    _enterAreaBannerSubStyle.normal.textColor = Color.white;
                }

                bool isJap = Application.systemLanguage == SystemLanguage.Japanese;
                bool isEng = Application.systemLanguage == SystemLanguage.English;

                float t = Mathf.Clamp01(_enterAreaBannerTimer / EnterAreaBannerDuration);

                float bannerHeight = 70f;
                Rect bgRect = new Rect(
                    0f,
                    40f,
                    Screen.width,
                    bannerHeight
                );

                GUI.color = new Color(0f, 0f, 0f, 0.5f * t);
                GUI.DrawTexture(bgRect, Texture2D.whiteTexture);

                Rect mainRect = new Rect(
                    0f,
                    bgRect.y + 6f,
                    Screen.width,
                    34f
                );

                GUI.color = new Color(1f, 0.9f, 0.6f, t);
                GUI.Label(mainRect, _currentAreaDisplayName, _enterAreaBannerMainStyle);

                Rect subRect = new Rect(
                    0f,
                    mainRect.y + 30f,
                    Screen.width,
                    24f
                );

                string subText;
                if (isJap)
                {
                    subText = "エリア進入中";
                }
                else if (isEng)
                {
                    subText = "Entering area";
                }
                else
                {
                    // ★ 사용자가 유지해 달라고 했던 문구
                    subText = "지금 진입 중";
                }

                GUI.color = new Color(1f, 1f, 1f, t);
                GUI.Label(subRect, subText, _enterAreaBannerSubStyle);
            }

            // ====== DUCK HUNTED 오버레이 ======
            if (_showDuckHunted && _duckHuntedTimer > 0f)
            {
                if (_duckHuntedStyle == null)
                {
                    _duckHuntedStyle = new GUIStyle(GUI.skin.label);
                    _duckHuntedStyle.alignment = TextAnchor.MiddleCenter;
                    _duckHuntedStyle.fontSize = 56;
                    _duckHuntedStyle.fontStyle = FontStyle.Bold;
                }

                if (_duckHuntedSubStyle == null)
                {
                    _duckHuntedSubStyle = new GUIStyle(GUI.skin.label);
                    _duckHuntedSubStyle.alignment = TextAnchor.MiddleCenter;
                    _duckHuntedSubStyle.fontSize = 26;
                }

                float t = Mathf.Clamp01(_duckHuntedTimer / DuckHuntedDuration);

                float overlayHeight = 140f;
                Rect bgRect = new Rect(
                    0f,
                    (Screen.height - overlayHeight) * 0.5f,
                    Screen.width,
                    overlayHeight
                );

                GUI.color = new Color(0f, 0f, 0f, 0.6f * t);
                GUI.DrawTexture(bgRect, Texture2D.whiteTexture);

                float mainSize = _duckHuntedStyle.fontSize;
                float subSize = _duckHuntedSubStyle.fontSize;

                Rect mainRect = new Rect(
                    0f,
                    bgRect.y + (overlayHeight * 0.5f) - mainSize,
                    Screen.width,
                    mainSize + 10f
                );

                // 메인 텍스트 색 (연한 청록)
                GUI.color = new Color(0.8f, 1f, 0.9f, t);
                GUI.Label(mainRect, "DUCK HUNTED", _duckHuntedStyle);

                if (!string.IsNullOrEmpty(_lastKilledBossName))
                {
                    GUI.color = new Color(1f, 1f, 1f, t);
                    Rect subRect = new Rect(
                        0f,
                        mainRect.y + mainSize,
                        Screen.width,
                        subSize + 10f
                    );
                    GUI.Label(subRect, _lastKilledBossName, _duckHuntedSubStyle);
                }
            }

            GUI.color = originalColor;
        }

        private static bool IsBossName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            // 전부 소문자로 통일해서 비교
            string lower = name.ToLowerInvariant();

            // 1) 화이트리스트 이름과 완전히 일치하는지 검사
            for (int i = 0; i < _bossNameExact.Length; i++)
            {
                string exact = _bossNameExact[i];
                if (!string.IsNullOrEmpty(exact) && lower == exact.ToLowerInvariant())
                {
                    return true;
                }
            }

            // 2) 키워드 포함 (대장, 장, 보스 등)
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
                return string.Empty;
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
