using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;

// 音频类型枚举
public enum AudioType
{
    Master,    // 总音量
    Music,     // 背景音乐
    SoundEffect // 音效
}

// 音频管理单例类，支持多音效并发（对象池实现）、多背景音乐
public class AudioManager : MonoBehaviour
{
    // 单例实例
    public static AudioManager Instance;
    
    [Header("基础音频源组件")]
    [SerializeField] private GameObject settingCanvus;      // 设置面板
    [SerializeField] private AudioSource musicSource;      // 背景音乐源（单独管理，不参与对象池）

    [Header("音效对象池配置")]
    [Tooltip("音效池父对象（用于层级管理，避免场景混乱）")]
    [SerializeField] private Transform soundPoolParent;
    [Tooltip("初始创建的音频源数量（建议5-10，根据音效密度调整）")]
    [SerializeField] private int initialPoolSize = 8;
    [Tooltip("池的最大容量（防止无限创建导致内存溢出，建议15-20）")]
    [SerializeField] private int maxPoolSize = 15;

    [Header("音频资源列表")]
    [Tooltip("存储所有背景音乐")]
    [SerializeField] private List<AudioClip> backgroundMusics = new List<AudioClip>();
    [Tooltip("存储所有音效（短音效+持续音效）")]
    [SerializeField] private List<AudioClip> soundEffects = new List<AudioClip>();

    [Header("UI元素 - 总音量")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Toggle masterVolumeToggle;

    [Header("UI元素 - 背景音乐")]
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Toggle musicVolumeToggle;

    [Header("UI元素 - 音效")]
    [SerializeField] private Slider soundEffectVolumeSlider;
    [SerializeField] private Toggle soundEffectVolumeToggle;

    [Header("默认设置")]
    [SerializeField] private float defaultMasterVolume = 1.0f;  
    [SerializeField] private float defaultMusicVolume = 0.8f;  
    [SerializeField] private float defaultSoundEffectVolume = 1.0f; 
    [SerializeField] private bool defaultMasterEnabled = true;
    [SerializeField] private bool defaultMusicEnabled = true;
    [SerializeField] private bool defaultSoundEffectEnabled = true;
    [SerializeField] private int defaultMusicIndex = 0; // 默认播放的背景音乐索引

    // ---------------- 私有核心变量 ----------------
    // 当前播放的背景音乐索引
    private int _currentMusicIndex = -1;

    // 音量与开关状态
    private float _masterVolume;
    private float _musicVolume;
    private float _soundEffectVolume;
    private bool _isMasterEnabled;
    private bool _isMusicEnabled;
    private bool _isSoundEffectEnabled;

    // 音效对象池核心（复用AudioSource，避免频繁创建销毁）
    private List<AudioSource> _soundPool = new List<AudioSource>(); // 闲置音频源池
    private List<AudioSource> _activeSoundSources = new List<AudioSource>(); // 正在使用的音频源
    private Dictionary<AudioSource, bool> _isLoopSource = new Dictionary<AudioSource, bool>(); // 标记持续音效

    // 游戏暂停状态标记
    private bool _isGamePaused = false;
    // 暂停前的音乐播放状态（用于恢复时还原）
    private bool _wasMusicPlayingBeforePause = false;


    private void Awake()
    {
        // 单例模式实现
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeAudioSettings();
            InitializeSoundPool();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        BindUIEvents();
        settingCanvus?.SetActive(false);
        _currentMusicIndex = -1;
        PlayMusic(0);
    }

    #region 基础初始化与UI绑定
    private void InitializeAudioSettings()
    {
        _masterVolume = PlayerPrefs.GetFloat("MasterVolume", defaultMasterVolume);
        _musicVolume = PlayerPrefs.GetFloat("MusicVolume", defaultMusicVolume);
        _soundEffectVolume = PlayerPrefs.GetFloat("SoundEffectVolume", defaultSoundEffectVolume);

        _isMasterEnabled = PlayerPrefs.GetInt("IsMasterEnabled", defaultMasterEnabled ? 1 : 0) == 1;
        _isMusicEnabled = PlayerPrefs.GetInt("IsMusicEnabled", defaultMusicEnabled ? 1 : 0) == 1;
        _isSoundEffectEnabled = PlayerPrefs.GetInt("IsSoundEffectEnabled", defaultSoundEffectEnabled ? 1 : 0) == 1;
        _currentMusicIndex = PlayerPrefs.GetInt("CurrentMusicIndex", defaultMusicIndex);

        UpdateUISettings();
        ApplyAudioSettings();
    }

    private void BindUIEvents()
    {
        // 总音量滑块
        masterVolumeSlider?.onValueChanged.AddListener(value => 
        {
            _masterVolume = value;
            ApplyAudioSettings();
        });

        // 总音量开关
        masterVolumeToggle?.onValueChanged.AddListener(value => 
        {
            _isMasterEnabled = value;
            ApplyAudioSettings();
        });

        // 背景音乐滑块
        musicVolumeSlider?.onValueChanged.AddListener(value => 
        {
            _musicVolume = value;
            ApplyAudioSettings();
        });

        // 背景音乐开关
        musicVolumeToggle?.onValueChanged.AddListener(value => 
        {
            _isMusicEnabled = value;
            ApplyAudioSettings();
            if (!_isGamePaused)
            {
                if (value && musicSource.clip != null && !musicSource.isPlaying)
                    musicSource.Play();
                else if (!value && musicSource.isPlaying)
                    musicSource.Stop();
            }
        });

        // 音效滑块
        soundEffectVolumeSlider?.onValueChanged.AddListener(value => 
        {
            _soundEffectVolume = value;
            ApplyAudioSettings();
        });

        // 音效开关
        soundEffectVolumeToggle?.onValueChanged.AddListener(value => 
        {
            _isSoundEffectEnabled = value;
            ApplyAudioSettings();
        });
    }

    private void UpdateUISettings()
    {
        if (masterVolumeSlider != null) masterVolumeSlider.value = _masterVolume;
        if (masterVolumeToggle != null) masterVolumeToggle.isOn = _isMasterEnabled;

        if (musicVolumeSlider != null) musicVolumeSlider.value = _musicVolume;
        if (musicVolumeToggle != null) musicVolumeToggle.isOn = _isMusicEnabled;

        if (soundEffectVolumeSlider != null) soundEffectVolumeSlider.value = _soundEffectVolume;
        if (soundEffectVolumeToggle != null) soundEffectVolumeToggle.isOn = _isSoundEffectEnabled;
    }
    #endregion

    #region 音效对象池核心功能
    private void InitializeSoundPool()
    {
        if (soundPoolParent == null)
        {
            GameObject poolObj = new GameObject("SoundPool_Parent");
            poolObj.transform.parent = transform;
            soundPoolParent = poolObj.transform;
        }

        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateNewSoundSource();
        }
    }

