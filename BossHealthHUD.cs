using System;
using System.Collections.Generic;
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
        // ───── 게임 언어 enum ─────
        private enum GameLanguage
        {
            Korean,
            Japanese,
            English,
            Other
        }

        // ───── 기본 ─────
        private Camera _mainCamera;
        private CharacterMainControl _player;

        // 여러 보스를 동시에 표시하기 위한 리스트
        private readonly List<CharacterMainControl> _bossList =
            new List<CharacterMainControl>();

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

        // 입장 배너 스타일
        private GUIStyle _enterAreaBannerStyle;
        private GUIStyle _enterAreaBannerSubStyle;

        // ───── DUCK HUNTED 오버레이 관련 ─────
        private bool _showDuckHunted;
        private float _duckHuntedTimer;
        private const float DuckHuntedDuration = 3.5f;
        private string _lastKilledBossName;
        private GUIStyle _duckHuntedStyle;
        private GUIStyle _duckHuntedSubStyle;

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

        // ───── 입장 배너용 필드 ─────
        private string _enterAreaTitle;
        private float _enterAreaShowEndTime;
        private bool _hasEnterAreaBannerShown;
        private string _lastSceneName;

        // 게임 언어 캐시 (Duckov 내부 Localization 추측용)
        private bool _cachedLangChecked;
        private GameLanguage _cachedGameLanguage = GameLanguage.Korean;

        // 사용자가 F9로 강제로 고른 HUD 언어 (null이면 자동)
        private GameLanguage? _overrideLang = null;

        private void Awake()
        {
            Debug.Log("[BossHealthHUD] Manager Awake");
            TryFindMainCamera();
            TryFindPlayer();
        }

        private void Update()
        {
            // 1) 씬 변경 감지 → 배너 상태 리셋
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            string sceneName = scene.name;

            if (_lastSceneName != sceneName)
            {
                _lastSceneName = sceneName;
                _hasEnterAreaBannerShown = false;
                _enterAreaTitle = null;
                _enterAreaShowEndTime = 0f;

                Debug.Log("[BossHealthHUD] Scene changed -> " + sceneName);
            }

            // 2) 플레이어가 잡힌 뒤, 씬마다 한 번만 입장 배너 표시
            if (!_hasEnterAreaBannerShown && _player != null)
            {
                _hasEnterAreaBannerShown = true;

                _enterAreaTitle = GetCurrentAreaTitle();
                _enterAreaShowEndTime = Time.time + 4f; // 4초 동안 표시

                Debug.Log("[BossHealthHUD] Enter area banner: " + _enterAreaTitle);
            }

            // F8로 HUD ON/OFF 토글
            if (UnityEngine.Input.GetKeyDown(KeyCode.F8))
            {
                _uiEnabled = !_uiEnabled;
                Debug.Log("[BossHealthHUD] HUD " + (_uiEnabled ? "ON" : "OFF"));
            }

            // F9로 HUD 언어 강제 변경 (Auto → JP → EN → KR → Auto)
            if (UnityEngine.Input.GetKeyDown(KeyCode.F9))
            {
                if (_overrideLang == null)
                {
                    _overrideLang = GameLanguage.Japanese;
                }
                else if (_overrideLang == GameLanguage.Japanese)
                {
                    _overrideLang = GameLanguage.English;
                }
                else if (_overrideLang == GameLanguage.English)
                {
                    _overrideLang = GameLanguage.Korean;
                }
                else
                {
                    _overrideLang = null; // 다시 자동 모드
                }

                Debug.Log("[BossHealthHUD] Language override: " +
                          (_overrideLang.HasValue ? _overrideLang.ToString() : "Auto(Game)"));

                // 배너가 아직 떠있다면, 바로 언어 반영
                if (_hasEnterAreaBannerShown && Time.time <= _enterAreaShowEndTime)
                {
                    _enterAreaTitle = GetCurrentAreaTitle();
                }
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

            // 3) 보스 HP 변화 체크 (죽었는지 감지)
            UpdateBossDeathState();

            // 4) 15프레임마다 보스 목록 재스캔
            if (Time.frameCount % 15 == 0)
            {
                ScanBosses();
            }

            // 5) DUCK HUNTED 페이드
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

        // ───── 맵 이름 + 언어 매핑 ─────
        private string GetCurrentAreaTitle()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var sceneName = scene.name;

            if (string.IsNullOrEmpty(sceneName))
                return "레이드 시작";

            string lower = scene.name.ToLowerInvariant();
            GameLanguage lang = GetGameLanguage();
            bool isJap = (lang == GameLanguage.Japanese);
            bool isEng = (lang == GameLanguage.English);
            bool isKor = (lang == GameLanguage.Korean || (!isJap && !isEng));

            // 기지(Base)
            if (lower == "base" || lower.Contains("base"))
            {
                if (isJap) return "バンカー";
                if (isEng) return "Bunker";
                return "벙커";
            }

            // 제로존 : Level_GroundZero_1 / Level_GroundZero_Main
            if (lower.StartsWith("level_groundzero"))
            {
                if (isJap) return "エリアゼロ";
                if (isEng) return "Ground Zero";
                return "제로존";
            }

            // 창고 구역 : Level_HiddenWarehouse
            if (lower.StartsWith("level_hiddenwarehouse"))
            {
                if (isJap) return "倉庫エリア";
                if (isEng) return "Warehouse Area";
                return "창고 구역";
            }

            // 농장마을 남부 : Level_Farm_01  (먼저 체크)
            if (lower == "level_farm_01")
            {
                if (isJap) return "農場町・どこか";
                if (isEng) return "Farm Town - South";
                return "농장마을 남부";
            }

            // 농장마을 : Level_Farm_Main
            if (lower == "level_farm_main")
            {
                if (isJap) return "農場町";
                if (isEng) return "Farm Town";
                return "농장마을";
            }

            // J-Lab 연구소 : Level_JLab_1
            if (lower == "level_jlab_1" || lower.StartsWith("level_jlab"))
            {
                if (isJap) return "J-Lab研究所";
                if (isEng) return "J-Lab";
                return "J-Lab 연구소";
            }

            // 폭풍 구역 : Level_StormZone_1
            if (lower == "level_stormzone_1" || lower.StartsWith("level_stormzone"))
            {
                if (isJap) return "嵐エリア";
                if (isEng) return "Storm Zone";
                return "폭풍 구역";
            }

            // 못 매칭하면 씬 이름 그대로
            return sceneName;
        }

        // 게임 옵션 언어를 최대한 추측해서 한/일/영 중 하나로 반환
        private GameLanguage GetGameLanguage()
        {
            // 0) F9로 강제한 언어가 있으면 그걸 먼저 사용
            if (_overrideLang.HasValue)
                return _overrideLang.Value;

            if (_cachedLangChecked)
                return _cachedGameLanguage;

            GameLanguage result = GameLanguage.Korean; // 기본값: 한국어

            try
            {
                Assembly asm = typeof(CharacterMainControl).Assembly;
                if (asm != null)
                {
                    string[] typeNames =
                    {
                        "Duckov.Settings.GameSettings",
                        "Duckov.Settings.LanguageSettings",
                        "Duckov.Localization.LocalizationManager",
                        "Duckov.Localization.LanguageManager"
                    };

                    string[] memberNames =
                    {
                        "CurrentLanguage",
                        "Language",
                        "CurrentLocale"
                    };

                    for (int ti = 0; ti < typeNames.Length; ti++)
                    {
                        string typeName = typeNames[ti];
                        Type t = asm.GetType(typeName, false);
                        if (t == null) continue;

                        for (int mi = 0; mi < memberNames.Length; mi++)
                        {
                            string mn = memberNames[mi];

                            // static property
                            PropertyInfo pi = t.GetProperty(mn,
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            if (pi != null)
                            {
                                object val = null;
                                try { val = pi.GetValue(null, null); } catch { }
                                GameLanguage? parsed = TryParseLanguageFromValue(val);
                                if (parsed.HasValue)
                                {
                                    result = parsed.Value;
                                    goto Done;
                                }
                            }

                            // static field
                            FieldInfo fi = t.GetField(mn,
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            if (fi != null)
                            {
                                object val = null;
                                try { val = fi.GetValue(null); } catch { }
                                GameLanguage? parsed = TryParseLanguageFromValue(val);
                                if (parsed.HasValue)
                                {
                                    result = parsed.Value;
                                    goto Done;
                                }
                            }
                        }
                    }
                }

                // 위에서 못 찾았으면 폴백: 시스템 언어
                switch (Application.systemLanguage)
                {
                    case SystemLanguage.Japanese:
                        result = GameLanguage.Japanese;
                        break;
                    case SystemLanguage.English:
                        result = GameLanguage.English;
                        break;
                    case SystemLanguage.Korean:
                        result = GameLanguage.Korean;
                        break;
                    default:
                        result = GameLanguage.Other;
                        break;
                }
            }
            catch
            {
                // 예외 나면 시스템 언어만 기준
                switch (Application.systemLanguage)
                {
                    case SystemLanguage.Japanese:
                        result = GameLanguage.Japanese;
                        break;
                    case SystemLanguage.English:
                        result = GameLanguage.English;
                        break;
                    case SystemLanguage.Korean:
                        result = GameLanguage.Korean;
                        break;
                    default:
                        result = GameLanguage.Other;
                        break;
                }
            }

        Done:
            _cachedLangChecked = true;
            _cachedGameLanguage = result;
            Debug.Log("[BossHealthHUD] Detected game language: " + result);
            return result;
        }

        private static GameLanguage? TryParseLanguageFromValue(object value)
        {
            if (value == null) return null;

            string s = value.ToString();
            if (string.IsNullOrEmpty(s)) return null;

            s = s.ToLowerInvariant();

            // 일본어
            if (s == "ja" || s == "ja-jp" || s.Contains("ja_jp") ||
                s.Contains("jap") ||
                s.Contains("日本") || s.Contains("にほん") || s.Contains("にっぽん") ||
                s.Contains("日本語"))
            {
                return GameLanguage.Japanese;
            }

            // 한국어
            if (s == "ko" || s == "ko-kr" || s.Contains("ko_kr") ||
                s.Contains("kor") ||
                s.Contains("korean") ||
                s.Contains("한국") || s.Contains("한글"))
            {
                return GameLanguage.Korean;
            }

            // 영어
            if (s == "en" || s == "en-us" || s == "en-gb" ||
                s.Contains("english") ||
                s.Contains("eng") ||
                s.Contains("英語") || s.Contains("えいご"))
            {
                return GameLanguage.English;
            }

            return null;
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

                CharacterMainControl[] allChars =
                    UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
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
                        float dist = Vector3.Distance(_player.transform.position,
                            ch.transform.position);
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

        private void TryPlayBossDefeatedSound()
        {
            try
            {
                string dllPath = Assembly.GetExecutingAssembly().Location;
                string folder = Path.GetDirectoryName(dllPath);
                if (string.IsNullOrEmpty(folder))
                {
                    Debug.Log("[BossHealthHUD] DLL 폴더 경로를 찾지 못했습니다.");
                    return;
                }

                string audioDir = Path.Combine(folder, "Audio");
                string filePath = Path.Combine(audioDir, "BossDefeated.mp3");

                if (!System.IO.File.Exists(filePath))
                {
                    Debug.Log("[BossHealthHUD] BossDefeated.mp3 not found: " + filePath);
                    return;
                }

                AudioManager.PostCustomSFX(filePath, null, false);
                Debug.Log("[BossHealthHUD] BossDefeated sound played: " + filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError("[BossHealthHUD] TryPlayBossDefeatedSound ERROR: " + ex);
            }
        }

        private void OnGUI()
        {
            if (!_uiEnabled)
                return;

            Color originalColor = GUI.color;

            try
            {
                // ====== 맵 입장 배너 ======
                if (!string.IsNullOrEmpty(_enterAreaTitle) &&
                    Time.time <= _enterAreaShowEndTime)
                {
                    if (_enterAreaBannerStyle == null)
                    {
                        _enterAreaBannerStyle = new GUIStyle(GUI.skin.label);
                        _enterAreaBannerStyle.alignment = TextAnchor.MiddleCenter;
                        _enterAreaBannerStyle.fontSize = 32;
                        _enterAreaBannerStyle.fontStyle = FontStyle.Bold;
                        _enterAreaBannerStyle.normal.textColor = Color.white;
                    }

                    if (_enterAreaBannerSubStyle == null)
                    {
                        _enterAreaBannerSubStyle = new GUIStyle(GUI.skin.label);
                        _enterAreaBannerSubStyle.alignment = TextAnchor.MiddleCenter;
                        _enterAreaBannerSubStyle.fontSize = 18;
                        _enterAreaBannerSubStyle.normal.textColor =
                            new Color(1f, 1f, 1f, 0.85f);
                    }

                    // ====== 맵 입장 배너 ======
                    if (!string.IsNullOrEmpty(_enterAreaTitle) &&
                        Time.time <= _enterAreaShowEndTime)
                    {
                        if (_enterAreaBannerStyle == null)
                        {
                            _enterAreaBannerStyle = new GUIStyle(GUI.skin.label);
                            _enterAreaBannerStyle.alignment = TextAnchor.MiddleCenter;
                            _enterAreaBannerStyle.fontSize = 32;
                            _enterAreaBannerStyle.fontStyle = FontStyle.Bold;
                            _enterAreaBannerStyle.normal.textColor = Color.white;
                        }

                        if (_enterAreaBannerSubStyle == null)
                        {
                            _enterAreaBannerSubStyle = new GUIStyle(GUI.skin.label);
                            _enterAreaBannerSubStyle.alignment = TextAnchor.MiddleCenter;
                            _enterAreaBannerSubStyle.fontSize = 18;
                            _enterAreaBannerSubStyle.normal.textColor =
                                new Color(1f, 1f, 1f, 0.85f);
                        }

                        float bannerHeight = 80f;
                        float y = Screen.height * 0.22f;

                        Rect bgRect = new Rect(
                            0f,
                            y,
                            Screen.width,
                            bannerHeight
                        );

                        GUI.color = new Color(0f, 0f, 0f, 0.7f);
                        GUI.DrawTexture(bgRect, Texture2D.whiteTexture);

                        // ── 여기부터 언어별 "지금 진입 중" ──
                        GUI.color = new Color(1f, 1f, 1f, 0.9f);
                        Rect subRect = new Rect(
                            0f,
                            y + 4f,
                            Screen.width,
                            24f
                        );

                        string enteringText;
                        switch (GetGameLanguage())
                        {
                            case GameLanguage.Japanese:
                                enteringText = "エリア進入中";
                                break;
                            case GameLanguage.English:
                                enteringText = "Entering Area";
                                break;
                            default:
                                enteringText = "지금 진입 중";
                                break;
                        }

                        GUI.Label(subRect, enteringText, _enterAreaBannerSubStyle);

                        // 맵 이름 (아랫줄, 크게)
                        GUI.color = Color.white;
                        Rect titleRect = new Rect(
                            0f,
                            y + 26f,
                            Screen.width,
                            bannerHeight - 26f
                        );
                        GUI.Label(titleRect, _enterAreaTitle, _enterAreaBannerStyle);
                    }

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
                        float barHeight = 32f;

                        float bottomMargin = 230f;

                        float baseX = (Screen.width - barWidth) * 0.5f;
                        float baseY = Screen.height - bottomMargin - barHeight;

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

                            if (maxHp < _bossMinMaxHp)
                            {
                                continue;
                            }

                            float dist = Vector3.Distance(_player.transform.position,
                                boss.transform.position);
                            if (dist > _maxBossDisplayDistance)
                            {
                                continue;
                            }

                            float ratio = Mathf.Clamp01(curHp / maxHp);

                            float x = baseX;
                            float y = baseY - drawnCount * verticalSpacing;

                            GUI.color = new Color(0.15f, 0f, 0f, 0.8f);
                            GUI.DrawTexture(new Rect(x, y, barWidth, barHeight), _hpTex);

                            GUI.color = new Color(0.9f, 0.1f, 0.1f, 0.95f);
                            GUI.DrawTexture(
                                new Rect(x + 2f, y + 2f, (barWidth - 4f) * ratio, barHeight - 4f),
                                _hpTex
                            );

                            GUI.color = Color.white;

                            string bossName = SafeGetName(boss);

                            Rect nameRect = new Rect(
                                x,
                                y - 29f,
                                barWidth,
                                30f
                            );

                            Rect hpRect = new Rect(
                                x + 2f,
                                y,
                                barWidth - 4f,
                                barHeight
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

                        GUI.color = new Color(0.8f, 1f, 0.9f, t);
                        GUI.Label(mainRect, "DUCK HUNTED", _duckHuntedStyle);

                        if (!string.IsNullOrEmpty(_lastKilledBossName))
                        {
                            GUI.color = new Color(1f, 1f, 1f, t);
                            Rect subRect2 = new Rect(
                                0f,
                                mainRect.y + mainSize,
                                Screen.width,
                                subSize + 10f
                            );
                            GUI.Label(subRect2, _lastKilledBossName, _duckHuntedSubStyle);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[BossHealthHUD] OnGUI 예외: " + ex);
            }
            finally
            {
                GUI.color = originalColor;
            }
        }

        private static bool IsBossName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();

            // 1) 화이트리스트 이름과 완전 일치
            for (int i = 0; i < _bossNameExact.Length; i++)
            {
                string exact = _bossNameExact[i];
                if (!string.IsNullOrEmpty(exact) &&
                    lower == exact.ToLowerInvariant())
                {
                    return true;
                }
            }

            // 2) 키워드 포함
            for (int i = 0; i < _bossNameKeywords.Length; i++)
            {
                string kw = _bossNameKeywords[i];
                if (!string.IsNullOrEmpty(kw) &&
                    lower.Contains(kw.ToLowerInvariant()))
                {
                    return true;
                }
            }

            return false;
        }

        private static string SafeGetName(CharacterMainControl ch)
        {
            if (ch == null)
                return string.Empty;

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