    private AudioSource CreateNewSoundSource()
    {
        if (_soundPool.Count + _activeSoundSources.Count >= maxPoolSize)
        {
            Debug.LogWarning($"音效池已达最大容量（{maxPoolSize}），无法创建新音频源！");
            return null;
        }

        GameObject sourceObj = new GameObject($"SoundSource_{_soundPool.Count + _activeSoundSources.Count + 1}");
        sourceObj.transform.parent = soundPoolParent;
        AudioSource source = sourceObj.AddComponent<AudioSource>();

        source.mute = false;
        source.bypassEffects = false;
        source.bypassListenerEffects = false;
        source.bypassReverbZones = false;
        source.playOnAwake = false;
        source.loop = false;
        source.priority = 128;
        source.spatialBlend = 0;

        _soundPool.Add(source);
        return source;
    }

    private AudioSource GetAvailableSource()
    {
        // 即使游戏暂停也允许获取新音频源（用于播放点击等短音效）
        foreach (var source in _soundPool)
        {
            if (!source.isPlaying)
            {
                _soundPool.Remove(source);
                _activeSoundSources.Add(source);
                return source;
            }
        }

        AudioSource newSource = CreateNewSoundSource();
        if (newSource != null)
        {
            _soundPool.Remove(newSource);
            _activeSoundSources.Add(newSource);
        }
        return newSource;
    }

    private void RecycleSource(AudioSource source)
    {
        if (_isLoopSource.ContainsKey(source) && _isLoopSource[source])
        {
            Debug.LogWarning("持续音效需先调用 StopLoopSound 手动停止后再回收！");
            return;
        }

        if (_activeSoundSources.Contains(source))
        {
            _activeSoundSources.Remove(source);
            _soundPool.Add(source);
            source.clip = null;
            source.loop = false;
            if (_isLoopSource.ContainsKey(source))
                _isLoopSource.Remove(source);
        }
    }
    #endregion

    #region 音频播放控制
    private void ApplyAudioSettings()
    {
        // 游戏暂停时只影响背景音乐和持续音效的音量
        float finalMusicVolume = _isGamePaused ? 0 : (_isMasterEnabled && _isMusicEnabled ? _masterVolume * _musicVolume : 0);
        // 短音效在暂停时仍可播放，所以不强制设为0
        float finalSoundVolume = _isMasterEnabled && _isSoundEffectEnabled ? _masterVolume * _soundEffectVolume : 0;

        if (musicSource != null)
        {
            musicSource.volume = finalMusicVolume;
            if (!_isGamePaused && finalMusicVolume > 0 && !musicSource.isPlaying && musicSource.clip != null)
                musicSource.Play();
        }

        foreach (var source in _activeSoundSources)
        {
            if (source == null) continue;

            // 对持续音效和普通音效区别处理
            if (_isLoopSource.ContainsKey(source) && _isLoopSource[source])
            {
                // 持续音效在暂停时音量设为0
                source.volume = _isGamePaused ? 0 : finalSoundVolume;
            }
            else
            {
                // 普通音效不受暂停影响
                source.volume = finalSoundVolume;
            }

            // 仅在音效开关关闭时停止音效
            if (finalSoundVolume <= 0 && source.isPlaying)
            {
                source.Stop();
                if (_isLoopSource.ContainsKey(source))
                    _isLoopSource[source] = false;
                RecycleSource(source);
            }
        }
    }

    // ---------------- 短音效播放 ----------------
    public void PlaySoundEffect(string effectName)
    {
        if (!_isMasterEnabled || !_isSoundEffectEnabled) return;

        int index = soundEffects.FindIndex(clip => clip != null && clip.name == effectName);
        if (index == -1)
        {
            Debug.LogWarning($"未找到名为「{effectName}」的音效，请检查soundEffects列表！");
            return;
        }

        AudioClip targetClip = soundEffects[index];
        AudioSource source = GetAvailableSource();

        if (source != null && targetClip != null)
        {
            source.clip = targetClip;
            // ✅ 立即根据当前设置计算音量
            float finalSoundVolume = _isMasterEnabled && _isSoundEffectEnabled ? _masterVolume * _soundEffectVolume : 0;
            source.volume = finalSoundVolume;

            source.Play();
            StartCoroutine(WaitForSoundEnd(source, targetClip.length));
        }
    }


    public void PlaySoundEffect(int index)
    {
        if (index < 0 || index >= soundEffects.Count || soundEffects[index] == null)
        {
            Debug.LogWarning($"音效索引「{index}」无效，请检查soundEffects列表！");
            return;
        }
        PlaySoundEffect(soundEffects[index].name);
    }

    private IEnumerator WaitForSoundEnd(AudioSource source, float duration)
    {
        yield return new WaitForSeconds(duration);
        RecycleSource(source);
    }

    // ---------------- 持续音效播放 ----------------
    public AudioSource StartLoopSound(string effectName)
    {
        // 游戏暂停时不允许播放新的持续音效
        if (_isGamePaused || !_isMasterEnabled || !_isSoundEffectEnabled) return null;

        int index = soundEffects.FindIndex(clip => clip != null && clip.name == effectName);
        if (index == -1)
        {
            Debug.LogWarning($"未找到名为「{effectName}」的音效，请检查soundEffects列表！");
            return null;
        }

        AudioClip targetClip = soundEffects[index];
        AudioSource source = GetAvailableSource();

        if (source != null && targetClip != null)
        {
            source.clip = targetClip;
            source.loop = true;
            source.volume = _masterVolume * _soundEffectVolume;
            source.Play();
            
            if (!_isLoopSource.ContainsKey(source))
                _isLoopSource.Add(source, true);
            else
                _isLoopSource[source] = true;
        }

        return source;
    }

    public AudioSource StartLoopSound(int index)
    {
        if (index < 0 || index >= soundEffects.Count || soundEffects[index] == null)
        {
            Debug.LogWarning($"音效索引「{index}」无效，请检查soundEffects列表！");
            return null;
        }
        return StartLoopSound(soundEffects[index].name);
    }

    public void StopLoopSound(AudioSource loopSource)
    {
        if (loopSource == null || !_activeSoundSources.Contains(loopSource)) return;

        loopSource.Stop();
        loopSource.clip = null;
        loopSource.loop = false;

        if (_isLoopSource.ContainsKey(loopSource))
            _isLoopSource[loopSource] = false;
        RecycleSource(loopSource);
    }

    public void PauseLoopSound(AudioSource loopSource)
    {
        if (loopSource != null && loopSource.isPlaying)
            loopSource.Pause();
    }

    public void ResumeLoopSound(AudioSource loopSource)
    {
        if (_isGamePaused) return;
        
        if (loopSource != null && !loopSource.isPlaying)
            loopSource.UnPause();
    }

    // ---------------- 背景音乐控制 ----------------
    public void PlayMusic(int index)
    {
        if (index < 0 || index >= backgroundMusics.Count || backgroundMusics[index] == null)
        {
            Debug.LogWarning($"背景音乐索引「{index}」无效，请检查backgroundMusics列表！");
            return;
        }

        if (!_isGamePaused && index == _currentMusicIndex && musicSource.isPlaying)
        {
            return;
        }

        _currentMusicIndex = index;
        musicSource.clip = backgroundMusics[index];
        musicSource.loop = true;

        if (!_isGamePaused && _isMusicEnabled && _isMasterEnabled)
        {
            musicSource.Play();
        }
    }

    public void PlayMusic(string musicName)
    {
        int index = backgroundMusics.FindIndex(clip => clip != null && clip.name == musicName);
        if (index != -1)
            PlayMusic(index);
        else
            Debug.LogWarning($"未找到名为「{musicName}」的背景音乐，请检查backgroundMusics列表！");
    }

    public void PlayNextMusic()
    {
        if (_isGamePaused) return;
        
        if (backgroundMusics.Count == 0) return;
        int nextIndex = (_currentMusicIndex + 1) % backgroundMusics.Count;
        PlayMusic(nextIndex);
    }

    public void PlayPreviousMusic()
    {
        if (_isGamePaused) return;
        
        if (backgroundMusics.Count == 0) return;
        int prevIndex = (_currentMusicIndex - 1 + backgroundMusics.Count) % backgroundMusics.Count;
        PlayMusic(prevIndex);
    }

    // 暂停背景音乐
    public void PauseMusic()
    {
        if (musicSource != null && musicSource.isPlaying)
        {
            _wasMusicPlayingBeforePause = true;
            musicSource.Pause();
        }
        else
        {
            _wasMusicPlayingBeforePause = false;
        }
    }

    // 恢复背景音乐
    public void ResumeMusic()
    {
        if (_isGamePaused || !_isMusicEnabled || !_isMasterEnabled) return;
        
        if (musicSource != null && _wasMusicPlayingBeforePause && !musicSource.isPlaying)
        {
            musicSource.UnPause();
        }
    }
    #endregion

    #region 游戏暂停/恢复统一接口
    /// <summary>
    /// 游戏暂停时调用：暂停背景音乐和持续音效，但允许短音效播放
    /// </summary>
    public void OnGamePause()
    {
        _isGamePaused = true;
        PauseMusic(); // 暂停背景音乐
        PauseAllActiveLoopSounds(); // 暂停所有活跃的持续音效
        ApplyAudioSettings(); // 应用暂停状态的音量设置
    }

    /// <summary>
    /// 游戏恢复时调用：恢复背景音乐和持续音效
    /// </summary>
    public void OnGameResume()
    {
        _isGamePaused = false;
        ResumeMusic(); // 恢复背景音乐
        ResumeAllActiveLoopSounds(); // 恢复所有暂停的持续音效
        ApplyAudioSettings(); // 恢复正常音量设置
    }

    // 暂停所有活跃的持续音效
    public void PauseAllActiveLoopSounds()
    {
        foreach (var source in _activeSoundSources)
        {
            if (source != null && _isLoopSource.ContainsKey(source) && _isLoopSource[source])
            {
                PauseLoopSound(source);
            }
        }
    }

    // 恢复所有暂停的持续音效
    public void ResumeAllActiveLoopSounds()
    {
        foreach (var source in _activeSoundSources)
        {
            if (source != null && _isLoopSource.ContainsKey(source) && _isLoopSource[source])
            {
                ResumeLoopSound(source);
            }
        }
    }
    #endregion

    #region 外部接口与存档
    public void SetVolume(AudioType type, float value)
    {
        value = Mathf.Clamp01(value);

        switch (type)
        {
            case AudioType.Master:
                _masterVolume = value;
                if (masterVolumeSlider != null) masterVolumeSlider.value = value;
                break;
            case AudioType.Music:
                _musicVolume = value;
                if (musicVolumeSlider != null) musicVolumeSlider.value = value;
                break;
            case AudioType.SoundEffect:
                _soundEffectVolume = value;
                if (soundEffectVolumeSlider != null) soundEffectVolumeSlider.value = value;
                break;
        }

        ApplyAudioSettings();
    }

    public void SetEnabled(AudioType type, bool enabled)
    {
        switch (type)
        {
            case AudioType.Master:
                _isMasterEnabled = enabled;
                if (masterVolumeToggle != null) masterVolumeToggle.isOn = enabled;
                break;
            case AudioType.Music:
                _isMusicEnabled = enabled;
                if (musicVolumeToggle != null) musicVolumeToggle.isOn = enabled;
                if (!_isGamePaused)
                {
                    if (enabled && musicSource.clip != null && !musicSource.isPlaying)
                        musicSource.Play();
                    else if (!enabled && musicSource.isPlaying)
                        musicSource.Stop();
                }
                break;
            case AudioType.SoundEffect:
                _isSoundEffectEnabled = enabled;
                if (soundEffectVolumeToggle != null) soundEffectVolumeToggle.isOn = enabled;
                break;
        }

        ApplyAudioSettings();
    }

    public void AddBackgroundMusic(AudioClip clip)
    {
        if (clip != null && !backgroundMusics.Contains(clip))
            backgroundMusics.Add(clip);
    }

    public void AddSoundEffect(AudioClip clip)
    {
        if (clip != null && !soundEffects.Contains(clip))
            soundEffects.Add(clip);
    }

    public void SaveSettings()
    {
        PlayerPrefs.SetFloat("MasterVolume", _masterVolume);
        PlayerPrefs.SetFloat("MusicVolume", _musicVolume);
        PlayerPrefs.SetFloat("SoundEffectVolume", _soundEffectVolume);
        
        PlayerPrefs.SetInt("IsMasterEnabled", _isMasterEnabled ? 1 : 0);
        PlayerPrefs.SetInt("IsMusicEnabled", _isMusicEnabled ? 1 : 0);
        PlayerPrefs.SetInt("IsSoundEffectEnabled", _isSoundEffectEnabled ? 1 : 0);
        PlayerPrefs.SetInt("CurrentMusicIndex", _currentMusicIndex);
        
        PlayerPrefs.Save();
    }

    public void LoadSettings()
    {
        _masterVolume = PlayerPrefs.GetFloat("MasterVolume", defaultMasterVolume);
        _musicVolume = PlayerPrefs.GetFloat("MusicVolume", defaultMusicVolume);
        _soundEffectVolume = PlayerPrefs.GetFloat("SoundEffectVolume", defaultSoundEffectVolume);
        
        _isMasterEnabled = PlayerPrefs.GetInt("IsMasterEnabled", defaultMasterEnabled ? 1 : 0) == 1;
        _isMusicEnabled = PlayerPrefs.GetInt("IsMusicEnabled", defaultMusicEnabled ? 1 : 0) == 1;
        _isSoundEffectEnabled = PlayerPrefs.GetInt("IsSoundEffectEnabled", defaultSoundEffectEnabled ? 1 : 0) == 1;
        _currentMusicIndex = PlayerPrefs.GetInt("CurrentMusicIndex", defaultMusicIndex);
        
        UpdateUISettings();
        ApplyAudioSettings();
    }

    public void ShowSettingCanvus()
    {
        if (settingCanvus != null)
            settingCanvus.SetActive(true);
    }

    public GameObject GetSettingCanvas() => settingCanvus;
    #endregion

    #region 生命周期与清理
    private void OnApplicationQuit()
    {
        SaveSettings();
    }

    private void OnDestroy()
    {
        foreach (var source in _activeSoundSources)
        {
            if (source != null)
            {
                source.Stop();
                Destroy(source.gameObject);
            }
        }

        foreach (var source in _soundPool)
        {
            if (source != null)
                Destroy(source.gameObject);
        }

        _activeSoundSources.Clear();
        _soundPool.Clear();
        _isLoopSource.Clear();
    }
    #endregion
}
